RISE.EXE v0.1.1 — AM/PM alarms, persistent timers, and a shorter musical dismissal puzzle.

Download **RISE.exe** for a self-contained Windows 10/11 x64 app. The ZIP also includes redistribution notices and a checksum. No .NET installation required.

- Set alarms with separate hour, minute, and AM/PM controls. The next occurrence is chosen automatically, with simple repeat presets and optional specific dates. Edit existing alarms in place.
- Create countdown timers with quick presets, live remaining time, pause/resume, editing, and persistent deadlines. Timers use the selected alarm sound, wake tasks, and dismissal puzzle.
- Dismiss with one five-tile sequence instead of three rounds. Four distinct, bright MIDI-style bell notes play during the sequence and your input. The alarm ducks briefly under the notes; finishing stops it immediately while the final chime decays.
- Retains the skeuomorphic skin, strong sub-bass, three original sound presets, saved schedules, native close rejection, and embarrassment fanfare.

The Windows build runs core tests, real Task Scheduler registration/cleanup, WPF lifecycle integration checks, multi-scale rendering, and PCM streaming checks before publishing. Physical sleep/wake behavior, firmware support, and speaker loudness require validation on your own machine. The app must run in a signed-in Windows session; wake timers and the selected audio device must be available. The executable is unsigned.

Original code, artwork and music: Unlicense. NAudio/.NET licenses are embedded and included in the ZIP. See the README for controls, wake limitations, and source builds.
