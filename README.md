# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.48.zip`](nanocad-test-ribbon-v1.2.48.zip)  
  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.48/nanocad-test-ribbon-v1.2.48.zip
- **Samples:** `Topology_Inserts.xlsx`, `Topology_Buos.xlsx`, `Topology_Nodes.xlsx`

## v1.2.48 — out-of-process NcGdalGeo (TCON-62)

- Fix `DllNotFoundException` / `0x8007007F` on `gdal_wrap` (SWIG vs process `gdal.dll` mismatch inside nanoCAD)
- Matched NuGet set **GDAL / GDAL.Native / GDAL.Plugins 3.12.1** (same wrap+gdal+ECW versions, End-style)
- Bundled `installer/tools/NcGdalGeo` (net48 x64) → INSTALL to LoadFile `tools\NcGdalGeo`; Resolve prefers it before in-process OSGeo

## v1.2.47 — GDAL preload + version stamp (TCON-62)

- LoadLibraryEx `gdal.dll`/`gdal_wrap`/`NCSEcw`/`gdal_ECW_JP2ECW` before any managed `Gdal.*`
- Errors/command line show `NcTestRibbon 1.2.47+…` so stale DLLs are obvious
- Open diagnostics: ECW driver, plugin exists, GT array, LastError, bootstrap dump

## v1.2.46 — GDAL path + Open diagnostics (TCON-62)

- Probe `%LOCALAPPDATA%\ASDC\LoadFile\NANOCAD\gdal` first; INSTALL hard-fails if wrap/ECW/csharp missing
- NcTestEcw error shows GDAL status (candidates, ECW driver, Gdal.Open / LastError)

## v1.2.45 — embedded ECW GeoTransform via GDAL (TCON-62)

- End-style: `GdalRuntimeBootstrap` + `Gdal.Open` → `GetGeoTransform` (ECW plugin `gdal_ECW_JP2ECW` / `NCSEcw`)
- Bundled `installer/gdal/x64` (+ plugins) copied to LoadFile by INSTALL; no PATH gdalinfo required

## v1.2.44 — PathTextDisplay FlowDirection (TCON-61)

- Qualify `System.Windows.FlowDirection` / `TextAlignment` for WPF path helpers (CS0104 vs Forms)

## v1.2.43 — PathTextDisplay Control ambiguity (TCON-60)

- WinForms helpers use `System.Windows.Forms.Control` only (no bare `Control` / WPF Controls import)

## v1.2.42 — ECW End-style georef attach

- `NcTestEcw` / `NcTestRaster`: insert ECW/raster with world coords + scale (RasterImage Orientation from GDAL/world), not plain IMAGEATTACH scale=1
- Resolves geo via world/TAF sidecar or `gdalinfo -json`; writes `.eww`/world sidecar; ribbon «ECW/растр»

## v1.2.41 — path TextBox scroll to filename

- LTR paths; after set Text: SelectionStart=Length + ScrollToCaret (and on Load/Shown) so filename stays visible

## v1.2.40 — right-align path fields

- Path TextBoxes in Исходные данные + settings: WinForms `TextAlign=Right`, WPF `TextAlignment=Right`

## v1.2.39 — CS0104 IWin32Window

- Qualify `System.Windows.Forms.IWin32Window` in pad/insert close prompts (vs Interop)

## v1.2.38 — portable paths.json

- Store paths as relative under Tracing_cache or `%LOCALAPPDATA%\…` (no username); expand on load; migrate absolute LocalAppData on save/load

## v1.2.37 — close prompt for yellow coords

- Closing «Исходные данные» with unsaved pad/insert coords asks «Применить новые координаты?» (Yes=save Excel, No=revert)

## v1.2.36 — ribbon: only «Исходные данные»

- Removed ribbon buttons Кустовые / Врезки / БУОС / Узлы; keep single «Исходные данные»
- `NcTestPadOpt` / `NcTestInserts` / `NcTestBuos` / `NcTestNodes` remain as typed commands

## v1.2.35 — fix Activate CS0103 (TCON-53)

- Pad/Inserts CAD focus restore uses host `Form.Activate` after UserControl hosting

## v1.2.34 — Исходные данные tabs + Узлы

- One WinForms window «Исходные данные» (TabControl): Кустовые / Врезки / БУОС / Узлы
- Ribbon buttons open the same window on the matching tab
- `Topology_Nodes.xlsx` — «Узел 3/4 соединения», sizes only; cost from Price like BUOS

## v1.2.33 — BUOS cost from Price.xlsx (TCON-51)

- Dropped UseInTopology / UseExcelPrice / CostRub from BUOS xlsx+UI
- Cost = Price.xlsx «Элементы топологии» A→B by name; missing → red «введите в Price стоимость»

## v1.2.32 — Варианты БУОС (catalog)

- `NcTestBuos` — End TopologyBuosType catalogue (Excel «БУОС»), no site coordinates
- Sample `Topology_Buos.xlsx`; path `paths.json` → `buosWorkbook`

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
