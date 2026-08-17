using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
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

    [Fact]
    public void Correlate_WithNestedTriageJson_JoinsByDeclaringMethodCoordinate()
    {
        string tracePath =
            FixtureCatalog.RunFasterAllocation.AssetPath("fixture.nettrace");
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var allocateOne =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "AllocateOne",
                BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(allocateOne);

        var index = LibraryBodyIndex.Open(assemblyPath);
        var occurrence = Assert.Single(
            index.GetAllocationOccurrences()[allocateOne.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$"""
                {
                  "assembly_info": {
                    "assembly_name": "{{occurrence.Method.AssemblyName}}"
                  },
                  "performance": {
                    "arrays": [
                      {
                        "member": "RunFaster.AllocationFixture.Program.AllocateOne()",
                        "assembly": "{{occurrence.Method.AssemblyName}}",
                        "method_token": "0x{{allocateOne.MetadataToken:X8}}",
                        "shape": "fixture-object",
                        "operation": "newobj",
                        "token": "0x0A000001",
                        "il": "IL_{{occurrence.ILOffset:X4}}",
                        "allocation": "System.Object",
                        "provenance": "exact"
                      }
                    ]
                  }
                }
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                tracePath,
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output = JsonDocument.Parse(result.Output);
            Assert.Equal(
                1,
                output.RootElement.GetProperty(
                    "observedCandidates").GetInt32());
            var triageInput = Assert.Single(
                output.RootElement.GetProperty(
                    "triageInputs").EnumerateArray());
            Assert.Equal(
                1,
                triageInput.GetProperty(
                    "correlatableRows").GetInt32());
            var candidate = Assert.Single(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.True(
                candidate.GetProperty(
                    "runtimeCorrelatable").GetBoolean());
            Assert.Equal(
                allocateOne.MetadataToken,
                candidate.GetProperty("methodToken").GetInt32());
            Assert.Equal(
                0x0A000001,
                candidate.GetProperty("operandToken").GetInt32());
            Assert.True(
                candidate.GetProperty(
                    "ilOffsetJoinObserved").GetBoolean());
            Assert.True(
                candidate.GetProperty(
                    "unambiguousIlOffsetJoinObserved").GetBoolean());
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_WithCompactTriageJsonl_DoesNotTreatOperandAsMethodToken()
    {
        string tracePath =
            FixtureCatalog.RunFasterAllocation.AssetPath("fixture.nettrace");
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {"kind":"Arrays","member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","shape":"fixture-object","token":"0x06000002","il":"IL_0000","allocation":"System.Object"}
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                tracePath);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            Assert.Contains(
                "Runtime-correlatable triage rows: 0/1",
                result.Output);
            Assert.Contains(
                "lack a complete declaring assembly + method token + IL offset",
                result.Output);
            Assert.DoesNotContain(
                "joined to a single nearest-preceding static allocation site",
                result.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    static (int ExitCode, string Output, string Error) RunCorrelate(
        params string[] arguments)
    {
        string runfasterDll =
            Path.Combine(AppContext.BaseDirectory, "runfaster.dll");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(runfasterDll);
        startInfo.ArgumentList.Add("correlate");
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var correlate = Process.Start(startInfo);
        Assert.NotNull(correlate);
        string output = correlate.StandardOutput.ReadToEnd();
        string error = correlate.StandardError.ReadToEnd();
        correlate.WaitForExit();
        return (correlate.ExitCode, output, error);
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
