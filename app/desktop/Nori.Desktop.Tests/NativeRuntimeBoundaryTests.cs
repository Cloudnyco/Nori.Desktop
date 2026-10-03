using System.Runtime.InteropServices;
using Nori.Live2D;

namespace Nori.Desktop.Tests;

[Collection("Native settings")]
public sealed class NativeRuntimeBoundaryTests
{
	[Fact]
	public void 原生日志可替换清除重启且托管异常不穿越回调边界()
	{
		Action<string>? previous = NativeRuntime.LogFunction;
		var firstMessages = new List<string>();
		var secondMessages = new List<string>();
		Action<string> firstLog = firstMessages.Add;
		Action<string> secondLog = secondMessages.Add;
		uint version = NativeRuntime.Version;
		uint implementation = NativeRuntime.ImplementationVersion;
		try
		{
			NativeRuntime.SetLogFunction(firstLog);
			Assert.Same(firstLog, NativeRuntime.LogFunction);
			nint pointer = GetLogFunction();
			Assert.NotEqual(nint.Zero, pointer);
			var callback = Marshal.GetDelegateForFunctionPointer<NativeMethods.LogCallback>(pointer);
			Send(callback, "首次回调");
			Assert.Equal(["首次回调"], firstMessages);

			NativeRuntime.SetLogFunction(secondLog);
			Assert.Same(secondLog, NativeRuntime.LogFunction);
			Assert.Equal(pointer, GetLogFunction());
			Send(callback, "替换后回调");
			Assert.Equal(["首次回调"], firstMessages);
			Assert.Equal(["替换后回调"], secondMessages);

			NativeRuntime.SetLogFunction(null);
			Assert.Null(NativeRuntime.LogFunction);
			Assert.Equal(nint.Zero, GetLogFunction());
			Send(callback, "清除后不再记录");
			Assert.Equal(["替换后回调"], secondMessages);

			NativeRuntime.SetLogFunction(_ => throw new InvalidOperationException("日志消费者失败"));
			Assert.NotEqual(nint.Zero, GetLogFunction());
			Assert.Null(Record.Exception(() => Send(callback, "不能穿越原生边界")));

			NativeRuntime.SetLogFunction(secondLog);
			Assert.Same(secondLog, NativeRuntime.LogFunction);
			Assert.Equal(pointer, GetLogFunction());
			Send(callback, "重新启用回调");
			Assert.Equal(["替换后回调", "重新启用回调"], secondMessages);
			Assert.Equal(version, NativeRuntime.Version);
			Assert.Equal(implementation, NativeRuntime.ImplementationVersion);
		}
		finally
		{
			NativeRuntime.SetLogFunction(previous);
		}
		Assert.Same(previous, NativeRuntime.LogFunction);
	}

	private static void Send(NativeMethods.LogCallback callback, string message)
	{
		nint utf8 = Marshal.StringToCoTaskMemUTF8(message);
		try { callback(utf8); }
		finally { Marshal.FreeCoTaskMem(utf8); }
	}

	// 从实际原生库读取回调，避免只核对托管字段却漏掉原生安装/清除。
	[DllImport("Live2DCubismCore", EntryPoint = "csmGetLogFunction", CallingConvention = CallingConvention.Winapi)]
	private static extern nint GetLogFunction();
}
