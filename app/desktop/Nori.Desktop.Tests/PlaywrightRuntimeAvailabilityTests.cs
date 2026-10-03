using Nori.Desktop.Automation.Browser;

namespace Nori.Desktop.Tests;

public sealed class PlaywrightRuntimeAvailabilityTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"nori-playwright-runtime-{Guid.NewGuid():N}");

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void 按driver目录是否存在报告可用性(bool installed)
	{
		Directory.CreateDirectory(_root);
		if (installed)
		{
			Directory.CreateDirectory(Path.Combine(_root, ".playwright", "package"));
			Directory.CreateDirectory(Path.Combine(_root, ".playwright", "node"));
		}
		Assert.Equal(installed, PlaywrightRuntimeAvailability.IsAvailable(_root));
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
		}
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}
}
