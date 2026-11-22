using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace NcBootstrap;

internal static class UiGenerator
{
	public const string TabTitle = "Инструменты ЦРПР";

	public const string CfgFileName = "NC_Tools.cfg";

	public const string CuixFileName = "NC_Tools.cuix";

	public static void Write(string configDir, List<ToolManifest> manifests, string syncFolder = null)
	{
		string loadIcon = CopyLoadIcon(configDir);
		WriteCfg(Path.Combine(configDir, "NC_Tools.cfg"), configDir, syncFolder, manifests, loadIcon);
		WriteCuix(Path.Combine(configDir, "NC_Tools.cuix"), manifests);
		int panelCount = 0;
		foreach (ToolManifest m in manifests)
		{
			foreach (ToolManifest.ManifestPanel _ in m.EnumeratePanels())
			{
				panelCount++;
			}
		}
		Log.Write("UI сгенерирован: панелей инструментов " + panelCount + " (манифестов " + manifests.Count + ")");
	}

	private static string CopyLoadIcon(string configDir)
	{
		string text = Path.Combine(configDir, "icons");
		Directory.CreateDirectory(text);
		string path = Path.Combine(Path.GetDirectoryName(typeof(UiGenerator).Assembly.Location), "icons");
		string text2 = Path.Combine(path, "load_chart.ico");
		string text3 = Path.Combine(text, "load_chart.ico");
		if (!File.Exists(text2))
		{
			Log.Write("Иконка Загрузить не найдена: " + text2);
			return null;
		}
		File.Copy(text2, text3, overwrite: true);
		Log.Write("Иконка Загрузить скопирована: " + text3);
		return "icons\\load_chart.ico";
	}

	private static string ExtractServerIcon(string configDir, string syncFolder, ToolManifest.ManifestCommand command)
	{
		if (command == null || string.IsNullOrWhiteSpace(syncFolder) || string.IsNullOrWhiteSpace(command.IconAssembly) || string.IsNullOrWhiteSpace(command.IconResource))
		{
			return null;
		}
		string text = Path.Combine(syncFolder, command.IconAssembly);
		if (!File.Exists(text))
		{
			Log.Write("DLL для иконки не найдена: " + text);
			return null;
		}
		string text2 = Path.Combine(configDir, "server-icons");
		Directory.CreateDirectory(text2);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(command.IconAssembly);
		string text3 = Sanitize(command.InterName);
		string text4 = fileNameWithoutExtension + "_" + text3 + ".ico";
		string text5 = Path.Combine(text2, text4);
		AssemblyLoadContext assemblyLoadContext = null;
		try
		{
			assemblyLoadContext = new AssemblyLoadContext("NcBootstrapIcon_" + Guid.NewGuid().ToString("N"), isCollectible: true);
			Assembly assembly = assemblyLoadContext.LoadFromAssemblyPath(Path.GetFullPath(text));
			using Stream stream = assembly.GetManifestResourceStream(command.IconResource);
			if (stream == null)
			{
				string text6 = string.Join(", ", assembly.GetManifestResourceNames());
				Log.Write("Ресурс иконки не найден: " + command.IconResource + " в " + command.IconAssembly + ". Доступны: " + text6);
				return null;
			}
			using (FileStream destination = new FileStream(text5, FileMode.Create, FileAccess.Write, FileShare.Read))
			{
				stream.CopyTo(destination);
			}
			Log.Write("Серверная иконка извлечена: " + command.IconAssembly + " :: " + command.IconResource + " -> " + text5);
			return "server-icons\\" + text4;
		}
		catch (Exception ex)
		{
			Log.Write("Не удалось извлечь серверную иконку " + command.IconResource + ": " + ex.Message);
			return null;
		}
		finally
		{
			assemblyLoadContext?.Unload();
		}
	}

	private static void WriteCfg(string path, string configDir, string syncFolder, List<ToolManifest> manifests, string loadIcon)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("[\\configman]");
		stringBuilder.AppendLine("[\\configman\\commands]");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("[\\menu\\NcTools]                          |name=sИнструменты ЦРПР");
		stringBuilder.AppendLine("[\\menu\\NcTools\\NcLoad]                   |name=sЗагрузить инструменты |intername=sNCLOAD");
		stringBuilder.AppendLine("[\\menu\\NcTools\\NcUnload]                 |name=sВыгрузить инструменты |intername=sNCUNLOAD");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("[\\toolbars\\NcTools]                      |name=sИнструменты ЦРПР |InitialVisible=f1");
		stringBuilder.AppendLine("[\\toolbars\\NcTools\\NcLoad]               |intername=sNCLOAD |ToolTipText=sСкачать и загрузить инструменты с сервера");
		stringBuilder.AppendLine("[\\toolbars\\NcTools\\sep0]");
		stringBuilder.AppendLine("[\\toolbars\\NcTools\\NcUnload]             |intername=sNCUNLOAD |ToolTipText=sУбрать загруженные инструменты");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("[\\toolbarspos\\NcTools]                   |DockPosition=sTop |row=i5 |pos=i0");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("[\\ribbon\\NcTools]");
		stringBuilder.AppendLine("CUIX=s%CFG_PATH%\\NC_Tools.cuix");
		stringBuilder.AppendLine("visible=f1");
		stringBuilder.AppendLine();
		AppendCommand(stringBuilder, "NCLOAD", "Загрузить инструменты", "Скачать и загрузить инструменты с сервера", loadIcon);
		AppendCommand(stringBuilder, "NCUNLOAD", "Выгрузить инструменты", "Убрать загруженные инструменты (появится после перезапуска)");
		AppendCommand(stringBuilder, "NCDIAG", "Диагностика NcBootstrap", "Сведения о настройке NcBootstrap");
		foreach (ToolManifest manifest in manifests)
		{
			foreach (ToolManifest.ManifestCommand command in manifest.Commands)
			{
				string iconPath = ExtractServerIcon(configDir, syncFolder, command);
				AppendCommand(stringBuilder, command.InterName, command.DisplayName, command.Tooltip, iconPath);
			}
		}
		File.WriteAllText(path, stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
	}

	private static void AppendCommand(StringBuilder sb, string intername, string dispName, string tooltip)
	{
		AppendCommand(sb, intername, dispName, tooltip, null);
	}

	private static void AppendCommand(StringBuilder sb, string intername, string dispName, string tooltip, string iconPath)
	{
		sb.AppendLine("[\\configman\\commands\\" + intername + "]");
		sb.AppendLine("weight=i30");
		sb.AppendLine("cmdtype=i1");
		sb.AppendLine("intername=s" + intername);
		if (!string.IsNullOrWhiteSpace(iconPath))
		{
			sb.AppendLine("BitmapDll=s" + iconPath);
		}
		sb.AppendLine("DispName=s" + dispName);
		sb.AppendLine("ToolTipText=s" + tooltip);
		sb.AppendLine("StatusText=s" + tooltip);
		sb.AppendLine();
	}

	private static void WriteCuix(string path, List<ToolManifest> manifests)
	{
		string content = BuildRibbonRoot(manifests);
		string content2 = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<MenuPackageParts>\n  <PartData PartData_Name=\"/RibbonRoot.cui\" />\n  <PartData PartData_Name=\"/Menu_Package_Info.xml\" />\n</MenuPackageParts>\n";
		string content3 = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"cui\" ContentType=\"text/xml\" /><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" /><Default Extension=\"xml\" ContentType=\"text/xml\" /></Types>";
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
		WriteEntry(zip, "RibbonRoot.cui", content);
		WriteEntry(zip, "Menu_Package_Info.xml", content2);
		WriteEntry(zip, "[Content_Types].xml", content3);
	}

	private static void WriteEntry(ZipArchive zip, string name, string content)
	{
		ZipArchiveEntry zipArchiveEntry = zip.CreateEntry(name);
		using StreamWriter streamWriter = new StreamWriter(zipArchiveEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.Write(content);
	}

	private static string BuildRibbonRoot(List<ToolManifest> manifests)
	{
		StringBuilder panels = new StringBuilder();
		StringBuilder refs = new StringBuilder();
		int panelIndex = 0;

		panels.Append("    <RibbonPanelSource UID=\"RBNU_NCTOOLS_CTRL\" Text=\"Управление\" HiddenInEditor=\"false\">\n");
		panels.Append("      <RibbonRowPanel UID=\"RBNURP_NCTOOLS_CTRL\" ResizeStyle=\"None\" ResizePriority=\"100\" TopJustify=\"True\">\n");
		panels.Append("        <RibbonRow UID=\"RBNURR_NCTOOLS_CTRL\">\n");
		panels.Append(ButtonXml("RBNU_NCLOAD", "Загрузить", "NCLOAD", "large", "Скачать и загрузить инструменты с сервера"));
		panels.Append("        </RibbonRow>\n");
		panels.Append("      </RibbonRowPanel>\n");
		panels.Append("    </RibbonPanelSource>\n");
		refs.Append("      <RibbonPanelSourceReference UID=\"RBNU_NCTOOLS_CTRL_Ref\" PanelId=\"RBNU_NCTOOLS_CTRL\" ResizeStyle=\"Default\" />\n");

		foreach (ToolManifest manifest in manifests)
		{
			foreach (ToolManifest.ManifestPanel panelDef in manifest.EnumeratePanels())
			{
				panelIndex++;
				string panelUid = "RBNU_NCTOOLS_P" + panelIndex + "_" + Sanitize(panelDef.Name);
				panels.Append("    <RibbonPanelSource UID=\"" + panelUid + "\" Text=\"" + Escape(panelDef.Name) + "\" HiddenInEditor=\"false\">\n");
				panels.Append("      <RibbonRowPanel UID=\"RBNURP_" + panelUid + "\" ResizeStyle=\"None\" ResizePriority=\"100\" TopJustify=\"True\">\n");
				panels.Append("        <RibbonRow UID=\"RBNURR_" + panelUid + "\">\n");

				int btnIndex = 0;
				foreach (ToolManifest.ManifestButton button in panelDef.Buttons ?? new List<ToolManifest.ManifestButton>())
				{
					btnIndex++;
					string style = string.Equals(button.Style, "small", StringComparison.OrdinalIgnoreCase) ? "small" : "large";
					if (button.IsFlyout)
					{
						string flyUid = "RBNU_FLY_" + panelIndex + "_" + btnIndex + "_" + Sanitize(button.Text);
						panels.Append(FlyoutXml(flyUid, button.Text, style, button.Command, button.Items));
						continue;
					}

					panels.Append(ButtonXml(
						"RBNU_BTN_" + panelIndex + "_" + Sanitize(button.Command),
						button.Text,
						button.Command,
						style,
						button.Text));
				}

				panels.Append("        </RibbonRow>\n");
				panels.Append("      </RibbonRowPanel>\n");
				panels.Append("    </RibbonPanelSource>\n");
				refs.Append("      <RibbonPanelSourceReference UID=\"" + panelUid + "_Ref\" PanelId=\"" + panelUid + "\" ResizeStyle=\"Default\" />\n");
			}
		}

		return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
			+ "<RibbonRoot>\n"
			+ "  <RibbonPanelSourceCollection xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n"
			+ panels
			+ "  </RibbonPanelSourceCollection>\n"
			+ "  <RibbonTabSourceCollection>\n"
			+ "    <RibbonTabSource Text=\"" + Escape(TabTitle) + "\" UID=\"ID_NCTOOLS_TAB\">\n"
			+ refs
			+ "    </RibbonTabSource>\n"
			+ "  </RibbonTabSourceCollection>\n"
			+ "</RibbonRoot>\n";
	}

	private static string ButtonXml(string uid, string text, string command, string style, string tooltip)
	{
		string buttonStyle = (style == "small") ? "SmallWithText" : "LargeWithText";
		return "          <RibbonCommandButton UID=\"" + uid + "\" Id=\"AcRibbonCommandButton\" Text=\"" + Escape(text)
			+ "\" ButtonStyle=\"" + buttonStyle + "\" MenuMacroID=\"" + command + "\" KeyTip=\"\">\n"
			+ "            <TooltipTitle xlate=\"true\" UID=\"XLS_" + uid + "\">" + Escape(tooltip) + "</TooltipTitle>\n"
			+ "          </RibbonCommandButton>\n";
	}

	private static string FlyoutXml(string uid, string text, string style, string command, List<ToolManifest.ManifestButton> items)
	{
		string buttonStyle = (style == "small") ? "SmallWithText" : "LargeWithText";
		StringBuilder sb = new StringBuilder();
		sb.Append("          <RibbonSplitButton UID=\"" + uid + "\" Id=\"AcRibbonSplitButton\" Text=\"" + Escape(text) + "\" ");
		sb.Append("Behavior=\"DropDown\" ListStyle=\"IconText\" ButtonStyle=\"" + buttonStyle + "\" Grouping=\"false\" KeyTip=\"\"");
		if (!string.IsNullOrWhiteSpace(command))
		{
			sb.Append(" MenuMacroID=\"" + Escape(command) + "\"");
		}
		sb.Append(">\n");
		sb.Append("            <TooltipTitle xlate=\"true\" UID=\"XLS_" + uid + "\">" + Escape(text) + "</TooltipTitle>\n");

		int i = 0;
		foreach (ToolManifest.ManifestButton item in items ?? new List<ToolManifest.ManifestButton>())
		{
			if (item == null || string.IsNullOrWhiteSpace(item.Command) || string.IsNullOrWhiteSpace(item.Text))
			{
				continue;
			}
			i++;
			string childUid = uid + "_I" + i + "_" + Sanitize(item.Command);
			sb.Append("            <RibbonCommandButton UID=\"" + childUid + "\" Id=\"AcRibbonCommandButton\" Text=\""
				+ Escape(item.Text) + "\" ButtonStyle=\"SmallWithText\" MenuMacroID=\"" + Escape(item.Command) + "\" KeyTip=\"\">\n");
			sb.Append("              <TooltipTitle xlate=\"true\" UID=\"XLS_" + childUid + "\">" + Escape(item.Text) + "</TooltipTitle>\n");
			sb.Append("            </RibbonCommandButton>\n");
		}

		sb.Append("          </RibbonSplitButton>\n");
		return sb.ToString();
	}

	private static string Sanitize(string s)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string text = s ?? string.Empty;
		foreach (char c in text)
		{
			stringBuilder.Append(char.IsLetterOrDigit(c) ? c : '_');
		}
		return stringBuilder.ToString();
	}

	private static string Escape(string s)
	{
		return (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
			.Replace("\"", "&quot;");
	}
}
