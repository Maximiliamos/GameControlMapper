using GameControlMapper.Models;
using GameControlMapper.Services;
using Xunit;

namespace GameControlMapper.Tests;
public sealed class BetaCapabilityHardeningTests
{
    private static ApplicationCapabilities Cap=>ApplicationCapabilities.Beta;
    private static string Source(string relative)=>File.ReadAllText(Path.Combine(FindRoot(),relative.Replace('/',Path.DirectorySeparatorChar)));
    private static string FindRoot(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"GameControlMapper.sln")))d=d.Parent;return d?.FullName??throw new DirectoryNotFoundException();}
    private static ProfileValidationResult Validate(MapperProfile p)=>new MapperProfileValidator(new HotkeyParser()).Validate(p);
    [Fact] public void Capabilities_ReportsSupportedBetaFeatures()=>Assert.All(new[]{"windows-touch","keyboard-mouse","target-window","multitouch","camera","diagnostics","profile-backup"},x=>Assert.True(Cap.IsSupported(x)));
    [Fact] public void Capabilities_ReportsUnsupportedBetaFeatures()=>Assert.All(new[]{"xinput","macro-sequence","raw-input","interception","vigem","adb","pinch","rotation"},x=>Assert.Contains(Cap.Items,c=>c.Id==x&&c.Status==CapabilityStatus.UnsupportedInBeta));
    [Fact] public void UnsupportedXInput_DoesNotStartPolling()=>Assert.DoesNotContain("_gamepadMapper.Start",Source("src/GameControlMapper/Services/InputMappingEngine.cs"));
    [Fact] public void LegacyProfileWithXInput_LoadsWithWarning(){var p=MapperProfile.CreateDefault();p.Gamepad.Enabled=true;Assert.Contains(Validate(p).Warnings,x=>x.Code=="UnsupportedInBeta");}
    [Fact] public void UnsupportedMacro_IsRejectedWithoutTouch()=>AssertRejected(BindingKind.Macro);
    [Fact] public void UnsupportedSequence_IsRejectedWithoutTouch()=>AssertRejected(BindingKind.Sequence);
    [Fact] public void UnsupportedAction_DoesNotLeaveKeySuppressed()=>Assert.DoesNotContain("ExecuteBindingAsync(binding",Source("src/GameControlMapper/Services/InputMappingEngine.cs"));
    [Fact] public void UnsupportedAction_LogsStructuredReason()=>Assert.Contains("UnsupportedInBeta: binding",Source("src/GameControlMapper/Services/InputMappingEngine.cs"));
    [Fact] public void ProductionDi_UsesOnlyWindowsTouchBackend()=>Assert.Contains("AddSingleton<ITouchBackend, WindowsTouchBackend>",Source("src/GameControlMapper/App.xaml.cs"));
    [Fact] public void ProductionDi_DoesNotRegisterLegacyTouchSimulator()=>Assert.DoesNotContain("AddSingleton<ITouchSimulator",Source("src/GameControlMapper/App.xaml.cs"));
    [Fact] public void ProductionTouchPath_DoesNotUseFixedContacts()=>Assert.DoesNotContain("FixedContacts",Source("src/GameControlMapper/Services/InputMappingEngine.cs"));
    [Fact] public void ProductionTouchPath_DoesNotUseLegacyDynamicContactApi()=>Assert.DoesNotContain("ContactManager",Source("src/GameControlMapper/Services/InputMappingEngine.cs"));
    [Fact] public void DebugOverlay_UsesLeaseOwnerMetadata()=>Assert.Contains("lease.OwnerId",Source("src/GameControlMapper/ViewModels/TouchDebugViewModel.cs"));
    [Fact] public void DebugOverlay_DoesNotInferOwnerFromContactId()=>Assert.DoesNotContain("FixedContacts",Source("src/GameControlMapper/ViewModels/TouchDebugViewModel.cs"));
    [Fact] public void TwoDynamicContacts_DisplayCorrectOwners(){var a=Lease("camera:look",0);var b=Lease("joystick:move",1);Assert.NotEqual(a.OwnerId,b.OwnerId);}
    [Fact] public void CameraLease_DisplaysCameraOwner()=>Assert.StartsWith("camera:",Lease("camera:look",0).OwnerId);
    [Fact] public void JoystickLease_DisplaysBindingOwner()=>Assert.StartsWith("joystick:",Lease("joystick:Move",1).OwnerId);
    [Fact] public void MouseAreaLease_DisplaysBindingOwner()=>Assert.StartsWith("mouse-area:",Lease("mouse-area:Aim",2).OwnerId);
    [Fact] public void DiagnosticExport_IncludesCapabilityMatrix()=>Assert.Contains("Capability matrix",Source("src/GameControlMapper/Services/ProductionDiagnostics.cs"));
    [Fact] public void DiagnosticExport_MarksXInputUnsupported()=>Assert.Contains(Cap.Items,x=>x.Id=="xinput"&&x.Status==CapabilityStatus.UnsupportedInBeta);
    [Fact] public void MainViewModel_ExposesBetaVersion()=>Assert.Contains("BetaVersion",Source("src/GameControlMapper/ViewModels/MainViewModel.cs"));
    [Fact] public void InformationalVersion_ContainsCommitWhenProvided()=>Assert.Contains("SourceRevisionId",Source("Directory.Build.props"));
    [Fact] public void UnsupportedBinding_DoesNotCrashUi()=>Assert.Contains("MessageBox.Show",Source("src/GameControlMapper/ViewModels/MainViewModel.cs"));
    [Fact] public void UnsupportedBinding_DoesNotCreateAllocatorLease()=>Assert.DoesNotContain("case BindingKind.Macro:",Source("src/GameControlMapper/Services/InputMappingEngine.cs").Split("TouchContactLease? Acquire")[0]);
    [Fact] public void LegacyProfile_IsNotModifiedDuringCapabilityValidation(){var p=MapperProfile.CreateDefault();p.Gamepad.Enabled=true;var json=System.Text.Json.JsonSerializer.Serialize(p);_ = Validate(p);Assert.Equal(json,System.Text.Json.JsonSerializer.Serialize(p));}
    private static void AssertRejected(BindingKind kind){var p=MapperProfile.CreateDefault();p.Bindings.Add(new(){Name="legacy",Hotkey="F1",Kind=kind});var r=Validate(p);Assert.True(r.IsValid);Assert.Contains(r.Warnings,x=>x.Code=="UnsupportedInBeta");}
    private static TouchContactLease Lease(string owner,int id){var ctor=typeof(TouchContactLease).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Single();return (TouchContactLease)ctor.Invoke(new object[]{id,1L,owner,1L});}
}
