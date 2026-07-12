# Manual release validation

Application version: __________  Commit hash: __________  Tester/date: __________

Run only in a human-controlled safe environment with no unsaved data. Record relevant journal lines and mark each
scenario **PASS** or **FAIL**.

## Focus safety

Preparation: select a disposable target window. Start with F8, then separately hold W, camera Ctrl, and a MouseArea
button while using Alt+Tab; also overlap F9 with Alt+Tab. Expected: exactly one Up per sent contact, suppression is
off in the new application, late releases pass through, returning focus does not restart mapping, and no Win32 87
appears. Journal: start, foreground invalidation, one stop/release result. Result: ______  Commit: ______

## Camera recenter and restoration

Hold camera movement continuously in one direction for at least two minutes and reach every physical desktop edge.
Stop separately by CtrlUp, F9, Alt+Tab, target minimization, and application close. Expected: rotation continues at
edges; cursor position, visibility, and previous clip-region are restored once. Journal: camera start/stop and no
cursor-controller failure. Result: ______  Commit: ______

## Monitors, coordinates, and DPI

Exercise the target on the primary monitor, a left-hand monitor with negative origin, DPI 100%, DPI 125%, and a
mixed-DPI move between monitors. Move the window during an active contact. Expected: points remain inside the client
area; movement invalidates and releases rather than continuing with stale geometry. Journal: geometry generation
invalidation and successful final Up. Result: ______  Commit: ______

## Multitouch allocator

Activate joystick, camera, MouseArea, and ordinary zones concurrently. Reach ten contacts, attempt an eleventh,
stop, then start again. Expected: IDs 0..9 are unique and stable; the eleventh is rejected without eviction or UI
failure; all leases release and a new session works. Journal: capacity warning only for rejected owner and release
result. Result: ______  Commit: ______

## Profile persistence and recovery

Save an existing profile twice and verify `.json.bak`; corrupt primary JSON and manually restore/load backup; import
corrupt JSON; simulate an unwritable Profiles directory. Expected: valid primary is never partially replaced, backup
contains the previous version, corrupt files are skipped but preserved, import does not alter the current profile,
and write failure leaves the last working file intact. Journal: structured validation code/path or atomic-save error,
without UI stack trace. Result: ______  Commit: ______

## TouchTestHarness procedure

Start `GameControlMapper.TouchTestHarness`, record its client size, select its window as target, create a matching
profile, and start mapping with F8. The harness must only receive and visualize input; it never injects input itself.

| Scenario | Expected harness sequence | PASS/FAIL | Commit |
|---|---|---|---|
| Tap | one Down, then one Up; no orphan ID | | |
| Hold | one Down, optional Moves, one Up on completion | | |
| DoubleTap | Down/Up followed by a second Down/Up | | |
| Swipe | Down, ordered Moves, one Up | | |
| WASD | one stable ID: Down, Moves as direction changes, Up on release | | |
| Camera for at least two minutes | one stable ID, continuous Moves despite desktop edges, one Up | | |
| Left and right MouseArea | independent IDs, each Down then one Up | | |
| Several simultaneous actions | independent markers and IDs; balanced Down/Up | | |
| Ten contacts | active count and maximum reach 10; all IDs independent | | |
| Eleventh action | mapper rejects it; harness remains at 10 and does not crash | | |
| F9 while held | every active ID receives exactly one Up | | |
| Alt+Tab while held | every sent contact receives one Up; no later Move | | |
| Move or resize target while held | session stops and active ID receives one Up | | |
| Minimize target while held | session stops and active ID receives one Up | | |
| Start after Stop | new balanced session; no event from previous generation | | |

Finally press **Проверить активные контакты**. PASS requires zero active IDs, balanced lifecycle for every sent
contact, no `PROTOCOL ERROR`, no Win32 error 87, and no automatic restart after focus loss.

## Production diagnostics

Run a normal mapping session and stop it by F9, focus loss, and geometry invalidation. Verify that
`%LocalAppData%\GameControlMapper\Logs` contains UTF-8 structured entries with a short session ID, the correct stop
reason, active-contact count, and final release result, without per-frame/mouse-move spam. Trigger only a controlled
non-destructive test exception in a dedicated build and verify cursor restoration plus `crash-report.txt`.

Use **Экспорт диагностики** and inspect the ZIP. It must contain metadata, README, exception summary, and recent
logs, but no JSON profile content, pressed keys, process/window lists, document contents, credentials, or unredacted
user-home paths. PASS requires at most three rotated archives, meaningful Win32 operation/code/message entries,
successful application shutdown, and an export that does not modify the source logs. Result: ______ Commit: ______
