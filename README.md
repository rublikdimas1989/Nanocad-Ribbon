# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.15.zip`](nanocad-test-ribbon-v1.2.15.zip)  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.2.15.zip  
  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.15/nanocad-test-ribbon-v1.2.15.zip
- Исходники: [`nanocad-test-ribbon/`](nanocad-test-ribbon/)

Распаковывайте zip в **новую** пустую папку (не поверх старой).

Подробности: [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).

## Что внутри (v1.2.15)

| Команда | Окно / действие |
|---------|-----------------|
| `NcTestWpf` | `MainWindow` — «Трассировка трубопровода» |
| `NcTestSettings` | «Настройки» (трассировка + траншея/дорога) |
| `NcTestPadOpt` | **Кустовые площадки** — просмотр КП; **двойной щелчок** → профили + «Ограничивает» |
| `NcTestBlink` | Toggle мигания выбранной полилинии |
| `NcTestBlinkOff` | выключить мигание |
| `NcTestHello` / `NcTestHttp` | прежние тесты |

### Кустовые площадки

- «Обзор…» → лист «КП» + «Профили» (+ ёмкости с «БУОС», если есть)
- Двойной щелчок по строке КП → месячные профили и колонка **Ограничивает в месяце** (как End TopologyPadLoadPreview)
- Правки — в Excel

## Быстрый старт

```powershell
cd nanocad-test-ribbon\NcTestRibbon
dotnet build NcTestRibbon.csproj -c Release
cd ..\installer
.\INSTALL.bat
.\VERIFY.bat
```
