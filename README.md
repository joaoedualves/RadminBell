# RadminBell - Private Bloodborne Co-op Server

Source code of **RadminBell**: 2 small Windows apps to host your own private
[Seamless Co-op (BBSC)](https://github.com/CLSAMG22/BBSC) server for BBport, over Radmin VPN.

- `01.RadminBell.exe` (`Painel.cs`): control panel. Starts/stops the official BBSC server
  (shadnet + BBSC-GameServer), adds Windows Firewall rules limited to the Radmin VPN range (26.0.0.0/8)
  and the local subnet, generates the Radmin network name/password and copies an invite for friends.
- `02.Switch Server.exe` (`Trocar.cs`): points your Seamless Co-op install to a RadminBell server
  (creates the game account on it and updates `account.dat` / the BBport launcher settings),
  and switches back to your previous server. Asks for administrator only if the Seamless Co-op folder is protected.
- `Common.cs`: shared code (dark theme, language EN/PT-BR, folders, Radmin VPN status, JSON).

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
Release builds are made by GitHub Actions (`.github/workflows/build.yml`) from this repository.

## Uninstall

1. `02.Switch Server.exe` > **Back to my previous server** (restores your previous server and removes
   the account / launcher settings written by RadminBell).
2. `01.RadminBell.exe` > **Stop**.
3. Remove the firewall rules: Windows Defender Firewall > Advanced settings > Inbound Rules > delete the
   rules named `RadminBell - ...` (or, in PowerShell as administrator:
   `Remove-NetFirewallRule -DisplayName "RadminBell - *"`).
4. Delete the RadminBell folder and `%LOCALAPPDATA%\RadminBell`.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).
Only `01.RadminBell.exe` and `02.Switch Server.exe` are built and signed by this project; the files in the
`server` folder are the official BBSC server from its author.

Team roles:
- Committers and reviewers: [joaoedualves](https://github.com/joaoedualves)
- Approvers: [joaoedualves](https://github.com/joaoedualves)

Privacy policy: this program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it. It only talks to the
Radmin VPN on the same PC and to the co-op server address that the user types in.

## Credits

- CLS_AMG - [Seamless Co-op (BBSC)](https://github.com/CLSAMG22/BBSC) and its server
- Supermedo - BBport
- [Radmin VPN](https://www.radmin-vpn.com) - Famatech (not affiliated)

## License

MIT (this source code only). The BBSC server keeps its own license.
