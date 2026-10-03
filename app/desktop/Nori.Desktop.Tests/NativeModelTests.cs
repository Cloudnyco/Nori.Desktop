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
