using System.Numerics;
using Nori.Live2D;

namespace Nori.Desktop.Live2D.Gl;

/// <summary>将自有遮罩计划提交到当前 GL 上下文；纹理仍由渲染器拥有。</summary>
public sealed class MaskAtlas(OpenGLApi gl, NativeModel model, int bufferCount)
{
	public MaskPlan Plan { get; } = new(model);
	public int RenderTextureCount { get; } = bufferCount;
	public Vector2 ClippingMaskBufferSize { get; private set; } = new(256);
	private readonly bool[] _cleared = new bool[bufferCount];

	public void Resize(float width, float height)
	{
		if (!float.IsFinite(width) || !float.IsFinite(height) || width < 1 || height < 1 || width > int.MaxValue || height > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(width), "遮罩尺寸必须是有效的正像素数");
		ClippingMaskBufferSize = new((int)width, (int)height);
	}

	public void Update(bool highPrecision) => Plan.Update(model, ClippingMaskBufferSize, RenderTextureCount, highPrecision);

	public void Draw(IModelMeshTarget renderer, int framebuffer, int[] viewport, MaskGroup? single = null)
	{
		Array.Clear(_cleared);
		int surface = -1;
		try
		{
			gl.Viewport(0, 0, (int)ClippingMaskBufferSize.X, (int)ClippingMaskBufferSize.Y);
			for (int i = 0; i < Plan.Groups.Count; i++)
			{
				MaskGroup group = Plan.Groups[i];
				if (!group.Active || (single is not null && !ReferenceEquals(single, group))) continue;
				int next = group.BufferIndex;
				if (next != surface)
				{
					int previous = surface;
					surface = -1;
					if (previous >= 0) renderer.EndMask(previous);
					renderer.BeginMask(next, framebuffer);
					surface = next;
					renderer.PreDraw();
				}
				if (!_cleared[group.BufferIndex])
				{
					gl.ClearColor(1, 1, 1, 1);
					gl.Clear(gl.GL_COLOR_BUFFER_BIT);
					_cleared[group.BufferIndex] = true;
				}
				for (int j = 0; j < group.Masks.Count; j++)
				{
					int drawable = group.Masks[j];
					// 原生模型缓冲区始终有效；静止顶点也必须写入本帧新清空的遮罩。
					renderer.ClippingContextBufferForMask = group;
					renderer.DrawMesh(drawable);
				}
			}
		}
		finally
		{
			try
			{
				if (surface >= 0) renderer.EndMask(surface);
			}
			finally
			{
				renderer.ClippingContextBufferForMask = null;
				gl.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
			}
		}
	}
}
