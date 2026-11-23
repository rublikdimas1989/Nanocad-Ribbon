using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.Runtime;
using CadApp = HostMgd.ApplicationServices.Application;

namespace NcBootstrap;

public class Commands
{
	private sealed class WindowWrapper : IWin32Window
	{
		public IntPtr Handle { get; }

		public WindowWrapper(IntPtr handle)
		{
			Handle = handle;
		}
	}

	[CommandMethod("NCLOAD")]
	public void NcLoad()
	{
		SyncAndLoad();
	}

	[CommandMethod("NCUPDATE")]
	public void NcUpdate()
	{
		SyncAndLoad();
	}

	private void SyncAndLoad()
	{
		Document mdiActiveDocument = CadApp.DocumentManager.MdiActiveDocument;
		if (mdiActiveDocument == null)
		{
			return;
		}
		Editor editor = mdiActiveDocument.Editor;
		Log.Write("NCLOAD: начало");
		SyncConfig syncConfig = SyncConfig.Load();
		editor.WriteMessage("\nNcBootstrap: сервер " + syncConfig.ServerUrl + ", тип файла \"" + syncConfig.FileType + "\"");
		try
		{
			AuthService authService = new AuthService(syncConfig.ServerUrl);
			if (!authService.Login(syncConfig.Username, syncConfig.Password))
			{
				editor.WriteMessage("\nNcBootstrap: не удалось авторизоваться на сервере " + syncConfig.ServerUrl + " (см. %TEMP%\\NcBootstrap.log)");
				return;
			}
			FileSyncService fileSyncService = new FileSyncService(syncConfig);
			List<FileInfoResponse> serverFileList = fileSyncService.GetServerFileList(syncConfig.FileType);
			if (serverFileList.Count == 0)
			{
				editor.WriteMessage("\nNcBootstrap: сервер не вернул список файлов для \"" + syncConfig.FileType + "\". Пользователь: " + Environment.UserName);
				return;
			}
			List<string> list = fileSyncService.DeleteOrphanedLocalFiles(serverFileList);
			bool flag = list.Count > 0;
			HashSet<string> loadedAssemblyPaths = ToolLoader.GetLoadedAssemblyPaths();
			List<string> list2 = new List<string>();
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			bool flag2 = false;
			foreach (FileInfoResponse item in serverFileList)
			{
				FileSyncCheckResult fileSyncCheckResult = fileSyncService.CheckLocalFile(item);
				editor.WriteMessage("\n------------------------------------------------------------\nNcBootstrap SHA256: " + item.FileName + "\n  Путь       : " + fileSyncCheckResult.LocalPath + "\n  LOCAL SHA256 : " + fileSyncCheckResult.LocalSha256 + "\n  SERVER SHA256: " + (string.IsNullOrWhiteSpace(fileSyncCheckResult.ServerSha256) ? "<СЕРВЕР НЕ ПЕРЕДАЛ SHA256>" : fileSyncCheckResult.ServerSha256) + "\n  Режим      : " + fileSyncCheckResult.ComparisonMode + "\n  STATUS     : " + (fileSyncCheckResult.IsOutdated ? "ОБНОВЛЕНИЕ ТРЕБУЕТСЯ" : "ФАЙЛ АКТУАЛЕН") + (string.IsNullOrWhiteSpace(fileSyncCheckResult.Error) ? string.Empty : ("\n  ОШИБКА     : " + fileSyncCheckResult.Error)) + "\n------------------------------------------------------------");
				if (!fileSyncCheckResult.IsOutdated)
				{
					num3++;
					continue;
				}
				num4++;
				bool flag3 = File.Exists(Path.Combine(fileSyncService.SyncFolder, item.FileName));
				editor.WriteMessage("\nNcBootstrap: скачиваем новую версию " + item.FileName + "...");
				if (!fileSyncService.DownloadFile(item))
				{
					num5++;
					editor.WriteMessage("\nNcBootstrap: ОШИБКА ОБНОВЛЕНИЯ " + item.FileName + ". SHA отличается, но новую версию безопасно применить не удалось. Текущая локальная версия оставлена в работе.");
					continue;
				}
				editor.WriteMessage("\nNcBootstrap: новая версия " + item.FileName + " успешно скачана и проверена по SHA256.");
				if (flag3)
				{
					num2++;
				}
				else
				{
					num++;
				}
				string fullPath = Path.GetFullPath(Path.Combine(fileSyncService.SyncFolder, item.FileName));
				if (item.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !loadedAssemblyPaths.Contains(fullPath))
				{
					list2.Add(fullPath);
				}
				if (item.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && loadedAssemblyPaths.Contains(fullPath))
				{
					flag2 = true;
				}
			}
			bool flag4 = File.Exists(Path.Combine(fileSyncService.SyncFolder, "NC_REPLACE_PENDING.txt"));
			editor.WriteMessage("\nСинхронизация завершена: требуется обновить " + num4 + ", успешно обновлено/скачано " + (num + num2) + ", ошибок обновления " + num5 + ", удалено старых файлов " + list.Count + ", без изменений " + num3 + ".");
			if (flag4)
			{
				editor.WriteMessage("\nNcBootstrap: новая версия загруженной DLL сохранена. Она будет заменена автоматически при следующем запуске nanoCAD.");
			}
			if (flag2)
			{
				editor.WriteMessage("\nNcBootstrap: обновлена библиотека, уже загруженная в текущий сеанс. Перезапустите nanoCAD, чтобы применить новую версию.");
			}
			string[] files = Directory.GetFiles(fileSyncService.SyncFolder, "*.dll");
			foreach (string path in files)
			{
				string fullPath2 = Path.GetFullPath(path);
				if (!list2.Contains(fullPath2) && !ToolLoader.GetLoadedAssemblyPaths().Contains(fullPath2))
				{
					list2.Add(fullPath2);
				}
			}
			int num6 = 0;
			foreach (string item2 in list2)
			{
				if (ToolLoader.LoadModuleViaCom(item2, out var error))
				{
					num6++;
					editor.WriteMessage("\nNcBootstrap: загружен " + Path.GetFileName(item2));
				}
				else
				{
					editor.WriteMessage("\nNcBootstrap: не загрузился " + Path.GetFileName(item2) + ": " + error);
				}
			}
			editor.WriteMessage("\nNcBootstrap: DLL в автозагрузке по текущей конфигурации сервера: " + ProfileInstaller.SynchronizeSyncToolsStartup(fileSyncService.SyncFolder, from f in serverFileList
				where f.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
				select f.FileName) + ".");
			int num7 = 0;
			List<ToolManifest> list3;
			if (flag4)
			{
				list3 = ToolManifest.LoadAll(fileSyncService.SyncFolder);
				editor.WriteMessage("\nNcBootstrap: применение новой DLL и перестроение панели будут выполнены после перезапуска nanoCAD.");
			}
			else
			{
				num7 = ToolLoader.ExecuteShowMainForm(fileSyncService.SyncFolder, editor);
				ProfileInstaller.EnsureUiInstalled(fileSyncService.SyncFolder);
				list3 = ToolManifest.LoadAll(fileSyncService.SyncFolder);
			}
			int panelCount = CountPanels(list3);
			editor.WriteMessage("\nNcBootstrap: загружено модулей " + num6 + ", SHOWMAINFORM в " + num7 + " DLL, панелей на вкладке: " + panelCount + ".");
			Log.Write("NCLOAD: завершено (скачано " + num + ", обновлено " + num2 + ", загружено " + num6 + ", панелей " + panelCount + ")");
			if (num5 > 0)
			{
				CadApp.ShowAlertDialog("Обнаружены более новые файлы на сервере, но " + num5 + " файл(а/ов) обновить не удалось.\n\nСтарая локальная версия оставлена в работе.\nПодробности смотрите в командной строке nanoCAD.");
				return;
			}
			if (num > 0 || num2 > 0 || flag)
			{
				editor.WriteMessage("\nНайдена новая конфигурация инструментов ЦРПР.");
				ProposeRestart();
				return;
			}
			if (num4 > 0)
			{
				CadApp.ShowAlertDialog("Обнаружено отличие файлов по SHA256.\nИнструменты нельзя считать актуальными.\nПодробности смотрите в командной строке nanoCAD.");
				return;
			}
			CadApp.ShowAlertDialog("Инструменты актуальны.\nВсе локальные SHA256 совпадают с серверными.\nЗагружено модулей в сеанс: " + num6 + ", панелей на вкладке «Инструменты ЦРПР»: " + CountPanels(list3) + ".");
		}
		catch (System.Exception ex)
		{
			Log.Write("NCLOAD: ОШИБКА " + ex);
			editor.WriteMessage("\nNcBootstrap: ошибка синхронизации: " + ex.Message);
		}
	}

	private static int CountPanels(List<ToolManifest> manifests)
	{
		int n = 0;
		foreach (ToolManifest m in manifests ?? new List<ToolManifest>())
		{
			foreach (ToolManifest.ManifestPanel _ in m.EnumeratePanels())
			{
				n++;
			}
		}
		return n;
	}

	private static void ProposeRestart()
	{
		try
		{
			WindowWrapper windowWrapper = new WindowWrapper(Process.GetCurrentProcess().MainWindowHandle);
			DialogResult val = MessageBox.Show(
				(IWin32Window)(object)windowWrapper,
				"Найдена новая конфигурация инструментов ЦРПР.\nПерезагрузить nanoCAD?",
				"Инструменты ЦРПР",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Question);
			if (val == DialogResult.Yes)
			{
				Process currentProcess = Process.GetCurrentProcess();
				string fileName = currentProcess.MainModule.FileName;
				ProcessStartInfo processStartInfo = new ProcessStartInfo();
				processStartInfo.FileName = "powershell";
				processStartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Wait-Process -Id " + currentProcess.Id + "; Start-Sleep -Seconds 1; Start-Process '" + fileName + "'\"";
				processStartInfo.UseShellExecute = false;
				processStartInfo.CreateNoWindow = true;
				Process.Start(processStartInfo);
				if (!currentProcess.CloseMainWindow())
				{
					throw new InvalidOperationException("Не удалось отправить команду закрытия главному окну nanoCAD.");
				}
			}
		}
		catch (System.Exception ex)
		{
			Log.Write("ProposeRestart: " + ex.Message);
		}
	}

	[CommandMethod("NCUNLOAD")]
	public void NcUnload()
	{
		Document mdiActiveDocument = CadApp.DocumentManager.MdiActiveDocument;
		if (mdiActiveDocument == null)
		{
			return;
		}
		Editor editor = mdiActiveDocument.Editor;
		Log.Write("NCUNLOAD: начало");
		try
		{
			SyncConfig syncConfig = SyncConfig.Load();
			string resolvedSyncFolder = syncConfig.ResolvedSyncFolder;
			ProfileInstaller.RemoveSyncToolsFromStartup();
			List<string> list = ProfileInstaller.DeleteSyncFiles(resolvedSyncFolder);
			UiGenerator.Write(ProfileInstaller.GetConfigDir(), new List<ToolManifest>());
			if (list.Count > 0)
			{
				editor.WriteMessage("\nNcBootstrap: инструменты выгружены. Файлы, заблокированные текущим сеансом (" + list.Count + " шт.), будут удалены автоматически после перезапуска.");
			}
			editor.WriteMessage("\nNcBootstrap: панели исчезнут с вкладки после перезапуска nanoCAD. Кнопка «Загрузить» вернёт их (файлы будут скачаны с сервера заново).");
			Log.Write("NCUNLOAD: завершено, отложено файлов " + list.Count);
		}
		catch (System.Exception ex)
		{
			Log.Write("NCUNLOAD: ОШИБКА " + ex);
			editor.WriteMessage("\nNcBootstrap: ошибка выгрузки: " + ex.Message);
		}
	}

	[CommandMethod("NCDIAG")]
	public void NcDiag()
	{
		Document mdiActiveDocument = CadApp.DocumentManager.MdiActiveDocument;
		Editor val = mdiActiveDocument?.Editor;
		if (val == null)
		{
			return;
		}
		SyncConfig syncConfig = SyncConfig.Load();
		string resolvedSyncFolder = syncConfig.ResolvedSyncFolder;
		val.WriteMessage("\nNcBootstrap: " + typeof(Commands).Assembly.Location);
		val.WriteMessage("\nСервер: " + syncConfig.ServerUrl + "  (тип файла \"" + syncConfig.FileType + "\")");
		val.WriteMessage("\nПапка синхронизации: " + resolvedSyncFolder);
		if (Directory.Exists(resolvedSyncFolder))
		{
			string[] files = Directory.GetFiles(resolvedSyncFolder);
			val.WriteMessage("\nЛокальных файлов: " + files.Length);
			foreach (string item in files.Take(15))
			{
				val.WriteMessage("\n  - " + Path.GetFileName(item));
			}
		}
		else
		{
			val.WriteMessage("\nПапка синхронизации не создана (нажмите «Загрузить»).");
		}
		List<ToolManifest> manifests = ToolManifest.LoadAll(resolvedSyncFolder);
		val.WriteMessage("\nМанифестов инструментов: " + manifests.Count + ", панелей: " + CountPanels(manifests));
		val.WriteMessage("\nАвторизован: " + CurrentSession.IsAuthenticated + "  Пользователь: " + CurrentSession.UserName);
		val.WriteMessage("\nЛог: %TEMP%\\NcBootstrap.log");
	}
}
