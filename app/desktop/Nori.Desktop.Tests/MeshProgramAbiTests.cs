using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using Nori.Desktop.Live2D.Gl;
using Nori.Desktop.Live2D;

namespace Nori.Desktop.Tests;

public sealed unsafe class MeshProgramAbiTests
{
	private static int _fragmentPrecision;
	private static int _precisionQueries;
	private static int _queriedShaderType;
	private static int _queriedPrecisionType;
	private static uint _colorMask;

	[Fact]
	public void GLboolean委托明确使用单字节编组并转接原有布尔调用()
	{
		MethodInfo enabled = typeof(AvaloniaGlApi.Func10).GetMethod("Invoke")!;
		Assert.Equal(UnmanagedType.U1, enabled.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>()!.Value);
		foreach (ParameterInfo parameter in typeof(AvaloniaGlApi.Func4).GetMethod("Invoke")!.GetParameters())
			Assert.Equal(UnmanagedType.U1, parameter.GetCustomAttribute<MarshalAsAttribute>()!.Value);

		var api = CreateApi(GlProfileType.OpenGL, 2, 1, false, 0);
		Assert.False(api.IsEnabled(0));
		Assert.True(api.IsEnabled(1));
		Assert.True(api.IsEnabled(255));
		for (int mask = 0; mask < 16; mask++)
		{
			api.ColorMask((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0);
			uint expected = (uint)((mask & 1) | ((mask >> 1 & 1) << 8) | ((mask >> 2 & 1) << 16) | ((mask >> 3 & 1) << 24));
			Assert.Equal(expected, _colorMask);
		}
	}

	[Theory]
	[InlineData(GlProfileType.OpenGL, 2, 0, false, 0, 110, "")]
	[InlineData(GlProfileType.OpenGL, 2, 1, false, 0, 120, "")]
	[InlineData(GlProfileType.OpenGL, 3, 0, true, 0, 120, "")]
	[InlineData(GlProfileType.OpenGL, 4, 6, true, 0, 120, "")]
	[InlineData(GlProfileType.OpenGL, 3, 1, false, 0, 130, "")]
	[InlineData(GlProfileType.OpenGL, 3, 2, false, 0, 150, "")]
	[InlineData(GlProfileType.OpenGL, 4, 1, false, 0, 150, "")]
	[InlineData(GlProfileType.OpenGLES, 2, 0, false, 0, 100, "mediump")]
	[InlineData(GlProfileType.OpenGLES, 2, 0, false, 23, 100, "highp")]
	[InlineData(GlProfileType.OpenGLES, 3, 0, false, 0, 300, "highp")]
	public void 真实上下文信息选择语言版本和匹配的varying精度(
		GlProfileType type, int major, int minor, bool compatibility, int precision, int version, string qualifier)
	{
		var api = CreateApi(type, major, minor, compatibility, precision);
		bool es = type == GlProfileType.OpenGLES;
		Assert.Equal(es, api.IsOpenGLES);
		Assert.Equal(es && major == 2, api.IsES2);
		Assert.Equal(version, api.ShaderLanguageVersion);
		Assert.Equal(!api.IsES2 || precision > 0, api.SupportsFragmentHighp);
		var sources = MeshProgram.CreateShaderSources(api);
		Assert.Equal(api.IsES2 ? 1 : 0, _precisionQueries);
		if (api.IsES2)
		{
			Assert.Equal(api.GL_FRAGMENT_SHADER, _queriedShaderType);
			Assert.Equal(0x8DF2, _queriedPrecisionType);
		}

		bool modern = es ? version >= 300 : version >= 130;
		foreach (string source in new[] { sources.Vertex, sources.Fragment })
		{
			Assert.StartsWith($"#version {version}{(es && modern ? " es" : "")}\n", source);
			Assert.Contains($"#define VARYING_PRECISION {qualifier}\n", source);
			Assert.Contains("VARYING VARYING_PRECISION vec2 texturePoint;", source);
			Assert.Contains("VARYING VARYING_PRECISION vec4 maskPoint;", source);
			if (!es) Assert.DoesNotContain("precision ", source);
		}
		if (es)
		{
			Assert.Contains("precision highp float;", sources.Vertex);
			Assert.Contains($"precision {qualifier} float;", sources.Fragment);
		}
		Assert.Contains($"#define ATTRIBUTE {(modern ? "in" : "attribute")}\n", sources.Vertex);
		Assert.Contains($"#define VARYING {(modern ? "out" : "varying")}\n", sources.Vertex);
		Assert.Contains($"#define VARYING {(modern ? "in" : "varying")}\n", sources.Fragment);
		Assert.Contains($"#define SAMPLE {(modern ? "texture" : "texture2D")}\n", sources.Fragment);
		Assert.Contains(modern ? "out vec4 fragmentColor;" : "#define FRAGMENT_COLOR gl_FragColor", sources.Fragment);
		if (modern)
		{
			Assert.DoesNotContain("#define ATTRIBUTE attribute", sources.Vertex);
			Assert.DoesNotContain("#define VARYING varying", sources.Vertex);
			Assert.DoesNotContain("#define VARYING varying", sources.Fragment);
			Assert.DoesNotContain("texture2D", sources.Fragment);
			Assert.DoesNotContain("gl_FragColor", sources.Fragment);
		}

		// 版本选择只能改变前缀，绘制、颜色与蒙版的统一主体不另作拷贝。
		var baseline = MeshProgram.CreateShaderSources(new RecordingGlApi());
		Assert.Equal(Body(baseline.Vertex, "ATTRIBUTE vec2 position;"), Body(sources.Vertex, "ATTRIBUTE vec2 position;"));
		Assert.Equal(Body(baseline.Fragment, "uniform sampler2D image;"), Body(sources.Fragment, "uniform sampler2D image;"));
	}

	[Theory]
	[InlineData(false, 120, "")]
	[InlineData(true, 100, "mediump")]
	public void GL接口按IsES2选择安全语法并将源码送入编译流程(bool es, int version, string precision)
	{
		var gl = new RecordingGlApi { Es2Context = es };
		using var program = new MeshProgram(gl);
		program.EnsureProgram();
		var sources = MeshProgram.CreateShaderSources(gl);
		Assert.Equal(new[] { sources.Vertex, sources.Fragment }, gl.Sources);
		Assert.StartsWith($"#version {version}\n", sources.Vertex);
		Assert.Contains($"#define VARYING_PRECISION {precision}\n", sources.Fragment);
	}

	private static string Body(string source, string firstDeclaration) => source[source.IndexOf(firstDeclaration, StringComparison.Ordinal)..];

	private static AvaloniaGlApi CreateApi(GlProfileType type, int major, int minor, bool compatibility, int precision)
	{
		_fragmentPrecision = precision;
		_precisionQueries = 0;
		// 构造实际 Avalonia 适配层，仅替换函数地址；未使用的 GL 入口不执行。
		return new AvaloniaGlApi(new GlInterface(new GlVersion(type, major, minor, compatibility), Resolve));
	}

	private static nint Resolve(string name) => name switch
	{
		"glGetString" => (nint)(delegate* unmanaged[Stdcall]<int, nint>)&GetString,
		"glGetStringi" => (nint)(delegate* unmanaged[Stdcall]<int, int, nint>)&GetStringi,
		"glGetError" => (nint)(delegate* unmanaged[Stdcall]<int>)&GetError,
		"glGetIntegerv" => (nint)(delegate* unmanaged[Stdcall]<int, int*, void>)&GetIntegerv,
		"glGetShaderPrecisionFormat" => (nint)(delegate* unmanaged[Stdcall]<int, int, int*, int*, void>)&GetShaderPrecisionFormat,
		"glIsEnabled" => (nint)(delegate* unmanaged[Stdcall]<int, byte>)&IsEnabled,
		"glColorMask" => (nint)(delegate* unmanaged[Stdcall]<byte, byte, byte, byte, void>)&ColorMask,
		_ => (nint)(delegate* unmanaged[Stdcall]<void>)&Unused
	};

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static nint GetString(int name) => 0;

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static nint GetStringi(int name, int index) => 0;

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int GetError() => 0;

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static void GetIntegerv(int name, int* value) => *value = 0;

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static void GetShaderPrecisionFormat(int shaderType, int precisionType, int* range, int* precision)
	{
		_precisionQueries++;
		_queriedShaderType = shaderType;
		_queriedPrecisionType = precisionType;
		range[0] = range[1] = _fragmentPrecision == 0 ? 0 : 127;
		*precision = _fragmentPrecision;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static byte IsEnabled(int value) => (byte)value;

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static void ColorMask(byte r, byte g, byte b, byte a) => _colorMask = (uint)(r | g << 8 | b << 16 | a << 24);

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static void Unused() { }
}
