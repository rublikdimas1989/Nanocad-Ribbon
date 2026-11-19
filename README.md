# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.14.zip`](nanocad-test-ribbon-v1.2.14.zip)  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.2.14.zip  
  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.14/nanocad-test-ribbon-v1.2.14.zip
- Исходники: [`nanocad-test-ribbon/`](nanocad-test-ribbon/)

Распаковывайте zip в **новую** пустую папку (не поверх старой).

Подробности: [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).

## Что внутри (v1.2.14)

| Команда | Окно / действие |
|---------|-----------------|
| `NcTestWpf` | `MainWindow` — «Трассировка трубопровода» |
| `NcTestSettings` | «Настройки» (трассировка + траншея/дорога) |
| `NcTestPadOpt` | **Кустовые площадки** — только просмотр КП из Excel |
| `NcTestBlink` | Toggle мигания выбранной полилинии |
| `NcTestBlinkOff` | выключить мигание |
| `NcTestHello` / `NcTestHttp` | прежние тесты |

### Кустовые площадки (display-only)

- «Обзор…» у Excel свойств → сразу «загрузка» (путь в `paths.json`)
- «Обзор…» у таблиц КП → сразу читает лист «КП» в сетку
- Правки — в Excel; БУОС / подложка / месяцы / кнопки расчёта убраны

## Быстрый старт

```powershell
cd nanocad-test-ribbon\NcTestRibbon
dotnet build NcTestRibbon.csproj -c Release
cd ..\installer
.\INSTALL.bat
.\VERIFY.bat
```
