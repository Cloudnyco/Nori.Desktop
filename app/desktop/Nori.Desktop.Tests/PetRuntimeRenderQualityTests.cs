using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Nori.Core.Live2D;
using Nori.Desktop.Live2D;
using Nori.Desktop.Live2D.Gl;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

public partial class BridgeCommandsTests
{
	[Fact]
	public void 遮罩热调整失败保留质量且重试不重复记录日志()
	{
		using var fixture = new BridgeCommandsTests(safeMode: true);
		var runtime = new PetRuntime(fixture._services, previewMode: true);
		var gl = new RecordingGlApi();
		NativeGlRenderer renderer = AttachQualityRenderer(runtime, gl);
		var atlas = (MaskAtlas)RuntimeHelpers.GetUninitializedObject(typeof(MaskAtlas));
		SetQualityField(atlas, "<ClippingMaskBufferSize>k__BackingField", new Vector2(1024));
		SetQualityField(renderer, "_mask", atlas);
		var previous = new NativeGlSurface(gl);
		SetQualityField(previous, "_framebuffer", 51);
		SetQualityField(previous, "<ColorBuffer>k__BackingField", 61);
		SetQualityField(renderer, "_targets", new[] { previous });
		SetQualityField(runtime, "_appliedMaskBufferSize", 1024);
		renderer.Anisotropy = 4;
		renderer.SetModelColor(1, 1, 1, 0.4f);
		SetRuntimeQuality(runtime, Live2DQualityMode.Quality);

		for (int frame = 0; frame < 5; frame++) runtime.ApplyRenderQualityOnGlThread();

		Assert.Equal(1024, QualityField<int>(runtime, "_appliedMaskBufferSize"));
		Assert.Equal(new Vector2(1024), atlas.ClippingMaskBufferSize);
		Assert.Equal(61, renderer.MaskTexture(0));
		Assert.True(previous.IsValid());
		Assert.Equal(4, renderer.Anisotropy);
		Assert.Equal(0.4f, renderer.ModelColor.W);
		Assert.Single(fixture._services.Logger.RecentLogs(), entry => entry.EventId == "live2d.mask_resize_failed");

		gl.CreateResources = true;
		runtime.ApplyRenderQualityOnGlThread();
		Assert.Equal(2048, QualityField<int>(runtime, "_appliedMaskBufferSize"));
		Assert.Equal(new Vector2(2048), atlas.ClippingMaskBufferSize);
		Assert.False(previous.IsValid());
		Assert.Equal(16, renderer.Anisotropy);
		Assert.False(renderer.UseHighPrecisionMask);

		gl.CreateResources = false;
		SetRuntimeQuality(runtime, Live2DQualityMode.Eco);
		for (int frame = 0; frame < 5; frame++) runtime.ApplyRenderQualityOnGlThread();
		Assert.Equal(2, fixture._services.Logger.RecentLogs().Count(entry => entry.EventId == "live2d.mask_resize_failed"));
		foreach (NativeGlSurface target in QualityField<NativeGlSurface[]>(renderer, "_targets")) target.DestroyOffscreenSurface();
	}

	[Theory]
	[InlineData(float.NaN, Live2DRenderSettings.DefaultOpacity)]
	[InlineData(float.PositiveInfinity, Live2DRenderSettings.DefaultOpacity)]
	[InlineData(float.NegativeInfinity, Live2DRenderSettings.DefaultOpacity)]
	[InlineData(-1f, Live2DRenderSettings.MinOpacity)]
	[InlineData(2f, Live2DRenderSettings.MaxOpacity)]
	public void 运行时模型颜色只传入有效不透明度(float opacity, float expected)
	{
		using var fixture = new BridgeCommandsTests(safeMode: true);
		var runtime = new PetRuntime(fixture._services, previewMode: true) { Opacity = opacity };
		NativeGlRenderer renderer = AttachQualityRenderer(runtime, new RecordingGlApi());
		runtime.ApplyRenderQualityOnGlThread();
		Assert.Equal(expected, renderer.ModelColor.W);
		Assert.InRange(renderer.Anisotropy, 4, 16);
	}

	[Live2DAssetsFact]
	public void 已提交模型遮罩失败仍完成渲染且下一帧可重试()
	{
		using var fixture = new BridgeCommandsTests(safeMode: true);
		var runtime = new PetRuntime(fixture._services, previewMode: true) { IdleAnimationEnabled = false };
		SetRuntimeQuality(runtime, Live2DQualityMode.Eco);
		// RenderFrame 只用此字段判断上下文存在；所有实际 GL 调用由记录替身接收。
		runtime.OnGlInit((AvaloniaGlApi)RuntimeHelpers.GetUninitializedObject(typeof(AvaloniaGlApi)));
		var gl = new RecordingGlApi { CreateResources = true };
		var definition = ModelDefinition.Parse("{\"FileReferences\":{\"Moc\":\"模型.moc3\",\"Textures\":[\"0.png\",\"1.png\"]}}"u8.ToArray());
		var assets = new PreparedModelData("测试模型", definition,
			File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("arg-nori", "ARGNori.moc3")),
			null, null, new Dictionary<string, MotionClip>(),
			[new(0, "0.png", new(1, 1, [255, 255, 255, 255])), new(1, "1.png", new(1, 1, [255, 255, 255, 255]))]);
		using var original = new NativeModelHost(gl, assets);
		SetQualityField(runtime, "_currentModel", original);
		runtime.ApplyRenderQualityOnGlThread();
		int oldMask = original.Renderer.MaskTexture(0);
		int frames = 0;
		runtime.FrameRendered += () => frames++;
		try
		{
			gl.CreateResources = false;
			SetRuntimeQuality(runtime, Live2DQualityMode.Quality);
			for (int frame = 0; frame < 3; frame++)
			{
				gl.Clear(gl.GL_COLOR_BUFFER_BIT);
				int before = gl.DrawCalls;
				runtime.RenderFrame(0.016f, 720, 480);
				Assert.True(gl.DrawCalls > before);
			}
			Assert.Equal(3, frames);
			Assert.Equal(oldMask, original.Renderer.MaskTexture(0));
			Assert.Equal(4, original.Renderer.Anisotropy);
			Assert.Single(fixture._services.Logger.RecentLogs(), entry => entry.EventId == "live2d.mask_resize_failed");

			gl.CreateResources = true;
			runtime.RenderFrame(0.016f, 720, 480);
			Assert.Equal(2048, QualityField<int>(runtime, "_appliedMaskBufferSize"));
			Assert.Equal(4, frames);
		}
		finally { runtime.OnGlDeinit(); }
	}

	[Fact]
	public void 首次候选遮罩质量应用失败必须传播异常()
	{
		using var fixture = new BridgeCommandsTests(safeMode: true);
		var runtime = new PetRuntime(fixture._services, previewMode: true);
		NativeGlRenderer renderer = AttachQualityRenderer(runtime, new RecordingGlApi());
		SetQualityField(renderer, "_mask", RuntimeHelpers.GetUninitializedObject(typeof(MaskAtlas)));
		Assert.Throws<InvalidOperationException>(runtime.ApplyRenderQualityOnGlThread);
		Assert.Equal(0, QualityField<int>(runtime, "_appliedMaskBufferSize"));
		Assert.DoesNotContain(fixture._services.Logger.RecentLogs(), entry => entry.EventId == "live2d.mask_resize_failed");
	}

	private static void SetRuntimeQuality(PetRuntime runtime, Live2DQualityMode mode)
	{
		runtime.SetPreviewRenderSettings(1, mode, 1, false, 30);
		SetQualityField(runtime, "_qualityPolicy", new RenderQualityPolicy(
			QualityField<Live2DRenderSettings>(runtime, "_renderSettings"), Live2DPowerSource.Ac));
	}

	private static NativeGlRenderer AttachQualityRenderer(PetRuntime runtime, RecordingGlApi gl)
	{
		var renderer = (NativeGlRenderer)RuntimeHelpers.GetUninitializedObject(typeof(NativeGlRenderer));
		SetQualityField(renderer, "_gl", gl);
		SetQualityField(renderer, "_targets", Array.Empty<NativeGlSurface>());
		var host = (NativeModelHost)RuntimeHelpers.GetUninitializedObject(typeof(NativeModelHost));
		SetQualityField(host, "<Renderer>k__BackingField", renderer);
		SetQualityField(runtime, "_currentModel", host);
		return renderer;
	}

	private static void SetQualityField(object target, string name, object value) =>
		target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

	private static T QualityField<T>(object target, string name) =>
		(T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}
