namespace NcTestRibbon.Shell
{
  using System;
  using System.IO;
  using System.Linq;
  using System.Reflection;
  using System.Windows;
  using System.Windows.Markup;

  /// <summary>
  /// Load shell XAML from embedded resources — avoids CAD NETLOAD pack/BAML URI failures
  /// like /NC_TestRibbon;V1.0.0.0;component/shell/settingswindow.xaml.
  /// </summary>
  internal static class ShellXaml
  {
    public const string TracingSettingsResource = "NcTestRibbon.Shell.Xaml.TracingSettingsPanel.xaml";

    public static FrameworkElement Load(string resourceName)
    {
      Assembly asm = typeof(ShellXaml).Assembly;
      Stream stream = asm.GetManifestResourceStream(resourceName);
      if (stream == null)
      {
        string available = string.Join(", ", asm.GetManifestResourceNames().OrderBy(x => x));
        throw new InvalidOperationException(
          "Embedded XAML not found: " + resourceName
          + ". Available: " + available);
      }

      using (stream)
      {
        object loaded = XamlReader.Load(stream);
        if (loaded is not FrameworkElement element)
        {
          throw new InvalidOperationException(
            "Embedded XAML root is not FrameworkElement: " + resourceName);
        }

        return element;
      }
    }

    public static T Find<T>(FrameworkElement root, string name) where T : class
    {
      return root.FindName(name) as T;
    }
  }
}
