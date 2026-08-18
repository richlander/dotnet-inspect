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
            "shape-hot",
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

    [Theory]
    [InlineData("Evidence Method")]
    [InlineData("evidence_method")]
    [InlineData("EvidenceMethod")]
    [InlineData("evidenceMethod")]
    public void Correlate_WithEvidenceMethod_JoinsPhysicalBodyCoordinate(
        string evidenceMethodProperty)
    {
        string tracePath =
            FixtureCatalog.RunFasterAllocation.AssetPath(
                "fixture.nettrace");
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var sourceMethod =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "Main",
                BindingFlags.Public | BindingFlags.Static);
        var evidenceMethod =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "AllocateOne",
                BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(sourceMethod);
        Assert.NotNull(evidenceMethod);
        var occurrence = Assert.Single(
            LibraryBodyIndex.Open(assemblyPath)
                .GetAllocationOccurrences()[
                    evidenceMethod.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.Main()","assembly":"RunFaster.AllocationFixture","method_token":"0x{{{sourceMethod.MetadataToken:X8}}}","{{{evidenceMethodProperty}}}":"0x{{{evidenceMethod.MetadataToken:X8}}}","shape":"fixture-object","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.Object"}]}}
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
            var candidate = Assert.Single(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.Equal(
                sourceMethod.MetadataToken,
                candidate.GetProperty(
                    "methodToken").GetInt32());
            Assert.Equal(
                evidenceMethod.MetadataToken,
                candidate.GetProperty(
                    "evidenceMethodToken").GetInt32());
            Assert.StartsWith(
                $"0x{evidenceMethod.MetadataToken:X8}+",
                candidate.GetProperty(
                    "tokenIl").GetString(),
                StringComparison.Ordinal);
            Assert.True(
                candidate.GetProperty(
                    "ilOffsetJoinObserved").GetBoolean());
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_ConsumesCompiledAsyncEvidenceMethodCoordinate()
    {
        var sourceMethod =
            typeof(EvidenceMethodAsyncFixture).GetMethod(
                nameof(EvidenceMethodAsyncFixture
                    .CallsSyncSiblingFromAsync),
                BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(sourceMethod);
        var index = LibraryBodyIndex.Open(
            Path.Combine(
                AppContext.BaseDirectory,
                "runfaster.Tests.dll"));
        var opportunity = Assert.Single(
            index.OptimizationOpportunities,
            opportunity =>
                opportunity.Shape == "sync-call-in-async"
                && opportunity.Method.MetadataToken
                    == sourceMethod.MetadataToken);
        int evidenceMethodToken = Assert.IsType<int>(
            opportunity.EvidenceMethodToken);
        int ilOffset = Assert.IsType<int>(
            opportunity.ILOffset);
        Assert.NotEqual(
            sourceMethod.MetadataToken,
            evidenceMethodToken);
        Assert.Equal(
            "MoveNext",
            Assert.Single(
                index.Methods,
                method => method.MetadataToken
                    == evidenceMethodToken).Name);

        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            string member =
                $"{opportunity.Method.DeclaringType.ToQualifiedDisplayString()}"
                + $".{opportunity.Method.Name}";
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"async":[{"member":"{{{member}}}","assembly":"{{{opportunity.Method.AssemblyName}}}","method_token":"0x{{{sourceMethod.MetadataToken:X8}}}","evidence_method":"0x{{{evidenceMethodToken:X8}}}","shape":"sync-call-in-async","il":"IL_{{{ilOffset:X4}}}"}]}}
                """);

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
            Assert.Equal(
                sourceMethod.MetadataToken,
                candidate.GetProperty(
                    "methodToken").GetInt32());
            Assert.Equal(
                evidenceMethodToken,
                candidate.GetProperty(
                    "evidenceMethodToken").GetInt32());
            Assert.StartsWith(
                $"0x{evidenceMethodToken:X8}+",
                candidate.GetProperty(
                    "tokenIl").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    public static class EvidenceMethodAsyncFixture
    {
        public static int ReadValue(int value) => value;

        public static Task<int> ReadValueAsync(int value)
            => Task.FromResult(value);

        public static async Task<int>
            CallsSyncSiblingFromAsync(int value)
        {
            await Task.Yield();
            return ReadValue(value);
        }
    }

    [Fact]
    public void Correlate_DoesNotMatchSourceTextForDistinctEvidenceMethod()
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var sourceMethod =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "Main",
                BindingFlags.Public | BindingFlags.Static);
        var evidenceMethod =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "AllocateOne",
                BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(sourceMethod);
        Assert.NotNull(evidenceMethod);
        var occurrence = Assert.Single(
            LibraryBodyIndex.Open(assemblyPath)
                .GetAllocationOccurrences()[
                    evidenceMethod.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-log-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.Main()","assembly":"RunFaster.AllocationFixture","method_token":"0x{{{sourceMethod.MetadataToken:X8}}}","evidence_method":"0x{{{evidenceMethod.MetadataToken:X8}}}","shape":"object-allocation","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.Object"}]}}
                """);
            File.WriteAllText(
                logPath,
                $"RunFaster.AllocationFixture.Program.Main() "
                    + $"IL_{occurrence.ILOffset:X4} 512 bytes");

            var sourceTextResult = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, sourceTextResult.ExitCode);
            Assert.Empty(sourceTextResult.Error);
            using (var output =
                JsonDocument.Parse(sourceTextResult.Output))
            {
                Assert.Equal(
                    0,
                    output.RootElement.GetProperty(
                        "observedCandidates").GetInt32());
                var candidate = Assert.Single(
                    output.RootElement.GetProperty(
                        "candidates").EnumerateArray());
                Assert.Equal(
                    "cold-for-this-workload",
                    candidate.GetProperty(
                        "status").GetString());
            }

            File.WriteAllText(
                logPath,
                $"0x{evidenceMethod.MetadataToken:X8}"
                    + $"+{occurrence.ILOffset:X4} 512 bytes");
            var tokenResult = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, tokenResult.ExitCode);
            Assert.Empty(tokenResult.Error);
            using var tokenOutput =
                JsonDocument.Parse(tokenResult.Output);
            Assert.Equal(
                1,
                tokenOutput.RootElement.GetProperty(
                    "observedCandidates").GetInt32());
            var tokenCandidate = Assert.Single(
                tokenOutput.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.Equal(
                "confirmed-hot",
                tokenCandidate.GetProperty(
                    "status").GetString());
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"not-a-token\"")]
    [InlineData("\"0x06000000\"")]
    [InlineData("\"0x0A000001\"")]
    public void Correlate_RejectsInvalidEvidenceMethodToken(
        string evidenceMethodJson)
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.Main()","assembly":"RunFaster.AllocationFixture","method_token":"0x06000001","evidence_method":{{{evidenceMethodJson}}},"shape":"object-allocation","il":"IL_0000","allocation":"System.Object"}]}}
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Invalid evidence method token in "
                    + "'evidence_method'",
                result.Error);
            Assert.Empty(result.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Theory]
    [InlineData("Evidence Method", "")]
    [InlineData("evidence_method", " ")]
    [InlineData("EvidenceMethod", "   ")]
    [InlineData("evidenceMethod", "  ")]
    public void Correlate_TreatsBlankEvidenceMethodAsAbsent(
        string evidenceMethodProperty,
        string evidenceMethodValue)
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.Main()","assembly":"RunFaster.AllocationFixture","method_token":"0x06000001","{{{evidenceMethodProperty}}}":"{{{evidenceMethodValue}}}","shape":"object-allocation","il":"IL_0000","allocation":"System.Object"}]}}
                """);

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
            var candidate = Assert.Single(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.False(
                candidate.TryGetProperty(
                    "evidenceMethodToken",
                    out _));
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Theory]
    [InlineData("root")]
    [InlineData("performance")]
    public void Correlate_RejectsInvalidEvidenceMethodOutsideRow(
        string location)
    {
        const string row =
            """
            {"member":"RunFaster.AllocationFixture.Program.Main()","assembly":"RunFaster.AllocationFixture","method_token":"0x06000001","shape":"object-allocation","il":"IL_0000","allocation":"System.Object"}
            """;
        string document = location == "root"
            ? $$$"""
              {"evidence_method":null,"performance":{"objects":[{{{row}}}]}}
              """
            : $$$"""
              {"performance":{"evidence_method":null,"objects":[{{{row}}}]}}
              """;
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(triagePath, document);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Invalid evidence method token in "
                    + "'evidence_method'",
                result.Error);
            Assert.Empty(result.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_RejectsConflictingEvidenceMethodTokens()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.Main()","assembly":"RunFaster.AllocationFixture","method_token":"0x06000001","evidence_method":"0x06000002","EvidenceMethod":"0x06000003","shape":"object-allocation","il":"IL_0000","allocation":"System.Object"}]}}
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Conflicting evidence method tokens "
                    + "were supplied",
                result.Error);
            Assert.Empty(result.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
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
                        "module_version_id": "{{occurrence.Method.ModuleVersionId:D}}",
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
                occurrence.Method.ModuleVersionId,
                candidate.GetProperty(
                    "moduleVersionId").GetGuid());
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

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    public void Correlate_RejectsInvalidTriageModuleVersionId(
        string moduleVersionIdJson)
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$"""
                {
                  "performance": {
                    "objects": [
                      {
                        "member": "RunFaster.AllocationFixture.Program.AllocateOne()",
                        "assembly": "RunFaster.AllocationFixture",
                        "module_version_id": {{moduleVersionIdJson}},
                        "method_token": "0x06000002",
                        "shape": "object-allocation",
                        "il": "IL_0000",
                        "allocation": "System.Object"
                      }
                    ]
                  }
                }
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Invalid module version ID in 'module_version_id'",
                result.Error);
            Assert.Empty(result.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Theory]
    [InlineData("performance")]
    [InlineData("Performance")]
    public void Correlate_ValidatesEveryPerformanceSection(
        string secondSectionName)
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$"""
                {
                  "performance": {
                    "objects": [
                      {
                        "member": "RunFaster.AllocationFixture.Program.AllocateOne()",
                        "assembly": "RunFaster.AllocationFixture",
                        "method_token": "0x06000002",
                        "shape": "object-allocation",
                        "il": "IL_0000",
                        "allocation": "System.Object"
                      }
                    ]
                  },
                  "{{secondSectionName}}": {
                    "objects": [
                      {
                        "member": "RunFaster.AllocationFixture.Program.AllocateOne()",
                        "assembly": "RunFaster.AllocationFixture",
                        "module_version_id": null,
                        "method_token": "0x06000002",
                        "shape": "object-allocation",
                        "il": "IL_0000",
                        "allocation": "System.Object"
                      }
                    ]
                  }
                }
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Invalid module version ID in 'module_version_id'",
                result.Error);
            Assert.Empty(result.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_ValidatesPerformanceSectionOnRootTriageRow()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {
                  "member": "RunFaster.AllocationFixture.Program.AllocateOne()",
                  "assembly": "RunFaster.AllocationFixture",
                  "method_token": "0x06000002",
                  "shape": "object-allocation",
                  "il": "IL_0000",
                  "allocation": "System.Object",
                  "Performance": {
                    "objects": [
                      {
                        "member": "RunFaster.AllocationFixture.Program.AllocateOne()",
                        "assembly": "RunFaster.AllocationFixture",
                        "module_version_id": null,
                        "method_token": "0x06000002",
                        "shape": "object-allocation",
                        "il": "IL_0000",
                        "allocation": "System.Object"
                      }
                    ]
                  }
                }
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Invalid module version ID in 'module_version_id'",
                result.Error);
            Assert.Empty(result.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_ValidatesPerformanceSectionOnJsonlRootTriageRow()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","method_token":"0x06000002","shape":"object-allocation","il":"IL_0000","allocation":"System.Object","Performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","module_version_id":null,"method_token":"0x06000002","shape":"object-allocation","il":"IL_0000","allocation":"System.Object"}]}}
                {"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","method_token":"0x06000002","shape":"object-allocation","il":"IL_0000","allocation":"System.Object"}
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Invalid module version ID in 'module_version_id'",
                result.Error);
            Assert.Empty(result.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_ValidatesPerformanceSectionOnArrayTriageRow()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                [
                  {
                    "member": "RunFaster.AllocationFixture.Program.AllocateOne()",
                    "assembly": "RunFaster.AllocationFixture",
                    "method_token": "0x06000002",
                    "shape": "object-allocation",
                    "il": "IL_0000",
                    "allocation": "System.Object",
                    "Performance": {
                      "objects": [
                        {
                          "member": "RunFaster.AllocationFixture.Program.AllocateOne()",
                          "assembly": "RunFaster.AllocationFixture",
                          "module_version_id": null,
                          "method_token": "0x06000002",
                          "shape": "object-allocation",
                          "il": "IL_0000",
                          "allocation": "System.Object"
                        }
                      ]
                    }
                  }
                ]
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Invalid module version ID in 'module_version_id'",
                result.Error);
            Assert.Empty(result.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_RejectsConflictingTriageModuleVersionIds()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","module_version_id":"11111111-1111-1111-1111-111111111111","mvid":"22222222-2222-2222-2222-222222222222","method_token":"0x06000002","shape":"object-allocation","il":"IL_0000","allocation":"System.Object"}]}}
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Conflicting module version IDs were supplied",
                result.Error);
            Assert.Empty(result.Output);
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
            Assert.False(
                row.RootElement.TryGetProperty(
                    "module_version_id",
                    out _));
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
    public void Correlate_AcceptsRealFlattenedPerformanceTriageJsonl()
    {
        string inspectDll =
            Path.Combine(AppContext.BaseDirectory, "dotnet-inspect.dll");
        var produced = RunTool(
            inspectDll,
            "type",
            Path.Combine(
                AppContext.BaseDirectory,
                "runfaster.Tests.dll"),
            "runfaster.Tests.E2EFixtureTests",
            "-S",
            "Performance Triage",
            "--jsonl");
        Assert.Equal(0, produced.ExitCode);
        Assert.Empty(produced.Error);
        string[] rows = produced.Output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(rows);
        bool hasBlankEvidenceMethod = false;
        foreach (string rowText in rows)
        {
            using var row = JsonDocument.Parse(rowText);
            if (row.RootElement.TryGetProperty(
                    "evidence_method",
                    out var evidenceMethod)
                && evidenceMethod.ValueKind
                    == JsonValueKind.String
                && string.IsNullOrEmpty(
                    evidenceMethod.GetString()))
            {
                hasBlankEvidenceMethod = true;
            }
        }
        Assert.True(hasBlankEvidenceMethod);

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
            Assert.True(
                output.RootElement.GetProperty(
                    "staticCandidates").GetInt32() > 0);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_DoesNotTokenMatchAssemblylessFlattenedTriage()
    {
        string inspectDll =
            Path.Combine(AppContext.BaseDirectory, "dotnet-inspect.dll");
        var produced = RunTool(
            inspectDll,
            "type",
            Path.Combine(
                AppContext.BaseDirectory,
                "runfaster.Tests.dll"),
            "runfaster.Tests.E2EFixtureTests.EvidenceMethodAsyncFixture",
            "-S",
            "Performance Triage",
            "--jsonl");
        Assert.Equal(0, produced.ExitCode);
        Assert.Empty(produced.Error);
        using var producedRow =
            JsonDocument.Parse(produced.Output);
        Assert.False(
            producedRow.RootElement.TryGetProperty(
                "assembly",
                out _));
        Assert.False(
            producedRow.RootElement.TryGetProperty(
                "method_token",
                out _));
        string evidenceMethod = Assert.IsType<string>(
            producedRow.RootElement.GetProperty(
                "evidence_method").GetString());
        Assert.NotEmpty(evidenceMethod);
        string ilOffset = Assert.IsType<string>(
            producedRow.RootElement.GetProperty(
                "il").GetString());
        Assert.StartsWith(
            "IL_",
            ilOffset,
            StringComparison.Ordinal);

        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.jsonl");
        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-log-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(triagePath, produced.Output);
            File.WriteAllText(
                logPath,
                $"GC alloc at {evidenceMethod}"
                    + $"+{ilOffset["IL_".Length..]} "
                    + "in SomeOtherAssembly 512 bytes");

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output = JsonDocument.Parse(result.Output);
            Assert.Equal(
                0,
                output.RootElement.GetProperty(
                    "observedCandidates").GetInt32());
            var candidate = Assert.Single(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.False(
                candidate.GetProperty(
                    "runtimeCorrelatable").GetBoolean());
            Assert.Equal(
                "not-runtime-correlatable",
                candidate.GetProperty(
                    "status").GetString());
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
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

    [Fact]
    public void Correlate_RejectsNonPerformanceRootRow()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {"method":"RunFaster.AllocationFixture.Program.AllocateOne()","kind":"Telemetry","assembly":"RunFaster.AllocationFixture","method_token":"0x06000002","il":"IL_0000","allocation":"System.Object"}
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

    [Fact]
    public void Correlate_RejectsAllocationKindAsRootDiscriminator()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {"method":"RunFaster.AllocationFixture.Program.AllocateOne()","allocationKind":"Telemetry","assembly":"RunFaster.AllocationFixture","method_token":"0x06000002","il":"IL_0000","allocation":"System.Object"}
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

    [Theory]
    [InlineData("Wrong.Assembly", "System.Object")]
    [InlineData("RunFaster.AllocationFixture", "Other.Namespace.Object")]
    public void Correlate_RequiresMatchingAssemblyAndAllocatedType(
        string assembly,
        string allocatedType)
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var allocateOne =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "AllocateOne",
                BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(allocateOne);
        var occurrence = Assert.Single(
            LibraryBodyIndex.Open(assemblyPath)
                .GetAllocationOccurrences()[allocateOne.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"arrays":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"{{{assembly}}}","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","shape":"fixture-object","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"{{{allocatedType}}}"}]}}
                """);

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
                0,
                output.RootElement.GetProperty(
                    "observedCandidates").GetInt32());
            var candidate = Assert.Single(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.Equal(
                0,
                candidate.GetProperty("allocationBytes").GetInt64());
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_FilteredTriage_DoesNotCreditUnexportedCalleeToCaller()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.Main()","assembly":"RunFaster.AllocationFixture","method_token":"0x06000001","shape":"object-allocation","il":"IL_0000","allocation":"System.Object"}]}}
                """);

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
                0,
                output.RootElement.GetProperty(
                    "observedCandidates").GetInt32());
            var candidate = Assert.Single(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.Equal(
                "type-hot",
                candidate.GetProperty("status").GetString());
            Assert.Equal(
                0,
                candidate.GetProperty("allocationHits").GetInt32());
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_LibraryAndTriageSameSite_PrefersTriageWithoutSplittingBytes()
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var allocateOne =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "AllocateOne",
                BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(allocateOne);
        var occurrence = Assert.Single(
            LibraryBodyIndex.Open(assemblyPath)
                .GetAllocationOccurrences()[allocateOne.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"arrays":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","shape":"fixture-object","operation":"newobj","token":"0x0A000001","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.Object","provenance":"exact"}]}}
                """);

            var result = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"),
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output = JsonDocument.Parse(result.Output);
            var sameMethod = output.RootElement
                .GetProperty("candidates")
                .EnumerateArray()
                .Where(candidate => candidate
                    .GetProperty("method")
                    .GetString()!
                    .EndsWith(
                        ".AllocateOne()",
                        StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(2, sameMethod.Length);
            var triage = Assert.Single(
                sameMethod,
                candidate => candidate
                    .GetProperty("source")
                    .GetString() == "triage");
            var library = Assert.Single(
                sameMethod,
                candidate => candidate
                    .GetProperty("source")
                    .GetString() == "library");
            Assert.Equal(
                1_167_872,
                triage.GetProperty("allocationBytes").GetInt64());
            Assert.False(
                triage.GetProperty(
                    "ambiguousIlOffsetJoin").GetBoolean());
            Assert.Equal(
                0,
                library.GetProperty("allocationBytes").GetInt64());
            Assert.Equal(
                "superseded-by-triage",
                library.GetProperty("status").GetString());

            var markdown = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));
            Assert.Equal(0, markdown.ExitCode);
            Assert.Empty(markdown.Error);
            Assert.Contains(
                "1 static candidate row(s) were not observed",
                markdown.Output);
            Assert.DoesNotContain(
                "2 static candidate row(s) were not observed",
                markdown.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_KnownMvidMismatch_DoesNotCollapseLibrarySite()
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var allocateOne =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "AllocateOne",
                BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(allocateOne);
        var occurrence = Assert.Single(
            LibraryBodyIndex.Open(assemblyPath)
                .GetAllocationOccurrences()[allocateOne.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"arrays":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","module_version_id":"99999999-9999-9999-9999-999999999999","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","shape":"fixture-object","operation":"newobj","token":"0x0A000001","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.Object","provenance":"exact"}]}}
                """);

            var result = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"),
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output = JsonDocument.Parse(result.Output);
            var sameMethod = output.RootElement
                .GetProperty("candidates")
                .EnumerateArray()
                .Where(candidate => candidate
                    .GetProperty("method")
                    .GetString()!
                    .EndsWith(
                        ".AllocateOne()",
                        StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(2, sameMethod.Length);
            Assert.All(
                sameMethod,
                candidate =>
                {
                    Assert.NotEqual(
                        "superseded-by-triage",
                        candidate.GetProperty("status").GetString());
                    Assert.True(
                        candidate.GetProperty(
                            "ambiguousIlOffsetJoin").GetBoolean());
                    Assert.True(
                        candidate.GetProperty(
                            "allocationBytes").GetInt64() > 0);
                });
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
