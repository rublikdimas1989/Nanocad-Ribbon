using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NcBootstrap;

internal static class ProfileInstaller
{
	private const string MasterCfgName = "nanoCAD.cfg";

	private const string CfgIniName = "Cfg.ini";

	internal static bool EnsureUiInstalled(string syncFolder)
	{
		string text = FindProfileConfigDir();
		string text2 = Path.Combine(text, "nanoCAD.cfg");
		Log.Write("профиль: " + text2);
		RemoveObsoleteIncludes(text, text2);
		List<ToolManifest> manifests = ToolManifest.LoadAll(syncFolder);
		UiGenerator.Write(text, manifests, syncFolder);
		string text3 = Path.Combine(text, "NC_Tools.cfg");
		string text4 = "#include \"" + text3 + "\"";
		string text5 = File.ReadAllText(text2, Encoding.UTF8);
		if (text5.Contains(text4))
		{
			return false;
		}
		File.AppendAllText(text2, Environment.NewLine + text4 + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		Log.Write("include добавлен: " + text4);
		return true;
	}

	private static void RemoveObsoleteIncludes(string configDir, string masterCfg)
	{
		string[] array = new string[3] { "NcBootstrap.cfg", "NC_RibbonExecDoc.cfg", "RibbonExecDoc.cfg" };
		string text = File.ReadAllText(masterCfg, Encoding.UTF8);
		bool flag = false;
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			string text3 = "#include \"" + Path.Combine(configDir, text2) + "\"";
			if (text.Contains(text3))
			{
				text = text.Replace(text3, string.Empty);
				flag = true;
				Log.Write("убран устаревший include: " + text2);
			}
			string path = Path.Combine(configDir, text2);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			string path2 = Path.ChangeExtension(path, ".cuix");
			if (File.Exists(path2))
			{
				File.Delete(path2);
			}
		}
		if (flag)
		{
			File.WriteAllText(masterCfg, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		}
	}

	internal static string GetConfigDir()
	{
		return FindProfileConfigDir();
	}

	internal static List<string> DeleteSyncFiles(string syncFolder)
	{
		List<string> list = new List<string>();
		if (!Directory.Exists(syncFolder))
		{
			return list;
		}
		string[] files = Directory.GetFiles(syncFolder);
		foreach (string text in files)
		{
			try
			{
				File.Delete(text);
			}
			catch (Exception ex)
			{
				list.Add(Path.GetFileName(text));
				Log.Write("файл заблокирован, отложено: " + text + " (" + ex.Message + ")");
			}
		}
		string path = Path.Combine(syncFolder, "NC_DELETE_PENDING.txt");
		if (list.Count > 0)
		{
			File.WriteAllLines(path, list, Encoding.UTF8);
		}
		else
		{
			try
			{
				Directory.Delete(syncFolder, recursive: false);
			}
			catch
			{
			}
		}
		return list;
	}

	internal static void ProcessPendingReplacements(string syncFolder)
	{
		string path = Path.Combine(syncFolder, "NC_REPLACE_PENDING.txt");
		if (!File.Exists(path))
		{
			return;
		}
		List<string> list = new List<string>();
		string[] array = File.ReadAllLines(path, Encoding.UTF8);
		foreach (string text in array)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			string[] array2 = text.Split(new char[1] { '|' }, 2);
			string text2 = array2[0].Trim();
			string text3 = ((array2.Length > 1) ? array2[1].Trim() : string.Empty);
			if (text2.Length == 0)
			{
				continue;
			}
			string text4 = Path.Combine(syncFolder, text2);
			string text5 = text4 + ".ncupdate";
			try
			{
				if (File.Exists(text5))
				{
					if (string.IsNullOrWhiteSpace(text3))
					{
						goto IL_012e;
					}
					string left = HashUtils.Sha256File(text5);
					if (HashUtils.EqualsSha256(left, text3))
					{
						goto IL_012e;
					}
					Log.Write("отложенное обновление ОТМЕНЕНО: SHA256 не совпал для " + text2 + ". Старая версия оставлена в работе.");
					try
					{
						File.Delete(text5);
					}
					catch
					{
					}
				}
				goto end_IL_00bc;
				IL_012e:
				if (text2.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(text4))
				{
					File.Copy(text4, text4 + ".previous", overwrite: true);
				}
				if (File.Exists(text4))
				{
					File.Delete(text4);
				}
				File.Move(text5, text4);
				Log.Write("старая версия удалена, отложенное SHA-проверенное обновление применено: " + text2);
				end_IL_00bc:;
			}
			catch (Exception ex)
			{
				list.Add(text.Trim());
				Log.Write("отложенное обновление пока не применено: " + text2 + " (" + ex.Message + "). Старая версия остаётся в работе.");
			}
		}
		try
		{
			if (list.Count == 0)
			{
				File.Delete(path);
			}
			else
			{
				File.WriteAllLines(path, list, Encoding.UTF8);
			}
		}
		catch
		{
		}
	}

	internal static void ProcessPendingDeletes(string syncFolder)
	{
		string path = Path.Combine(syncFolder, "NC_DELETE_PENDING.txt");
		if (!File.Exists(path))
		{
			return;
		}
		List<string> list = new List<string>();
		string[] array = File.ReadAllLines(path, Encoding.UTF8);
		foreach (string text in array)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			string path2 = Path.Combine(syncFolder, text.Trim());
			try
			{
				if (File.Exists(path2))
				{
					File.Delete(path2);
					Log.Write("отложенное удаление выполнено: " + text);
				}
			}
			catch
			{
				list.Add(text.Trim());
			}
		}
		try
		{
			if (list.Count == 0)
			{
				File.Delete(path);
			}
			else
			{
				File.WriteAllLines(path, list, Encoding.UTF8);
			}
		}
		catch
		{
		}
	}

	internal static int SynchronizeSyncToolsStartup(string syncFolder, IEnumerable<string> currentDllFileNames)
	{
		string cfgIni = Path.Combine(FindProfileConfigDir(), "Cfg.ini");
		List<string> list = new List<string>();
		foreach (string item in currentDllFileNames ?? Enumerable.Empty<string>())
		{
			if (!string.IsNullOrWhiteSpace(item) && item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			{
				string fullPath = Path.GetFullPath(Path.Combine(syncFolder, item));
				if (File.Exists(fullPath) && !list.Contains<string>(fullPath, StringComparer.OrdinalIgnoreCase))
				{
					list.Add(fullPath);
				}
			}
		}
		RemoveStartupSectionsForSyncFolder(cfgIni, syncFolder);
		foreach (string item2 in list)
		{
			AddToStartup(item2);
		}
		Log.Write("автозагрузка синхронизирована с сервером: DLL=" + list.Count);
		return list.Count;
	}

	private static int RemoveStartupSectionsForSyncFolder(string cfgIni, string syncFolder)
	{
		if (!File.Exists(cfgIni))
		{
			return 0;
		}
		string fullSync = Path.GetFullPath(syncFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string[] array = File.ReadAllLines(cfgIni, Encoding.UTF8);
		List<string> output = new List<string>();
		List<string> section = new List<string>();
		int removed = 0;
		Action action = delegate
		{
			if (section.Count != 0)
			{
				bool flag = Regex.IsMatch(section[0], "^\\[\\\\Configuration\\\\<<Default>>\\\\Appload\\\\Startup\\\\app\\d+\\]$", RegexOptions.IgnoreCase);
				bool flag2 = false;
				if (flag)
				{
					foreach (string item in section)
					{
						if (item.StartsWith("Loader=s", StringComparison.OrdinalIgnoreCase))
						{
							string path = item.Substring("Loader=s".Length).Trim();
							try
							{
								string fullPath = Path.GetFullPath(path);
								flag2 = fullPath.StartsWith(fullSync + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(fullPath, fullSync, StringComparison.OrdinalIgnoreCase);
							}
							catch
							{
							}
							break;
						}
					}
				}
				if (flag2)
				{
					removed++;
				}
				else
				{
					output.AddRange(section);
				}
				section.Clear();
			}
		};
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (text.StartsWith("["))
			{
				action();
			}
			section.Add(text);
		}
		action();
		File.WriteAllLines(cfgIni, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		if (removed > 0)
		{
			Log.Write("удалено старых Startup-секций папки синхронизации: " + removed);
		}
		return removed;
	}

	internal static void AddToStartup(string dllPath)
	{
		string path = Path.Combine(FindProfileConfigDir(), "Cfg.ini");
		string text = File.ReadAllText(path, Encoding.UTF8);
		if (text.Contains(dllPath))
		{
			return;
		}
		HashSet<int> hashSet = new HashSet<int>();
		foreach (Match item in Regex.Matches(text, "\\\\Appload\\\\Startup\\\\app(\\d+)\\]"))
		{
			hashSet.Add(int.Parse(item.Groups[1].Value));
		}
		int i;
		for (i = 0; hashSet.Contains(i); i++)
		{
		}
		string contents = "\r\n[\\Configuration\\<<Default>>\\Appload\\Startup\\app" + i + "]\r\nLoader=s" + dllPath + "\r\nType=sMGD\r\nEnabled=i1\r\n";
		File.AppendAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		Log.Write("автозагрузка добавлена (app" + i + "): " + dllPath);
	}

	internal static void RemoveSyncToolsFromStartup()
	{
		SyncConfig syncConfig = SyncConfig.Load();
		string cfgIni = Path.Combine(FindProfileConfigDir(), "Cfg.ini");
		Log.Write("автозагрузка инструментов убрана: " + RemoveStartupSectionsForSyncFolder(cfgIni, syncConfig.ResolvedSyncFolder) + " секций");
	}

	private static string FindProfileConfigDir()
	{
		string installDirName = null;
		try
		{
			installDirName = Path.GetFileName(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName).TrimEnd('\\'));
		}
		catch
		{
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nanosoft");
		if (!Directory.Exists(text))
		{
			throw new DirectoryNotFoundException("Каталог профилей не найден: " + text);
		}
		var source = (from d in Directory.GetDirectories(text)
			select new
			{
				Dir = d,
				Config = Path.Combine(d, "Config")
			} into x
			where File.Exists(Path.Combine(x.Config, "nanoCAD.cfg"))
			select x).ToList();
		var anon = source.FirstOrDefault(x => string.Equals(Path.GetFileName(x.Dir), installDirName, StringComparison.OrdinalIgnoreCase));
		if (anon != null)
		{
			return anon.Config;
		}
		var anon2 = source.OrderByDescending(x => File.GetLastWriteTime(Path.Combine(x.Config, "nanoCAD.cfg"))).FirstOrDefault();
		if (anon2 == null)
		{
			throw new FileNotFoundException("Не найден профиль с файлом nanoCAD.cfg в " + text);
		}
		return anon2.Config;
	}
}
