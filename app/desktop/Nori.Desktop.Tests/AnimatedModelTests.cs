using System.Numerics;
using System.Text;
using System.Text.Json;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

public sealed class AnimatedModelTests
{
	private static byte[] Moc() => File.ReadAllBytes(PreparedModelAssetsTests.FindFixture("nori", "Nori.moc3"));
	private static ModelDefinition Definition(string groups = "{}") => ModelDefinition.Parse(Encoding.UTF8.GetBytes(
		"{\"FileReferences\":{\"Moc\":\"不存在.moc3\",\"Motions\":" + groups + "}}"));
	private static MotionClip Clip() => MotionClip.Parse("""
		{"Version":3,"Meta":{"Duration":1,"Loop":false,"FadeInTime":0,"FadeOutTime":0},
		"Curves":[{"Target":"Parameter","Id":"ParamAngleX","Segments":[0,10,0,1,10]}],
		"UserData":[{"Time":0.5,"Value":"事件"}]}
		"""u8.ToArray());

	[Live2DAssetsFact]
	public void 动作快照在行为之前保存且最终回调在呼吸姿势之后()
	{
		PoseDefinition pose = PoseDefinition.Parse("""{"Groups":[[{"Id":"TestPose"}]]}"""u8.ToArray());
		using var animation = new AnimatedModel(Moc(), Definition("""{"Idle":[{"File":"idle.motion3.json"}]}"""),
			new Dictionary<string, MotionClip> { ["Idle_0"] = Clip() }, pose: pose) { RandomMotion = false };
		NativeModel model = animation.Model;
		int angle = model.GetParameterIndex("ParamAngleX"), breath = model.GetParameterIndex("ParamBreath");
		List<string> order = [];
		animation.BeforeEffects = () =>
		{
			order.Add("行为");
			Assert.Equal(10, model.GetParameterValue(angle));
			model.AddParameterValue(angle, 2);
			model.SetParameterValue(breath, 0);
		};
		animation.AfterEffects = () =>
		{
			order.Add("最终");
			Assert.Equal(12, model.GetParameterValue(angle));
			Assert.Equal(0.25f, model.GetParameterValue(breath));
			Assert.Equal(1, model.GetPartOpacity(model.GetPartIndex("TestPose")));
			model.AddParameterValue(angle, 3);
		};
		Assert.NotNull(animation.StartMotion("Idle", 0, MotionPriority.Force));
		animation.Update(0);
		Assert.Equal(15, model.GetParameterValue(angle));
		animation.StopAllMotions();
		animation.Update(0);
		Assert.Equal(15, model.GetParameterValue(angle));
		Assert.Equal(new[] { "行为", "最终", "行为", "最终" }, order);
	}

	[Live2DAssetsFact]
	public void 动作组精确匹配优先且大小写回退保留真实组名()
	{
		var clips = new Dictionary<string, MotionClip> { ["Idle_0"] = Clip(), ["idle_0"] = Clip() };
		using var animation = new AnimatedModel(Moc(), Definition("""{"Idle":[{"File":"a"}],"idle":[{"File":"b"}],"Empty":[]}"""), clips);
		Assert.NotNull(animation.StartMotion("idle", 0, MotionPriority.Force));
		Assert.Equal("idle", animation.CurrentMotionGroup);
		Assert.Null(animation.StartMotion("Idle", 0, MotionPriority.Idle));
		Assert.Equal("idle", animation.CurrentMotionGroup);
		animation.StopAllMotions();
		Assert.Null(animation.CurrentMotionGroup);
		Assert.NotNull(animation.StartMotion("IDLE", 0, MotionPriority.Force));
		Assert.Equal("Idle", animation.CurrentMotionGroup);
		Assert.Null(animation.StartMotion("Idle", -1, MotionPriority.Force));
		Assert.Null(animation.StartMotion("Idle", 1, MotionPriority.Force));
		Assert.Null(animation.StartRandomMotion("Empty", MotionPriority.Force));
		Assert.Null(animation.StartMotion("missing", 0, MotionPriority.Force));
	}

	[Live2DAssetsFact]
	public void 自动待机下一帧起播且完成回调可重入播放()
	{
		using var animation = new AnimatedModel(Moc(), Definition("""{"Idle":[{"File":"idle"}]}"""),
			new Dictionary<string, MotionClip> { ["Idle_0"] = Clip() });
		animation.Update(0.1f);
		Assert.False(animation.IsMotionFinished);
		Assert.Equal("Idle", animation.CurrentMotionGroup);
		animation.RandomMotion = false;
		int events = 0;
		animation.MotionEvent += value => { Assert.Equal("事件", value); events++; };
		MotionPlayback? replay = null;
		animation.StartMotion("Idle", 0, MotionPriority.Force,
			() => replay = animation.StartMotion("Idle", 0, MotionPriority.Force));
		animation.Update(0);
		animation.Update(0.5f);
		Assert.Equal(1, events);
		animation.Update(0.6f);
		Assert.NotNull(replay);
		Assert.False(animation.IsMotionFinished);
	}

	[Live2DAssetsFact]
	public void 两个装配实例共享定义与动作但不共享播放和释放()
	{
		ModelDefinition definition = Definition("""{"Idle":[{"File":"idle"}]}""");
		var clips = new Dictionary<string, MotionClip> { ["Idle_0"] = Clip() };
		var first = new AnimatedModel(Moc(), definition, clips) { RandomMotion = false };
		using var second = new AnimatedModel(Moc(), definition, clips) { RandomMotion = false };
		first.StartMotion("Idle", 0, MotionPriority.Force);
		Assert.True(second.IsMotionFinished);
		first.Update(0);
		first.Dispose();
		first.Dispose();
		Assert.True(first.Model.IsDisposed);
		Assert.False(second.Model.IsDisposed);
		second.Update(0);
		Assert.Throws<ObjectDisposedException>(() => first.Update(0));
		Assert.Throws<ObjectDisposedException>(() => first.StartMotion("Idle", 0, MotionPriority.Force));
		foreach (float value in new[] { -1, float.NaN, float.PositiveInfinity })
			Assert.Throws<ArgumentOutOfRangeException>(() => second.Update(value));
	}

	[Fact]
	public void 缺失动作在创建原生模型之前拒绝而不回退读取文件()
	{
		var error = Assert.Throws<InvalidOperationException>(() => new AnimatedModel([], Definition("""{"Idle":[{"File":"不存在"}]}"""),
			new Dictionary<string, MotionClip>()));
		Assert.Contains("Idle_0", error.Message);
	}

	[Live2DAssetsFact]
	public unsafe void 命名命中区域使用当前网格模型坐标且透明时不命中()
	{
		byte[] moc = Moc();
		using var reference = new NativeModel(moc);
		string id = reference.DrawableIds[0];
		ModelDefinition definition = ModelDefinition.Parse(JsonSerializer.SerializeToUtf8Bytes(new
		{
			FileReferences = new { Moc = "内存" }, HitAreas = new[] { new { Id = id, Name = "Head" } },
		}));
		using var animation = new AnimatedModel(moc, definition, new Dictionary<string, MotionClip>()) { RandomMotion = false };
		animation.Update(0);
		NativeModel model = animation.Model;
		Vector2* vertices = model.GetDrawableVertexPositions(0);
		Vector2 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
		for (int i = 0; i < model.GetDrawableVertexCount(0); i++)
		{
			min = Vector2.Min(min, vertices[i]);
			max = Vector2.Max(max, vertices[i]);
		}
		Vector2 center = (min + max) / 2;
		Assert.True(animation.HitTest("head", center));
		Assert.False(animation.HitTest("missing", center));
		Assert.False(animation.HitTest("Head", max + Vector2.One));
		Assert.False(animation.HitTest("Head", new(float.NaN, 0)));
		model.Opacity = 0.5f;
		Assert.False(animation.HitTest("Head", center));
		GC.KeepAlive(model);
	}
}
