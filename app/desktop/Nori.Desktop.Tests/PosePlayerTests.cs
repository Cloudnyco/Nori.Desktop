using System.Text;
using System.Text.Json;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

[Collection("Native settings")]
public sealed class PosePlayerTests
{
	private const string Json = """
		{"Groups":[[{"Id":"TestPoseA","Link":["TestPoseLink"]},{"Id":"TestPoseB"}],[{"Id":"TestPoseC"}]]}
		""";
	private static PoseDefinition Definition(string json = Json) => PoseDefinition.Parse(Encoding.UTF8.GetBytes(json));
	private static NativeModel Model() => new(File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("nori", "Nori.moc3")));

	[Fact]
	public void 默认时间与空组解析()
	{
		Assert.Equal(0.5f, Definition().FadeInTime);
		Assert.Equal(2, Definition().GroupCount);
		Assert.Equal(0, Definition("{\"Groups\":[[]]}").GroupCount);
		Assert.Equal(0, Definition("{\"Version\":3,\"Groups\":[]}").GroupCount);
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("null")]
	[InlineData("{\"Version\":4,\"Groups\":[]}")]
	[InlineData("{\"FadeInTime\":-1,\"Groups\":[]}")]
	[InlineData("{\"FadeInTime\":1e100,\"Groups\":[]}")]
	[InlineData("{\"Groups\":[[{\"Id\":\" \"}]]}")]
	[InlineData("{\"Groups\":[[{\"Id\":\"A\",\"Link\":null}]]}")]
	public void 损坏结构在后台拒绝(string json) => Assert.Throws<JsonException>(() => Definition(json));

	[Theory]
	[InlineData(0, 1)]
	[InlineData(0.2f, 0.8125f)]
	[InlineData(0.4f, 0.75f)]
	[InlineData(0.6f, 0.625f)]
	[InlineData(0.8f, 0.25f)]
	[InlineData(1, 0)]
	public void 交叉渐变限制透光且匹配既有轨迹(float foreground, float expected)
	{
		float background = PosePlayer.BackgroundOpacityLimit(foreground);
		Assert.Equal(expected, background, 5);
		Assert.InRange((1 - foreground) * (1 - background), 0, 0.150001f);
	}

	[Live2DAssetsFact]
	public void 虚拟参数切换渐变关联同步与无选择回退()
	{
		using NativeModel model = Model();
		var player = new PosePlayer(model, Definition());
		int a = model.GetParameterIndex("TestPoseA"), b = model.GetParameterIndex("TestPoseB");
		int partA = model.GetPartIndex("TestPoseA"), partB = model.GetPartIndex("TestPoseB");
		int link = model.GetPartIndex("TestPoseLink");
		player.Update(0);
		Assert.Equal(1, model.GetParameterValue(a));
		Assert.Equal(0, model.GetParameterValue(b));
		Assert.Equal(1, model.GetPartOpacity(link));
		model.SetParameterValue(a, 0);
		model.SetParameterValue(b, 1);
		float[] background = [0.8125f, 0.75f, 0.625f, 0.25f, 0];
		for (int i = 0; i < background.Length; i++)
		{
			player.Update(0.1f);
			Assert.Equal(background[i], model.GetPartOpacity(partA), 5);
			Assert.Equal((i + 1) * 0.2f, model.GetPartOpacity(partB), 5);
			Assert.Equal(model.GetPartOpacity(partA), model.GetPartOpacity(link));
			Assert.Equal(1, model.GetPartOpacity(model.GetPartIndex("TestPoseC")));
		}
		model.SetParameterValue(b, 0);
		player.Update(0);
		Assert.Equal(1, model.GetPartOpacity(partA));
		Assert.Equal(0, model.GetPartOpacity(partB));
	}

	[Live2DAssetsFact]
	public void 原生部件切换且共享定义不共享状态()
	{
		using NativeModel first = Model();
		using NativeModel second = Model();
		string a = first.PartIds[0], b = first.PartIds[1];
		PoseDefinition definition = Definition(JsonSerializer.Serialize(new
		{
			FadeInTime = 0,
			Groups = new[] { new[] { new { Id = a }, new { Id = b } } },
		}));
		var player = new PosePlayer(first, definition);
		new PosePlayer(second, definition).Update(0);
		player.Update(0);
		first.SetParameterValue(first.GetParameterIndex(a), 0);
		first.SetParameterValue(first.GetParameterIndex(b), 1);
		player.Update(0);
		Assert.Equal(0, first.GetPartOpacity(0));
		Assert.Equal(1, first.GetPartOpacity(1));
		Assert.Equal(1, second.GetPartOpacity(0));
		Assert.Equal(0, second.GetPartOpacity(1));
		foreach (float invalid in new[] { -1, float.NaN, float.PositiveInfinity })
			Assert.Throws<ArgumentOutOfRangeException>(() => player.Update(invalid));
		first.Update();
	}

	[Live2DAssetsFact]
	public void 真实模型姿势更新稳态每帧零分配()
	{
		using NativeModel model = Model();
		var player = new PosePlayer(model, Definition());
		for (int i = 0; i < 8; i++) player.Update(1 / 60f);
		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 100; i++) player.Update(1 / 60f);
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.Equal(0, allocated);
	}

	[Live2DAssetsFact]
	public void 多项同时激活选择首项且打断不会提高退场不透明度()
	{
		using NativeModel model = Model();
		var player = new PosePlayer(model, Definition());
		player.Update(0);
		int a = model.GetParameterIndex("TestPoseA"), b = model.GetParameterIndex("TestPoseB");
		int partA = model.GetPartIndex("TestPoseA"), partB = model.GetPartIndex("TestPoseB");
		model.SetParameterValue(a, 0);
		model.SetParameterValue(b, 1);
		player.Update(0.1f);
		model.SetParameterValue(a, 1);
		player.Update(0.05f);
		Assert.True(model.GetPartOpacity(partA) > 0.8125f);
		Assert.InRange(model.GetPartOpacity(partB), 0, 0.2f);
	}
}
