using Nori.Live2D;

namespace Live2DCSharpSDK.Framework.Model;

/// <summary>旧 SDK 的兼容入口；原生分配、校验和释放由 Nori.Live2D 负责。</summary>
public sealed class CubismMoc : IDisposable
{
	public CubismModel Model { get; }

	public CubismMoc(byte[] mocBytes, bool shouldCheckMocConsistency = false)
	{
		ArgumentNullException.ThrowIfNull(mocBytes);
		// 保留旧调用签名，但不再允许跳过不可信 MOC 的一致性检查。
		Model = new CubismModel(new NativeModel(mocBytes));
	}

	public void Dispose() => Model.Dispose();
}
