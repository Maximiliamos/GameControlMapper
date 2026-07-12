using System;
using System.Collections.Generic;
using GameControlMapper.Models;

namespace GameControlMapper.Services;

/// <summary>
/// Бэкенд для тестов: не вызывает WinAPI, только сохраняет все кадры в память
/// </summary>
public class FakeTouchBackend : ITouchBackend
{
    public TouchCapabilities Capabilities { get; } = new TouchCapabilities(32, true, true, true);

    public List<TouchFrame> RecordedFrames { get; } = new List<TouchFrame>();
    public bool IsInitialized { get; private set; }

    public bool Initialize()
    {
        IsInitialized = true;
        return true;
    }

    public bool SendFrame(TouchFrame frame)
    {
        RecordedFrames.Add(frame);
        return true;
    }

    public void Shutdown()
    {
        IsInitialized = false;
        // Clear frames? Let's keep them for tests.
    }

    /// <summary>
    /// Очистить записанные кадры
    /// </summary>
    public void Clear()
    {
        foreach (var frame in RecordedFrames)
        {
            frame.Dispose();
        }
        RecordedFrames.Clear();
    }
}
