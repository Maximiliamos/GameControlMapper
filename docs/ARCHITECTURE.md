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
