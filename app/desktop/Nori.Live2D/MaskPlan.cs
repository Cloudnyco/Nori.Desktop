using System.Numerics;

namespace Nori.Live2D;

/// <summary>模型独占的遮罩组与每帧布局；不持有原生指针或 GL 资源。</summary>
public sealed class MaskPlan
{
	private readonly MaskGroup[] _groups;
	private readonly MaskGroup?[] _drawables;
	private readonly MaskGroup[] _activeGroups;

	public IReadOnlyList<MaskGroup> Groups { get; }
	public IReadOnlyList<MaskGroup?> Drawables { get; }

	public unsafe MaskPlan(NativeModel model) : this(ReadMasks(model)) { }

	internal MaskPlan(int[][] masks)
	{
		List<MaskGroup> groups = [];
		MaskGroup?[] drawables = new MaskGroup?[masks.Length];
		for (int drawable = 0; drawable < masks.Length; drawable++)
		{
			int[] indices = masks[drawable].Distinct().Order().ToArray();
			if (indices.Length == 0) continue;
			if (indices.Any(index => index < 0 || index >= masks.Length))
				throw new ArgumentException("遮罩绘制对象索引越界", nameof(masks));
			MaskGroup? group = groups.Find(item => item.Masks.SequenceEqual(indices));
			if (group is null) groups.Add(group = new MaskGroup(indices));
			group.AddClippedDrawable(drawable);
			drawables[drawable] = group;
		}
		_groups = groups.ToArray();
		_drawables = drawables;
		_activeGroups = new MaskGroup[_groups.Length];
		foreach (MaskGroup group in _groups) group.FreezeClippedDrawables();
		Groups = Array.AsReadOnly(_groups);
		Drawables = Array.AsReadOnly(_drawables);
	}

	private static unsafe int[][] ReadMasks(NativeModel model)
	{
		int[][] result = new int[model.DrawableCount][];
		int* counts = model.GetDrawableMaskCounts();
		int** masks = model.GetDrawableMasks();
		for (int i = 0; i < result.Length; i++) result[i] = new ReadOnlySpan<int>(masks[i], counts[i]).ToArray();
		GC.KeepAlive(model);
		return result;
	}

	public unsafe void Update(NativeModel model, Vector2 textureSize, int bufferCount, bool highPrecision)
	{
		if (!float.IsFinite(textureSize.X) || !float.IsFinite(textureSize.Y) || textureSize.X <= 0 || textureSize.Y <= 0)
			throw new ArgumentOutOfRangeException(nameof(textureSize), "遮罩纹理尺寸必须是有限正数");
		ArgumentOutOfRangeException.ThrowIfLessThan(bufferCount, 1);
		for (int groupIndex = 0; groupIndex < _groups.Length; groupIndex++)
		{
			MaskGroup group = _groups[groupIndex];
			Vector2 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
			for (int clippedIndex = 0; clippedIndex < group.ClippedDrawables.Length; clippedIndex++)
			{
				int drawable = group.ClippedDrawables[clippedIndex];
				Vector2* vertices = model.GetDrawableVertexPositions(drawable);
				int vertexCount = model.GetDrawableVertexCount(drawable);
				for (int i = 0; i < vertexCount; i++)
				{
					min = Vector2.Min(min, vertices[i]);
					max = Vector2.Max(max, vertices[i]);
				}
			}
			Vector2 size = max - min;
			group.Active = float.IsFinite(size.X) && float.IsFinite(size.Y) && size.X > 0 && size.Y > 0;
			group.Bounds = group.Active ? new(min.X, min.Y, size.X, size.Y) : Vector4.Zero;
		}
		GC.KeepAlive(model);
		int activeCount = 0;
		for (int i = 0; i < _groups.Length; i++)
			if (_groups[i].Active) _activeGroups[activeCount++] = _groups[i];
		for (int i = 0; i < activeCount; i++)
		{
			MaskGroup group = _activeGroups[i];
			(group.BufferIndex, group.Channel, group.Tile) = highPrecision ? (0, 0, new Vector4(0, 0, 1, 1))
				: Place(i, activeCount, bufferCount);
			(group.MaskMatrix, group.DrawMatrix) = Transform(group.Bounds, group.Tile, textureSize, model.PixelsPerUnit, highPrecision);
		}
	}

	internal static (int Buffer, int Channel, Vector4 Tile) Place(int index, int count, int buffers)
	{
		// 先均分到纹理，再均分到 RGBA 通道；超过常见容量时继续细分，不丢弃遮罩。
		int buffer = 0;
		while (index >= count / buffers + (buffer < count % buffers ? 1 : 0))
		{
			index -= count / buffers + (buffer < count % buffers ? 1 : 0);
			buffer++;
		}
		int items = count / buffers + (buffer < count % buffers ? 1 : 0);
		int channel = 0;
		while (index >= items / 4 + (channel < items % 4 ? 1 : 0))
		{
			index -= items / 4 + (channel < items % 4 ? 1 : 0);
			channel++;
		}
		int cells = items / 4 + (channel < items % 4 ? 1 : 0);
		int columns = (int)Math.Ceiling(Math.Sqrt(cells));
		int rows = cells == 2 ? 1 : columns;
		return (buffer, channel, new((float)(index % columns) / columns, (float)(index / columns) / rows, 1f / columns, 1f / rows));
	}

	internal static (Matrix4x4 Mask, Matrix4x4 Draw) Transform(Vector4 bounds, Vector4 tile, Vector2 textureSize, float pixelsPerUnit, bool highPrecision)
	{
		Vector2 origin = new(bounds.X, bounds.Y), size = new(bounds.Z, bounds.W);
		Vector2 region = new(tile.Z, tile.W);
		Vector2 margin = size * 0.05f;
		bool fitX = !highPrecision || size.X * pixelsPerUnit > textureSize.X * region.X;
		bool fitY = !highPrecision || size.Y * pixelsPerUnit > textureSize.Y * region.Y;
		Vector2 padding = new(fitX ? margin.X : 0, fitY ? margin.Y : 0);
		Vector2 padded = size + padding * 2;
		Vector2 scale = new(fitX ? region.X / padded.X : pixelsPerUnit / textureSize.X,
			fitY ? region.Y / padded.Y : pixelsPerUnit / textureSize.Y);
		return ModelTransforms.CreateMask(origin - padding, new(tile.X, tile.Y), scale, false);
	}
}

/// <summary>共享一组遮罩来源的绘制对象及其当前纹理映射。</summary>
public sealed class MaskGroup
{
	internal MaskGroup(int[] masks) => Masks = Array.AsReadOnly(masks);
	private readonly List<int> _clippedDrawableBuilder = [];
	internal int[] ClippedDrawables { get; private set; } = [];
	internal void AddClippedDrawable(int drawable) => _clippedDrawableBuilder.Add(drawable);
	internal void FreezeClippedDrawables() => ClippedDrawables = _clippedDrawableBuilder.ToArray();
	public IReadOnlyList<int> Masks { get; }
	public bool Active { get; internal set; }
	public Vector4 Bounds { get; internal set; }
	public Vector4 Tile { get; internal set; }
	public int BufferIndex { get; internal set; }
	public int Channel { get; internal set; }
	public Matrix4x4 MaskMatrix { get; internal set; } = Matrix4x4.Identity;
	public Matrix4x4 DrawMatrix { get; internal set; } = Matrix4x4.Identity;
}
