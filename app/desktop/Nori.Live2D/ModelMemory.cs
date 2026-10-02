using System.Runtime.InteropServices;

namespace Nori.Live2D;

/// <summary>成对持有 MOC 与模型内存，释放顺序与创建顺序相反。</summary>
internal sealed unsafe class ModelMemory : SafeHandle
{
	private readonly nint _moc;
	private readonly nint _model;

	private ModelMemory(nint moc, nint model, nint instance) : base(0, ownsHandle: true)
	{
		_moc = moc;
		_model = model;
		SetHandle(instance);
	}

	public override bool IsInvalid => handle == 0;

	internal static ModelMemory Create(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 4 || !bytes[..4].SequenceEqual("MOC3"u8))
			throw new InvalidDataException("Live2D 模型不是有效的 MOC3 文件。");

		nint mocMemory = 0, modelMemory = 0;
		try
		{
			mocMemory = Allocate((uint)bytes.Length, 64);
			bytes.CopyTo(new Span<byte>((void*)mocMemory, bytes.Length));
			// 本地导入也属于不可信输入；不能通过旧 SDK 的可选开关跳过检查。
			if (NativeMethods.HasMocConsistency(mocMemory, (uint)bytes.Length) != 1)
				throw new InvalidDataException("Live2D 模型 MOC3 一致性检查失败。");
			nint moc = NativeMethods.ReviveMocInPlace(mocMemory, (uint)bytes.Length);
			if (moc == 0) throw new InvalidDataException("Live2D 模型 MOC3 初始化失败。");

			uint size = NativeMethods.GetSizeofModel(moc);
			if (size == 0 || size > int.MaxValue)
				throw new InvalidDataException("Live2D 模型内存大小无效。");
			modelMemory = Allocate(size, 16);
			nint instance = NativeMethods.InitializeModelInPlace(moc, modelMemory, size);
			if (instance == 0) throw new InvalidDataException("Live2D 原生模型创建失败。");

			var result = new ModelMemory(mocMemory, modelMemory, instance);
			mocMemory = modelMemory = 0;
			return result;
		}
		finally
		{
			NativeMemory.AlignedFree((void*)modelMemory);
			NativeMemory.AlignedFree((void*)mocMemory);
		}
	}

	private static nint Allocate(uint size, nuint alignment)
	{
		// 向上补齐，兼容要求 size 为 alignment 整数倍的平台分配器。
		nuint paddedSize = checked(((nuint)size + alignment - 1) / alignment * alignment);
		void* memory = NativeMemory.AlignedAlloc(paddedSize, alignment);
		if (memory == null) throw new OutOfMemoryException("Live2D 原生内存分配失败。");
		return (nint)memory;
	}

	protected override bool ReleaseHandle()
	{
		NativeMemory.AlignedFree((void*)_model);
		NativeMemory.AlignedFree((void*)_moc);
		handle = 0;
		return true;
	}
}
