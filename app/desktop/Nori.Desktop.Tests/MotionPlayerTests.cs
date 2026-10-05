using System.Text;
using System.Text.Json;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

[Collection("Native settings")]
public sealed class MotionPlayerTests
{
	private static MotionClip Clip(string curves, string extra = "", string meta = "") => MotionClip.Parse(Encoding.UTF8.GetBytes(
		$$"""{"Version":3,"Meta":{"Duration":2,"FadeInTime":0,"FadeOutTime":0{{meta}}},"Curves":[{{curves}}]{{extra}}} """));

	private static string Curve(string segments, string id = "Test", string target = "Parameter", string extra = "") =>
		$$"""{"Target":"{{target}}","Id":"{{id}}","Segments":[{{segments}}]{{extra}}} """;

	[Theory]
	[InlineData("0,0,0,1,10", 0.25f, 2.5f)]
	[InlineData("0,0,2,1,10", 0.25f, 0)]
	[InlineData("0,0,3,1,10", 0.25f, 10)]
	[InlineData("0,0,2,1,10,0,2,20", 1, 10)]
	[InlineData("0,0,0,1,10", 2, 10)]
	[InlineData("0.2,7", 0, 7)]
	public void 基础曲线与段边界(string segments, float time, float expected) =>
		Assert.Equal(expected, Assert.Single(Clip(Curve(segments)).Curves).Evaluate(time), 5);

	[Fact]
	public void 非限制贝塞尔先反解时间而非直接代入()
	{
		// x(u)=u³，y(u)=3u(1-u)；x=1/8 时 u=1/2，y=3/4。
		MotionCurve curve = Assert.Single(Clip(Curve("0,0,1,0,1,0,1,1,0")).Curves);
		Assert.Equal(0.75f, curve.Evaluate(0.125f), 5);
		Assert.Equal(0, curve.Evaluate(0), 5);
		Assert.Equal(0, curve.Evaluate(1), 5);
		MotionCurve restricted = Assert.Single(Clip(Curve("0,0,1,0.3333333,1,0.6666667,1,1,0"), meta: ",\"AreBeziersRestricted\":true").Curves);
		Assert.Equal(0.75f, restricted.Evaluate(0.5f), 5);
	}

	[Theory]
	[InlineData("0,0,4,1,1")]
	[InlineData("0,0,1,0.1,1")]
	[InlineData("0,0,0,0,1")]
	[InlineData("0,0,0,3,1")]
	[InlineData("0,0,1,-1,0,0.8,1,1,1")]
	[InlineData("0,0,0,1,1e100")]
	public void 损坏曲线在后台解析时拒绝(string segments) => Assert.Throws<JsonException>(() => Clip(Curve(segments)));

	[Theory]
	[InlineData("{}")]
	[InlineData("null")]
	[InlineData("{\"Version\":3,\"Meta\":{\"Duration\":0},\"Curves\":[]}")]
	public void 损坏结构报告JSON异常(string json) => Assert.Throws<JsonException>(() => MotionClip.Parse(Encoding.UTF8.GetBytes(json)));

	[Live2DAssetsFact]
	public void 效果通道与部件不透明度使用各自目标()
	{
		using NativeModel model = Model();
		var player = new MotionPlayer(model, ["Eye", "UntrackedEye"], ["Mouth", "UntrackedMouth"]);
		MotionClip clip = Clip(string.Join(",", [
			Curve("0,0.4", "EyeBlink", "Model"), Curve("0,0.2", "LipSync", "Model"),
			Curve("0,0.7", "Opacity", "Model"), Curve("0,0.5", "Eye"), Curve("0,0.1", "Mouth"),
			Curve("0,0.6", "Part", "PartOpacity")]));
		Assert.NotNull(player.Start(clip, MotionPriority.Normal));
		player.Update(0);
		Assert.Equal(0.2f, model.GetParameterValue(model.GetParameterIndex("Eye")), 5);
		Assert.Equal(0.3f, model.GetParameterValue(model.GetParameterIndex("Mouth")), 5);
		Assert.Equal(0.4f, model.GetParameterValue(model.GetParameterIndex("UntrackedEye")), 5);
		Assert.Equal(0.2f, model.GetParameterValue(model.GetParameterIndex("UntrackedMouth")), 5);
		Assert.Equal(0.6f, model.GetPartOpacity(model.GetPartIndex("Part")), 5);
		Assert.Equal(0, model.GetParameterValue(model.GetParameterIndex("Part")));
		Assert.Equal(0.7f, model.Opacity);
	}

	[Live2DAssetsFact]
	public void 优先级中断淡化及单次完成回调()
	{
		using NativeModel model = Model();
		var player = new MotionPlayer(model, [], []);
		int interruptedFinished = 0, finished = 0;
		MotionClip one = Clip(Curve("0,1"));
		MotionPlayback first = player.Start(one, MotionPriority.Normal, () => interruptedFinished++, fadeOut: 1)!;
		player.Update(0);
		Assert.Null(player.Start(one, MotionPriority.Idle));
		Assert.Null(player.Start(one, MotionPriority.Normal));
		MotionPlayback second = player.Start(Clip(Curve("0,0")), MotionPriority.Force, () => finished++, fadeIn: 1)!;
		player.Update(0);
		player.Update(0.5f);
		Assert.Equal(0.5f, model.GetParameterValue(model.GetParameterIndex("Test")), 5);
		player.Update(0.5f);
		Assert.True(first.IsFinished);
		Assert.False(second.IsFinished);
		Assert.Equal(0, interruptedFinished);
		player.Update(1);
		Assert.Equal(1, finished);
		Assert.True(player.IsFinished);
		player.Update(1);
		Assert.Equal(1, finished);
	}

	[Live2DAssetsFact]
	public void 曲线淡化覆盖动作淡化且编辑器循环标记不自动循环()
	{
		using NativeModel model = Model();
		var player = new MotionPlayer(model, [], []);
		MotionClip clip = Clip(Curve("0,1", extra: ",\"FadeInTime\":0,\"FadeOutTime\":0"), meta: ",\"Loop\":true");
		Assert.True(clip.SuggestedLoop);
		player.Start(clip, MotionPriority.Normal, fadeIn: 2, fadeOut: 2);
		player.Update(0);
		Assert.Equal(1, model.GetParameterValue(model.GetParameterIndex("Test")));
		player.Update(2);
		Assert.True(player.IsFinished);
	}

	[Live2DAssetsFact]
	public void 同一动作重播与多模型不共享游标和回调()
	{
		using NativeModel a = Model();
		using NativeModel b = Model();
		var first = new MotionPlayer(a, [], []);
		var second = new MotionPlayer(b, [], []);
		MotionClip clip = Clip(Curve("0,0,0,2,2"));
		int completed = 0;
		first.Start(clip, MotionPriority.Normal, () => completed++);
		second.Start(clip, MotionPriority.Normal, () => completed += 10);
		first.Update(0);
		second.Update(0);
		first.Update(1);
		Assert.Equal(1, a.GetParameterValue(a.GetParameterIndex("Test")));
		Assert.Equal(0, b.GetParameterValue(b.GetParameterIndex("Test")));
		MotionPlayback restarted = first.Start(clip, MotionPriority.Force)!;
		first.Update(0);
		Assert.Equal(0, restarted.Elapsed);
		second.Update(2);
		Assert.Equal(10, completed);
		first.Stop();
		Assert.True(restarted.IsFinished);
		Assert.True(first.IsFinished);
	}

	[Live2DAssetsFact]
	public void 事件跨帧跨循环不丢失且完成回调可再次播放()
	{
		using NativeModel model = Model();
		var player = new MotionPlayer(model, [], []);
		List<string> events = [];
		player.EventFired += events.Add;
		MotionClip clip = Clip("", ",\"UserData\":[{\"Time\":0,\"Value\":\"start\"},{\"Time\":1,\"Value\":\"middle\"},{\"Time\":2,\"Value\":\"end\"}]");
		player.Start(clip, MotionPriority.Normal, loop: true);
		player.Update(0);
		player.Update(4.5f);
		Assert.Equal(["start", "middle", "end", "start", "middle", "end", "start"], events);
		player.Stop();
		player.Start(clip, MotionPriority.Normal, () => player.Start(clip, MotionPriority.Idle));
		player.Update(0);
		player.Update(2);
		Assert.False(player.IsFinished);
	}

	[Live2DAssetsFact]
	public void 真实Idle动作更新稳态每帧零分配()
	{
		string modelPath = PreparedModelAssetsTests.FindFixture("nori", "Nori.model3.json");
		string root = Path.GetDirectoryName(modelPath)!;
		ModelDefinition definition = ModelDefinition.Parse(File.ReadAllBytes(modelPath));
		string motionPath = Path.Combine(root, definition.Motions["Idle"][0].File);
		MotionClip clip = MotionClip.Parse(File.ReadAllBytes(motionPath));
		using NativeModel model = new(File.ReadAllBytes(Path.Combine(root, "Nori.moc3")));
		var player = new MotionPlayer(model, definition.EyeBlinkIds, definition.LipSyncIds);
		player.Start(clip, MotionPriority.Force, loop: true);
		for (int i = 0; i < 8; i++) player.Update(1 / 60f);
		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int i = 0; i < 100; i++) player.Update(1 / 60f);
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.Equal(0, allocated);
	}

	[Live2DAssetsFact]
	public void 两套真实模型全部动作可求值并独立更新()
	{
		foreach ((string id, string name) in new[] { ("arg-nori", "ARGNori"), ("nori", "Nori") })
		{
			string root = Path.GetDirectoryName(PreparedModelAssetsTests.FindFixture(id, $"{name}.moc3"))!;
			using NativeModel model = new(File.ReadAllBytes(Path.Combine(root, $"{name}.moc3")));
			var player = new MotionPlayer(model, [], []);
			string[] files = Directory.GetFiles(root, "*.motion3.json", SearchOption.AllDirectories);
			Assert.NotEmpty(files);
			foreach (string file in files)
			{
				MotionClip clip = MotionClip.Parse(File.ReadAllBytes(file));
				player.Start(clip, MotionPriority.Force);
				player.Update(0);
				for (int frame = 0; frame <= 120; frame++)
				{
					foreach (MotionCurve curve in clip.Curves)
						Assert.True(float.IsFinite(curve.Evaluate(clip.Duration * frame / 120)));
					player.Update(clip.Duration / 120);
					model.Update();
					for (int parameter = 0; parameter < model.ParameterCount; parameter++)
						Assert.True(float.IsFinite(model.GetParameterValue(parameter)));
				}
				Assert.True(player.IsFinished);
			}
		}
	}

	private static NativeModel Model() => new(File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("arg-nori", "ARGNori.moc3")));
}
