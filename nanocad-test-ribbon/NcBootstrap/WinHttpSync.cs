using System;

namespace NcBootstrap;

internal static class WinHttpSync
{
	public static string Send(string method, string url, string body = null, string sessionToken = null, string contentType = null)
	{
		Type typeFromProgID = Type.GetTypeFromProgID("WinHttp.WinHttpRequest.5.1");
		if (typeFromProgID == null)
		{
			throw new InvalidOperationException("WinHttp.WinHttpRequest.5.1 недоступен");
		}
		dynamic val = Activator.CreateInstance(typeFromProgID);
		val.Open(method, url, false);
		if (!string.IsNullOrEmpty(contentType))
		{
			val.SetRequestHeader("Content-Type", contentType);
		}
		if (!string.IsNullOrEmpty(sessionToken))
		{
			val.SetRequestHeader("X-Session-Token", sessionToken);
		}
		val.Send(body ?? "");
		return (string)val.ResponseText;
	}
}
