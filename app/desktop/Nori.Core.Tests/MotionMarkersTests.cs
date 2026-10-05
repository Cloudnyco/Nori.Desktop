using Nori.Core.Chat;

namespace Nori.Core.Tests;

/// <summary>
/// 动作标记解析测试
/// </summary>
public class MotionMarkersTests
{
	[Theory]
	[InlineData("你好呀。\n[nori_motion:smile]", "你好呀。\n", new[] { "smile" })]
	[InlineData("A[nori_motion:a]B[nori_motion:b]C", "ABC", new[] { "a", "b" })]
	[InlineData("[nori_motion:  smile  ]", "", new[] { "smile" })]
	[InlineData("A[nori_motion:]B", "AB", new string[0])]
	[InlineData("A[nori_motion:smile", "A[nori_motion:smile", new string[0])]
	[InlineData("普通回复", "普通回复", new string[0])]
	public void 提取动作标记(string input, string expectedContent, string[] expectedMotions)
	{
		(string content, IReadOnlyList<string> motions) = MotionMarkers.Extract(input);
		Assert.Equal(expectedContent, content);
		Assert.Equal(expectedMotions, motions);
	}
}
