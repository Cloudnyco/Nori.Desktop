using Nori.Core.Voice.Audio;
using Nori.Desktop.Audio.Mac;

namespace Nori.Desktop.Tests;

/// <summary>用队列替身校验格式回传、音量和停止打断，不打开真实 AudioQueue。</summary>
public sealed class CoreAudioDeviceTests
{
	[Fact]
	public void 输出返回设备实际格式并把音量交给队列()
	{
		FakeQueue queue = new(new AudioFormat(48_000, 2));
		using CoreAudioOutputDevice device = new(queue, bufferSamples: 8);
		device.Volume = 0.25;

		AudioFormat format = device.Open(16_000, 1);

		Assert.Equal(new AudioFormat(48_000, 2), format);
		Assert.Equal(0.25, queue.Volume);
		Assert.Equal(4, device.Write([0.1f, 0.2f, 0.3f, 0.4f], CancellationToken.None));
		float[] pulled = new float[4];
		Assert.Equal(4, queue.Render!(pulled));
		Assert.Equal(0.1f, pulled[0]);
		Assert.Equal(0.4f, pulled[3]);
	}

	[Fact]
	public async Task 输出缓冲满时停止会返回()
	{
		FakeQueue queue = new(new AudioFormat(48_000, 1));
		using CoreAudioOutputDevice device = new(queue, bufferSamples: 4);
		device.Open(48_000, 1);
		Task<int> pending = Task.Run(() => device.Write([1f, 2f, 3f, 4f, 5f, 6f], CancellationToken.None));
		await WaitUntilAsync(() => !pending.IsCompleted);
		device.Stop();
		int written = await pending.WaitAsync(TimeSpan.FromSeconds(2));
		Assert.True(written < 6);
		Assert.True(queue.Stopped);
	}

	[Fact]
	public void 输入把回调样本交给读取方()
	{
		FakeQueue queue = new(new AudioFormat(44_100, 2));
		using CoreAudioCaptureDevice device = new(queue, bufferSamples: 8);
		Assert.Equal(new AudioFormat(44_100, 2), device.Open());
		queue.Capture!([0.25f, -0.25f]);
		float[] got = new float[2];
		Assert.Equal(2, device.Read(got, CancellationToken.None));
		Assert.Equal(0.25f, got[0]);
		Assert.Equal(-0.25f, got[1]);
	}

	[Fact]
	public async Task 输入没有数据时停止会返回()
	{
		FakeQueue queue = new(new AudioFormat(48_000, 1));
		using CoreAudioCaptureDevice device = new(queue, bufferSamples: 4);
		device.Open();
		Task<int> pending = Task.Run(() => device.Read(new float[2], CancellationToken.None));
		await WaitUntilAsync(() => !pending.IsCompleted);
		device.Stop();
		Assert.Equal(0, await pending.WaitAsync(TimeSpan.FromSeconds(2)));
	}

	[Fact]
	public void 输出启动失败会中止队列并允许再次打开()
	{
		FakeQueue queue = new(new AudioFormat(48_000, 1)) { FailStart = true };
		using CoreAudioOutputDevice device = new(queue, bufferSamples: 8);

		Assert.Throws<AudioDeviceException>(() => device.Open(48_000, 1));
		Assert.True(queue.AbortCount >= 1);

		queue.FailStart = false;
		Assert.Equal(new AudioFormat(48_000, 1), device.Open(48_000, 1));
		Assert.True(queue.Started);
	}

	[Fact]
	public void 输入启动失败会中止队列并允许再次打开()
	{
		FakeQueue queue = new(new AudioFormat(48_000, 1)) { FailStart = true };
		using CoreAudioCaptureDevice device = new(queue, bufferSamples: 8);

		Assert.Throws<AudioDeviceException>(() => device.Open());
		Assert.True(queue.AbortCount >= 1);

		queue.FailStart = false;
		Assert.Equal(new AudioFormat(48_000, 1), device.Open());
		Assert.True(queue.Started);
	}

	[Fact]
	public void 非macOS缺少AudioToolbox时给出中文原因()
	{
		if (System.OperatingSystem.IsMacOS()) return;
		using CoreAudioOutputDevice device = new();
		AudioDeviceException error = Assert.Throws<AudioDeviceException>(() => device.Open(48_000, 1));
		Assert.Contains("AudioToolbox", error.Message, StringComparison.Ordinal);
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
		while (!condition())
			await Task.Delay(10, timeout.Token);
	}

	private sealed class FakeQueue(AudioFormat format) : ICoreAudioQueue
	{
		public Func<Span<float>, int>? Render { get; private set; }
		public Action<ReadOnlySpan<float>>? Capture { get; private set; }
		public double Volume { get; private set; } = 1;
		public bool Started { get; private set; }
		public bool Stopped { get; private set; }
		public int AbortCount { get; private set; }
		public bool FailStart { get; set; }

		public AudioFormat OpenOutput(int sampleRate, int channels, Func<Span<float>, int> render)
		{
			_ = sampleRate;
			_ = channels;
			Render = render;
			return format;
		}

		public AudioFormat OpenInput(Action<ReadOnlySpan<float>> capture)
		{
			Capture = capture;
			return format;
		}

		public void SetVolume(double volume) => Volume = volume;
		public void Start()
		{
			if (FailStart) throw new AudioDeviceException("启动音频队列失败：测试");
			Started = true;
		}
		public void Finish() => Stopped = true;
		public void StopImmediate() => Stopped = true;
		public void Abort() => AbortCount++;
		public void Dispose() { }
	}
}
