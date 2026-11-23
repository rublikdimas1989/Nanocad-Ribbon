using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;

namespace NcBootstrap;

internal static class ToolLoader
{
	public static int ExecuteShowMainForm(string syncFolder, Editor ed)
	{
		int num = 0;
		string[] files = Directory.GetFiles(syncFolder, "*.dll");
		foreach (string text in files)
		{
			try
			{
				Assembly assembly = Assembly.LoadFrom(text);
				foreach (Type item in from t in assembly.GetTypes()
					where t.IsClass && !t.IsAbstract
					select t)
				{
					MethodInfo method = item.GetMethod("SHOWMAINFORM", BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public);
					if (method == null)
					{
						continue;
					}
					try
					{
						object obj = (method.IsStatic ? null : Activator.CreateInstance(item));
						method.Invoke(obj, null);
						num++;
						ed.WriteMessage("\n  SHOWMAINFORM выполнен: " + Path.GetFileName(text));
					}
					catch (Exception ex)
					{
						ed.WriteMessage("\n  ошибка SHOWMAINFORM в " + Path.GetFileName(text) + ": " + ex.InnerException?.Message);
					}
					break;
				}
			}
			catch (Exception ex2)
			{
				ed.WriteMessage("\n  не загружается " + Path.GetFileName(text) + ": " + ex2.Message);
			}
		}
		return num;
	}

	public static bool LoadModuleViaCom(string dllPath, out string error)
	{
		error = null;
		try
		{
			dynamic acadApplication = Application.AcadApplication;
			acadApplication.LoadModule(dllPath);
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	public static HashSet<string> GetLoadedAssemblyPaths()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(assembly.Location))
				{
					hashSet.Add(Path.GetFullPath(assembly.Location));
				}
			}
			catch
			{
			}
		}
		return hashSet;
	}
}
