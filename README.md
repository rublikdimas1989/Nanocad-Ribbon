# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows): три команды (`NcTestHello`, `NcTestHttp`, `NcTestWpf`) и манифест панели «Тест». Сервер ASDC для локальной проверки **не нужен**.

## Содержимое репозитория

- [`nanocad-test-ribbon.zip`](nanocad-test-ribbon.zip) — архив пакета (включая `installer/`)
- [`nanocad-test-ribbon/`](nanocad-test-ribbon/) — исходники VS-проекта (то же содержимое)

Подробности — в [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).

## Открыть решение

1. Установите Visual Studio 2022 (.NET Desktop) и **nanoCAD SDK 24**.
2. Откройте `nanocad-test-ribbon/NcTestRibbon/NcTestRibbon.sln`.

По умолчанию путь к SDK в `.csproj`: `C:\NC_SDK_RU_24.0\` (свой путь — через `NcSdkDir`).

## Сборка

```powershell
cd nanocad-test-ribbon\NcTestRibbon
dotnet build NcTestRibbon.csproj -c Release
```

DLL: `bin\Release\net6.0-windows\NC_TestRibbon.dll`.

## Установка (после сборки или после переустановки nanoCAD)

Закройте nanoCAD. Если nanoCAD только что переустановили — один раз запустите и закройте его (создаётся `Cfg.ini`), затем:

```text
nanocad-test-ribbon\installer\INSTALL.bat
```

Установщик ставит **NcBootstrap** + копирует **только** `NC_TestRibbon` в LoadFile и прописывает Startup, затем **дважды запускает nanoCAD** (1-й — прогрев ленты и закрытие, 2-й — оставляет открытым). Панель «Рамки» / `NC_RibbonExecDoc` **не** устанавливается (остатки счищаются; `serverUrl` в bootstrap пустой — без ASDC).

Удаление: `nanocad-test-ribbon\installer\UNINSTALL.bat`.

Быстрый деплой без bootstrap: `nanocad-test-ribbon\dev-deploy.ps1` (опция `-RegisterStartup`).

После запуска nanoCAD: команды `NcTestHello` / `NcTestHttp` / `NcTestWpf` (WinForms-окно через `Form.Show()` — modeless; лента «Окно»). WPF не используется.
