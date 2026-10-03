using System.Numerics;
using System.Text.Json;

namespace Nori.Live2D;

/// <summary>后台编译的 physics3 配置；不包含模型绑定或可变粒子状态。</summary>
public sealed class PhysicsDefinition
{
	internal PhysicsRig[] Rigs { get; }
	public int RigCount => Rigs.Length;
	public float FramesPerSecond { get; }
	public Vector2 Gravity { get; }
	public Vector2 Wind { get; }

	private PhysicsDefinition(PhysicsRig[] rigs, float fps, Vector2 gravity, Vector2 wind) =>
		(Rigs, FramesPerSecond, Gravity, Wind) = (rigs, fps, gravity, wind);

	public static PhysicsDefinition Parse(ReadOnlyMemory<byte> json)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.GetProperty("Version").GetInt32() != 3) throw new JsonException("物理文件版本必须为 3");
			JsonElement meta = root.GetProperty("Meta");
			float fps = meta.TryGetProperty("Fps", out JsonElement value) ? Number(value) : 0;
			if (fps < 0 || fps > 1000) throw new JsonException("物理帧率超出有效范围");
			JsonElement forces = meta.GetProperty("EffectiveForces");
			Vector2 gravity = Vector(forces.GetProperty("Gravity")), wind = Vector(forces.GetProperty("Wind"));
			List<PhysicsRig> rigs = [];
			foreach (JsonElement rig in root.GetProperty("PhysicsSettings").EnumerateArray())
			{
				PhysicsParticle[] particles = rig.GetProperty("Vertices").EnumerateArray().Select(p =>
					new PhysicsParticle(Vector(p.GetProperty("Position")), Field(p, "Mobility"), Field(p, "Delay"),
						Field(p, "Acceleration"), Field(p, "Radius"))).ToArray();
				if (particles.Length < 2 || particles.Any(p => p.Mobility < 0 || p.Mobility > 1 || p.Delay < 0 || p.Acceleration < 0 || p.Radius < 0)
					|| particles.Skip(1).Any(p => p.Radius == 0)) throw new JsonException("物理粒子配置无效");
				PhysicsInput[] inputs = rig.GetProperty("Input").EnumerateArray().Select(p =>
					new PhysicsInput(Parameter(p.GetProperty("Source")), Axis(p), Weight(p), p.GetProperty("Reflect").GetBoolean())).ToArray();
				PhysicsOutput[] outputs = rig.GetProperty("Output").EnumerateArray().Select(p =>
					new PhysicsOutput(Parameter(p.GetProperty("Destination")), Axis(p), Weight(p), p.GetProperty("Reflect").GetBoolean(),
						p.GetProperty("VertexIndex").GetInt32(), Field(p, "Scale"))).ToArray();
				if (outputs.Any(p => p.VertexIndex < 1 || p.VertexIndex >= particles.Length)) throw new JsonException("物理输出粒子索引无效");
				JsonElement normalization = rig.GetProperty("Normalization");
				rigs.Add(new(inputs, outputs, particles, Range(normalization.GetProperty("Position")), Range(normalization.GetProperty("Angle"))));
			}
			return new(rigs.ToArray(), fps, gravity, wind);
		}
		catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
		{
			throw new JsonException("physics3.json 结构或数值无效", exception);
		}
	}

	private static float Number(JsonElement value)
	{
		float number = value.GetSingle();
		return float.IsFinite(number) ? number : throw new JsonException("物理数值必须有限");
	}
	private static float Field(JsonElement parent, string name) => Number(parent.GetProperty(name));
	private static Vector2 Vector(JsonElement value) => new(Field(value, "X"), Field(value, "Y"));
	private static float Weight(JsonElement value)
	{
		float weight = Field(value, "Weight");
		return weight is >= 0 and <= 100 ? weight / 100 : throw new JsonException("物理权重必须在 0 到 100 之间");
	}
	private static string Parameter(JsonElement value)
	{
		if (value.GetProperty("Target").GetString() != "Parameter") throw new JsonException("物理输入输出必须绑定参数");
		string? id = value.GetProperty("Id").GetString();
		return !string.IsNullOrWhiteSpace(id) ? id : throw new JsonException("物理参数 ID 不能为空");
	}
	private static PhysicsAxis Axis(JsonElement value) => value.GetProperty("Type").GetString() switch
	{
		"X" => PhysicsAxis.X, "Y" => PhysicsAxis.Y, "Angle" => PhysicsAxis.Angle,
		_ => throw new JsonException("物理通道类型无效"),
	};
	private static PhysicsRange Range(JsonElement value)
	{
		var range = new PhysicsRange(Field(value, "Minimum"), Field(value, "Default"), Field(value, "Maximum"));
		return range.Minimum <= range.Default && range.Default <= range.Maximum
			? range : throw new JsonException("物理归一化区间无效");
	}
}

internal enum PhysicsAxis { X, Y, Angle }
internal readonly record struct PhysicsRange(float Minimum, float Default, float Maximum);
internal readonly record struct PhysicsInput(string Id, PhysicsAxis Axis, float Weight, bool Reflect);
internal readonly record struct PhysicsOutput(string Id, PhysicsAxis Axis, float Weight, bool Reflect, int VertexIndex, float Scale);
internal readonly record struct PhysicsParticle(Vector2 Position, float Mobility, float Delay, float Acceleration, float Radius);
internal sealed record PhysicsRig(PhysicsInput[] Inputs, PhysicsOutput[] Outputs, PhysicsParticle[] Particles, PhysicsRange Position, PhysicsRange Angle);
