namespace NcTestRibbon.Cad
{
  using System;
  using System.Reflection;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.DatabaseServices;
  using Teigha.Runtime;
  using CadApp = HostMgd.ApplicationServices.Application;

  /// <summary>
  /// Optional object context-menu hooks for pad / BUOS placement entities.
  /// Uses reflection so missing HostMgd.Windows types do not break compile/load.
  /// See internal report: area-objects-flyout-context-menu.md
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
        // AutoCAD-compatible API often present as HostMgd.Windows.* in nanoCAD hostmgd.
        Type cmeType = Type.GetType("HostMgd.Windows.ContextMenuExtension, hostmgd", throwOnError: false)
          ?? Type.GetType("HostMgd.Windows.ContextMenuExtension, HostMgd", throwOnError: false);
        Type menuItemType = Type.GetType("HostMgd.Windows.MenuItem, hostmgd", throwOnError: false)
          ?? Type.GetType("HostMgd.Windows.MenuItem, HostMgd", throwOnError: false);
        if (cmeType == null || menuItemType == null)
        {
          _status = "HostMgd.Windows.ContextMenuExtension not found in hostmgd — object context menu not registered.";
          return;
        }

        object cme = Activator.CreateInstance(cmeType);
        PropertyInfo title = cmeType.GetProperty("Title");
        title?.SetValue(cme, "Площадные объекты");

        object placeItem = Activator.CreateInstance(menuItemType, "Задать положение вручную");
        object rotateItem = Activator.CreateInstance(menuItemType, "Повернуть вручную");
        object openItem = Activator.CreateInstance(menuItemType, "Открыть исходные данные");

        EventInfo click = menuItemType.GetEvent("Click");
        click?.AddEventHandler(placeItem, new EventHandler((_, __) => SendCommand("NcTestAddPad")));
        click?.AddEventHandler(rotateItem, new EventHandler((_, __) => SendCommand("NcTestPadOpt")));
        click?.AddEventHandler(openItem, new EventHandler((_, __) => SendCommand("NcTestSourceData")));

        object items = cmeType.GetProperty("MenuItems")?.GetValue(cme);
        MethodInfo add = items?.GetType().GetMethod("Add", new[] { menuItemType });
        add?.Invoke(items, new[] { placeItem });
        add?.Invoke(items, new[] { rotateItem });
        add?.Invoke(items, new[] { openItem });

        // Prefer object menu on Entity; Popup filter would refine to our layers/XData.
        MethodInfo addExt = typeof(CadApp).GetMethod(
          "AddObjectContextMenuExtension",
          BindingFlags.Public | BindingFlags.Static,
          binder: null,
          types: new[] { typeof(RXClass), cmeType },
          modifiers: null);
        if (addExt == null)
        {
          _status = "Application.AddObjectContextMenuExtension not found — context menu API unavailable.";
          return;
        }

        RXClass entityClass = RXObject.GetClass(typeof(Entity));
        addExt.Invoke(null, new object[] { entityClass, cme });
        _extension = cme;
        _attached = true;
        _status = "Attached Entity context menu (HostMgd.Windows) — filter by layer/XData still recommended.";
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
        MethodInfo removeExt = typeof(CadApp).GetMethod(
          "RemoveObjectContextMenuExtension",
          BindingFlags.Public | BindingFlags.Static,
          binder: null,
          types: new[] { typeof(RXClass), cmeType },
          modifiers: null);
        removeExt?.Invoke(null, new object[] { RXObject.GetClass(typeof(Entity)), _extension });
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

    private static void SendCommand(string command)
    {
      try
      {
        Document doc = CadApp.DocumentManager.MdiActiveDocument;
        if (doc == null)
        {
          return;
        }

        // Trailing space executes; UsePickSet-friendly for place commands.
        doc.SendStringToExecute(command + " ", true, false, false);
      }
      catch
      {
      }
    }

    /// <summary>Diagnostic helper — writes attach status to the command line.</summary>
    public static void WriteStatusToEditor()
    {
      try
      {
        Editor ed = CadApp.DocumentManager.MdiActiveDocument?.Editor;
        ed?.WriteMessage("\nNcTestRibbon context menu: " + _status);
      }
      catch
      {
      }
    }
  }
}
