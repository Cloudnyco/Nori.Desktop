using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nori.Live2D;

/// <summary>PurismCore 版本和进程级日志；模型数据不存放在这里。</summary>
public static class NativeRuntime
{
	private static readonly NativeMethods.LogCallback Callback = ReceiveLog;
	private static Action<string>? _logFunction;

	public static uint Version => NativeMethods.GetVersion();
	public static uint ImplementationVersion => NativeMethods.GetTrueVersion();
	public static Action<string>? LogFunction => Volatile.Read(ref _logFunction);

	public static void SetLogFunction(Action<string>? callback)
	{
		Volatile.Write(ref _logFunction, callback);
		NativeMethods.SetLogFunction(callback is null ? null : Callback);
	}

	private static void ReceiveLog(nint message)
	{
		try { LogFunction?.Invoke(Marshal.PtrToStringUTF8(message) ?? ""); }
		catch (Exception error)
		{
			// 托管日志异常不能穿越原生回调边界。
			Debug.WriteLine($"Live2D 原生日志回调失败：{error.GetType().Name}");
		}
	}
}
