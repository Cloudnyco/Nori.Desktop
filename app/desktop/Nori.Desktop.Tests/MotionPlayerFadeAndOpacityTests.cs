using System.Text;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

public sealed class MotionPlayerFadeAndOpacityTests
{
	private static MotionClip Clip(string meta = "") => MotionClip.Parse(Encoding.UTF8.GetBytes(
		$$"""
		{"Version":3,"Meta":{"Duration":2{{meta}}},"FadeInTime":9,"FadeOutTime":8,
		"Curves":[{"Target":"Parameter","Id":"FadeRegression","Segments":[0,1]}]}
		"""));

	[Theory]
	[InlineData("", 1, 1)]
	[InlineData(",\"FadeInTime\":0,\"FadeOutTime\":0", 0, 0)]
	[InlineData(",\"FadeInTime\":2,\"FadeOutTime\":0.5", 2, 0.5f)]
	[InlineData(",\"FadeInTime\":-1,\"FadeOutTime\":-1", 1, 1)]
	[InlineData(",\"FadeInTime\":0", 0, 1)]
	[InlineData(",\"FadeOutTime\":0", 1, 0)]
	public void 全局淡化只读取Meta且保留缺省与显式零(string meta, float fadeIn, float fadeOut)
	{
		MotionClip clip = Clip(meta);
		Assert.Equal(fadeIn, clip.FadeIn);
		Assert.Equal(fadeOut, clip.FadeOut);
		MotionCurve curve = Assert.Single(clip.Curves);
		Assert.Equal(-1, curve.FadeIn);
		Assert.Equal(-1, curve.FadeOut);
	}

	[Live2DAssetsFact]
	public void MotionPlayer使用Meta全局淡化且model3可继承或覆盖()
	{
		MotionClip clip = Clip(",\"FadeInTime\":2,\"FadeOutTime\":1");
		byte[] moc = File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("arg-nori", "ARGNori.moc3"));
		foreach (var (fields, fadeIn, fadeOut, early, late) in new[]
		{
			("", 2f, 1f, 0.1464466f, 0.4267767f),
			(",\"FadeInTime\":-1,\"FadeOutTime\":-1", 2f, 1f, 0.1464466f, 0.4267767f),
			(",\"FadeInTime\":0,\"FadeOutTime\":0", 0f, 0f, 1f, 1f),
			(",\"FadeInTime\":1,\"FadeOutTime\":2", 1f, 2f, 0.4267767f, 0.1464466f),
		})
		{
			ModelDefinition definition = ModelDefinition.Parse(Encoding.UTF8.GetBytes(
				$$"""{"Version":3,"FileReferences":{"Moc":"memory.moc3","Motions":{"Idle":[{"File":"memory.motion3.json"{{fields}}}] } } }"""));
			using var animation = new AnimatedModel(moc, definition,
				new Dictionary<string, MotionClip> { ["Idle_0"] = clip }) { RandomMotion = false };
			MotionPlayback playback = Assert.IsType<MotionPlayback>(animation.StartMotion("Idle", 0, MotionPriority.Normal));
			Assert.Equal(fadeIn, playback.FadeIn);
			Assert.Equal(fadeOut, playback.FadeOut);
			int parameter = animation.Model.GetParameterIndex("FadeRegression");
			animation.Update(0);
			Assert.Equal(fadeIn == 0 ? 1 : 0, animation.Model.GetParameterValue(parameter));
			animation.Model.SetParameterValue(parameter, 0);
			animation.Update(0.5f);
			Assert.Equal(early, animation.Model.GetParameterValue(parameter), 5);
			animation.Model.SetParameterValue(parameter, 0);
			animation.Update(1);
			Assert.Equal(late, animation.Model.GetParameterValue(parameter), 5);
		}
	}

	[Fact]
	public void 绘制Tint组合三层不透明度且零模型不透明度清除可见Alpha()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Nori.Desktop", "Live2D", "Gl")))
			directory = directory.Parent;
		Assert.NotNull(directory);
		string source = File.ReadAllText(Path.Combine(directory.FullName, "Nori.Desktop", "Live2D", "Gl", "MeshProgram.cs"));
		// 源码契约直接锁定提交的 alpha 与着色器乘法，不需要 GPU 或外部模型。
		Assert.Contains("var color = renderer.ModelColor;", source);
		Assert.Contains("Set(\"tint\", new(color.X, color.Y, color.Z, color.W * model.Opacity * model.GetDrawableOpacity(drawable)));", source);
		Assert.Contains("pixel *= vec4(tint.rgb * tint.a, tint.a);", source);
	}
}
