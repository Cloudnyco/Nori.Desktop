using System.Numerics;

namespace Nori.Live2D;

/// <summary>模型独占的串联摆求解器；按文件帧率积分并插值输出。</summary>
public sealed class PhysicsPlayer
{
	private readonly NativeModel _model;
	private readonly PhysicsDefinition _definition;
	private readonly RigState[] _rigs;
	private readonly float[] _inputHistory;
	private readonly float[] _working;
	private double _remaining;
	private bool _initialized;

	public PhysicsPlayer(NativeModel model, PhysicsDefinition definition)
	{
		_model = model;
		_definition = definition;
		// 缺失参数不参与物理；不向原生参数缓冲区写入虚拟索引。
		_rigs = definition.Rigs.Select(rig => new RigState(rig, model)).ToArray();
		_inputHistory = new float[model.ParameterCount];
		_working = new float[model.ParameterCount];
	}

	public void Update(float deltaSeconds)
	{
		if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
		if (deltaSeconds == 0) return;
		if (!_initialized)
		{
			for (int i = 0; i < _inputHistory.Length; i++) _inputHistory[i] = _model.GetParameterValue(i);
			_initialized = true;
		}
		// 恢复窗口后不追赶数秒积压，避免在渲染线程出现积分风暴。
		_remaining += Math.Min(deltaSeconds, 0.25f);
		float step = _definition.FramesPerSecond > 0 ? 1 / _definition.FramesPerSecond : Math.Min(deltaSeconds, 0.25f);
		while (_remaining + 1e-9 >= step)
		{
			float fraction = Math.Min(1, (float)(step / _remaining));
			for (int i = 0; i < _working.Length; i++)
			{
				_inputHistory[i] += (_model.GetParameterValue(i) - _inputHistory[i]) * fraction;
				_working[i] = _inputHistory[i];
			}
			foreach (RigState rig in _rigs) Integrate(rig, step);
			_remaining = Math.Max(0, _remaining - step);
		}
		float alpha = Math.Clamp((float)(_remaining / step), 0, 1);
		foreach (RigState rig in _rigs)
		{
			for (int i = 0; i < rig.OutputIndices.Length; i++)
			{
				int index = rig.OutputIndices[i];
				if (index < 0) continue;
				float value = rig.Previous[i] + (rig.Current[i] - rig.Previous[i]) * alpha;
				_model.SetParameterValue(index, value, rig.Definition.Outputs[i].Weight);
			}
		}
	}

	private void Integrate(RigState state, float seconds)
	{
		PhysicsRig rig = state.Definition;
		Vector2 translation = Vector2.Zero;
		float angle = 0;
		for (int i = 0; i < rig.Inputs.Length; i++)
		{
			int index = state.InputIndices[i];
			if (index < 0) continue;
			PhysicsInput input = rig.Inputs[i];
			float value = Normalize(_working[index], state.InputRanges[i], input.Axis == PhysicsAxis.Angle ? rig.Angle : rig.Position);
			value *= input.Weight * (input.Reflect ? 1 : -1);
			switch (input.Axis)
			{
				case PhysicsAxis.X: translation.X += value; break;
				case PhysicsAxis.Y: translation.Y += value; break;
				case PhysicsAxis.Angle: angle += value; break;
			}
		}
		float radians = angle * (MathF.PI / 180);
		translation = Rotate(translation, -radians);
		Vector2 gravity = new(MathF.Sin(radians), MathF.Cos(radians));
		state.Positions[0] = translation;
		for (int i = 1; i < state.Positions.Length; i++)
		{
			PhysicsParticle particle = rig.Particles[i];
			Vector2 old = state.Positions[i];
			float delay = particle.Delay * seconds * 30;
			Vector2 offset = old - state.Positions[i - 1];
			float turn = SignedAngle(state.Gravity[i], gravity) / 5;
			Vector2 predicted = state.Positions[i - 1] + Rotate(offset, turn)
				+ state.Velocities[i] * delay + (gravity * particle.Acceleration + _definition.Wind) * delay * delay;
			Vector2 direction = Unit(predicted - state.Positions[i - 1], gravity);
			Vector2 position = state.Positions[i - 1] + direction * particle.Radius;
			if (MathF.Abs(position.X) < MathF.Abs(rig.Position.Maximum) * 0.001f) position.X = 0;
			state.Positions[i] = position;
			state.Velocities[i] = delay > 0 ? (position - old) / delay * particle.Mobility : Vector2.Zero;
			state.Gravity[i] = gravity;
		}
		for (int i = 0; i < rig.Outputs.Length; i++)
		{
			PhysicsOutput output = rig.Outputs[i];
			int vertex = output.VertexIndex;
			Vector2 direction = state.Positions[vertex] - state.Positions[vertex - 1];
			Vector2 parent = vertex > 1 ? state.Positions[vertex - 1] - state.Positions[vertex - 2] : -_definition.Gravity;
			float value = output.Axis switch
			{
				PhysicsAxis.X => direction.X,
				PhysicsAxis.Y => direction.Y,
				_ => SignedAngle(parent, direction),
			};
			value *= output.Scale * (output.Reflect ? -1 : 1);
			state.Previous[i] = state.Current[i];
			state.Current[i] = value;
			int index = state.OutputIndices[i];
			if (index >= 0)
			{
				PhysicsRange range = state.OutputRanges[i];
				value = Math.Clamp(value, range.Minimum, range.Maximum);
				_working[index] += (value - _working[index]) * output.Weight;
			}
		}
	}

	internal static float Normalize(float value, PhysicsRange source, PhysicsRange target)
	{
		value = Math.Clamp(value, source.Minimum, source.Maximum);
		float extent = value >= source.Default ? source.Maximum - source.Default : source.Default - source.Minimum;
		float scale = value >= source.Default ? target.Maximum - target.Default : target.Default - target.Minimum;
		return target.Default + (extent > 0 ? (value - source.Default) / extent * scale : 0);
	}
	private static Vector2 Rotate(Vector2 value, float radians) => Vector2.Transform(value, Matrix3x2.CreateRotation(radians));
	private static Vector2 Unit(Vector2 value, Vector2 fallback) => value.LengthSquared() > 1e-12f ? Vector2.Normalize(value) : fallback;
	private static float SignedAngle(Vector2 from, Vector2 to) => MathF.Atan2(from.X * to.Y - from.Y * to.X, Vector2.Dot(from, to));

	private sealed class RigState
	{
		internal readonly PhysicsRig Definition;
		internal readonly int[] InputIndices, OutputIndices;
		internal readonly PhysicsRange[] InputRanges, OutputRanges;
		internal readonly Vector2[] Positions, Velocities, Gravity;
		internal readonly float[] Previous, Current;

		internal RigState(PhysicsRig definition, NativeModel model)
		{
			Definition = definition;
			var indices = model.ParameterIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
			InputIndices = definition.Inputs.Select(input => indices.GetValueOrDefault(input.Id, -1)).ToArray();
			OutputIndices = definition.Outputs.Select(output => indices.GetValueOrDefault(output.Id, -1)).ToArray();
			InputRanges = InputIndices.Select(index => ReadRange(model, index)).ToArray();
			OutputRanges = OutputIndices.Select(index => ReadRange(model, index)).ToArray();
			Positions = new Vector2[definition.Particles.Length];
			for (int i = 1; i < Positions.Length; i++) Positions[i] = Positions[i - 1] + Vector2.UnitY * definition.Particles[i].Radius;
			Velocities = new Vector2[Positions.Length];
			Gravity = Enumerable.Repeat(Vector2.UnitY, Positions.Length).ToArray();
			Previous = new float[OutputIndices.Length];
			Current = new float[OutputIndices.Length];
		}

		private static unsafe PhysicsRange ReadRange(NativeModel model, int index)
		{
			if (index < 0) return default;
			float minimum = model.GetParameterMinimumValues()[index], maximum = model.GetParameterMaximumValues()[index];
			// 物理输入以参数区间中点为中心，不以模型静态默认姿态为中心。
			var range = new PhysicsRange(minimum, (minimum + maximum) / 2, maximum);
			GC.KeepAlive(model);
			return range;
		}
	}
}
