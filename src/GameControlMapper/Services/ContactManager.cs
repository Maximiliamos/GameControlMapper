using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using GameControlMapper.Models;

namespace GameControlMapper.Services;

/// <summary>
/// Управляет пулом контактов и выдачей ID
/// </summary>
public class ContactManager
{
    private readonly ILogger<ContactManager> _logger;
    private readonly TouchContact[] _contactPool;
    private readonly Dictionary<int, TouchContact> _activeContacts = new();
    private readonly Queue<int> _freeDynamicIds = new();
    private readonly object _gate = new();
    
    public event EventHandler? ContactsChanged;

    public IReadOnlyDictionary<int, TouchContact> ActiveContacts
    {
        get { lock (_gate) return _activeContacts.ToDictionary(p => p.Key, p => Clone(p.Value)); }
    }

    /// <summary>
    /// Фиксированные ID (зарезервированные)
    /// 0 - камера
    /// 1 - джойстик
    /// 2 - огонь
    /// 3 - прицел
    /// </summary>
    private const int MinFixedId = 0;
    private const int MaxFixedId = 3;
    private const int MinDynamicId = MaxFixedId + 1;

    public int MaxContacts { get; }

    public ContactManager(ILogger<ContactManager> logger, TouchCapabilities capabilities)
    {
        _logger = logger;
        MaxContacts = capabilities.MaxContacts;

        // Создаём пул контактов
        _contactPool = new TouchContact[MaxContacts];
        for (int i = 0; i < MaxContacts; i++)
        {
            _contactPool[i] = new TouchContact(i);
        }

        // Инициализируем свободные динамические ID
        for (int i = MinDynamicId; i < MaxContacts; i++)
        {
            _freeDynamicIds.Enqueue(i);
        }
    }

    /// <summary>
    /// Получить контакт по ID, создавать, если отсутствует
    /// </summary>
    public TouchContact GetOrCreate(int id)
    {
        lock (_gate)
        {
            if (_activeContacts.TryGetValue(id, out var active)) return active;
            if (id < 0 || id >= MaxContacts) throw new ArgumentOutOfRangeException(nameof(id));
            var contact = _contactPool[id];
            contact.Reset();
            _activeContacts.Add(id, contact);
            ContactsChanged?.Invoke(this, EventArgs.Empty);
            return contact;
        }
    }

    public void StartContact(int id, double x, double y)
    {
        lock (_gate)
        {
            var contact = GetOrCreate(id);
            contact.X = x;
            contact.Y = y;
            contact.State = TouchState.Down;
            contact.Timestamp = DateTime.UtcNow;
            contact.StartTime = contact.Timestamp;
        }
    }

    public bool MoveContact(int id, double x, double y)
    {
        lock (_gate)
        {
            if (!_activeContacts.TryGetValue(id, out var contact)) return false;
            contact.X = x;
            contact.Y = y;
            contact.State = TouchState.Update;
            contact.Timestamp = DateTime.UtcNow;
            contact.UpdateCount++;
            return true;
        }
    }

    public bool EndContact(int id)
    {
        lock (_gate)
        {
            if (!_activeContacts.TryGetValue(id, out var contact)) return false;
            contact.State = TouchState.Up;
            contact.Timestamp = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// Получить контакт по зарезервированному фиксированному ID
    /// </summary>
    public TouchContact? GetFixedContact(int fixedId)
    {
        if (fixedId < MinFixedId || fixedId > MaxFixedId)
        {
            _logger.LogError("Некорректный фиксированный ID: {FixedId}", fixedId);
            return null;
        }

        // Если контакт уже активен, возвращаем его
        if (_activeContacts.TryGetValue(fixedId, out var contact))
        {
            return contact;
        }

        // Инициализируем новый контакт
        contact = _contactPool[fixedId];
        return contact;
    }

    /// <summary>
    /// Получить свободный динамический контакт
    /// </summary>
    public TouchContact? AcquireDynamicContact()
    {
        if (_freeDynamicIds.Count == 0)
        {
            _logger.LogError("Нет свободных динамических ID!");
            return null;
        }

        int id = _freeDynamicIds.Dequeue();
        var contact = _contactPool[id];
        _activeContacts.Add(id, contact);
        _logger.LogTrace("Выдан динамический контакт ID: {Id}", id);
        return contact;
    }

    /// <summary>
    /// Вернуть контакт в пул
    /// </summary>
    public void ReleaseContact(int contactId)
    {
        if (!_activeContacts.TryGetValue(contactId, out var contact))
        {
            _logger.LogWarning("Контакт ID {ContactId} не найден в активных!", contactId);
            return;
        }

        // Сбрасываем состояние
        contact.Reset();
        _activeContacts.Remove(contactId);

        // Если это динамический ID, вернём в очередь свободных
        if (contactId >= MinDynamicId && contactId < MaxContacts)
        {
            _freeDynamicIds.Enqueue(contactId);
        }

        _logger.LogTrace("Контакт ID {ContactId} возвращён в пул", contactId);
        ContactsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Получить все активные контакты
    /// </summary>
    public List<TouchContact> GetActiveContacts()
    {
        lock (_gate) return _activeContacts.Values.Select(Clone).ToList();
    }

    public void CompleteReleasedContacts(IEnumerable<int> contactIds)
    {
        lock (_gate)
        {
            foreach (var id in contactIds)
            {
                if (_activeContacts.TryGetValue(id, out var contact) && contact.State == TouchState.Up)
                {
                    contact.Reset();
                    _activeContacts.Remove(id);
                }
            }
        }
        ContactsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AdvanceSentContacts(IEnumerable<int> contactIds)
    {
        lock (_gate)
        {
            foreach (var id in contactIds)
            {
                if (_activeContacts.TryGetValue(id, out var contact) && contact.State == TouchState.Down)
                {
                    contact.State = TouchState.Update;
                }
            }
        }
    }

    /// <summary>
    /// Завершить все контакты
    /// </summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            foreach (var contact in _activeContacts.Values) contact.State = TouchState.Up;
        }
        _logger.LogInformation("Все контакты помечены для завершения");
        ContactsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Получить контакт по ID
    /// </summary>
    public TouchContact? GetContact(int contactId)
    {
        lock (_gate) return _activeContacts.TryGetValue(contactId, out var contact) ? contact : null;
    }

    private static TouchContact Clone(TouchContact source) => new(source.ContactId)
    {
        X = source.X, Y = source.Y, Pressure = source.Pressure,
        Orientation = source.Orientation, ContactSize = source.ContactSize,
        State = source.State, Timestamp = source.Timestamp,
        UpdateCount = source.UpdateCount, StartTime = source.StartTime
    };

    /// <summary>
    /// Активировать контакт (добавить в active)
    /// </summary>
    public bool ActivateContact(TouchContact contact)
    {
        if (contact == null) throw new ArgumentNullException(nameof(contact));
        if (_activeContacts.ContainsKey(contact.ContactId))
        {
            _logger.LogWarning("Контакт ID {ContactId} уже активен!", contact.ContactId);
            return false;
        }
        _activeContacts.Add(contact.ContactId, contact);
        _logger.LogTrace("Контакт ID {ContactId} активирован", contact.ContactId);
        return true;
    }
}
