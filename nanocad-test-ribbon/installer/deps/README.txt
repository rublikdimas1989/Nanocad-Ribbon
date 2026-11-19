EPPlus and transitive managed deps for NC_TestRibbon.

INSTALL.bat / Install-NcTestRibbon.ps1 copy these DLLs into
%LOCALAPPDATA%\ASDC\LoadFile\NANOCAD\ next to NC_TestRibbon.dll.

After a local Release build, MSBuild target SyncEpplusDepsToInstaller
refreshes this folder from bin\Release\net6.0-windows\.

Do not put nanoCAD SDK DLLs (hostmgd / hostdbmgd) here.
