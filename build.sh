#!/bin/bash
# Builds RadminBell.exe (.NET Framework 4.8, already included in Windows 10/11) into out/
# Needs: Git Bash (or MSYS2), .NET SDK 6+ (for the Roslyn compiler), the .NET Framework 4.8 Developer Pack
# and Windows PowerShell (converts the images in imagens.ps1)
cd "$(dirname "$0")"
R='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
[ -d "$R" ] || { echo "Missing .NET Framework 4.8 Developer Pack: https://dotnet.microsoft.com/download/dotnet-framework/net48"; exit 1; }
CSC=$(ls -d "C:/Program Files/dotnet/sdk/"*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)
[ -n "$CSC" ] || { echo "Missing .NET SDK: https://dotnet.microsoft.com/download"; exit 1; }
mkdir -p out/img
powershell.exe -NoProfile -ExecutionPolicy Bypass -File imagens.ps1 -Out out/img || exit 1
{ echo /nologo /noconfig /nostdlib /target:winexe /platform:anycpu /optimize+ /langversion:7.3 /utf8output
  echo /win32icon:icon.ico; echo /win32manifest:app.manifest
  for i in logo host guest; do echo "/resource:out/img/$i.rgba,$i.rgba"; done
  for a in mscorlib System System.Core System.Windows.Forms System.Drawing System.Security System.Web.Extensions Microsoft.CSharp; do echo "/r:\"$R/$a.dll\""; done
  echo '/out:"out/RadminBell.exe"'; echo Common.cs App.cs Host.cs Guest.cs; } > build.rsp
MSYS2_ARG_CONV_EXCL='*' dotnet "$CSC" @build.rsp || exit 1
echo BUILD OK
