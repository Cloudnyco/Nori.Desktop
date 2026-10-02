using System.Numerics;
using Live2DCSharpSDK.Framework.Rendering;
using Nori.Live2D;

namespace Live2DCSharpSDK.Framework.Model;

/// <summary>
/// 旧动作、物理和 GL 层的薄适配器。不持有第二套模型状态，不执行原生绑定或分配。
/// 此兼容文件仍属于旧 SDK 边界；不能据此宣称整个 SDK 已独立实现。
/// </summary>
public sealed unsafe class CubismModel : IDisposable
{
	public NativeModel Native { get; }
	internal CubismModel(NativeModel native) => Native = native;
	public IReadOnlyList<string> ParameterIds => Native.ParameterIds;
	public IReadOnlyList<string> PartIds => Native.PartIds;
	public IReadOnlyList<string> DrawableIds => Native.DrawableIds;

	public void Dispose() => Native.Dispose();
	public void Update() => Native.Update();
	public float GetCanvasWidthPixel() => Native.CanvasSize.X;
	public float GetCanvasHeightPixel() => Native.CanvasSize.Y;
	public float GetPixelsPerUnit() => Native.PixelsPerUnit;
	public float GetCanvasWidth() => Native.CanvasSize.X / Native.PixelsPerUnit;
	public float GetCanvasHeight() => Native.CanvasSize.Y / Native.PixelsPerUnit;
	public int GetPartIndex(string id) => Native.GetPartIndex(id);
	public int GetPartCount() => Native.PartCount;
	public void SetPartOpacity(string id, float opacity) => SetPartOpacity(GetPartIndex(id), opacity);
	public void SetPartOpacity(int index, float opacity) => Native.SetPartOpacity(index, opacity);
	public float GetPartOpacity(string id) => GetPartOpacity(GetPartIndex(id));
	public float GetPartOpacity(int index) => Native.GetPartOpacity(index);
	public int GetParameterIndex(string id) => Native.GetParameterIndex(id);
	public int GetParameterCount() => Native.ParameterCount;
	public float GetParameterDefaultValue(int index) => Native.GetParameterDefaultValue(index);
	public float GetParameterDefaultValue(string id) => GetParameterDefaultValue(GetParameterIndex(id));
	public float GetParameterValue(string id) => GetParameterValue(GetParameterIndex(id));
	public float GetParameterValue(int index) => Native.GetParameterValue(index);
	public void SetParameterValue(string id, float value, float weight = 1) => SetParameterValue(GetParameterIndex(id), value, weight);
	public void SetParameterValue(int index, float value, float weight = 1) => Native.SetParameterValue(index, value, weight);
	public void AddParameterValue(string id, float value, float weight = 1) => AddParameterValue(GetParameterIndex(id), value, weight);
	public void AddParameterValue(int index, float value, float weight = 1) => Native.AddParameterValue(index, value, weight);
	public void MultiplyParameterValue(string id, float value, float weight = 1) => MultiplyParameterValue(GetParameterIndex(id), value, weight);
	public void MultiplyParameterValue(int index, float value, float weight = 1) => Native.MultiplyParameterValue(index, value, weight);
	public int GetDrawableIndex(string id) => Native.GetDrawableIndex(id);
	public int GetDrawableCount() => Native.DrawableCount;
	public int* GetDrawableRenderOrders() => Native.GetDrawableRenderOrders();
	public int GetDrawableTextureIndex(int index) => Native.GetDrawableTextureIndex(index);
	public int GetDrawableVertexIndexCount(int index) => Native.GetDrawableVertexIndexCount(index);
	public int GetDrawableVertexCount(int index) => Native.GetDrawableVertexCount(index);
	public float* GetDrawableVertices(int index) => (float*)Native.GetDrawableVertexPositions(index);
	public ushort* GetDrawableVertexIndices(int index) => Native.GetDrawableVertexIndices(index);
	public Vector2* GetDrawableVertexPositions(int index) => Native.GetDrawableVertexPositions(index);
	public Vector2* GetDrawableVertexUvs(int index) => Native.GetDrawableVertexUvs(index);
	public float GetDrawableOpacity(int index) => Native.GetDrawableOpacity(index);
	public CubismBlendMode GetDrawableBlendMode(int index) => (CubismBlendMode)Native.GetDrawableBlendMode(index);
	public bool GetDrawableInvertedMask(int index) => Native.GetDrawableInvertedMask(index);
	public bool GetDrawableDynamicFlagIsVisible(int index) => Native.GetDrawableDynamicFlagIsVisible(index);
	public bool GetDrawableDynamicFlagVertexPositionsDidChange(int index) => Native.GetDrawableDynamicFlagVertexPositionsDidChange(index);
	public int** GetDrawableMasks() => Native.GetDrawableMasks();
	public int* GetDrawableMaskCounts() => Native.GetDrawableMaskCounts();
	public bool IsUsingMasking() => Native.IsUsingMasking();
	public void LoadParameters() => Native.LoadParameters();
	public void SaveParameters() => Native.SaveParameters();
	public CubismTextureColor GetMultiplyColor(int index) => ToLegacyColor(Native.GetMultiplyColor(index));
	public CubismTextureColor GetScreenColor(int index) => ToLegacyColor(Native.GetScreenColor(index));
	public bool GetDrawableCulling(int index) => Native.GetDrawableCulling(index);
	public float GetModelOpacity() => Native.Opacity;
	public void SetModelOpacity(float value) => Native.Opacity = value;

	private static CubismTextureColor ToLegacyColor(Vector4 color) => new() { R = color.X, G = color.Y, B = color.Z, A = color.W };
}
