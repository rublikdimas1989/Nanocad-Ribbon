//
// Точка входа тестового плагина. Панель «Тест» на вкладке «Инструменты ЦРПР»
// описывается манифестом NC_TestRibbon.json и генерируется NcBootstrap —
// инструмент профиль самостоятельно не меняет.
//

namespace NcTestRibbon
{
  using Teigha.Runtime;

  public class Plugin : IExtensionApplication
  {
    public void Initialize()
    {
    }

    public void Terminate()
    {
    }
  }
}
