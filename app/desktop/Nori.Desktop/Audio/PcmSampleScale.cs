namespace Nori.Desktop.Audio;

/// <summary>
/// 把浮点样本乘上音量。设备不提供音量接口时，在送进声卡前用这里收成目标格式。
/// </summary>
internal static class PcmSampleScale
{
	/// <summary>乘上音量，不改变采样格式。</summary>
	internal static void Apply(ReadOnlySpan<float> source, Span<float> target, double volume)
	{
		if (target.Length < source.Length) throw new ArgumentException("目标缓冲小于样本数", nameof(target));
		float gain = (float) volume;
		for (int index = 0; index < source.Length; index++)
			target[index] = source[index] * gain;
	}

	/// <summary>乘上音量后夹到 [-1, 1]，再收成 16 位整数。越界不夹会绕回成爆音。</summary>
	internal static void ToInt16(ReadOnlySpan<float> source, Span<short> target, double volume)
	{
		if (target.Length < source.Length) throw new ArgumentException("目标缓冲小于样本数", nameof(target));
		for (int index = 0; index < source.Length; index++)
		{
			double sample = Math.Clamp(source[index] * volume, -1, 1);
			target[index] = (short) Math.Round(sample * 32767);
		}
	}

	/// <summary>把 16 位整数还原成 [-1, 1] 浮点，供上层重采样。</summary>
	internal static void FromInt16(ReadOnlySpan<short> source, Span<float> target)
	{
		if (target.Length < source.Length) throw new ArgumentException("目标缓冲小于样本数", nameof(target));
		for (int index = 0; index < source.Length; index++)
			target[index] = source[index] / 32768f;
	}
}
