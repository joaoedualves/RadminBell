# RadminBell - Private Bloodborne Co-op Server

Source code of **RadminBell**: a small Windows app to host or join your own private
[Seamless Co-op (BBSC)](https://github.com/CLSAMG22/BBSC) server for BBport, over Radmin VPN.

One app, `RadminBell.exe`. On first open you pick the language and **I'm the host** or **I'm the guest**
(you can switch later from the link at the bottom of the window).

- `Host.cs` - host screen: starts/stops the official BBSC server (shadnet + BBSC-GameServer), adds Windows
  Firewall rules limited to the Radmin VPN range (26.0.0.0/8), helps create the Radmin network, points the
  host's own game to the server and makes an invite code for friends. Shows who is playing.
- `Guest.cs` - guest screen: reads the invite (or finds the host on the Radmin network), helps join the Radmin
  network, points your Seamless Co-op install to the host's server (creates the game account on it and
  updates `account.dat` / the BBport launcher settings) and switches back to your previous server.
- `App.cs` - start-up, Host/Guest choice, invite code, player list, tray icon.
- `Common.cs` - shared code (dark theme, language EN/PT-BR, folders, Radmin VPN status, JSON).

Closing RadminBell (tray icon > Close) also stops the server.

The server itself is **not** part of this repository: it is the official, unchanged
`BBSC-0.0.2-Server.zip` from the [BBSC v0.0.2 release](https://github.com/CLSAMG22/BBSC/releases).

## Build

Requirements (Windows 10/11):
- [.NET SDK](https://dotnet.microsoft.com/download) 6 or newer (only for the Roslyn C# compiler)
- [.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net48) (reference assemblies)
- Git Bash (comes with [Git for Windows](https://git-scm.com/download/win)) or MSYS2
- Windows PowerShell (converts the images with `imagens.ps1`)

```bash
bash build.sh
```

Output: `out/RadminBell.exe`.
No NuGet packages or third-party libraries; only .NET Framework 4.8 (already included in Windows).
Release builds are made by GitHub Actions (`.github/workflows/build.yml`) from this repository.

## Uninstall

1. RadminBell as guest > **Back to my previous server** (restores your previous server and removes
   the account / launcher settings written by RadminBell).
2. Close RadminBell (tray icon > Close; this also stops the server).
3. Remove the firewall rules: Windows Defender Firewall > Advanced settings > Inbound Rules > delete the
   rules named `RadminBell - ...` (or, in PowerShell as administrator:
   `Remove-NetFirewallRule -DisplayName "RadminBell - *"`).
4. Delete the RadminBell folder and `%LOCALAPPDATA%\RadminBell`.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).
Only `RadminBell.exe` is built and signed by this project; the files in the
`server` folder are the official BBSC server from its author.

Team roles:
- Committers and reviewers: [joaoedualves](https://github.com/joaoedualves)
- Approvers: [joaoedualves](https://github.com/joaoedualves)

Privacy policy: this program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it. It only talks to the
Radmin VPN on the same PC, to the co-op server address that the user types in or pastes as an invite,
and to other RadminBell apps inside the user's own Radmin VPN network (to find the host and list players).

## Credits

- CLS_AMG - [Seamless Co-op (BBSC)](https://github.com/CLSAMG22/BBSC) and its server
- Supermedo - BBport
- [Radmin VPN](https://www.radmin-vpn.com) - Famatech (not affiliated)

## License

MIT (this source code only). The BBSC server keeps its own license.
