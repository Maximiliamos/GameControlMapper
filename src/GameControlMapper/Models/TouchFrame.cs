using System;
using System.Buffers;

namespace GameControlMapper.Models;

/// <summary>
/// Кадр с состоянием всех контактов за один момент времени
/// </summary>
public class TouchFrame : IDisposable
{
    private static readonly ArrayPool<TouchContact> _contactPool = ArrayPool<TouchContact>.Create(32, 10);

    private TouchContact[]? _contactsBuffer;
    private bool _disposed;

    /// <summary>
    /// Уникальный идентификатор кадра
    /// </summary>
    public int FrameId { get; }

    /// <summary>
    /// Время создания кадра
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Время с предыдущего кадра
    /// </summary>
    public TimeSpan DeltaTime { get; }

    /// <summary>
    /// Количество активных контактов в этом кадре
    /// </summary>
    public int ContactCount { get; private set; }

    /// <summary>
    /// Контекст кадра
    /// </summary>
    public FrameContext Context { get; }

    /// <summary>
    /// Создаёт пустой кадр
    /// </summary>
    public TouchFrame(int frameId, DateTime timestamp, TimeSpan deltaTime, FrameContext context)
    {
        FrameId = frameId;
        Timestamp = timestamp;
        DeltaTime = deltaTime;
        Context = context;
    }

    /// <summary>
    /// Копирует массив контактов с использованием ArrayPool
    /// </summary>
    /// <param name="contacts">Исходный массив контактов</param>
    public void SetContacts(ReadOnlySpan<TouchContact> contacts)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TouchFrame));

        // Возвращаем старый буфер в пул, если есть
        if (_contactsBuffer != null)
        {
            _contactPool.Return(_contactsBuffer, clearArray: true);
        }

        ContactCount = contacts.Length;
        _contactsBuffer = _contactPool.Rent(ContactCount);
        contacts.CopyTo(_contactsBuffer);
    }

    public void SetContacts(System.Collections.Generic.List<TouchContact> contacts)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TouchFrame));

        if (_contactsBuffer != null)
        {
            _contactPool.Return(_contactsBuffer, clearArray: true);
        }

        ContactCount = contacts.Count;
        _contactsBuffer = _contactPool.Rent(ContactCount);
        contacts.CopyTo(_contactsBuffer);
    }

    /// <summary>
    /// Возвращает span с контактами (не храните ссылку после Dispose!)
    /// </summary>
    public ReadOnlySpan<TouchContact> GetContacts()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TouchFrame));
        return _contactsBuffer.AsSpan(0, ContactCount);
    }

    /// <summary>
    /// Освобождает ресурсы (возвращает массив в пул)
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            if (_contactsBuffer != null)
            {
                _contactPool.Return(_contactsBuffer, clearArray: true);
                _contactsBuffer = null;
            }
        }
        _disposed = true;
    }

    ~TouchFrame()
    {
        Dispose(false);
    }
}
