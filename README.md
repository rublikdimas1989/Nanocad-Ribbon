# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows): три команды (`NcTestHello`, `NcTestHttp`, `NcTestWpf`) и манифест панели «Тест». Сервер ASDC для локальной проверки **не нужен**.

## Содержимое репозитория

- **Скачать пакет (актуальный):** [`nanocad-test-ribbon-v1.1.1.zip`](nanocad-test-ribbon-v1.1.1.zip)  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.1.1.zip
- [`nanocad-test-ribbon/`](nanocad-test-ribbon/) — исходники VS-проекта
- Старые имена zip (`v1.1.0`, `nanocad-test-ribbon.zip`) могут кэшироваться — берите **v1.1.1**

Подробности — в [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).

## Быстрый старт (после скачивания v1.1.1)

1. Распаковать в **новую** папку. Если видите `DemoWindow.xaml` — удалить (старый WPF; в v1.1.1 его нет).
2. Указать SDK (нужен `include-x64\hostmgd.dll`):

```powershell
cd nanocad-test-ribbon\NcTestRibbon
copy Directory.Build.props.example Directory.Build.props
notepad Directory.Build.props
```

В `NcSdkDir` — ваш путь, пробелы (YandexDisk) допустимы, например  
`C:\Users\bublik_da\YandexDisk\...\NC_SDK_RU_24.0.6388.4326.7145\`

3. Сборка и установка:

```powershell
dotnet build NcTestRibbon.csproj -c Release
cd ..\installer
.\INSTALL.bat
.\VERIFY.bat
```

4. nanoCAD: `NcTestHello` / `NcTestHttp` / `NcTestWpf`

Если Error List = **CS0246 HostMgd/Teigha** — неверный `NcSdkDir` (см. выше).  
Если Error List = **Window / RoutedEventArgs / DemoWindow** — удалите старые `DemoWindow.*` и откройте свежий v1.1.1.

## Открыть решение

1. Visual Studio 2022 (.NET Desktop) + **nanoCAD SDK 24**.
2. `nanocad-test-ribbon/NcTestRibbon/NcTestRibbon.sln`.

SDK: через `Directory.Build.props` или `-p:NcSdkDir="..."`.
