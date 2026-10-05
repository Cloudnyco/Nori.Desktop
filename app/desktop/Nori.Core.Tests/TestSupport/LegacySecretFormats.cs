using Nori.Core.Security;

namespace Nori.Core.Tests.TestSupport;

internal static class LegacySecretFormats
{
	/// <summary>
	/// 测试本地的 nsec1 造数: base64(nonce|cipher|tag), 无 AAD, 与已发布格式一致。
	/// 不能改用生产 nsec2 入口，否则无法覆盖旧格式读取与迁移。
	/// </summary>
	public static string ProtectNsec1(byte[] key, string plainText)
	{
		const int nonceSize = 12;
		const int tagSize = 16;
		byte[] nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(nonceSize);
		byte[] plain = System.Text.Encoding.UTF8.GetBytes(plainText);
		byte[] cipher = new byte[plain.Length];
		byte[] tag = new byte[tagSize];
		using System.Security.Cryptography.AesGcm aes = new(key, tagSize);
		aes.Encrypt(nonce, plain, cipher, tag);
		byte[] payload = new byte[nonceSize + cipher.Length + tagSize];
		nonce.CopyTo(payload.AsSpan(0, nonceSize));
		cipher.CopyTo(payload.AsSpan(nonceSize, cipher.Length));
		tag.CopyTo(payload.AsSpan(nonceSize + cipher.Length, tagSize));
		return SecretProtector.LegacyNsec1Prefix + Convert.ToBase64String(payload);
	}
}
