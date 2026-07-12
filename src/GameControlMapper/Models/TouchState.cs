namespace GameControlMapper.Models;

/// <summary>
/// Состояние одного контакта (пальца)
/// </summary>
public enum TouchState
{
    /// <summary>
    /// Контакт неактивен
    /// </summary>
    Idle,

    /// <summary>
    /// Начало касания (TouchDown)
    /// </summary>
    Down,

    /// <summary>
    /// Обновление касания (TouchMove)
    /// </summary>
    Update,

    /// <summary>
    /// Завершение касания (TouchUp)
    /// </summary>
    Up,

    /// <summary>
    /// Отмена касания
    /// </summary>
    Cancelled
}
