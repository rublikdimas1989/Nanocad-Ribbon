# NcTestRibbon — тестовая лента nanoCAD (локально, без ASDC)

Минимальный плагин **.NET 6 / Windows** для nanoCAD 24: две команды и манифест панели «Тест».
Сервер ASDC **не нужен** для локальной проверки команд. Раздача через ASDC — отдельный следующий шаг.

| Команда | Что делает |
|---------|------------|
| `NcTestHello` | Сообщение «Тест 1: Hello» (Editor + диалог) |
| `NcTestHttp` | WinForms: URL → `HttpClient` GET (по умолчанию `https://httpbin.org/get`); ошибка — MessageBox |

Сборка: `NC_TestRibbon.dll`  
Манифест ленты: `NC_TestRibbon.json` (схема как у `NC_RibbonExecDoc.json`)

---

## Состав каталога

```
nanocad-test-ribbon/
  README.md
  NC_TestRibbon.json          ← манифест панели «Тест»
  dev-deploy.ps1              ← копирование DLL+JSON в LoadFile
  NcTestRibbon/
    NcTestRibbon.sln
    NcTestRibbon.csproj
    Plugin.cs                 ← IExtensionApplication
    Commands.cs               ← NcTestHello / NcTestHttp
    icons/hello.ico
    icons/http.ico            ← embedded как NC_ICONS.*
```

---

## 1. Открыть в Visual Studio

1. Установите **Visual Studio 2022** с рабочей нагрузкой «Разработка классических приложений .NET» (нужен .NET 6 SDK / Desktop).
2. Установите **nanoCAD SDK 24** (нужны `include-x64\hostmgd.dll` и `hostdbmgd.dll`).
3. Откройте `NcTestRibbon\NcTestRibbon.sln` двойным щелчком  
   **или** `NcTestRibbon.csproj` → «Open with Visual Studio».

### Путь к SDK (`NcSdkDir`)

В `NcTestRibbon.csproj` задано:

```xml
<NcSdkDir Condition="'$(NcSdkDir)' == ''">C:\NC_SDK_RU_24.0\</NcSdkDir>
```

HintPath ссылок: `$(NcSdkDir)include-x64\hostmgd.dll` и `hostdbmgd.dll` (`Private=False` — в выход не копируются).

**Как указать свой SDK:**

- Править `NcSdkDir` в `.csproj`, **или**
- Сборка из командной строки:

```powershell
dotnet build NcTestRibbon\NcTestRibbon.csproj -c Release `
  -p:NcSdkDir="C:\Users\...\NC_SDK_RU_24.0.6388.4326.7145\"
```

(Слеш в конце пути желателен.)

Также можно положить SDK рядом и править HintPath под относительный путь — главное, чтобы `hostmgd` / `hostdbmgd` находились при сборке.

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

Если команда «неизвестна» — DLL не загрузилась: проверьте Startup, путь, что сборка `net6.0-windows`, что nanoCAD 24 x64.

---

## 5. Лента (панель «Тест») и обновление

- **Команды** регистрирует DLL (`[CommandMethod]`).
- **Кнопки ленты** строит **NcBootstrap** по `NC_TestRibbon.json` (как у образца «Рамки»). Плагин сам UI не создаёт.

Без ASDC:

- Команды проверяются с командной строки сразу после Startup (см. выше) — этого достаточно для локального теста логики.
- Панель «Тест» с двумя large-кнопками появится, когда JSON обработает NcBootstrap (уже установленный у вас) или когда файлы позже попадут на ASDC и пользователь нажмёт «Загрузить».

**Обновление после пересборки:**

1. Закройте nanoCAD.
2. Снова `.\dev-deploy.ps1` (или скопируйте DLL/JSON вручную).
3. Запустите nanoCAD.
4. Если пользуетесь NcBootstrap: при необходимости «Загрузить» / перезапуск — модули и лента кэшируются профилем.

Удаление тестового модуля: уберите секцию Startup с `NC_TestRibbon.dll` из `Cfg.ini` и удалите файлы из `LoadFile\NANOCAD`.

---

## 6. ASDC — позже

Этот пакет — **только локальный стенд**. Когда понадобится серверная раздача:

1. Выложить на ASDC в набор file list nanocad: `NC_TestRibbon.dll` + `NC_TestRibbon.json`.
2. Строка в `users_nanocad.conf` (по аналогии с `NC_RibbonExecDoc`).
3. У пользователей с NcBootstrap — кнопка «Загрузить» подтянет панель «Тест».

Иконки уже внутри DLL (`NC_ICONS.hello.ico`, `NC_ICONS.http.ico`); bootstrap извлекает их сам.

---

## Зависимости (кратко)

| Что | Зачем |
|-----|--------|
| `net6.0-windows` | runtime nanoCAD 24 |
| `Microsoft.WindowsDesktop.App` | WinForms |
| `hostmgd` / `hostdbmgd` | Document, Editor, `[CommandMethod]`, `IExtensionApplication` |

См. также: `docs/nanocad-ribbon-reference.md` — архитектура образца RibbonExecDoc.
