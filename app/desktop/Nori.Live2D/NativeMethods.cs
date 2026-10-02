// ABI 声明依据 PurismCore v1.1.0 的 include/PurismCore.h（MIT）。
// Copyright (c) 2026 Sakura Motion Project
// 固定提交与许可全文位置见 README.md；此处不包含上游实现。
using System.Numerics;
using System.Runtime.InteropServices;

namespace Nori.Live2D;

internal static unsafe class NativeMethods
{
	private const string Library = "Live2DCubismCore";

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void LogCallback(nint message);

	[DllImport(Library, EntryPoint = "csmGetVersion", CallingConvention = CallingConvention.Winapi)]
	internal static extern uint GetVersion();

	[DllImport(Library, EntryPoint = "csmGetTrueVersion", CallingConvention = CallingConvention.Winapi)]
	internal static extern uint GetTrueVersion();

	[DllImport(Library, EntryPoint = "csmSetLogFunction", CallingConvention = CallingConvention.Winapi)]
	internal static extern void SetLogFunction(LogCallback? callback);

	[DllImport(Library, EntryPoint = "csmHasMocConsistency", CallingConvention = CallingConvention.Winapi)]
	internal static extern int HasMocConsistency(nint moc, uint size);

	[DllImport(Library, EntryPoint = "csmReviveMocInPlace", CallingConvention = CallingConvention.Winapi)]
	internal static extern nint ReviveMocInPlace(nint moc, uint size);

	[DllImport(Library, EntryPoint = "csmGetSizeofModel", CallingConvention = CallingConvention.Winapi)]
	internal static extern uint GetSizeofModel(nint moc);

	[DllImport(Library, EntryPoint = "csmInitializeModelInPlace", CallingConvention = CallingConvention.Winapi)]
	internal static extern nint InitializeModelInPlace(nint moc, nint memory, uint size);

	[DllImport(Library, EntryPoint = "csmReadCanvasInfo", CallingConvention = CallingConvention.Winapi)]
	internal static extern void ReadCanvasInfo(ModelMemory model, out Vector2 size, out Vector2 origin, out float pixelsPerUnit);

	[DllImport(Library, EntryPoint = "csmUpdateModel", CallingConvention = CallingConvention.Winapi)]
	internal static extern void UpdateModel(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmResetDrawableDynamicFlags", CallingConvention = CallingConvention.Winapi)]
	internal static extern void ResetDrawableDynamicFlags(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetParameterCount", CallingConvention = CallingConvention.Winapi)]
	internal static extern int GetParameterCount(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetParameterIds", CallingConvention = CallingConvention.Winapi)]
	internal static extern nint* GetParameterIds(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetParameterValues", CallingConvention = CallingConvention.Winapi)]
	internal static extern float* GetParameterValues(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetParameterMinimumValues", CallingConvention = CallingConvention.Winapi)]
	internal static extern float* GetParameterMinimumValues(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetParameterMaximumValues", CallingConvention = CallingConvention.Winapi)]
	internal static extern float* GetParameterMaximumValues(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetParameterDefaultValues", CallingConvention = CallingConvention.Winapi)]
	internal static extern float* GetParameterDefaultValues(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetPartCount", CallingConvention = CallingConvention.Winapi)]
	internal static extern int GetPartCount(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetPartIds", CallingConvention = CallingConvention.Winapi)]
	internal static extern nint* GetPartIds(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetPartOpacities", CallingConvention = CallingConvention.Winapi)]
	internal static extern float* GetPartOpacities(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableCount", CallingConvention = CallingConvention.Winapi)]
	internal static extern int GetDrawableCount(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableIds", CallingConvention = CallingConvention.Winapi)]
	internal static extern nint* GetDrawableIds(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableConstantFlags", CallingConvention = CallingConvention.Winapi)]
	internal static extern byte* GetDrawableConstantFlags(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableDynamicFlags", CallingConvention = CallingConvention.Winapi)]
	internal static extern byte* GetDrawableDynamicFlags(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableTextureIndices", CallingConvention = CallingConvention.Winapi)]
	internal static extern int* GetDrawableTextureIndices(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetRenderOrders", CallingConvention = CallingConvention.Winapi)]
	internal static extern int* GetRenderOrders(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableOpacities", CallingConvention = CallingConvention.Winapi)]
	internal static extern float* GetDrawableOpacities(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableMaskCounts", CallingConvention = CallingConvention.Winapi)]
	internal static extern int* GetDrawableMaskCounts(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableMasks", CallingConvention = CallingConvention.Winapi)]
	internal static extern int** GetDrawableMasks(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableVertexCounts", CallingConvention = CallingConvention.Winapi)]
	internal static extern int* GetDrawableVertexCounts(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableVertexPositions", CallingConvention = CallingConvention.Winapi)]
	internal static extern Vector2** GetDrawableVertexPositions(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableVertexUvs", CallingConvention = CallingConvention.Winapi)]
	internal static extern Vector2** GetDrawableVertexUvs(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableIndexCounts", CallingConvention = CallingConvention.Winapi)]
	internal static extern int* GetDrawableIndexCounts(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableIndices", CallingConvention = CallingConvention.Winapi)]
	internal static extern ushort** GetDrawableIndices(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableMultiplyColors", CallingConvention = CallingConvention.Winapi)]
	internal static extern Vector4* GetDrawableMultiplyColors(ModelMemory model);

	[DllImport(Library, EntryPoint = "csmGetDrawableScreenColors", CallingConvention = CallingConvention.Winapi)]
	internal static extern Vector4* GetDrawableScreenColors(ModelMemory model);
}
