using System.Numerics;
using System.Runtime.InteropServices;

namespace Nori.Live2D;

/// <summary>
/// Nori 的原生模型数据层；不依赖动作、物理、GL 或旧 SDK。
/// 一个实例独占一份 MOC 和模型。同一实例的读取、更新、释放必须由调用方串行执行。
/// 指针仅供旧渲染器/物理借用，不能缓存到 Dispose 之后；使用期间须保持实例存活。
/// </summary>
public sealed unsafe class NativeModel : IDisposable
{
	private readonly ModelMemory _memory;
	private readonly IReadOnlyList<string> _parameterIds;
	private readonly IReadOnlyList<string> _partIds;
	private readonly IReadOnlyList<string> _drawableIds;
	private readonly Dictionary<string, int> _parameters = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _parts = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _drawables = new(StringComparer.Ordinal);
	private readonly List<float> _virtualParameters = [];
	private readonly List<float> _virtualParts = [];
	private readonly float[] _savedParameters;
	private readonly Vector2 _canvasSize;
	private readonly float _pixelsPerUnit;
	private float _opacity = 1;

	public NativeModel(ReadOnlySpan<byte> mocBytes)
	{
		_memory = ModelMemory.Create(mocBytes);
		try
		{
			_parameterIds = ReadIds(NativeMethods.GetParameterIds(_memory), NativeMethods.GetParameterCount(_memory), _parameters);
			_partIds = ReadIds(NativeMethods.GetPartIds(_memory), NativeMethods.GetPartCount(_memory), _parts);
			_drawableIds = ReadIds(NativeMethods.GetDrawableIds(_memory), NativeMethods.GetDrawableCount(_memory), _drawables);
			NativeMethods.ReadCanvasInfo(_memory, out _canvasSize, out _, out _pixelsPerUnit);
			if (!float.IsFinite(_pixelsPerUnit) || _pixelsPerUnit <= 0 ||
				!float.IsFinite(_canvasSize.X) || _canvasSize.X <= 0 ||
				!float.IsFinite(_canvasSize.Y) || _canvasSize.Y <= 0)
				throw new InvalidDataException("Live2D 模型画布尺寸无效。");
			_savedParameters = new float[_parameterIds.Count];
			SaveParameters();
		}
		catch
		{
			_memory.Dispose();
			throw;
		}
	}

	internal ModelMemory Memory => _memory;
	public bool IsDisposed => _memory.IsClosed;
	public IReadOnlyList<string> ParameterIds { get { CheckDisposed(); return _parameterIds; } }
	public IReadOnlyList<string> PartIds { get { CheckDisposed(); return _partIds; } }
	public IReadOnlyList<string> DrawableIds { get { CheckDisposed(); return _drawableIds; } }
	public int ParameterCount => ParameterIds.Count;
	public int PartCount => PartIds.Count;
	public int DrawableCount => DrawableIds.Count;
	public Vector2 CanvasSize { get { CheckDisposed(); return _canvasSize; } }
	public float PixelsPerUnit { get { CheckDisposed(); return _pixelsPerUnit; } }
	public float Opacity
	{
		get { CheckDisposed(); return _opacity; }
		set { CheckDisposed(); _opacity = value; }
	}

	public void Dispose() => _memory.Dispose();

	public void Update()
	{
		CheckDisposed();
		NativeMethods.ResetDrawableDynamicFlags(_memory);
		NativeMethods.UpdateModel(_memory);
	}

	// 动作与姿势可能引用模型未定义的 ID，保存独立虚拟值，绝不拿虚拟索引访问原生数组。
	public int GetParameterIndex(string id) => FindOrCreate(id, _parameters, _virtualParameters, ParameterCount);
	public int GetPartIndex(string id) => FindOrCreate(id, _parts, _virtualParts, PartCount);
	public int GetDrawableIndex(string id)
	{
		CheckDisposed();
		return _drawables.GetValueOrDefault(id, -1);
	}

	public float GetParameterValue(int index)
	{
		CheckIndex(index, ParameterCount + _virtualParameters.Count);
		return index < ParameterCount ? Read(GetParameterValues(), index) : _virtualParameters[index - ParameterCount];
	}

	public float GetParameterDefaultValue(int index)
	{
		CheckIndex(index, ParameterCount + _virtualParameters.Count);
		return index < ParameterCount ? Read(GetParameterDefaultValues(), index) : 0;
	}

	public void SetParameterValue(int index, float value, float weight = 1)
	{
		float current = GetParameterValue(index);
		if (index < ParameterCount)
		{
			value = Math.Clamp(value, Read(GetParameterMinimumValues(), index), Read(GetParameterMaximumValues(), index));
			GetParameterValues()[index] = current * (1 - weight) + value * weight;
			GC.KeepAlive(this);
		}
		else _virtualParameters[index - ParameterCount] = current * (1 - weight) + value * weight;
	}

	public void AddParameterValue(int index, float value, float weight = 1) =>
		SetParameterValue(index, GetParameterValue(index) + value * weight);
	public void MultiplyParameterValue(int index, float value, float weight = 1) =>
		SetParameterValue(index, GetParameterValue(index) * (1 + (value - 1) * weight));

	public float GetPartOpacity(int index)
	{
		CheckIndex(index, PartCount + _virtualParts.Count);
		return index < PartCount ? Read(NativeMethods.GetPartOpacities(_memory), index) : _virtualParts[index - PartCount];
	}

	public void SetPartOpacity(int index, float value)
	{
		CheckIndex(index, PartCount + _virtualParts.Count);
		if (index < PartCount)
		{
			NativeMethods.GetPartOpacities(_memory)[index] = value;
			GC.KeepAlive(this);
		}
		else _virtualParts[index - PartCount] = value;
	}

	/// <summary>仅快照模型实际参数；虚拟参数作为动作/姿势的独立状态保留。</summary>
	public void SaveParameters()
	{
		new ReadOnlySpan<float>(GetParameterValues(), ParameterCount).CopyTo(_savedParameters);
		GC.KeepAlive(this);
	}

	public void LoadParameters()
	{
		_savedParameters.CopyTo(new Span<float>(GetParameterValues(), ParameterCount));
		GC.KeepAlive(this);
	}

	public float* GetParameterValues() { CheckDisposed(); return NativeMethods.GetParameterValues(_memory); }
	public float* GetParameterMinimumValues() { CheckDisposed(); return NativeMethods.GetParameterMinimumValues(_memory); }
	public float* GetParameterMaximumValues() { CheckDisposed(); return NativeMethods.GetParameterMaximumValues(_memory); }
	public float* GetParameterDefaultValues() { CheckDisposed(); return NativeMethods.GetParameterDefaultValues(_memory); }
	public int* GetDrawableRenderOrders() { CheckDisposed(); return NativeMethods.GetRenderOrders(_memory); }
	public int* GetDrawableMaskCounts() { CheckDisposed(); return NativeMethods.GetDrawableMaskCounts(_memory); }
	public int** GetDrawableMasks() { CheckDisposed(); return NativeMethods.GetDrawableMasks(_memory); }

	public int GetDrawableTextureIndex(int index) { CheckDrawable(index); return Read(NativeMethods.GetDrawableTextureIndices(_memory), index); }
	public int GetDrawableVertexCount(int index) { CheckDrawable(index); return Read(NativeMethods.GetDrawableVertexCounts(_memory), index); }
	public int GetDrawableVertexIndexCount(int index) { CheckDrawable(index); return Read(NativeMethods.GetDrawableIndexCounts(_memory), index); }
	public float GetDrawableOpacity(int index) { CheckDrawable(index); return Read(NativeMethods.GetDrawableOpacities(_memory), index); }
	public Vector4 GetMultiplyColor(int index) { CheckDrawable(index); return Read(NativeMethods.GetDrawableMultiplyColors(_memory), index); }
	public Vector4 GetScreenColor(int index) { CheckDrawable(index); return Read(NativeMethods.GetDrawableScreenColors(_memory), index); }
	public byte GetDrawableConstantFlags(int index) { CheckDrawable(index); return Read(NativeMethods.GetDrawableConstantFlags(_memory), index); }
	public byte GetDrawableDynamicFlags(int index) { CheckDrawable(index); return Read(NativeMethods.GetDrawableDynamicFlags(_memory), index); }
	public Vector2* GetDrawableVertexPositions(int index)
	{
		CheckDrawable(index);
		Vector2* result = NativeMethods.GetDrawableVertexPositions(_memory)[index];
		GC.KeepAlive(this);
		return result;
	}
	public Vector2* GetDrawableVertexUvs(int index)
	{
		CheckDrawable(index);
		Vector2* result = NativeMethods.GetDrawableVertexUvs(_memory)[index];
		GC.KeepAlive(this);
		return result;
	}
	public ushort* GetDrawableVertexIndices(int index)
	{
		CheckDrawable(index);
		ushort* result = NativeMethods.GetDrawableIndices(_memory)[index];
		GC.KeepAlive(this);
		return result;
	}

	// 位定义来自同一 MIT 公共头文件；仅提供现有渲染器支持的旧混合模式。
	public int GetDrawableBlendMode(int index) => (GetDrawableConstantFlags(index) & 3) switch { 1 => 1, 2 => 2, _ => 0 };
	public bool GetDrawableCulling(int index) => (GetDrawableConstantFlags(index) & 4) == 0;
	public bool GetDrawableInvertedMask(int index) => (GetDrawableConstantFlags(index) & 8) != 0;
	public bool GetDrawableDynamicFlagIsVisible(int index) => (GetDrawableDynamicFlags(index) & 1) != 0;
	public bool GetDrawableDynamicFlagVertexPositionsDidChange(int index) => (GetDrawableDynamicFlags(index) & 32) != 0;
	public bool IsUsingMasking()
	{
		int* counts = GetDrawableMaskCounts();
		bool found = false;
		for (int i = 0; i < DrawableCount; i++) found |= counts[i] > 0;
		GC.KeepAlive(this);
		return found;
	}

	private void CheckDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);
	private void CheckDrawable(int index) => CheckIndex(index, DrawableCount);
	private static void CheckIndex(int index, int count)
	{
		if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index), "Live2D 模型索引越界。");
	}

	private int FindOrCreate(string id, Dictionary<string, int> lookup, List<float> values, int nativeCount)
	{
		CheckDisposed();
		ArgumentNullException.ThrowIfNull(id);
		if (lookup.TryGetValue(id, out int index)) return index;
		index = nativeCount + values.Count;
		values.Add(0);
		lookup.Add(id, index);
		return index;
	}

	private T Read<T>(T* values, int index) where T : unmanaged
	{
		T value = values[index];
		GC.KeepAlive(this);
		return value;
	}

	private IReadOnlyList<string> ReadIds(nint* ids, int count, Dictionary<string, int> lookup)
	{
		if (count < 0 || (count > 0 && ids == null)) throw new InvalidDataException("Live2D 模型 ID 表无效。");
		var result = new string[count];
		for (int i = 0; i < count; i++)
		{
			result[i] = Marshal.PtrToStringUTF8(ids[i]) ?? throw new InvalidDataException("Live2D 模型 ID 为空。");
			if (!lookup.TryAdd(result[i], i)) throw new InvalidDataException("Live2D 模型 ID 重复。");
		}
		GC.KeepAlive(this);
		return Array.AsReadOnly(result);
	}
}
