using System.Numerics;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

public sealed class PointerSmootherTests
{
	[Fact]
	public void 静止输入保持零且台阶平滑单调收敛()
	{
		var smoother = new PointerSmoother();
		smoother.Update(1);
		Assert.Equal(Vector2.Zero, smoother.Position);
		smoother.SetTarget(new(1, -1));
		float previous = 0;
		for (int i = 0; i < 120; i++)
		{
			smoother.Update(1 / 60f);
			Assert.InRange(smoother.Position.X, previous - 0.000001f, 1.000001f);
			Assert.Equal(-smoother.Position.X, smoother.Position.Y);
			previous = smoother.Position.X;
		}
		Assert.InRange(smoother.Position.X, 0.9999f, 1.000001f);
	}

	[Fact]
	public void 恒定目标的结果不依赖分帧且模型间状态独立()
	{
		var first = new PointerSmoother();
		var second = new PointerSmoother();
		first.SetTarget(new(0.8f, -0.4f));
		second.SetTarget(new(0.8f, -0.4f));
		first.Update(0.5f);
		for (int i = 0; i < 60; i++) second.Update(1 / 120f);
		Assert.InRange(Vector2.Distance(first.Position, second.Position), 0, 0.000001f);
		Vector2 unchanged = second.Position;
		first.SetTarget(new(-1, 1));
		first.Update(0.1f);
		Assert.Equal(unchanged, second.Position);
		Assert.True(first.Position.X < unchanged.X);
	}

	[Fact]
	public void 暂停恢复与无效输入处理()
	{
		var smoother = new PointerSmoother();
		smoother.SetTarget(new(1, -1));
		smoother.Update(0);
		Assert.Equal(Vector2.Zero, smoother.Position);
		smoother.Update(float.MaxValue);
		Assert.Equal(new Vector2(1, -1), smoother.Position);
		foreach (float value in new[] { -1, float.NaN, float.PositiveInfinity })
			Assert.Throws<ArgumentOutOfRangeException>(() => smoother.Update(value));
		Assert.Throws<ArgumentOutOfRangeException>(() => smoother.SetTarget(new(float.NaN, 0)));
	}
}
