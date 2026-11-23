using System;
using System.IO;

namespace NcBootstrap;

internal static class Log
{
	private static readonly object Sync = new object();

	private static readonly string Path = System.IO.Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "NcBootstrap.log");

	internal static void Write(string message)
	{
		try
		{
			lock (Sync)
			{
				File.AppendAllText(Path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
			}
		}
		catch
		{
		}
	}
}
