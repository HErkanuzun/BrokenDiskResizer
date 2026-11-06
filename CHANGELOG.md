# Changelog

All notable changes to Broken Disk Resizer. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.1] - 2026-11-06

### Changed
- Logo, name and the language / About buttons sit in a fixed header at the top, in the same place in every Broken app. They no longer move when you switch pages, and content always starts at the top instead of being centred vertically.
- New design, the same across the whole Broken family: a soft colour background, glass cards and buttons, and the window frame in the same style (no white Windows title bar; minimize / maximize / close are drawn by the app). Snapping, resizing from the edges, shadow and rounded corners still work. Dialogs use the same frame.

## [1.0] - 2026-10-23

First release as an installable app.

### Added
- **Installer** (`BrokenDiskResizer-1.0-Setup.exe`): installs to Program Files, adds a Start menu entry (optional desktop icon) and appears with its version in Windows "Apps". Updating closes a running copy first (a running scan is stopped and saved). Uninstalling asks whether to delete the maps, SMART history and settings.
- **One-click packaging** (`paketle.bat`): builds the exe, installs Inno Setup with winget if needed, and produces the Setup, a portable ZIP and `SHA256SUMS.txt`.
- **Disk health (SMART)** card: reallocated, pending and uncorrectable sector counts, temperature and power-on hours, and how much each count has grown **since the first check**. The history is kept per disk.
- **Known bad areas are never read again**: imported log areas, manually marked areas and places where the disk dropped out are skipped (with a 1 GB margin) in every scan, so a scan can't knock the disk off Windows twice.
- A place where the disk drops out during a scan is added to the bad areas automatically ("dropout").
- Taskbar progress during scans, the taskbar button flashes when a scan ends, and Windows doesn't go to sleep while scanning.
- The disk list refreshes by itself when a disk is plugged in or removed.
- **About** window with version, license, data folder and an on-demand update check.
- A short **welcome guide** on first start.
- **Save report…**: disk, SMART, bad areas, plan and log in one text file (useful for bug reports).
- Asks before closing while a scan runs; can't close while partitioning. Remembers the window position.
- Opening the app a second time brings the existing window to the front. Keyboard: Esc clears the selection, F5 refreshes disks.

## [0.1] - 2026-10-23

First working version: read-only quick/full scan, format-log import, platter map with selection, safety margin, partitioning with diskpart.
