<p align="center">
  <img src="assets/VictusModeSwitch.png" width="72" alt="Victus Mode Switch icon">
</p>

<h1 align="center">Victus Mode Switch</h1>

<p align="center">
  A lightweight Windows 11 tray app that replaces the basic performance controls of OMEN Gaming Hub.
</p>

<p align="center">
  <a href="https://github.com/Juanbod/VictusModeSwitch/actions/workflows/build.yml"><img src="https://github.com/Juanbod/VictusModeSwitch/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  <a href="https://github.com/Juanbod/VictusModeSwitch/releases/latest"><img src="https://img.shields.io/github/v/release/Juanbod/VictusModeSwitch" alt="Latest release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Juanbod/VictusModeSwitch" alt="MIT license"></a>
</p>

> [!CAUTION]
> This project was developed with AI assistance and tested on a single laptop. It calls an undocumented HP BIOS interface. Installation and use are entirely at your own risk. Read the [full disclaimer](DISCLAIMER.md) before installing.

[Русская версия](docs/README.ru.md)

![Victus Mode Switch general settings](docs/images/settings-general.png)

## What it does

- One diamond-button press switches between **Standard** and **Performance**.
- Two quick presses toggle **Max Fan**.
- Three quick presses enable **Eco**.
- A user-defined global keyboard shortcut can mirror the same one-, two-, and three-press gestures.
- The tray menu provides direct, silent mode selection.
- The settings UI follows the Windows light/dark theme and supports English, Russian, Ukrainian, or the Windows display language.
- A General settings toggle controls whether the tray app starts after signing in to Windows; the choice is preserved across updates.
- Optional Windows power tuning adjusts CPU, cooling, PCIe, Wi-Fi, and USB power preferences and restores the original values in Standard mode.
- Optional Eco controls can independently limit the built-in display to 60 Hz, cap NVIDIA globally at 60 FPS, and disable CPU Turbo Boost. All three are off by default and restore the captured values after leaving Eco.
- An opt-in General setting pauses five optional HP HSA app services while Victus Mode Switch runs and restores only the services it stopped.
- The built-in updater checks GitHub Releases and verifies the installer SHA-256 checksum before it can run.

The public gesture mapping uses a triple press for Eco. HP WMI emits one pulse without a release event; the dedicated keyboard route is kept focused on reliable press detection, including immediately after sign-in.

To add the keyboard alternative, open **Settings > General**, click **Not assigned**, and press a combination containing `Ctrl`, `Alt`, `Shift`, or `Win`. Press `Esc` to cancel; use the delete button or press `Backspace`/`Delete` without modifiers to clear it. The shortcut is unassigned by default.

## Supported hardware

The current safety allowlist contains only:

| Device | Required value |
|---|---|
| Tested model | HP Victus 15-fa0020ua |
| System board | `8A4F` |
| HP Thermal Policy | `V0` |

BIOS writes are blocked when the system board or thermal policy does not match. Similar Victus models are **not assumed compatible**. Open an issue with diagnostic information before proposing support for another board.

## Install

1. Download [`VictusModeSwitch-Setup.exe`](https://github.com/Juanbod/VictusModeSwitch/releases/latest/download/VictusModeSwitch-Setup.exe) and its [SHA-256 file](https://github.com/Juanbod/VictusModeSwitch/releases/latest/download/VictusModeSwitch-Setup.exe.sha256).
2. Verify the checksum:

   ```powershell
   (Get-FileHash .\VictusModeSwitch-Setup.exe -Algorithm SHA256).Hash
   Get-Content .\VictusModeSwitch-Setup.exe.sha256
   ```

3. Run the installer and read the hardware warning.
4. Approve the one-time UAC prompt used to validate the laptop, install the protected BIOS broker under `Program Files`, and register its short elevated task.

The release installer is currently **not code-signed**, so Windows may show an unknown-publisher warning. A SHA-256 checksum verifies download integrity but is not a replacement for Authenticode publisher verification.

The optional setup checkbox pauses OMEN Gaming Hub background helpers. OMEN Gaming Hub is not uninstalled, the HP Omen HSA hardware service remains available, and saved task/background states are restored on uninstall.

## Minimum HP software

Victus Mode Switch talks to the HP WMI BIOS interface and the diamond-button event directly. It does not call OMEN Gaming Hub, HP Support Assistant, or the HP HSA user-mode services.

The conservative minimum retained on the tested laptop is:

- **HP Application Driver** (`ACPI\HPIC0003`);
- **HP Omen Driver** (`ACPI\HPIC0004`);
- the NVIDIA display driver, only when the optional 60 FPS limit is used.

If no other HP application is needed, OMEN Gaming Hub, HP Support Solutions Framework, HP Insights/Touchpoint Analytics, and the `HPAppHelperCap`, `HPDiagsCap`, `HPNetworkCap`, `HPOmenCap`, and `HPSysInfoCap` services are not required by Victus Mode Switch. During measurements those five running helpers used roughly 200 MB of memory together. The **Pause optional HP services** setting stops this fixed allowlist, checks locally every 15 seconds for restarts, and restores only services it captured as running. Disabling them can interrupt HP Support Assistant diagnostics or other HP features. Unrelated chipset, ACPI, keyboard, and graphics drivers remain untouched.

## Modes

| Mode | HP BIOS | Optional Windows tuning | Optional Eco controls | Max Fan |
|---|---|---|---|---|
| Eco | Standard thermal policy | Best efficiency, configurable CPU limits, device power saving | 60 Hz, 60 FPS, and Turbo Boost off, independently selectable | Turns off when entering Eco |
| Standard | Standard thermal policy | Restores the captured Windows values | Restores captured values | Preserved |
| Performance | Performance thermal policy | Best performance, CPU/EPP and device power saving tuned for speed | Restores captured values | Preserved |

Max Fan can still be toggled independently after entering any mode.

![Windows power settings](docs/images/settings-power.png)

## Updates and privacy

Automatic checks are enabled by default and run at most once per day against the public GitHub Releases API. There is no telemetry, account, analytics service, network polling loop, or embedded browser. When HP service suppression is enabled, a local status check runs every 15 seconds. Update installation always requires a user click.

For each update the app downloads the installer and checksum from the same GitHub Release, verifies SHA-256, exits fully, verifies the file again in a helper process, and only then starts setup.

## Architecture

- `.NET 10` Windows Forms, self-contained `win-x64` release
- Verified dedicated-key scan-code hook for immediate input, with HP WMI `EventID=29`, `EventData=8613` as a fallback
- Native Windows `RegisterHotKey` shortcut with repeat suppression for the user-defined alternative
- Per-user tray process with normal privileges
- Direct Windows display APIs, NVIDIA NVAPI, and Windows power APIs for the optional Eco controls
- Protected PowerShell broker under `Program Files`, launched without a console window, that accepts only fixed mode/fan requests and a five-service HP allowlist, then runs briefly at highest privileges
- Inno Setup per-user installer
- GitHub Actions build, tests, installer packaging, checksums, and Releases

Idle resource use on the tested system is approximately 15-25 MB of private memory and effectively zero CPU while waiting for events.

## BIOS protocol research

The documented [HP OMEN BIOS communication path](docs/hp-bios-protocol.md) covers the Gaming Hub HSA/RPC layer, native HP WMI envelope, command map, `8A4F` capability block, safety boundaries, and promising read-only features. A [Russian version](docs/hp-bios-protocol.ru.md) is also available.

To collect a shareable hardware/interface snapshot without invoking any BIOS method:

```powershell
.\scripts\Inspect-HpBiosInterface.ps1 -OutputPath .\artifacts\hp-bios-interface.json
```

## Build

Requirements: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), and [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
dotnet test .\VictusModeSwitch.slnx -c Release
.\scripts\Build-Release.ps1 -Version 2.4.1
```

Artifacts are written to `artifacts/`. For hardware diagnostics:

```powershell
.\artifacts\publish\VictusModeSwitch.exe --diagnose 20
```

Normal mode switching requires the installer-created protected broker task. Do not experiment with command IDs or remove the hardware allowlist on a machine you cannot afford to lose.

## Uninstall

Use **Settings > Apps > Installed apps > Victus Mode Switch > Uninstall**. Removal switches to Standard, disables Max Fan, restores paused HP services and captured Windows power, display refresh, NVIDIA frame-limit, and Turbo Boost values, removes the startup entry and elevated BIOS task, and restores saved OMEN background settings. User settings and logs remain in `%LOCALAPPDATA%\VictusModeSwitch` unless removed manually.

## License and notice

Source code is available under the [MIT License](LICENSE). The hardware disclaimer and limitation of liability still apply. This project is independent and is not affiliated with, endorsed by, or supported by HP.
