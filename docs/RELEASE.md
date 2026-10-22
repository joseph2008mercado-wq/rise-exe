RISE.EXE v0.1.3 — periodic silent headphone keep-alive.

Download **RISE-Setup.exe** to install the self-contained Windows 10/11 x64 app for your user, with desktop and Start menu shortcuts and an Installed apps entry. No .NET installation or administrator access required. **RISE.exe** and the ZIP remain available for portable use.

- New **KEEP HEADPHONES ACTIVE** control: two seconds of literal digital silence on connected headphones every **5 or 10 minutes** while an alarm or timer is armed. Default: enabled, every five minutes. Preferences survive restarts.
- While enabled with an upcoming alarm/running timer, RISE requests that Windows remain awake while permitting the display to turn off. Keep the app open. Turning the mode off or disabling the final future alarm/running timer releases that idle-sleep request. Explicit sleep/shutdown and lid policy remain available.
- Keep-alive pauses for alarms/previews, never routes to speakers or changes volume/mute, and retries unavailable headphones. Silent audio is an inactivity workaround to test on your own Crusher ANC 2; firmware may ignore it. Multipoint headphones may briefly switch from phone audio when a PC stream begins.
- Retains the speaker fallback: during alarms and expired timers, headphones disconnecting, muting, reaching Windows volume zero, or a muted RISE headphone mixer session trigger speaker playback at **100% Windows and app volume, unmuted**. Fallback stays on speakers until dismissal even if headphones reconnect. The previous speaker volume/mute setting is restored afterward.
- Explicit WASAPI routing follows Windows device changes, retries failed outputs, and reports unavailable speakers. The global default output is not changed. Physical controls that report no change to Windows, or combined headphone/speaker endpoints, cannot be detected reliably. Speaker master volume affects other apps on that endpoint.
- Setup adds shortcuts and an uninstaller. Uninstall removes RISE wake tasks and shortcuts while retaining saved alarms. Close RISE before upgrading/uninstalling. Finish an active puzzle first.
- Retains AM/PM editing, persistent timers, the single five-tile musical puzzle, skeuomorphic skin, strong sub-bass, and all three original presets.

The Windows build verifies core behavior, simulated device disconnect/mute/failure recovery, native audio enumeration, real Task Scheduler registration/cleanup, WPF lifecycle, PCM streaming, and installer/shortcut/uninstaller behavior before publishing. Physical headphone disconnects, sleep/wake, and loudness still require testing on your own machine. The app must run in a signed-in Windows session; wake timers and enabled speakers must be available. Executables are unsigned and may be blocked by Windows Application Control policies.

Original code, artwork and music: Unlicense. NAudio/.NET licenses are embedded and included in the ZIP. See the README for controls, wake limitations, and source builds.
