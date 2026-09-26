using Nori.Desktop.Audio;

namespace Nori.Desktop.Tests;

public sealed class PcmBlockingBufferTests
{
	[Fact]
	public void 写入后能原样读出()
	{
		using PcmBlockingBuffer buffer = new(8);
		Assert.Equal(4, buffer.Write([1f, 2f, 3f, 4f], CancellationToken.None));
		float[] destination = new float[4];
		Assert.Equal(4, buffer.TryRead(destination));
		Assert.Equal([1f, 2f, 3f, 4f], destination);
	}

	[Fact]
	public void 环绕后仍保持顺序()
	{
		using PcmBlockingBuffer buffer = new(4);
		Assert.Equal(3, buffer.Write([1f, 2f, 3f], CancellationToken.None));
		float[] first = new float[2];
		Assert.Equal(2, buffer.TryRead(first));
		Assert.Equal(3, buffer.Write([4f, 5f, 6f], CancellationToken.None));
		float[] rest = new float[4];
		Assert.Equal(4, buffer.TryRead(rest));
		Assert.Equal([3f, 4f, 5f, 6f], rest);
	}

	[Fact]
	public async Task 缓冲满时停止会打断写入()
	{
		using PcmBlockingBuffer buffer = new(4);
		Assert.Equal(4, buffer.Write([1f, 2f, 3f, 4f], CancellationToken.None));
		Task<int> pending = Task.Run(() => buffer.Write([5f, 6f], CancellationToken.None));
		await WaitUntilAsync(() => !pending.IsCompleted);
		buffer.Stop();
		int written = await pending.WaitAsync(TimeSpan.FromSeconds(2));
		Assert.Equal(0, written);
	}

	[Fact]
	public async Task 没有数据时停止会打断读取()
	{
		using PcmBlockingBuffer buffer = new(4);
		Task<int> pending = Task.Run(() => buffer.Read(new float[2], CancellationToken.None));
		await WaitUntilAsync(() => !pending.IsCompleted);
		buffer.Stop();
		Assert.Equal(0, await pending.WaitAsync(TimeSpan.FromSeconds(2)));
	}

	[Fact]
	public void 取消的写入不会塞进缓冲()
	{
		using PcmBlockingBuffer buffer = new(4);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		Assert.Equal(0, buffer.Write([1f, 2f], cancellation.Token));
		Assert.Equal(0, buffer.TryRead(new float[2]));
	}

	[Fact]
	public async Task 排空会等到缓冲被取走()
	{
		using PcmBlockingBuffer buffer = new(4);
		buffer.Write([1f, 2f], CancellationToken.None);
		Task draining = Task.Run(() => buffer.Drain(CancellationToken.None));
		await WaitUntilAsync(() => !draining.IsCompleted);
		Assert.Equal(2, buffer.TryRead(new float[2]));
		await draining.WaitAsync(TimeSpan.FromSeconds(2));
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
		while (!condition())
		{
			await Task.Delay(10, timeout.Token);
		}
	}
}
