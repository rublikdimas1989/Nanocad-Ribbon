# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.27.zip`](nanocad-test-ribbon-v1.2.27.zip)  
  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.27/nanocad-test-ribbon-v1.2.27.zip

## v1.2.27 — WinForms DataGridView for KP rows (TCON-45)

- Pad window is **WinForms** with `DataGridView` + `BindingList` (WPF DataGrid stayed headers-only after load)
- Status/log reports `pads=` / `grid.Rows=` after each bind
- Still modal `ShowDialog`; place/rotate uses Opacity=0 (not Hide)

## v1.2.26 — grid ItemsSource rebind after load (TCON-45)

- After load / place / rotate / Visibility restore: always reassign `ItemsSource` to a fresh `ObservableCollection` copy (no `Items.Refresh`)
- Disables DataGrid row virtualization (Collapsed→Visible headers-only bug)
- Ensures 6 KP rows show when log says «Загружено КП: 6»

## v1.2.25 — grid display-only + safe refresh (TCON-45)

- Entire KP grid **read-only** (X/Y/угол too) — position only via place/rotate on drawing
- Fixes empty grid: cancel EditItem before Refresh; safe rebind after Visibility restore
