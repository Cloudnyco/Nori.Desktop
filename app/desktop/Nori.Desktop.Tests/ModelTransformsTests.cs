using System.Numerics;
using System.Runtime.InteropServices;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

public sealed class ModelTransformsTests
{
	[Theory]
	[InlineData("width", 0.75f, 0, 0)]
	[InlineData("height", 1.5f, 0, 0)]
	[InlineData("x", 1, 3, 0)]
	[InlineData("y", 1, 0, 3)]
	[InlineData("center_x", 1, 1, 0)]
	[InlineData("center_y", 1, 0, 2)]
	[InlineData("top", 1, 0, 3)]
	[InlineData("bottom", 1, 0, 1)]
	[InlineData("left", 1, 3, 0)]
	[InlineData("right", 1, -1, 0)]
	public void 布局各定位字段保持既有结果(string key, float scale, float x, float y)
	{
		Matrix4x4 matrix = ModelTransforms.CreateLayout(new(4, 2), new Dictionary<string, float> { [key] = 3 });
		Assert.Equal(scale, matrix.M11);
		Assert.Equal(scale, matrix.M22);
		Assert.Equal(x, matrix.M41);
		Assert.Equal(y, matrix.M42);
	}

	[Fact]
	public void 默认高度与先尺寸后位置的声明顺序()
	{
		Assert.Equal(0.5f, ModelTransforms.CreateLayout(new(2, 4), new Dictionary<string, float>()).M11);
		var layout = new Dictionary<string, float> { ["center_x"] = 4, ["width"] = 3, ["height"] = 4, ["left"] = 2 };
		Matrix4x4 matrix = ModelTransforms.CreateLayout(new(4, 2), layout);
		Assert.Equal(2, matrix.M11);
		Assert.Equal(2, matrix.M41);
		Assert.Throws<ArgumentOutOfRangeException>(() => ModelTransforms.CreateLayout(Vector2.Zero, layout));
		Assert.Throws<ArgumentException>(() => ModelTransforms.CreateLayout(Vector2.One, new Dictionary<string, float> { ["width"] = float.NaN }));
	}

	[Fact]
	public void 先模型后投影且上传内存就是GL列主序()
	{
		Matrix4x4 model = Matrix4x4.CreateScale(0.5f, 0.25f, 1) * Matrix4x4.CreateTranslation(6, 7, 0);
		Matrix4x4 projection = Matrix4x4.CreateScale(2, 3, 1) * Matrix4x4.CreateTranslation(4, 5, 0);
		Matrix4x4 combined = model * projection;
		Assert.Equal(new Vector2(17, 27.5f), Vector2.Transform(new(1, 2), combined));
		ReadOnlySpan<float> upload = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(in combined, 1));
		Assert.Equal(16, upload.Length);
		Assert.Equal(1, upload[0]);
		Assert.Equal(0.75f, upload[5]);
		Assert.Equal(16, upload[12]);
		Assert.Equal(26, upload[13]);
		Assert.True(Matrix4x4.Invert(combined, out Matrix4x4 inverse));
		Assert.Equal(new Vector2(1, 2), Vector2.Transform(Vector2.Transform(new(1, 2), combined), inverse));
		Assert.Equal(4, projection.M41);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void 遮罩UV与裁剪空间映射独立于绘制Y翻转(bool flip)
	{
		var (mask, draw) = ModelTransforms.CreateMask(new(2, 3), new(0.25f, 0.5f), new(0.1f, 0.2f), flip);
		float sign = flip ? -1 : 1;
		Near(new(-0.5f, 0), Vector2.Transform(new(2, 3), mask));
		Near(new(0.25f, 0.5f * sign), Vector2.Transform(new(2, 3), draw));
		Near(new(0.5f, 0.8f), Vector2.Transform(new(7, 5), mask));
		Near(new(0.75f, 0.9f * sign), Vector2.Transform(new(7, 5), draw));
	}

	private static void Near(Vector2 expected, Vector2 actual) => Assert.InRange(Vector2.Distance(expected, actual), 0, 0.000001f);
}
