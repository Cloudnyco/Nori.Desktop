using System.Net.Http;
using System.Net.Sockets;
using Nori.Core.Voice;

namespace Nori.Desktop.Telemetry;

/// <summary>
/// 已知的预期网络、取消和关闭噪声。
///
/// 这些异常仍然记日志，但不再送进 Sentry。分类器不读异常正文；
/// 只有这里识别 Avalonia 关闭时的固定英文消息。
/// </summary>
internal static class TelemetryNoise
{
	public static bool IsNoise(Exception? exception)
	{
		if (exception is null) return false;
		if (exception is AggregateException aggregate)
		{
			if (aggregate.InnerExceptions.Count == 0) return false;
			foreach (Exception inner in aggregate.InnerExceptions)
			{
				if (!IsNoise(inner)) return false;
			}
			return true;
		}
		if (exception is OperationCanceledException or TimeoutException or SocketException) return true;
		if (exception is HttpRequestException request && ContainsSocket(request)) return true;
		if (exception is VoiceProviderException voice
			&& voice.FailureKind is VoiceFailureKind.Network or VoiceFailureKind.Timeout or VoiceFailureKind.HttpRejected)
			return true;
		return exception is InvalidOperationException
			&& exception.Message == "Application is already shutting down.";
	}

	private static bool ContainsSocket(Exception exception)
	{
		for (Exception? current = exception.InnerException; current is not null; current = current.InnerException)
		{
			if (current is SocketException) return true;
		}
		return false;
	}
}
