# Game Control Mapper — beta release candidate

Beta supports Windows Touch Injection, keyboard/mouse mapping, target-window coordinates, multitouch, camera, MouseArea, diagnostics and profile backup. XInput, Macro/Sequence, RawInput, Interception, ViGEm, ADB/Android, pinch and rotation are UnsupportedInBeta.

This package targets 64-bit Windows 10/11 and is self-contained; installing a separate .NET runtime is not
required. Extract the complete ZIP before launch. Run `GameControlMapper.exe`; run
`GameControlMapper.TouchTestHarness.exe` from its separate archive when performing safe touch validation.

The application stores profiles beside the executable in `Profiles` when created. Logs and crash reports are under
`%LocalAppData%\GameControlMapper\Logs`. Use **Экспорт диагностики** to create a redacted diagnostic ZIP, then attach
that ZIP together with reproduction steps and the release version when reporting a problem. Do not attach private
documents or profile JSON unless you have reviewed them yourself.

Follow `MANUAL_RELEASE_VALIDATION.md`: select TouchTestHarness as target, create a profile matching its client area,
start with F8, exercise contacts, stop with F9, and confirm zero active contacts and no protocol errors. Real Touch
Injection, mixed-DPI, negative-origin multi-monitor behavior, target games, and two-minute camera operation remain
manual validation gates and are not claimed by the automated build.

This is an unsigned beta build, not an installer. Compatibility with every game, anti-cheat, endpoint-protection
product, or security policy is not guaranteed. The project does not implement anti-cheat bypasses. Stop using the
build if a game or protection product rejects global hooks or Windows Touch Injection.
