using System;

namespace GameControlMapper.Services;

/// <summary>
/// Результат работы распознавания жеста
/// </summary>
public class GestureResult : EventArgs
{
    public string GestureName { get; }

    public GestureResult(string gestureName)
    {
        GestureName = gestureName;
    }
}
