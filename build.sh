#!/bin/bash
# Builds the 2 exes (.NET Framework 4.8, already included in Windows 10/11) into out/
# Needs: Git Bash (or MSYS2), .NET SDK 6+ (for the Roslyn compiler) and the .NET Framework 4.8 Developer Pack
cd "$(dirname "$0")"
R='C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
[ -d "$R" ] || { echo "Missing .NET Framework 4.8 Developer Pack: https://dotnet.microsoft.com/download/dotnet-framework/net48"; exit 1; }
CSC=$(ls -d "C:/Program Files/dotnet/sdk/"*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)
[ -n "$CSC" ] || { echo "Missing .NET SDK: https://dotnet.microsoft.com/download"; exit 1; }
mkdir -p out
build() { # out manifest files...
  out=$1; man=$2; shift 2
  { echo /nologo /noconfig /nostdlib /target:winexe /platform:anycpu /optimize+ /langversion:7.3 /utf8output
    echo /win32icon:icon.ico; echo /win32manifest:$man; echo /resource:logo.png,logo.png
    for a in mscorlib System System.Core System.Windows.Forms System.Drawing System.Security System.Web.Extensions Microsoft.CSharp; do echo "/r:\"$R/$a.dll\""; done
    echo "/out:\"$out\""; for f in "$@"; do echo "$f"; done; } > build.rsp
  MSYS2_ARG_CONV_EXCL='*' dotnet "$CSC" @build.rsp || exit 1
}
build "out/01.RadminBell.exe" painel.manifest Common.cs Painel.cs
build "out/02.Switch Server.exe" trocar.manifest Common.cs Trocar.cs
echo BUILD OK
