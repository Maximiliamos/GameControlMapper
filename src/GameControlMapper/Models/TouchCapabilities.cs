namespace GameControlMapper.Models;

/// <summary>
/// Возможности Backend (максимальное кол-во пальцев, поддержка давления и т.д.)
/// </summary>
public class TouchCapabilities
{
    /// <summary>
    /// Максимальное количество контактов (пальцев)
    /// </summary>
    public int MaxContacts { get; }

    /// <summary>
    /// Поддерживается ли измерение давления
    /// </summary>
    public bool SupportsPressure { get; }

    /// <summary>
    /// Поддерживается ли ориентация
    /// </summary>
    public bool SupportsOrientation { get; }

    /// <summary>
    /// Поддерживается ли размер контакта
    /// </summary>
    public bool SupportsContactSize { get; }

    public TouchCapabilities(int maxContacts, bool supportsPressure, bool supportsOrientation, bool supportsContactSize)
    {
        MaxContacts = maxContacts;
        SupportsPressure = supportsPressure;
        SupportsOrientation = supportsOrientation;
        SupportsContactSize = supportsContactSize;
    }
}
