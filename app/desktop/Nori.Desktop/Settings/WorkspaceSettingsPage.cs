using System.Text.Json;

namespace Nori.Desktop.Settings;

/// <summary>
/// 文件访问设置页：她能看哪个文件夹，以及一轮里最多连续用多少次工具。
///
/// 归入 `core` 组，与「AI 大脑」相邻：前者决定模型与推理配置，本页决定可访问的资源范围。
/// </summary>
public sealed class WorkspaceSettingsPage : SettingsPageBase
{
	private const string GearOptionAsk = "ask";

	/// <summary>
	/// 四个档位。措辞按**她会怎么做**写，不按内部名字写 —— 用户要判断的是
	/// 「我会不会被打扰」和「我放掉了多少」，不是 trusted 和 bypass 的区别。
	/// </summary>
	private static readonly IReadOnlyList<SettingsOption> GearOptions =
	[
		new(GearOptionAsk, new("逐次确认（默认）", "Ask every time (default)")),
		new("session", new("本轮记住：同一个工具这轮只问一次", "Remember for this reply: ask once per tool")),
		new("trusted", new("完全授权：日常操作不再问（接管鼠标键盘仍然会问）", "Full: everyday actions run silently; taking over mouse and keyboard still asks")),
		new("bypass", new("完全放行：什么都不问，含接管鼠标键盘（4 小时后降回完全授权）", "Bypass: never ask, mouse and keyboard takeover included (falls back to Full after 4 hours)")),
	];

	/// <summary>创建文件访问设置页。</summary>
	public WorkspaceSettingsPage(SettingsService service, CancellationToken lifetimeToken = default)
		: base(
			service,
			"workspace",
			"reach",
			new("访问权限", "Access"),
			new("她能碰到你哪些东西：文件夹、可运行的命令、屏幕。", "What she can reach: folders, runnable commands, and your screen."),
			lifetimeToken)
	{
		SettingsSectionViewModel folder = AddSection(new("工作文件夹", "Working folder"));

		// 选择按钮排在输入框之前：多数用户使用选择对话框，手工输入路径是次要入口。
		AddAction(
			folder,
			"pickWorkspace",
			new("选择文件夹…", "Choose folder…"),
			new("留空表示不授予本地文件访问权限。", "Leave empty to grant no local file access."),
			// 不写成 `async _ => await ...`：那会被转成 Action<object?>，即 async void，
			// 抛出的异常逃到 UI 线程成为未处理异常。与其余各页一致，交给一个返回 Task 的
			// 方法并在其中 catch，失败走状态栏。
			new SettingsCommand(_ => _ = PickAsync()));

		AddField(
			folder,
			"workspaceRoot",
			new("文件夹路径", "Folder path"),
			new(
				"文件的查看、搜索与修改均限定在该文件夹内，超出范围的路径一律拒绝。修改文件逐次请求确认。",
				"Reading, searching and editing are limited to this folder; paths outside it are refused. Edits request confirmation each time."),
			SettingsEditorKind.Text,
			snapshot => SettingsSnapshotReader.String(snapshot, "", "workspace", "root"),
			"",
			(value, token) => ExecuteAsync(
				"settings_update_workspace",
				new { root = Convert.ToString(value) ?? "" },
				token));

		SettingsSectionViewModel commands = AddSection(new("可运行的任务", "Runnable tasks"));
		AddField(
			commands,
			"isolation",
			new("执行边界", "Execution boundary"),
			new(
				"命令跑在什么范围里。由系统能力决定，不可配置。",
				"What the command can reach. Determined by the platform; not configurable."),
			SettingsEditorKind.Text,
			IsolationText,
			"",
			(_, _) => Task.FromResult(default(JsonElement)),
			readOnly: true);


		AddField(
			commands,
			"tasks",
			new("任务清单", "Task list"),
			new(
				"一行一条，写成「名称 = 命令」。她只能按名字触发这里配好的任务，不能自己拼命令行；"
					+ "每次运行都会请求确认。命令在工作文件夹下执行，默认没有网络。",
				"One per line, written as \"name = command\". She can only trigger tasks listed here "
					+ "and cannot compose her own command line; each run asks for confirmation. "
					+ "Commands run in the working folder with no network access."),
			SettingsEditorKind.Multiline,
			snapshot => FormatTasks(snapshot),
			"",
			(value, token) => ExecuteAsync("settings_update_tasks", new { tasks = ParseTasks(Convert.ToString(value)) }, token));

		SettingsSectionViewModel screen = AddSection(new("屏幕", "Screen"));
		AddField(
			screen,
			"screenReading",
			new("允许查看屏幕", "Allow looking at your screen"),
			new(
				"开启后她可以在你问起时截取当前窗口交给模型分析。每次都会请求确认。"
					+ "只看你正在用的那个窗口，看不了整个屏幕，也不会自己主动去看。",
				"When on, she can capture the current window and have the model analyse it when you ask. "
					+ "Each capture asks for confirmation. Only the window you are using, never the whole "
					+ "screen, and never on her own initiative."),
			SettingsEditorKind.Boolean,
			snapshot => SettingsSnapshotReader.Boolean(snapshot, false, "workspace", "screenEnabled"),
			false,
			(value, token) => ExecuteAsync(
				"settings_update_screen", new { enabled = Convert.ToBoolean(value) }, token));

		AddField(
			screen,
			"screenAvailability",
			new("可用性", "Availability"),
			new(
				"需要当前平台支持截屏，且已配置支持看图的模型。",
				"Requires screen capture support on this platform and a configured model that accepts images."),
			SettingsEditorKind.Text,
			ScreenAvailabilityText,
			"",
			(_, _) => Task.FromResult(default(JsonElement)),
			readOnly: true);

		/* ── 授权档位 ────────────────────────────────────────────────────────
		 * 排在工作文件夹与任务之后：那两节决定「她能碰到什么」，这一节只决定
		 * 「碰之前问不问你」。顺序反过来会让人以为调档位能扩大她的活动范围。 */
		SettingsSectionViewModel permission = AddSection(new("确认方式", "Confirmations"));
		AddField(
			permission,
			"permissionGear",
			new("动手之前问不问", "Ask before acting"),
			new(
				"只影响问不问，不影响她能碰到什么 —— 工作文件夹之外的文件、没配过的命令，哪一档都碰不到。",
				"Only changes whether she asks. It never widens what she can reach: files outside the working folder and unconfigured commands stay off limits at every setting."),
			SettingsEditorKind.Choice,
			snapshot => SettingsSnapshotReader.String(snapshot, GearOptionAsk, "workspace", "permissions", "gear"),
			GearOptionAsk,
			(value, token) => ExecuteAsync(
				"settings_update_permission",
				new {gear = Convert.ToString(value) ?? GearOptionAsk},
				token),
			options: GearOptions);

		AddField(
			permission,
			"permissionState",
			new("　当前生效", "　In effect"),
			new("", ""),
			SettingsEditorKind.Text,
			GearStateText,
			"",
			(_, _) => Task.FromResult(default(JsonElement)),
			readOnly: true);

		SettingsSectionViewModel limits = AddSection(new("工具次数", "Tool calls"));
		AddField(
			limits,
			"maxToolIterations",
			new("单轮最多用几次工具", "Tool calls per turn"),
			new(
				"单轮回复中连续调用工具的次数上限。调高不影响不使用工具的对话：循环在模型停止调用工具时结束。",
				"Maximum consecutive tool calls in one reply. Raising it does not affect turns without tool use; the loop ends when the model stops calling tools."),
			// 使用数字框而非滑块：当前呈现层的滑块不显示数值，而该设置项的判读依赖具体数字。
			SettingsEditorKind.Number,
			snapshot => SettingsSnapshotReader.Number(
				snapshot,
				Core.Agent.AgentEngine.DefaultToolIterations,
				"workspace",
				"maxToolIterations"),
			(double)Core.Agent.AgentEngine.DefaultToolIterations,
			(value, token) => ExecuteAsync(
				"settings_update_workspace",
				new { maxToolIterations = (int)Convert.ToDouble(value) },
				token),
			minimum: Core.Agent.AgentEngine.MinToolIterations,
			maximum: Core.Agent.AgentEngine.MaxToolIterationsLimit,
			increment: 1);
	}

	/// <summary>
	/// 当前真正生效的那一档。
	///
	/// 与上面的下拉分开显示，理由和读屏那两行一样：下拉是「你选的」，这一行是
	/// 「现在按什么走」。完全放行到期之后两者会不一样，只显示一个的话，用户看到
	/// 还写着完全放行却仍然被弹框，只能怀疑是坏了。
	/// </summary>
	private string GearStateText(JsonElement snapshot)
	{
		if (SettingsSnapshotReader.Boolean(snapshot, false, "workspace", "permissions", "safeMode"))
		{
			return IsEnglish
				? "Safe mode: every action that needs confirmation is refused, whatever this is set to."
				: "安全模式：需要确认的操作一律拒绝，这里选什么都不算数。";
		}

		string stored = SettingsSnapshotReader.String(snapshot, GearOptionAsk, "workspace", "permissions", "gear");
		string effective = SettingsSnapshotReader.String(snapshot, GearOptionAsk, "workspace", "permissions", "effective");
		if (stored != effective)
		{
			return IsEnglish
				? "Bypass has expired; running as Full. Pick it again for another 4 hours."
				: "完全放行已到期，现在按完全授权走。要继续就再选一次。";
		}

		if (stored == "bypass")
		{
			int seconds = (int)SettingsSnapshotReader.Number(
				snapshot, 0, "workspace", "permissions", "bypassRemainingSeconds");
			int minutes = Math.Max(1, seconds / 60);
			return IsEnglish
				? $"Nothing will be asked for the next {minutes} min, mouse and keyboard takeover included."
				: $"接下来 {minutes} 分钟内她做什么都不问你，包括接管鼠标键盘。";
		}

		return stored switch
		{
			"trusted" => IsEnglish
				? "Everyday actions run without asking; taking over your mouse or keyboard still asks."
				: "日常操作直接做，接管鼠标键盘仍然会问你。",
			"session" => IsEnglish
				? "Each tool asks once per reply, then stays allowed until that reply ends."
				: "每个工具在一轮回复里只问一次，这轮结束后重新开始问。",
			_ => IsEnglish ? "Every action that needs confirmation asks first." : "每一次需要确认的操作都会先问你。",
		};
	}

	/// <summary>
	/// 读屏能不能用。
	///
	/// 与开关分开显示：开关是「要不要」，这一行是「能不能」。两者混在一起时，用户打开了开关
	/// 却没反应，只能怀疑是坏了 —— 实际原因可能是当前模型不支持看图。
	/// </summary>
	private string ScreenAvailabilityText(JsonElement snapshot) =>
		SettingsSnapshotReader.Boolean(snapshot, false, "workspace", "screenAvailable")
			? IsEnglish ? "Ready" : "可用"
			: IsEnglish
				? "Unavailable — this platform has no capture support, or the current model cannot read images"
				: "不可用 —— 当前平台不支持截屏，或当前模型不支持看图";

	/// <summary>
	/// 把隔离强度翻成用户能判断的话。
	///
	/// 这条是安全信息而不是装饰：无隔离时命令拥有你的全部权限，与 AppContainer 下「只能读写
	/// 工作文件夹、默认不联网」是两回事。界面上不说，用户在 Linux 与 macOS 上无从察觉这个差别。
	///
	/// 文案放在呈现层而不是 <c>ISandboxLauncher</c> 上：那是 Core 的契约，给不出双语。
	/// </summary>
	private string IsolationText(JsonElement snapshot) =>
		SettingsSnapshotReader.String(snapshot, "", "workspace", "isolation") switch
		{
			"appcontainer" => IsEnglish
				? "Sandboxed (AppContainer) — limited to the working folder, no network"
				: "受限执行（AppContainer）—— 只能读写工作文件夹，默认无法联网",
			"none" => IsEnglish
				? "Not sandboxed — commands run with your full permissions; only configure commands you trust"
				: "无隔离 —— 命令以你的身份运行，可访问全部文件与网络；只配置你信任的命令",
			_ => IsEnglish ? "Unknown" : "未知",
		};

	/// <summary>
	/// 把快照里的任务清单渲染成「名称 = 命令」的文本。
	///
	/// 用多行文本而不是列表控件：任务是一组名值对，行文本的编辑成本低于增删行按钮，
	/// 且改名与新增在整份替换的语义下本来就没有区别。
	/// </summary>
	private static string FormatTasks(JsonElement snapshot)
	{
		if (SettingsSnapshotReader.Get(snapshot, "workspace", "tasks") is not {ValueKind: JsonValueKind.Array} list)
		{
			return "";
		}

		return string.Join(
			Environment.NewLine,
			list.EnumerateArray().Select(entry =>
				(entry.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? "" : "")
					+ " = "
					+ (entry.TryGetProperty("command", out JsonElement command) ? command.GetString() ?? "" : "")));
	}

	/// <summary>
	/// 解析「名称 = 命令」文本。
	///
	/// 按首个等号拆分：命令行里常含等号（`-p:Foo=Bar`），按最后一个或全部拆会把命令截断。
	/// 空行与 `#` 开头的行跳过，便于用户临时注释掉一条而不必删除。
	/// </summary>
	private static IReadOnlyList<object> ParseTasks(string? text)
	{
		List<object> tasks = [];
		foreach (string line in (text ?? "").Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

			int separator = trimmed.IndexOf('=', StringComparison.Ordinal);
			if (separator <= 0) continue;

			tasks.Add(new
			{
				name = trimmed[..separator].Trim(),
				command = trimmed[(separator + 1)..].Trim(),
			});
		}

		return tasks;
	}

	/// <summary>
	/// 打开系统文件夹选择对话框，选定后写回配置。
	///
	/// 用户取消时不做任何变更，特别是不清空已有配置。
	/// </summary>
	private async Task PickAsync()
	{
		try
		{
			JsonElement picked = await ExecuteAsync(
				"settings_pick_workspace", cancellationToken: LifetimeToken).ConfigureAwait(false);
			if (picked.ValueKind != JsonValueKind.Object
				|| !picked.TryGetProperty("root", out JsonElement root)
				|| root.ValueKind != JsonValueKind.String)
			{
				return;
			}

			await ExecuteAsync(
				"settings_update_workspace",
				new { root = root.GetString() ?? "" },
				LifetimeToken).ConfigureAwait(false);
			// 不在此处刷新页面：`settings_update_workspace` 已经作废快照，
			// 设置服务的 StateChanged 会驱动重新读取。
		}
		catch (OperationCanceledException)
		{
			// 设置窗口关闭时正常取消，不报错。
		}
		catch (Exception exception)
		{
			SetStatus(exception.Message);
		}
	}
}
