# PurismCore 可行性与性能评估

## 决定与结论

本次评估曾在本地 `experimental` 分支验证 PurismCore v1.1.0。现已决定结束该实验、删除分支，**仅在 main 保留结论，不保留原生库替换、兼容改动或新增测试**。main 继续使用原官方 Cubism Core。

- **技术上可作为替代方案继续研究，并非仅支持 Windows。** 上游 v6 产物覆盖 Nori 的 win-x64、linux-x64、linux-arm64、osx-x64、osx-arm64；macOS 可使用 universal 库。
- **没有证明性能更好。** 本机纯 Core 更新耗时比官方库高约 9%～11%，绝对差约 0.017～0.024 ms/帧；包含物理与 GL 绘制后的帧耗时相近。
- **主要价值是开放源码及跨平台构建可控性，不是已证实的提速。** 不建议仅以性能为理由替换当前稳定方案。
- **可以修改源码优化。** MIT 允许修改、重新编译和分发，需保留版权及许可声明；其余 SDK、模型资产及官方 Core 的许可不随此改变。本次未修改 PurismCore 源码，也未验证自定义优化补丁。

以下是已结束实验的历史证据，不代表 main 当前已经集成 PurismCore。

## 评估基线与来源

- Nori 基线：`a291e1ee65e1931f991d53576f160ad6959324a4`。
- 上游：[SakuraMotion/PurismCore v1.1.0](https://github.com/SakuraMotion/PurismCore/releases/tag/v1.1.0)。
- 上游提交：`1069334965522df5d0b791e97f01e26b19c45456`。
- 归档：`PurismCore-v1.1.0.zip`，SHA-256 为 `ae1b32af28a06b27f47cda61f8b5f5d4caa047dc856cdde1d6d839b850b6a70c`。
- 实验使用 `dll/` 下的 **v6 ABI**，不是 `dll-v5/`。二进制仅改部署文件名，未修改内容。

以下归档路径均以 `PurismCore-v1.1.0/` 为前缀：

| RID | 归档内路径 | 二进制 SHA-256 |
| --- | --- | --- |
| win-x64 | `dll/windows/x86_64/PurismCore.dll` | `261c484adfea81cf59eb2aaa051b55be3a8576567b7285c770c194a79df4f6af` |
| linux-x64 | `dll/linux/x86_64/libPurismCore.so` | `237b36498e85d8e5fe0278418af93b794a063e2a96dc17409a3058eaf4ec90e4` |
| linux-arm64 | `dll/linux/arm64/libPurismCore.so` | `d4cedff155f441cb1bb35d538b6d0db6c3472484d3c3282d9a5f963e87309497` |
| osx-x64、osx-arm64 | `dll/macos/universal/libPurismCore.dylib` | `8f9336f5b62e8ceb575c965cbd26dcd63743c4068cf1fc1905d2ee3af12110db` |

静态检查确认 Linux 两份库分别为 ELF64 x86-64、AArch64，最高 GLIBC 版本需求均为 **GLIBC_2.27**（来自 `libm.so.6`）；macOS universal 库包含 x86_64 与 arm64。归档提取目标为真实二进制，不是符号链接目标文本。

## 兼容性发现

### API 与接入范围

当前 C# 绑定使用 `csmGetRenderOrders`，实验因此选择 v6 ABI。所需的 35 个导出符号均在实际 Windows DLL 中确认存在。官方基线与 PurismCore 的 `csmGetVersion` 均为 `0x06000001`，不能仅凭这个值辨认后端；PurismCore 的 `csmGetTrueVersion` 为 `0x01010000`。

实验沿用现有 C# SDK、OpenGL 渲染器、模型导入和行为管线，将原生库改名为既有的 `Live2DCubismCore.dll`、`libLive2DCubismCore.so`、`libLive2DCubismCore.dylib`，没有引入后端配置层。

Linux ARM64 在基线项目中缺少原生库复制项；未来重新接入时，需要同时检查显式 RID 发布及未指定 RID 的 Linux 本机构建，避免 ARM64 误用 x64 库。发布时还需将上游 MIT 许可全文复制到原生库同目录。

### 动态标记与遮罩

PurismCore v1.1.0 的 `csmResetDrawableDynamicFlags` 会立即清除除可见性外的变化标记。基线 `CubismModel.Update()` 先更新、后 reset，导致后续遮罩绘制读不到 `VertexPositionsDidChange`，跳过 mask。

实验把顺序改为先 reset、再 update 后，两份模型及遮罩绘制通过检查；原官方 Core 也通过同一调用顺序的检查。**此兼容改动已随实验清理，不在 main 保留。**

上游相关讨论：[Issue #3](https://github.com/SakuraMotion/PurismCore/issues/3)、[PR #4](https://github.com/SakuraMotion/PurismCore/pull/4)。本次没有合入该 PR 的动态标记或性能补丁。

更换 Core 不会自动补齐当前 C# 渲染器尚未实现的 Cubism 5.3 offscreen 和新混合模式。

## 历史验证结果

所有执行均在 Windows 主机完成；交叉发布不等于目标平台实机运行。

- 实验状态下，21 项相关测试通过、无跳过。新增测试覆盖两份真实模型连续三帧的可见性、顶点变化标记、有限顶点、参数驱动形变及有效且唯一的 renderOrder。新增测试已随实验删除，不应把该数量当作 main 的当前测试结果。
- 五个 RID 的 Release framework-dependent 交叉发布成功。逐个检查发布内容：只包含对应 Core，SHA-256 与上表一致，MIT 许可完整随包，组件清单声明 PurismCore/MIT。
- 仓库外临时 Avalonia GL harness 复用 `ModelPreparation`、`LAppDelegateOpenGL`、`AvaloniaGlApi` 与现有投影计算，分别对官方 Core 和 PurismCore 绘制 ARGNori、Nori。
- 每个模型覆盖 720×480、1920×1080，普通和高精度遮罩；每种组合独立进程连续绘制 60 帧，无 GL 错误，首帧和末帧均有非空模型及透明边界。
- 对比 16 对 RGBA 截图：每对有差异的像素低于整图的 0.054%，并排检查未见明显差异。结果并非逐像素完全一致，也不是所有模型的等价性保证。

私有模型资产、临时 harness、截图及基准原始输出未加入仓库；本文保留方法和汇总数据，不依赖本机临时目录。

## 本机性能对比

### 方法与环境

- Windows，AMD Ryzen 5 1600X（6 核 / 12 线程），AMD Radeon RX 580 2048SP，驱动 `31.0.21925.1001`。
- 实际 GL renderer 为 ANGLE / AMD RX 580 / Direct3D11，OpenGL ES 3.0；.NET SDK 10.0.400，Release，两个后端均设 `DOTNET_TieredCompilation=0`。
- 官方基线为 Nori 基线提交中的 Windows DLL（兼容版本 6.0.1），SHA-256 为 `d883c00d114fdf6cef61f439feb23e02d000fdf683e092803010470b80dfaf09`。
- 两个后端使用同一 C# SDK、相同的 reset-before-update 顺序及本地模型，仅替换临时输出目录的 DLL。
- 每个组合独立进程重复三轮，第二轮反转后端顺序。模型加载、纹理解码/上传及截图编码均不计入帧耗时。表中数值为三轮中位数，单位为 **ms/帧，越小越好**。

### 纯 Core 更新

每轮预热 500 帧，再记录 7 组、每组 1000 帧的平均耗时，取七组中位数。参数不变场景仍调用 `CubismModel.Update()`；动态场景以固定周期改变角度、身体、眼球和嘴型六个参数。不包含物理或 GL。

| 模型 | 场景 | 官方 Core | PurismCore | 耗时变化 |
| --- | --- | ---: | ---: | ---: |
| ARGNori | 参数不变 | 0.1788 | 0.1986 | +11.1% |
| ARGNori | 参数变化 | 0.1908 | 0.2078 | +8.9% |
| Nori | 参数不变 | 0.2420 | 0.2655 | +9.7% |
| Nori | 参数变化 | 0.2519 | 0.2747 | +9.0% |

绝对差约为每帧 **0.017～0.024 ms**，不等于整机 CPU 占用增加 9%～11%。

### 同渲染链路的帧耗时

使用 `LAppModel.Update()`（含呼吸、物理）、普通遮罩及 2048×2048 mask；关闭随机动作，用固定步长驱动角度和嘴型。每轮预热 120 帧后记录 600 帧，以每帧 `glFinish` 等待 GPU 完成，统计更新、绘制提交与 GPU 等待总耗时。

| 模型 | 渲染尺寸 | 官方 Core | PurismCore | 耗时变化 |
| --- | --- | ---: | ---: | ---: |
| ARGNori | 720×480 | 2.7464 | 2.7556 | +0.3% |
| ARGNori | 1920×1080 | 2.7641 | 2.6885 | -2.7% |
| Nori | 720×480 | 2.9240 | 2.8528 | -2.4% |
| Nori | 1920×1080 | 2.9593 | 2.9695 | +0.3% |

各组合三轮的单轮中位数范围合计为：官方 2.6708～3.1141 ms，PurismCore 2.6393～3.2411 ms。约 0.01～0.08 ms 的总帧差异不足以声称稳定提速。

此计时不包含 Avalonia 最终合成、屏幕呈现、命中采样、阴影或应用其他服务，不能换算成完整桌面伴侣 FPS。

## 限制与未来重新评估的条件

- **未验证 Linux/macOS 原生运行**，仅完成二进制静态检查及交叉发布。
- 未完成完整 PetWindow 交互、长时间运行、全部动作/表情组合及其他硬件上的性能、功耗、内存验证。
- 临时 harness 在同一 GL 上下文销毁模型后再创建模型时，新旧 Core 均出现 `GL_INVALID_VALUE (1281)`。因此截图与性能检查采用独立进程；没有把该现象归因于 PurismCore，也没有扩大修复。实际模型切换仍需验证。
- 若以后尝试源码优化，应先采样定位参数解析、变形器、网格计算或排序中的实际热点，再审查上游已有补丁、减少可证明的重复计算。不能简单因参数未变化而跳过整帧：呼吸、物理及遮罩动态标记都需要保持正确。
- 当前 Core 约占测得帧耗时的一成；即使其耗时减半，整帧收益也可能只有约 5%，必须重新实测。首次目标应是消除本次约 9%～11% 的更新劣势，而非先重写渲染器或引入 SIMD、多线程。
- 重新采用前，需对修改后的源码及各平台产物重新验证 ABI、动态标记、顶点/顺序、遮罩、截图与性能，不能沿用本报告宣称新版本已经通过。
