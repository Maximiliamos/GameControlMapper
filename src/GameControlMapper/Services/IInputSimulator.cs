using GameControlMapper.Models;

namespace GameControlMapper.Services;

public interface IInputSimulator
{
    Task ExecuteBindingAsync(ControlBinding binding, CancellationToken cancellationToken = default);
    Task ClickAsync(double x, double y, SimulatedMouseButton button = SimulatedMouseButton.Left, CancellationToken cancellationToken = default);
    Task DoubleClickAsync(double x, double y, SimulatedMouseButton button = SimulatedMouseButton.Left, CancellationToken cancellationToken = default);
    Task SwipeAsync(double startX, double startY, double endX, double endY, int durationMilliseconds, CancellationToken cancellationToken = default);
    void MouseDownAt(double x, double y, SimulatedMouseButton button = SimulatedMouseButton.Left);
    void MouseMoveTo(double x, double y);
    void MouseUp(SimulatedMouseButton button = SimulatedMouseButton.Left);
    void MouseDown(SimulatedMouseButton button = SimulatedMouseButton.Left);
    void KeyDown(string key);
    void KeyUp(string key);
    (int X, int Y) GetCursorPosition();
    void RestoreCursor(int x, int y);
    void MoveRelative(int dx, int dy);
}
