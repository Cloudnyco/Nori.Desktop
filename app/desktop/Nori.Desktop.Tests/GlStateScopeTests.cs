using Nori.Desktop.Live2D.Gl;

namespace Nori.Desktop.Tests;

public sealed class GlStateScopeTests
{
	[Theory]
	[InlineData(false, true)]
	[InlineData(true, true)]
	[InlineData(true, false)]
	public void CapturesWithoutChangingStateAndRestoresAfterDispose(bool es2, bool vao)
	{
		var gl = CreateHost(es2, vao);
		var expected = Capture(gl);
		int attributeQueries = gl.VertexAttributeQueries;
		var scope = new GlStateScope(gl);
		Assert.Equal(vao ? 0 : 4, gl.VertexAttributeQueries - attributeQueries);
		Assert.Equal(31, scope.Framebuffer);
		Assert.Equal(new[] { 3, 5, 720, 480 }, scope.Viewport);
		AssertState(expected, Capture(gl));

		ChangeForDraw(gl);
		gl.StateCalls.Clear();
		scope.Dispose();
		AssertState(expected, Capture(gl));
		if (vao)
		{
			Assert.Contains("vao:11", gl.StateCalls);
			Assert.DoesNotContain(gl.StateCalls, call => call.StartsWith($"buffer:{gl.GL_ELEMENT_ARRAY_BUFFER}:", StringComparison.Ordinal));
			// 恢复宿主 VAO 后其 EBO 应保持原绑定，不能向宿主 VAO 再写入。
			gl.BindVertexArray(11);
			gl.GetIntegerv(gl.GL_ELEMENT_ARRAY_BUFFER_BINDING, out int hostEbo);
			Assert.Equal(13, hostEbo);
			gl.BindVertexArray(21);
			gl.GetIntegerv(gl.GL_ELEMENT_ARRAY_BUFFER_BINDING, out int drawEbo);
			Assert.Equal(23, drawEbo);
		}
		else
		{
			Assert.DoesNotContain(gl.GL_VERTEX_ARRAY_BINDING, gl.IntegerQueries);
			Assert.DoesNotContain(gl.StateCalls, call => call.StartsWith("vao:", StringComparison.Ordinal));
		}
		if (es2) Assert.DoesNotContain(0x8CAA, gl.IntegerQueries);
	}

	[Theory]
	[InlineData(false, true)]
	[InlineData(true, true)]
	[InlineData(true, false)]
	public void RestoresStateWhenDrawThrows(bool es2, bool vao)
	{
		var gl = CreateHost(es2, vao);
		var expected = Capture(gl);
		Action draw = () =>
		{
			using var scope = new GlStateScope(gl);
			ChangeForDraw(gl);
			throw new InvalidOperationException("注入绘制失败");
		};
		Assert.Throws<InvalidOperationException>(draw);
		AssertState(expected, Capture(gl));
	}

	[Fact]
	public void NestedScopesRestoreTheirOwnEntryStateAndDisposeIsIdempotent()
	{
		var gl = CreateHost(false, true);
		var host = Capture(gl);
		using (var outer = new GlStateScope(gl))
		{
			ChangeForDraw(gl);
			gl.BindVertexArray(31);
			gl.BindBuffer(gl.GL_ELEMENT_ARRAY_BUFFER, 97);
			var innerEntry = Capture(gl);
			var inner = new GlStateScope(gl);
			gl.UseProgram(99);
			gl.BindBuffer(gl.GL_ELEMENT_ARRAY_BUFFER, 98);
			gl.ClearColor(0, 0, 0, 0);
			inner.Dispose();
			AssertState(innerEntry, Capture(gl));
			gl.UseProgram(97);
			inner.Dispose();
			gl.GetIntegerv(gl.GL_CURRENT_PROGRAM, out int program);
			Assert.Equal(97, program);
		}
		AssertState(host, Capture(gl));
	}

	private static RecordingGlApi CreateHost(bool es2, bool vao)
	{
		var gl = new RecordingGlApi { Es2Context = es2, VertexArraysSupported = vao };
		gl.UseProgram(7);
		if (vao) gl.BindVertexArray(11);
		gl.BindBuffer(gl.GL_ARRAY_BUFFER, 12);
		gl.BindBuffer(gl.GL_ELEMENT_ARRAY_BUFFER, 13);
		gl.EnableVertexAttribArray(0);
		gl.EnableVertexAttribArray(2);
		gl.BindFramebuffer(gl.GL_FRAMEBUFFER, 31);
		if (!es2) gl.BindFramebuffer(0x8CA8, 32);
		gl.Viewport(3, 5, 720, 480);
		gl.ActiveTexture(gl.GL_TEXTURE0);
		gl.BindTexture(gl.GL_TEXTURE_2D, 41);
		gl.ActiveTexture(gl.GL_TEXTURE1);
		gl.BindTexture(gl.GL_TEXTURE_2D, 42);
		// 入口可以是其他单元；绘制只允许修改 0/1。
		gl.ActiveTexture(gl.GL_TEXTURE0 + 2);
		gl.BindTexture(gl.GL_TEXTURE_2D, 43);
		gl.BlendFuncSeparate(gl.GL_SRC_ALPHA, gl.GL_ONE_MINUS_SRC_ALPHA, gl.GL_ZERO, gl.GL_ONE);
		gl.BlendEquationSeparate(0x800A, 0x800B);
		gl.Enable(gl.GL_DEPTH_TEST);
		gl.Enable(gl.GL_SCISSOR_TEST);
		gl.Enable(gl.GL_CULL_FACE);
		gl.FrontFace(0x0900);
		gl.ColorMask(false, true, false, true);
		gl.ClearColor(0.125f, 0.25f, 0.5f, 0.75f);
		return gl;
	}

	private static void ChangeForDraw(RecordingGlApi gl)
	{
		gl.UseProgram(17);
		if (gl.SupportsVertexArrayObjects) gl.BindVertexArray(21);
		gl.BindBuffer(gl.GL_ARRAY_BUFFER, 22);
		gl.BindBuffer(gl.GL_ELEMENT_ARRAY_BUFFER, 23);
		gl.DisableVertexAttribArray(0);
		gl.EnableVertexAttribArray(1);
		gl.DisableVertexAttribArray(2);
		gl.EnableVertexAttribArray(3);
		gl.BindFramebuffer(gl.GL_FRAMEBUFFER, 51);
		gl.Viewport(0, 0, 256, 256);
		gl.ActiveTexture(gl.GL_TEXTURE0);
		gl.BindTexture(gl.GL_TEXTURE_2D, 61);
		gl.ActiveTexture(gl.GL_TEXTURE1);
		gl.BindTexture(gl.GL_TEXTURE_2D, 62);
		gl.BlendFuncSeparate(gl.GL_ONE, gl.GL_ZERO, gl.GL_ONE, gl.GL_ZERO);
		gl.BlendEquationSeparate(0x8006, 0x8006);
		gl.Enable(gl.GL_BLEND);
		gl.Disable(gl.GL_DEPTH_TEST);
		gl.Enable(gl.GL_STENCIL_TEST);
		gl.Disable(gl.GL_SCISSOR_TEST);
		gl.Disable(gl.GL_CULL_FACE);
		gl.FrontFace(gl.GL_CCW);
		gl.ColorMask(true, true, true, true);
		gl.ClearColor(1, 1, 1, 1);
	}

	private sealed record State(int[] Integers, int[] Viewport, bool[] Enabled, bool[] ColorMask, float[] ClearColor, int[] Attributes);

	private static State Capture(RecordingGlApi gl)
	{
		int[] parameters = [gl.GL_CURRENT_PROGRAM, gl.GL_ARRAY_BUFFER_BINDING, gl.GL_ELEMENT_ARRAY_BUFFER_BINDING,
			gl.GL_FRAMEBUFFER_BINDING, gl.GL_ACTIVE_TEXTURE, gl.GL_BLEND_SRC_RGB, gl.GL_BLEND_DST_RGB,
			gl.GL_BLEND_SRC_ALPHA, gl.GL_BLEND_DST_ALPHA, gl.GL_BLEND_EQUATION_RGB, gl.GL_BLEND_EQUATION_ALPHA, gl.GL_FRONT_FACE];
		var values = new List<int>();
		foreach (int parameter in parameters)
		{
			gl.GetIntegerv(parameter, out int value);
			values.Add(value);
		}
		if (gl.SupportsVertexArrayObjects)
		{
			gl.GetIntegerv(gl.GL_VERTEX_ARRAY_BINDING, out int vao);
			values.Add(vao);
		}
		if (!gl.IsES2)
		{
			gl.GetIntegerv(0x8CAA, out int readFramebuffer);
			values.Add(readFramebuffer);
		}
		gl.GetIntegerv(gl.GL_ACTIVE_TEXTURE, out int active);
		try
		{
			for (int i = 0; i < 3; i++)
			{
				gl.ActiveTexture(gl.GL_TEXTURE0 + i);
				gl.GetIntegerv(gl.GL_TEXTURE_BINDING_2D, out int texture);
				values.Add(texture);
			}
		}
		finally { gl.ActiveTexture(active); }
		int[] viewport = new int[4];
		bool[] mask = new bool[4];
		float[] clear = new float[4];
		int[] attributes = new int[4];
		gl.GetIntegerv(gl.GL_VIEWPORT, viewport);
		gl.GetBooleanv(gl.GL_COLOR_WRITEMASK, mask);
		gl.GetFloatv(gl.GL_COLOR_CLEAR_VALUE, clear);
		for (int i = 0; i < attributes.Length; i++)
			gl.GetVertexAttribiv(i, gl.GL_VERTEX_ATTRIB_ARRAY_ENABLED, out attributes[i]);
		bool[] enabled = [gl.IsEnabled(gl.GL_BLEND), gl.IsEnabled(gl.GL_DEPTH_TEST), gl.IsEnabled(gl.GL_STENCIL_TEST),
			gl.IsEnabled(gl.GL_SCISSOR_TEST), gl.IsEnabled(gl.GL_CULL_FACE)];
		return new(values.ToArray(), viewport, enabled, mask, clear, attributes);
	}

	private static void AssertState(State expected, State actual)
	{
		Assert.Equal(expected.Integers, actual.Integers);
		Assert.Equal(expected.Viewport, actual.Viewport);
		Assert.Equal(expected.Enabled, actual.Enabled);
		Assert.Equal(expected.ColorMask, actual.ColorMask);
		Assert.Equal(expected.ClearColor, actual.ClearColor);
		Assert.Equal(expected.Attributes, actual.Attributes);
	}
}
