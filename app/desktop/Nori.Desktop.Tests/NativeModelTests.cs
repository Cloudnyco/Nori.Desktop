using System.Numerics;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

[Collection("Native settings")]
public sealed class NativeModelTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(4)]
	[InlineData(63)]
	[InlineData(64)]
	[InlineData(512)]
	public void 损坏输入在原生创建前明确失败(int size)
	{
		var bytes = new byte[size];
		if (size >= 4) "MOC3"u8.CopyTo(bytes);
		Assert.Throws<InvalidDataException>(() => new NativeModel(bytes));
	}

	[Live2DAssetsFact]
	public unsafe void 无需旧框架即可创建两份模型且数组独立()
	{
		foreach (byte[] bytes in Models())
		{
			byte[] original = (byte[])bytes.Clone();
			using var pet = new NativeModel(bytes);
			using var preview = new NativeModel(bytes);
			Assert.Equal(original, bytes);
			Assert.NotEqual((nint)pet.GetParameterValues(), (nint)preview.GetParameterValues());
			Assert.NotEqual((nint)pet.GetDrawableVertexPositions(0), (nint)preview.GetDrawableVertexPositions(0));
			Assert.Equal(0L, (long)pet.Memory.DangerousGetHandle() % 16);
			Assert.True(pet.CanvasSize.X > 0 && pet.CanvasSize.Y > 0 && pet.PixelsPerUnit > 0);
			Assert.True(pet.IsUsingMasking());
			int angle = pet.GetParameterIndex("ParamAngleX");
			float initial = preview.GetParameterValue(angle);
			pet.SetParameterValue(angle, initial == 20 ? -20 : 20);
			pet.Update();
			Assert.Equal(initial, preview.GetParameterValue(angle));
			pet.Dispose();
			preview.Update();
			Assert.True(pet.Memory.IsClosed);
			Assert.True(pet.Memory.IsInvalid);
			Assert.False(preview.IsDisposed);
		}
	}

	[Live2DAssetsFact]
	public unsafe void 参数权重范围快照与虚拟ID有独立存储()
	{
		foreach (byte[] bytes in Models())
		{
			using var model = new NativeModel(bytes);
			int angle = model.GetParameterIndex("ParamAngleX");
			float min = model.GetParameterMinimumValues()[angle];
			float max = model.GetParameterMaximumValues()[angle];
			model.SetParameterValue(angle, min - 100);
			Assert.Equal(min, model.GetParameterValue(angle));
			model.SetParameterValue(angle, max + 100, 0.25f);
			Assert.Equal(min * 0.75f + max * 0.25f, model.GetParameterValue(angle));
			model.SetParameterValue(angle, 4);
			model.AddParameterValue(angle, 4, 0.5f);
			model.MultiplyParameterValue(angle, 2, 0.5f);
			Assert.Equal(9, model.GetParameterValue(angle));
			model.SaveParameters();
			model.SetParameterValue(angle, 0);
			model.LoadParameters();
			Assert.Equal(9, model.GetParameterValue(angle));

			int count = model.ParameterCount;
			int missing = model.GetParameterIndex("不存在的动作参数");
			Assert.Equal(count, missing);
			Assert.Equal(missing, model.GetParameterIndex("不存在的动作参数"));
			Assert.Equal(count, model.ParameterCount);
			Assert.Equal(0, model.GetParameterDefaultValue(missing));
			model.SetParameterValue(missing, 100, 0.5f);
			model.AddParameterValue(missing, 10);
			model.MultiplyParameterValue(missing, 2);
			Assert.Equal(120, model.GetParameterValue(missing));
			model.LoadParameters();
			Assert.Equal(120, model.GetParameterValue(missing));
			Assert.Equal(9, model.GetParameterValue(angle));
			int part = model.GetPartIndex("不存在的姿势部件");
			Assert.Equal(model.PartCount, part);
			Assert.Equal(0, model.GetPartOpacity(part));
			model.SetPartOpacity(part, 0.4f);
			Assert.Equal(0.4f, model.GetPartOpacity(part));
			Assert.Equal(-1, model.GetDrawableIndex("不存在的绘制对象"));
			GC.KeepAlive(model);
		}
	}

	[Live2DAssetsFact]
	public unsafe void 越界及释放后访问不能触及原生内存()
	{
		using var model = new NativeModel(Models().First());
		Assert.Throws<ArgumentOutOfRangeException>(() => model.GetParameterValue(-1));
		Assert.Throws<ArgumentOutOfRangeException>(() => model.SetParameterValue(model.ParameterCount, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => model.GetPartOpacity(model.PartCount));
		Assert.Throws<ArgumentOutOfRangeException>(() => model.SetPartOpacity(-1, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => model.GetDrawableVertexPositions(-1));
		Assert.Throws<ArgumentOutOfRangeException>(() => model.GetDrawableOpacity(model.DrawableCount));
		model.Dispose();
		model.Dispose();
		Assert.Throws<ObjectDisposedException>(() => model.Update());
		Assert.Throws<ObjectDisposedException>(() => model.GetParameterIndex("ParamAngleX"));
		Assert.Throws<ObjectDisposedException>(() => model.GetParameterValue(0));
		Assert.Throws<ObjectDisposedException>(() => model.SetParameterValue(0, 1));
		Assert.Throws<ObjectDisposedException>(() => model.GetParameterValues());
		Assert.Throws<ObjectDisposedException>(() => model.GetDrawableVertexIndices(0));
		Assert.Throws<ObjectDisposedException>(() => model.GetDrawableMasks());
		Assert.Throws<ObjectDisposedException>(() => model.GetPartOpacity(0));
		Assert.Throws<ObjectDisposedException>(() => model.SaveParameters());
		Assert.Throws<ObjectDisposedException>(() => model.LoadParameters());
		Assert.Throws<ObjectDisposedException>(() => model.Opacity);
	}

	[Live2DAssetsFact]
	public unsafe void 缓存指针更新后与原生接口一致且计数只统计成功更新()
	{
		using NativeModel model = new(File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("arg-nori", "ARGNori.moc3")));
		Assert.Equal(0, model.UpdateCount);
		for (int frame = 0; frame < 4; frame++)
		{
			Assert.Equal((nint)NativeMethods.GetParameterValues(model.Memory), (nint)model.GetParameterValues());
			Assert.Equal((nint)NativeMethods.GetParameterMinimumValues(model.Memory), (nint)model.GetParameterMinimumValues());
			Assert.Equal((nint)NativeMethods.GetParameterMaximumValues(model.Memory), (nint)model.GetParameterMaximumValues());
			Assert.Equal((nint)NativeMethods.GetParameterDefaultValues(model.Memory), (nint)model.GetParameterDefaultValues());
			Assert.Equal((nint)NativeMethods.GetRenderOrders(model.Memory), (nint)model.GetDrawableRenderOrders());
			Assert.Equal((nint)NativeMethods.GetDrawableMaskCounts(model.Memory), (nint)model.GetDrawableMaskCounts());
			Assert.Equal((nint)NativeMethods.GetDrawableMasks(model.Memory), (nint)model.GetDrawableMasks());
			Assert.Equal(model.ParameterCount, NativeMethods.GetParameterCount(model.Memory));
			Assert.Equal(model.PartCount, NativeMethods.GetPartCount(model.Memory));
			Assert.Equal(model.DrawableCount, NativeMethods.GetDrawableCount(model.Memory));
			for (int i = 0; i < model.ParameterCount; i++)
			{
				Assert.Equal(NativeMethods.GetParameterValues(model.Memory)[i], model.GetParameterValue(i));
				Assert.Equal(NativeMethods.GetParameterMinimumValues(model.Memory)[i], model.GetParameterMinimumValues()[i]);
				Assert.Equal(NativeMethods.GetParameterMaximumValues(model.Memory)[i], model.GetParameterMaximumValues()[i]);
				Assert.Equal(NativeMethods.GetParameterDefaultValues(model.Memory)[i], model.GetParameterDefaultValue(i));
			}
			for (int i = 0; i < model.PartCount; i++)
				Assert.Equal(NativeMethods.GetPartOpacities(model.Memory)[i], model.GetPartOpacity(i));
			for (int i = 0; i < model.DrawableCount; i++)
			{
				Assert.Equal(NativeMethods.GetDrawableTextureIndices(model.Memory)[i], model.GetDrawableTextureIndex(i));
				Assert.Equal(NativeMethods.GetDrawableVertexCounts(model.Memory)[i], model.GetDrawableVertexCount(i));
				Assert.Equal(NativeMethods.GetDrawableIndexCounts(model.Memory)[i], model.GetDrawableVertexIndexCount(i));
				Assert.Equal(NativeMethods.GetDrawableOpacities(model.Memory)[i], model.GetDrawableOpacity(i));
				Assert.Equal(NativeMethods.GetDrawableMultiplyColors(model.Memory)[i], model.GetMultiplyColor(i));
				Assert.Equal(NativeMethods.GetDrawableScreenColors(model.Memory)[i], model.GetScreenColor(i));
				Assert.Equal(NativeMethods.GetDrawableConstantFlags(model.Memory)[i], model.GetDrawableConstantFlags(i));
				Assert.Equal(NativeMethods.GetDrawableDynamicFlags(model.Memory)[i], model.GetDrawableDynamicFlags(i));
				Assert.Equal((nint)NativeMethods.GetDrawableVertexPositions(model.Memory)[i], (nint)model.GetDrawableVertexPositions(i));
				Assert.Equal((nint)NativeMethods.GetDrawableVertexUvs(model.Memory)[i], (nint)model.GetDrawableVertexUvs(i));
				Assert.Equal((nint)NativeMethods.GetDrawableIndices(model.Memory)[i], (nint)model.GetDrawableVertexIndices(i));
			}

			if (frame < 3)
			{
				model.SetParameterValue(0, model.GetParameterValue(0) + 0.01f);
				model.Update();
				Assert.Equal(frame + 1, model.UpdateCount);
			}
		}
		model.Dispose();
		Assert.Throws<ObjectDisposedException>(() => model.UpdateCount);
	}

	[Live2DAssetsFact]
	public unsafe void 两个真实模型更新后静态绘制数据不变且索引合法()
	{
		foreach ((string id, string moc) in new[] { ("arg-nori", "ARGNori.moc3"), ("nori", "Nori.moc3") })
		{
			using NativeModel model = new(File.ReadAllBytes(PreparedModelAssetsTests.FindFixture(id, moc)));
			int count = model.DrawableCount;
			int[] vertexCounts = new int[count], indexCounts = new int[count], textures = new int[count];
			byte[] flags = new byte[count];
			Vector2[][] uvs = new Vector2[count][];
			ushort[][] indices = new ushort[count][];
			for (int drawable = 0; drawable < count; drawable++)
			{
				vertexCounts[drawable] = model.GetDrawableVertexCount(drawable);
				indexCounts[drawable] = model.GetDrawableVertexIndexCount(drawable);
				textures[drawable] = model.GetDrawableTextureIndex(drawable);
				flags[drawable] = model.GetDrawableConstantFlags(drawable);
				uvs[drawable] = new ReadOnlySpan<Vector2>(model.GetDrawableVertexUvs(drawable), vertexCounts[drawable]).ToArray();
				indices[drawable] = new ReadOnlySpan<ushort>(model.GetDrawableVertexIndices(drawable), indexCounts[drawable]).ToArray();
				Assert.All(indices[drawable], index => Assert.True(index < vertexCounts[drawable]));
			}
			for (int frame = 0; frame < 120; frame++)
			{
				model.SetParameterValue(frame % model.ParameterCount, (frame % 31) - 15);
				model.Update();
			}
			for (int drawable = 0; drawable < count; drawable++)
			{
				Assert.Equal(vertexCounts[drawable], model.GetDrawableVertexCount(drawable));
				Assert.Equal(indexCounts[drawable], model.GetDrawableVertexIndexCount(drawable));
				Assert.Equal(textures[drawable], model.GetDrawableTextureIndex(drawable));
				Assert.Equal(flags[drawable], model.GetDrawableConstantFlags(drawable));
				Assert.Equal(uvs[drawable], new ReadOnlySpan<Vector2>(model.GetDrawableVertexUvs(drawable), vertexCounts[drawable]).ToArray());
				Assert.Equal(indices[drawable], new ReadOnlySpan<ushort>(model.GetDrawableVertexIndices(drawable), indexCounts[drawable]).ToArray());
			}
		}
	}

	[Live2DAssetsFact]
	public void 原生一致性检查拒绝截断模型且失败后仍可创建和释放()
	{
		foreach (byte[] bytes in Models())
		{
			foreach (int length in new[] { 64, 512, bytes.Length / 2, bytes.Length - 1 })
			{
				byte[] truncated = bytes[..length];
				InvalidDataException error = Assert.Throws<InvalidDataException>(() => new NativeModel(truncated));
				Assert.Contains("一致性检查", error.Message, StringComparison.Ordinal);
			}
			using var good = new NativeModel(bytes);
			good.Update();
			good.Dispose();
			Assert.True(good.Memory.IsClosed);
			Assert.True(good.Memory.IsInvalid);
			Assert.Throws<ObjectDisposedException>(() => good.Update());
		}
	}

	private static IEnumerable<byte[]> Models()
	{
		yield return File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("arg-nori", "ARGNori.moc3"));
		yield return File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("nori", "Nori.moc3"));
	}
}
