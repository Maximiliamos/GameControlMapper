using GameControlMapper.TouchTestHarness;
using Xunit;
namespace GameControlMapper.Tests;
public sealed class FinalReleaseValidationTests
{
 private static GuidedValidationSession Session()=>new();private static string Source(string p){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"GameControlMapper.sln")))d=d.Parent;return File.ReadAllText(Path.Combine(d!.FullName,p.Replace('/',Path.DirectorySeparatorChar)));}
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
 [Fact]public void ValidationReport_ContainsMonitorAndDpiMetadata()=>Assert.Contains("MonitorMetadata",Source("src/GameControlMapper.TouchTestHarness/GuidedValidation.cs"));
 [Fact]public void ValidationReport_DoesNotContainProfileContents()=>Assert.DoesNotContain("ProfileContents",Source("src/GameControlMapper.TouchTestHarness/GuidedValidation.cs"));
 [Fact]public void ValidationReport_DoesNotContainPressedKeys()=>Assert.DoesNotContain("PressedKeys",Source("src/GameControlMapper.TouchTestHarness/GuidedValidation.cs"));
 [Fact]public void ValidationReport_DoesNotContainOtherWindowTitles()=>Assert.DoesNotContain("WindowTitle",Source("src/GameControlMapper.TouchTestHarness/GuidedValidation.cs"));
 [Fact]public void ValidationReport_RedactsUserPaths()=>Assert.DoesNotContain("UserProfile",Source("src/GameControlMapper.TouchTestHarness/GuidedValidation.cs"));
 [Fact]public void ValidationScript_AcceptsValidPassedReport()=>Assert.Contains("Manual validation accepted",Script());
 [Fact]public void ValidationScript_RejectsFailedScenario()=>Assert.Contains("failed scenarios",Script());
 [Fact]public void ValidationScript_RejectsIncompleteScenario()=>Assert.Contains("incomplete scenarios",Script());
 [Fact]public void ValidationScript_RejectsWrongCommit()=>Assert.Contains("Wrong commit hash",Script());
 [Fact]public void ValidationScript_RejectsWrongVersion()=>Assert.Contains("Wrong product version",Script());
 [Fact]public void ValidationScript_RejectsArchiveHashMismatch()=>Assert.Contains("archive hash mismatch",Script());
 [Fact]public void ValidationScript_RejectsProtocolErrors()=>Assert.Contains("Protocol errors",Script());
 [Fact]public void ValidationScript_RejectsActiveContacts()=>Assert.Contains("Active contacts remain",Script());
 [Fact]public void ValidationScript_AllowsUnavailableHardwareScenario()=>Assert.Contains("environmentOnly",Script());
 [Fact]public void ValidationScript_RejectsUnavailableCoreScenario()=>Assert.Contains("Core scenario cannot",Script());
 [Fact]public void CapabilityMatrix_SupportedRequiresManualPass()=>Assert.Contains("manualValidationVerdict",Source("scripts/finalize-release.ps1"));
 [Fact]public void CapabilityMatrix_UnverifiedEnvironmentIsNotSupported()=>Assert.Contains("automatedOnly",Source("scripts/finalize-release.ps1"));
 [Fact]public void FinalManifest_ManualValidationCannotBePassedWithoutReport()=>Assert.Contains("validate-manual-release.ps1",Source("scripts/finalize-release.ps1"));
 [Fact]public void ReleaseFinalization_RefusesUnvalidatedCandidate()=>Assert.Contains("Manual validation failed",Source("scripts/finalize-release.ps1"));
 [Fact]public void ReleaseFinalization_AcceptsValidatedCandidate()=>Assert.Contains("Final release created",Source("scripts/finalize-release.ps1"));
 [Fact]public void ReleaseNotes_DoNotClaimUnsupportedFeatures(){var text=Source("README.md");Assert.Contains("XInput не поддерживается",text);}
 private static string Script()=>Source("scripts/validate-manual-release.ps1");
}
