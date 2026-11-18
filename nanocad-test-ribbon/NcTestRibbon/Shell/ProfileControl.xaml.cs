namespace NcTestRibbon.Shell
{
  using System.Windows;

  public partial class ProfileControl : System.Windows.Controls.UserControl
  {
    public ProfileControl()
    {
      InitializeComponent();

    }

private void BtnResetProfileZoom_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "ProfileControl.BtnResetProfileZoom_Click");
    }

private void ProfileCanvas_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
      StubUi.Notify(this, "ProfileControl.ProfileCanvas_PreviewMouseWheel");
    }

private void ProfileCanvas_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
      StubUi.Notify(this, "ProfileControl.ProfileCanvas_SizeChanged");
    }
  }
}
