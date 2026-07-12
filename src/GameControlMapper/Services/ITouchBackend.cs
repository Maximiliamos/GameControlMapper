using GameControlMapper.Models;

namespace GameControlMapper.Services;

/// <summary>
/// Интерфейс платформенной реализации сенсорного ввода
/// </summary>
public interface ITouchBackend
{
    /// <summary>
    /// Возможности бэкенда (макс. контактов, поддержка давления и т.д.)
    /// </summary>
    TouchCapabilities Capabilities { get; }

    /// <summary>
    /// Инициализация бэкенда
    /// </summary>
    bool Initialize();

    /// <summary>
    /// Отправка кадра с контактами
    /// </summary>
    bool SendFrame(TouchFrame frame);

    /// <summary>
    /// Завершение работы бэкенда
    /// </summary>
    void Shutdown();
}
