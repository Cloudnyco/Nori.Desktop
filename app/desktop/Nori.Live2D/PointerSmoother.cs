using System.Numerics;

namespace Nori.Live2D;

/// <summary>解析积分的临界阻尼跟踪器；窗口拖拽输入不依赖渲染帧率。</summary>
public sealed class PointerSmoother
{
	private const float Frequency = 12;
	private Vector2 _target;
	private Vector2 _velocity;
	public Vector2 Position { get; private set; }

	public void SetTarget(Vector2 target)
	{
		if (!float.IsFinite(target.X) || !float.IsFinite(target.Y))
			throw new ArgumentOutOfRangeException(nameof(target), "跟踪目标必须是有限坐标");
		_target = target;
	}

	public void Update(float deltaSeconds)
	{
		if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0)
			throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "跟踪时间增量必须是有限非负数");
		// 暂停两秒后剩余响应低于可见精度，直接收敛也避免超大时间参与乘法。
		if (deltaSeconds >= 2)
		{
			Position = _target;
			_velocity = Vector2.Zero;
			return;
		}
		Vector2 displacement = Position - _target;
		Vector2 slope = _velocity + Frequency * displacement;
		float decay = MathF.Exp(-Frequency * deltaSeconds);
		Position = _target + (displacement + slope * deltaSeconds) * decay;
		_velocity = (_velocity - Frequency * slope * deltaSeconds) * decay;
	}
}
