# RadminBell - Private Bloodborne Co-op Server

Source code of **RadminBell**: 2 small Windows apps to host your own private
[Seamless Co-op (BBSC)](https://github.com/CLSAMG22/BBSC) server for BBport, over Radmin VPN.

- `01.RadminBell.exe` (`Painel.cs`): control panel. Starts/stops the official BBSC server
  (shadnet + BBSC-GameServer), adds Windows Firewall rules limited to the Radmin VPN range (26.0.0.0/8)
  and the local subnet, generates the Radmin network name/password and copies an invite for friends.
- `02.Switch Server.exe` (`Trocar.cs`): points your Seamless Co-op install to a RadminBell server
  (creates the game account on it and updates `account.dat` / the BBport launcher settings),
  and switches back to your previous server. Runs as administrator.
- `Common.cs`: shared code (dark theme, language EN/PT-BR, folders, Radmin VPN status, DPAPI, JSON).

The server itself is **not** part of this repository: it is the official, unchanged
`BBSC-0.0.2-Server.zip` from the [BBSC v0.0.2 release](https://github.com/CLSAMG22/BBSC/releases).

## Build

Requirements (Windows 10/11):
- [.NET SDK](https://dotnet.microsoft.com/download) 6 or newer (only for the Roslyn C# compiler)
- [.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net48) (reference assemblies)
- Git Bash (comes with [Git for Windows](https://git-scm.com/download/win)) or MSYS2

```bash
bash build.sh
```

Output: `out/01.RadminBell.exe` and `out/02.Switch Server.exe`.
No NuGet packages or third-party libraries; only .NET Framework 4.8 (already included in Windows).

## Credits

- CLS_AMG - [Seamless Co-op (BBSC)](https://github.com/CLSAMG22/BBSC) and its server
- Supermedo - BBport
- [Radmin VPN](https://www.radmin-vpn.com) - Famatech (not affiliated)

## License

MIT (this source code only). The BBSC server keeps its own license.
