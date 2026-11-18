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
  }

  /// <summary>Stub converter so RatesPanel XAML loads without Cost/EPPlus logic.</summary>
  public sealed class NumberFormatConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      return value == null ? string.Empty : Convert.ToString(value, culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      return value;
    }
  }
}
