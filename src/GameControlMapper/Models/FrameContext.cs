using System;

namespace GameControlMapper.Models;

/// <summary>
/// Контекст кадра для отладки, сглаживания, записи и воспроизведения
/// </summary>
public class FrameContext
{
    /// <summary>
    /// Целевая частота кадров
    /// </summary>
    public int TargetFps { get; set; } = 120;

    /// <summary>
    /// Включено ли сглаживание
    /// </summary>
    public bool IsSmoothingEnabled { get; set; }

    /// <summary>
    /// Включена ли запись кадров
    /// </summary>
    public bool IsRecording { get; set; }

    /// <summary>
    /// Включен ли режим отладки
    /// </summary>
    public bool IsDebugMode { get; set; }

    /// <summary>
    /// Время начала записи
    /// </summary>
    public DateTime? RecordingStartTime { get; set; }
}
