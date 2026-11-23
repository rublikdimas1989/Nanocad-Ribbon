using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NcBootstrap;

internal sealed class SyncConfig
{
	[JsonPropertyName("serverUrl")]
	public string ServerUrl { get; set; } = "http://ink-aae-t:5000/api";


	[JsonPropertyName("username")]
	public string Username { get; set; } = "admin";


	[JsonPropertyName("password")]
	public string Password { get; set; } = "admin123";


	[JsonPropertyName("fileType")]
	public string FileType { get; set; } = "file list nanocad";


	[JsonPropertyName("syncFolder")]
	public string SyncFolder { get; set; } = "%LOCALAPPDATA%\\ASDC\\LoadFile\\NANOCAD";


	public string ResolvedSyncFolder => Environment.ExpandEnvironmentVariables(SyncFolder);

	public static SyncConfig Load()
	{
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(typeof(SyncConfig).Assembly.Location), "NcBootstrap.json");
			if (File.Exists(path))
			{
				SyncConfig syncConfig = JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(path));
				if (syncConfig != null)
				{
					return syncConfig;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Write("конфиг: " + ex.Message + " - используются значения по умолчанию");
		}
		return new SyncConfig();
	}
}
