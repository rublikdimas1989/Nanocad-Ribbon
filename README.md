# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.13.zip`](nanocad-test-ribbon-v1.2.13.zip)  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.2.13.zip
- Исходники: [`nanocad-test-ribbon/`](nanocad-test-ribbon/)

Распаковывайте zip в **новую** пустую папку (не поверх старой).

Подробности: [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).

## Что внутри (v1.2.13)

UI-оболочки из End-варианта (WPF), **без** Topology/PipeCalc/PathFinding + CAD blink:

| Команда | Окно / действие |
|---------|-----------------|
| `NcTestWpf` | `MainWindow` — «Трассировка трубопровода» |
| `NcTestSettings` | «Настройки» (трассировка + траншея/дорога) |
| `NcTestInitialData` | alias → `NcTestSettings` |
| `NcTestPadOpt` | «Оптимизация кустовых площадок» (TopologyInputs/Pad) |
| `NcTestBlink` | **Toggle** мигания выбранной полилинии (Highlight 200 мс, как End) |
| `NcTestBlinkOff` | выключить мигание |
| `NcTestHello` / `NcTestHttp` | прежние тесты |
| `NcTestDemo` | маленький WinForms smoke |

Кнопки ленты: Hello / HTTP GET / **Трассировка** / **Настройки** / **Кустовые площадки** / **Мигание**.

### Мигание

1. Выделите полилинию в чертеже.  
2. Нажмите **Мигание** (или `NcTestBlink`).  
3. Снова нажмите ту же кнопку — **отжать** / стоп.

## Быстрый старт

`NcSdkDir` уже в `Directory.Build.props` (путь RibbonExecDoc / YandexDisk).

```powershell
cd nanocad-test-ribbon\NcTestRibbon
dotnet build NcTestRibbon.csproj -c Release
cd ..\installer
.\INSTALL.bat
.\VERIFY.bat
```

nanoCAD: `NcTestBlink` / `NcTestSettings` / `NcTestWpf` / `NcTestPadOpt`.

## Local rates + pads (v1.2.12+)

- Rates: `Price.xlsx` / `Price.xlsm`; remembered in `Tracing_cache\Settings\paths.json`.
- Pad window (`NcTestPadOpt`): open KP/Top_tree **xlsx** tables; last path in `paths.json`; pad **Ширина/Длина** (default 150×250).
- Trench preview matches End scheme drawing.

Sample workbooks: [Burskoe_6](https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/burskoe-6-kp/Burskoe_6_KP.xlsx), [Burskoe_10](https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/burskoe-10-kp/Burskoe_10_KP.xlsx), [Top_tree_config](https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/top-tree-config/Top_tree_config.xlsx).
