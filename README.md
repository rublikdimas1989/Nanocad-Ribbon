# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.25.zip`](nanocad-test-ribbon-v1.2.25.zip)  
  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.25/nanocad-test-ribbon-v1.2.25.zip

## v1.2.25 — grid display-only + safe refresh (TCON-45)

- Entire KP grid **read-only** (X/Y/угол too) — position only via place/rotate on drawing
- Fixes empty grid: cancel EditItem before Refresh; safe rebind after Visibility restore
