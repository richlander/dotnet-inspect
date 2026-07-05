using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using DotnetInspector.Fixtures;
using Xunit;

namespace runfaster.Tests;

public class E2EFixtureTests
{
    [Fact]
    public void Correlate_WithNetTrace_JoinsByMethodTokenAndIlOffset()
    {
        string tracePath = FixtureCatalog.RunFasterAllocation.AssetPath("fixture.nettrace");
        string assemblyPath = FixtureCatalog.RunFasterAllocation.AssemblyPath();
        string runfasterDll = Path.Combine(AppContext.BaseDirectory, "runfaster.dll");
        var allocateOne = typeof(RunFaster.AllocationFixture.Program).GetMethod(
            "AllocateOne",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(allocateOne);
        Assert.Equal(0x06000002, allocateOne.MetadataToken);

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

        string observedAllocateOneRow = RowContaining(
            output,
            "il-offset-hot",
            "RunFaster.AllocationFixture.Program.AllocateOne()");
        Assert.Contains("Object", observedAllocateOneRow); // AllocationKind
        Assert.Contains("Return", observedAllocateOneRow); // EscapeKind

        // Assert it preserves bytes without ambiguity inflation (the fixture trace has 11 ticks and 1.11 MB for AllocateOne)
        string confirmedAllocateOneRow = RowContaining(
            output,
            "RunFaster.AllocationFixture.Program.AllocateOne()",
            "11 alloc ticks / 1.11 MB");
        Assert.Contains("Object", confirmedAllocateOneRow); // AllocationKind
        Assert.Contains("Return", confirmedAllocateOneRow); // EscapeKind
        Assert.Contains("System.Object 1.11 MB", confirmedAllocateOneRow);

        // Assert it shows negative evidence for the unexercised row
        Assert.Contains("1 static candidate row(s) were not observed", output);
    }

    static string RowContaining(string output, params string[] fragments)
    {
        foreach (var line in output.Split('\n'))
        {
            if (fragments.All(fragment => line.Contains(fragment, StringComparison.Ordinal)))
                return line;
        }

        throw new Xunit.Sdk.XunitException($"Expected a report row containing '{string.Join("', '", fragments)}'.\nOutput:\n{output}");
    }
}
