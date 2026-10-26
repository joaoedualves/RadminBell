# Converte os PNG (logo, Host, Guest) para o formato que vai dentro do exe: pixels sem compressao
# (int32 largura, int32 altura, depois BGRA linha a linha). Corta as bordas transparentes e reduz
# para a altura usada na tela (com folga para monitor 200%).
param([string]$Out = "out")
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null

function Convert-Img([string]$src, [string]$dst, [int]$height) {
    $img = [Drawing.Bitmap]::FromFile((Resolve-Path $src))
    # corta as bordas transparentes
    $minX = $img.Width; $minY = $img.Height; $maxX = -1; $maxY = -1
    $rect = New-Object Drawing.Rectangle 0, 0, $img.Width, $img.Height
    $data = $img.LockBits($rect, 'ReadOnly', 'Format32bppArgb')
    $bytes = New-Object byte[] ($data.Stride * $img.Height)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $img.UnlockBits($data)
    for ($y = 0; $y -lt $img.Height; $y++) {
        for ($x = 0; $x -lt $img.Width; $x++) {
            if ($bytes[$y * $data.Stride + $x * 4 + 3] -gt 8) {
                if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { $minX = 0; $minY = 0; $maxX = $img.Width - 1; $maxY = $img.Height - 1 }
    $cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1
    $w = [Math]::Max(1, [int][Math]::Round($cw * $height / $ch))
    $bmp = New-Object Drawing.Bitmap $w, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.DrawImage($img, (New-Object Drawing.Rectangle 0, 0, $w, $height), $minX, $minY, $cw, $ch, 'Pixel')
    $g.Dispose(); $img.Dispose()
    $d2 = $bmp.LockBits((New-Object Drawing.Rectangle 0, 0, $w, $height), 'ReadOnly', 'Format32bppArgb')
    $px = New-Object byte[] ($w * 4 * $height)
    for ($y = 0; $y -lt $height; $y++) { [Runtime.InteropServices.Marshal]::Copy([IntPtr]($d2.Scan0.ToInt64() + $y * $d2.Stride), $px, $y * $w * 4, $w * 4) }
    $bmp.UnlockBits($d2); $bmp.Dispose()
    $fs = [IO.File]::Create((Join-Path $Out $dst))
    $bw = New-Object IO.BinaryWriter $fs
    $bw.Write([int]$w); $bw.Write([int]$height); $bw.Write($px)
    $bw.Close()
}

Convert-Img "logo.png"  "logo.rgba"  64
Convert-Img "Host.png"  "host.rgba"  80
Convert-Img "Guest.png" "guest.rgba" 80
