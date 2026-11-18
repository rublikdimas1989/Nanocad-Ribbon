# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows): две команды (`NcTestHello`, `NcTestHttp`) и манифест панели «Тест». Сервер ASDC для локальной проверки **не нужен**.

## Содержимое репозитория

- [`nanocad-test-ribbon.zip`](nanocad-test-ribbon.zip) — архив пакета
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

## Локальный деплой без ASDC

Закройте nanoCAD, затем из `nanocad-test-ribbon`:

```powershell
.\dev-deploy.ps1
# или с автозагрузкой в Cfg.ini:
.\dev-deploy.ps1 -RegisterStartup
```

Файлы копируются в `%LOCALAPPDATA%\ASDC\LoadFile\NANOCAD\`. После перезапуска nanoCAD: `NETLOAD` DLL (если не зарегистрировали Startup) и команды `NcTestHello` / `NcTestHttp`.
