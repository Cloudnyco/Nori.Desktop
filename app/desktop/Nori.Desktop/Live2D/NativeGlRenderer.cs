using System.Numerics;
using System.Runtime.ExceptionServices;
using Nori.Desktop.Live2D.Gl;
using Nori.Live2D;

namespace Nori.Desktop.Live2D;

/// <summary>原生模型的 GL 资源与提交器；不继承旧 SDK 模型或渲染器。</summary>
public sealed class NativeGlRenderer : IModelMeshTarget, IDisposable
{
	private readonly OpenGLApi _gl;
	private readonly MeshProgram _program;
	private readonly NativeTextureOwner _textures;
	private readonly MaskAtlas? _mask;
	private NativeGlSurface[] _targets = [];
	private int _vao, _vertices, _indices;
	private float[] _interleaved = [];
	private readonly int[] _order;
	private float _appliedAnisotropy = float.NaN;
	private bool _disposed;

	public NativeModel NativeModel { get; }
	public Matrix4x4 Projection { get; private set; } = Matrix4x4.Identity;
	public Vector4 ModelColor { get; private set; } = Vector4.One;
	public bool IsPremultipliedAlpha => false;
	public bool UseHighPrecisionMask { get; set; }
	public float Anisotropy { get; set; }
	public MaskGroup? ClippingContextBufferForMask { get; set; }
	public MaskGroup? ClippingContextBufferForDraw { get; set; }

	public NativeGlRenderer(OpenGLApi gl, NativeModel model, IReadOnlyList<PreparedTexture> textures)
	{
		_gl = gl;
		NativeModel = model;
		_order = new int[model.DrawableCount];
		_program = new(gl);
		_textures = new(gl);
		using var state = new GlStateScope(gl);
		try
		{
			if (!gl.SupportsVertexArrayObjects) throw new InvalidOperationException("当前 GL 上下文不支持模型顶点数组对象");
			_vao = gl.GenVertexArray();
			_vertices = gl.GenBuffer();
			_indices = gl.GenBuffer();
			if (_vao == 0 || _vertices == 0 || _indices == 0) throw new InvalidOperationException("模型网格缓冲创建失败");
			foreach (PreparedTexture texture in textures) _textures.Upload(texture);
			// 首帧之前即验证程序，候选失败不能先替换当前模型。
			_program.EnsureProgram();
			if (model.IsUsingMasking())
			{
				_mask = new(gl, model, 1);
				SetClippingMaskBufferSize(256, 256);
			}
		}
		catch
		{
			try { Dispose(); } catch { /* 保留初始化异常，资源释放仍逐项执行。 */ }
			throw;
		}
	}

	public void SetModelColor(float r, float g, float b, float a) => ModelColor = Vector4.Clamp(new(r, g, b, a), Vector4.Zero, Vector4.One);
	public int GetBindedTextureId(int index) => _textures[index];
	public int MaskTexture(int index) => _targets[index].ColorBuffer;
	public void BeginMask(int index, int framebuffer) => _targets[index].BeginDraw();
	public void EndMask(int index) => _targets[index].EndDraw();

	public void SetClippingMaskBufferSize(int width, int height)
	{
		if (_mask is null) return;
		var target = new NativeGlSurface(_gl);
		try
		{
			if (!target.CreateOffscreenSurface(width, height)) throw new InvalidOperationException("模型遮罩目标创建失败");
			_mask.Resize(width, height);
		}
		catch
		{
			try { target.DestroyOffscreenSurface(); } catch { /* 不覆盖创建异常。 */ }
			throw;
		}
		var previous = _targets;
		_targets = [target];
		foreach (var surface in previous) surface.DestroyOffscreenSurface();
	}

	public unsafe void Draw(Matrix4x4 projection)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		Projection = projection;
		using var state = new GlStateScope(_gl);
		try
		{
			_mask?.Update(UseHighPrecisionMask);
			if (_mask is not null && !UseHighPrecisionMask) _mask.Draw(this, state.Framebuffer, state.Viewport);
			PreDraw();
			int* orders = NativeModel.GetDrawableRenderOrders();
			for (int i = 0; i < _order.Length; i++) _order[orders[i]] = i;
			foreach (int drawable in _order)
			{
				if (!NativeModel.GetDrawableDynamicFlagIsVisible(drawable)) continue;
				MaskGroup? group = _mask?.Plan.Drawables[drawable];
				if (group is { Active: false }) continue;
				if (group is not null && UseHighPrecisionMask)
				{
					_mask!.Draw(this, state.Framebuffer, state.Viewport, group);
					PreDraw();
				}
				ClippingContextBufferForDraw = group;
				DrawMesh(drawable);
			}
		}
		finally
		{
			ClippingContextBufferForDraw = ClippingContextBufferForMask = null;
			GC.KeepAlive(NativeModel);
		}
	}

	public void PreDraw()
	{
		_gl.BindVertexArray(_vao);
		_gl.Disable(_gl.GL_DEPTH_TEST);
		_gl.Disable(_gl.GL_STENCIL_TEST);
		_gl.Disable(_gl.GL_SCISSOR_TEST);
		_gl.Enable(_gl.GL_BLEND);
		_gl.BlendEquationSeparate(0x8006, 0x8006);
		_gl.ColorMask(true, true, true, true);
		if (!float.IsFinite(Anisotropy) || Anisotropy <= 0) return;
		float maximum = _gl.MaxTextureAnisotropy;
		if (!float.IsFinite(maximum) || maximum < 1) return;
		float applied = Math.Clamp(Anisotropy, 1, maximum);
		if (applied.Equals(_appliedAnisotropy)) return;
		// 部分纹理设置失败时不能保留旧缓存，下一次必须重新逐项应用。
		_appliedAnisotropy = float.NaN;
		_gl.ActiveTexture(_gl.GL_TEXTURE0);
		foreach (int texture in _textures.Handles)
		{
			_gl.BindTexture(_gl.GL_TEXTURE_2D, texture);
			_gl.TexParameterf(_gl.GL_TEXTURE_2D, _gl.GL_TEXTURE_MAX_ANISOTROPY_EXT, applied);
			_gl.ThrowIfError("模型纹理各向异性设置");
		}
		_appliedAnisotropy = applied;
	}

	public unsafe void DrawMesh(int index)
	{
		try
		{
			if (_textures[NativeModel.GetDrawableTextureIndex(index)] == 0) return;
			if (NativeModel.GetDrawableCulling(index)) _gl.Enable(_gl.GL_CULL_FACE);
			else _gl.Disable(_gl.GL_CULL_FACE);
			_gl.FrontFace(_gl.GL_CCW);
			int count = NativeModel.GetDrawableVertexCount(index);
			if (_interleaved.Length < count * 4) _interleaved = new float[count * 4];
			Vector2* positions = NativeModel.GetDrawableVertexPositions(index);
			Vector2* uvs = NativeModel.GetDrawableVertexUvs(index);
			for (int i = 0; i < count; i++)
			{
				_interleaved[i * 4] = positions[i].X;
				_interleaved[i * 4 + 1] = positions[i].Y;
				_interleaved[i * 4 + 2] = uvs[i].X;
				_interleaved[i * 4 + 3] = uvs[i].Y;
			}
			_gl.BindVertexArray(_vao);
			_gl.BindBuffer(_gl.GL_ARRAY_BUFFER, _vertices);
			fixed (float* data = _interleaved)
				_gl.BufferData(_gl.GL_ARRAY_BUFFER, count * 4 * sizeof(float), (nint)data, _gl.GL_DYNAMIC_DRAW);
			int indexCount = NativeModel.GetDrawableVertexIndexCount(index);
			_gl.BindBuffer(_gl.GL_ELEMENT_ARRAY_BUFFER, _indices);
			_gl.BufferData(_gl.GL_ELEMENT_ARRAY_BUFFER, indexCount * sizeof(ushort), (nint)NativeModel.GetDrawableVertexIndices(index), _gl.GL_DYNAMIC_DRAW);
			_program.Apply(this, NativeModel, index);
			_gl.DrawElements(_gl.GL_TRIANGLES, indexCount, _gl.GL_UNSIGNED_SHORT, 0);
		}
		finally { ClippingContextBufferForDraw = ClippingContextBufferForMask = null; }
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Exception? failure = null;
		Release(_program.Dispose);
		Release(_textures.Dispose);
		foreach (var target in _targets) Release(target.DestroyOffscreenSurface);
		_targets = [];
		if (_vertices != 0) Release(() => _gl.DeleteBuffer(_vertices));
		if (_indices != 0) Release(() => _gl.DeleteBuffer(_indices));
		if (_vao != 0) Release(() => _gl.DeleteVertexArray(_vao));
		_vao = _vertices = _indices = 0;
		if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();

		void Release(Action action)
		{
			try { action(); }
			catch (Exception exception) { failure ??= exception; }
		}
	}
}
