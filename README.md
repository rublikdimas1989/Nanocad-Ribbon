# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.9.zip`](nanocad-test-ribbon-v1.2.9.zip)  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.2.9.zip
- Исходники: [`nanocad-test-ribbon/`](nanocad-test-ribbon/)

Распаковывайте zip в **новую** пустую папку (не поверх старой).

Подробности: [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).

## Что внутри (v1.2.9)

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

## Local rates (v1.2.9)

Tab «Настройки трассировки» loads rates from `%LocalAppData%\ASDC\LoadFile\NANOCAD\Tracing_cache\Settings\Price.xlsx` (EPPlus) and saves medium transitions into `tracing.conf` (JSON AppSettings). Place the End Price.xlsx workbook there.

**v1.2.9:** `installer\deps\` ships `EPPlus.dll` (8.6.3) + transitive deps; `INSTALL.bat` copies them next to `NC_TestRibbon.dll` in LoadFile. Unpack zip into a **NEW** empty folder, rebuild Release, run INSTALL, then `NcTestSettings`.
