using GameControlMapper.TouchTestHarness;
using Xunit;
namespace GameControlMapper.Tests;
public sealed class FinalReleaseValidationTests
{
 private static readonly MonitorEnvironmentMetadata SingleMonitor=new([new("Monitor-1",0,0,1920,1080,96,96,true,100)],false,false,"Monitor-1",null,null);
 private static GuidedValidationSession Session()=>new(SingleMonitor);private static string Source(string p){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"GameControlMapper.sln")))d=d.Parent;return File.ReadAllText(Path.Combine(d!.FullName,p.Replace('/',Path.DirectorySeparatorChar)));}
 private static void Complete(GuidedValidationSession s,bool unavailable=false){foreach(var x in s.Scenarios)Assert.True(s.SetStatus(x.Id,unavailable&&x.EnvironmentOnly?ValidationStatus.NotAvailable:ValidationStatus.Passed,"ok",out _));}
 [Fact]public void GuidedValidation_ContainsAllRequiredScenarios()=>Assert.Equal(47,Session().Scenarios.Count);
 [Fact]public void GuidedValidation_CoreScenarioCannotBeNotAvailable()=>Assert.False(Session().SetStatus(1,ValidationStatus.NotAvailable,"",out _));
 [Fact]public void GuidedValidation_EnvironmentScenarioCanBeNotAvailable()=>Assert.True(Session().SetStatus(47,ValidationStatus.NotAvailable,"",out _));
 [Fact]public void GuidedValidation_FailedScenarioRequiresComment()=>Assert.False(Session().SetStatus(1,ValidationStatus.Failed,"",out _));
 [Fact]public void GuidedValidation_ProtocolErrorForcesFailure(){var s=Session();Complete(s);s.ProtocolErrors.Add("x");Assert.Equal(ValidationVerdict.Failed,s.Evaluate(0));}
 [Fact]public void GuidedValidation_ActiveContactForcesFailure(){var s=Session();Complete(s);Assert.Equal(ValidationVerdict.Failed,s.Evaluate(1));}
 [Fact]public void GuidedValidation_AllCorePassedProducesPassed(){var s=Session();Complete(s);Assert.Equal(ValidationVerdict.Passed,s.Evaluate(0));}
 [Fact]public void GuidedValidation_UnavailableMixedDpiProducesUnverifiedVerdict(){var s=Session();Complete(s,true);Assert.Equal(ValidationVerdict.PassedWithUnverifiedEnvironments,s.Evaluate(0));}
 [Fact]public void ValidationReport_ContainsCommitAndArchiveHashes()=>Assert.Contains("ApplicationArchiveSha256",Source("src/GameControlMapper.TouchTestHarness/GuidedValidation.cs"));
 [Fact]public void ValidationReport_SerializesStatusesAsStrings(){var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{GuidedValidationSession.Export(new ManualValidationReport{Verdict=ValidationVerdict.Failed,Scenarios=[new(1,"x",false){Status=ValidationStatus.NotStarted}]},path);var json=File.ReadAllText(path);Assert.Contains("\"Failed\"",json);Assert.Contains("\"NotStarted\"",json);}finally{File.Delete(path);File.Delete(Path.ChangeExtension(path,".txt"));}}
 [Fact]public void MonitorMetadata_ReportsAllMonitors(){var result=Provider(new PlatformMonitor(1,0,0,1920,1080,96,96,true),new PlatformMonitor(2,-1280,0,1280,1024,120,120,false)).Capture(0);Assert.Equal(2,result.Monitors.Count);Assert.Equal(["Monitor-1","Monitor-2"],result.Monitors.Select(x=>x.Id));}
 [Fact]public void MonitorMetadata_ReportsActualDpi(){var result=Provider(new PlatformMonitor(1,0,0,1920,1080,144,120,true)).Capture(0);var monitor=Assert.Single(result.Monitors);Assert.Equal(144,monitor.DpiX);Assert.Equal(120,monitor.DpiY);Assert.Equal(150,monitor.ScalePercent);}
 [Fact]public void MonitorMetadata_DetectsNegativeOrigin(){var result=Provider(new PlatformMonitor(1,-1920,0,1920,1080,96,96,true)).Capture(0);Assert.True(result.HasNegativeOrigin);}
 [Fact]public void MonitorMetadata_DetectsMixedDpi(){var result=Provider(new PlatformMonitor(1,0,0,1920,1080,96,96,true),new PlatformMonitor(2,1920,0,2560,1440,144,144,false)).Capture(0);Assert.True(result.HasMixedDpi);}
 [Fact]public void NotAvailable_IsRejectedWhenHardwareExists(){var environment=new MonitorEnvironmentMetadata([new("Monitor-1",0,0,1920,1080,96,96,true,100),new("Monitor-2",1920,0,1920,1080,96,96,false,100)],false,false,"Monitor-1",null,null);Assert.False(new GuidedValidationSession(environment).SetStatus(45,ValidationStatus.NotAvailable,"",out _));}
 [Fact]public void NotAvailable_IsAllowedWhenHardwareIsAbsent()=>Assert.True(Session().SetStatus(45,ValidationStatus.NotAvailable,"",out _));
 [Fact]public void ValidationReport_DoesNotContainProfileContents()=>Assert.DoesNotContain("ProfileContents",Source("src/GameControlMapper.TouchTestHarness/GuidedValidation.cs"));
 [Fact]public void ValidationReport_DoesNotContainPressedKeys()=>Assert.DoesNotContain("PressedKeys",Source("src/GameControlMapper.TouchTestHarness/GuidedValidation.cs"));
 [Fact]public void ValidationReport_DoesNotContainOtherWindowTitles()=>Assert.DoesNotContain("WindowTitle",Source("src/GameControlMapper.TouchTestHarness/GuidedValidation.cs"));
 [Fact]public void ValidationReport_RedactsUserPaths()=>Assert.DoesNotContain("UserProfile",Source("src/GameControlMapper.TouchTestHarness/GuidedValidation.cs"));
 [Fact]public void ValidationScript_AcceptsValidPassedReport(){var result=RunValidation(_=>{});Assert.True(result.ExitCode==0,result.Output);}
 [Fact]public void ValidationScript_RejectsFailedScenario()=>Assert.NotEqual(0,RunValidation(r=>r.Scenarios[0].Status=ValidationStatus.Failed).ExitCode);
 [Fact]public void ValidationScript_RejectsIncompleteScenario()=>Assert.NotEqual(0,RunValidation(r=>r.Scenarios[0].Status=ValidationStatus.NotStarted).ExitCode);
 [Fact]public void ValidationScript_RejectsWrongCommit()=>Assert.Contains("Wrong commit hash",Script());
 [Fact]public void ValidationScript_RejectsWrongVersion()=>Assert.Contains("Wrong product version",Script());
 [Fact]public void ValidationScript_RejectsArchiveHashMismatch()=>Assert.Contains("archive hash mismatch",Script());
 [Fact]public void ValidationScript_RejectsProtocolErrors()=>Assert.Contains("Protocol errors",Script());
 [Fact]public void ValidationScript_RejectsActiveContacts()=>Assert.Contains("Active contacts remain",Script());
 [Fact]public void ValidationScript_AllowsUnavailableHardwareScenario(){var result=RunValidation(r=>{r.Scenarios.Single(x=>x.Id==45).Status=ValidationStatus.NotAvailable;r.Verdict=ValidationVerdict.PassedWithUnverifiedEnvironments;});Assert.True(result.ExitCode==0,result.Output);}
 [Fact]public void ValidationScript_RejectsUnavailableCoreScenario()=>Assert.NotEqual(0,RunValidation(r=>{r.Scenarios[0].Status=ValidationStatus.NotAvailable;r.Verdict=ValidationVerdict.PassedWithUnverifiedEnvironments;}).ExitCode);
 [Fact]public void ValidationScript_DoesNotTrustEnvironmentOnlyFromJson()=>Assert.NotEqual(0,RunValidation(r=>{r.Scenarios[0].Status=ValidationStatus.NotAvailable;r.Verdict=ValidationVerdict.PassedWithUnverifiedEnvironments;}).ExitCode);
 [Fact]public void ValidationScript_UsesKnownScenarioIds(){var result=RunValidation(_=>{});Assert.True(result.ExitCode==0,result.Output);}
 [Fact]public void ValidationScript_RejectsMissingRequiredScenario()=>Assert.NotEqual(0,RunValidation(r=>r.Scenarios.RemoveAt(0)).ExitCode);
 [Fact]public void ValidationScript_RejectsDuplicateScenarioIds()=>Assert.NotEqual(0,RunValidation(r=>r.Scenarios[1]=new ValidationScenario(1,r.Scenarios[1].Name){Status=ValidationStatus.Passed}).ExitCode);
 [Fact]public void ValidationScript_RejectsUnknownScenarioIds()=>Assert.NotEqual(0,RunValidation(r=>r.Scenarios[0]=new ValidationScenario(99,r.Scenarios[0].Name){Status=ValidationStatus.Passed}).ExitCode);
 [Fact]public void ValidationScript_RejectsModifiedScenarioNames()=>Assert.NotEqual(0,RunValidation(r=>r.Scenarios[0]=new ValidationScenario(1,"Modified"){Status=ValidationStatus.Passed}).ExitCode);
 [Fact]public void CapabilityMatrix_SupportedRequiresManualPass()=>Assert.Contains("manualValidationVerdict",Source("scripts/finalize-release.ps1"));
 [Fact]public void CapabilityMatrix_UnverifiedEnvironmentIsNotSupported()=>Assert.Contains("automatedOnly",Source("scripts/finalize-release.ps1"));
 [Fact]public void FinalManifest_ManualValidationCannotBePassedWithoutReport()=>Assert.Contains("validate-manual-release.ps1",Source("scripts/finalize-release.ps1"));
 [Fact]public void ReleaseFinalization_RefusesUnvalidatedCandidate()=>Assert.Contains("Manual validation failed",Source("scripts/finalize-release.ps1"));
 [Fact]public void ReleaseFinalization_AcceptsValidatedCandidate()=>Assert.Contains("Final release created",Source("scripts/finalize-release.ps1"));
 [Fact]public void ReleaseNotes_DoNotClaimUnsupportedFeatures(){var text=Source("README.md");Assert.Contains("XInput не поддерживается",text);}
 private static string Script()=>Source("scripts/validate-manual-release.ps1");
 private static WindowsMonitorInformationProvider Provider(params PlatformMonitor[] monitors)=>new(new FakeMonitorPlatform(monitors));
 private sealed class FakeMonitorPlatform(PlatformMonitor[] monitors):IMonitorPlatform{public IReadOnlyList<PlatformMonitor> Enumerate()=>monitors;public nint MonitorFromWindow(nint window)=>window;}
 private static (int ExitCode,string Output) RunValidation(Action<ManualValidationReport> mutate)
 {
  var root=Path.Combine(Path.GetTempPath(),"gcm-validation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   var app=Path.Combine(root,"app.zip");var harness=Path.Combine(root,"harness.zip");File.WriteAllText(app,"app");File.WriteAllText(harness,"harness");
   var session=Session();Complete(session);var report=session.CreateReport(new TouchLifecycleTracker(),"1.0.0-beta.2","abc123","abc123",app,harness,monitorProvider:new FakeMonitorProvider(SingleMonitor));mutate(report);
   var reportPath=Path.Combine(root,"report.json");GuidedValidationSession.Export(report,reportPath);
   var start=new System.Diagnostics.ProcessStartInfo("powershell",$"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(Root(),"scripts","validate-manual-release.ps1")}\" -ReportPath \"{reportPath}\" -ApplicationArchive \"{app}\" -HarnessArchive \"{harness}\" -ExpectedVersion 1.0.0-beta.2 -ExpectedCommit abc123") { RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false };
   using var process=System.Diagnostics.Process.Start(start)!;var output=process.StandardOutput.ReadToEnd()+process.StandardError.ReadToEnd();process.WaitForExit();return(process.ExitCode,output);
  }
  finally{try{Directory.Delete(root,true);}catch{}}
 }
 private static string Root(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"GameControlMapper.sln")))d=d.Parent;return d!.FullName;}
 private sealed class FakeMonitorProvider(MonitorEnvironmentMetadata environment):IMonitorInformationProvider{public MonitorEnvironmentMetadata Capture(nint harnessWindow,nint targetWindow=0)=>environment;}
}
