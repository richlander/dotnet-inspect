using System;
using System.IO;
using System.Diagnostics;
using Xunit;

namespace runfaster.Tests;

public class E2EFixtureTests
{
    [Fact]
    public void Correlate_WithNetTrace_JoinsByMethodTokenAndIlOffset()
    {
        string fixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RunFaster.AllocationFixture");
        string tracePath = Path.Combine(fixtureDir, "fixture.nettrace");
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "RunFaster.AllocationFixture.dll");
        string runfasterDll = Path.Combine(AppContext.BaseDirectory, "runfaster.dll");

        var correlate = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"exec \"{runfasterDll}\" correlate --library \"{assemblyPath}\" --trace \"{tracePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        
        Assert.NotNull(correlate);
        string output = correlate.StandardOutput.ReadToEnd();
        string error = correlate.StandardError.ReadToEnd();
        correlate.WaitForExit();

        Assert.True(0 == correlate.ExitCode, $"runfaster failed with exit code {correlate.ExitCode}.\nError: {error}\nOutput: {output}");
        
        // Assert the report marks the expected row as il-offset-hot
        Assert.Contains("il-offset-hot", output);
        Assert.Contains("RunFaster.AllocationFixture.Program.AllocateOne()", output);
        
        // Assert it surfaces AllocationKind + EscapeKind
        Assert.Contains("Object", output); // AllocationKind
        Assert.Contains("Return", output); // EscapeKind
        
        // Assert it preserves bytes without ambiguity inflation (the fixture trace has 11 ticks and 1.11 MB for AllocateOne)
        Assert.Contains("11 alloc ticks / 1.11 MB", output);
        Assert.Contains("System.Object 1.11 MB", output);
        
        // Assert it shows negative evidence for the unexercised row
        Assert.Contains("1 static candidate row(s) were not observed", output); 
    }
}
