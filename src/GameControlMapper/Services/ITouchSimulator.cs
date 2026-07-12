namespace GameControlMapper.Services;

public interface ITouchSimulator
{
    Task TapAsync(double x, double y, int milliseconds = 35, CancellationToken cancellationToken = default);
    Task DoubleTapAsync(double x, double y, CancellationToken cancellationToken = default);
    Task HoldAsync(int contactId, double x, double y, int milliseconds, CancellationToken cancellationToken = default);
    Task SwipeAsync(int contactId, double startX, double startY, double endX, double endY, int milliseconds, CancellationToken cancellationToken = default);
    void TouchDown(int contactId, double x, double y);
    void TouchMove(int contactId, double x, double y);
    void TouchUp(int contactId);
    void ReleaseAll();
}
