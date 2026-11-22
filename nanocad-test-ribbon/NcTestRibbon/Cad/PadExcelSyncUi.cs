namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using System.Reflection;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Media;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using CadApp = HostMgd.ApplicationServices.Application;

  /// <summary>
  /// Highlights NC Tools ribbon button «Сохранить в исходные» when pad CAD→Excel sync is pending.
  /// Uses reflection / WPF visual-tree walk — no MessageBox.
  /// </summary>
  public static class PadExcelSyncUi
  {
    public const string CommandName = "NcTestSaveSources";
    public const string ButtonText = "Сохранить в исходные";
    public const string LegacyCommandName = "NcTestPadSyncExcel";

    private static readonly object Gate = new object();
    private static bool _pending;
    private static readonly Dictionary<object, Brush> OriginalBackgrounds = new Dictionary<object, Brush>();
    private static readonly Dictionary<object, object> OriginalTexts = new Dictionary<object, object>();
    private static Brush _redBrush;

    public static bool IsPending
    {
      get { lock (Gate) { return _pending; } }
    }

    public static void SetPending(bool pending)
    {
      bool changed;
      lock (Gate)
      {
        changed = _pending != pending;
        _pending = pending;
      }

      if (!changed && pending)
      {
        // Still refresh highlight (button may have been rebuilt).
      }

      try
      {
        ApplyHighlight(pending);
      }
      catch
      {
      }

      try
      {
        Document doc = CadApp.DocumentManager.MdiActiveDocument;
        Editor ed = doc?.Editor;
        if (ed == null)
        {
          return;
        }

        if (pending)
        {
          ed.WriteMessage(
            "\nКП: изменения на чертеже — «Площадные объекты» → «Сохранить в исходные».");
        }
      }
      catch
      {
      }
    }

    public static void Refresh()
    {
      SetPending(IsPending);
    }

    private static void ApplyHighlight(bool pending)
    {
      _redBrush ??= new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
      if (_redBrush.CanFreeze)
      {
        _redBrush.Freeze();
      }

      int touched = 0;
      touched += TryHighlightAdWindows(pending);
      touched += TryHighlightWpfTree(pending);

      // Soft fallback: nothing found yet (ribbon not ready) — OK, button still works via CUI.
      _ = touched;
    }

    private static int TryHighlightAdWindows(bool pending)
    {
      int count = 0;
      try
      {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
          Type cm = null;
          try
          {
            cm = asm.GetType("Autodesk.Windows.ComponentManager", false)
              ?? asm.GetType("HostMgd.Windows.ComponentManager", false);
          }
          catch
          {
            continue;
          }

          if (cm == null)
          {
            continue;
          }

          PropertyInfo ribbonProp = cm.GetProperty("Ribbon", BindingFlags.Public | BindingFlags.Static);
          object ribbon = ribbonProp?.GetValue(null);
          if (ribbon == null)
          {
            continue;
          }

          count += WalkAdRibbon(ribbon, pending);
        }
      }
      catch
      {
      }

      return count;
    }

    private static int WalkAdRibbon(object node, bool pending)
    {
      if (node == null)
      {
        return 0;
      }

      int count = 0;
      try
      {
        if (LooksLikeSyncButton(node))
        {
          ApplyAdButtonStyle(node, pending);
          count++;
        }
      }
      catch
      {
      }

      try
      {
        foreach (PropertyInfo prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
          if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType))
          {
            continue;
          }

          if (prop.GetIndexParameters().Length > 0)
          {
            continue;
          }

          string name = prop.Name;
          if (name != "Tabs" && name != "Panels" && name != "Items"
              && name != "Children" && name != "SourceItems" && name != "Controls")
          {
            continue;
          }

          object coll = prop.GetValue(node);
          if (coll is not System.Collections.IEnumerable en || coll is string)
          {
            continue;
          }

          foreach (object child in en)
          {
            count += WalkAdRibbon(child, pending);
          }
        }
      }
      catch
      {
      }

      return count;
    }

    private static bool LooksLikeSyncButton(object node)
    {
      Type t = node.GetType();
      string typeName = t.Name ?? string.Empty;
      if (typeName.IndexOf("Button", StringComparison.OrdinalIgnoreCase) < 0
          && typeName.IndexOf("CommandItem", StringComparison.OrdinalIgnoreCase) < 0)
      {
        return false;
      }

      string id = ReadStringProp(node, "Id")
        ?? ReadStringProp(node, "UID")
        ?? ReadStringProp(node, "Name")
        ?? string.Empty;
      string text = ReadStringProp(node, "Text")
        ?? ReadStringProp(node, "Name")
        ?? string.Empty;
      string macro = ReadStringProp(node, "MenuMacroID")
        ?? ReadStringProp(node, "Command")
        ?? string.Empty;

      if (macro.IndexOf(CommandName, StringComparison.OrdinalIgnoreCase) >= 0
          || macro.IndexOf(LegacyCommandName, StringComparison.OrdinalIgnoreCase) >= 0)
      {
        return true;
      }

      if (id.IndexOf(CommandName, StringComparison.OrdinalIgnoreCase) >= 0
          || id.IndexOf(LegacyCommandName, StringComparison.OrdinalIgnoreCase) >= 0)
      {
        return true;
      }

      return text.IndexOf(ButtonText, StringComparison.OrdinalIgnoreCase) >= 0
        || text.IndexOf("Обновить данные в Excel", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void ApplyAdButtonStyle(object node, bool pending)
    {
      lock (Gate)
      {
        try
        {
          PropertyInfo bg = node.GetType().GetProperty("Background");
          if (bg != null && bg.CanWrite)
          {
            if (!OriginalBackgrounds.ContainsKey(node))
            {
              OriginalBackgrounds[node] = bg.GetValue(node) as Brush;
            }

            bg.SetValue(node, pending ? _redBrush : OriginalBackgrounds[node]);
          }
        }
        catch
        {
        }

        try
        {
          PropertyInfo textProp = node.GetType().GetProperty("Text")
            ?? node.GetType().GetProperty("Name");
          if (textProp != null && textProp.CanWrite && textProp.PropertyType == typeof(string))
          {
            if (!OriginalTexts.ContainsKey(node))
            {
              OriginalTexts[node] = textProp.GetValue(node);
            }

            string baseText = OriginalTexts[node] as string ?? ButtonText;
            textProp.SetValue(node, pending ? ("● " + baseText) : baseText);
          }
        }
        catch
        {
        }
      }
    }

    private static int TryHighlightWpfTree(bool pending)
    {
      int count = 0;
      try
      {
        if (System.Windows.Application.Current == null)
        {
          return 0;
        }

        foreach (Window window in System.Windows.Application.Current.Windows)
        {
          count += WalkVisual(window, pending);
        }
      }
      catch
      {
      }

      return count;
    }

    private static int WalkVisual(DependencyObject root, bool pending)
    {
      if (root == null)
      {
        return 0;
      }

      int count = 0;
      try
      {
        if (root is Button btn)
        {
          string content = btn.Content?.ToString() ?? string.Empty;
          string name = btn.Name ?? string.Empty;
          string tip = btn.ToolTip?.ToString() ?? string.Empty;
          if (content.IndexOf(ButtonText, StringComparison.OrdinalIgnoreCase) >= 0
              || content.IndexOf("Обновить данные в Excel", StringComparison.OrdinalIgnoreCase) >= 0
              || name.IndexOf(CommandName, StringComparison.OrdinalIgnoreCase) >= 0
              || name.IndexOf(LegacyCommandName, StringComparison.OrdinalIgnoreCase) >= 0
              || tip.IndexOf(CommandName, StringComparison.OrdinalIgnoreCase) >= 0
              || tip.IndexOf(ButtonText, StringComparison.OrdinalIgnoreCase) >= 0)
          {
            lock (Gate)
            {
              if (!OriginalBackgrounds.ContainsKey(btn))
              {
                OriginalBackgrounds[btn] = btn.Background;
              }

              if (!OriginalTexts.ContainsKey(btn))
              {
                OriginalTexts[btn] = btn.Content;
              }

              btn.Background = pending ? _redBrush : OriginalBackgrounds[btn];
              if (OriginalTexts[btn] is string s)
              {
                btn.Content = pending ? ("● " + s.TrimStart('●', ' ').TrimStart()) : s;
              }
            }

            count++;
          }
        }
      }
      catch
      {
      }

      try
      {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
          count += WalkVisual(VisualTreeHelper.GetChild(root, i), pending);
        }
      }
      catch
      {
      }

      return count;
    }

    private static string ReadStringProp(object node, string name)
    {
      try
      {
        PropertyInfo p = node.GetType().GetProperty(name);
        return p?.GetValue(node)?.ToString();
      }
      catch
      {
        return null;
      }
    }
  }
}
