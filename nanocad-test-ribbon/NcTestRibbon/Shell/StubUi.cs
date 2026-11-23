namespace NcTestRibbon.Shell
{
  using System.Windows;

  internal static class StubUi
  {
    public static void Notify(DependencyObject owner, string action)
    {
      Window win = Window.GetWindow(owner);
      System.Windows.MessageBox.Show(
        win,
        "Действие: " + action + "\n\nЛогика расчёта будет на сервере. Сейчас только UI-оболочка.",
        "NcTestRibbon UI",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
    }
  }
}
