using System.Collections.ObjectModel;
using System.Text.Json;

namespace Nori.Live2D;

/// <summary>model3 的只读资源声明；解析不访问文件，路径安全由宿主统一校验。</summary>
public sealed class ModelDefinition
{
	private ModelDefinition(JsonElement root)
	{
		if (root.TryGetProperty("Version", out var version) && version.GetInt32() != 3)
			throw new JsonException("模型声明版本必须为 3");
		JsonElement files = root.GetProperty("FileReferences");
		Moc = RequiredText(files, "Moc");
		Textures = Strings(files, "Textures");
		Physics = OptionalText(files, "Physics");
		Pose = OptionalText(files, "Pose");
		var motions = new Dictionary<string, IReadOnlyList<ModelMotion>>(StringComparer.Ordinal);
		if (files.TryGetProperty("Motions", out var groups))
			foreach (var group in groups.EnumerateObject())
			{
				var items = new List<ModelMotion>();
				foreach (var item in group.Value.EnumerateArray())
					items.Add(new(RequiredText(item, "File"), Number(item, "FadeInTime", -1), Number(item, "FadeOutTime", -1)));
				motions.Add(group.Name, items.AsReadOnly());
			}
		Motions = new ReadOnlyDictionary<string, IReadOnlyList<ModelMotion>>(motions);
		var expressions = new List<ModelExpression>();
		if (files.TryGetProperty("Expressions", out var expressionArray))
			foreach (var item in expressionArray.EnumerateArray())
				expressions.Add(new(RequiredText(item, "Name"), RequiredText(item, "File")));
		Expressions = expressions.AsReadOnly();
		var eyeBlink = new List<string>();
		var lipSync = new List<string>();
		if (root.TryGetProperty("Groups", out var parameters))
			foreach (var group in parameters.EnumerateArray())
			{
				if (OptionalText(group, "Target") is { } target && target != "Parameter") continue;
				string name = RequiredText(group, "Name");
				if (name == "EyeBlink") eyeBlink.AddRange(Strings(group, "Ids"));
				if (name == "LipSync") lipSync.AddRange(Strings(group, "Ids"));
			}
		EyeBlinkIds = eyeBlink.AsReadOnly();
		LipSyncIds = lipSync.AsReadOnly();
		var layout = new Dictionary<string, float>(StringComparer.Ordinal);
		if (root.TryGetProperty("Layout", out var layoutObject))
			foreach (var item in layoutObject.EnumerateObject()) layout.Add(item.Name, Number(layoutObject, item.Name, 0));
		Layout = new ReadOnlyDictionary<string, float>(layout);
		var hitAreas = new List<ModelHitArea>();
		if (root.TryGetProperty("HitAreas", out var areas))
			foreach (var area in areas.EnumerateArray()) hitAreas.Add(new(RequiredText(area, "Id"), RequiredText(area, "Name")));
		HitAreas = hitAreas.AsReadOnly();
	}

	public string Moc { get; }
	public IReadOnlyList<string> Textures { get; }
	public string? Physics { get; }
	public string? Pose { get; }
	public IReadOnlyDictionary<string, IReadOnlyList<ModelMotion>> Motions { get; }
	public IReadOnlyList<ModelExpression> Expressions { get; }
	public IReadOnlyList<string> EyeBlinkIds { get; }
	public IReadOnlyList<string> LipSyncIds { get; }
	public IReadOnlyDictionary<string, float> Layout { get; }
	public IReadOnlyList<ModelHitArea> HitAreas { get; }

	public static ModelDefinition Parse(ReadOnlyMemory<byte> json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			return new ModelDefinition(document.RootElement);
		}
		catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
		{
			throw new JsonException("模型声明格式无效", error);
		}
	}

	private static string RequiredText(JsonElement value, string key) =>
		!string.IsNullOrWhiteSpace(OptionalText(value, key)) ? value.GetProperty(key).GetString()! : throw new JsonException($"模型声明缺少 {key}");

	private static string? OptionalText(JsonElement value, string key) =>
		value.TryGetProperty(key, out var text) ? text.GetString() : null;

	private static IReadOnlyList<string> Strings(JsonElement value, string key)
	{
		if (!value.TryGetProperty(key, out var array)) return Array.Empty<string>();
		return Array.AsReadOnly(array.EnumerateArray().Select(item =>
			!string.IsNullOrWhiteSpace(item.GetString()) ? item.GetString()! : throw new JsonException($"模型声明 {key} 含有空条目")).ToArray());
	}

	private static float Number(JsonElement value, string key, float fallback)
	{
		float number = value.TryGetProperty(key, out var item) ? item.GetSingle() : fallback;
		return float.IsFinite(number) ? number : throw new JsonException($"模型声明 {key} 必须为有限数值");
	}
}

/// <summary>动作资源及可选淡入淡出覆盖；负值使用动作文件自己的设置。</summary>
public sealed record ModelMotion(string File, float FadeInTime, float FadeOutTime);
/// <summary>表情名称及其文件引用。</summary>
public sealed record ModelExpression(string Name, string File);
/// <summary>命中区域名称及对应的绘制对象。</summary>
public sealed record ModelHitArea(string Id, string Name);
