# Steamworks.NET（精简说明）

本目录包含 [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) 的 Runtime 源码与匹配的 `steam_api64.dll`（MIT）。

主工程通过 `app/app.csproj` 以 Link 方式编译 `Runtime/**/*.cs`，并复制 `native/win-x64/steam_api64.dll`。

请勿单独对旧版 Keplerth 的 `Steamworks.NET.dll`（.NET Framework）做引用。
