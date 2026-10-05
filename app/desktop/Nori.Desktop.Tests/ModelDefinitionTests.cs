using System.Text;
using System.Text.Json;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

public sealed class ModelDefinitionTests
{
	[Fact]
	public void 资源声明保留顺序分组布局和淡入淡出覆盖()
	{
		var definition = ModelDefinition.Parse("""
			{
				"Version":3,
				"FileReferences":{
					"Moc":"模型.moc3","Textures":["二.png","一.png"],
					"Physics":"模型.physics3.json","Pose":"模型.pose3.json",
					"Motions":{"Idle":[{"File":"一.motion3.json"},{"File":"二.motion3.json","FadeInTime":0,"FadeOutTime":0.75}]},
					"Expressions":[{"Name":"笑","File":"笑.exp3.json"}]
				},
				"Groups":[
					{"Target":"Parameter","Name":"EyeBlink","Ids":["左眼","右眼"]},
					{"Target":"PartOpacity","Name":"EyeBlink","Ids":["不应绑定"]},
					{"Name":"LipSync","Ids":["口型"]}
				],
				"Layout":{"Width":2,"CenterX":-0.5},
				"HitAreas":[{"Id":"头部绘制对象","Name":"Head"}]
			}
			"""u8.ToArray());
		Assert.Equal("模型.moc3", definition.Moc);
		Assert.Equal(new[] { "二.png", "一.png" }, definition.Textures);
		Assert.Equal("模型.physics3.json", definition.Physics);
		Assert.Equal("模型.pose3.json", definition.Pose);
		Assert.Equal(new ModelMotion("一.motion3.json", -1, -1), definition.Motions["Idle"][0]);
		Assert.Equal(new ModelMotion("二.motion3.json", 0, 0.75f), definition.Motions["Idle"][1]);
		Assert.Equal(new ModelExpression("笑", "笑.exp3.json"), Assert.Single(definition.Expressions));
		Assert.Equal(new[] { "左眼", "右眼" }, definition.EyeBlinkIds);
		Assert.Equal("口型", Assert.Single(definition.LipSyncIds));
		Assert.Equal(-0.5f, definition.Layout["CenterX"]);
		Assert.Equal(new ModelHitArea("头部绘制对象", "Head"), Assert.Single(definition.HitAreas));
		Assert.Throws<NotSupportedException>(() => ((IList<string>)definition.Textures)[0] = "更改.png");
		Assert.Throws<NotSupportedException>(() => ((IDictionary<string, float>)definition.Layout).Clear());
		Assert.Throws<NotSupportedException>(() => ((IList<ModelMotion>)definition.Motions["Idle"]).Clear());
	}

	[Fact]
	public void 最小声明不依赖源JSON生命周期或可选集合()
	{
		byte[] bytes = """{"FileReferences":{"Moc":"m.moc3"}}"""u8.ToArray();
		var definition = ModelDefinition.Parse(bytes);
		Array.Clear(bytes);
		Assert.Equal("m.moc3", definition.Moc);
		Assert.Empty(definition.Textures);
		Assert.Empty(definition.Motions);
		Assert.Empty(definition.Expressions);
		Assert.Empty(definition.EyeBlinkIds);
		Assert.Empty(definition.LipSyncIds);
		Assert.Empty(definition.Layout);
		Assert.Empty(definition.HitAreas);
		Assert.Null(definition.Pose);
		Assert.Null(definition.Physics);
	}

	[Theory]
	[InlineData("null")]
	[InlineData("{}")]
	[InlineData("{\"Version\":4,\"FileReferences\":{\"Moc\":\"m\"}}")]
	[InlineData("{\"FileReferences\":{\"Moc\":\" \"}}")]
	[InlineData("{\"FileReferences\":{\"Moc\":\"m\",\"Textures\":[null]}}")]
	[InlineData("{\"FileReferences\":{\"Moc\":\"m\",\"Motions\":{\"Idle\":[{}]}}}")]
	[InlineData("{\"FileReferences\":{\"Moc\":\"m\",\"Motions\":{\"Idle\":[{\"File\":\"i\",\"FadeInTime\":1e100}]}}}")]
	[InlineData("{\"FileReferences\":{\"Moc\":\"m\"},\"Layout\":{\"Width\":1e100}}")]
	[InlineData("{\"FileReferences\":{\"Moc\":\"m\"},\"HitAreas\":[{\"Id\":\"d\"}]}")]
	[InlineData("{\"FileReferences\":{\"Moc\":\"m\",\"Expressions\":[{\"Name\":\"e\"}]}}")]
	public void 损坏声明报告JSON错误(string json) =>
		Assert.Throws<JsonException>(() => ModelDefinition.Parse(Encoding.UTF8.GetBytes(json)));
}
