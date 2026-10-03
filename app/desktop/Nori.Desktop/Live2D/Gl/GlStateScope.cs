namespace Nori.Desktop.Live2D.Gl;

/// <summary>
/// 保存网格和遮罩绘制会改变的上下文状态；必须在同一当前 GL 上下文内释放。
/// 无 VAO 时仅恢复顶点属性开关，现有 API 无法查询并恢复属性指针与布局。
/// </summary>
public sealed class GlStateScope : IDisposable
{
	private const int MaxVertexAttribs = 0x8869;
	private const int ReadFramebufferBinding = 0x8CAA;
	private const int ReadFramebuffer = 0x8CA8;
	private readonly OpenGLApi _gl;
	private readonly int _program, _vertexArray, _arrayBuffer, _elementBuffer;
	private readonly int _activeTexture, _texture0, _texture1;
	private readonly int _srcRgb, _dstRgb, _srcAlpha, _dstAlpha, _equationRgb, _equationAlpha;
	private readonly int _frontFace, _readFramebuffer;
	private readonly bool _hasVertexArrays, _separateFramebuffers;
	private readonly int[] _capabilities;
	private readonly bool[] _enabled;
	private readonly bool[] _colorMask = new bool[4];
	private readonly float[] _clearColor = new float[4];
	private readonly bool[] _attributes;
	private bool _disposed;

	public int Framebuffer { get; }
	// 供遮罩绘制读取，调用方不得修改快照。
	public int[] Viewport { get; } = new int[4];

	public GlStateScope(OpenGLApi gl)
	{
		_gl = gl;
		_hasVertexArrays = gl.SupportsVertexArrayObjects;
		// 原生非 ES2 路径使用 GL3/GLES3，可独立绑定读写 FBO。
		_separateFramebuffers = !gl.IsES2;
		_program = Read(gl.GL_CURRENT_PROGRAM);
		_vertexArray = _hasVertexArrays ? Read(gl.GL_VERTEX_ARRAY_BINDING) : 0;
		_arrayBuffer = Read(gl.GL_ARRAY_BUFFER_BINDING);
		_elementBuffer = _hasVertexArrays && _vertexArray != 0 || !_hasVertexArrays ? Read(gl.GL_ELEMENT_ARRAY_BUFFER_BINDING) : 0;
		Framebuffer = Read(gl.GL_FRAMEBUFFER_BINDING);
		_readFramebuffer = _separateFramebuffers ? Read(ReadFramebufferBinding) : Framebuffer;
		gl.GetIntegerv(gl.GL_VIEWPORT, Viewport);
		_activeTexture = Read(gl.GL_ACTIVE_TEXTURE);
		_srcRgb = Read(gl.GL_BLEND_SRC_RGB);
		_dstRgb = Read(gl.GL_BLEND_DST_RGB);
		_srcAlpha = Read(gl.GL_BLEND_SRC_ALPHA);
		_dstAlpha = Read(gl.GL_BLEND_DST_ALPHA);
		_equationRgb = Read(gl.GL_BLEND_EQUATION_RGB);
		_equationAlpha = Read(gl.GL_BLEND_EQUATION_ALPHA);
		_frontFace = Read(gl.GL_FRONT_FACE);
		gl.GetBooleanv(gl.GL_COLOR_WRITEMASK, _colorMask);
		gl.GetFloatv(gl.GL_COLOR_CLEAR_VALUE, _clearColor);
		_capabilities = [gl.GL_BLEND, gl.GL_DEPTH_TEST, gl.GL_STENCIL_TEST, gl.GL_SCISSOR_TEST, gl.GL_CULL_FACE];
		_enabled = new bool[_capabilities.Length];
		for (int i = 0; i < _capabilities.Length; i++)
			_enabled[i] = gl.IsEnabled(_capabilities[i]);
		_attributes = _hasVertexArrays ? [] : new bool[Read(MaxVertexAttribs)];
		for (int i = 0; i < _attributes.Length; i++)
		{
			gl.GetVertexAttribiv(i, gl.GL_VERTEX_ATTRIB_ARRAY_ENABLED, out int enabled);
			_attributes[i] = enabled != 0;
		}

		// 查询纹理需要切换单元，构造失败也不能留下不同的活动单元。
		try
		{
			gl.ActiveTexture(gl.GL_TEXTURE0);
			_texture0 = Read(gl.GL_TEXTURE_BINDING_2D);
			gl.ActiveTexture(gl.GL_TEXTURE1);
			_texture1 = Read(gl.GL_TEXTURE_BINDING_2D);
		}
		finally
		{
			gl.ActiveTexture(_activeTexture);
		}
	}

	private int Read(int parameter)
	{
		_gl.GetIntegerv(parameter, out int value);
		return value;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_gl.UseProgram(_program);
		if (_hasVertexArrays)
		{
			_gl.GetIntegerv(_gl.GL_VERTEX_ARRAY_BINDING, out int currentVertexArray);
			int currentElementBuffer = currentVertexArray != 0 && currentVertexArray == _vertexArray
				? Read(_gl.GL_ELEMENT_ARRAY_BUFFER_BINDING) : _elementBuffer;
			_gl.BindVertexArray(_vertexArray);
			if (_vertexArray != 0 && currentVertexArray == _vertexArray && currentElementBuffer != _elementBuffer)
				_gl.BindBuffer(_gl.GL_ELEMENT_ARRAY_BUFFER, _elementBuffer);
		}
		else _gl.BindBuffer(_gl.GL_ELEMENT_ARRAY_BUFFER, _elementBuffer);
		_gl.BindBuffer(_gl.GL_ARRAY_BUFFER, _arrayBuffer);
		for (int i = 0; i < _attributes.Length; i++)
		{
			if (_attributes[i]) _gl.EnableVertexAttribArray(i);
			else _gl.DisableVertexAttribArray(i);
		}
		_gl.BindFramebuffer(_gl.GL_FRAMEBUFFER, Framebuffer);
		if (_separateFramebuffers) _gl.BindFramebuffer(ReadFramebuffer, _readFramebuffer);
		_gl.Viewport(Viewport[0], Viewport[1], Viewport[2], Viewport[3]);
		_gl.ActiveTexture(_gl.GL_TEXTURE0);
		_gl.BindTexture(_gl.GL_TEXTURE_2D, _texture0);
		_gl.ActiveTexture(_gl.GL_TEXTURE1);
		_gl.BindTexture(_gl.GL_TEXTURE_2D, _texture1);
		_gl.ActiveTexture(_activeTexture);
		_gl.BlendFuncSeparate(_srcRgb, _dstRgb, _srcAlpha, _dstAlpha);
		_gl.BlendEquationSeparate(_equationRgb, _equationAlpha);
		_gl.FrontFace(_frontFace);
		_gl.ColorMask(_colorMask[0], _colorMask[1], _colorMask[2], _colorMask[3]);
		_gl.ClearColor(_clearColor[0], _clearColor[1], _clearColor[2], _clearColor[3]);
		for (int i = 0; i < _capabilities.Length; i++)
		{
			if (_enabled[i]) _gl.Enable(_capabilities[i]);
			else _gl.Disable(_capabilities[i]);
		}
	}
}
