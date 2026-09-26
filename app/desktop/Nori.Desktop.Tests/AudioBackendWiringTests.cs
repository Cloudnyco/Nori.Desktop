using Nori.Core.Configuration;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Tests;

/// <summary>
/// 运行时始终装配原生音频。
///
/// 旧配置不能把播放切回页面。这类回退没有报错，只是声音走了另一条链路，所以钉在这里。
/// </summary>
public partial class BridgeCommandsTests
{
	[Fact]
	public async Task 默认使用原生音频后端()
	{
		await using AppRuntime runtime = new(_services);

		Assert.Equal("native", runtime.AudioBackendName);
	}

	/// <summary>旧的 webview 配置不再切回页面播放。</summary>
	[Theory]
	[InlineData("webview")]
	[InlineData("")]
	[InlineData("WEBAUDIO")]
	public async Task 配置不再切换音频后端(string configured)
	{
		_config.Set(ConfigStore.KeyAudioBackend, new ConfigValue.Text(configured));

		await using AppRuntime runtime = new(_services);

		Assert.Equal("native", runtime.AudioBackendName);
	}
}
