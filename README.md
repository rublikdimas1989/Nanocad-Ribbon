# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.10.zip`](nanocad-test-ribbon-v1.2.10.zip)  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.2.10.zip
- Исходники: [`nanocad-test-ribbon/`](nanocad-test-ribbon/)

Распаковывайте zip в **новую** пустую папку (не поверх старой).

Подробности: [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).

## Что внутри (v1.2.10)

UI-оболочки из End-варианта (WPF), **без** Topology/PipeCalc/PathFinding:

| Команда | Окно |
|---------|------|
| `NcTestWpf` | `MainWindow` — «Трассировка трубопровода» |
| `NcTestSettings` | «Настройки» (трассировка + траншея/дорога) |
| `NcTestInitialData` | alias → `NcTestSettings` |
| `NcTestPadOpt` | «Оптимизация кустовых площадок» (TopologyInputs/Pad) |
| `NcTestHello` / `NcTestHttp` | прежние тесты |
| `NcTestDemo` | маленький WinForms smoke |

Кнопки ленты: Hello / HTTP GET / **Трассировка** / **Настройки** / **Кустовые площадки**. Клики в UI → MessageBox «сервер позже».

## Быстрый старт

`NcSdkDir` уже в `Directory.Build.props` (путь RibbonExecDoc / YandexDisk).

```powershell
cd nanocad-test-ribbon\NcTestRibbon
dotnet build NcTestRibbon.csproj -c Release
cd ..\installer
.\INSTALL.bat
.\VERIFY.bat
```

nanoCAD: `NcTestSettings` / `NcTestWpf` / `NcTestPadOpt`.

## Local rates (v1.2.10)

Tab «Настройки трассировки» loads rates from `%LocalAppData%\ASDC\LoadFile\NANOCAD\Tracing_cache\Settings\` — prefers **Price.xlsx**, else **Price.xlsm** (EPPlus; macros ignored). Saves medium transitions into `tracing.conf`.

**v1.2.9+:** `installer\deps\` ships EPPlus 8.6.3; INSTALL copies deps next to the plugin. Unpack into a **NEW** empty folder, rebuild Release, run INSTALL, then `NcTestSettings`.
