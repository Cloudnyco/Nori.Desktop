using Nori.Core.Voice.Audio;

namespace Nori.Core.Tests;

/// <summary>
/// 音频后端不再按配置分流。
///
/// 三平台都走原生设备。旧的 webview 值和写坏的值都不能把声音切走。
/// </summary>
public sealed class AudioBackendTests
{
	[Theory]
	[InlineData(AudioBackend.Auto)]
	[InlineData(AudioBackend.Native)]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("WEBAUDIO")]
	[InlineData("原生")]
	[InlineData("WebView")]
	[InlineData("  webview  ")]
	[InlineData("WEBVIEW")]
	public void 任何配置都交给平台是否有原生实现(string? configured)
	{
		Assert.True(AudioBackend.PrefersNative(configured, nativeAvailable: true));
		Assert.False(AudioBackend.PrefersNative(configured, nativeAvailable: false));
	}
}
