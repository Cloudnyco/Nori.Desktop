using System.Numerics;
using Nori.Live2D;

namespace Nori.Desktop.Live2D.Gl;

/// <summary>宿主提供的着色语言与精度能力，避免将桌面 GL 或 GLES 3 误判为 GLES 2。</summary>
public interface IMeshShaderProfile
{
	bool IsOpenGLES { get; }
	/// <summary>GLSL 版本号：GLES 2 为 100，GLES 3 为 300；桌面 GL 使用当前上下文兼容的版本。</summary>
	int ShaderLanguageVersion { get; }
	bool SupportsFragmentHighp { get; }
}

/// <summary>自有网格/遮罩提交边界，不借用旧 SDK 模型或渲染器基类。</summary>
public interface IModelMeshTarget
{
	NativeModel NativeModel { get; }
	Matrix4x4 Projection { get; }
	Vector4 ModelColor { get; }
	bool IsPremultipliedAlpha { get; }
	MaskGroup? ClippingContextBufferForMask { get; set; }
	MaskGroup? ClippingContextBufferForDraw { get; set; }
	int GetBindedTextureId(int index);
	int MaskTexture(int index);
	void BeginMask(int index, int framebuffer);
	void EndMask(int index);
	void PreDraw();
	void DrawMesh(int index);
}

/// <summary>模型网格与蒙版共用的程序；只拥有当前上下文中的程序对象。</summary>
public sealed class MeshProgram(OpenGLApi gl) : IDisposable
{
	private int _program;
	private int _position, _uv, _image, _maskImage, _mode, _premultiplied;
	private int _projection, _maskTransform, _channel, _tile, _tint, _multiplyColor, _screenColor;
	// Uniform 属于本实例独占的程序对象，可跨宿主状态恢复复用。
	private int? _modeValue, _premultipliedValue;
	private Matrix4x4? _projectionValue, _maskTransformValue;
	private Vector4? _channelValue, _tileValue, _tintValue, _multiplyColorValue, _screenColorValue;

	private const string VertexSource = """
		ATTRIBUTE vec2 position;
		ATTRIBUTE vec2 uv;
		uniform mat4 projection;
		uniform mat4 maskTransform;
		VARYING VARYING_PRECISION vec2 texturePoint;
		VARYING VARYING_PRECISION vec4 maskPoint;
		void main() {
			vec4 p = vec4(position, 0.0, 1.0);
			gl_Position = projection * p;
			maskPoint = maskTransform * p;
			texturePoint = vec2(uv.x, 1.0 - uv.y);
		}
		""";

	private const string FragmentSource = """
		uniform sampler2D image;
		uniform sampler2D maskImage;
		uniform int mode;
		uniform int premultiplied;
		uniform vec4 channel;
		uniform vec4 tile;
		uniform vec4 tint;
		uniform vec4 multiplyColor;
		uniform vec4 screenColor;
		VARYING VARYING_PRECISION vec2 texturePoint;
		VARYING VARYING_PRECISION vec4 maskPoint;
		void main() {
			vec4 pixel = SAMPLE(image, texturePoint);
			vec2 q = maskPoint.xy / maskPoint.w;
			if (mode == 1) {
				vec2 inside = step(tile.xy, q) * step(q, tile.zw);
				FRAGMENT_COLOR = channel * pixel.a * inside.x * inside.y;
			} else {
				pixel.rgb *= multiplyColor.rgb;
				if (premultiplied == 1) {
					pixel.rgb += screenColor.rgb * pixel.a - pixel.rgb * screenColor.rgb;
				} else {
					pixel.rgb += screenColor.rgb - pixel.rgb * screenColor.rgb;
					pixel.rgb *= pixel.a;
				}
				pixel *= vec4(tint.rgb * tint.a, tint.a);
				if (mode >= 2) {
					float uncovered = dot(SAMPLE(maskImage, q), channel);
					pixel *= mode == 3 ? uncovered : 1.0 - uncovered;
				}
				FRAGMENT_COLOR = pixel;
			}
		}
		""";

	internal static (string Vertex, string Fragment) CreateShaderSources(OpenGLApi gl)
	{
		var profile = gl as IMeshShaderProfile;
		bool es = profile?.IsOpenGLES ?? gl.IsES2;
		int version = profile?.ShaderLanguageVersion ?? (es ? 100 : 120);
		bool modern = es ? version >= 300 : version >= 130;
		// 未提供精度能力的 GLES 适配器使用必需支持的 mediump。
		string precision = profile?.SupportsFragmentHighp == true ? "highp" : "mediump";
		string header = $"#version {version}{(es && modern ? " es" : "")}\n#define VARYING_PRECISION {(es ? precision : "")}\n";
		string vertex = header + (es ? "precision highp float;\n" : "")
			+ $"#define ATTRIBUTE {(modern ? "in" : "attribute")}\n#define VARYING {(modern ? "out" : "varying")}\n" + VertexSource;
		string fragment = header + (es ? $"precision {precision} float;\n" : "")
			+ $"#define VARYING {(modern ? "in" : "varying")}\n#define SAMPLE {(modern ? "texture" : "texture2D")}\n"
			+ (modern ? "out vec4 fragmentColor;\n#define FRAGMENT_COLOR fragmentColor\n" : "#define FRAGMENT_COLOR gl_FragColor\n") + FragmentSource;
		return (vertex, fragment);
	}

	internal void Bind()
	{
		EnsureProgram();
		gl.UseProgram(_program);
	}

	internal void InitializeVertices(int vertices, int uvs)
	{
		gl.EnableVertexAttribArray(_position);
		gl.EnableVertexAttribArray(_uv);
		BindVertexSegment(vertices, uvs, 0);
	}

	internal void BindVertexSegment(int vertices, int uvs, int segmentBase)
	{
		nint offset = checked(segmentBase * 8);
		gl.BindBuffer(gl.GL_ARRAY_BUFFER, vertices);
		gl.VertexAttribPointer(_position, 2, gl.GL_FLOAT, false, 8, offset);
		gl.BindBuffer(gl.GL_ARRAY_BUFFER, uvs);
		gl.VertexAttribPointer(_uv, 2, gl.GL_FLOAT, false, 8, offset);
	}

	public void Apply(IModelMeshTarget renderer, NativeModel model, int drawable, bool invertedMask)
	{
		MaskGroup? mask = renderer.ClippingContextBufferForMask;
		bool generating = mask is not null;
		mask ??= renderer.ClippingContextBufferForDraw;
		Set(_mode, generating ? 1 : mask is null ? 0 : invertedMask ? 3 : 2, ref _modeValue);
		Set(_premultiplied, renderer.IsPremultipliedAlpha ? 1 : 0, ref _premultipliedValue);
		Set(_projection, generating ? mask!.MaskMatrix : renderer.Projection, ref _projectionValue);
		Set(_maskTransform, mask is null ? Matrix4x4.Identity : generating ? mask.MaskMatrix : mask.DrawMatrix, ref _maskTransformValue);
		if (mask is not null)
		{
			Set(_channel, mask.Channel switch { 0 => Vector4.UnitX, 1 => Vector4.UnitY, 2 => Vector4.UnitZ, _ => Vector4.UnitW }, ref _channelValue);
			Vector4 tile = mask.Tile;
			Set(_tile, new(tile.X * 2 - 1, tile.Y * 2 - 1, (tile.X + tile.Z) * 2 - 1, (tile.Y + tile.W) * 2 - 1), ref _tileValue);
		}
		var color = renderer.ModelColor;
		Set(_tint, new(color.X, color.Y, color.Z, color.W * model.Opacity * model.GetDrawableOpacity(drawable)), ref _tintValue);
		Set(_multiplyColor, model.GetMultiplyColor(drawable), ref _multiplyColorValue);
		Set(_screenColor, model.GetScreenColor(drawable), ref _screenColorValue);
	}

	private void Set(int location, int value, ref int? previous)
	{
		if (previous == value) return;
		gl.Uniform1i(location, value);
		previous = value;
	}

	private void Set(int location, Vector4 value, ref Vector4? previous)
	{
		if (previous == value) return;
		gl.Uniform4f(location, value.X, value.Y, value.Z, value.W);
		previous = value;
	}

	private void Set(int location, Matrix4x4 value, ref Matrix4x4? previous)
	{
		if (previous == value) return;
		gl.UniformMatrix4fv(location, 1, false, value);
		previous = value;
	}

	private void ResetUniforms()
	{
		_modeValue = _premultipliedValue = null;
		_projectionValue = _maskTransformValue = null;
		_channelValue = _tileValue = _tintValue = _multiplyColorValue = _screenColorValue = null;
	}

	public unsafe void EnsureProgram()
	{
		if (_program != 0) return;
		int vertex = 0, fragment = 0, program = 0;
		try
		{
			var sources = CreateShaderSources(gl);
			vertex = Compile(gl.GL_VERTEX_SHADER, sources.Vertex);
			fragment = Compile(gl.GL_FRAGMENT_SHADER, sources.Fragment);
			program = gl.CreateProgram();
			gl.AttachShader(program, vertex);
			gl.AttachShader(program, fragment);
			gl.LinkProgram(program);
			int linked;
			gl.GetProgramiv(program, gl.GL_LINK_STATUS, &linked);
			if (linked == 0)
			{
				gl.GetProgramInfoLog(program, out string log);
				throw new InvalidOperationException($"模型着色程序链接失败：{log}");
			}
			_position = gl.GetAttribLocation(program, "position");
			_uv = gl.GetAttribLocation(program, "uv");
			_image = gl.GetUniformLocation(program, "image");
			_maskImage = gl.GetUniformLocation(program, "maskImage");
			_mode = gl.GetUniformLocation(program, "mode");
			_premultiplied = gl.GetUniformLocation(program, "premultiplied");
			_projection = gl.GetUniformLocation(program, "projection");
			_maskTransform = gl.GetUniformLocation(program, "maskTransform");
			_channel = gl.GetUniformLocation(program, "channel");
			_tile = gl.GetUniformLocation(program, "tile");
			_tint = gl.GetUniformLocation(program, "tint");
			_multiplyColor = gl.GetUniformLocation(program, "multiplyColor");
			_screenColor = gl.GetUniformLocation(program, "screenColor");
			gl.GetIntegerv(gl.GL_CURRENT_PROGRAM, out int previous);
			try
			{
				gl.UseProgram(program);
				// 采样器固定使用不同单元；初始化不改变宿主的当前程序。
				gl.Uniform1i(_image, 0);
				gl.Uniform1i(_maskImage, 1);
			}
			finally { gl.UseProgram(previous); }
			ResetUniforms();
			gl.DetachShader(program, vertex);
			gl.DetachShader(program, fragment);
			_program = program;
			program = 0;
		}
		finally
		{
			if (vertex != 0) gl.DeleteShader(vertex);
			if (fragment != 0) gl.DeleteShader(fragment);
			if (program != 0) gl.DeleteProgram(program);
		}
	}

	private unsafe int Compile(int type, string source)
	{
		int shader = gl.CreateShader(type);
		try
		{
			gl.ShaderSource(shader, source);
			gl.CompileShader(shader);
			int compiled;
			gl.GetShaderiv(shader, gl.GL_COMPILE_STATUS, &compiled);
			if (compiled == 0)
			{
				gl.GetShaderInfoLog(shader, out string log);
				throw new InvalidOperationException($"模型着色器编译失败：{log}");
			}
			return shader;
		}
		catch { gl.DeleteShader(shader); throw; }
	}

	public void Dispose()
	{
		if (_program != 0) gl.DeleteProgram(_program);
		_program = 0;
		ResetUniforms();
	}
}
