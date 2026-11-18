namespace NcTestRibbon.Shell
{
  using System;
  using System.Globalization;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Data;

  public partial class RatesPanel : UserControl
  {
    public RatesPanel()
    {
      InitializeComponent();
    }

    private void ExportToExcel_Click(object sender, RoutedEventArgs e)
    {
      StubUi.Notify(this, "RatesPanel.ExportToExcel_Click");
    }

    private void CostDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
      // Stub: editing disabled for shell UI (server later).
      e.Cancel = true;
      StubUi.Notify(this, "RatesPanel.CostDataGrid_BeginningEdit");
    }
  }

  /// <summary>Stub converter so RatesPanel XAML loads without Cost/EPPlus logic.</summary>
  public sealed class NumberFormatConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      // Use System.Convert explicitly — Convert alone binds to this method (CS0119).
      return value == null ? string.Empty : System.Convert.ToString(value, culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      return value;
    }
  }
}
