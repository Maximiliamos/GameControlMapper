using System.IO;
using System.Text.Json;
using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class ProfileStore : IProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<ProfileStore> _logger;
    private readonly string _profilesDirectory;

    public ProfileStore(ILogger<ProfileStore> logger)
    {
        _logger = logger;
        _profilesDirectory = Path.Combine(AppContext.BaseDirectory, "Profiles");
    }

    public async Task<IReadOnlyList<string>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_profilesDirectory);
        return await Task.Run(() => Directory
            .EnumerateFiles(_profilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name)
            .ToArray(), cancellationToken);
    }

    public async Task<MapperProfile> LoadAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var path = GetProfilePath(profileName);
        if (!File.Exists(path))
        {
            var profile = MapperProfile.CreateDefault(profileName);
            await SaveAsync(profile, cancellationToken);
            return profile;
        }

        await using var stream = File.OpenRead(path);
        var loaded = await JsonSerializer.DeserializeAsync<MapperProfile>(stream, JsonOptions, cancellationToken);
        return loaded ?? MapperProfile.CreateDefault(profileName);
    }

    public async Task SaveAsync(MapperProfile profile, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_profilesDirectory);
        var path = GetProfilePath(profile.Name);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
        _logger.LogInformation("Profile saved: {Profile}", profile.Name);
    }

    public Task DeleteAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var path = GetProfilePath(profileName);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Profile deleted: {Profile}", profileName);
        }

        return Task.CompletedTask;
    }

    public async Task<string> ExportAsync(MapperProfile profile, string targetPath, CancellationToken cancellationToken = default)
    {
        var path = Directory.Exists(targetPath)
            ? Path.Combine(targetPath, $"{SanitizeFileName(profile.Name)}.json")
            : targetPath;

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
        return path;
    }

    public async Task<MapperProfile> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(sourcePath);
        var profile = await JsonSerializer.DeserializeAsync<MapperProfile>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Profile file is empty or invalid.");

        await SaveAsync(profile, cancellationToken);
        return profile;
    }

    private string GetProfilePath(string profileName)
    {
        return Path.Combine(_profilesDirectory, $"{SanitizeFileName(profileName)}.json");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "Profile" : clean;
    }
}
