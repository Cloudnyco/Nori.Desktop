using Nori.Desktop.Live2D;

namespace Nori.Desktop.Tests;

/// <summary>使用无设备 GL 替身验证控件的目标交换逻辑，不模拟 GPU 光栅化。</summary>
public sealed class PetRenderTargetResizeTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void 场景重建失败保留旧场景命中目标和尺寸且仍可绘制(bool throws)
	{
		var gl = new RecordingGlApi { CreateResources = true };
		NativeGlSurface? scene = null, hit = null;
		var logs = new List<string>();
		Assert.True(PetGlControl.TryResizeRenderTargets(gl, ref scene, ref hit, 32, 48, logs.Add));
		var previousScene = scene;
		var previousHit = hit;
		if (throws) gl.FailTextureUploadAt = 2;
		else gl.CreateResources = false;

		Assert.True(PetGlControl.TryResizeRenderTargets(gl, ref scene, ref hit, 64, 96, logs.Add));
		Assert.Same(previousScene, scene);
		Assert.Same(previousHit, hit);
		Assert.Equal(32, scene!.BufferWidth);
		Assert.Equal(48, scene.BufferHeight);
		Assert.DoesNotContain(scene.ColorBuffer, gl.DeletedTextures);
		Assert.DoesNotContain(hit!.ColorBuffer, gl.DeletedTextures);
		Assert.Single(logs);
		gl.BindFramebuffer(gl.GL_FRAMEBUFFER, 31);
		scene.BeginDraw();
		try { scene.Clear(0, 0, 0, 0); }
		finally { scene.EndDraw(); }
		gl.GetIntegerv(gl.GL_FRAMEBUFFER_BINDING, out int framebuffer);
		Assert.Equal(31, framebuffer);

		scene.Dispose();
		hit.Dispose();
		AssertAllReleased(gl);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void 命中创建异常不撤销已准备场景(bool hasPreviousScene)
	{
		var gl = new RecordingGlApi { CreateResources = true };
		NativeGlSurface? scene = null, hit = null;
		if (hasPreviousScene)
		{
			scene = new NativeGlSurface(gl);
			Assert.True(scene.CreateOffscreenSurface(32, 48));
		}
		var previousScene = scene;
		gl.FailTextureUploadAt = hasPreviousScene ? 2 : 1;
		var logs = new List<string>();

		Assert.True(PetGlControl.TryResizeRenderTargets(gl, ref scene, ref hit, 64, 96, logs.Add));
		Assert.NotSame(previousScene, scene);
		Assert.True(scene!.IsValid());
		Assert.Equal(64, scene.BufferWidth);
		Assert.Equal(96, scene.BufferHeight);
		Assert.Null(hit);
		if (previousScene is not null) Assert.False(previousScene.IsValid());
		Assert.Single(logs);
		Assert.Contains("场景纹理单次回读", logs[0]);
		int allocations = gl.CreatedTextures.Count;
		Assert.True(PetGlControl.TryResizeRenderTargets(gl, ref scene, ref hit, 64, 96, logs.Add));
		Assert.Equal(allocations, gl.CreatedTextures.Count);

		scene!.Dispose();
		AssertAllReleased(gl);
	}

	[Fact]
	public void 成功重建只替换场景并复用固定尺寸命中目标()
	{
		var gl = new RecordingGlApi { CreateResources = true };
		NativeGlSurface? scene = null, hit = null;
		var logs = new List<string>();
		Assert.True(PetGlControl.TryResizeRenderTargets(gl, ref scene, ref hit, 32, 48, logs.Add));
		var previousScene = scene;
		var previousHit = hit;
		Assert.True(PetGlControl.TryResizeRenderTargets(gl, ref scene, ref hit, 64, 96, logs.Add));
		Assert.NotSame(previousScene, scene);
		Assert.False(previousScene!.IsValid());
		Assert.Same(previousHit, hit);
		Assert.True(hit!.IsValid());
		Assert.Equal(3, gl.CreatedTextures.Count);
		Assert.Empty(logs);
		scene!.Dispose();
		hit.Dispose();
		AssertAllReleased(gl);
	}

	[Fact]
	public void 首次场景创建失败降级直接渲染且不分配命中目标()
	{
		var gl = new RecordingGlApi { CreateResources = true, FailTextureUploadAt = 0 };
		NativeGlSurface? scene = null, hit = null;
		var logs = new List<string>();
		Assert.False(PetGlControl.TryResizeRenderTargets(gl, ref scene, ref hit, 64, 96, logs.Add));
		Assert.Null(scene);
		Assert.Null(hit);
		Assert.Single(gl.CreatedTextures);
		Assert.Single(logs);
		Assert.Contains("直接渲染", logs[0]);
		AssertAllReleased(gl);
	}

	private static void AssertAllReleased(RecordingGlApi gl)
	{
		Assert.Equal(gl.CreatedTextures.Order(), gl.DeletedTextures.Order());
		Assert.Equal(gl.CreatedFramebuffers.Order(), gl.DeletedFramebuffers.Order());
	}
}
