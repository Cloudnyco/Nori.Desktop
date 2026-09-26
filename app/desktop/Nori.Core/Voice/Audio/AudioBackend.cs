namespace Nori.Core.Voice.Audio;

/// <summary>
/// 音频后端选择。
///
/// 三平台都直接推声卡。历史上的 <c>audio_backend=webview</c> 不再切换实现，
/// 写坏的配置也同样忽略，避免一次坏值把声音关掉。
/// </summary>
public static class AudioBackend
{
	/// <summary>按平台使用原生设备。保留给已有配置读取。</summary>
	public const string Auto = "auto";

	/// <summary>直接推声卡。</summary>
	public const string Native = "native";

	/// <summary>
	/// 这一轮是否使用原生设备。
	///
	/// <paramref name="configured"/> 不再参与分流：旧的 webview 值和无法识别的值都交给
	/// <paramref name="nativeAvailable"/>。当前三个桌面平台都有原生实现。
	/// </summary>
	public static bool PrefersNative(string? configured, bool nativeAvailable)
	{
		_ = configured;
		return nativeAvailable;
	}
}
