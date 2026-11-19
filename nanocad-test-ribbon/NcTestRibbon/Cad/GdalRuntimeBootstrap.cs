using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace NcTestRibbon.Cad
{
    /// <summary>
    /// Инициализация native GDAL + явная диагностика/загрузка ECW plugin.
    ///
    /// R3:
    /// 1) пути native/plugin настраиваются до первого Gdal.*;
    /// 2) затем загружается core GDAL;
    /// 3) GDAL_DRIVER_PATH задаётся через Gdal.SetConfigOption ДО AllRegister();
    /// 4) NCSEcw.dll и gdal_ECW_JP2ECW.dll пробуются через LoadLibraryExW;
    /// 5) если AutoLoadDrivers не зарегистрировал ECW, пробуем вызвать
    ///    экспорт регистрации plugin напрямую;
    /// 6) в диагностике выводится Win32 error 126/127/193 и версии DLL.
    /// </summary>
    public static class GdalRuntimeBootstrap
    {
        private static readonly object Sync = new();

        private static bool _initialized;
        private static string _diagnostics = "";

        // Держим handles до завершения процесса.
        // Нельзя FreeLibrary() после регистрации GDAL driver.
        private static IntPtr _ncsEcwHandle = IntPtr.Zero;
        private static IntPtr _ecwPluginHandle = IntPtr.Zero;

        // SetDefaultDllDirectories:
        // LOAD_LIBRARY_SEARCH_USER_DIRS | LOAD_LIBRARY_SEARCH_SYSTEM32
        private const uint DefaultDllSearchFlags =
            0x00000400u |
            0x00000800u;

        // LoadLibraryEx:
        private const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100u;
        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000u;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint DirectoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string NewDirectory);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(
            string lpLibFileName,
            IntPtr hFile,
            uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(
            IntPtr hModule,
            string lpProcName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GdalRegisterPluginDelegate();

        public static string Diagnostics => _diagnostics;

        public static bool TryInitialize(
            string applicationBaseDirectory,
            out string diagnostics,
            out string error)
        {
            lock (Sync)
            {
                if (_initialized)
                {
                    diagnostics = _diagnostics;
                    error = null;
                    return true;
                }

                var sb = new StringBuilder();

                try
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        throw new PlatformNotSupportedException(
                            "Этот проект настроен на Windows GDAL runtime.");
                    }

                    string baseDir =
                        string.IsNullOrWhiteSpace(applicationBaseDirectory)
                            ? AppContext.BaseDirectory
                            : Path.GetFullPath(applicationBaseDirectory);

                    string architecture =
                        Environment.Is64BitProcess ? "x64" : "x86";

                    string nativeDir =
                        FindNativeDirectory(baseDir, architecture);

                    if (nativeDir == null)
                    {
                        throw new FileNotFoundException(
                            "Не найден gdal_wrap.dll. " +
                            $"Ожидался, например, '{Path.Combine(baseDir, "gdal", architecture)}'.");
                    }

                    string wrapPath =
                        Path.Combine(nativeDir, "gdal_wrap.dll");

                    string pluginDir =
                        FindPluginDirectory(baseDir, nativeDir);

                    string gdalRoot =
                        FindGdalRoot(baseDir, nativeDir);

                    string dataDir =
                        FindExistingDirectory(
                            Path.Combine(gdalRoot, "data"),
                            Path.Combine(gdalRoot, "gdal-data"));

                    string projDir =
                        FindExistingDirectory(
                            Path.Combine(gdalRoot, "share"),
                            Path.Combine(gdalRoot, "projlib"),
                            Path.Combine(gdalRoot, "proj"));

                    string ecwPluginPath =
                        string.IsNullOrWhiteSpace(pluginDir)
                            ? null
                            : FindFileIgnoreCase(
                                pluginDir,
                                "gdal_ECW_JP2ECW.dll");

                    string ncsEcwPath =
                        FindFileNearby(
                            pluginDir,
                            nativeDir,
                            "NCSEcw.dll");

                    // ---------------------------------------------------------
                    // 1. Windows DLL search path — ДО первого Gdal.*
                    // ---------------------------------------------------------

                    bool defaultDirsOk =
                        SetDefaultDllDirectories(DefaultDllSearchFlags);

                    int defaultDirsError =
                        defaultDirsOk ? 0 : Marshal.GetLastWin32Error();

                    IntPtr nativeCookie =
                        AddSearchDirectory(nativeDir);

                    IntPtr pluginCookie =
                        !string.IsNullOrWhiteSpace(pluginDir)
                            ? AddSearchDirectory(pluginDir)
                            : IntPtr.Zero;

                    // В PATH кладём:
                    // plugins -> native -> старый PATH
                    //
                    // Это полезно для vendor DLL, которые внутри сами
                    // делают LoadLibrary("имя.dll").
                    PrependProcessPath(nativeDir);

                    if (!string.IsNullOrWhiteSpace(pluginDir))
                        PrependProcessPath(pluginDir);

                    // Fallback старого Win32 loader.
                    // Для ECW выгоднее pluginDir, т.к. NCSEcw.dll лежит там.
                    if (!string.IsNullOrWhiteSpace(pluginDir))
                        SetDllDirectory(pluginDir);
                    else
                        SetDllDirectory(nativeDir);

                    // ---------------------------------------------------------
                    // 2. Environment config — всё ещё до первого Gdal.*
                    // ---------------------------------------------------------

                    if (!string.IsNullOrWhiteSpace(dataDir))
                    {
                        Environment.SetEnvironmentVariable(
                            "GDAL_DATA",
                            dataDir,
                            EnvironmentVariableTarget.Process);

                        Environment.SetEnvironmentVariable(
                            "GEOTIFF_CSV",
                            dataDir,
                            EnvironmentVariableTarget.Process);
                    }

                    if (!string.IsNullOrWhiteSpace(projDir))
                    {
                        Environment.SetEnvironmentVariable(
                            "PROJ_LIB",
                            projDir,
                            EnvironmentVariableTarget.Process);

                        Environment.SetEnvironmentVariable(
                            "PROJ_DATA",
                            projDir,
                            EnvironmentVariableTarget.Process);
                    }

                    if (!string.IsNullOrWhiteSpace(pluginDir))
                    {
                        Environment.SetEnvironmentVariable(
                            "GDAL_DRIVER_PATH",
                            pluginDir,
                            EnvironmentVariableTarget.Process);
                    }

                    // ---------------------------------------------------------
                    // 3. Загружаем core GDAL, НО AllRegister пока не вызываем.
                    // ---------------------------------------------------------

                    string gdalVersion =
                        SafeGdalVersionInfo();

                    // Важное изменение R3:
                    // задаём config option GDAL_DRIVER_PATH непосредственно
                    // внутри GDAL ДО AllRegister().
                    if (!string.IsNullOrWhiteSpace(dataDir))
                    {
                        Gdal.SetConfigOption("GDAL_DATA", dataDir);
                        Gdal.SetConfigOption("GEOTIFF_CSV", dataDir);
                    }

                    if (!string.IsNullOrWhiteSpace(projDir))
                    {
                        Gdal.SetConfigOption("PROJ_LIB", projDir);
                        Gdal.SetConfigOption("PROJ_DATA", projDir);
                    }

                    if (!string.IsNullOrWhiteSpace(pluginDir))
                    {
                        Gdal.SetConfigOption(
                            "GDAL_DRIVER_PATH",
                            pluginDir);
                    }

                    // ---------------------------------------------------------
                    // 4. Явно пробуем загрузить vendor ECW runtime и plugin.
                    //
                    // Это одновременно:
                    // - помогает Windows loader;
                    // - даёт ТОЧНЫЙ Win32 error, если DLL не грузится.
                    // ---------------------------------------------------------

                    NativeLoadResult ncsLoad =
                        TryLoadNativeLibrary(ncsEcwPath);

                    if (ncsLoad.Success)
                        _ncsEcwHandle = ncsLoad.Handle;

                    NativeLoadResult pluginLoad =
                        TryLoadNativeLibrary(ecwPluginPath);

                    if (pluginLoad.Success)
                        _ecwPluginHandle = pluginLoad.Handle;

                    // ---------------------------------------------------------
                    // 5. Теперь стандартная регистрация GDAL.
                    // ---------------------------------------------------------

                    Gdal.AllRegister();

                    Driver ecw =
                        Gdal.GetDriverByName("ECW");

                    string manualRegistration = "не требовалась";

                    // Если DLL физически загрузилась, но AutoLoadDrivers почему-то
                    // не вызвал register export — регистрируем явно.
                    if (ecw == null &&
                        _ecwPluginHandle != IntPtr.Zero)
                    {
                        manualRegistration =
                            TryRegisterEcwPluginExports(
                                _ecwPluginHandle,
                                out string registerDetails);

                        ecw =
                            Gdal.GetDriverByName("ECW");

                        if (!string.IsNullOrWhiteSpace(registerDetails))
                            manualRegistration +=
                                " | " + registerDetails;
                    }

                    Driver jp2ecw =
                        Gdal.GetDriverByName("JP2ECW");

                    // ---------------------------------------------------------
                    // 6. Диагностика.
                    // ---------------------------------------------------------

                    sb.AppendLine($"GDAL version: {gdalVersion}");
                    sb.AppendLine($"Process: {(Environment.Is64BitProcess ? "x64" : "x86")}");
                    sb.AppendLine($"App base: {baseDir}");

                    sb.AppendLine($"GDAL native: {nativeDir}");
                    sb.AppendLine($"gdal_wrap.dll: {wrapPath}");
                    AppendFileVersion(sb, "gdal_wrap.dll version", wrapPath);

                    sb.AppendLine(
                        $"SetDefaultDllDirectories: " +
                        $"{(defaultDirsOk ? "OK" : $"ERROR {defaultDirsError}: {GetWin32Message(defaultDirsError)}")}");

                    sb.AppendLine(
                        $"AddDllDirectory(native): " +
                        $"{(nativeCookie != IntPtr.Zero ? "OK" : "FAILED")}");

                    sb.AppendLine(
                        $"GDAL plugins: {(pluginDir ?? "не найден")}");

                    sb.AppendLine(
                        $"AddDllDirectory(plugin): " +
                        $"{(pluginCookie != IntPtr.Zero ? "OK" : "FAILED/нет каталога")}");

                    sb.AppendLine($"GDAL_DATA: {(dataDir ?? "не найден")}");
                    sb.AppendLine($"PROJ: {(projDir ?? "не найден")}");

                    sb.AppendLine(
                        $"GDAL_DRIVER_PATH(env): " +
                        $"{Environment.GetEnvironmentVariable("GDAL_DRIVER_PATH", EnvironmentVariableTarget.Process) ?? "не задан"}");

                    sb.AppendLine(
                        $"GDAL_DRIVER_PATH(config): " +
                        $"{Gdal.GetConfigOption("GDAL_DRIVER_PATH", "не задан")}");

                    sb.AppendLine(
                        $"NCSEcw.dll: {(ncsEcwPath ?? "не найден")}");

                    if (!string.IsNullOrWhiteSpace(ncsEcwPath))
                        AppendFileVersion(sb, "NCSEcw.dll version", ncsEcwPath);

                    sb.AppendLine(
                        $"NCSEcw LoadLibraryEx: {ncsLoad.Description}");

                    sb.AppendLine(
                        $"ECW plugin DLL: {(ecwPluginPath ?? "не найден")}");

                    if (!string.IsNullOrWhiteSpace(ecwPluginPath))
                        AppendFileVersion(sb, "ECW plugin version", ecwPluginPath);

                    sb.AppendLine(
                        $"ECW plugin LoadLibraryEx: {pluginLoad.Description}");

                    sb.AppendLine(
                        $"Manual ECW register: {manualRegistration}");

                    sb.AppendLine(
                        $"ECW driver: {(ecw != null ? "OK" : "НЕ ЗАРЕГИСТРИРОВАН")}");

                    sb.AppendLine(
                        $"JP2ECW driver: {(jp2ecw != null ? "OK" : "НЕ ЗАРЕГИСТРИРОВАН")}");

                    if (ecw == null)
                    {
                        sb.AppendLine();
                        sb.AppendLine("ECW DIAGNOSIS:");

                        if (!ncsLoad.Success &&
                            !string.IsNullOrWhiteSpace(ncsEcwPath))
                        {
                            sb.AppendLine(
                                "- Не загружается NCSEcw.dll. " +
                                "Смотрите Win32 error выше. " +
                                "Обычно это отсутствующая транзитивная DLL " +
                                "или VC++ runtime.");
                        }
                        else if (!pluginLoad.Success &&
                                 !string.IsNullOrWhiteSpace(ecwPluginPath))
                        {
                            sb.AppendLine(
                                "- NCSEcw.dll загрузилась, но не загружается " +
                                "gdal_ECW_JP2ECW.dll. " +
                                "ERROR 127 обычно означает несовместимость " +
                                "plugin с текущим gdal.dll; ERROR 126 — " +
                                "не найдена одна из зависимостей; ERROR 193 — " +
                                "несовпадение x86/x64.");
                        }
                        else if (pluginLoad.Success)
                        {
                            sb.AppendLine(
                                "- ECW plugin DLL физически загрузилась, " +
                                "но driver не зарегистрирован. " +
                                "Смотрите строку Manual ECW register: это уже " +
                                "проблема exports/ABI plugin.");
                        }
                        else
                        {
                            sb.AppendLine(
                                "- ECW plugin/runtime не найден либо не загрузился.");
                        }
                    }

                    _diagnostics =
                        sb.ToString().TrimEnd();

                    // Core GDAL успешно запущен даже если optional ECW отсутствует.
                    _initialized = true;

                    diagnostics = _diagnostics;
                    error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("EXCEPTION:");
                    }

                    sb.AppendLine(BuildDetailedException(ex));

                    _diagnostics =
                        sb.ToString().TrimEnd();

                    diagnostics = _diagnostics;

                    error =
                        "Не удалось инициализировать native GDAL. " +
                        BuildDetailedException(ex);

                    return false;
                }
            }
        }

        private static string SafeGdalVersionInfo()
        {
            try
            {
                // Первый managed -> native GDAL вызов.
                // Драйверы он не регистрирует.
                string value =
                    Gdal.VersionInfo("RELEASE_NAME");

                return string.IsNullOrWhiteSpace(value)
                    ? "не определена"
                    : value;
            }
            catch
            {
                // Дадим исходному исключению подняться более информативно
                // через второй вызов.
                return Gdal.VersionInfo("--version") ??
                       "не определена";
            }
        }

        private static NativeLoadResult TryLoadNativeLibrary(
            string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return new NativeLoadResult(
                    false,
                    IntPtr.Zero,
                    0,
                    "файл не найден");
            }

            if (!File.Exists(fullPath))
            {
                return new NativeLoadResult(
                    false,
                    IntPtr.Zero,
                    2,
                    "файл не существует: " + fullPath);
            }

            // DLL_LOAD_DIR:
            // зависимости этой DLL ищутся также в её собственной папке.
            IntPtr handle =
                LoadLibraryExW(
                    fullPath,
                    IntPtr.Zero,
                    LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR |
                    LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);

            if (handle != IntPtr.Zero)
            {
                return new NativeLoadResult(
                    true,
                    handle,
                    0,
                    "OK");
            }

            int error =
                Marshal.GetLastWin32Error();

            return new NativeLoadResult(
                false,
                IntPtr.Zero,
                error,
                $"ERROR {error}: {GetWin32Message(error)}");
        }

        private static string TryRegisterEcwPluginExports(
            IntPtr pluginHandle,
            out string details)
        {
            var called =
                new List<string>();

            var found =
                new List<string>();

            // Стандарт AutoLoadDrivers:
            // basename gdal_ECW_JP2ECW.dll ->
            // GDALRegister_ECW_JP2ECW(), либо GDALRegisterMe().
            //
            // Некоторые ECW builds экспортируют отдельные функции.
            string[] names =
            {
                "GDALRegister_ECW_JP2ECW",
                "GDALRegisterMe",
                "GDALRegister_ECW",
                "GDALRegister_JP2ECW"
            };

            foreach (string name in names)
            {
                IntPtr proc =
                    GetProcAddress(pluginHandle, name);

                if (proc == IntPtr.Zero)
                    continue;

                found.Add(name);

                try
                {
                    var register =
                        Marshal.GetDelegateForFunctionPointer
                            <GdalRegisterPluginDelegate>(proc);

                    register();
                    called.Add(name);

                    if (Gdal.GetDriverByName("ECW") != null)
                        break;
                }
                catch (Exception ex)
                {
                    details =
                        $"export {name} найден, но вызов дал " +
                        $"{ex.GetType().Name}: {ex.Message}";

                    return "ОШИБКА";
                }
            }

            details =
                $"exports found=[{string.Join(", ", found)}], " +
                $"called=[{string.Join(", ", called)}]";

            if (Gdal.GetDriverByName("ECW") != null)
                return "OK";

            if (found.Count == 0)
                return "НЕ НАЙДЕН REGISTER EXPORT";

            return "DRIVER НЕ ПОЯВИЛСЯ";
        }

        private static string FindNativeDirectory(
            string baseDir,
            string architecture)
        {
            var searchRoots = new List<string>();
            if (!string.IsNullOrWhiteSpace(baseDir))
                searchRoots.Add(baseDir);

            try
            {
                string loadFile = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "ASDC",
                    "LoadFile",
                    "NANOCAD");
                if (!searchRoots.Any(
                        r => string.Equals(
                            r,
                            loadFile,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    searchRoots.Add(loadFile);
                }
            }
            catch
            {
            }

            foreach (string root in searchRoots)
            {
                string[] preferred =
                {
                    Path.Combine(root, "gdal", architecture),
                    Path.Combine(
                        root,
                        "runtimes",
                        "win-" + architecture,
                        "native"),
                    root
                };

                foreach (string dir in preferred)
                {
                    if (Directory.Exists(dir) &&
                        File.Exists(
                            Path.Combine(dir, "gdal_wrap.dll")))
                    {
                        return dir;
                    }
                }
            }

            foreach (string root in searchRoots)
            {
                if (!Directory.Exists(root))
                    continue;

                try
                {
                    string wrap =
                        Directory
                            .EnumerateFiles(
                                root,
                                "gdal_wrap.dll",
                                SearchOption.AllDirectories)
                            .FirstOrDefault();

                    if (wrap != null)
                        return Path.GetDirectoryName(wrap);
                }
                catch
                {
                }
            }

            return null;
        }

        private static string FindPluginDirectory(
            string baseDir,
            string nativeDir)
        {
            string architecture =
                Environment.Is64BitProcess ? "x64" : "x86";

            string[] preferred =
            {
                Path.Combine(nativeDir, "plugins"),
                Path.Combine(nativeDir, "gdalplugins"),
                Path.Combine(
                    baseDir,
                    "gdal",
                    architecture,
                    "plugins")
            };

            foreach (string dir in preferred)
            {
                if (Directory.Exists(dir))
                    return dir;
            }

            try
            {
                string plugin =
                    Directory
                        .EnumerateFiles(
                            baseDir,
                            "gdal_ECW_JP2ECW.dll",
                            SearchOption.AllDirectories)
                        .FirstOrDefault();

                return plugin == null
                    ? null
                    : Path.GetDirectoryName(plugin);
            }
            catch
            {
                return null;
            }
        }

        private static string FindGdalRoot(
            string baseDir,
            string nativeDir)
        {
            var dir =
                new DirectoryInfo(nativeDir);

            if (dir.Parent != null &&
                (string.Equals(
                     dir.Name,
                     "x64",
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     dir.Name,
                     "x86",
                     StringComparison.OrdinalIgnoreCase)))
            {
                return dir.Parent.FullName;
            }

            string standard =
                Path.Combine(baseDir, "gdal");

            return Directory.Exists(standard)
                ? standard
                : baseDir;
        }

        private static IntPtr AddSearchDirectory(
            string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) ||
                !Directory.Exists(dir))
            {
                return IntPtr.Zero;
            }

            return AddDllDirectory(dir);
        }

        private static void PrependProcessPath(
            string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) ||
                !Directory.Exists(dir))
            {
                return;
            }

            string path =
                Environment.GetEnvironmentVariable(
                    "PATH",
                    EnvironmentVariableTarget.Process) ?? "";

            var parts =
                path.Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

            if (parts.Any(
                    p => string.Equals(
                        p,
                        dir,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Environment.SetEnvironmentVariable(
                "PATH",
                dir + ";" + path,
                EnvironmentVariableTarget.Process);
        }

        private static string FindExistingDirectory(
            params string[] dirs)
        {
            return dirs.FirstOrDefault(Directory.Exists);
        }

        private static string FindFileNearby(
            string pluginDir,
            string nativeDir,
            string fileName)
        {
            string[] dirs =
            {
                pluginDir,
                nativeDir
            };

            foreach (string dir in dirs)
            {
                if (string.IsNullOrWhiteSpace(dir) ||
                    !Directory.Exists(dir))
                {
                    continue;
                }

                string exact =
                    Path.Combine(dir, fileName);

                if (File.Exists(exact))
                    return exact;

                string insensitive =
                    FindFileIgnoreCase(dir, fileName);

                if (insensitive != null)
                    return insensitive;
            }

            return null;
        }

        private static string FindFileIgnoreCase(
            string dir,
            string fileName)
        {
            if (string.IsNullOrWhiteSpace(dir) ||
                !Directory.Exists(dir))
            {
                return null;
            }

            try
            {
                return Directory
                    .EnumerateFiles(
                        dir,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(
                        p => string.Equals(
                            Path.GetFileName(p),
                            fileName,
                            StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static void AppendFileVersion(
            StringBuilder sb,
            string label,
            string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) ||
                    !File.Exists(filePath))
                {
                    sb.AppendLine($"{label}: файл не найден");
                    return;
                }

                FileVersionInfo info =
                    FileVersionInfo.GetVersionInfo(filePath);

                string version =
                    info.FileVersion ??
                    info.ProductVersion ??
                    "нет version resource";

                sb.AppendLine($"{label}: {version}");
            }
            catch (Exception ex)
            {
                sb.AppendLine(
                    $"{label}: ошибка чтения версии: {ex.Message}");
            }
        }

        private static string GetWin32Message(
            int errorCode)
        {
            if (errorCode == 0)
                return "OK";

            try
            {
                return new Win32Exception(
                    errorCode).Message;
            }
            catch
            {
                return "неизвестная ошибка Win32";
            }
        }

        private static string BuildDetailedException(
            Exception ex)
        {
            var messages =
                new List<string>();

            Exception cur = ex;
            int depth = 0;

            while (cur != null && depth < 10)
            {
                messages.Add(
                    $"{cur.GetType().Name}: {cur.Message}");

                cur = cur.InnerException;
                depth++;
            }

            return string.Join(
                " -> ",
                messages);
        }

        private readonly struct NativeLoadResult
        {
            public NativeLoadResult(
                bool success,
                IntPtr handle,
                int errorCode,
                string description)
            {
                Success = success;
                Handle = handle;
                ErrorCode = errorCode;
                Description = description;
            }

            public bool Success { get; }
            public IntPtr Handle { get; }
            public int ErrorCode { get; }
            public string Description { get; }
        }
    }
}
