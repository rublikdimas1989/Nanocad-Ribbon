# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows): три команды (`NcTestHello`, `NcTestHttp`, `NcTestWpf`) и манифест панели «Тест».

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.1.2.zip`](nanocad-test-ribbon-v1.1.2.zip)  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.1.2.zip
- Исходники: [`nanocad-test-ribbon/`](nanocad-test-ribbon/)
- Старые zip (`v1.1.1` и ранее) могут кэшироваться — берите **v1.1.2**

Подробности: [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).

## Быстрый старт (v1.1.2)

`NcSdkDir` уже прописан в `Directory.Build.props` и в `.csproj` (путь как в RibbonExecDoc):

`C:\Users\bublik_da\YandexDisk\Автоматизация проектирования\Проекты\Паспотизация ИЗП\Инструменты nanocad\NC_SDK_RU_24.0.6388.4326.7145\`

1. Распаковать **v1.1.2** в новую папку. Если есть `DemoWindow.xaml` — удалить.
2. Открыть `nanocad-test-ribbon\NcTestRibbon\NcTestRibbon.sln` в VS **или**:

```powershell
cd nanocad-test-ribbon\NcTestRibbon
dotnet build NcTestRibbon.csproj -c Release
cd ..\installer
.\INSTALL.bat
.\VERIFY.bat
```

3. nanoCAD: `NcTestHello` / `NcTestHttp` / `NcTestWpf`

Если CS0246 HostMgd — проверьте, что SDK реально лежит по пути выше (или поправьте `Directory.Build.props`).
