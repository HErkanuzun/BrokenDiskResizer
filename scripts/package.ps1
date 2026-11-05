# Broken Disk Resizer - sürüm paketleri
# build.bat'ten sonra çalışır; dist\ klasörüne şunları üretir:
#   BrokenDiskResizer-<sürüm>-Setup.exe     kurulum paketi (önerilen; varsa günceller, yoksa kurar)
#   BrokenDiskResizer-<sürüm>-Portable.zip  kurulumsuz kullanım: program, okuma notu, lisans
#   SHA256SUMS.txt                          dosyaların SHA-256 özetleri (indirilen dosyayı doğrulamak için)
# Kullanım: powershell -ExecutionPolicy Bypass -File scripts\package.ps1 -Version 1.0 [-Iscc <ISCC.exe yolu>]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Iscc = ''
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'bin'
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'BrokenDiskResizer'

if (-not (Test-Path (Join-Path $bin 'BrokenDiskResizer.exe'))) { throw 'bin\BrokenDiskResizer.exe yok; önce build.bat çalıştır.' }

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

# ------------------------------------------------------------------ taşınabilir ZIP
Copy-Item (Join-Path $bin 'BrokenDiskResizer.exe') $stage
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')
Copy-Item (Join-Path $root 'packaging\README.txt') $stage
Compress-Archive -Path $stage -DestinationPath (Join-Path $dist "BrokenDiskResizer-$Version-Portable.zip") -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

# ------------------------------------------------------------------ kurulum paketi
if ($Iscc -eq '') {
    $c = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($c) { $Iscc = $c.Source }
    else {
        foreach ($p in "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") {
            if (Test-Path $p) { $Iscc = $p; break }
        }
    }
}
if ($Iscc -eq '' -or -not (Test-Path $Iscc)) { throw 'Inno Setup 6 (ISCC.exe) bulunamadı; -Iscc ile yolunu ver.' }
& $Iscc /Q "/DAppVersion=$Version" (Join-Path $root 'installer\BrokenDiskResizer.iss')
if ($LASTEXITCODE -ne 0) { throw "Kurulum paketi derlenemedi ($LASTEXITCODE)." }

# ------------------------------------------------------------------ özetler
$sums = foreach ($f in Get-ChildItem $dist -File | Sort-Object Name) {
    "$((Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($f.Name)"
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), [string[]]$sums)

Write-Host ''
Write-Host "Paketler hazır (dist\):"
$sums | ForEach-Object { Write-Host "  $_" }
