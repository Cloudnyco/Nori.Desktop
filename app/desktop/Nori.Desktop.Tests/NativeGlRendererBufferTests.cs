using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Nori.Desktop.Live2D;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

[Collection("Native settings")]
public sealed class NativeGlRendererBufferTests
{
	[Theory]
	[InlineData(60000, 5535, 0, 65536, 0)]
	[InlineData(60000, 5536, 0, 65536, 60000)]
	[InlineData(60000, 65536, 0, 65536, 60000)]
	[InlineData(125536, 100, 60000, 65536, 125536)]
	[InlineData(64, 64, 0, 128, 0)]
	[InlineData(128, 1, 0, 128, 128)]
	public void 分段容量边界不引入固定重启索引(int vertexBase, int count, int previousBase, int limit, int expectedBase)
	{
		int segmentBase = NativeGlRenderer.GetSegmentBase(vertexBase, count, previousBase, limit);
		Assert.Equal(expectedBase, segmentBase);
		int relocated = checked((ushort)(count - 1 + vertexBase - segmentBase));
		if (count < 65536) Assert.True(relocated < ushort.MaxValue);
		else Assert.Equal(ushort.MaxValue, relocated);
	}

	[Live2DAssetsFact]
	public void 三个缓冲由每个渲染器独占且仅释放一次()
	{
		var gl = new RecordingGlApi { CreateResources = true };
		using var first = new NativeModelHost(gl, Assets("arg-nori"));
		using var second = new NativeModelHost(gl, Assets("nori"));
		int[] firstBuffers = Buffers(first.Renderer), secondBuffers = Buffers(second.Renderer);
		Assert.Equal(3, firstBuffers.Distinct().Count());
		Assert.Equal(3, secondBuffers.Distinct().Count());
		Assert.Empty(firstBuffers.Intersect(secondBuffers));
		Assert.Equal(6, gl.CreatedBuffers.Count);
		for (int frame = 0; frame < 3; frame++)
		{
			first.Model.Update();
			second.Model.Update();
			first.Draw(Matrix4x4.Identity);
			second.Draw(Matrix4x4.Identity);
		}
		Assert.Equal(6, gl.CreatedBuffers.Count);
		first.Dispose();
		first.Dispose();
		Assert.Equal(firstBuffers.Order(), gl.DeletedBuffers.Order());
		Assert.All(Buffers(first.Renderer), handle => Assert.Equal(0, handle));
		second.Dispose();
		second.Dispose();
		Assert.Equal(gl.CreatedBuffers.Order(), gl.DeletedBuffers.Order());
		Assert.Equal(6, gl.DeletedBuffers.Distinct().Count());
		Assert.All(Buffers(second.Renderer), handle => Assert.Equal(0, handle));
	}

	[Live2DAssetsFact]
	public unsafe void 静态数据只上传一次且每次模型更新仅上传全部位置一次()
	{
		foreach (string name in new[] { "arg-nori", "nori" })
		{
			var gl = new RecordingGlApi { CreateResources = true, CaptureBufferContents = true };
			using var host = new NativeModelHost(gl, Assets(name));
			int[] buffers = Buffers(host.Renderer);
			Assert.Equal(2, gl.BufferUploads.Count);
			Assert.All(gl.BufferUploads, upload => Assert.Equal(gl.GL_STATIC_DRAW, upload.Usage));
			Assert.Equal(buffers[1], Assert.Single(gl.BufferUploads, upload => upload.Target == gl.GL_ARRAY_BUFFER).Buffer);
			Assert.Equal(buffers[2], Assert.Single(gl.BufferUploads, upload => upload.Target == gl.GL_ELEMENT_ARRAY_BUFFER).Buffer);
			gl.BufferUploads.Clear();
			// 尚未 Update 的首次绘制也必须提供位置数据。
			CheckUpload();
			for (int frame = 0; frame < 3; frame++)
			{
				long before = host.Model.UpdateCount;
				host.Model.SetParameterValue(host.Model.GetParameterIndex("ParamAngleX"), frame * 10);
				host.Model.Update();
				Assert.Equal(before + 1, host.Model.UpdateCount);
				CheckUpload();
			}
			Assert.Equal(3, gl.CreatedBuffers.Count);

			void CheckUpload()
			{
				host.Draw(Matrix4x4.Identity);
				var upload = Assert.Single(gl.BufferUploads);
				Assert.Equal(gl.GL_ARRAY_BUFFER, upload.Target);
				Assert.Equal(gl.GL_DYNAMIC_DRAW, upload.Usage);
				Assert.Equal(buffers[0], upload.Buffer);
				ReadOnlySpan<Vector2> positions = MemoryMarshal.Cast<byte, Vector2>(upload.Data);
				int vertex = 0;
				for (int i = 0; i < host.Model.DrawableCount; i++)
				for (int v = 0; v < host.Model.GetDrawableVertexCount(i); v++)
					Assert.Equal(host.Model.GetDrawableVertexPositions(i)[v], positions[vertex++]);
				Assert.Equal(vertex * 8, upload.Size);
				gl.BufferUploads.Clear();
				host.Draw(Matrix4x4.Identity);
				Assert.Empty(gl.BufferUploads);
			}
		}
	}

	[Live2DAssetsFact]
	public unsafe void 多段索引重定位与属性偏移正确且只在段切换时重设()
	{
		var gl = new RecordingGlApi { CreateResources = true, CaptureBufferContents = true };
		PreparedModelData assets = Assets("arg-nori");
		using var model = new NativeModel(assets.Moc.Span);
		int limit = Enumerable.Range(0, model.DrawableCount).Max(model.GetDrawableVertexCount) * 2;
		using var renderer = new NativeGlRenderer(gl, model, assets.Textures, limit);
		ReadOnlySpan<ushort> indices = MemoryMarshal.Cast<byte, ushort>(Assert.Single(gl.BufferUploads, upload => upload.Target == gl.GL_ELEMENT_ARRAY_BUFFER).Data);
		ReadOnlySpan<Vector2> uvs = MemoryMarshal.Cast<byte, Vector2>(Assert.Single(gl.BufferUploads, upload => upload.Target == gl.GL_ARRAY_BUFFER).Data);
		int vertexBase = 0, segmentBase = 0, element = 0;
		var expectedSegments = new int[model.DrawableCount];
		for (int i = 0; i < model.DrawableCount; i++)
		{
			int count = model.GetDrawableVertexCount(i);
			if (vertexBase - segmentBase + count > limit) segmentBase = vertexBase;
			expectedSegments[i] = segmentBase;
			Assert.InRange(vertexBase - segmentBase + count, 0, limit);
			for (int j = 0; j < model.GetDrawableVertexIndexCount(i); j++)
				Assert.Equal(model.GetDrawableVertexIndices(i)[j] + vertexBase - segmentBase, (int)indices[element++]);
			for (int v = 0; v < count; v++) Assert.Equal(model.GetDrawableVertexUvs(i)[v], uvs[vertexBase + v]);
			vertexBase += count;
		}
		Assert.Equal(element, indices.Length);
		Assert.Equal(vertexBase, uvs.Length);
		Assert.True(expectedSegments.Distinct().Count() > 1);
		Assert.Equal(2, gl.AttributePointers.Count);
		gl.AttributePointers.Clear();
		gl.BufferUploads.Clear();
		renderer.PreDraw();
		int bound = 0, indexOffset = 0, switches = 0;
		for (int i = 0; i < model.DrawableCount; i++)
		{
			int before = gl.AttributePointers.Count;
			renderer.DrawMesh(i);
			int changes = bound == expectedSegments[i] ? 0 : 2;
			Assert.Equal(before + changes, gl.AttributePointers.Count);
			if (changes != 0)
			{
				int[] buffers = Buffers(renderer);
				Assert.Equal(buffers[0], gl.AttributePointers[^2].Buffer);
				Assert.Equal(buffers[1], gl.AttributePointers[^1].Buffer);
				Assert.Equal(0, gl.AttributePointers[^2].Attribute);
				Assert.Equal(1, gl.AttributePointers[^1].Attribute);
				Assert.All(gl.AttributePointers.TakeLast(2), pointer =>
				{
					Assert.Equal((nint)(expectedSegments[i] * 8), pointer.Offset);
					Assert.Equal(8, pointer.Stride);
				});
				switches++;
			}
			Assert.Equal((nint)indexOffset, gl.ElementDraws[^1].Offset);
			Assert.Equal(model.GetDrawableVertexIndexCount(i), gl.ElementDraws[^1].Count);
			bound = expectedSegments[i];
			before = gl.AttributePointers.Count;
			renderer.DrawMesh(i);
			Assert.Equal(before, gl.AttributePointers.Count);
			indexOffset += model.GetDrawableVertexIndexCount(i) * 2;
		}
		Assert.Equal(switches * 2, gl.AttributePointers.Count);
		// pass 重置不改变 VAO 属性；回到首段时才重设。
		renderer.PreDraw();
		renderer.DrawMesh(model.DrawableCount - 1);
		Assert.Equal(switches * 2, gl.AttributePointers.Count);
		renderer.DrawMesh(0);
		Assert.Equal(switches * 2 + 2, gl.AttributePointers.Count);
		Assert.Empty(gl.BufferUploads);
	}

	[Live2DAssetsFact]
	public unsafe void 单网格超过段容量时独占一段且保留原索引()
	{
		PreparedModelData assets = Assets("arg-nori");
		using var model = new NativeModel(assets.Moc.Span);
		int largest = Enumerable.Range(0, model.DrawableCount).MaxBy(model.GetDrawableVertexCount);
		int vertexBase = 0, element = 0;
		for (int i = 0; i < largest; i++)
		{
			vertexBase += model.GetDrawableVertexCount(i);
			element += model.GetDrawableVertexIndexCount(i);
		}
		var gl = new RecordingGlApi { CreateResources = true, CaptureBufferContents = true };
		using (var renderer = new NativeGlRenderer(gl, model, assets.Textures, model.GetDrawableVertexCount(largest) - 1))
		{
			ReadOnlySpan<ushort> indices = MemoryMarshal.Cast<byte, ushort>(
				Assert.Single(gl.BufferUploads, upload => upload.Target == gl.GL_ELEMENT_ARRAY_BUFFER).Data);
			// 超出段容量的网格从自身顶点基址开始新段，索引不需要重定位。
			for (int j = 0; j < model.GetDrawableVertexIndexCount(largest); j++)
				Assert.Equal(model.GetDrawableVertexIndices(largest)[j], indices[element + j]);
			gl.AttributePointers.Clear();
			renderer.PreDraw();
			renderer.DrawMesh(largest);
			if (vertexBase != 0)
				Assert.All(gl.AttributePointers.TakeLast(2), pointer => Assert.Equal((nint)(vertexBase * 8), pointer.Offset));
			Assert.Equal(model.GetDrawableVertexIndexCount(largest), gl.ElementDraws[^1].Count);
		}
		Assert.Equal(gl.CreatedBuffers.Order(), gl.DeletedBuffers.Order());
	}

	[Live2DAssetsFact]
	public unsafe void 越界索引在构造时拒绝且回滚全部GL资源()
	{
		PreparedModelData assets = Assets("arg-nori");
		using var model = new NativeModel(assets.Moc.Span);
		int drawable = Enumerable.Range(0, model.DrawableCount).First(i => model.GetDrawableVertexIndexCount(i) > 0);
		ushort* indices = model.GetDrawableVertexIndices(drawable);
		ushort saved = indices[0];
		var gl = new RecordingGlApi { CreateResources = true };
		try
		{
			indices[0] = checked((ushort)model.GetDrawableVertexCount(drawable));
			Assert.Contains("索引", Assert.Throws<InvalidDataException>(() => new NativeGlRenderer(gl, model, assets.Textures)).Message);
			Assert.Empty(gl.BufferUploads);
			Assert.Equal(gl.CreatedBuffers.Order(), gl.DeletedBuffers.Order());
			Assert.Equal(gl.CreatedVertexArrays, gl.DeletedVertexArrays);
			Assert.Equal(gl.CreatedTextures, gl.DeletedTextures);
		}
		finally { indices[0] = saved; }
	}

	internal static PreparedModelData Assets(string name, bool withTextures = true) => new(name,
		ModelDefinition.Parse("{\"FileReferences\":{\"Moc\":\"模型.moc3\"}}"u8.ToArray()),
		File.ReadAllBytes(PreparedModelAssetsTests.FindFixture(name, name == "arg-nori" ? "ARGNori.moc3" : "Nori.moc3")),
		null, null, new Dictionary<string, MotionClip>(), withTextures
			? Enumerable.Range(0, name == "arg-nori" ? 3 : 1)
				.Select(slot => new PreparedTexture(slot, $"{slot}.png", new(1, 1, [255, 255, 255, 255]))).ToArray() : []);

	internal static int[] Buffers(NativeGlRenderer renderer) => new[] { "_vertices", "_uvs", "_indices" }
		.Select(name => (int)typeof(NativeGlRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!).ToArray();
}
