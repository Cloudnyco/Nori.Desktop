using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Nori.Core.Network;

namespace Nori.Core.Tests;

[SuppressMessage("Security", "S5332", Justification = "HTTP 地址用于验证网络地址策略边界，调用方另行执行 HTTPS 契约。")]
public class UrlAccessPolicyTests
{
	[Theory]
	[InlineData("https://example.com/page")]
	[InlineData("http://api.anysearch.com/v1/search")]
	[InlineData("https://1.1.1.1/dns")]
	[InlineData("https://8.8.8.8/dns")]
	[InlineData("http://[2001:4860:4860::8888]/dns")]
	public void 公网地址允许访问(string url) =>
		UrlAccessPolicy.EnsurePublicHttp(new Uri(url));

	[Theory]
	[InlineData("http://127.0.0.1:8080/api")]
	[InlineData("http://localhost/x")]
	[InlineData("http://app.localhost/x")]
	[InlineData("http://169.254.1.1/metadata")]
	[InlineData("http://169.254.169.254/latest/meta-data")]
	[InlineData("http://192.168.1.10/router")]
	[InlineData("http://10.0.0.1/")]
	[InlineData("http://172.16.0.1/")]
	[InlineData("http://100.64.0.1/")]
	[InlineData("http://192.0.2.1/")]
	[InlineData("http://198.18.0.1/")]
	[InlineData("http://224.0.0.1/")]
	[InlineData("http://[::1]/")]
	[InlineData("http://[fc00::1]/")]
	[InlineData("http://[fe80::1]/")]
	[InlineData("http://[::ffff:127.0.0.1]/")]
	[InlineData("http://metadata/")]
	[InlineData("http://metadata.google.internal/")]
	public void 公网策略拒绝私网与保留地址(string url)
	{
		InvalidOperationException error = Assert.Throws<InvalidOperationException>(
			() => UrlAccessPolicy.EnsurePublicHttp(new Uri(url)));
		Assert.Contains("私网或保留地址", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("ftp://example.com/file")]
	[InlineData("file:///etc/passwd")]
	public void 非HTTP地址仍然拒绝(string url)
	{
		InvalidOperationException error = Assert.Throws<InvalidOperationException>(
			() => UrlAccessPolicy.EnsurePublicHttp(new Uri(url)));
		Assert.Contains("仅支持 http/https", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void 允许私网参数保持本机端点可用()
	{
		UrlAccessPolicy.EnsureAllowed(new Uri("http://127.0.0.1:9880/tts"), allowPrivate: true);
		UrlAccessPolicy.EnsureAllowed(new Uri("http://localhost:11434/api"), allowPrivate: true);
		Assert.Throws<InvalidOperationException>(() =>
			UrlAccessPolicy.EnsureAllowed(new Uri("http://localhost:11434/api"), allowPrivate: false));
		Assert.Throws<InvalidOperationException>(() =>
			UrlAccessPolicy.EnsureAllowed(new Uri("http://169.254.169.254/latest/meta-data"), allowPrivate: false));
	}

	[Fact]
	public async Task 公网抓取拒绝重定向到元数据地址()
	{
		using HttpClient client = new(new ScriptedHandler(_ => Found(new Uri("http://169.254.169.254/latest/meta-data"))));
		InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			UrlAccessPolicy.GetWithSafeRedirectsAsync(client, new Uri("https://example.com/start"), allowPrivate: false));
		Assert.Contains("私网或保留地址", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task 解析到私网地址时拒绝且不发出请求()
	{
		int calls = 0;
		UrlAccessPolicy.ResolveHostForTests = (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.1.2.3"), IPAddress.Parse("1.1.1.1") });
		try
		{
			using HttpClient client = new(new ScriptedHandler(_ =>
			{
				calls++;
				return new HttpResponseMessage(HttpStatusCode.OK);
			}));
			InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
				UrlAccessPolicy.GetWithSafeRedirectsAsync(client, new Uri("https://cdn.example/file"), allowPrivate: false));
			Assert.Contains("解析到私网或保留地址", error.Message, StringComparison.Ordinal);
			Assert.Equal(0, calls);
		}
		finally
		{
			UrlAccessPolicy.ResolveHostForTests = null;
		}
	}

	[Fact]
	public async Task DNS失败时仍交给HTTP客户端()
	{
		UrlAccessPolicy.ResolveHostForTests = (_, _) => throw new SocketException((int)SocketError.HostNotFound);
		try
		{
			using HttpClient client = new(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));
			using HttpResponseMessage response = await UrlAccessPolicy.GetWithSafeRedirectsAsync(
				client, new Uri("https://example.com/page"), allowPrivate: false);
			Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
		}
		finally
		{
			UrlAccessPolicy.ResolveHostForTests = null;
		}
	}

	[Fact]
	public async Task 官方SDK响应包装拒绝超限正文()
	{
		using HttpContent original = new StringContent(new string('x', 32));
		using HttpContent capped = UrlAccessPolicy.WrapResponseContent(original, 8);

		await Assert.ThrowsAsync<InvalidOperationException>(() => capped.ReadAsStringAsync());
	}

	[Fact]
	public void 网络异常翻译为稳定消息()
	{
		InvalidOperationException exception = UrlAccessPolicy.Translate(
			new HttpRequestException("连接失败"),
			new Uri("https://example.com"));
		Assert.Equal("访问公网地址失败: example.com", exception.Message);
	}

	private static HttpResponseMessage Found(Uri location)
	{
		HttpResponseMessage response = new(HttpStatusCode.Found);
		response.Headers.Location = location;
		return response;
	}

	private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(respond(request));
	}
}
