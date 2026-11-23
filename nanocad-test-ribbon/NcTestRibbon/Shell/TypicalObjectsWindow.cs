namespace NcTestRibbon.Shell
{
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Forms.Integration;

  /// <summary>
  /// Settings flyout «Типовые объекты»: catalog tabs БУОС + Узлы
  /// (same panels as full SettingsWindow — more flyout entries can open sibling shells later).
  /// </summary>
  public sealed class TypicalObjectsWindow : Window
  {
    private readonly BuosVariantsWindow _buosCatalog = new BuosVariantsWindow();
    private readonly NodeVariantsWindow _nodesCatalog = new NodeVariantsWindow();

    public TypicalObjectsWindow()
    {
      Title = "Типовые объекты";
      Height = 900;
      Width = 1100;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      ResizeMode = ResizeMode.CanResize;

      TracingCache.EnsureCreated();

      var root = new DockPanel { Margin = new Thickness(10) };
      Content = root;

      var buttons = new StackPanel
      {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(0, 10, 0, 0)
      };
      DockPanel.SetDock(buttons, Dock.Bottom);
      root.Children.Add(buttons);
      buttons.Children.Add(new Button
      {
        Content = "Закрыть",
        Padding = new Thickness(22, 7, 22, 7),
        IsCancel = true
      });

      var tabs = new TabControl();
      root.Children.Add(tabs);
      tabs.Items.Add(new TabItem
      {
        Header = "БУОС",
        Content = new WindowsFormsHost { Child = _buosCatalog }
      });
      tabs.Items.Add(new TabItem
      {
        Header = "Узлы",
        Content = new WindowsFormsHost { Child = _nodesCatalog }
      });
    }
  }
}
