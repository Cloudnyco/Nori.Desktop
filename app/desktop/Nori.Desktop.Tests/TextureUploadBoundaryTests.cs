using System.Reflection;
using System.Runtime.CompilerServices;
using Nori.Desktop.Live2D;
using Nori.Desktop.Live2D.Gl;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

public sealed class TextureUploadBoundaryTests
{
	private static PreparedTexture Pixel(int slot = 0) => new(slot, "内存.png", new(1, 1, [255, 255, 255, 255]));

	[Theory]
	[InlineData(0, 1, 4)]
	[InlineData(1, -1, 4)]
	[InlineData(1, 1, 3)]
	[InlineData(1, 1, 8)]
	[InlineData(int.MaxValue, int.MaxValue, 4)]
	public void 无效尺寸或字节长度在分配前拒绝(int width, int height, int length)
	{
		var gl = new TextureGlApi();
		using var owner = new NativeTextureOwner(gl);
		Assert.Throws<InvalidDataException>(() => owner.Upload(new(0, "无效.png", new(width, height, new byte[length]))));
		Assert.Empty(gl.Inner.CreatedTextures);
	}

	[Fact]
	public void 无效槽重复槽和零句柄不进入上传()
	{
		var gl = new TextureGlApi();
		using var owner = new NativeTextureOwner(gl);
		Assert.Throws<InvalidDataException>(() => owner.Upload(Pixel(-1)));
		owner.Upload(Pixel());
		Assert.Throws<InvalidDataException>(() => owner.Upload(Pixel()));
		Assert.Single(gl.Inner.CreatedTextures);
		gl.Inner.CreateResources = false;
		Assert.Throws<InvalidOperationException>(() => owner.Upload(Pixel(1)));
		Assert.Equal(1, gl.Uploads);
		Assert.Equal(0, owner[1]);
		gl.FailureStage = "分配";
		Assert.Contains("0x505", Assert.Throws<InvalidOperationException>(() => owner.Upload(Pixel(1))).Message);
		Assert.Equal(0, gl.GetError());
	}

	[Theory]
	[InlineData("分配")]
	[InlineData("绑定")]
	[InlineData("上传")]
	[InlineData("mipmap")]
	[InlineData("参数")]
	public void 驱动错误抛出且所有已登记纹理可回滚(string stage)
	{
		var gl = new TextureGlApi();
		var owner = new NativeTextureOwner(gl);
		owner.Upload(Pixel());
		gl.FailureStage = stage;
		InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => owner.Upload(Pixel(1)));
		Assert.Contains("0x505", error.Message);
		Assert.Equal(2, gl.Inner.CreatedTextures.Count);
		owner.Dispose();
		owner.Dispose();
		Assert.Equal(gl.Inner.CreatedTextures, gl.Inner.DeletedTextures);
	}

	[Fact]
	public void 已有GL错误不被吞掉且不会分配候选纹理()
	{
		var gl = new TextureGlApi { Error = 0x502 };
		using var owner = new NativeTextureOwner(gl);
		Assert.Throws<InvalidOperationException>(() => owner.Upload(Pixel()));
		Assert.Empty(gl.Inner.CreatedTextures);
	}

	[Fact]
	public void 上传固定使用纹理单元零且释放失败仍逐项清理()
	{
		var gl = new TextureGlApi();
		var owner = new NativeTextureOwner(gl);
		gl.ActiveTexture(gl.GL_TEXTURE1);
		owner.Upload(Pixel());
		owner.Upload(Pixel(1));
		Assert.All(gl.UploadUnits, unit => Assert.Equal(gl.GL_TEXTURE0, unit));
		gl.FailDeleteTexture = true;
		Assert.Throws<InvalidOperationException>(owner.Dispose);
		Assert.Equal(gl.Inner.CreatedTextures, gl.Inner.DeletedTextures);
		owner.Dispose();
	}

	[Theory]
	[InlineData(4, 16, 4)]
	[InlineData(8, 16, 8)]
	[InlineData(16, 16, 16)]
	[InlineData(16, 8, 8)]
	[InlineData(4, 2, 2)]
	[InlineData(0.5f, 16, 1)]
	public void 各向异性按能力裁剪且缓存实际值(float request, float maximum, float expected)
	{
		var gl = new TextureGlApi { MaximumAnisotropy = maximum };
		using var renderer = RendererWithoutNativeModel(gl);
		renderer.Anisotropy = request;
		gl.ActiveTexture(gl.GL_TEXTURE1);
		renderer.PreDraw();
		renderer.PreDraw();
		Assert.Equal(expected, Assert.Single(gl.AnisotropyValues));
		Assert.Equal(expected, Applied(renderer));
		Assert.Equal(gl.GL_TEXTURE0, Assert.Single(gl.AnisotropyUnits));
	}

	[Fact]
	public void 超出上限的不同请求不重复应用且降级质量仍更新()
	{
		var gl = new TextureGlApi { MaximumAnisotropy = 4 };
		using var renderer = RendererWithoutNativeModel(gl);
		foreach (float request in new[] { 16f, 8f, 4f })
		{
			renderer.Anisotropy = request;
			renderer.PreDraw();
		}
		Assert.Equal(4, Assert.Single(gl.AnisotropyValues));
		gl.MaximumAnisotropy = 16;
		renderer.Anisotropy = 16;
		renderer.PreDraw();
		renderer.Anisotropy = 8;
		renderer.PreDraw();
		Assert.Equal(new[] { 4f, 16f, 8f }, gl.AnisotropyValues);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(0.5f)]
	[InlineData(float.NaN)]
	[InlineData(float.PositiveInfinity)]
	public void 不支持或无效上限跳过且不记录为已应用(float maximum)
	{
		var gl = new TextureGlApi { MaximumAnisotropy = maximum };
		using var renderer = RendererWithoutNativeModel(gl);
		renderer.Anisotropy = 16;
		renderer.PreDraw();
		Assert.Empty(gl.AnisotropyValues);
		Assert.True(float.IsNaN(Applied(renderer)));
		gl.MaximumAnisotropy = 8;
		renderer.PreDraw();
		Assert.Equal(8, Assert.Single(gl.AnisotropyValues));
	}

	[Fact]
	public void 各向异性错误不记为成功且重试会重新应用()
	{
		var gl = new TextureGlApi { MaximumAnisotropy = 8 };
		using var renderer = RendererWithoutNativeModel(gl);
		renderer.Anisotropy = 4;
		renderer.PreDraw();
		gl.FailureStage = "各向异性";
		renderer.Anisotropy = 16;
		Assert.Throws<InvalidOperationException>(renderer.PreDraw);
		Assert.True(float.IsNaN(Applied(renderer)));
		renderer.Anisotropy = 4;
		renderer.PreDraw();
		Assert.Equal(new[] { 4f, 8f, 4f }, gl.AnisotropyValues);
		Assert.Equal(4, Applied(renderer));
	}

	[Live2DAssetsFact]
	public void 候选上传错误回滚宿主GL资源且不影响原宿主()
	{
		var data = new PreparedModelData("内存模型", ModelDefinition.Parse("{\"FileReferences\":{\"Moc\":\"内存.moc3\"}}"u8.ToArray()),
			File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("arg-nori", "ARGNori.moc3")), null, null,
			new Dictionary<string, MotionClip>(), [Pixel()]);
		var gl = new TextureGlApi();
		using var original = new NativeModelHost(gl, data);
		int textureCount = gl.Inner.CreatedTextures.Count;
		int bufferCount = gl.Inner.CreatedBuffers.Count;
		int vaoCount = gl.Inner.CreatedVertexArrays.Count;
		foreach (string stage in new[] { "分配", "绑定", "上传", "mipmap", "参数" })
		{
			gl.FailureStage = stage;
			InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new NativeModelHost(gl, data));
			Assert.Contains("0x505", error.Message);
			Assert.Equal(gl.Inner.CreatedTextures.Skip(textureCount), gl.Inner.DeletedTextures);
			Assert.Equal(gl.Inner.CreatedBuffers.Skip(bufferCount), gl.Inner.DeletedBuffers);
			Assert.Equal(gl.Inner.CreatedVertexArrays.Skip(vaoCount), gl.Inner.DeletedVertexArrays);
			Assert.False(original.Model.IsDisposed);
		}
		original.Draw(System.Numerics.Matrix4x4.Identity);
		Assert.True(gl.Inner.DrawCalls > 0);
	}

	private static float Applied(NativeGlRenderer renderer) => (float)typeof(NativeGlRenderer)
		.GetField("_appliedAnisotropy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;

	// 仅隔离 PreDraw 的托管状态；不把此测试当作原生模型或真实 GPU 验证。
	private static NativeGlRenderer RendererWithoutNativeModel(TextureGlApi gl)
	{
		var renderer = (NativeGlRenderer)RuntimeHelpers.GetUninitializedObject(typeof(NativeGlRenderer));
		var textures = new NativeTextureOwner(gl);
		textures.Upload(Pixel());
		Set("_gl", gl);
		Set("_textures", textures);
		Set("_program", new MeshProgram(gl));
		Set("_targets", Array.Empty<NativeGlSurface>());
		Set("_appliedAnisotropy", float.NaN);
		return renderer;

		void Set(string name, object value) => typeof(NativeGlRenderer)
			.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, value);
	}

	/// <summary>只包装共享替身，不修改它；注入 GL 返回错误而非托管上传异常。</summary>
	private sealed unsafe class TextureGlApi : OpenGLApi
	{
		public RecordingGlApi Inner { get; } = new() { CreateResources = true };
		public string? FailureStage { get; set; }
		public int Error { get; set; }
		public int Uploads { get; private set; }
		public bool FailDeleteTexture { get; set; }
		public float MaximumAnisotropy { get; set; }
		public List<int> UploadUnits { get; } = [];
		public List<int> AnisotropyUnits { get; } = [];
		public List<float> AnisotropyValues { get; } = [];
		public override float MaxTextureAnisotropy => MaximumAnisotropy;
		public override bool AlwaysClear => Inner.AlwaysClear;
		public override int GetError()
		{
			int error = Error;
			Error = 0;
			return error;
		}
		private void Fail(string stage)
		{
			if (FailureStage != stage) return;
			Error = 0x505;
			FailureStage = null;
		}
		private int ActiveUnit() { Inner.GetIntegerv(GL_ACTIVE_TEXTURE, out int unit); return unit; }
		public override int GenTexture() { int handle = Inner.GenTexture(); Fail("分配"); return handle; }
		public override void BindTexture(int bit, int index) { Inner.BindTexture(bit, index); Fail("绑定"); }
		public override void TexImage2D(int type, int a, int type1, int w, int h, int size, int type2, int type3, nint data)
		{
			Uploads++;
			UploadUnits.Add(ActiveUnit());
			Inner.TexImage2D(type, a, type1, w, h, size, type2, type3, data);
			Fail("上传");
		}
		public override void GenerateMipmap(int a) { Inner.GenerateMipmap(a); Fail("mipmap"); }
		public override void TexParameteri(int a, int b, int c) { Inner.TexParameteri(a, b, c); Fail("参数"); }
		public override void TexParameterf(int type, int type1, float value)
		{
			AnisotropyUnits.Add(ActiveUnit());
			AnisotropyValues.Add(value);
			Inner.TexParameterf(type, type1, value);
			Fail("各向异性");
		}
		public override void DeleteTexture(int data)
		{
			Inner.DeleteTexture(data);
			if (FailDeleteTexture) throw new InvalidOperationException("注入的纹理释放故障");
		}

		public override void Viewport(int x, int y, int w, int h) => Inner.Viewport(x, y, w, h);
		public override void ClearColor(float r, float g, float b, float a) => Inner.ClearColor(r, g, b, a);
		public override void Clear(int bit) => Inner.Clear(bit);
		public override void Enable(int bit) => Inner.Enable(bit);
		public override void Disable(int bit) => Inner.Disable(bit);
		public override void EnableVertexAttribArray(int index) => Inner.EnableVertexAttribArray(index);
		public override void DisableVertexAttribArray(int index) => Inner.DisableVertexAttribArray(index);
		public override void GetIntegerv(int bit, out int data) => Inner.GetIntegerv(bit, out data);
		public override void GetIntegerv(int bit, int[] data) => Inner.GetIntegerv(bit, data);
		public override void GetFloatv(int bit, float[] data) => Inner.GetFloatv(bit, data);
		public override void ActiveTexture(int bit) => Inner.ActiveTexture(bit);
		public override void GetVertexAttribiv(int index, int bit, out int data) => Inner.GetVertexAttribiv(index, bit, out data);
		public override bool IsEnabled(int bit) => Inner.IsEnabled(bit);
		public override void GetBooleanv(int bit, bool[] data) => Inner.GetBooleanv(bit, data);
		public override void UseProgram(int index) => Inner.UseProgram(index);
		public override void FrontFace(int data) => Inner.FrontFace(data);
		public override void ColorMask(bool a, bool b, bool c, bool d) => Inner.ColorMask(a, b, c, d);
		public override void BindBuffer(int bit, int index) => Inner.BindBuffer(bit, index);
		public override void BlendFuncSeparate(int a, int b, int c, int d) => Inner.BlendFuncSeparate(a, b, c, d);
		public override void BlendEquationSeparate(int rgb, int alpha) => Inner.BlendEquationSeparate(rgb, alpha);
		public override void DeleteProgram(int index) => Inner.DeleteProgram(index);
		public override int GetAttribLocation(int index, string attr) => Inner.GetAttribLocation(index, attr);
		public override int GetUniformLocation(int index, string uni) => Inner.GetUniformLocation(index, uni);
		public override void Uniform1i(int index, int data) => Inner.Uniform1i(index, data);
		public override void VertexAttribPointer(int index, int length, int type, bool b, int size, nint arr) => Inner.VertexAttribPointer(index, length, type, b, size, arr);
		public override void Uniform4f(int index, float a, float b, float c, float d) => Inner.Uniform4f(index, a, b, c, d);
		public override void UniformMatrix4fv(int index, int length, bool b, ReadOnlySpan<float> data) => Inner.UniformMatrix4fv(index, length, b, data);
		public override int CreateProgram() => Inner.CreateProgram();
		public override void AttachShader(int a, int b) => Inner.AttachShader(a, b);
		public override void DeleteShader(int index) => Inner.DeleteShader(index);
		public override void DetachShader(int index, int data) => Inner.DetachShader(index, data);
		public override int CreateShader(int type) => Inner.CreateShader(type);
		public override void ShaderSource(int a, string source) => Inner.ShaderSource(a, source);
		public override void CompileShader(int index) => Inner.CompileShader(index);
		public override void GetShaderiv(int index, int type, int* length) => Inner.GetShaderiv(index, type, length);
		public override void GetShaderInfoLog(int index, out string log) => Inner.GetShaderInfoLog(index, out log);
		public override void LinkProgram(int index) => Inner.LinkProgram(index);
		public override void GetProgramiv(int index, int type, int* length) => Inner.GetProgramiv(index, type, length);
		public override void GetProgramInfoLog(int index, out string log) => Inner.GetProgramInfoLog(index, out log);
		public override void DrawElements(int type, int count, int type1, nint arry) => Inner.DrawElements(type, count, type1, arry);
		public override void BindFramebuffer(int type, int data) => Inner.BindFramebuffer(type, data);
		public override int GenFramebuffer() => Inner.GenFramebuffer();
		public override void FramebufferTexture2D(int a, int b, int c, int buff, int data) => Inner.FramebufferTexture2D(a, b, c, buff, data);
		public override void DeleteFramebuffer(int fb) => Inner.DeleteFramebuffer(fb);
		public override void BlendFunc(int a, int b) => Inner.BlendFunc(a, b);
		public override int GenBuffer() => Inner.GenBuffer();
		public override void DeleteBuffer(int buffer) => Inner.DeleteBuffer(buffer);
		public override void BufferData(int type, int v1, nint v2, int type1) => Inner.BufferData(type, v1, v2, type1);
		public override int GenVertexArray() => Inner.GenVertexArray();
		public override void DeleteVertexArray(int vertexArray) => Inner.DeleteVertexArray(vertexArray);
		public override void BindVertexArray(int vertexArray) => Inner.BindVertexArray(vertexArray);
		public override int CheckFramebufferStatus(int target) => Inner.CheckFramebufferStatus(target);
	}
}
