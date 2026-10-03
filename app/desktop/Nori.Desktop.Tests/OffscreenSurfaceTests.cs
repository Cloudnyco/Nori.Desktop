using Nori.Desktop.Live2D.Gl;
using Nori.Desktop.Live2D;

namespace Nori.Desktop.Tests;

public sealed class OffscreenSurfaceTests
{
	[Theory]
	[InlineData(0, 1)]
	[InlineData(1, 0)]
	[InlineData(-1, 1)]
	[InlineData(1, -1)]
	[InlineData(int.MinValue, int.MaxValue)]
	public void InvalidDimensionsAreRejectedBeforeGlCalls(int width, int height)
	{
		var gl = new RecordingGlApi();
		var surface = new NativeGlSurface(gl);

		Assert.Throws<ArgumentOutOfRangeException>(() => surface.CreateOffscreenSurface(width, height));
		Assert.Empty(gl.IntegerQueries);
		Assert.Empty(gl.StateCalls);
		AssertEmpty(surface);
	}

	[Theory]
	[InlineData(true, 1, 1)]
	[InlineData(false, 720, 480)]
	public void TextureGenerationFailurePreservesEntryState(bool es2, int width, int height)
	{
		var gl = CreateHost(es2);
		var expected = Capture(gl);
		gl.IntegerQueries.Clear();
		var surface = new NativeGlSurface(gl);

		// 现有替身只返回零句柄，不能覆盖成功创建或挂接后的异常。
		Assert.False(surface.CreateOffscreenSurface(width, height));
		Assert.NotEmpty(gl.IntegerQueries);
		if (es2) Assert.DoesNotContain(0x8CAA, gl.IntegerQueries);
		Assert.Equal(expected, Capture(gl));
		AssertEmpty(surface);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void EmptySurfaceDrawingAndRepeatedDestroyDoNotChangeState(bool es2)
	{
		var gl = CreateHost(es2);
		var expected = Capture(gl);
		gl.IntegerQueries.Clear();
		var surface = new NativeGlSurface(gl);

		Assert.Throws<InvalidOperationException>(surface.BeginDraw);
		surface.EndDraw();
		Assert.Throws<InvalidOperationException>(surface.BeginDraw);
		surface.DestroyOffscreenSurface();
		surface.EndDraw();
		surface.DestroyOffscreenSurface();

		Assert.Empty(gl.IntegerQueries);
		Assert.Equal(expected, Capture(gl));
		AssertEmpty(surface);
	}

	[Fact]
	public void 成功目标在重建失败时保留旧句柄尺寸且清理候选()
	{
		var gl = CreateHost(false);
		gl.CreateResources = true;
		var expected = Capture(gl);
		using var surface = new NativeGlSurface(gl);
		Assert.True(surface.CreateOffscreenSurface(32, 48));
		int texture = surface.ColorBuffer;
		gl.FailTextureUploadAt = 1;
		Assert.Throws<InvalidOperationException>(() => surface.CreateOffscreenSurface(64, 96));
		Assert.Equal(texture, surface.ColorBuffer);
		Assert.Equal(32, surface.BufferWidth);
		Assert.Equal(48, surface.BufferHeight);
		Assert.True(surface.IsValid());
		Assert.DoesNotContain(texture, gl.DeletedTextures);
		Assert.Equal(expected, Capture(gl));
		surface.Dispose();
		surface.Dispose();
		Assert.Equal(gl.CreatedTextures.Order(), gl.DeletedTextures.Order());
		Assert.Equal(gl.CreatedFramebuffers.Order(), gl.DeletedFramebuffers.Order());
	}

	private static RecordingGlApi CreateHost(bool es2)
	{
		var gl = new RecordingGlApi { Es2Context = es2 };
		gl.BindFramebuffer(gl.GL_FRAMEBUFFER, 31);
		if (!es2) gl.BindFramebuffer(0x8CA8, 32);
		gl.Viewport(3, 5, 720, 480);
		for (int i = 0; i < 3; i++)
		{
			gl.ActiveTexture(gl.GL_TEXTURE0 + i);
			gl.BindTexture(gl.GL_TEXTURE_2D, 41 + i);
		}
		return gl;
	}

	private static int[] Capture(RecordingGlApi gl)
	{
		gl.GetIntegerv(gl.GL_FRAMEBUFFER_BINDING, out int framebuffer);
		gl.GetIntegerv(gl.GL_ACTIVE_TEXTURE, out int activeTexture);
		int[] viewport = new int[4];
		gl.GetIntegerv(gl.GL_VIEWPORT, viewport);
		var state = new List<int> { framebuffer, activeTexture };
		state.AddRange(viewport);
		if (!gl.IsES2)
		{
			gl.GetIntegerv(0x8CAA, out int readFramebuffer);
			state.Add(readFramebuffer);
		}
		try
		{
			for (int i = 0; i < 3; i++)
			{
				gl.ActiveTexture(gl.GL_TEXTURE0 + i);
				gl.GetIntegerv(gl.GL_TEXTURE_BINDING_2D, out int texture);
				state.Add(texture);
			}
		}
		finally
		{
			gl.ActiveTexture(activeTexture);
		}
		return state.ToArray();
	}

	private static void AssertEmpty(NativeGlSurface surface)
	{
		Assert.False(surface.IsValid());
		Assert.Equal(0, surface.ColorBuffer);
		Assert.Equal(0, surface.BufferWidth);
		Assert.Equal(0, surface.BufferHeight);
	}
}
