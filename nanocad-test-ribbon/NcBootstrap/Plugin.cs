using System;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.Runtime;
using CadApp = HostMgd.ApplicationServices.Application;

namespace NcBootstrap;

public class Plugin : IExtensionApplication
{
	public void Initialize()
	{
		Log.Write("Initialize: начало");
		Document mdiActiveDocument = CadApp.DocumentManager.MdiActiveDocument;
		Editor val = mdiActiveDocument?.Editor;
		try
		{
			SyncConfig syncConfig = SyncConfig.Load();
			ProfileInstaller.ProcessPendingReplacements(syncConfig.ResolvedSyncFolder);
			ProfileInstaller.ProcessPendingDeletes(syncConfig.ResolvedSyncFolder);
			bool flag = ProfileInstaller.EnsureUiInstalled(syncConfig.ResolvedSyncFolder);
			if (val != null && flag)
			{
				val.WriteMessage("\nNcBootstrap: вкладка «Инструменты ЦРПР» подключена. Перезапустите nanoCAD, чтобы она появилась. До перезапуска доступны команды NCLOAD и NCUNLOAD.");
			}
			Log.Write("Initialize: завершено, include добавлен=" + flag);
		}
		catch (System.Exception ex)
		{
			Log.Write("Initialize: ОШИБКА " + ex);
			if (val != null)
			{
				val.WriteMessage("\nNcBootstrap: ошибка установки вкладки: " + ex.Message);
			}
		}
	}

	public void Terminate()
	{
	}
}
