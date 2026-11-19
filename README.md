# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.18.zip`](nanocad-test-ribbon-v1.2.18.zip)  
  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.18/nanocad-test-ribbon-v1.2.18.zip  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.2.18.zip
- Исходники: [`nanocad-test-ribbon/`](nanocad-test-ribbon/)

Распаковывайте zip в **новую** пустую папку.

## v1.2.18 — KP overlay fix

- Locked layer `NC_KP_PADS`
- Угол 0…360 + **Повернуть**
- Повтор «Показывать на чертеже» — только refresh (без дублей)
- Исправлено: объекты реально появляются (model space + unlock/lock + regen)
- 0°: **Длина → X** (широкая), **Ширина → Y** (узкая)

Подробности: [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).
