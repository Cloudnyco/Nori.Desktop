using Nori.Core.Voice.Audio;

namespace Nori.Desktop.Audio.Mac;

/// <summary>
/// AudioQueue 的可替换边界。测试注入替身，真机走 <see cref="CoreAudioQueue"/>。
/// </summary>
internal interface ICoreAudioQueue : IDisposable
{
	/// <summary>按设备实际格式打开输出，并在回调里向 <paramref name="render"/> 要样本。</summary>
	AudioFormat OpenOutput(int sampleRate, int channels, Func<Span<float>, int> render);

	/// <summary>按设备实际格式打开输入。回调拿到的是设备格式的交错浮点。</summary>
	AudioFormat OpenInput(Action<ReadOnlySpan<float>> capture);

	/// <summary>输出音量 0..1。</summary>
	void SetVolume(double volume);

	void Start();

	/// <summary>等已经排队的缓冲播完再停。</summary>
	void Finish();

	/// <summary>立刻停，丢掉还没播的缓冲。</summary>
	void StopImmediate();

	/// <summary>
	/// 释放已创建的原生队列和固定句柄，但不把对象标成已释放。
	/// 打开失败后可以再次打开。
	/// </summary>
	void Abort();
}
