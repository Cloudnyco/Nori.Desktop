using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Nori.Core.Security;

/// <summary>
/// macOS 钥匙串读写。密码通过 Security 框架传入，不出现在进程参数里。
/// 存的是 ASCII 十六进制，和 <c>security find-generic-password -w</c> 的读取格式一致。
/// 调用期间关闭用户交互，避免无界面环境卡在授权对话框。
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacKeychainStore
{
	private const int ErrSecSuccess = 0;
	private const int ErrSecDuplicateItem = -25299;
	private const int ErrSecItemNotFound = -25300;
	private const int MaxPasswordBytes = 256;
	private const string Security = "/System/Library/Frameworks/Security.framework/Security";
	private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
	private static readonly Lock Gate = new();

	/// <summary>读出通用密码的原始字节。不存在或无法读取时返回 null。</summary>
	public static byte[]? TryReadGenericPassword(string service, string account)
	{
		lock (Gate)
		{
			using InteractionScope interaction = InteractionScope.Suppress();
			if (!interaction.Active) return null;
			try
			{
				byte[] serviceBytes = Encoding.UTF8.GetBytes(service);
				byte[] accountBytes = Encoding.UTF8.GetBytes(account);
				int found = SecKeychainFindGenericPassword(
					IntPtr.Zero,
					(uint)serviceBytes.Length,
					serviceBytes,
					(uint)accountBytes.Length,
					accountBytes,
					out uint length,
					out IntPtr passwordData,
					out IntPtr item);
				if (found != ErrSecSuccess) return null;
				try
				{
					if (passwordData == IntPtr.Zero || length == 0 || length > MaxPasswordBytes) return null;
					byte[] payload = new byte[length];
					Marshal.Copy(passwordData, payload, 0, (int)length);
					return payload;
				}
				finally
				{
					if (passwordData != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
					Release(item);
				}
			}
			catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
			{
				return null;
			}
		}
	}

	/// <summary>写入或更新通用密码。失败返回 false，调用方回退到文件。</summary>
	public static bool TryWriteGenericPassword(string service, string account, ReadOnlySpan<byte> secret)
	{
		lock (Gate)
		{
			using InteractionScope interaction = InteractionScope.Suppress();
			if (!interaction.Active) return false;
			return TryWriteUnlocked(service, account, secret);
		}
	}

	private static bool TryWriteUnlocked(string service, string account, ReadOnlySpan<byte> secret)
	{
		try
		{
			byte[] serviceBytes = Encoding.UTF8.GetBytes(service);
			byte[] accountBytes = Encoding.UTF8.GetBytes(account);
			byte[] payload = secret.ToArray();
			int found = SecKeychainFindGenericPassword(
				IntPtr.Zero,
				(uint)serviceBytes.Length,
				serviceBytes,
				(uint)accountBytes.Length,
				accountBytes,
				out _,
				out IntPtr passwordData,
				out IntPtr item);
			if (found == ErrSecSuccess)
				return UpdateItem(item, passwordData, payload);
			if (found != ErrSecItemNotFound)
				return false;

			int added = SecKeychainAddGenericPassword(
				IntPtr.Zero,
				(uint)serviceBytes.Length,
				serviceBytes,
				(uint)accountBytes.Length,
				accountBytes,
				(uint)payload.Length,
				payload,
				out IntPtr created);
			if (added == ErrSecSuccess)
			{
				Release(created);
				return true;
			}
			if (added != ErrSecDuplicateItem)
				return false;

			int again = SecKeychainFindGenericPassword(
				IntPtr.Zero,
				(uint)serviceBytes.Length,
				serviceBytes,
				(uint)accountBytes.Length,
				accountBytes,
				out _,
				out IntPtr existingPassword,
				out IntPtr existingItem);
			return again == ErrSecSuccess && UpdateItem(existingItem, existingPassword, payload);
		}
		catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
		{
			return false;
		}
	}

	private static bool UpdateItem(IntPtr item, IntPtr passwordData, byte[] payload)
	{
		if (passwordData != IntPtr.Zero)
			SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
		if (item == IntPtr.Zero) return false;
		try
		{
			return SecKeychainItemModifyContent(item, IntPtr.Zero, (uint)payload.Length, payload) == ErrSecSuccess;
		}
		finally
		{
			Release(item);
		}
	}

	private static void Release(IntPtr item)
	{
		if (item != IntPtr.Zero) CFRelease(item);
	}

	/// <summary>暂时禁止钥匙串弹窗，结束时恢复原来的开关。</summary>
	private readonly struct InteractionScope : IDisposable
	{
		private readonly byte _previous;
		public bool Active { get; }

		private InteractionScope(byte previous, bool active)
		{
			_previous = previous;
			Active = active;
		}

		public static InteractionScope Suppress()
		{
			try
			{
				byte previous = 1;
				if (SecKeychainGetUserInteractionAllowed(out byte current) == ErrSecSuccess) previous = current;
				if (SecKeychainSetUserInteractionAllowed(0) != ErrSecSuccess)
					return new InteractionScope(previous, false);
				return new InteractionScope(previous, true);
			}
			catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
			{
				return new InteractionScope(1, false);
			}
		}

		public void Dispose()
		{
			if (!Active) return;
			try
			{
				SecKeychainSetUserInteractionAllowed(_previous);
			}
			catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
			{
			}
		}
	}

	[DllImport(Security)]
	private static extern int SecKeychainGetUserInteractionAllowed(out byte state);

	[DllImport(Security)]
	private static extern int SecKeychainSetUserInteractionAllowed(byte state);

	[DllImport(Security)]
	private static extern int SecKeychainFindGenericPassword(
		IntPtr keychainOrArray,
		uint serviceNameLength,
		byte[] serviceName,
		uint accountNameLength,
		byte[] accountName,
		out uint passwordLength,
		out IntPtr passwordData,
		out IntPtr itemRef);

	[DllImport(Security)]
	private static extern int SecKeychainItemModifyContent(
		IntPtr itemRef,
		IntPtr attributeList,
		uint length,
		byte[] data);

	[DllImport(Security)]
	private static extern int SecKeychainAddGenericPassword(
		IntPtr keychain,
		uint serviceNameLength,
		byte[] serviceName,
		uint accountNameLength,
		byte[] accountName,
		uint passwordLength,
		byte[] passwordData,
		out IntPtr itemRef);

	[DllImport(Security)]
	private static extern int SecKeychainItemFreeContent(IntPtr attributeList, IntPtr data);

	[DllImport(CoreFoundation)]
	private static extern void CFRelease(IntPtr value);
}
