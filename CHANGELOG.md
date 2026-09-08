# Changelog

## 2.0.0 - 2026-09-08

- Reduced the app to Eco, Standard, Performance, Max Fan, tray controls, and diamond-button gestures.
- Removed Gaming mode, memory trimming, automation rules, user scripts, display refresh changes, Turbo Boost overrides, and the proprietary OMEN NVIDIA DLL dependency.
- Rebuilt settings with a Windows 11-style navigation layout and system light/dark theme support.
- Added English, Russian, Ukrainian, and Windows-language selection.
- Added a self-contained Inno Setup installer with reversible OMEN background-helper handling.
- Added opt-in Windows power-plan tuning with exact captured-value restoration.
- Added GitHub Releases update checks with mandatory SHA-256 verification.
- Isolated elevated BIOS writes in a constrained PowerShell broker protected under `Program Files`.
- Added a hidden Windows Script Host launcher so BIOS operations do not flash a console window.
- Added migration from 1.x settings, automated tests, and GitHub Actions build/release workflows.
