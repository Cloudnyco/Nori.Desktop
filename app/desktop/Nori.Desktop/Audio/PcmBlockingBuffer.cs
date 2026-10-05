namespace Nori.Desktop.Audio;

/// <summary>
/// 阻塞的交错浮点环形缓冲。
///
/// 播放线程往里写，声卡回调从里面取。缓冲满或空时最多每 10ms 醒一次，
/// 这样 <see cref="Stop"/> 不必跟声卡 API 抢同一把锁也能打断读写。
/// </summary>
internal sealed class PcmBlockingBuffer : IDisposable
{
	private readonly float[] _samples;
	private readonly Lock _gate = new();
	private readonly ManualResetEventSlim _changed = new(false);
	private int _read;
	private int _count;
	private volatile bool _stopped;
	private bool _disposed;

	internal PcmBlockingBuffer(int capacity)
	{
		if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
		_samples = new float[capacity];
	}

	/// <summary>写入样本。缓冲满时阻塞，被 <see cref="Stop"/> 或取消打断时返回已经写入的数量。</summary>
	internal int Write(ReadOnlySpan<float> samples, CancellationToken cancellationToken)
	{
		if (samples.IsEmpty) return 0;
		int written = 0;
		while (written < samples.Length)
		{
			int copied = 0;
			lock (_gate)
			{
				if (_stopped || _disposed || cancellationToken.IsCancellationRequested) break;
				int free = _samples.Length - _count;
				if (free > 0)
				{
					copied = CopyIn(samples[written..], free);
					_changed.Set();
				}
				else
				{
					_changed.Reset();
				}
			}
			if (copied > 0)
			{
				written += copied;
				continue;
			}
			if (!Wait(cancellationToken)) break;
		}
		return written;
	}

	/// <summary>声卡回调使用：有多少取多少，绝不阻塞。</summary>
	internal int TryRead(Span<float> destination)
	{
		if (destination.IsEmpty) return 0;
		lock (_gate)
		{
			int take = Math.Min(destination.Length, _count);
			if (take == 0) return 0;
			CopyOut(destination[..take]);
			_changed.Set();
			return take;
		}
	}

	/// <summary>声卡回调使用：写得下多少写多少，满了就丢，绝不阻塞音频线程。</summary>
	internal int TryWrite(ReadOnlySpan<float> samples)
	{
		if (samples.IsEmpty) return 0;
		lock (_gate)
		{
			if (_stopped || _disposed) return 0;
			int copied = CopyIn(samples, _samples.Length - _count);
			if (copied > 0) _changed.Set();
			return copied;
		}
	}

	/// <summary>读出样本。没有新数据时阻塞，被打断时返回 0 或已经读到的数量。</summary>
	internal int Read(Span<float> destination, CancellationToken cancellationToken)
	{
		if (destination.IsEmpty) return 0;
		while (true)
		{
			lock (_gate)
			{
				if (_disposed) return 0;
				int take = Math.Min(destination.Length, _count);
				if (take > 0)
				{
					CopyOut(destination[..take]);
					_changed.Set();
					return take;
				}
				if (_stopped) return 0;
				_changed.Reset();
			}
			if (!Wait(cancellationToken)) return 0;
		}
	}

	/// <summary>等到缓冲被取空，或被 <see cref="Stop"/> / 取消打断。</summary>
	internal void Drain(CancellationToken cancellationToken)
	{
		while (true)
		{
			lock (_gate)
			{
				if (_count == 0 || _stopped || _disposed) return;
				_changed.Reset();
			}
			if (!Wait(cancellationToken)) return;
		}
	}

	/// <summary>立刻停，并唤醒阻塞中的读写。</summary>
	internal void Stop()
	{
		_stopped = true;
		try { _changed.Set(); }
		catch (ObjectDisposedException) { }
	}

	public void Dispose()
	{
		Stop();
		lock (_gate)
		{
			if (_disposed) return;
			_disposed = true;
		}
		_changed.Dispose();
	}

	private bool Wait(CancellationToken cancellationToken)
	{
		if (_stopped) return false;
		try
		{
			_changed.Wait(10, cancellationToken);
			return !_stopped && !cancellationToken.IsCancellationRequested;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	private int CopyIn(ReadOnlySpan<float> source, int free)
	{
		int take = Math.Min(source.Length, free);
		if (take <= 0) return 0;
		int writeAt = (_read + _count) % _samples.Length;
		int first = Math.Min(take, _samples.Length - writeAt);
		source[..first].CopyTo(_samples.AsSpan(writeAt, first));
		if (take > first)
			source.Slice(first, take - first).CopyTo(_samples.AsSpan(0, take - first));
		_count += take;
		return take;
	}

	private void CopyOut(Span<float> destination)
	{
		int take = destination.Length;
		int first = Math.Min(take, _samples.Length - _read);
		_samples.AsSpan(_read, first).CopyTo(destination[..first]);
		if (take > first)
			_samples.AsSpan(0, take - first).CopyTo(destination.Slice(first, take - first));
		_read = (_read + take) % _samples.Length;
		_count -= take;
	}
}
