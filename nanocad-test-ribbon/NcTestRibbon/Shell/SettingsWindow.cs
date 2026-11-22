namespace NcTestRibbon.Shell
{
  using System;
  using System.IO;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Forms.Integration;

  /// <summary>
  /// Unified settings shell (code-built — no XAML pack URI).
  /// Tabs: tracing → catalog БУОС/Узлы → trench/road → network.
  /// </summary>
  public sealed class SettingsWindow : Window
  {
    private readonly TrenchRoadSettingsPanel _trenchPanel = new TrenchRoadSettingsPanel();
    private readonly TracingSettingsPanel _tracingPanel;
    private readonly NetworkSettingsPanel _networkPanel = new NetworkSettingsPanel();
    private readonly BuosVariantsWindow _buosCatalog = new BuosVariantsWindow();
    private readonly NodeVariantsWindow _nodesCatalog = new NodeVariantsWindow();
    private readonly TextBlock _cacheInfo = new TextBlock
    {
      Margin = new Thickness(0, 6, 0, 0),
      FontSize = 11,
      Foreground = System.Windows.Media.Brushes.Gray,
      TextWrapping = TextWrapping.NoWrap,
      TextAlignment = TextAlignment.Left,
      // Qualify: ImplicitUsings brings System.Windows.Forms (also has FlowDirection).
      FlowDirection = System.Windows.FlowDirection.LeftToRight
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
        + " + " + Path.GetFileName(TracingCache.TrenchRoadConf)
        + " + " + Path.GetFileName(TracingCache.NetConf);

      var tabs = new TabControl();
      root.Children.Add(tabs);
      tabs.Items.Add(new TabItem
      {
        Header = "Настройки трассировки",
        Content = _tracingPanel
      });
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
      tabs.Items.Add(new TabItem
      {
        Header = "Сетевые настройки",
        Content = new ScrollViewer
        {
          VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
          HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
          Content = _networkPanel
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
        _networkPanel.SaveToConfig();
        System.Windows.MessageBox.Show(
          this,
          "Сохранено:\n" + TracingCache.TracingConf + "\n" + TracingCache.TrenchRoadConf
          + "\n" + TracingCache.NetConf,
          "Настройки",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
        DialogResult = true;
      }
      catch (Exception ex)
      {
        System.Windows.MessageBox.Show(
          this,
          ex.Message,
          "Проверьте настройки",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      }
    }

  }
}
