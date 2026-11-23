Managed deps for NC_TestRibbon (EPPlus + GDAL csharp bindings).

INSTALL.bat / Install-NcTestRibbon.ps1 copy these DLLs into
%LOCALAPPDATA%\ASDC\LoadFile\NANOCAD\ next to NC_TestRibbon.dll.

Native GDAL + ECW plugins live under installer\gdal\ (copied to
LoadFile\NANOCAD\gdal\). Layout: gdal\x64\gdal_wrap.dll and
gdal\x64\plugins\gdal_ECW_JP2ECW.dll + NCSEcw.dll.

After a local Release build, MSBuild target SyncEpplusDepsToInstaller
refreshes deps\ from bin\Release and may refresh gdal\ from NuGet.

Do not put nanoCAD SDK DLLs (hostmgd / hostdbmgd) here.
