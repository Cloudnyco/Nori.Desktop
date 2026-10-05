using System.Text;
using System.Text.Json;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

public sealed class PhysicsPlayerTests
{
	private const string Rig = """
		{"Input":[{"Source":{"Target":"Parameter","Id":"ParamAngleX"},"Weight":100,"Type":"X","Reflect":true}],
		"Output":[{"Destination":{"Target":"Parameter","Id":"ParamAngleY"},"VertexIndex":1,"Scale":1,"Weight":100,"Type":"Angle","Reflect":false}],
		"Vertices":[{"Position":{"X":0,"Y":0},"Mobility":0,"Delay":1,"Acceleration":0,"Radius":0},
		{"Position":{"X":0,"Y":10},"Mobility":0,"Delay":1,"Acceleration":0,"Radius":10}],
		"Normalization":{"Position":{"Minimum":-10,"Default":0,"Maximum":10},"Angle":{"Minimum":-30,"Default":0,"Maximum":30}}}
		""";

	private static PhysicsDefinition Definition(string rig = Rig, string fps = "60") => PhysicsDefinition.Parse(Encoding.UTF8.GetBytes(
		$$$$"""{"Version":3,"Meta":{"Fps":{{{{fps}}}},"EffectiveForces":{"Gravity":{"X":0,"Y":-1},"Wind":{"X":0,"Y":0}}},"PhysicsSettings":[{{{{rig}}}}]}"""));

	[Fact]
	public void 归一化保持中点与两侧区间并截断越界()
	{
		var source = new PhysicsRange(-2, 1, 4);
		var target = new PhysicsRange(-10, 2, 8);
		Assert.Equal(2, PhysicsPlayer.Normalize(1, source, target));
		Assert.Equal(-4, PhysicsPlayer.Normalize(-0.5f, source, target));
		Assert.Equal(5, PhysicsPlayer.Normalize(2.5f, source, target));
		Assert.Equal(-10, PhysicsPlayer.Normalize(-100, source, target));
		Assert.Equal(8, PhysicsPlayer.Normalize(100, source, target));
		Assert.Equal(2, PhysicsPlayer.Normalize(1, new(1, 1, 1), target));
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("null")]
	[InlineData("{\"Version\":4}")]
	public void 损坏结构在后台解析时拒绝(string json) => Assert.Throws<JsonException>(() => PhysicsDefinition.Parse(Encoding.UTF8.GetBytes(json)));

	[Theory]
	[InlineData("\"VertexIndex\":1", "\"VertexIndex\":0")]
	[InlineData("\"VertexIndex\":1", "\"VertexIndex\":2")]
	[InlineData("\"Type\":\"Angle\"", "\"Type\":\"Unknown\"")]
	[InlineData("\"Radius\":10", "\"Radius\":-1")]
	[InlineData("\"Weight\":100", "\"Weight\":101")]
	[InlineData("\"Maximum\":10", "\"Maximum\":-20")]
	[InlineData("\"Delay\":1", "\"Delay\":1e100")]
	public void 损坏物理配置拒绝(string before, string after) => Assert.Throws<JsonException>(() => Definition(Rig.Replace(before, after)));

	[Theory]
	[InlineData("-1")]
	[InlineData("1001")]
	public void 无效物理帧率拒绝(string fps) => Assert.Throws<JsonException>(() => Definition(fps: fps));

	[Live2DAssetsFact]
	public void 串联摆输出弧度且固定步长输出滞后一帧()
	{
		using NativeModel model = Model();
		int source = model.GetParameterIndex("ParamAngleX"), target = model.GetParameterIndex("ParamAngleY");
		model.SetParameterValue(source, 30);
		model.SetParameterValue(target, 0);
		var player = new PhysicsPlayer(model, Definition());
		player.Update(1 / 60f);
		Assert.Equal(0, model.GetParameterValue(target));
		player.Update(1 / 60f);
		// 根节点平移到 (10,0)，静止末端 (0,10) 投影到定长摆杆，偏角为 π/4。
		Assert.Equal(MathF.PI / 4, model.GetParameterValue(target), 5);
	}

	[Live2DAssetsFact]
	public void 输出反射权重及坐标通道()
	{
		foreach ((string axis, float expected) in new[] { ("X", -MathF.Sqrt(50)), ("Y", MathF.Sqrt(50)), ("Angle", MathF.PI / 4) })
		{
			using NativeModel model = Model();
			model.SetParameterValue(model.GetParameterIndex("ParamAngleX"), 30);
			int target = model.GetParameterIndex("ParamAngleY");
			string rig = Rig.Replace("\"Type\":\"Angle\",\"Reflect\":false", $"\"Type\":\"{axis}\",\"Reflect\":true")
				.Replace("\"Scale\":1,\"Weight\":100", "\"Scale\":1,\"Weight\":50");
			var player = new PhysicsPlayer(model, Definition(rig));
			player.Update(1 / 60f);
			model.SetParameterValue(target, 0);
			player.Update(1 / 60f);
			Assert.Equal(-expected / 2, model.GetParameterValue(target), 5);
		}
	}

	[Live2DAssetsFact]
	public void 定帧插值及实例隔离()
	{
		using NativeModel first = Model(), second = Model();
		PhysicsDefinition definition = Definition(fps: "30");
		var a = new PhysicsPlayer(first, definition);
		var b = new PhysicsPlayer(second, definition);
		int source = first.GetParameterIndex("ParamAngleX"), target = first.GetParameterIndex("ParamAngleY");
		first.SetParameterValue(source, 30);
		second.SetParameterValue(source, -30);
		for (int frame = 0; frame < 3; frame++) { a.Update(1 / 60f); b.Update(1 / 60f); }
		Assert.Equal(MathF.PI / 8, first.GetParameterValue(target), 5);
		Assert.Equal(-MathF.PI / 8, second.GetParameterValue(target), 5);
	}

	[Live2DAssetsFact]
	public void 相同输入在不同渲染帧率下保持物理轨迹()
	{
		using NativeModel first = Model(), second = Model();
		PhysicsDefinition definition = Definition(Rig.Replace("\"Mobility\":0", "\"Mobility\":0.8").Replace("\"Acceleration\":0", "\"Acceleration\":1"));
		var a = new PhysicsPlayer(first, definition);
		var b = new PhysicsPlayer(second, definition);
		int source = first.GetParameterIndex("ParamAngleX"), target = first.GetParameterIndex("ParamAngleY");
		first.SetParameterValue(source, 30);
		second.SetParameterValue(source, 30);
		for (int frame = 0; frame < 120; frame++)
		{
			a.Update(1 / 60f);
			b.Update(1 / 120f);
			b.Update(1 / 120f);
			Assert.Equal(first.GetParameterValue(target), second.GetParameterValue(target), 5);
		}
	}

	[Live2DAssetsFact]
	public void 无固定帧率时使用本次步长且零延迟不会除零()
	{
		using NativeModel model = Model();
		model.SetParameterValue(model.GetParameterIndex("ParamAngleX"), 30);
		var player = new PhysicsPlayer(model, Definition(Rig.Replace("\"Delay\":1", "\"Delay\":0"), fps: "0"));
		player.Update(0.01f);
		player.Update(0.02f);
		Assert.Equal(MathF.PI / 4, model.GetParameterValue(model.GetParameterIndex("ParamAngleY")), 5);
	}

	[Live2DAssetsFact]
	public void 缺失参数不创建虚拟物理绑定且暂停恢复有限()
	{
		using NativeModel model = Model();
		var player = new PhysicsPlayer(model, Definition(Rig.Replace("ParamAngleX", "MissingInput").Replace("ParamAngleY", "MissingOutput")));
		player.Update(0);
		player.Update(60);
		Assert.Throws<ArgumentOutOfRangeException>(() => player.Update(float.NaN));
		Assert.Throws<ArgumentOutOfRangeException>(() => player.Update(-1));
		for (int i = 0; i < model.ParameterCount; i++) Assert.True(float.IsFinite(model.GetParameterValue(i)));
	}

	[Live2DAssetsFact]
	public unsafe void 两套真实物理配置长序列有限且目标不越界()
	{
		foreach ((string id, string moc, int count) in new[] { ("arg-nori", "ARGNori.moc3", 14), ("nori", "Nori.moc3", 43) })
		{
			string path = PreparedModelAssetsTests.FindFixture(id, moc);
			using var model = new NativeModel(File.ReadAllBytes(path));
			PhysicsDefinition definition = PhysicsDefinition.Parse(File.ReadAllBytes(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.physics3.json").Single()));
			Assert.Equal(count, definition.RigCount);
			var player = new PhysicsPlayer(model, definition);
			int input = model.GetParameterIndex("ParamAngleX");
			for (int frame = 0; frame < 600; frame++)
			{
				for (int i = 0; i < model.ParameterCount; i++) model.SetParameterValue(i, model.GetParameterDefaultValue(i));
				model.SetParameterValue(input, MathF.Sin(frame / 30f) * 30);
				player.Update(frame % 3 == 0 ? 1 / 30f : 1 / 120f);
				for (int i = 0; i < model.ParameterCount; i++)
				{
					float value = model.GetParameterValue(i);
					Assert.True(float.IsFinite(value));
					Assert.InRange(value, model.GetParameterMinimumValues()[i] - 1e-5f, model.GetParameterMaximumValues()[i] + 1e-5f);
				}
				model.Update();
			}
			GC.KeepAlive(model);
		}
	}

	private static NativeModel Model() => new(File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("arg-nori", "ARGNori.moc3")));
}
