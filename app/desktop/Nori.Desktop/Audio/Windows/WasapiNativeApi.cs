using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nori.Desktop.Audio.Windows;

/// <summary>
/// WASAPI 的 COM 接口与结构体。
///
/// 手写而不是引第三方库：这里要的只是「打开默认输出、把 float 样本推进去」，
/// 而能跨三平台的音频库（miniaudio / SDL / OpenAL）都要带一份原生二进制，
/// 打包、签名、每个 RID 各一份都得跟着走。仓库里已经有 83 处 P/Invoke，
/// 这份和它们同一套路。
///
/// **vtable 顺序不能错。** 这些接口都是 IUnknown 系（不是 IInspectable），
/// 方法必须**按 COM 里的声明顺序一条不漏地写全**，用不到的也要占位。
/// 少一条或调换顺序，调用会落到相邻的方法上 —— 不报错，直接崩或者返回垃圾。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WasapiNativeApi
{
	internal static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
	internal static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
	internal static readonly Guid IidAudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

	/// <summary>WAVE_FORMAT_IEEE_FLOAT。共享模式的混音格式几乎总是它。</summary>
	internal const ushort FormatFloat = 3;

	/// <summary>WAVE_FORMAT_EXTENSIBLE；真正的格式在 SubFormat 里。</summary>
	internal const ushort FormatExtensible = 0xFFFE;

	internal const int ShareModeShared = 0;

	/// <summary>缓冲时长的单位是 100 纳秒。</summary>
	internal const long HundredNanosecondsPerMillisecond = 10_000;

	/// <summary>eRender：输出端。</summary>
	internal const int DataFlowRender = 0;

	/// <summary>eCapture：输入端。</summary>
	internal const int DataFlowCapture = 1;

	/// <summary>eConsole：用户日常使用的那个设备，跟系统默认走。</summary>
	internal const int RoleConsole = 0;

	[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	internal class MmDeviceEnumerator { }

	[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMmDeviceEnumerator
	{
		// 用不到，但槽位必须占住。
		void EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);

		void GetDefaultAudioEndpoint(int dataFlow, int role,
			[MarshalAs(UnmanagedType.Interface)] out IMmDevice device);

		void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id,
			[MarshalAs(UnmanagedType.Interface)] out IMmDevice device);

		void RegisterEndpointNotificationCallback(IntPtr client);
		void UnregisterEndpointNotificationCallback(IntPtr client);
	}

	[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMmDevice
	{
		void Activate([In] ref Guid iid, uint classContext, IntPtr activationParams,
			[MarshalAs(UnmanagedType.IUnknown)] out object instance);

		void OpenPropertyStore(uint access, out IntPtr properties);
		void GetId(out IntPtr id);
		void GetState(out uint state);
	}

	[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IAudioClient
	{
		void Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity,
			IntPtr format, IntPtr audioSessionGuid);

		void GetBufferSize(out uint bufferFrameCount);
		void GetStreamLatency(out long latency);
		void GetCurrentPadding(out uint paddingFrames);
		void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

		/// <summary>设备当前的混音格式。返回的指针要用 CoTaskMemFree 放掉。</summary>
		void GetMixFormat(out IntPtr deviceFormat);

		void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
		void Start();
		void Stop();
		void Reset();
		void SetEventHandle(IntPtr eventHandle);

		void GetService([In] ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
	}

	[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IAudioRenderClient
	{
		void GetBuffer(uint requestedFrames, out IntPtr buffer);
		void ReleaseBuffer(uint writtenFrames, uint flags);
	}

	/// <summary>
	/// WAVEFORMATEX。
	///
	/// <c>GetMixFormat</c> 返回的实际上常是 WAVEFORMATEXTENSIBLE（前 18 字节与这个
	/// 结构一致，后面还有 22 字节）。我们只读前半段，并按 <c>cbSize</c> 判断要不要
	/// 去 SubFormat 里取真正的格式。
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	internal struct WaveFormatEx
	{
		internal ushort FormatTag;
		internal ushort Channels;
		internal uint SamplesPerSecond;
		internal uint AverageBytesPerSecond;
		internal ushort BlockAlign;
		internal ushort BitsPerSample;
		internal ushort ExtraSize;
	}

	/// <summary>WAVEFORMATEXTENSIBLE 里 SubFormat 相对结构体起点的偏移。</summary>
	internal const int SubFormatOffset = 18 + 2 + 4;

	internal static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00AA00389B71");

	[DllImport("ole32.dll")]
	internal static extern void CoTaskMemFree(IntPtr block);

	/// <summary>AUDCLNT_E_DEVICE_INVALIDATED：设备被拔了或被改了配置。</summary>
	internal const int DeviceInvalidated = unchecked((int) 0x88890004);
}
