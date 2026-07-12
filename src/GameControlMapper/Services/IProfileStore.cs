using GameControlMapper.Models;

namespace GameControlMapper.Services;

public interface IProfileStore
{
    Task<IReadOnlyList<string>> ListProfilesAsync(CancellationToken cancellationToken = default);
    Task<MapperProfile> LoadAsync(string profileName, CancellationToken cancellationToken = default);
    Task SaveAsync(MapperProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(string profileName, CancellationToken cancellationToken = default);
    Task<string> ExportAsync(MapperProfile profile, string targetPath, CancellationToken cancellationToken = default);
    Task<MapperProfile> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
}
