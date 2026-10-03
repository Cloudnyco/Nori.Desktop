using Nori.Core.Resources;

namespace Nori.Core.Tests;

/// <summary>
/// 资源名称校验测试
/// </summary>
public class ResourceNameTests
{
	[Theory]
	[InlineData("arg-nori", true)]
	[InlineData("nori", true)]
	[InlineData("", false)]
	[InlineData(".", false)]
	[InlineData("..", false)]
	[InlineData("a/b", false)]
	[InlineData("a\\b", false)]
	[InlineData("C:", false)]
	[InlineData("a\u0001b", false)]
	public void 名称校验(string name, bool expected) =>
		Assert.Equal(expected, ResourceName.IsValid(name));
}
