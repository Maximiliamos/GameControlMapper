namespace GameControlMapper.Models;

public sealed record InputPermissionSnapshot(bool AllowMappedInput, bool AllowSuppression, long Generation,
    IReadOnlySet<int> SuppressedKeys, IReadOnlySet<int> SuppressedButtons)
{
    public static readonly InputPermissionSnapshot Denied = new(false, false, 0, new HashSet<int>(), new HashSet<int>());
}

public readonly record struct GeneratedInputEvent(int VirtualKey, long Generation);
