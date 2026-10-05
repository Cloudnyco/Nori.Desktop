using Nori.Desktop.Live2D.Gl;

namespace Nori.Desktop.Tests;

public sealed class MeshProgramTests
{
	[Theory]
	[InlineData(0, false)]
	[InlineData(1, false)]
	[InlineData(-1, true)]
	public void 编译或链接失败清理全部已创建对象且可重试(int shader, bool link)
	{
		var gl = new RecordingGlApi { FailShader = shader, FailLink = link };
		using var program = new MeshProgram(gl);
		InvalidOperationException failure = Assert.Throws<InvalidOperationException>(program.EnsureProgram);
		Assert.Contains("注入的 GPU 故障", failure.Message);
		Assert.Equal(gl.CreatedShaders.Order(), gl.DeletedShaders.Order());
		Assert.Equal(gl.CreatedPrograms.Order(), gl.DeletedPrograms.Order());
		gl.FailShader = -1;
		gl.FailLink = false;
		program.EnsureProgram();
		Assert.Equal(gl.CreatedShaders.Order(), gl.DeletedShaders.Order());
		Assert.Equal(gl.CreatedPrograms.Count - 1, gl.DeletedPrograms.Count);
		program.Dispose();
		program.Dispose();
		Assert.Equal(gl.CreatedPrograms.Order(), gl.DeletedPrograms.Order());
	}

	[Fact]
	public void 单模型只编译一个程序且不同实例不共享句柄()
	{
		var gl = new RecordingGlApi();
		using var first = new MeshProgram(gl);
		using var second = new MeshProgram(gl);
		first.EnsureProgram();
		first.EnsureProgram();
		Assert.Single(gl.CreatedPrograms);
		second.EnsureProgram();
		Assert.Equal(2, gl.CreatedPrograms.Count);
		Assert.Equal(4, gl.CreatedShaders.Count);
		Assert.Equal(gl.CreatedShaders.Order(), gl.DeletedShaders.Order());
		first.Dispose();
		Assert.Single(gl.DeletedPrograms);
		second.EnsureProgram();
		Assert.Equal(2, gl.CreatedPrograms.Count);
	}
}
