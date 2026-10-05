using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Nori.Desktop.Audio.Windows;
using Nori.Desktop.Notifications.Windows;

namespace Nori.Desktop.Tests;

/// <summary>结构布局不调用系统库，可以在三个平台上校验。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInteropValueTests
{
	[Fact]
	public void 属性键和音频格式保持原生字段布局()
	{
		Assert.Equal(20, Marshal.SizeOf<WasapiNativeApi.PropertyKey>());
		Assert.Equal(20, Marshal.SizeOf<ToastNativeApi.PropertyKey>());
		Assert.Equal(16, Marshal.OffsetOf<WasapiNativeApi.PropertyKey>("PropertyId").ToInt32());
		Assert.Equal(16, Marshal.OffsetOf<ToastNativeApi.PropertyKey>("PropertyId").ToInt32());
		Assert.Equal(18, Marshal.SizeOf<WasapiNativeApi.WaveFormatEx>());
		string[] fields = ["FormatTag", "Channels", "SamplesPerSecond", "AverageBytesPerSecond", "BlockAlign", "BitsPerSample", "ExtraSize"];
		int[] offsets = [0, 2, 4, 8, 12, 14, 16];
		for (int i = 0; i < fields.Length; i++)
			Assert.Equal(offsets[i], Marshal.OffsetOf<WasapiNativeApi.WaveFormatEx>(fields[i]).ToInt32());
	}

	[Fact]
	public void 变体保持标签与联合体指针的原生布局()
	{
		int expectedSize = 8 + 2 * IntPtr.Size;
		Assert.Equal(expectedSize, Marshal.SizeOf<WasapiNativeApi.PropVariant>());
		Assert.Equal(expectedSize, Marshal.SizeOf<ToastNativeApi.PropVariant>());
		Assert.Equal(0, Marshal.OffsetOf<WasapiNativeApi.PropVariant>("VarType").ToInt32());
		Assert.Equal(0, Marshal.OffsetOf<ToastNativeApi.PropVariant>("VarType").ToInt32());
		Assert.Equal(8, Marshal.OffsetOf<WasapiNativeApi.PropVariant>("Value").ToInt32());
		Assert.Equal(8, Marshal.OffsetOf<ToastNativeApi.PropVariant>("Value").ToInt32());
	}

	[Fact]
	public void 非字符串变体不会解引用联合体中的整数()
	{
		WasapiNativeApi.PropVariant integer = new() {VarType = 3, Value = new IntPtr(1)};
		Assert.Empty(WasapiNativeApi.ReadString(in integer));
		WasapiNativeApi.PropVariant empty = new() {VarType = WasapiNativeApi.VtLpwstr};
		Assert.Empty(WasapiNativeApi.ReadString(in empty));
	}

	[Fact]
	public void 字符串变体读取成功且不转移内存所有权()
	{
		IntPtr text = Marshal.StringToCoTaskMemUni("Nori 音频设备");
		try
		{
			WasapiNativeApi.PropVariant value = new() {VarType = WasapiNativeApi.VtLpwstr, Value = text};
			Assert.Equal("Nori 音频设备", WasapiNativeApi.ReadString(in value));
			Assert.Equal(text, value.Value);
			Assert.Equal("Nori 音频设备", WasapiNativeApi.ReadString(in value));
		}
		finally
		{
			Marshal.FreeCoTaskMem(text);
		}
	}

	public sealed class WindowsHStringFactAttribute : FactAttribute
	{
		public WindowsHStringFactAttribute()
		{
			if (!System.OperatingSystem.IsWindows()) Skip = "HSTRING 句柄由 Windows 系统库分配和释放。";
		}
	}

	[WindowsHStringFact]
	public void 字符串句柄的共享引用只释放一次()
	{
		using ToastNativeApi.HString value = new("Nori");
		Assert.NotEqual(IntPtr.Zero, value.Handle);
		ToastNativeApi.HString alias = value;
		Assert.Same(value, alias);
		value.Dispose();
		Assert.Equal(IntPtr.Zero, alias.Handle);
		alias.Dispose();
		Assert.Equal(IntPtr.Zero, value.Handle);
	}
}
