# Verification

Test environment: Windows x64, .NET SDK 10.0.402. The same commands run on `windows-latest` in `.github/workflows/build-windows.yml`.

## Automated coverage

**39 core checks** cover exact/missed/disabled/recurring alarm times, deduplication, DST gaps and repeated hours, simultaneous alarm sessions, close-state transitions, bounded escape counts, three puzzle rounds, randomized sequences, retry/reset behavior, CRUD persistence, active-session recovery, backup recovery, invalid settings, distinct compositions, looping boundaries, peak limiting, sustained industrial output, and measured sub-bass fundamentals in all three presets.

**27 WPF/Windows integration checks** cover:

- Actual editor create/edit/delete controls and settings reload.
- Rendered UI snapshots at 100%, 125%, 150%, and 200% resolution scales. These are rendered scaling checks, not physical monitor-DPI changes.
- Calendar/time task XML, correct executable paths, explicit spring-gap compensation triggers, and preserved weekly wall times.
- Real Task Scheduler registration, inspecting the persisted wake flag, and deletion of a temporary alarm task. Temporary test tasks/folders are removed.
- Ringing, title-bar button behavior through its real click handler, native `WM_SYSCOMMAND / SC_CLOSE` routing, WPF `Window.Closing` cancellation, puzzle reset, embarrassment activation, and saved active/escape state.
- Completing the puzzle stops playback and clears prominence; ordinary closing succeeds afterward.
- Each NAudio sample provider streams through and beyond a complete composition without EOF or an empty buffer.

Native `SC_CLOSE` is the close command used by Alt+F4 and the system menu. Automated integration sends that command to the real HWND and verifies it routes through the WPF close gate. The title button also runs the actual handler. Physical taskbar and keyboard input can additionally be checked manually using the procedure below.

Build, test, and packaging errors fail the build script and CI job. Inspect the repository's Actions run for the authoritative CI outcome and its downloadable evidence. No success is inferred from source generation alone.

## Hardware-dependent checks

- Actual overnight wake from the device's supported sleep/hibernate modes, battery and lid policies, firmware wake timers, and Modern Standby behavior.
- Audible loudness and Skullcandy Crusher haptic response. Digital tests verify signal energy and a fixed peak ceiling, not acoustic pressure or a headphone actuator's behavior.
- Physical monitor-DPI changes and multi-monitor transitions beyond the rendered scaling checks.

## Manual regression procedure

1. Launch `RISE.exe --data-dir <disposable folder>` for an isolated session without wake tasks. Add a future alarm, edit it, toggle it, restart, verify it remains, then delete it.
2. Set an alarm one or two minutes ahead; leave the app running and verify the exact minute triggers it. Try a second launch while it rings: it should bring the existing instance forward, with one audio output.
3. Preview all three presets. Check volume/intensity, spectrum motion, distinct timbres, and sub-bass on the intended output device.
4. Use **TEST ALARM**. While ringing, click X, press Alt+F4, and use ordinary taskbar Close. The app should remain open, reset to round one, display the escape warning, and play the fanfare. Force termination must remain available.
5. Intentionally make a puzzle error, then finish all three rounds using mouse/keyboard. Audio must stop immediately; the window must close normally.
6. Relaunch after force-terminating an unacknowledged isolated session. Verify the active alarm and escape state resume. Complete it to stop audio.
7. For physical wake, use the normal app data directory, choose a near-future alarm, check the wake diagnostic, and put Windows to sleep while remaining signed in. Confirm wake and playback on that hardware. Disable/delete test alarms afterward to remove their tasks.
