# NcTestRibbon — тестовая лента nanoCAD (локально, без ASDC)

Минимальный плагин **.NET 6 / Windows** для nanoCAD 24: три команды и манифест панели «Тест».
Сервер ASDC **не нужен** для локальной проверки команд. Раздача через ASDC — отдельный следующий шаг.

Актуальный zip с GitHub (версия **1.1.1**): `nanocad-test-ribbon-v1.1.1.zip`  
https://raw.githubusercontent.com/rublikdimas1989/Nanocad-Ribbon/main/nanocad-test-ribbon-v1.1.1.zip  

### Быстрая пересборка (если CS0246 HostMgd / Teigha)

1. Скачайте **v1.1.1**, распакуйте в **новую** папку (не поверх старой с `DemoWindow.xaml`).
2. Если в `NcTestRibbon\` остались `DemoWindow.xaml` / `DemoWindow.xaml.cs` — **удалите** их (это старый WPF; сейчас только WinForms в `Commands.cs`).
3. Укажите путь к SDK (папка, где есть `include-x64\hostmgd.dll`):

```powershell
cd NcTestRibbon
copy Directory.Build.props.example Directory.Build.props
notepad Directory.Build.props
```

В `NcSdkDir` впишите свой путь (пробелы в YandexDisk — нормально), например:

`C:\Users\bublik_da\YandexDisk\...\NC_SDK_RU_24.0.6388.4326.7145\`

Или одной командой (кавычки обязательны при пробелах):

```powershell
dotnet build NcTestRibbon.csproj -c Release `
  -p:NcSdkDir="C:\Users\bublik_da\YandexDisk\PATH\NC_SDK_RU_24.0.6388.4326.7145\"
```

4. `dotnet build NcTestRibbon.csproj -c Release`
5. `..\installer\INSTALL.bat` → `..\installer\VERIFY.bat` → nanoCAD: `NcTestHello` / `NcTestHttp` / `NcTestWpf`

| Команда | Что делает |
|---------|------------|
| `NcTestHello` | Сообщение «Тест 1: Hello» (Editor + диалог) |
| `NcTestHttp` | WinForms: URL → `HttpClient` GET (по умолчанию `https://httpbin.org/get`); ошибка — MessageBox |
| `NcTestWpf` | **WinForms** (не WPF): `DemoForm` через `Form.ShowDialog(owner)` — тот же modal-путь, что у `NcTestHttp` |

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
    NcBootstrap.dll / .json / .deps.json
    icons/load_chart.ico
  NcTestRibbon/
    NcTestRibbon.sln
    NcTestRibbon.csproj
    Plugin.cs                 ← IExtensionApplication
    Commands.cs               ← NcTestHello / NcTestHttp / NcTestWpf (DemoForm WinForms)
    Directory.Build.props.example  ← скопировать в Directory.Build.props и задать NcSdkDir
    icons/hello.ico
    icons/http.ico
    icons/wpf.ico             ← icon for ribbon «Окно» (NC_ICONS.wpf.ico)
    (нет DemoWindow.xaml — WPF удалён)
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
```

Что делает установщик:

- копирует **NcBootstrap** в `%LOCALAPPDATA%\ASDC\NcBootstrap` и регистрирует его в Startup (`Type=sMGD`) всех найденных профилей;
- **принудительно перезаписывает** `NC_TestRibbon.dll` + `NC_TestRibbon.json` в `%LOCALAPPDATA%\ASDC\LoadFile\NANOCAD`  
  (ищет сначала `..\NcTestRibbon\bin\Release\net6.0-windows\`, иначе Debug / `installer\payload\`; JSON берётся из корня пакета);
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

### Путь к SDK (`NcSdkDir`) — устранение CS0246

Ошибки `CS0246: HostMgd / Teigha / CommandMethod / Document / Editor` значат: **Visual Studio не видит SDK**.  
Нужна папка SDK с файлами `include-x64\hostmgd.dll` и `hostdbmgd.dll`.

**Рекомендуемый способ** (путь с пробелами / YandexDisk ок):

1. `copy Directory.Build.props.example Directory.Build.props`
2. В `Directory.Build.props` задайте:

```xml
<NcSdkDir>C:\Users\bublik_da\YandexDisk\ВАША_ПАПКА\NC_SDK_RU_24.0.6388.4326.7145\</NcSdkDir>
```

3. Reload проекта / Rebuild.

**Альтернатива — командная строка** (кавычки при пробелах):

```powershell
dotnet build NcTestRibbon.csproj -c Release `
  -p:NcSdkDir="C:\Users\bublik_da\YandexDisk\PATH\NC_SDK_RU_24.0.6388.4326.7145\"
```

По умолчанию в `.csproj`: `C:\NC_SDK_RU_24.0\` (если `NcSdkDir` не задан).  
При сборке скрипт сам добавит `\` в конце пути, если его нет. Если `hostmgd.dll` не найден — сборка остановится с понятным текстом вместо «тихих» CS0246.

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
