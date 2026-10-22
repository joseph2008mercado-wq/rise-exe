# Verification

Test environment: Windows x64, .NET SDK 10.0.402. The same commands run on `windows-latest` in `.github/workflows/build-windows.yml`.

## Automated coverage

**57 core checks** cover exact/missed/disabled/recurring alarm times, deduplication, DST gaps and repeated hours, AM/PM conversion (including noon and midnight), automatic next-occurrence dates, timer deadlines/pause/resume/restart/missed recovery, legacy settings compatibility, simultaneous sessions, close-state transitions, bounded escape counts, a single five-tile puzzle, randomized sequences, retry/reset behavior, CRUD persistence, active-session recovery, backup recovery, invalid settings, distinct compositions, looping boundaries, peak limiting, sustained industrial output, measured sub-bass fundamentals, and four distinct peak-limited bell notes.

**84 WPF/Windows checks**, plus one silent native WASAPI playback check when an output endpoint is available, cover:

- Actual editor create/edit/delete controls, AM/PM time entry, timer start/pause/resume/edit/delete, and settings reload.
- Rendered UI snapshots at 100%, 125%, 150%, and 200% resolution scales. These are rendered scaling checks, not physical monitor-DPI changes.
- Calendar/time task XML, timer UTC deadlines, correct executable paths, explicit spring-gap compensation triggers, and preserved weekly wall times.
- Real Task Scheduler registration for alarms and timers, inspecting the persisted wake flag, removing a timer's task when paused, and deletion of a temporary alarm task. Temporary test tasks/folders are removed.
- Ringing, title-bar button behavior through its real click handler, native `WM_SYSCOMMAND / SC_CLOSE` routing, WPF `Window.Closing` cancellation, puzzle reset, embarrassment activation, and saved active/escape state.
- Completing the one-sequence puzzle stops the alarm and clears prominence; ordinary closing succeeds afterward.
- Each NAudio sample provider streams through and beyond a complete composition without EOF or an empty buffer.
- Chimes mix audibly into each preset under the shared limiter. The final chime survives alarm dismissal, then reaches EOF without restarting the alarm.
- Nineteen hardware-free routing checks exercise the actual AudioEngine with injected devices and outputs: audible headphones, disconnect/default change, initial and mid-alarm zero/mute, RISE mixer mute, latched fallback, volume enforcement, original volume restoration, no speakers/recovery, previews, output initialization failure/retry, and ordinary speaker playback.
- Native Windows Core Audio enumeration reads endpoint classifications/levels without altering them. A zero-sample WASAPI stream checks native initialization/resampling if a default device exists; runners without an endpoint explicitly record that hardware playback is unavailable.
- The uninstall cleanup removes an isolated real wake-task folder and safely handles a second cleanup with the folder already absent.

**Nine installer checks** install into a unique workspace directory, verify app registration in an isolated HKCU test key, create and inspect both real `.lnk` shortcuts with their targets/working directories, uninstall, and verify unrelated files survive. Test mode redirects shortcuts inside the test directory and never changes real desktop/Start menu links or real wake tasks. Setup uses a SHA-256-pinned NSIS 3.12 compiler from the official SourceForge project. The workflow publishes only after both app and installer checks succeed.

Native `SC_CLOSE` is the close command used by Alt+F4 and the system menu. Automated integration sends that command to the real HWND and verifies it routes through the WPF close gate. The title button also runs the actual handler. Physical taskbar and keyboard input can additionally be checked manually using the procedure below.

Build, test, and packaging errors fail the build script and CI job. Inspect the repository's Actions run for the authoritative CI outcome and its downloadable evidence. No success is inferred from source generation alone.

Additional keep-alive coverage: exact zero PCM with buffer boundaries, two-second pulse lifetime, five/ten-minute scheduling using an injected clock, armed/off transitions, alarm/preview interruption, absent headphones, initialization failure and reconnect recovery, no endpoint/session volume writes, sleep-request flags, UI preferences and persistence. Actual ANC 2 overnight retention is not claimed by the software tests.

## Hardware-dependent checks

- Actual overnight wake from the device's supported sleep/hibernate modes, battery and lid policies, firmware wake timers, and Modern Standby behavior.
- Audible loudness and Skullcandy Crusher haptic response. Digital tests verify signal energy and a fixed peak ceiling, not acoustic pressure or a headphone actuator's behavior.
- Real headphone power-off/unplug and volume-control reporting, speaker selection on machines with several outputs, restoration after device removal, and devices exposing a combined speaker/headphone endpoint. No automated test raises the host's actual Windows volume or plays an audible alarm.
- Physical monitor-DPI changes and multi-monitor transitions beyond the rendered scaling checks.

## Manual regression procedure

1. Launch `RISE.exe --data-dir <disposable folder>` for an isolated session without wake tasks. Add a future AM/PM alarm, edit it, toggle it, restart, verify it remains, then delete it. Start a timer, pause/resume it, edit its duration, restart the app, and confirm the countdown survives.
2. Set an alarm one or two minutes ahead; leave the app running and verify the exact minute triggers it. Try a second launch while it rings: it should bring the existing instance forward, with one audio output.
3. Preview all three presets. Check volume/intensity, spectrum motion, distinct timbres, and sub-bass on the intended output device.
4. Use **TEST ALARM**. While ringing, click X, press Alt+F4, and use ordinary taskbar Close. The app should remain open, reset the sequence, display the escape warning, and play the fanfare. Force termination must remain available.
5. Listen for a different bell note on each tile during playback and input. Intentionally make an error, then finish the single five-tile sequence using mouse/keyboard. The alarm must stop immediately and the final chime may decay; the window must close normally.
6. Relaunch after force-terminating an unacknowledged isolated session. Verify the active alarm and escape state resume. Complete it to stop audio.
7. For physical wake, use the normal app data directory, choose a near-future alarm, check the wake diagnostic, and put Windows to sleep while remaining signed in. Confirm wake and playback on that hardware. Disable/delete test alarms afterward to remove their tasks.
