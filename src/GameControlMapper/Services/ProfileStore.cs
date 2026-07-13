using System.Text.Json;
using System.IO;
using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class ProfileStore : IProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private readonly ILogger<ProfileStore> _logger;private readonly IMapperProfileValidator _validator;private readonly string _profilesDirectory;private readonly SemaphoreSlim _saveGate=new(1,1);
    public ProfileStore(ILogger<ProfileStore> logger,IMapperProfileValidator validator):this(logger,validator,Path.Combine(AppContext.BaseDirectory,"Profiles")){}
    public ProfileStore(ILogger<ProfileStore> logger,IMapperProfileValidator validator,string profilesDirectory){_logger=logger;_validator=validator;_profilesDirectory=Path.GetFullPath(profilesDirectory);}
    public async Task<IReadOnlyList<string>> ListProfilesAsync(CancellationToken ct=default)
    {
        Directory.CreateDirectory(_profilesDirectory);var result=new List<string>();
        foreach(var path in Directory.EnumerateFiles(_profilesDirectory,"*.json"))try{var profile=await ReadValidatedAsync(path,ct);result.Add(profile.Name);}catch(Exception ex){_logger.LogError(ex,"Profile skipped because it is corrupt or invalid: {Path}",path);}
        return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToArray();
    }
    public async Task<MapperProfile> LoadAsync(string name,CancellationToken ct=default)
    {var path=GetProfilePath(name);if(!File.Exists(path)){var p=MapperProfile.CreateDefault(name);await SaveAsync(p,ct);return p;}return await ReadValidatedAsync(path,ct);}
    public async Task SaveAsync(MapperProfile profile,CancellationToken ct=default)
    {
        EnsureValid(profile);Directory.CreateDirectory(_profilesDirectory);EnsureNoNormalizedCollision(profile.Name);var path=GetProfilePath(profile.Name);var temp=path+".profile.tmp";var backup=path+".bak";var json=JsonSerializer.Serialize(profile,JsonOptions);
        await _saveGate.WaitAsync(ct);try
        {
            try
            {
                await using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.Asynchronous|FileOptions.WriteThrough)){await using var writer=new StreamWriter(stream);await writer.WriteAsync(json.AsMemory(),ct);await writer.FlushAsync(ct);stream.Flush(true);}
                await ReadValidatedAsync(temp,ct);
                if(File.Exists(path))File.Replace(temp,path,backup,true);else File.Move(temp,path);
                _logger.LogInformation("Profile saved atomically: {Profile}",profile.Name);
            }
            catch(Exception ex){_logger.LogError(ex,"Atomic profile save failed: {Profile}",profile.Name);throw;}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }finally{_saveGate.Release();}
    }
    public async Task DeleteAsync(string name,CancellationToken ct=default)
    {
        var path=GetProfilePath(name);
        await _saveGate.WaitAsync(ct);
        try
        {
            foreach(var candidate in new[]{path,path+".bak",path+".profile.tmp"})
                if(File.Exists(candidate))File.Delete(candidate);
            _logger.LogInformation("Profile and its recovery artifacts deleted: {Profile}",name);
        }
        finally{_saveGate.Release();}
    }
    public async Task<string> ExportAsync(MapperProfile profile,string targetPath,CancellationToken ct=default)
    {
        EnsureValid(profile);var path=Directory.Exists(targetPath)?Path.Combine(targetPath,SafeName(profile.Name)+".json"):Path.GetFullPath(targetPath);var temp=path+".export.tmp";
        try{await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(profile,JsonOptions),ct);await ReadValidatedAsync(temp,ct);File.Move(temp,path,true);return path;}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public async Task<MapperProfile> ImportAsync(string sourcePath,CancellationToken ct=default){var profile=await ReadValidatedAsync(sourcePath,ct);await SaveAsync(profile,ct);return profile;}
    public async Task<MapperProfile> LoadBackupAsync(string name,CancellationToken ct=default)=>await ReadValidatedAsync(GetProfilePath(name)+".bak",ct);
    private async Task<MapperProfile> ReadValidatedAsync(string path,CancellationToken ct){try{await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);var p=await JsonSerializer.DeserializeAsync<MapperProfile>(stream,JsonOptions,ct)??throw new JsonException("Profile is empty.");EnsureValid(p);return p;}catch(ProfileValidationException){throw;}catch(Exception ex){var result=new ProfileValidationResult([new("profile.json.invalid","$",ex.Message)],[]);throw new ProfileValidationException(result);}}
    private void EnsureValid(MapperProfile profile){var result=_validator.Validate(profile);if(!result.IsValid)throw new ProfileValidationException(result);}
    private string GetProfilePath(string name){var safe=SafeName(name);var path=Path.GetFullPath(Path.Combine(_profilesDirectory,safe+".json"));if(!path.StartsWith(_profilesDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Profile path escapes Profiles directory.");return path;}
    private void EnsureNoNormalizedCollision(string name)
    {
        var wanted=ProfileNamePolicy.NormalizeForComparison(name);
        foreach(var path in Directory.EnumerateFiles(_profilesDirectory,"*.json"))
        {
            var existing=Path.GetFileNameWithoutExtension(path);
            if(ProfileNamePolicy.NormalizeForComparison(existing)==wanted&&!string.Equals(existing,name,StringComparison.Ordinal))
                throw new ProfileValidationException(new ProfileValidationResult([new("profile.name.collision","name","Profile name collides after Windows normalization.")],[]));
        }
    }
    private static string SafeName(string name){if(!ProfileNamePolicy.TryValidate(name,out _,out var message))throw new InvalidOperationException(message);return name;}
}
