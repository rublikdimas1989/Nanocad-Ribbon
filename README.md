# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.31.zip`](nanocad-test-ribbon-v1.2.31.zip)  
  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.31/nanocad-test-ribbon-v1.2.31.zip
- **Sample inserts xlsx:** [`Topology_Inserts.xlsx`](Topology_Inserts.xlsx)  
  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.31/Topology_Inserts.xlsx

## v1.2.31 — inserts unique «Имя» + oil т/сут (TCON-49)

- Dropped Excel `Id` column; identity = unique «Имя»; duplicates → load error
- Max oil header/UI: **т/сут** (not м³/сут); sample updated

## v1.2.30 — zoom before insert place

- `NcTestInserts` «Задать положение вручную» zooms to the insert (or planned X/Y) before GetPoint — same rule as KP pads/rotate

## v1.2.29 — Topology inserts (врезки) + interface.conf

- `NcTestInserts` — End «Топология — точки врезки» WinForms grid (Excel «Врезки»), CAD place, show checkbox
- `NcTestInterface` — font size + insert circle diameter → `Tracing_cache\Settings\interface.conf` (also KP labels)
- Sample `Topology_Inserts.xlsx`; path in `paths.json` (`insertsWorkbook`)

## v1.2.28 — pad session: stay open, persist show, revert unsaved

- Modeless pad window — stays open after place/rotate; grid shows new X/Y/angle immediately
- «Показывать на чертеже» persisted in `paths.json`
- Without «Сохранить положения», close discards preview and restores CAD to last Excel poses

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
