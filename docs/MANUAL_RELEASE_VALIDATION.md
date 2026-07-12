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
