namespace NcTestRibbon.Cad
{
  using System;
  using System.Reflection;

  /// <summary>
  /// Optional object context-menu hooks for pad / BUOS placement entities.
  /// Fully reflection-based so missing HostMgd.Windows / RX helpers never break compile.
  /// </summary>
  public static class CadSiteContextMenus
  {
    private static object _extension;
    private static bool _attached;
    private static string _status = "not attempted";

    public static string Status => _status;

    public static void TryAttach()
    {
      if (_attached)
      {
        return;
      }

      try
      {
        Type cmeType = FindType("HostMgd.Windows.ContextMenuExtension", "hostmgd", "HostMgd");
        Type menuItemType = FindType("HostMgd.Windows.MenuItem", "hostmgd", "HostMgd");
        if (cmeType == null || menuItemType == null)
        {
          _status = "HostMgd.Windows.ContextMenuExtension not found — object context menu not registered.";
          return;
        }

        object cme = Activator.CreateInstance(cmeType);
        cmeType.GetProperty("Title")?.SetValue(cme, "Площадные объекты");

        object placeItem = CreateMenuItem(menuItemType, "Задать положение вручную");
        object rotateItem = CreateMenuItem(menuItemType, "Повернуть вручную");
        object openItem = CreateMenuItem(menuItemType, "Открыть исходные данные");
        if (placeItem == null || rotateItem == null || openItem == null)
        {
          _status = "HostMgd.Windows.MenuItem ctor failed.";
          return;
        }

        EventInfo click = menuItemType.GetEvent("Click");
        click?.AddEventHandler(placeItem, new EventHandler((_, __) => SendCommand("NcTestAddPad")));
        click?.AddEventHandler(rotateItem, new EventHandler((_, __) => SendCommand("NcTestPadOpt")));
        click?.AddEventHandler(openItem, new EventHandler((_, __) => SendCommand("NcTestSourceData")));

        object items = cmeType.GetProperty("MenuItems")?.GetValue(cme);
        MethodInfo add = items?.GetType().GetMethod("Add", new[] { menuItemType });
        add?.Invoke(items, new[] { placeItem });
        add?.Invoke(items, new[] { rotateItem });
        add?.Invoke(items, new[] { openItem });

        Type appType = FindType("HostMgd.ApplicationServices.Application", "hostmgd", "HostMgd");
        Type rxClassType = FindType("Teigha.Runtime.RXClass", "hostdbmgd", "hostdbmgd", "TD_Mgd", "Teigha");
        Type entityType = FindType("Teigha.DatabaseServices.Entity", "hostdbmgd", "hostdbmgd", "TD_Mgd", "Teigha");
        if (appType == null || rxClassType == null || entityType == null)
        {
          _status = "HostMgd/Teigha types missing for context-menu attach.";
          return;
        }

        object entityRx = GetRxClass(rxClassType, entityType);
        if (entityRx == null)
        {
          _status = "RXClass.GetClass(Entity) unavailable.";
          return;
        }

        MethodInfo addExt = appType.GetMethod(
          "AddObjectContextMenuExtension",
          BindingFlags.Public | BindingFlags.Static,
          binder: null,
          types: new[] { rxClassType, cmeType },
          modifiers: null);
        if (addExt == null)
        {
          // Some hosts expose only default menu extension.
          addExt = appType.GetMethod(
            "AddDefaultContextMenuExtension",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { cmeType },
            modifiers: null);
          if (addExt == null)
          {
            _status = "No AddObject/DefaultContextMenuExtension on Application.";
            return;
          }

          addExt.Invoke(null, new[] { cme });
        }
        else
        {
          addExt.Invoke(null, new[] { entityRx, cme });
        }

        _extension = cme;
        _attached = true;
        _status = "Attached context menu via HostMgd.Windows (reflection).";
      }
      catch (Exception ex)
      {
        _status = "Attach failed: " + ex.GetType().Name + ": " + ex.Message;
        _attached = false;
        _extension = null;
      }
    }

    public static void TryDetach()
    {
      if (!_attached || _extension == null)
      {
        return;
      }

      try
      {
        Type cmeType = _extension.GetType();
        Type appType = FindType("HostMgd.ApplicationServices.Application", "hostmgd", "HostMgd");
        Type rxClassType = FindType("Teigha.Runtime.RXClass", "hostdbmgd", "hostdbmgd", "TD_Mgd", "Teigha");
        Type entityType = FindType("Teigha.DatabaseServices.Entity", "hostdbmgd", "hostdbmgd", "TD_Mgd", "Teigha");
        MethodInfo removeExt = appType?.GetMethod(
          "RemoveObjectContextMenuExtension",
          BindingFlags.Public | BindingFlags.Static,
          binder: null,
          types: new[] { rxClassType, cmeType },
          modifiers: null);
        if (removeExt != null && rxClassType != null && entityType != null)
        {
          object entityRx = GetRxClass(rxClassType, entityType);
          if (entityRx != null)
          {
            removeExt.Invoke(null, new[] { entityRx, _extension });
          }
        }
        else
        {
          MethodInfo removeDefault = appType?.GetMethod(
            "RemoveDefaultContextMenuExtension",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { cmeType },
            modifiers: null);
          removeDefault?.Invoke(null, new[] { _extension });
        }
      }
      catch
      {
      }
      finally
      {
        _attached = false;
        _extension = null;
      }
    }

    private static object CreateMenuItem(Type menuItemType, string text)
    {
      try
      {
        return Activator.CreateInstance(menuItemType, text);
      }
      catch
      {
        try
        {
          object item = Activator.CreateInstance(menuItemType);
          menuItemType.GetProperty("Text")?.SetValue(item, text);
          menuItemType.GetProperty("Header")?.SetValue(item, text);
          return item;
        }
        catch
        {
          return null;
        }
      }
    }

    private static object GetRxClass(Type rxClassType, Type entityType)
    {
      MethodInfo getClass = rxClassType.GetMethod(
        "GetClass",
        BindingFlags.Public | BindingFlags.Static,
        binder: null,
        types: new[] { typeof(Type) },
        modifiers: null);
      if (getClass != null)
      {
        return getClass.Invoke(null, new object[] { entityType });
      }

      Type rxObjectType = FindType("Teigha.Runtime.RXObject", "hostdbmgd", "hostdbmgd", "TD_Mgd", "Teigha");
      MethodInfo viaObject = rxObjectType?.GetMethod(
        "GetClass",
        BindingFlags.Public | BindingFlags.Static,
        binder: null,
        types: new[] { typeof(Type) },
        modifiers: null);
      return viaObject?.Invoke(null, new object[] { entityType });
    }

    private static void SendCommand(string command)
    {
      try
      {
        Type appType = FindType("HostMgd.ApplicationServices.Application", "hostmgd", "HostMgd");
        object docs = appType?.GetProperty("DocumentManager", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        object doc = docs?.GetType().GetProperty("MdiActiveDocument")?.GetValue(docs);
        if (doc == null)
        {
          return;
        }

        MethodInfo send = doc.GetType().GetMethod(
          "SendStringToExecute",
          BindingFlags.Public | BindingFlags.Instance,
          binder: null,
          types: new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool) },
          modifiers: null);
        send?.Invoke(doc, new object[] { command + " ", true, false, false });
      }
      catch
      {
      }
    }

    private static Type FindType(string fullName, params string[] assemblyHints)
    {
      Type t = Type.GetType(fullName, throwOnError: false);
      if (t != null)
      {
        return t;
      }

      foreach (string hint in assemblyHints)
      {
        t = Type.GetType(fullName + ", " + hint, throwOnError: false);
        if (t != null)
        {
          return t;
        }
      }

      foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
      {
        try
        {
          t = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
          if (t != null)
          {
            return t;
          }
        }
        catch
        {
        }
      }

      return null;
    }
  }
}
