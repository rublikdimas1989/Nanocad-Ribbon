namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.ObjectModel;
  using System.Globalization;
  using System.Linq;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Data;
  using System.Windows.Media;
  using NcTestRibbon.Rates;

  /// <summary>
  /// Display-only End-style KP detail: monthly profiles from Excel + «Ограничивает в месяце».
  /// </summary>
  public sealed class TopologyPadDetailWindow : Window
  {
    public TopologyPadDetailWindow(
      PadWorkbookLoader.PadRecord pad,
      PadWorkbookLoader.WorkbookData book,
      PadLoadPreview.Result load)
    {
      if (pad == null)
      {
        throw new ArgumentNullException(nameof(pad));
      }

      Title = pad.Name + " — месячные профили и загрузка БУОС";
      Width = 1180;
      Height = 780;
      MinWidth = 900;
      MinHeight = 560;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;

      var root = new DockPanel { Margin = new Thickness(12) };
      Content = root;

      var close = new Button
      {
        Content = "Закрыть",
        IsCancel = true,
        IsDefault = true,
        Margin = new Thickness(5),
        Padding = new Thickness(16, 6, 16, 6),
        HorizontalAlignment = HorizontalAlignment.Right
      };
      DockPanel.SetDock(close, Dock.Bottom);
      root.Children.Add(close);

      var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
      DockPanel.SetDock(header, Dock.Top);
      root.Children.Add(header);

      header.Children.Add(new TextBlock
      {
        Text = pad.Name,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 4)
      });
      header.Children.Add(new TextBlock
      {
        Text = string.Format(
          CultureInfo.InvariantCulture,
          "X={0:G17}; Y={1:G17}; {2:0.###}×{3:0.###} м; P={4:0.###} бар; separation={5}",
          pad.X, pad.Y, pad.WidthM, pad.LengthM, pad.PressureBar, pad.SeparationMode),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 2)
      });
      header.Children.Add(new TextBlock
      {
        Text = "Активны: "
          + (pad.ActiveLiquid ? "жидкость " : string.Empty)
          + (pad.ActiveOil ? "нефть " : string.Empty)
          + (pad.ActiveGas ? "ПНГ" : string.Empty),
        Margin = new Thickness(0, 0, 0, 2)
      });

      string units = book?.UnitsNote;
      if (string.IsNullOrWhiteSpace(units))
      {
        units = string.Format(
          CultureInfo.InvariantCulture,
          "oil={0}; liquid={1}; gas={2}",
          book?.OilUnit, book?.LiquidUnit, book?.GasUnit);
      }

      header.Children.Add(new TextBlock
      {
        Text = "Единицы: " + units,
        Foreground = Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap
      });

      double combined = load?.CombinedCapacity ?? PadLoadPreview.DefaultCombinedCapacity;
      double gasCap = load?.GasCapacity ?? PadLoadPreview.DefaultGasCapacity;
      string buosLine = string.Format(
        CultureInfo.InvariantCulture,
        "БУОС (один): жидкость/нефть ≤ {0:N0} м³/сут; газ ≤ {1:N0} м³/сут",
        combined, gasCap);
      buosLine += book?.CapacitiesFromBuosSheet == true ? " (из листа БУОС)" : " (по умолчанию End)";
      if (!string.IsNullOrWhiteSpace(load?.DensityNote))
      {
        buosLine += "; " + load.DensityNote;
      }

      header.Children.Add(new TextBlock
      {
        Text = buosLine,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0)
      });

      header.Children.Add(new TextBlock
      {
        Text = "Только просмотр. Правки — в Excel (листы «КП» / «Профили»).",
        Foreground = Brushes.DarkSlateGray,
        Margin = new Thickness(0, 6, 0, 0)
      });

      var rows = new ObservableCollection<MonthRow>();
      if (load?.Months != null)
      {
        foreach (PadLoadPreview.MonthLoad month in load.Months)
        {
          rows.Add(new MonthRow
          {
            Month = month.Month.ToString(CultureInfo.InvariantCulture),
            Liquid = Format(month.LiquidDisplay),
            Oil = Format(month.OilDisplay),
            Gas = Format(month.GasDisplay),
            OilVolume = Format(month.OilVolumeM3Day),
            WaterCut = month.WaterCutPercent.HasValue
              ? month.WaterCutPercent.Value.ToString("0.##", CultureInfo.CurrentCulture)
              : "—",
            Status = month.Status,
            PercentMax = month.Percent.DefaultIfEmpty(0).Max()
          });
        }
      }

      string oilHeader = book?.OilIsTonnesPerDay == true ? "Нефть\nт/сут" : "Нефть\nм³/сут";
      string liquidHeader = book?.OilIsTonnesPerDay == true ? "Жидкость (сумма)\nм³/сут" : "Жидкость\nм³/сут";

      var grid = new DataGrid
      {
        ItemsSource = rows,
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Single,
        FrozenColumnCount = 1,
        RowHeaderWidth = 0
      };
      grid.Columns.Add(new DataGridTextColumn
      {
        Header = "Месяц",
        Binding = new Binding(nameof(MonthRow.Month)),
        Width = 65
      });
      grid.Columns.Add(Text(liquidHeader, nameof(MonthRow.Liquid)));
      grid.Columns.Add(Text(oilHeader, nameof(MonthRow.Oil)));
      grid.Columns.Add(Text("ПНГ\nм³/сут", nameof(MonthRow.Gas)));
      if (book?.OilIsTonnesPerDay == true)
      {
        grid.Columns.Add(Text("Нефть\nм³/сут", nameof(MonthRow.OilVolume), 90));
        grid.Columns.Add(Text("Обводн.\n%", nameof(MonthRow.WaterCut), 80));
      }

      grid.Columns.Add(new DataGridTextColumn
      {
        Header = "Ограничивает в месяце",
        Binding = new Binding(nameof(MonthRow.Status)),
        Width = new DataGridLength(1.6, DataGridLengthUnitType.Star),
        MinWidth = 180
      });
      root.Children.Add(grid);

      if (rows.Count == 0)
      {
        header.Children.Add(new TextBlock
        {
          Text = "В книге нет листа «Профили» (или нет столбцов для этой КП).",
          Foreground = Brushes.Firebrick,
          Margin = new Thickness(0, 8, 0, 0)
        });
      }
    }

    private static DataGridTextColumn Text(string header, string path, double? width = null)
    {
      return new DataGridTextColumn
      {
        Header = header,
        Binding = new Binding(path),
        Width = width.HasValue
          ? new DataGridLength(width.Value)
          : new DataGridLength(1, DataGridLengthUnitType.Star),
        MinWidth = 90
      };
    }

    private static string Format(double value)
    {
      return value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    public sealed class MonthRow
    {
      public string Month { get; set; } = string.Empty;
      public string Liquid { get; set; } = "0";
      public string Oil { get; set; } = "0";
      public string Gas { get; set; } = "0";
      public string OilVolume { get; set; } = "0";
      public string WaterCut { get; set; } = "—";
      public string Status { get; set; } = "—";
      public double PercentMax { get; set; }
    }
  }
}
