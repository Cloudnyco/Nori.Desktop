# Nori.Live2D：自有原生模型数据层

这是逐步替换旧 SDK 的第一片实现，不是完整的 Cubism SDK 替代品。项目仅依赖 .NET，不引用 `Live2DCSharpSDK.*`、Avalonia 或 OpenGL；实际桌宠与模型预览均通过旧模型薄适配器使用它，不是独立演示代码。

## 当前职责

- `NativeMethods` / `NativeRuntime`：固定 PurismCore v1.1.0 v6 ABI 的 P/Invoke、版本与日志回调。
- `ModelMemory`：使用 .NET `NativeMemory.AlignedAlloc`，分别满足 MOC 64 字节、模型 16 字节对齐；`SafeHandle` 成对拥有两块内存，先释放模型再释放 MOC。导入时始终执行原生一致性检查，不再允许旧可选参数跳过。
- `NativeModel`：参数/部件/绘制对象 ID、参数加权及范围、参数快照、虚拟 ID、画布、顶点/UV/索引、遮罩与变化标记。每实例独占数据，更新顺序固定为 reset → update。

同一实例由调用方串行读写与释放，沿用现有宿主同步边界；没有新增全局模型状态或后端选择配置。借出的裸指针不能跨越 Dispose，借用期间调用方必须保持模型存活；安全的索引方法在访问原生内存前检查范围和释放状态。

## 来源和许可边界

ABI 声明、对齐和位标记依据 PurismCore 的 MIT 公共头文件：

- [include/PurismCore.h](https://github.com/SakuraMotion/PurismCore/blob/1069334965522df5d0b791e97f01e26b19c45456/include/PurismCore.h)
- 固定提交：`1069334965522df5d0b791e97f01e26b19c45456`（v1.1.0）
- Copyright (c) 2026 Sakura Motion Project
- [MIT 全文](../Live2D/PurismCore.LICENSE.txt)，随原生库一起发布；[二进制来源及哈希](../Live2D/README.md)。

托管生命周期与数据访问实现按上述公共 API、.NET 内存管理和 Nori 消费端契约编写。该层采用仓库自身 GPLv3 许可，保留公共声明来源的 MIT 归属；不把 PurismCore 重新声明为 GPL。

旧 `Core/CubismCore.cs` 已删除；`CubismMoc` / `CubismModel` 仅作必要兼容转发。旧 Framework 中的动作、物理、姿势、数学与裁剪，以及 App 资源加载和 OpenGL 渲染仍保留，其来源/许可问题不因本片完成而消失。兼容适配器也不视为已经通过独立来源审查；本次不作法律意义上的 clean-room 认证。

## 回归

在 `app/desktop/` 执行：

```bash
NORI_TEST_LIVE2D_ASSETS=1 dotnet test Nori.Desktop.Tests -c Release -m:1
```

真实模型沿用 `NORI_LIVE2D_FIXTURES` 或本地 `data/resources/installed/live2d`，不加入源码仓库。`NativeModelTests` 覆盖无旧框架初始化的模型创建、双实例隔离、参数/虚拟 ID、重复释放和释放后访问、越界、损坏/截断输入及不可跳过一致性检查；已有准备与遮罩测试覆盖实际加载路径及失败回滚。

本片在 Windows 上验证：Desktop Release 构建成功；启用真实资源后 553 项测试通过、0 跳过；C# LSP 检查 8 个文件无诊断。一方注释检查通过，Windows x64 FDD 发布包含 `Nori.Live2D.dll` 和 MIT 许可。仓库外 GL harness 复用宿主加载及绘制代码，对两模型 × 普通/高精度遮罩 × 720×480/1920×1080 各运行 60 帧，无 GL 错误；首末帧均有可见模型和透明区域，抽查截图无明显缺失。1080p 使用离屏目标，临时程序和私有截图不加入仓库。

未启用真实资源时相关测试明确跳过，不能据此声称模型验证通过。Linux/macOS 原生运行、完整桌宠长时间交互及其余 SDK 层重写不属于本片已完成的验证。
