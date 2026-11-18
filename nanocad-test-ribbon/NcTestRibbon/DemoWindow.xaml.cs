namespace NcTestRibbon
{
  using System.Windows;

  public partial class DemoWindow : Window
  {
    public DemoWindow()
    {
      InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
      Close();
    }
  }
}
