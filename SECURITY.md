# Security Policy · Güvenlik Politikası

## Supported versions · Desteklenen sürümler

Only the [latest release](https://github.com/HErkanuzun/BrokenDiskResizer/releases/latest) receives fixes.
Düzeltmeler yalnızca [son sürüme](https://github.com/HErkanuzun/BrokenDiskResizer/releases/latest) gelir.

## Reporting a vulnerability · Güvenlik açığı bildirme

Please **do not** open a public issue for security problems. Report them privately on GitHub:
**Security → Report a vulnerability** ([link](https://github.com/HErkanuzun/BrokenDiskResizer/security/advisories/new)).

Güvenlik sorunları için lütfen herkese açık bir konu (issue) **açma**. GitHub'da gizli olarak bildir:
**Security → Report a vulnerability** ([bağlantı](https://github.com/HErkanuzun/BrokenDiskResizer/security/advisories/new)).

## What the app does on your computer · Program bilgisayarında ne yapar

| | |
|---|---|
| Administrator rights · Yönetici izni | Always. Reading a disk sector by sector and partitioning need it. · Her zaman. Diski sektör sektör okumak ve bölümlemek için gerekir. |
| Disk access · Disk erişimi | Scans open the disk **read-only** (`GENERIC_READ`) and never write. Writing happens only when you press Apply and type the confirmation word; it runs `diskpart` (`clean`, `convert gpt`, `create partition`, `format quick`) on the selected disk. The Windows/boot disk is refused, and the disk is re-identified by serial number right before. · Tarama diski **salt okunur** açar, hiç yazmaz. Yazma yalnızca Uygula'ya basıp onay kelimesini yazınca olur ve seçili diskte `diskpart` çalıştırılır. Windows/önyükleme diski reddedilir; disk uygulamadan hemen önce seri numarasıyla yeniden doğrulanır. |
| Network · Ağ | Only when you press "Check for updates" in About: one request to the GitHub API. No telemetry. · Yalnızca Hakkında'da "Güncellemeleri denetle"ye basınca GitHub API'sine tek istek. Veri toplamaz. |
| Files · Dosyalar | `%AppData%\BrokenDiskResizer` (maps, SMART history, settings) and a temporary diskpart script in `%TEMP%` that is deleted right after use. · Harita, SMART geçmişi, ayarlar; bir de kullanılıp hemen silinen geçici diskpart betiği. |
| Registry · Kayıt defteri | Only the installer's own uninstall entry. · Sadece kurulum paketinin kaldırma kaydı. |

Uninstalling from Windows "Apps" removes the program and asks whether to delete `%AppData%\BrokenDiskResizer`.
Windows "Uygulamalar"dan kaldırınca program silinir ve veri klasörünün de silinip silinmeyeceği sorulur.

## Verifying releases · Sürümleri doğrulama

Release files are built from this repository by GitHub Actions. Each release includes `SHA256SUMS.txt`:

```powershell
Get-FileHash .\BrokenDiskResizer-1.0-Setup.exe -Algorithm SHA256
```
