using System.Runtime.ExceptionServices;
using Nori.Desktop.Live2D.Gl;

namespace Nori.Desktop.Live2D;

/// <summary>只上传预载像素，模型之间从不按文件名共享 GL 句柄。</summary>
internal sealed class NativeTextureOwner(OpenGLApi gl) : IDisposable
{
	private readonly Dictionary<int, int> _textures = [];
	public int this[int slot] => _textures.GetValueOrDefault(slot);
	public IEnumerable<int> Handles => _textures.Values;

	public unsafe void Upload(PreparedTexture texture)
	{
		TexturePixels pixels = texture.Pixels;
		if (texture.Index < 0 || _textures.ContainsKey(texture.Index)
			|| pixels.Width <= 0 || pixels.Height <= 0 || pixels.Data is null
			|| pixels.Data.Length % 4 != 0 || (long)pixels.Width * pixels.Height != pixels.Data.LongLength / 4)
			throw new InvalidDataException("模型纹理槽或 RGBA8888 像素无效");
		gl.ThrowIfError("模型纹理上传前的 GL 状态检查");
		int handle = gl.GenTexture();
		// 从创建起即登记所有权，上传中途失败也能由构造回滚释放。
		if (handle != 0) _textures.Add(texture.Index, handle);
		gl.ThrowIfError("模型纹理分配");
		if (handle == 0) throw new InvalidOperationException("模型纹理创建失败");
		gl.ActiveTexture(gl.GL_TEXTURE0);
		gl.BindTexture(gl.GL_TEXTURE_2D, handle);
		gl.ThrowIfError("模型纹理绑定");
		fixed (byte* pointer = pixels.Data)
			gl.TexImage2D(gl.GL_TEXTURE_2D, 0, gl.GL_RGBA, pixels.Width, pixels.Height, 0, gl.GL_RGBA, gl.GL_UNSIGNED_BYTE, (nint)pointer);
		gl.ThrowIfError("模型纹理像素上传");
		gl.GenerateMipmap(gl.GL_TEXTURE_2D);
		gl.ThrowIfError("模型纹理 mipmap 分配");
		gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_MIN_FILTER, gl.GL_LINEAR_MIPMAP_LINEAR);
		gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_MAG_FILTER, gl.GL_LINEAR);
		gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_WRAP_S, gl.GL_CLAMP_TO_EDGE);
		gl.TexParameteri(gl.GL_TEXTURE_2D, gl.GL_TEXTURE_WRAP_T, gl.GL_CLAMP_TO_EDGE);
		gl.ThrowIfError("模型纹理采样参数设置");
	}

	public void Dispose()
	{
		int[] handles = [.. _textures.Values];
		_textures.Clear();
		Exception? failure = null;
		foreach (int handle in handles)
		{
			try { gl.DeleteTexture(handle); }
			catch (Exception exception) { failure ??= exception; }
		}
		if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
	}
}
