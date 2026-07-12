using System;

namespace GameControlMapper.Models;

/// <summary>
/// Модель одного контакта (пальца) на экране
/// </summary>
public class TouchContact
{
    /// <summary>
    /// Уникальный идентификатор контакта
    /// </summary>
    public int ContactId { get; }

    /// <summary>
    /// Координата X в пикселях
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Координата Y в пикселях
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Давление (0-1024)
    /// </summary>
    public int Pressure { get; set; } = 512;

    /// <summary>
    /// Ориентация (-180 - 180 градусов)
    /// </summary>
    public float Orientation { get; set; }

    /// <summary>
    /// Размер контакта (диаметр в пикселях)
    /// </summary>
    public double ContactSize { get; set; } = 14;

    /// <summary>
    /// Текущее состояние
    /// </summary>
    public TouchState State { get; set; } = TouchState.Idle;

    /// <summary>
    /// Время создания/последнего обновления
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Активен ли контакт
    /// </summary>
    public bool IsActive => State == TouchState.Down || State == TouchState.Update;

    /// <summary>
    /// Количество обновлений
    /// </summary>
    public int UpdateCount { get; set; }

    /// <summary>
    /// Время начала контакта
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Конструктор
    /// </summary>
    public TouchContact(int contactId)
    {
        ContactId = contactId;
    }

    /// <summary>
    /// Сбросить состояние контакта (для повторного использования)
    /// </summary>
    public void Reset()
    {
        X = 0;
        Y = 0;
        Pressure = 512;
        Orientation = 0;
        ContactSize = 14;
        State = TouchState.Idle;
        Timestamp = DateTime.MinValue;
        UpdateCount = 0;
        StartTime = DateTime.MinValue;
    }
}
