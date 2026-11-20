namespace NcTestRibbon.Shell
{
  using System;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Media;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Net;

  /// <summary>
  /// Settings tab «Сетевые настройки»: editable host → net.conf; upload status label.
  /// </summary>
  public sealed class NetworkSettingsPanel : UserControl
  {
    private readonly TextBox _host;
    private readonly TextBlock _status;
    private readonly TextBlock _hint;

    public NetworkSettingsPanel()
    {
      NetConf conf = NetConf.Load();

      var root = new StackPanel { Margin = new Thickness(8) };
      Content = root;

      root.Children.Add(new TextBlock
      {
        Text = "Хост решателя (BaseAddress)",
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 6)
      });

      root.Children.Add(new TextBlock
      {
        Text = "Укажите host:port (по умолчанию localhost:5080). При запросах строится http://host:port/.",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
        Foreground = Brushes.DimGray
      });

      _host = new TextBox
      {
        Text = conf.Host ?? NetConf.DefaultHost,
        Padding = new Thickness(4),
        Margin = new Thickness(0, 0, 0, 8)
      };
      root.Children.Add(_host);

      _hint = new TextBlock
      {
        Text = "Файл: " + NetConf.FilePath + "\nBaseAddress → " + NetConf.BuildBaseAddress(conf.Host),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 12),
        Foreground = Brushes.Gray,
        FontSize = 11
      };
      root.Children.Add(_hint);

      _host.TextChanged += (_, __) =>
      {
        _hint.Text = "Файл: " + NetConf.FilePath
          + "\nBaseAddress → " + NetConf.BuildBaseAddress(_host.Text);
      };

      root.Children.Add(new TextBlock
      {
        Text = "Статус передачи карты",
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 6)
      });

      _status = new TextBlock
      {
        Text = string.IsNullOrEmpty(MapUploadClient.LastStatus)
          ? "—"
          : MapUploadClient.LastStatus,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
      };
      ApplyStatusColor(_status.Text);
      root.Children.Add(_status);

      MapUploadClient.StatusChanged += OnStatus;
      Unloaded += (_, __) => MapUploadClient.StatusChanged -= OnStatus;
    }

    public void SaveToConfig()
    {
      string host = (_host.Text ?? string.Empty).Trim();
      NetConf.Set(c => c.Host = host);
      NetConf reloaded = NetConf.Load();
      _host.Text = reloaded.Host;
      _hint.Text = "Файл: " + NetConf.FilePath
        + "\nBaseAddress → " + NetConf.BuildBaseAddress(reloaded.Host);
    }

    private void OnStatus(string message)
    {
      Dispatcher.BeginInvoke(new Action(() =>
      {
        _status.Text = string.IsNullOrEmpty(message) ? "—" : message;
        ApplyStatusColor(_status.Text);
      }));
    }

    private void ApplyStatusColor(string text)
    {
      if (text != null && text.StartsWith(MapUploadClient.StatusOk, StringComparison.Ordinal))
      {
        _status.Foreground = Brushes.DarkGreen;
      }
      else if (text != null && text.IndexOf(MapUploadClient.StatusFail, StringComparison.Ordinal) >= 0)
      {
        _status.Foreground = Brushes.DarkRed;
      }
      else
      {
        _status.Foreground = Brushes.DimGray;
      }
    }
  }
}
