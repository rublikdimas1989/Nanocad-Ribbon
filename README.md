# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows): три команды (`NcTestHello`, `NcTestHttp`, `NcTestWpf`) и манифест панели «Тест». Сервер ASDC для локальной проверки **не нужен**.

## Содержимое репозитория

- **Скачать пакет (актуальный, без кэша старого имени):** [`nanocad-test-ribbon-v1.1.0.zip`](nanocad-test-ribbon-v1.1.0.zip)  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.1.0.zip
- [`nanocad-test-ribbon/`](nanocad-test-ribbon/) — исходники VS-проекта (то же содержимое)
- [`nanocad-test-ribbon.zip`](nanocad-test-ribbon.zip) — копия того же архива (старое имя; браузер может кэшировать)

Подробности — в [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).

**После скачивания v1.1.0:** распаковать → **обязательно** `dotnet build ... -c Release` → `installer\INSTALL.bat` → `installer\VERIFY.bat` → в nanoCAD команды `NcTestHello` / `NcTestHttp` / `NcTestWpf`.

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

Установщик ставит **NcBootstrap** + **перезаписывает** `NC_TestRibbon` в LoadFile и прописывает Startup (nanoCAD **не** запускается автоматически). Перед копированием проверяет, что DLL содержит `NcTestHello` / `NcTestHttp` / `NcTestWpf` (иначе отказ — нужна пересборка Release). Панель «Рамки» / `NC_RibbonExecDoc` **не** устанавливается.

Удаление: `nanocad-test-ribbon\installer\UNINSTALL.bat`.  
Проверка DLL: `nanocad-test-ribbon\installer\VERIFY.bat`.

Быстрый деплой без bootstrap: `nanocad-test-ribbon\dev-deploy.ps1` (опция `-RegisterStartup`).

После запуска nanoCAD: `NcTestHello` / `NcTestHttp` / `NcTestWpf` (лента «Окно»). Если `NcTestWpf` неизвестна — старая DLL; rebuild + INSTALL.
