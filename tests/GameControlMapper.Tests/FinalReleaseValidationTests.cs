using System.Diagnostics;
using System.Text.Json;
using GameControlMapper.TouchTestHarness;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class FinalReleaseValidationTests
{
    private static readonly MonitorEnvironmentMetadata SingleMonitor = new(
        [new("Monitor-1", 0, 0, 1920, 1080, 96, 96, true, 100)], false, false, "Monitor-1", null, null);

    [Fact] public void GuidedValidation_ContainsAllRequiredScenarios() => Assert.Equal(47, Session().Scenarios.Count);
    [Fact] public void GuidedValidation_CoreScenarioCannotBeNotAvailable() => Assert.False(Session().SetStatus(1, ValidationStatus.NotAvailable, "", out _));
    [Fact] public void GuidedValidation_EnvironmentScenarioCanBeNotAvailable() => Assert.True(Session().SetStatus(47, ValidationStatus.NotAvailable, "", out _));
    [Fact] public void GuidedValidation_FailedScenarioRequiresComment() => Assert.False(Session().SetStatus(1, ValidationStatus.Failed, "", out _));
    [Fact] public void GuidedValidation_ProtocolErrorForcesFailure() { var session=CompletedSession();session.ProtocolErrors.Add("x");Assert.Equal(ValidationVerdict.Failed,session.Evaluate(0)); }
    [Fact] public void GuidedValidation_ActiveContactForcesFailure() => Assert.Equal(ValidationVerdict.Failed, CompletedSession().Evaluate(1));
    [Fact] public void GuidedValidation_AllCorePassedProducesPassed() => Assert.Equal(ValidationVerdict.Passed, CompletedSession().Evaluate(0));
    [Fact] public void GuidedValidation_UnavailableEnvironmentProducesUnverifiedVerdict() { var session=Session();Complete(session,true);Assert.Equal(ValidationVerdict.PassedWithUnverifiedEnvironments,session.Evaluate(0)); }

    [Fact]
    public void ValidationReport_SerializesRequiredIdentityAndHashesWithoutPrivateFields()
    {
        using var fixture=new ValidationFixture();var report=fixture.CreateReport();var json=JsonSerializer.Serialize(report);
        Assert.Equal("1.0.0-beta.2",report.ProductVersion);Assert.Equal("abc123",report.CommitHash);
        Assert.Equal(64,report.ApplicationArchiveSha256.Length);Assert.Equal(64,report.HarnessArchiveSha256.Length);
        Assert.DoesNotContain("ProfileContents",json);Assert.DoesNotContain("PressedKeys",json);Assert.DoesNotContain("WindowTitle",json);Assert.DoesNotContain("UserProfile",json);
    }

    [Fact]
    public void ValidationReport_SerializesStatusesAsStrings()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try { GuidedValidationSession.Export(new ManualValidationReport{Verdict=ValidationVerdict.Failed,Scenarios=[new(1,"x",false){Status=ValidationStatus.NotStarted}]},path);var json=File.ReadAllText(path);Assert.Contains("\"Failed\"",json);Assert.Contains("\"NotStarted\"",json); }
        finally { File.Delete(path);File.Delete(Path.ChangeExtension(path,".txt")); }
    }

    [Fact] public void MonitorMetadata_ReportsAllMonitors() { var result=Provider(new PlatformMonitor(1,0,0,1920,1080,96,96,true),new PlatformMonitor(2,-1280,0,1280,1024,120,120,false)).Capture(0);Assert.Equal(2,result.Monitors.Count);Assert.Equal(["Monitor-1","Monitor-2"],result.Monitors.Select(x=>x.Id)); }
    [Fact] public void MonitorMetadata_ReportsActualDpi() { var monitor=Assert.Single(Provider(new PlatformMonitor(1,0,0,1920,1080,144,120,true)).Capture(0).Monitors);Assert.Equal(144,monitor.DpiX);Assert.Equal(120,monitor.DpiY);Assert.Equal(150,monitor.ScalePercent); }
    [Fact] public void MonitorMetadata_DetectsNegativeOrigin() => Assert.True(Provider(new PlatformMonitor(1,-1920,0,1920,1080,96,96,true)).Capture(0).HasNegativeOrigin);
    [Fact] public void MonitorMetadata_DetectsMixedDpi() => Assert.True(Provider(new PlatformMonitor(1,0,0,1920,1080,96,96,true),new PlatformMonitor(2,1920,0,2560,1440,144,144,false)).Capture(0).HasMixedDpi);
    [Fact] public void NotAvailable_IsRejectedWhenHardwareExists() { var environment=new MonitorEnvironmentMetadata([new("Monitor-1",0,0,1920,1080,96,96,true,100),new("Monitor-2",1920,0,1920,1080,96,96,false,100)],false,false,"Monitor-1",null,null);Assert.False(new GuidedValidationSession(environment).SetStatus(45,ValidationStatus.NotAvailable,"",out _)); }
    [Fact] public void NotAvailable_IsAllowedWhenHardwareIsAbsent() => Assert.True(Session().SetStatus(45,ValidationStatus.NotAvailable,"",out _));

    [Fact] public void GuidedEvidence_TapRequiresDownAndUp() { var s=Session();var t=new TouchLifecycleTracker();s.BeginScenario(1,t);Assert.False(s.CanPass(1,t,out _));t.Process(7,10,10,HarnessTouchState.Down);t.Process(7,10,10,HarnessTouchState.Up);Assert.True(s.CanPass(1,t,out _)); }
    [Fact] public void GuidedEvidence_HoldRequiresDuration() { var s=Session();var t=new TouchLifecycleTracker();s.BeginScenario(2,t);var start=DateTimeOffset.Now;t.Process(7,10,10,HarnessTouchState.Down,start);t.Process(7,10,10,HarnessTouchState.Up,start.AddMilliseconds(600));Assert.True(s.CanPass(2,t,out _)); }
    [Fact] public void GuidedEvidence_DoubleTapRequiresTwoLifecycles() { var s=Session();var t=new TouchLifecycleTracker();s.BeginScenario(3,t);foreach(var id in new[]{7,8}){t.Process(id,10,10,HarnessTouchState.Down);t.Process(id,10,10,HarnessTouchState.Up);}Assert.True(s.CanPass(3,t,out _)); }
    [Fact] public void GuidedEvidence_CameraRequiresMeasuredTwoMinutes() { var s=Session();var t=new TouchLifecycleTracker();s.BeginScenario(9,t);var start=DateTimeOffset.Now;t.Process(7,10,10,HarnessTouchState.Down,start);t.Process(7,20,10,HarnessTouchState.Move,start.AddMinutes(1));t.Process(7,20,10,HarnessTouchState.Up,start.AddMinutes(2));Assert.True(s.CanPass(9,t,out _)); }
    [Fact] public void GuidedEvidence_MultitouchUsesMeasuredConcurrency() { var s=Session();var t=new TouchLifecycleTracker();s.BeginScenario(15,t);t.Process(7,10,10,HarnessTouchState.Down);t.Process(8,20,10,HarnessTouchState.Down);t.Process(7,10,10,HarnessTouchState.Up);t.Process(8,20,10,HarnessTouchState.Up);Assert.True(s.CanPass(15,t,out _)); }

    [Fact] public void ValidationScript_AcceptsValidPassedReport() => AssertSuccess(RunValidation());
    [Fact] public void ValidationScript_RejectsFailedScenario() => AssertFailure(RunValidation(r=>r.Scenarios[0].Status=ValidationStatus.Failed));
    [Fact] public void ValidationScript_RejectsIncompleteScenario() => AssertFailure(RunValidation(r=>r.Scenarios[0].Status=ValidationStatus.NotStarted));
    [Fact] public void ValidationScript_RejectsWrongCommit() => AssertFailure(RunValidation(expectedCommit:"def456"));
    [Fact] public void ValidationScript_RejectsWrongVersion() => AssertFailure(RunValidation(expectedVersion:"9.9.9"));
    [Fact] public void ValidationScript_RejectsArchiveHashMismatch() => AssertFailure(RunValidation(afterExport:fixture=>File.AppendAllText(fixture.ApplicationArchive,"changed")));
    [Fact] public void ValidationScript_RejectsProtocolErrors() => AssertFailure(RunValidation(r=>r.ProtocolErrors.Add("protocol")));
    [Fact] public void ValidationScript_RejectsActiveContacts() => AssertFailure(RunValidation(r=>r.ActiveContactsAtEnd=1));
    [Fact] public void ValidationScript_AllowsUnavailableHardwareScenario() { var result=RunValidation(r=>{r.Scenarios.Single(x=>x.Id==45).Status=ValidationStatus.NotAvailable;r.Verdict=ValidationVerdict.PassedWithUnverifiedEnvironments;});AssertSuccess(result); }
    [Fact] public void ValidationScript_RejectsUnavailableCoreScenario() => AssertFailure(RunValidation(r=>{r.Scenarios[0].Status=ValidationStatus.NotAvailable;r.Verdict=ValidationVerdict.PassedWithUnverifiedEnvironments;}));
    [Fact] public void ValidationScript_RejectsMissingRequiredScenario() => AssertFailure(RunValidation(r=>r.Scenarios.RemoveAt(0)));
    [Fact] public void ValidationScript_RejectsDuplicateScenarioIds() => AssertFailure(RunValidation(r=>r.Scenarios[1]=new(1,r.Scenarios[1].Name){Status=ValidationStatus.Passed}));
    [Fact] public void ValidationScript_RejectsUnknownScenarioIds() => AssertFailure(RunValidation(r=>r.Scenarios[0]=new(99,r.Scenarios[0].Name){Status=ValidationStatus.Passed}));
    [Fact] public void ValidationScript_RejectsModifiedScenarioNames() => AssertFailure(RunValidation(r=>r.Scenarios[0]=new(1,"Modified"){Status=ValidationStatus.Passed}));
    [Fact] public void Finalizer_RejectsNonFinalVersionByExecutingScript() { var result=RunPowerShell(Path.Combine(Root(),"scripts","finalize-release.ps1"),"-ReportPath x -Version 1.0.0-beta.2 -Commit x -ApplicationArchive x -HarnessArchive x -OutputDirectory x");AssertFailure(result);Assert.Contains("Final release version must be 1.0.0",result.Output); }

    private static GuidedValidationSession Session() => new(SingleMonitor);
    private static GuidedValidationSession CompletedSession() { var session=Session();Complete(session);return session; }
    private static void Complete(GuidedValidationSession session,bool unavailable=false) { foreach(var scenario in session.Scenarios)Assert.True(session.SetStatus(scenario.Id,unavailable&&scenario.EnvironmentOnly?ValidationStatus.NotAvailable:ValidationStatus.Passed,"ok",out _)); }
    private static WindowsMonitorInformationProvider Provider(params PlatformMonitor[] monitors) => new(new FakeMonitorPlatform(monitors));
    private static void AssertSuccess((int ExitCode,string Output) result) => Assert.True(result.ExitCode==0,result.Output);
    private static void AssertFailure((int ExitCode,string Output) result) => Assert.True(result.ExitCode!=0,"Expected failure but command succeeded: "+result.Output);

    private static (int ExitCode,string Output) RunValidation(Action<ManualValidationReport>? mutate=null,string expectedVersion="1.0.0-beta.2",string expectedCommit="abc123",Action<ValidationFixture>? afterExport=null)
    {
        using var fixture=new ValidationFixture();var report=fixture.CreateReport();mutate?.Invoke(report);GuidedValidationSession.Export(report,fixture.ReportPath);afterExport?.Invoke(fixture);
        return RunPowerShell(Path.Combine(Root(),"scripts","validate-manual-release.ps1"),$"-ReportPath \"{fixture.ReportPath}\" -ApplicationArchive \"{fixture.ApplicationArchive}\" -HarnessArchive \"{fixture.HarnessArchive}\" -ExpectedVersion {expectedVersion} -ExpectedCommit {expectedCommit}");
    }

    private static (int ExitCode,string Output) RunPowerShell(string script,string arguments)
    {
        var start=new ProcessStartInfo("powershell",$"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {arguments}"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEnd()+process.StandardError.ReadToEnd();process.WaitForExit();return(process.ExitCode,output);
    }

    private static string Root() { var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"GameControlMapper.sln")))directory=directory.Parent;return directory?.FullName??throw new DirectoryNotFoundException(); }
    private sealed class FakeMonitorPlatform(PlatformMonitor[] monitors):IMonitorPlatform { public IReadOnlyList<PlatformMonitor> Enumerate()=>monitors;public nint MonitorFromWindow(nint window)=>window; }
    private sealed class FakeMonitorProvider(MonitorEnvironmentMetadata environment):IMonitorInformationProvider { public MonitorEnvironmentMetadata Capture(nint harnessWindow,nint targetWindow=0)=>environment; }

    private sealed class ValidationFixture:IDisposable
    {
        public string DirectoryPath{get;}=Path.Combine(Path.GetTempPath(),"gcm-validation-"+Guid.NewGuid().ToString("N"));
        public string ApplicationArchive=>Path.Combine(DirectoryPath,"app.zip");public string HarnessArchive=>Path.Combine(DirectoryPath,"harness.zip");public string ReportPath=>Path.Combine(DirectoryPath,"report.json");
        public ValidationFixture(){Directory.CreateDirectory(DirectoryPath);File.WriteAllText(ApplicationArchive,"app");File.WriteAllText(HarnessArchive,"harness");}
        public ManualValidationReport CreateReport(){var session=CompletedSession();var report=session.CreateReport(new TouchLifecycleTracker(),"1.0.0-beta.2","abc123","abc123",ApplicationArchive,HarnessArchive,monitorProvider:new FakeMonitorProvider(SingleMonitor));foreach(var scenario in report.Scenarios.Where(item=>GuidedValidationSession.RequiresMachineEvidence(item.Id))){scenario.Evidence=new(){StartedAt=DateTimeOffset.Now.AddSeconds(-1),CompletedAt=DateTimeOffset.Now,EventCount=1,AutomaticVerdict=ValidationStatus.Passed,AutomaticReason="test evidence"};scenario.UserVerdict=ValidationStatus.Passed;scenario.FinalVerdict=ValidationStatus.Passed;}return report;}
        public void Dispose(){try{Directory.Delete(DirectoryPath,true);}catch{}}
    }
}
