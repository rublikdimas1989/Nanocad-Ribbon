# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.16.zip`](nanocad-test-ribbon-v1.2.16.zip)  
  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.16/nanocad-test-ribbon-v1.2.16.zip  
  Raw: https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.2.16.zip
- Исходники: [`nanocad-test-ribbon/`](nanocad-test-ribbon/)

Распаковывайте zip в **новую** пустую папку (не поверх старой).

## v1.2.16 — Кустовые на чертеже

`NcTestPadOpt`:

- Fix CS1503 (`FormattableString.Invariant`)
- **Показывать на чертеже** — прямоугольники ширина×длина, подпись сверху (высота = ½ ширины КП)
- Выбрать КП → **Задать положение вручную** → jig на чертеже → жёлтая строка → **Сохранить координаты** в Excel (лист «КП»)
- Двойной щелчок — профили + «Ограничивает в месяце»

Подробности: [`nanocad-test-ribbon/README.md`](nanocad-test-ribbon/README.md).
