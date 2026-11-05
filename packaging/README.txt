Broken Disk Resizer
===================
Bozuk sektörleri olan bir diskin hatalı bölgesini bulur ve kullanım dışı bırakır.
Finds the broken area of a hard disk and keeps it out of use.
https://github.com/HErkanuzun/BrokenDiskResizer

TÜRKÇE
------
1. BrokenDiskResizer.exe'yi aç (yönetici izni ister; diskleri okumak için gerekir).
2. Diski seç, "Taramayı başlat" ile hızlı tarama yap (diske hiçbir şey yazmaz).
   Elinde format log'u varsa "Format/tarama log'u içe aktar…" ile ekleyebilirsin.
3. Sağdaki "Açılacak bölümler"e bak, güvenlik payını ayarla.
4. "Uygula" → SİL yaz → onayla. Disk silinir; sadece sağlam kısımlara bölüm açılır,
   bozuk bölge ayrılmamış alan olarak kalır.

Bu araç diski onarmaz. Bozuk sektörleri artan bir disk genelde ölmektedir;
üzerine önemli hiçbir şey koyma.

Veriler (harita, SMART geçmişi, ayarlar): %AppData%\BrokenDiskResizer
Taşınabilir sürümü kaldırmak için bu klasörü ve yukarıdaki veri klasörünü silmen yeterli.

ENGLISH
-------
1. Open BrokenDiskResizer.exe (it asks for administrator rights to read disks).
2. Pick the disk and press "Start scan" for a quick scan (it never writes to the disk).
   You can also import a format log with "Import format/scan log…".
3. Check "Partitions to create" on the right and adjust the safety margin.
4. "Apply" → type ERASE → confirm. The disk is erased and partitions are created only on
   the healthy parts; the bad area stays unallocated.

This tool doesn't repair the disk. A disk with growing bad sectors is usually dying;
don't keep anything important on it.

Data (maps, SMART history, settings): %AppData%\BrokenDiskResizer
To remove the portable version, delete this folder and the data folder above.

LICENSE.txt  MIT License
