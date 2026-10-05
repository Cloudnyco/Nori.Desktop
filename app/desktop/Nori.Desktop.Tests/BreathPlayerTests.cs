using Nori.Live2D;

namespace Nori.Desktop.Tests;

public sealed class BreathPlayerTests
{
	private static NativeModel Model() => new(File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("nori", "Nori.moc3")));

	[Live2DAssetsFact]
	public void 周期偏移与权重叠加在当前参数且实例独立()
	{
		using NativeModel first = Model();
		using NativeModel second = Model();
		int index = first.GetParameterIndex("TestBreath");
		int other = second.GetParameterIndex("TestBreath");
		var player = new BreathPlayer(first, ("TestBreath", 2, 4, 4, 0.5f));
		var another = new BreathPlayer(second, ("TestBreath", 2, 4, 4, 0.5f));
		float[] expected = [13, 11, 9, 11];
		foreach (float value in expected)
		{
			first.SetParameterValue(index, 10);
			player.Update(1);
			Assert.Equal(value, first.GetParameterValue(index), 5);
		}
		another.Update(0);
		Assert.Equal(1, second.GetParameterValue(other), 5);
	}

	[Live2DAssetsFact]
	public void 呼吸对原生参数遵守边界且帧率无关()
	{
		using NativeModel first = Model();
		using NativeModel second = Model();
		int a = first.GetParameterIndex("ParamAngleX"), b = second.GetParameterIndex("ParamAngleX");
		var slow = new BreathPlayer(first, ("ParamAngleX", 0, 15, 6.5345f, 0.5f));
		var fast = new BreathPlayer(second, ("ParamAngleX", 0, 15, 6.5345f, 0.5f));
		for (int frame = 0; frame < 60; frame++)
		{
			first.SetParameterValue(a, 5);
			slow.Update(1 / 60f);
			for (int step = 0; step < 2; step++)
			{
				second.SetParameterValue(b, 5);
				fast.Update(1 / 120f);
			}
			Assert.Equal(first.GetParameterValue(a), second.GetParameterValue(b), 5);
		}
		first.SetParameterValue(a, 30);
		new BreathPlayer(first, ("ParamAngleX", 100, 0, 1, 1)).Update(0);
		Assert.Equal(30, first.GetParameterValue(a));
	}

	[Live2DAssetsFact]
	public void 拒绝无效周期权重与时间()
	{
		using NativeModel model = Model();
		foreach (float period in new[] { 0, -1, float.PositiveInfinity, float.NaN })
			Assert.Throws<ArgumentException>(() => new BreathPlayer(model, ("Test", 0, 1, period, 1)));
		foreach (float weight in new[] { -1, 2, float.NaN })
			Assert.Throws<ArgumentException>(() => new BreathPlayer(model, ("Test", 0, 1, 1, weight)));
		var player = new BreathPlayer(model);
		foreach (float time in new[] { -1, float.PositiveInfinity, float.NaN })
			Assert.Throws<ArgumentOutOfRangeException>(() => player.Update(time));
	}
}
