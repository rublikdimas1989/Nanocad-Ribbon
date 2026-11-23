using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NcBootstrap;

internal sealed class ToolManifest
{
	public sealed class ManifestCommand
	{
		[JsonPropertyName("intername")]
		public string InterName { get; set; }

		[JsonPropertyName("displayName")]
		public string DisplayName { get; set; }

		[JsonPropertyName("tooltip")]
		public string Tooltip { get; set; }

		[JsonPropertyName("iconAssembly")]
		public string IconAssembly { get; set; }

		[JsonPropertyName("iconResource")]
		public string IconResource { get; set; }
	}

	public sealed class ManifestButton
	{
		[JsonPropertyName("text")]
		public string Text { get; set; }

		[JsonPropertyName("command")]
		public string Command { get; set; }

		[JsonPropertyName("style")]
		public string Style { get; set; } = "large";

		/// <summary>flyout / split / dropdown → RibbonSplitButton; otherwise command button.</summary>
		[JsonPropertyName("type")]
		public string Type { get; set; }

		[JsonPropertyName("items")]
		public List<ManifestButton> Items { get; set; } = new List<ManifestButton>();

		public bool IsFlyout
		{
			get
			{
				string t = Type ?? string.Empty;
				return string.Equals(t, "flyout", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(t, "split", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(t, "dropdown", StringComparison.OrdinalIgnoreCase);
			}
		}
	}

	public sealed class ManifestPanel
	{
		[JsonPropertyName("name")]
		public string Name { get; set; }

		[JsonPropertyName("buttons")]
		public List<ManifestButton> Buttons { get; set; } = new List<ManifestButton>();
	}

	/// <summary>Legacy single-panel field (pre panels[]).</summary>
	[JsonPropertyName("panel")]
	public string Panel { get; set; }

	[JsonPropertyName("panels")]
	public List<ManifestPanel> Panels { get; set; } = new List<ManifestPanel>();

	[JsonPropertyName("commands")]
	public List<ManifestCommand> Commands { get; set; } = new List<ManifestCommand>();

	[JsonPropertyName("buttons")]
	public List<ManifestButton> Buttons { get; set; } = new List<ManifestButton>();

	/// <summary>
	/// Effective ribbon groups: prefer panels[]; else legacy panel + buttons.
	/// </summary>
	public IEnumerable<ManifestPanel> EnumeratePanels()
	{
		if (Panels != null && Panels.Count > 0)
		{
			foreach (ManifestPanel p in Panels)
			{
				if (p != null && !string.IsNullOrWhiteSpace(p.Name))
				{
					yield return p;
				}
			}
			yield break;
		}

		if (!string.IsNullOrWhiteSpace(Panel))
		{
			yield return new ManifestPanel
			{
				Name = Panel,
				Buttons = Buttons ?? new List<ManifestButton>()
			};
		}
	}

	public static List<ToolManifest> LoadAll(string syncFolder)
	{
		List<ToolManifest> list = new List<ToolManifest>();
		if (!Directory.Exists(syncFolder))
		{
			return list;
		}

		string[] files = Directory.GetFiles(syncFolder, "*.json");
		foreach (string path in files)
		{
			try
			{
				ToolManifest toolManifest = JsonSerializer.Deserialize<ToolManifest>(File.ReadAllText(path));
				if (toolManifest == null)
				{
					continue;
				}

				if (!toolManifest.EnumeratePanels().Any())
				{
					// Skip bootstrap/config JSON without ribbon panels.
					continue;
				}

				list.Add(toolManifest);
			}
			catch (Exception ex)
			{
				Log.Write("манифест " + Path.GetFileName(path) + ": " + ex.Message);
			}
		}

		return list;
	}
}
