# Game Control Mapper Architecture

The application is split into WPF views, MVVM view models, serializable domain models, and services.

## Main Modules

- `Models` contains JSON-friendly profile data: profiles, bindings, camera settings, macros, and window binding metadata.
- `ViewModels` contains UI state and commands. Views should not own profile logic.
- `Services` contains storage, coordinate scaling, input simulation, keyboard/mouse hooks, logging, and window discovery.
- `Win32` contains the narrow P/Invoke surface used by hooks, window enumeration, cursor control, and `SendInput`.
- `UI/Views` contains WPF windows: the main editor, transparent overlay, and capture windows.

## Safety Boundary

The project intentionally uses standard operating-system input mechanisms only. It does not inject DLLs into games, patch game memory, or attempt anti-cheat bypasses. Overlay support is intended for windowed and borderless-windowed modes.

## Profile Flow

Profiles are stored as readable JSON files in `Profiles`. The editor updates `BindingViewModel` instances, then `MainViewModel` writes the underlying `ControlBinding` list back into the active `MapperProfile` before saving or activating.

## Input Flow

`InputMappingEngine` receives keyboard hook events, matches hotkeys through `HotkeyParser`, and delegates actions to `IInputSimulator`. Mouse Look uses `CameraMouseLookService`, which captures relative mouse movement, sends relative deltas, and restores the cursor to the activation origin.

## Coordinate Spaces

`WindowCoordinateTransformer` is an isolated mathematical core that converts `ProfilePoint` values from a declared `ProfileSize` into absolute physical screen pixels inside a `PhysicalClientRect`. It supports `Stretch` compatibility scaling and aspect-preserving `UniformFit` with a centered content viewport. Points outside profile bounds are transformed without clamping and are marked with `IsOutsideProfile`.

`GameWindowGeometryProvider` obtains the target window client size with `GetClientRect` and converts its client origin to an absolute screen origin with `ClientToScreen`. For this PerMonitorV2-aware process, the provider contract is physical Windows screen pixels. WPF device-independent pixels are not accepted by the transformer and must be converted at the WPF boundary in a later integration step.

The existing `CoordinateScaler` remains available only as a legacy whole-primary-screen utility. Production points emitted by `InputMappingEngine` use the target-window transform before reaching `TouchEngine`.

Profile and viewport bounds are half-open: `0 <= X < Width` and `0 <= Y < Height`. Rasterization uses `MidpointRounding.AwayFromZero`, followed for in-bounds points by the explicit integer viewport bounds `Ceiling(left)` through `Ceiling(right) - 1` (and the equivalent Y bounds). This prevents rounding from producing the exclusive right or bottom coordinate. Out-of-profile points remain diagnostic transform results and are rejected by the production mapping path rather than clamped.

The production mapping path now owns one `TargetWindowSession` through `TargetWindowSessionManager`. A session snapshots HWND, profile size, `Stretch` mode, physical client geometry, and a generation at Start. The scheduler validates that snapshot before each frame. Window movement, resize, hiding, minimization, destruction, or geometry read failure invalidates the session and joins the existing idempotent graceful Stop/Up flow. A new Start is required to capture new geometry.

Per-monitor DPI awareness is declared by `ApplicationDPIAware=true/PM` and `ApplicationHighDpiMode=PerMonitorV2` in the project. Startup logs the current thread DPI awareness context. Client geometry from `GetClientRect` plus `ClientToScreen` is treated as physical pixels without applying WPF `DpiScale`. WPF capture-window DIP conversion remains intentionally outside this integration.

## Tanks Blitz Gamepad Flow

`XInputGamepadMapper` follows the AntiMicroX approach: it polls an XInput gamepad and maps state changes to native keyboard and mouse events instead of coordinate clicks.

- Left stick: `W`, `A`, `S`, `D`
- Right stick: relative mouse look
- Right trigger / RB: left mouse button
- Left trigger / LB: right mouse button
- X/Y/B: `Q`/`E`/`R`
- D-pad left/up/right: shells `1`/`2`/`3`

This path does not move the cursor to overlay zones.

## Foreground focus safety

Mapping is fail-closed and is permitted only while the canonical `GA_ROOT` target HWND is the foreground root,
the saved process ID still matches that HWND, the window is valid and not minimized, and mapping is running.
A Win32 foreground-event hook queues a notification without logging, waiting, locking, or running asynchronous
shutdown inside the native callback. The managed handler atomically publishes a denied immutable
`InputPermissionSnapshot` before joining the existing idempotent graceful `StopAsync` path. Focus return never
restarts mapping.

Low-level suppression callbacks perform one lock-free snapshot read and a key/button lookup. They do not query
window geometry or foreground state, use the WPF dispatcher, wait for touch sending, or call `StopAsync`. Queued
hook events carry the target generation captured by the callback and the managed handler validates it again. This
prevents input queued before focus loss from creating contacts in an ended or later session. Lifecycle hotkeys are
not suppressed: F8 performs full target validation, F9 joins the current stop, and F10/F11 preserve their existing
non-touch behavior while ordinary input outside the target remains untouched.

## Continuous camera mouse look

`CameraMouseLookService` owns a generation-scoped cursor session through `IMouseCursorController`. Start saves the
cursor position and clip rectangle, clips to the target client rectangle when a production target session exists,
hides the cursor, starts the camera contact, and warps to the physical anchor. Every accepted physical move is
followed by another warp to the anchor, so desktop edges do not limit rotation. The expected warp position and
camera generation identify the resulting synthetic move deterministically; it does not alter velocity or touch.

For input vector `d`, movement is ignored when `|d| <= DeadZone`. Sensitivity and inversion are applied per axis.
Acceleration uses `targetVelocity = sensitiveDelta * (1 + max(0, Acceleration) * |d|)`. Time-based exponential
smoothing uses `alpha = 1 - exp(-dt / max(Smooth, 0.0001))` and
`velocity += alpha * (targetVelocity - velocity)`; `Smooth <= 0` means no smoothing. Velocity magnitude is limited
to `max(0, MaxSpeed)`, and the accumulated touch point is projected onto the circle of radius
`max(0, DragRadius)` around the anchor. Non-finite input is rejected. Stop is idempotent and restores clip,
visibility, and the saved position after ending the camera contact. `UseMouseDrag` remains a compatibility field:
its prior production semantics are ambiguous, so this change does not invent a new branch for it.

## Touch contact allocation

All production contacts acquire a `TouchContactLease` from the singleton `ITouchContactAllocator`. The allocator
uses `TouchCapabilities.MaxContacts`, never evicts an active lease, and records contact ID, target-session
generation, owner identity, state, and a monotonic diagnostic sequence. Camera, every joystick binding, every
MouseArea, and each Tap/Hold/DoubleTap/Swipe execution use this same path; `FixedContacts` is legacy-only.

A lease remains stable until its Up outcome is known. `EndTouch` changes it to `ReleasePending`. The scheduler
returns the ID only after the backend accepts the Up frame. A failed Up is removed from frame retry and moved to
`Quarantined`, so it cannot be issued again in the same backend/session generation. `Reset` on a new generation
clears active and quarantined state. Stale or duplicate releases compare lease identity and generation and cannot
release a newer owner. Capacity exhaustion rejects only the new action and leaves existing contacts untouched.
