using System.Runtime.ExceptionServices;
using Nori.Desktop.Live2D.Gl;

namespace Nori.Desktop.Live2D;

/// <summary>当前上下文独占的 RGBA 离屏目标；创建失败不替换现有目标。</summary>
internal sealed class NativeGlSurface(OpenGLApi gl) : IDisposable
{
	private int _framebuffer;
	private GlStateScope? _drawing;
	public int ColorBuffer { get; private set; }
	public int BufferWidth { get; private set; }
	public int BufferHeight { get; private set; }
	public bool IsValid() => _framebuffer != 0 && ColorBuffer != 0;

	public bool CreateOffscreenSurface(int width, int height)
	{
		if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "离屏尺寸必须大于零");
		if (_drawing is not null) throw new InvalidOperationException("不能在绘制过程中替换离屏目标");
		using var state = new GlStateScope(gl);
		int texture = 0, framebuffer = 0;
		try
		{
			texture = gl.GenTexture();
			framebuffer = gl.GenFramebuffer();
			if (texture == 0 || framebuffer == 0) return false;
			gl.ActiveTexture(gl.GL_TEXTURE0);
			gl.BindTexture(gl.GL_TEXTURE_2D, texture);
			gl.TexImage2D(gl.GL_TEXTURE_2D, 0, gl.GL_RGBA, width, height, 0, gl.GL_RGBA, gl.GL_UNSIGNED_BYTE, 0);
			gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_MIN_FILTER, gl.GL_LINEAR);
			gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_MAG_FILTER, gl.GL_LINEAR);
			gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_WRAP_S, gl.GL_CLAMP_TO_EDGE);
			gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_WRAP_T, gl.GL_CLAMP_TO_EDGE);
			gl.BindFramebuffer(gl.GL_FRAMEBUFFER, framebuffer);
			gl.FramebufferTexture2D(gl.GL_FRAMEBUFFER, gl.GL_COLOR_ATTACHMENT0, gl.GL_TEXTURE_2D, texture, 0);
			if (gl.CheckFramebufferStatus(gl.GL_FRAMEBUFFER) != gl.GL_FRAMEBUFFER_COMPLETE) return false;
			int oldTexture = ColorBuffer, oldFramebuffer = _framebuffer;
			ColorBuffer = texture;
			_framebuffer = framebuffer;
			BufferWidth = width;
			BufferHeight = height;
			texture = framebuffer = 0;
			Delete(oldTexture, oldFramebuffer);
			return true;
		}
		finally { Delete(texture, framebuffer); }
	}

	public void BeginDraw()
	{
		if (!IsValid() || _drawing is not null) throw new InvalidOperationException("离屏目标不可用或已开始绘制");
		_drawing = new GlStateScope(gl);
		try { gl.BindFramebuffer(gl.GL_FRAMEBUFFER, _framebuffer); }
		catch
		{
			EndDraw();
			throw;
		}
	}

	public void EndDraw()
	{
		var state = _drawing;
		_drawing = null;
		state?.Dispose();
	}

	public void Clear(float r, float g, float b, float a)
	{
		gl.ClearColor(r, g, b, a);
		gl.Clear(gl.GL_COLOR_BUFFER_BIT);
	}

	public void DestroyOffscreenSurface() => Dispose();

	public void Dispose()
	{
		try { EndDraw(); }
		finally
		{
			int texture = ColorBuffer, framebuffer = _framebuffer;
			ColorBuffer = _framebuffer = BufferWidth = BufferHeight = 0;
			Delete(texture, framebuffer);
		}
	}

	private void Delete(int texture, int framebuffer)
	{
		Exception? failure = null;
		try { if (framebuffer != 0) gl.DeleteFramebuffer(framebuffer); }
		catch (Exception exception) { failure = exception; }
		try { if (texture != 0) gl.DeleteTexture(texture); }
		catch (Exception exception) { failure ??= exception; }
		if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
	}
}
