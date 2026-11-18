# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.0.zip`](nanocad-test-ribbon-v1.2.0.zip)  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.2.0.zip
- Исходники: [`nanocad-test-ribbon/`](nanocad-test-ribbon/)

Подробности: [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).

## Что внутри (v1.2.0)

UI-оболочки из End-варианта (WPF), **без** Topology/PipeCalc/PathFinding:

| Команда | Окно |
|---------|------|
| `NcTestWpf` | `MainWindow` — «Трассировка трубопровода» |
| `NcTestInitialData` | `InitialData` — «Исходные данные» |
| `NcTestHello` / `NcTestHttp` | прежние тесты |
| `NcTestDemo` | маленький WinForms smoke |

Кнопки ленты: Hello / HTTP GET / **Трассировка** / **Исходные**. Клики в UI → MessageBox «сервер позже».

## Быстрый старт

`NcSdkDir` уже в `Directory.Build.props` (путь RibbonExecDoc / YandexDisk).

```powershell
cd nanocad-test-ribbon\NcTestRibbon
dotnet build NcTestRibbon.csproj -c Release
cd ..\installer
.\INSTALL.bat
.\VERIFY.bat
```

nanoCAD: `NcTestWpf` или `NcTestInitialData`.
