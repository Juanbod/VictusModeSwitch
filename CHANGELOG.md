# Changelog

## Unreleased

## 2.4.4 - 2026-09-12

- Retry the saved BIOS state for up to five minutes while HP hardware interfaces finish cold-boot initialization.
- Reconnect the diamond-button HP WMI listener with increasing delays instead of giving up after one attempt.
- Restore services left paused by an interrupted session before hardware initialization, then defer optional HP service suppression until BIOS and WMI are ready.
- Record UI, background-task, process-lifecycle, and periodic health diagnostics for startup failures that previously left no crash evidence.

## 2.4.3 - 2026-09-11

- Accept verified diamond-key WMI events even when the low-level keyboard hook installed successfully but receives no usable scan code.
- Deduplicate near-simultaneous keyboard and WMI copies while preserving rapid double- and triple-press gestures.
- Subscribe only to the verified HP OMEN key event instead of processing every HP hotkey event.

## 2.4.2 - 2026-09-11

- Relaunch the tray application from Inno Setup's final run phase after installs and silent automatic updates complete.
- Keep hardware and startup configuration separate from process launch so setup cannot leave the diamond key without a listener.

## 2.4.1 - 2026-09-09

- Capture the dedicated diamond key through its verified keyboard scan code from the start of the tray process, with HP WMI retained as a fallback.
- Prevent the delayed startup reapply from overwriting a mode or Max Fan command already requested by the user.
- Apply the saved BIOS state before pausing optional HP services so service cleanup cannot delay cold-start hardware initialization.
- Retry short-lived BIOS task and WMI communication failures with a small bounded backoff.

## 2.4.0 - 2026-09-09

- Add a General settings toggle that immediately enables or disables per-user Windows startup without UAC.
- Reflect startup entries disabled through Windows Task Manager and clear its cached state when re-enabled.
- Preserve the selected startup behavior across repairs, reinstalls, and automatic updates.
- Wait for the running tray process to exit fully before replacing application files during an update.

## 2.3.0 - 2026-09-08

- Add an opt-in controller that pauses five optional HP HSA app services while Victus Mode Switch runs.
- Keep the HP Application and HP Omen hardware drivers active for BIOS access and diamond-button events.
- Check locally every 15 seconds and stop an allowed helper again if another HP app restarts it.
- Restore only services captured as running before suppression, including recovery after a crash, update, or uninstall.
- Constrain elevated service operations to a fixed five-service allowlist and keep them console-free.
- Queue and serialize privileged requests so startup service handling cannot drop a simultaneous BIOS command.

## 2.2.0 - 2026-09-08

- Add a configurable global keyboard shortcut that mirrors diamond-button single, double, and triple presses.
- Record or clear the shortcut directly in General settings, with Windows conflict detection and automatic rollback.
- Use native `RegisterHotKey` repeat suppression without a persistent low-level keyboard hook.
- Keep an open Settings window synchronized when a diamond-button or keyboard gesture changes the mode or Max Fan.

## 2.1.0 - 2026-09-08

- Add independent Eco options for a 60 Hz built-in display limit, a 60 FPS NVIDIA limit, and disabling CPU Turbo Boost.
- Capture and restore every changed display, NVIDIA, and Windows power value independently.
- Use the NVIDIA driver's native NVAPI interface without an OMEN Gaming Hub DLL dependency.
- Align page headings with their subtitles and reserve enough height for descenders at scaled DPI.
- Document the minimum HP software required by Victus Mode Switch.
- Document the OMEN Gaming Hub to HP BIOS communication path, known command map, and board `8A4F` capability data.
- Add a read-only interface inventory script that never invokes a BIOS method.

## 2.0.2 - 2026-09-08

- Restore saved OMEN scheduled tasks correctly when uninstalling through Windows PowerShell 5.1.
- Make hardware cleanup retryable after an interrupted or partially completed uninstall.

## 2.0.1 - 2026-09-08

- Show immediate pending feedback after a diamond-button gesture, then confirm it when the hardware operation finishes.
- Reduce the multi-press recognition window from 420 ms to 320 ms.
- Move the local WMI safety check off the UI thread and cache a successful board lookup for faster subsequent switches.
- Stop launching the tray process with Windows' hidden-window flag, and reliably show, restore, and foreground Settings.
- Give page and setting headings enough vertical space at scaled DPI so glyphs are not clipped.
- Replace the compressed documentation screenshots with full-resolution lossless PNG images.

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
