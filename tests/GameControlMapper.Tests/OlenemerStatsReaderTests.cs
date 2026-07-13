using GameControlMapper.Services;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class OlenemerStatsReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gcm-olenemer-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReadsAggregateAccountAndLastBattleData()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "account_stats.json"), "{\"battles\":100,\"wins\":55,\"losses\":45,\"damage_dealt\":123400,\"frags\":80}");
        File.WriteAllText(Path.Combine(_directory, "battle_result.json"), "{\"finished\":true,\"outcome\":\"win\",\"damage\":2500,\"kills\":2,\"xp\":900,\"tank\":\"T30\",\"tank_level\":9,\"survived\":true}");
        Assert.True(new OlenemerStatsReader(_directory).TryRead(out var snapshot));
        Assert.NotNull(snapshot);
        Assert.Equal(100, snapshot.Battles);
        Assert.Equal(123400, snapshot.DamageDealt);
        Assert.Equal("T30", snapshot.LastBattle!.Tank);
        Assert.Equal(2500, snapshot.LastBattle.Damage);
    }

    [Fact]
    public void MissingOrInvalidAccountFileFailsClosed()
    {
        Directory.CreateDirectory(_directory);
        var reader = new OlenemerStatsReader(_directory);
        Assert.False(reader.TryRead(out _));
        File.WriteAllText(Path.Combine(_directory, "account_stats.json"), "not-json");
        Assert.False(reader.TryRead(out _));
        File.WriteAllText(Path.Combine(_directory, "account_stats.json"), "[]");
        Assert.False(reader.TryRead(out _));
    }

    public void Dispose() { try { Directory.Delete(_directory, true); } catch { } }
}
