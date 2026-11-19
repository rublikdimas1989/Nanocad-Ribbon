# NcTestRibbon — тестовая лента nanoCAD (локально, без ASDC)

Минимальный плагин **.NET 6 / Windows** для nanoCAD 24: три команды и манифест панели «Тест».
Сервер ASDC **не нужен** для локальной проверки команд. Раздача через ASDC — отдельный следующий шаг.

Актуальный zip с GitHub (версия **1.2.17**): `nanocad-test-ribbon-v1.2.17.zip`  
https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.2.17.zip  
Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/download/v1.2.17/nanocad-test-ribbon-v1.2.17.zip  





**`NcSdkDir` уже зашит** в `Directory.Build.props` и `.csproj` (UTF-8 BOM), тот же путь, что в RibbonExecDoc.

### Быстрая пересборка

1. Скачайте **v1.2.17**, распакуйте в **НОВУЮ** пустую папку (не поверх старой — иначе CS0101/CS0111).
2. `cd NcTestRibbon` → `dotnet build NcTestRibbon.csproj -c Release`
3. `..\installer\INSTALL.bat` → `..\installer\VERIFY.bat` (checks EPPlus.dll in LoadFile)
4. nanoCAD: `NcTestPadOpt` / `NcTestBlink` / `NcTestSettings` / `NcTestWpf` / `NcTestHello` / `NcTestHttp`

| Команда | Что делает |
|---------|------------|
| `NcTestHello` | Сообщение «Тест 1: Hello» |
| `NcTestHttp` | WinForms HTTP GET |
| `NcTestWpf` | **WPF-оболочка** `MainWindow` (End-variant UI; клики stub → MessageBox) |
| `NcTestSettings` | **WPF** «Настройки» (трассировка + траншея/дорога) |
| `NcTestInitialData` | alias → `NcTestSettings` |
| `NcTestPadOpt` | КП из Excel; чертёж / ручная расстановка / сохранение X/Y; двойной щелчок → профили |
| `NcTestBlink` | **Toggle**: выбрать полилинию → мигание (Highlight 200 мс, как End). Повтор / отжать → стоп |
| `NcTestBlinkOff` | принудительно выключить мигание |
| `NcTestDemo` | маленький WinForms smoke |

Сборка: `NC_TestRibbon.dll`  
Манифест ленты: `NC_TestRibbon.json` (панель «Тест»; «Рамки» в пакет не входит)

---

## Состав каталога

```
nanocad-test-ribbon/
  README.md
  NC_TestRibbon.json          ← манифест панели «Тест»
  dev-deploy.ps1              ← быстрое копирование DLL+JSON в LoadFile
  installer/                  ← полный локальный установщик (NcBootstrap + лента)
    INSTALL.bat / Install-NcTestRibbon.ps1
    VERIFY.bat / Verify-NcTestRibbon.ps1
    UNINSTALL.bat / Uninstall-NcTestRibbon.ps1
    deps/                     ← EPPlus 8.6.3 + transitive DLLs (copied to LoadFile)
    NcBootstrap.dll / .json / .deps.json
    icons/load_chart.ico
  NcTestRibbon/
    NcTestRibbon.sln
    NcTestRibbon.csproj
    Plugin.cs                 ← IExtensionApplication
    Commands.cs               ← команды + открытие WPF shell
    Shell/                    ← SettingsWindow (+Tracing/Trench tabs), MainWindow, PadOptimization…
    Directory.Build.props     ← NcSdkDir уже задан (UTF-8 BOM)
    icons/*.ico
```

---

## Установка после переустановки nanoCAD

После переустановки (или новой версии) nanoCAD профиль `Cfg.ini` создаётся заново — автозагрузку нужно прописать снова.

1. Закройте все окна nanoCAD.
2. **Обязательно пересоберите** плагин после обновления исходников (раздел «Сборка»).  
   Zip с GitHub содержит исходники; старый `bin\Release\...\NC_TestRibbon.dll` без `NcTestWpf` даст «неизвестную команду», хотя кнопка «Окно» на ленте уже есть.
3. Запустите **один раз** нужную версию nanoCAD и сразу закройте её  
   (чтобы появился `%APPDATA%\Nanosoft\nanoCAD*\Config\Cfg.ini`).
4. Из каталога `installer\` запустите `INSTALL.bat`  
   (или `Install-NcTestRibbon.ps1`). Установщик **откажется** ставить DLL, если в ней нет строк `NcTestHello` / `NcTestHttp` / `NcTestWpf`.
5. По желанию: `installer\VERIFY.bat` — путь к установленной DLL, LastWriteTime, FileVersion и проверка имён команд внутри файла.
6. Запустите nanoCAD и в командной строке проверьте:

```
NcTestHello
NcTestHttp
NcTestWpf
NcTestSettings
NcTestPadOpt
NcTestBlink
```

### Кустовые площадки (v1.2.16+)

`NcTestPadOpt` / лента **Кустовые площадки**:

- **Показывать на чертеже** — прямоугольники (ширина×длина), подпись сверху (высота текста = ½ ширины)
- Выбрать строку → **Задать положение вручную** (окно скрывается, jig следует за курсором) → жёлтая строка → **Сохранить координаты** (подтверждение, запись X/Y в лист «КП»)
- **Двойной щелчок** → профили + **Ограничивает в месяце**
- v1.2.17: CS0104/CS1503 в `CadPadOverlay` (DbPolyline + Geometry.Polyline normal/IntPtr)

### Мигание сегмента (v1.2.13+)

Как в End (`MainWindow.ManualProgress` / fragment blink, таймер 200 мс), но в чертеже nanoCAD:

1. Выделите **полилинию** (или другую кривую) в чертеже.
2. Лента **Тест → Мигание** или команда `NcTestBlink`.
3. Объект мигает (`Highlight` / `Unhighlight`), пока снова не нажмёте кнопку / `NcTestBlink` (отжать) или `NcTestBlinkOff`.

Что делает установщик:

- копирует **NcBootstrap** в `%LOCALAPPDATA%\ASDC\NcBootstrap` и регистрирует его в Startup (`Type=sMGD`) всех найденных профилей;
- **принудительно перезаписывает** `NC_TestRibbon.dll` + `NC_TestRibbon.json` в `%LOCALAPPDATA%\ASDC\LoadFile\NANOCAD`  
  (ищет сначала `..\NcTestRibbon\bin\Release\net6.0-windows\`, иначе Debug / `installer\payload\`; JSON берётся из корня пакета);
- copies `installer\deps\*.dll` (EPPlus 8.6.3 + transitive) — and any matching NuGet DLLs from the build output — next to the plugin in LoadFile;
- проверяет, что DLL содержит имена команд `NcTestHello`, `NcTestHttp`, `NcTestWpf`;
- **не** ставит панель «Рамки» / `NC_RibbonExecDoc` — при установке удаляет их DLL/JSON из LoadFile, Startup и кэш `.cfg`/`.cuix` профиля;
- `NcBootstrap.json` с пустым `serverUrl` (локальный режим, без синхронизации ASDC, чтобы «Загрузить» снова не подтянуло «Рамки»);
- добавляет Startup и для `NC_TestRibbon.dll`.

Удаление: `installer\UNINSTALL.bat` (чистит и остатки `NC_RibbonExecDoc`, если были).

Альтернатива без NcBootstrap: только `.\dev-deploy.ps1` / `.\dev-deploy.ps1 -RegisterStartup`.

Если команда «неизвестна» после установки: закройте nanoCAD → `dotnet build ... -c Release` → `INSTALL.bat` → `VERIFY.bat`.  
Вручную: команда **NETLOAD** → укажите  
`%LOCALAPPDATA%\ASDC\LoadFile\NANOCAD\NC_TestRibbon.dll`.

---

## 1. Открыть в Visual Studio

1. Установите **Visual Studio 2022** с рабочей нагрузкой «Разработка классических приложений .NET» (нужен .NET 6 SDK / Desktop).
2. Установите **nanoCAD SDK 24** (нужны `include-x64\hostmgd.dll` и `hostdbmgd.dll`).
3. Откройте `NcTestRibbon\NcTestRibbon.sln` двойным щелчком  
   **или** `NcTestRibbon.csproj` → «Open with Visual Studio».

### Путь к SDK (`NcSdkDir`)

В **v1.1.2** путь уже задан в `Directory.Build.props` + fallback в `.csproj` (как в RibbonExecDoc).  
Файлы сохранены в **UTF-8 с BOM** — кириллица в пути корректна для MSBuild.

Если CS0246 всё же есть: проверьте наличие  
`...\NC_SDK_RU_24.0.6388.4326.7145\include-x64\hostmgd.dll`  
или временно переопределите: `dotnet build -c Release -p:NcSdkDir="D:\OtherSdk\"`.

---

## 2. Сборка

```powershell
cd NcTestRibbon
dotnet build NcTestRibbon.csproj -c Release
```

Результат:

`NcTestRibbon\bin\Release\net6.0-windows\NC_TestRibbon.dll`

В Visual Studio: конфигурация **Release**, Build → Build Solution.

---

## 3. Локальная установка **без сервера ASDC**

Закройте nanoCAD перед копированием и правкой `Cfg.ini`.

### Вариант A — скрипт деплоя (рекомендуется)

Из корня `nanocad-test-ribbon`:

```powershell
# только файлы в LoadFile
.\dev-deploy.ps1

# + автозагрузка DLL во всех найденных профилях nanoCAD (Cfg.ini Startup)
.\dev-deploy.ps1 -RegisterStartup
```

Куда копируется:

`%LOCALAPPDATA%\ASDC\LoadFile\NANOCAD\`

- `NC_TestRibbon.dll`
- `NC_TestRibbon.json`

### Вариант B — вручную в LoadFile

Скопируйте:

| Файл | Куда |
|------|------|
| `NcTestRibbon\bin\Release\net6.0-windows\NC_TestRibbon.dll` | `%LOCALAPPDATA%\ASDC\LoadFile\NANOCAD\` |
| `NC_TestRibbon.json` | туда же |

### Вариант C — регистрация в `Cfg.ini` Startup

1. Найдите профиль: `%APPDATA%\Nanosoft\nanoCAD*\Config\Cfg.ini`  
   (сначала один раз запустите и закройте нужную версию nanoCAD).
2. Добавьте секцию (номер `appN` — свободный):

```ini
[\Configuration\<<Default>>\Appload\Startup\app42]
Loader=sC:\Users\<USER>\AppData\Local\ASDC\LoadFile\NANOCAD\NC_TestRibbon.dll
Type=sMGD
Enabled=i1
```

`Type=sMGD` — managed-модуль. Путь — абсолютный, к вашей копии DLL.

Можно указать путь прямо к `bin\Release\...\NC_TestRibbon.dll` для отладки без копирования в LoadFile.

---

## 4. Проверка команд

1. Запустите nanoCAD.
2. В командной строке введите:

```
NcTestHello
```

Ожидание: диалог / сообщение **«Тест 1: Hello»**.

```
NcTestHttp
```

Ожидание: окно с URL → GET → диалог с фрагментом ответа (или MessageBox при ошибке сети/URL).

```
NcTestWpf
```

Ожидание: отдельное **WinForms**-окно «Демо-окно (WinForms)» (кнопка OK / Закрыть). Открыто через `Form.ShowDialog()` с владельцем — главным HWND nanoCAD (**modal**, как `NcTestHttp`). В командную строку пишутся start/HWND/результат и любые исключения. WPF в проекте **нет**.

Если **`NcTestHello` / `NcTestHttp` работают, а `NcTestWpf` — «неизвестная команда»**: в LoadFile лежит **старая DLL** (кнопка ленты из нового JSON). Пересоберите Release и снова `INSTALL.bat`, затем `VERIFY.bat`.

Если все три «неизвестны» — DLL не загрузилась: Startup, путь, `net6.0-windows`, nanoCAD 24 x64; либо NETLOAD на файл в LoadFile.

---

## 5. Лента (панель «Тест») и обновление

- **Команды** регистрирует DLL (`[CommandMethod]`).
- **Кнопки ленты** строит **NcBootstrap** по `NC_TestRibbon.json`. Плагин сам UI не создаёт. Панель «Рамки» в этот пакет **не входит**.

Без ASDC:

- Команды проверяются с командной строки сразу после Startup (см. выше) — этого достаточно для локального теста логики.
- Панель «Тест» с тремя large-кнопками (Hello / HTTP GET / **Окно**) появится, когда JSON обработает NcBootstrap после `installer\INSTALL.bat`.

**Обновление после пересборки:**

1. Закройте nanoCAD.
2. Снова `.\dev-deploy.ps1` (или скопируйте DLL/JSON вручную).
3. Запустите nanoCAD.
4. Если пользуетесь NcBootstrap: при необходимости «Загрузить» / перезапуск — модули и лента кэшируются профилем.

Удаление тестового модуля: уберите секцию Startup с `NC_TestRibbon.dll` из `Cfg.ini` и удалите файлы из `LoadFile\NANOCAD`.

---

## 6. ASDC — позже

Этот пакет — **только локальный стенд** (без панели «Рамки»). Когда понадобится серверная раздача **только** `NC_TestRibbon`:

1. Выложить на ASDC в набор file list nanocad: `NC_TestRibbon.dll` + `NC_TestRibbon.json` (без `NC_RibbonExecDoc`).
2. Строка в `users_nanocad.conf` для тестового модуля.
3. Вернуть `serverUrl` в `NcBootstrap.json` на нужный ASDC — иначе локальный установщик намеренно не синхронизирует сервер.

Иконки уже внутри DLL (`NC_ICONS.hello.ico`, `NC_ICONS.http.ico`, `NC_ICONS.wpf.ico`); bootstrap извлекает их сам.

---

## Зависимости (кратко)

| Что | Зачем |
|-----|--------|
| `net6.0-windows` | runtime nanoCAD 24 |
| `UseWindowsForms` | WinForms (`NcTestHttp` + `NcTestWpf` / `DemoForm`, оба `ShowDialog`). **WPF не используется** |
| `hostmgd` / `hostdbmgd` | Document, Editor, `[CommandMethod]`, `IExtensionApplication` |

См. также: `docs/nanocad-ribbon-reference.md` — архитектура образца RibbonExecDoc.


## WPF pack URI / CAD

`AssemblyVersion` зафиксирован на `1.0.0.0` (FileVersion = продукт). Иначе после NETLOAD WPF ищет `/NC_TestRibbon;V1.x.y.z;component/Shell/...` и падает.
Pad-окно (`NcTestPadOpt`) собрано в коде без XAML.

## Tracing_cache

`%LocalAppData%\ASDC\LoadFile\NANOCAD\Tracing_cache\` — `Settings\` (Price.xlsx|xlsm, `paths.json`, tracing.conf, trench_road.conf), `Maps\`, `SourceData\`.

`paths.json` remembers last pads workbook / Price / fluid / background / profiles JSON. Pad window: **Открыть таблицы…** loads sheet «КП» (incl. Ширина/Длина).

## Settings without pack URI

`SettingsWindow` is code-built. Tracing tab XAML is an **EmbeddedResource** loaded via `XamlReader` (no `/NC_TestRibbon;V…;component/shell/settingswindow.xaml`).

## Duplicate Shell classes (CS0101/CS0111)

Always unpack into a **NEW** empty folder.

If VS says a class is defined twice (`SettingsWindow` / `TracingSettingsPanel`): delete leftover `Shell\*.xaml.cs` twins from a previous unzip (`SettingsWindow.xaml.cs`, `TracingSettingsPanel.xaml.cs`, `InitialData.*`), keep `SettingsWindow.cs` + `TracingSettingsPanel.cs`, then rebuild. INSTALL and MSBuild also delete those leftovers.
