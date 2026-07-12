namespace GameControlMapper.Services;

/// <summary>
/// Интерфейс для реализации жестов
/// </summary>
public interface IGesture
{
    /// <summary>
    /// Событие, срабатываемое при распознавании жеста
    /// </summary>
    event EventHandler<GestureResult>? GestureRecognized;

    /// <summary>
    /// Инициализация жеста
    /// </summary>
    void Initialize();

    /// <summary>
    /// Обновление состояния жеста (вызывается на каждом кадре)
    /// </summary>
    void Update();

    /// <summary>
    /// Сброс состояния жеста
    /// </summary>
    void Reset();
}
