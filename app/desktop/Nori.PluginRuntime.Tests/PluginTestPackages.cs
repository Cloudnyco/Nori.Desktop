using System.IO.Compression;

namespace Nori.PluginRuntime.Tests;

/// <summary>测试插件包的机械操作：写 ZIP 条目、复制程序集、建临时目录与尽力清理。manifest 与包名策略留在各测试类。</summary>
internal static class PluginTestPackages
{
	internal static void WriteEntry(ZipArchive archive, string name, string content)
	{
		using StreamWriter writer = new(archive.CreateEntry(name).Open());
		writer.Write(content);
	}

	internal static void WriteAssemblyEntry(ZipArchive archive, string name, string assemblyPath)
	{
		ZipArchiveEntry entry = archive.CreateEntry(name);
		using Stream target = entry.Open();
		using FileStream source = File.OpenRead(assemblyPath);
		source.CopyTo(target);
	}

	internal static string CreateTemp(string directoryName)
	{
		string path = Path.Combine(Path.GetTempPath(), directoryName, Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "S2486", Justification = "测试夹具销毁只能尽力清理，不能让清理异常覆盖测试结果。")]
	internal static void DeleteDirectory(string path)
	{
		try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
	}
}
