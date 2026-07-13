using System.IO;
using System.Text.Json;

namespace GameControlMapper.Services;

public sealed record OlenemerBattleSummary(string Outcome, int Damage, int Kills, int Experience, string Tank, int TankLevel, bool Survived);

public sealed record OlenemerStatsSnapshot(
    int Battles, int Wins, int Losses, long DamageDealt, int Frags,
    OlenemerBattleSummary? LastBattle, DateTime LastUpdatedUtc);

public sealed class OlenemerStatsReader
{
    private readonly string _directory;

    public OlenemerStatsReader() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Olenemer")) { }

    public OlenemerStatsReader(string directory) => _directory = Path.GetFullPath(directory);

    public bool TryRead(out OlenemerStatsSnapshot? snapshot)
    {
        snapshot = null;
        var accountPath = Path.Combine(_directory, "account_stats.json");
        if (!File.Exists(accountPath)) return false;
        try
        {
            using var account = ReadDocument(accountPath);
            var root = account.RootElement;
            OlenemerBattleSummary? lastBattle = null;
            var battlePath = Path.Combine(_directory, "battle_result.json");
            if (File.Exists(battlePath))
            {
                using var battle = ReadDocument(battlePath);
                var value = battle.RootElement;
                if (ReadBool(value, "finished"))
                    lastBattle = new(ReadString(value, "outcome"), ReadInt(value, "damage"),
                        ReadInt(value, "kills"), ReadInt(value, "xp"), ReadString(value, "tank"),
                        ReadInt(value, "tank_level"), ReadBool(value, "survived"));
            }

            snapshot = new OlenemerStatsSnapshot(
                Math.Max(0, ReadInt(root, "battles")), Math.Max(0, ReadInt(root, "wins")),
                Math.Max(0, ReadInt(root, "losses")), Math.Max(0, ReadLong(root, "damage_dealt")),
                Math.Max(0, ReadInt(root, "frags")), lastBattle, File.GetLastWriteTimeUtc(accountPath));
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static JsonDocument ReadDocument(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonDocument.Parse(stream);
    }
    private static int ReadInt(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;
    private static long ReadLong(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;
    private static bool ReadBool(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    private static string ReadString(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
