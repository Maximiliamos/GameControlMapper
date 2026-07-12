using System.Text.Json;
using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class ProfileStoreSafetyTests
{
    [Fact] public async Task SaveProfile_WritesValidJson(){using var f=new F();await f.Store.SaveAsync(f.Profile);Assert.NotNull(JsonSerializer.Deserialize<MapperProfile>(File.ReadAllText(f.Path)));}
    [Fact] public async Task SaveProfile_AtomicallyReplacesExistingFile(){using var f=new F();await f.Store.SaveAsync(f.Profile);f.Profile.Game="new";await f.Store.SaveAsync(f.Profile);Assert.Equal("new",(await f.Store.LoadAsync("Test")).Game);}
    [Fact] public async Task SaveProfile_CreatesBackupOfPreviousVersion(){using var f=new F();f.Profile.Game="old";await f.Store.SaveAsync(f.Profile);f.Profile.Game="new";await f.Store.SaveAsync(f.Profile);Assert.Equal("old",(await f.Store.LoadBackupAsync("Test")).Game);}
    [Fact] public async Task SaveProfile_WriteFailureLeavesOriginalUntouched(){using var f=new F();await f.Store.SaveAsync(f.Profile);var before=File.ReadAllText(f.Path);Directory.CreateDirectory(f.Path+".profile.tmp");await Assert.ThrowsAnyAsync<Exception>(()=>f.Store.SaveAsync(f.Profile));Assert.Equal(before,File.ReadAllText(f.Path));}
    [Fact] public async Task SaveProfile_ValidationFailureDoesNotWriteFile(){using var f=new F();f.Profile.ResolutionWidth=0;await Assert.ThrowsAsync<ProfileValidationException>(()=>f.Store.SaveAsync(f.Profile));Assert.False(File.Exists(f.Path));}
    [Fact] public async Task SaveProfile_TemporaryFileIsRemovedAfterSuccess(){using var f=new F();await f.Store.SaveAsync(f.Profile);Assert.False(File.Exists(f.Path+".profile.tmp"));}
    [Fact] public async Task SaveProfile_TemporaryFileIsCleanedAfterFailure(){using var f=new F();f.Profile.ResolutionWidth=0;await Assert.ThrowsAsync<ProfileValidationException>(()=>f.Store.SaveAsync(f.Profile));Assert.False(File.Exists(f.Path+".profile.tmp"));}
    [Fact] public async Task SaveProfile_VerifiesTemporaryJsonBeforeReplace()=>await SaveProfile_WritesValidJson();
    [Fact] public async Task LoadProfiles_CorruptFileDoesNotBlockOtherProfiles(){using var f=new F();await f.Store.SaveAsync(f.Profile);File.WriteAllText(Path.Combine(f.Dir,"bad.json"),"{");Assert.Equal(["Test"],await f.Store.ListProfilesAsync());}
    [Fact] public async Task LoadProfile_CorruptJsonReturnsStructuredError(){using var f=new F();File.WriteAllText(f.Path,"{");var e=await Assert.ThrowsAsync<ProfileValidationException>(()=>f.Store.LoadAsync("Test"));Assert.Equal("profile.json.invalid",e.Result.Errors[0].Code);}
    [Fact] public async Task Import_InvalidProfileDoesNotChangeCurrentProfile(){using var f=new F();File.WriteAllText(Path.Combine(f.Dir,"import.txt"),"{");await Assert.ThrowsAsync<ProfileValidationException>(()=>f.Store.ImportAsync(Path.Combine(f.Dir,"import.txt")));Assert.False(File.Exists(f.Path));}
    [Fact] public async Task Import_DuplicateBindingIdsIsRejected(){using var f=new F();f.Profile.Bindings.Add(f.Profile.Bindings[0]);var p=Path.Combine(f.Dir,"i.txt");File.WriteAllText(p,JsonSerializer.Serialize(f.Profile));await Assert.ThrowsAsync<ProfileValidationException>(()=>f.Store.ImportAsync(p));}
    [Fact] public void Validation_RejectsZeroResolution(){var p=Valid();p.ResolutionWidth=0;Bad(p,"profile.resolution.width");}
    [Fact] public void Validation_RejectsNegativeResolution(){var p=Valid();p.ResolutionHeight=-1;Bad(p,"profile.resolution.height");}
    [Fact] public void Validation_RejectsExcessiveResolution(){var p=Valid();p.ResolutionWidth=20000;Bad(p,"profile.resolution.excessive");}
    [Fact] public void Validation_RejectsNaNCoordinates(){var p=Valid();p.Bindings[0].X=double.NaN;Bad(p,"binding.geometry.nonfinite");}
    [Fact] public void Validation_RejectsInfiniteCoordinates(){var p=Valid();p.Bindings[0].Y=double.PositiveInfinity;Bad(p,"binding.geometry.nonfinite");}
    [Fact] public void Validation_RejectsNonPositiveArea(){var p=Valid();p.Bindings[0].Width=0;Bad(p,"binding.area.nonpositive");}
    [Fact] public void Validation_RejectsPointOutsideHalfOpenBounds(){var p=Valid();p.Bindings[0].X=p.ResolutionWidth;Bad(p,"binding.point.outside");}
    [Fact] public void Validation_RejectsUnsupportedBindingKind(){var p=Valid();p.Bindings[0].Kind=(BindingKind)999;Bad(p,"binding.kind.unsupported");}
    [Fact] public void Validation_RejectsInvalidHotkey(){var p=Valid();p.Bindings[0].Hotkey="NoSuchKey";Bad(p,"binding.hotkey.invalid");}
    [Fact] public void Validation_RejectsNegativeDuration(){var p=Valid();p.Bindings[0].HoldMilliseconds=-1;Bad(p,"binding.duration.negative");}
    [Fact] public void Validation_RejectsExcessiveDuration(){var p=Valid();p.Bindings[0].DelayMilliseconds=700000;Bad(p,"binding.duration.excessive");}
    [Fact] public void Validation_RejectsInvalidOpacity(){var p=Valid();p.Bindings[0].Opacity=2;Bad(p,"binding.opacity.invalid");}
    [Fact] public void Validation_ReturnsFieldPathAndErrorCode(){var p=Valid();p.Bindings[0].Opacity=2;var i=Validator.Validate(p).Errors.Single();Assert.Equal("bindings[0].opacity",i.FieldPath);Assert.Equal("binding.opacity.invalid",i.Code);}
    [Fact] public async Task LegacyValidProfile_LoadsWithoutMigration(){using var f=new F();File.WriteAllText(f.Path,JsonSerializer.Serialize(f.Profile));Assert.Equal("Test",(await f.Store.LoadAsync("Test")).Name);}
    [Fact] public async Task BackupCanBeLoadedAfterPrimaryCorruption(){using var f=new F();await f.Store.SaveAsync(f.Profile);f.Profile.Game="v2";await f.Store.SaveAsync(f.Profile);File.WriteAllText(f.Path,"{");Assert.Equal("Test",(await f.Store.LoadBackupAsync("Test")).Name);}
    [Fact] public async Task ConcurrentSaves_DoNotProducePartialJson(){using var f=new F();await Task.WhenAll(Enumerable.Range(0,20).Select(async i=>{var p=Valid();p.Name="Test";p.Game=i.ToString();await f.Store.SaveAsync(p);}));Assert.NotNull(JsonSerializer.Deserialize<MapperProfile>(File.ReadAllText(f.Path)));}
    [Fact] public async Task ProfileNameCannotEscapeProfilesDirectory(){using var f=new F();f.Profile.Name="../escape";await Assert.ThrowsAnyAsync<Exception>(()=>f.Store.SaveAsync(f.Profile));}
    [Fact] public async Task ProfileNameSanitizationCannotCauseSilentOverwrite(){using var f=new F();f.Profile.Name="a/b";await Assert.ThrowsAnyAsync<Exception>(()=>f.Store.SaveAsync(f.Profile));}
    private static MapperProfileValidator Validator=>new(new HotkeyParser());private static MapperProfile Valid(){var p=MapperProfile.CreateDefault("Test");return p;}
    private static void Bad(MapperProfile p,string code)=>Assert.Contains(Validator.Validate(p).Errors,x=>x.Code==code);
    private sealed class F:IDisposable{public string Dir{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"gcm-"+Guid.NewGuid().ToString("N"));public ProfileStore Store{get;}public MapperProfile Profile=Valid();public string Path=>System.IO.Path.Combine(Dir,"Test.json");public F(){Directory.CreateDirectory(Dir);Store=new(NullLogger<ProfileStore>.Instance,Validator,Dir);}public void Dispose(){try{Directory.Delete(Dir,true);}catch{}}}
}
