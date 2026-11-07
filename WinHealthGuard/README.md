# WinHealthGuard

WinHealthGuard is a Windows tray app that watches this PC, logs crashes, and applies repairs that are meant to stop games from crashing or stuttering. It stays in the background and uses a light sampling interval.

Closing the window hides the app in the notification area. Crash watching continues. Double-click the tray icon to open the window again. Only one copy runs at a time; starting it again brings the open window forward.

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8, which current Windows installs already include
- Microsoft Edge, only for the NVIDIA driver search page

Some repairs ask for an administrator password. A few ask you to restart. The app does not restart the PC for you.

## Run

Build first, then start:

```powershell
d:\system_tools\WinHealthGuard\bin\WinHealthGuard.exe
```

Start hidden in the tray:

```powershell
d:\system_tools\WinHealthGuard\bin\WinHealthGuard.exe --tray
```

Settings can also register that tray command under the current user so Windows starts it at sign-in.

## Pages

**Home** shows a health score and the issues that can crash or stutter games. Each issue has one sentence and one button. The meters under the list show CPU, memory, free space on the system drive, and the power plan.

**Crash History** lists recent crashes and hangs. Pick a row to read what happened, which program was involved, and which repair matches it. Check looks at that event. Fix runs the matching repair. You can copy the detail, export a CSV, or open the log folder.

**Performance** lists repairs. Nothing on this page runs until you press its button. Many rows open a short list so you can see the current value and choose the new one before Apply. After a repair, the line at the top compares the health score with the reading taken just before it.

**Settings** controls startup, crash notices, the high-performance power boost while a game is running on AC, live meters, and the idle sample interval (15, 30, or 60 seconds). You can add extra game process names and names that must never be treated as games.

## Health score

The score starts at 100 and drops for problems found in the last 7 days, plus current disk, memory, and driver checks.

| Status | Meaning |
| --- | --- |
| Healthy | Score is 90 or higher, and no single problem is severe |
| Attention | Score is under 90 |
| Critical | Score is under 50, or one problem is severe on its own |

A successful repair is marked done and drops out of the score until the same kind of crash happens again. Opening Windows Update, Game Mode, a memory test, or the graphics vendor's app does not count as a finished repair. File cleanup and free disk space are measured again every time.

A graphics driver is called old only when the real graphics adapter's date is older than 6 months. Remote, basic, and virtual display adapters are ignored.

## What a repair will do

Repairs are specific. Examples:

- Remove a Windows boot limit that hides part of the installed RAM. BIOS video memory is left alone.
- Leave the page file on and let Windows choose its size. It is not turned off and not locked to a fixed size.
- Turn Fast Startup off. That is a performance choice. A sudden power loss is not treated as Fast Startup.
- Give the graphics driver a longer timeout, or turn hardware-accelerated GPU scheduling off or on.
- Check or repair Windows system files. The result says the files are healthy, lists files that were repaired, or lists files that could not be repaired. DISM followed by the system file check can take more than an hour.
- Schedule a disk check for the next restart. The check can take hours. The PC is not restarted for you.
- Flush DNS, or rebuild the network stack.
- Turn off Game DVR recording, browser background running, extra startup updaters, or SysMain.
- Clean temporary files and shader caches.

These actions are left to you, or are not done at all:

- The app does not change BIOS settings, flash drive firmware, uninstall antivirus, or take ownership of Windows registration keys.
- It does not download loose DLLs, run a display-driver cleaner, or uninstall a device from Device Manager.
- A power-loss or unexpected-shutdown event is explained. It is not used as a reason to update a driver or turn off Fast Startup.
- Safe Mode, a memory-test USB stick, memory-profile changes, and a power-supply inspection stay physical steps.

## Graphics drivers

Update checks Windows Update first, then the chip maker.

- An installer is downloaded only from `drivers.amd.com`, `us.download.nvidia.com`, `international.download.nvidia.com`, `download.nvidia.com`, or `downloadmirror.intel.com`.
- The file must have a valid signature from that vendor, or from Microsoft Windows Hardware Compatibility Publisher. Anything else is deleted and not run.
- A Windows Update driver is preferred over a vendor beta.
- NVIDIA and GeForce open the official driver page in a separate Edge window and type the card name into Manual Driver Search. Nothing is downloaded from that page. GeForce cards use the GeForce driver page. Other NVIDIA cards use the general NVIDIA driver page.
- Softpedia is a search page only. Choosing it shows a warning first. The page opens on Windows drivers, graphics boards, updates from the last 6 months, sorted by most downloaded. This app does not download that file.
- Qualcomm graphics and laptop-maker BIOS, power, touchpad, and hotkey packages are named with the maker's support site. They are not downloaded.

## Logs

Logs are written for the current user:

`%LocalAppData%\WinHealthGuard`

| File | Contents |
| --- | --- |
| `crashes.jsonl` | One crash or hang per line |
| `actions.log` | Repairs and other actions |
| `repairs.jsonl` | Score before and after a repair |
| `settings.json` | The choices on the Settings page |
| `app-error.log` | Unexpected errors inside the app |
| `sfc-capture.txt` | Last system-file check output, after you run that repair |
| `dism-capture.txt` | Last Windows-image repair output, after you run that repair |

Crash History has a Log folder button that opens this directory.

## Build

From PowerShell, with the app closed so the exe is not locked:

```powershell
powershell -ExecutionPolicy Bypass -File d:\system_tools\WinHealthGuard\build.ps1
```

The script uses the C# compiler that ships with Windows and writes `bin\WinHealthGuard.exe`. `WinHealthGuard.exe.config` is copied beside it so the window scales correctly on a high-DPI display.

A developer check that does not open the window:

```powershell
d:\system_tools\WinHealthGuard\bin\WinHealthGuard.exe --self-test
```

It writes `self-test.txt` in the `bin` folder and in the log folder.
