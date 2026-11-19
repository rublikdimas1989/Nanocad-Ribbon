namespace NcTestRibbon.Shell
{
  using System;
  using System.IO;
  using System.Windows;
  using System.Windows.Controls;

  /// <summary>
  /// Unified settings shell (code-built — no XAML pack URI).
  /// Tabs: tracing settings + trench/road. Ensures Tracing_cache on open.
  /// </summary>
  public sealed class SettingsWindow : Window
  {
    private readonly TrenchRoadSettingsPanel _trenchPanel = new TrenchRoadSettingsPanel();
    private readonly TracingSettingsPanel _tracingPanel;
    private readonly TextBlock _cacheInfo = new TextBlock
    {
      Margin = new Thickness(0, 6, 0, 0),
      FontSize = 11,
      Foreground = System.Windows.Media.Brushes.Gray,
      TextWrapping = TextWrapping.Wrap,
      TextAlignment = TextAlignment.Right
    };

    public SettingsWindow()
    {
      Title = "Настройки";
      Height = 1100;
      Width = 1300;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      ResizeMode = ResizeMode.CanResize;

      TracingCache.EnsureCreated();
      _tracingPanel = new TracingSettingsPanel();

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

      var save = new Button
      {
        Content = "Сохранить",
        Padding = new Thickness(22, 7, 22, 7),
        IsDefault = true
      };
      save.Click += Save_Click;
      buttons.Children.Add(save);
      buttons.Children.Add(new Button
      {
        Content = "Отмена",
        Padding = new Thickness(22, 7, 22, 7),
        Margin = new Thickness(8, 0, 0, 0),
        IsCancel = true
      });

      DockPanel.SetDock(_cacheInfo, Dock.Bottom);
      root.Children.Add(_cacheInfo);
      _cacheInfo.Text =
        "Кэш: " + TracingCache.Root
        + "  |  расценки: " + TracingCache.ResolveRatesFilePath()
        + "  |  " + Path.GetFileName(TracingCache.TracingConf)
        + " + " + Path.GetFileName(TracingCache.TrenchRoadConf);

      var tabs = new TabControl();
      root.Children.Add(tabs);
      tabs.Items.Add(new TabItem
      {
        Header = "Настройки трассировки",
        Content = _tracingPanel
      });
      tabs.Items.Add(new TabItem
      {
        Header = "Траншея и дорога",
        Content = new ScrollViewer
        {
          VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
          HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
          Content = _trenchPanel
        }
      });
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        TracingCache.EnsureCreated();
        _tracingPanel.SaveToConfig();
        _trenchPanel.SaveStubConf();
        MessageBox.Show(
          this,
          "Сохранено:\n" + TracingCache.TracingConf + "\n" + TracingCache.TrenchRoadConf,
          "Настройки",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
        DialogResult = true;
      }
      catch (Exception ex)
      {
        MessageBox.Show(
          this,
          ex.Message,
          "Проверьте настройки",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      }
    }

  }
}
