namespace Nori.Core.Voice.Audio;

/// <summary>
/// 直接推给声卡的播放后端。
///
/// 与 WebView 那份的区别只有一条链路：
/// <code>
/// WebView：字节 → 一次性媒体端点 → 本地 HTTP → WebView fetch → WebAudio → 设备
///          电平在前端算，每约 60ms 经桥回传一次
/// 原生：  字节 → 解码 → 设备
///          电平在送进设备的**那个缓冲**上算，和听到的声音严格对齐
/// </code>
///
/// 设备那一层是 <see cref="IAudioDevice"/>，只有它需要原生实现；这里的编排
/// （解码、切块、发电平、停止、收尾）全是普通托管代码，用假设备就能测完。
/// </summary>
public sealed class NativeAudioPlayback(Func<IAudioDevice> openDevice, Func<ReadOnlyMemory<byte>, string, PcmAudio> decode)
	: IAudioPlayback
{
	private readonly Lock _gate = new();
	private CancellationTokenSource? _current;
	private IAudioDevice? _device;
	private bool _playing;
	private double _volume = 1.0;

	/// <inheritdoc />
	public bool IsPlaying => Volatile.Read(ref _playing);

	/// <inheritdoc />
	public event Action<bool>? PlayingChanged;

	/// <inheritdoc />
	public event Action<double>? VolumeSampled;

	/// <summary>输出音量 0..1，随下一段播放生效。</summary>
	public void SetDeviceVolume(double volume)
	{
		double clamped = Math.Clamp(volume, 0, 1);
		lock (_gate)
		{
			_volume = clamped;
			if (_device is { } device) device.Volume = clamped;
		}
	}

	/// <inheritdoc />
	public async Task PlayAsync(EncodedAudio audio, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(audio);
		PcmAudio pcm = decode(audio.Bytes, audio.Mime);
		if (pcm.Samples.Length == 0) return;

		// 新的一段顶掉旧的。语音是串行的：上一句还没播完就来了下一句，说明用户
		// 已经不想听上一句了。
		Stop();

		CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		IAudioDevice device = openDevice();
		lock (_gate)
		{
			_current = linked;
			_device = device;
			device.Volume = _volume;
		}
		SetPlaying(true);

		try
		{
			await Task.Run(() => Pump(device, pcm, linked.Token), CancellationToken.None).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// 被顶掉或被调用方取消，不是失败。
		}
		finally
		{
			lock (_gate)
			{
				if (ReferenceEquals(_current, linked))
				{
					_current = null;
					_device = null;
				}
			}
			device.Dispose();
			linked.Dispose();
			SetPlaying(false);
			// 收尾必须把嘴合上。少这一条，播放被打断时口型会停在张开的那一帧。
			VolumeSampled?.Invoke(0);
		}
	}

	/// <summary>
	/// 一窗一窗地推。
	///
	/// 窗长取 <see cref="PcmLevel.WindowMilliseconds"/>：它同时是「写给设备的块大小」
	/// 和「发电平的节奏」。两者用同一个值，电平就天然对齐到正在播的那一段，
	/// 不需要另外算时间。
	/// </summary>
	private void Pump(IAudioDevice device, PcmAudio pcm, CancellationToken cancellationToken)
	{
		AudioFormat actual = device.Open(pcm.SampleRate, pcm.Channels);
		float[] samples = actual.SampleRate == pcm.SampleRate && actual.Channels == pcm.Channels
			? pcm.Samples
			: Resample(pcm, actual);

		int window = PcmLevel.WindowSamples(actual.SampleRate, actual.Channels);
		double smoothed = 0;

		for (int at = 0; at < samples.Length && !cancellationToken.IsCancellationRequested;)
		{
			int take = Math.Min(window, samples.Length - at);
			ReadOnlySpan<float> chunk = samples.AsSpan(at, take);

			// 先发电平再写：写会阻塞到设备吃得下，而这一窗**就是**马上要响的那一窗。
			smoothed = PcmLevel.Smooth(smoothed, PcmLevel.Rms(chunk));
			VolumeSampled?.Invoke(smoothed);

			int written = device.Write(chunk, cancellationToken);
			if (written <= 0) break;          // 被 Stop 打断
			at += written;
		}

		if (!cancellationToken.IsCancellationRequested) device.Drain(cancellationToken);
	}

	/// <summary>
	/// 最近邻重采样 + 声道铺开。
	///
	/// 够用的理由：这里处理的是语音，不是音乐；而多数声卡固定跑 48000，
	/// 语音合成常给 22050 或 24000，不转就变调。最近邻在语音上的可闻损失很小，
	/// 换来的是零依赖。真需要更好的质量时，换成线性插值也只动这一个方法。
	/// </summary>
	private static float[] Resample(PcmAudio source, AudioFormat target)
	{
		int sourceFrames = source.FrameCount;
		if (sourceFrames == 0) return [];

		long targetFrames = (long) sourceFrames * target.SampleRate / Math.Max(1, source.SampleRate);
		float[] output = new float[targetFrames * target.Channels];

		for (long frame = 0; frame < targetFrames; frame++)
		{
			long sourceFrame = frame * source.SampleRate / Math.Max(1, target.SampleRate);
			if (sourceFrame >= sourceFrames) sourceFrame = sourceFrames - 1;

			for (int channel = 0; channel < target.Channels; channel++)
			{
				// 源声道少于目标就重复最后一个（单声道铺成立体声）；多于就丢掉多的。
				int sourceChannel = Math.Min(channel, source.Channels - 1);
				output[frame * target.Channels + channel] =
					source.Samples[sourceFrame * source.Channels + sourceChannel];
			}
		}
		return output;
	}

	/// <inheritdoc />
	public void Stop()
	{
		CancellationTokenSource? cancelling;
		IAudioDevice? device;
		lock (_gate)
		{
			cancelling = _current;
			device = _device;
		}
		// 先让设备把阻塞中的 Write 放出来，再取消 —— 反过来的话取消信号会卡在
		// 一个正在等缓冲的 Write 后面。
		device?.Stop();
		try { cancelling?.Cancel(); } catch (ObjectDisposedException) { /* 已经收尾了 */ }
	}

	private void SetPlaying(bool playing)
	{
		if (Volatile.Read(ref _playing) == playing) return;
		Volatile.Write(ref _playing, playing);
		PlayingChanged?.Invoke(playing);
	}

	public void Dispose()
	{
		Stop();
		lock (_gate)
		{
			_device?.Dispose();
			_device = null;
		}
	}
}
