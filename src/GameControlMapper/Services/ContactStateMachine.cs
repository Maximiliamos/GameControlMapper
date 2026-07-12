using System;
using Microsoft.Extensions.Logging;
using GameControlMapper.Models;

namespace GameControlMapper.Services;

/// <summary>
/// Строгая машина состояний для TouchContact
/// </summary>
public class ContactStateMachine
{
    private readonly ILogger<ContactStateMachine> _logger;

    public ContactStateMachine(ILogger<ContactStateMachine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Проверка возможности перехода
    /// </summary>
    public bool CanTransition(TouchState from, TouchState to)
    {
        return (from, to) switch
        {
            (TouchState.Idle, TouchState.Down) => true,
            (TouchState.Down, TouchState.Update) => true,
            (TouchState.Down, TouchState.Up) => true,
            (TouchState.Down, TouchState.Cancelled) => true,
            (TouchState.Update, TouchState.Update) => true,
            (TouchState.Update, TouchState.Up) => true,
            (TouchState.Update, TouchState.Cancelled) => true,
            (TouchState.Up, TouchState.Idle) => true,
            (TouchState.Cancelled, TouchState.Idle) => true,
            _ => false
        };
    }

    /// <summary>
    /// Попытка перевести контакт в новое состояние
    /// </summary>
    public bool TryTransition(TouchContact contact, TouchState newState)
    {
        if (contact == null)
            throw new ArgumentNullException(nameof(contact));

        if (!CanTransition(contact.State, newState))
        {
            _logger.LogError($"Недопустимый переход состояния: {contact.State} → {newState} (ContactId = {contact.ContactId})");
            return false;
        }

        // Обновляем состояние
        contact.State = newState;
        contact.Timestamp = DateTime.Now;

        // При Down: запоминаем время начала
        if (newState == TouchState.Down)
        {
            contact.StartTime = DateTime.Now;
            contact.UpdateCount = 0;
        }

        // При Update: увеличиваем счётчик
        if (newState == TouchState.Update)
        {
            contact.UpdateCount++;
        }

        _logger.LogTrace($"Контакт {contact.ContactId}: {contact.State} → {newState}");
        return true;
    }

    /// <summary>
    /// Сброс состояния контакта в Idle
    /// </summary>
    public void Reset(TouchContact contact)
    {
        if (contact == null)
            throw new ArgumentNullException(nameof(contact));

        if (contact.State != TouchState.Idle)
        {
            // Попробуем перейти в Idle через допустимые состояния
            if (contact.State == TouchState.Down || contact.State == TouchState.Update)
            {
                TryTransition(contact, TouchState.Up);
            }
            if (contact.State == TouchState.Up || contact.State == TouchState.Cancelled)
            {
                TryTransition(contact, TouchState.Idle);
            }
        }

        contact.Reset();
    }
}
