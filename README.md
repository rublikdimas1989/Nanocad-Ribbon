# Nanocad-Ribbon

Тестовый пакет плагина ленты для **nanoCAD 24** (`.NET 6`, Windows).

## Скачать

- **Актуальный пакет:** [`nanocad-test-ribbon-v1.2.24.zip`](nanocad-test-ribbon-v1.2.24.zip)  
  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.24/nanocad-test-ribbon-v1.2.24.zip

## v1.2.24 — crash harden (WPF + zoom)

- Place/rotate: no `Hide()`/`Show()` on modal WPF (use `Visibility.Collapsed`) — avoids Qt↔WPF re-entry crash
- Zoom: no `ViewTableRecord.Dispose` after `SetCurrentView`; swallow all zoom errors
- Crash log: `%LocalAppData%\ASDC\LoadFile\NANOCAD\Tracing_cache\Settings\nc_test_ribbon_crash.log`
