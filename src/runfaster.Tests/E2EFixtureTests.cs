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
                  "assembly_info": null,
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
                {"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","method_token":"0x0A000001","token":"0x06000002","il":"IL_0000","evidence":"new object","allocation":"Fixture.Unseen","reach":"1","priority":"high","confidence":"high"}
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
            Assert.Contains(
                "not-runtime-correlatable",
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

    [Fact]
    public void Correlate_AcceptsRealSingleKindPerformanceJsonl()
    {
        string inspectDll =
            Path.Combine(AppContext.BaseDirectory, "dotnet-inspect.dll");
        var produced = RunTool(
            inspectDll,
            "library",
            Path.Combine(
                AppContext.BaseDirectory,
                "ILInspector.Analysis.dll"),
            "-S",
            "Performance:*",
            "--top",
            "1",
            "--jsonl",
            "--tips",
            "q");
        Assert.Equal(0, produced.ExitCode);
        Assert.Empty(produced.Error);
        using (var row = JsonDocument.Parse(produced.Output))
        {
            Assert.False(row.RootElement.TryGetProperty("kind", out _));
            Assert.False(row.RootElement.TryGetProperty("shape", out _));
            Assert.True(row.RootElement.TryGetProperty("priority", out _));
        }

        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(triagePath, produced.Output);
            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"),
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output = JsonDocument.Parse(result.Output);
            Assert.Equal(
                1,
                output.RootElement.GetProperty(
                    "staticCandidates").GetInt32());
            var candidate = Assert.Single(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.Equal(
                "not-runtime-correlatable",
                candidate.GetProperty("status").GetString());
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_RejectsNonPerformanceJsonDocument()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {"unrelated":{"rows":[{"method":"Other.Component.Work()","kind":"Telemetry","assembly":"Other","method_token":"0x06000001","il":"IL_0000"}]}}
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "contains no Performance Triage rows",
                result.Error);
            Assert.Empty(result.Output);
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
        return RunTool(
            runfasterDll,
            ["correlate", .. arguments]);
    }

    static (int ExitCode, string Output, string Error) RunTool(
        string toolDll,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(toolDll);
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
