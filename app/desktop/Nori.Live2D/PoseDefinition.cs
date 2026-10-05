using System.Text.Json;

namespace Nori.Live2D;

/// <summary>后台解析的 pose3 互斥部件组；同一份定义可用于多个独立模型。</summary>
public sealed class PoseDefinition
{
	internal PosePart[][] Groups { get; }
	public int GroupCount => Groups.Length;
	public float FadeInTime { get; }

	private PoseDefinition(PosePart[][] groups, float fadeInTime)
	{
		Groups = groups;
		FadeInTime = fadeInTime;
	}

	public static PoseDefinition Parse(ReadOnlyMemory<byte> data)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(data);
			JsonElement root = document.RootElement;
			if (root.TryGetProperty("Version", out JsonElement version) && version.GetInt32() != 3)
				throw new JsonException("不支持的姿势文件版本");
			float fade = root.TryGetProperty("FadeInTime", out JsonElement value) ? value.GetSingle() : 0.5f;
			if (!float.IsFinite(fade) || fade < 0) throw new JsonException("姿势淡入时间必须是有限非负数");
			List<PosePart[]> groups = [];
			foreach (JsonElement group in root.GetProperty("Groups").EnumerateArray())
			{
				List<PosePart> parts = [];
				foreach (JsonElement part in group.EnumerateArray())
				{
					string id = ReadId(part.GetProperty("Id"));
					string[] links = part.TryGetProperty("Link", out JsonElement link)
						? link.EnumerateArray().Select(ReadId).ToArray() : [];
					parts.Add(new(id, links));
				}
				if (parts.Count > 0) groups.Add(parts.ToArray());
			}
			return new(groups.ToArray(), fade);
		}
		catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
		{
			throw new JsonException("姿势文件结构无效", exception);
		}
	}

	private static string ReadId(JsonElement value) => value.GetString() is { } id && !string.IsNullOrWhiteSpace(id)
		? id : throw new JsonException("姿势部件 ID 不能为空");
}

internal sealed record PosePart(string Id, string[] Links);
