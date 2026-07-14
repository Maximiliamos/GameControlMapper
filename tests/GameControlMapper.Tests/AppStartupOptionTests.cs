using Xunit;

namespace GameControlMapper.Tests;

public sealed class AppStartupOptionTests
{
    [Fact]
    public void DiagnosticExportOption_AcceptsSingleZipPath()
    {
        Assert.True(App.TryGetDiagnosticExportPath(["--export-diagnostics", "diagnostics.zip"], out var path));
        Assert.Equal(Path.GetFullPath("diagnostics.zip"), path);
    }

    [Theory]
    [InlineData()]
    [InlineData("--export-diagnostics")]
    [InlineData("--export-diagnostics", "diagnostics.txt")]
    [InlineData("--unknown", "diagnostics.zip")]
    [InlineData("--export-diagnostics", "diagnostics.zip", "extra")]
    public void DiagnosticExportOption_RejectsInvalidArguments(params string[] args)
    {
        Assert.False(App.TryGetDiagnosticExportPath(args, out _));
    }
}
