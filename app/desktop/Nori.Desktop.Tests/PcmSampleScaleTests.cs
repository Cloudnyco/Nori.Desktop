using Nori.Desktop.Audio;

namespace Nori.Desktop.Tests;

public sealed class PcmSampleScaleTests
{
	[Fact]
	public void 音量乘在浮点样本上()
	{
		float[] target = new float[3];
		PcmSampleScale.Apply([1f, -0.5f, 0.25f], target, 0.5);
		Assert.Equal(0.5f, target[0]);
		Assert.Equal(-0.25f, target[1]);
		Assert.Equal(0.125f, target[2]);
	}

	[Fact]
	public void 十六位转换会夹住越界并按音量缩放()
	{
		short[] target = new short[3];
		PcmSampleScale.ToInt16([1f, -1f, 4f], target, 0.5);
		Assert.Equal((short) Math.Round(0.5 * 32767), target[0]);
		Assert.Equal((short) Math.Round(-0.5 * 32767), target[1]);
		Assert.Equal((short) 32767, target[2]);
	}

	[Fact]
	public void 十六位往返保持符号()
	{
		short[] encoded = new short[2];
		PcmSampleScale.ToInt16([0.5f, -0.5f], encoded, 1);
		float[] decoded = new float[2];
		PcmSampleScale.FromInt16(encoded, decoded);
		Assert.True(decoded[0] > 0.49f);
		Assert.True(decoded[1] < -0.49f);
	}
}
