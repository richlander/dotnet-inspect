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
    public void Correlate_SourceTextDoesNotConfirmPhysicalEvidenceOffset()
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
        string speedscopePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-{Guid.NewGuid():N}.speedscope.json");
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
                    + "IL_FFFE 512 bytes");

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
                    1,
                    output.RootElement.GetProperty(
                        "observedCandidates").GetInt32());
                var candidate = Assert.Single(
                    output.RootElement.GetProperty(
                        "candidates").EnumerateArray());
                Assert.Equal(
                    "method-hot",
                    candidate.GetProperty(
                        "status").GetString());
                Assert.False(
                    candidate.GetProperty(
                        "exactOffsetObserved").GetBoolean());
                Assert.Equal(
                    512,
                    candidate.GetProperty(
                        "runtimeBytes").GetInt64());
            }

            File.WriteAllText(
                speedscopePath,
                """
                {"shared":{"frames":[{"name":"RunFaster.AllocationFixture.Program.Main()"}]},"profiles":[{"type":"sampled","samples":[[0]],"weights":[100]}]}
                """);
            var sampledResult = RunCorrelate(
                "--triage",
                triagePath,
                "--input",
                speedscopePath,
                "--json");

            Assert.Equal(0, sampledResult.ExitCode);
            Assert.Empty(sampledResult.Error);
            using (var sampledOutput =
                JsonDocument.Parse(sampledResult.Output))
            {
                Assert.Equal(
                    1,
                    sampledOutput.RootElement.GetProperty(
                        "observedCandidates").GetInt32());
                var candidate = Assert.Single(
                    sampledOutput.RootElement.GetProperty(
                        "candidates").EnumerateArray());
                Assert.Equal(
                    "method-hot",
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
            File.Delete(speedscopePath);
        }
    }

    [Fact]
    public void Correlate_TextCoordinateSupersedesMatchingLibraryRow()
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
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.Main()","assembly":"{{{occurrence.Method.AssemblyName}}}","module_version_id":"{{{occurrence.Method.ModuleVersionId:D}}}","method_token":"0x{{{sourceMethod.MetadataToken:X8}}}","evidence_method":"0x{{{evidenceMethod.MetadataToken:X8}}}","shape":"object-allocation","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.Object"},{"member":"RunFaster.AllocationFixture.Program.Main()","assembly":"{{{occurrence.Method.AssemblyName}}}","module_version_id":"{{{occurrence.Method.ModuleVersionId:D}}}","method_token":"0x{{{sourceMethod.MetadataToken:X8}}}","evidence_method":"0x{{{evidenceMethod.MetadataToken:X8}}}","shape":"fixture-object","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.Object"}]}}
                """);
            File.WriteAllText(
                logPath,
                $"0x{evidenceMethod.MetadataToken:X8}"
                    + $"+{occurrence.ILOffset:X4} "
                    + "RunFaster.AllocationFixture.Program.AllocateOne() "
                    + "512 bytes");

            var result = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output = JsonDocument.Parse(result.Output);
            var coordinateRows = output.RootElement
                .GetProperty("candidates")
                .EnumerateArray()
                .Where(candidate =>
                    candidate.GetProperty(
                        "tokenIl").GetString()
                    == $"0x{evidenceMethod.MetadataToken:X8}"
                        + $"+IL_{occurrence.ILOffset:X4}")
                .ToArray();
            var triageRows = coordinateRows
                .Where(candidate => candidate.GetProperty(
                    "source").GetString() == "triage")
                .ToArray();
            Assert.Equal(2, triageRows.Length);
            var library = Assert.Single(
                coordinateRows,
                candidate => candidate.GetProperty(
                    "source").GetString() == "library");
            Assert.All(
                triageRows,
                candidate =>
                {
                    Assert.Equal(
                        256,
                        candidate.GetProperty(
                            "runtimeBytes").GetInt64());
                    Assert.Equal(
                        "confirmed-hot",
                        candidate.GetProperty(
                            "status").GetString());
                });
            Assert.Equal(
                0,
                library.GetProperty(
                    "runtimeBytes").GetInt64());
            Assert.Equal(
                "superseded-by-triage",
                library.GetProperty(
                    "status").GetString());
            Assert.Equal(
                512,
                coordinateRows.Sum(candidate =>
                    candidate.GetProperty(
                        "runtimeBytes").GetInt64()));
            Assert.Equal(
                1,
                coordinateRows.Sum(candidate =>
                    candidate.GetProperty(
                        "runtimeWeight").GetDouble()));
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
        }
    }

    [Fact]
    public void Correlate_TextSupersessionIsOrderIndependentAndShapeCompatible()
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var evidenceMethod =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "AllocateOne",
                BindingFlags.Public | BindingFlags.Static);
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
            WriteTriage("System.Object");
            string methodLine =
                "RunFaster.AllocationFixture.Program.AllocateOne() "
                + "100 bytes";
            string coordinateLine =
                $"0x{evidenceMethod.MetadataToken:X8}"
                + $"+{occurrence.ILOffset:X4} 200 bytes";
            var forward = Correlate(
                $"{methodLine}{Environment.NewLine}"
                + coordinateLine);
            var reverse = Correlate(
                $"{coordinateLine}{Environment.NewLine}"
                + methodLine);

            Assert.Equal(forward, reverse);
            Assert.Equal(300, forward.TotalBytes);
            Assert.Equal(300, forward.TriageBytes);
            Assert.Equal(0, forward.LibraryBytes);
            Assert.Equal(2, forward.TotalWeight);
            Assert.Equal(
                "superseded-by-triage",
                forward.LibraryStatus);

            WriteTriage("Incompatible.Type");
            var incompatible = Correlate(
                $"0x{evidenceMethod.MetadataToken:X8}"
                + $"+{occurrence.ILOffset:X4} 512 bytes");

            Assert.Equal(512, incompatible.TotalBytes);
            Assert.Equal(256, incompatible.TriageBytes);
            Assert.Equal(256, incompatible.LibraryBytes);
            Assert.Equal(
                "confirmed-hot",
                incompatible.TriageStatus);
            Assert.Equal(
                "confirmed-hot",
                incompatible.LibraryStatus);

            var incompatibleMethod = Correlate(
                "RunFaster.AllocationFixture.Program.AllocateOne() "
                + "101 bytes");

            Assert.Equal(101, incompatibleMethod.TotalBytes);
            Assert.Equal(1, incompatibleMethod.TotalWeight);
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
        }

        void WriteTriage(string allocatedType)
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"{{{occurrence.Method.AssemblyName}}}","module_version_id":"{{{occurrence.Method.ModuleVersionId:D}}}","method_token":"0x{{{evidenceMethod.MetadataToken:X8}}}","evidence_method":"0x{{{evidenceMethod.MetadataToken:X8}}}","shape":"object-allocation","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"{{{allocatedType}}}"}]}}
                """);
        }

        (
            long TotalBytes,
            long TriageBytes,
            long LibraryBytes,
            double TotalWeight,
            string TriageStatus,
            string LibraryStatus)
            Correlate(string log)
        {
            File.WriteAllText(logPath, log);
            var result = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output =
                JsonDocument.Parse(result.Output);
            var coordinateRows = output.RootElement
                .GetProperty("candidates")
                .EnumerateArray()
                .Where(candidate =>
                    candidate.GetProperty(
                        "tokenIl").GetString()
                    == $"0x{evidenceMethod.MetadataToken:X8}"
                        + $"+IL_{occurrence.ILOffset:X4}")
                .ToArray();
            var triage = Assert.Single(
                coordinateRows,
                candidate => candidate.GetProperty(
                    "source").GetString() == "triage");
            var library = Assert.Single(
                coordinateRows,
                candidate => candidate.GetProperty(
                    "source").GetString() == "library");
            long triageBytes = triage.GetProperty(
                "runtimeBytes").GetInt64();
            long libraryBytes = library.GetProperty(
                "runtimeBytes").GetInt64();
            return (
                triageBytes + libraryBytes,
                triageBytes,
                libraryBytes,
                triage.GetProperty(
                    "runtimeWeight").GetDouble()
                    + library.GetProperty(
                        "runtimeWeight").GetDouble(),
                triage.GetProperty(
                    "status").GetString()!,
                library.GetProperty(
                    "status").GetString()!);
        }
    }

    [Fact]
    public void Correlate_RedirectedEvidenceFragmentsPreserveBodySemantics()
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var evidenceMethod =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "AllocateOne",
                BindingFlags.Public | BindingFlags.Static);
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
        string speedscopePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-{Guid.NewGuid():N}.speedscope.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.Main()","assembly":"{{{occurrence.Method.AssemblyName}}}","module_version_id":"{{{occurrence.Method.ModuleVersionId:D}}}","evidence_method":"0x{{{evidenceMethod.MetadataToken:X8}}}","shape":"object-allocation","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.Object"}]}}
                """);
            File.WriteAllText(
                logPath,
                "RunFaster.AllocationFixture.Program.AllocateOne() "
                + "IL_FFFE 512 bytes");

            var wrongOffset = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, wrongOffset.ExitCode);
            Assert.Empty(wrongOffset.Error);
            using (var output =
                JsonDocument.Parse(wrongOffset.Output))
            {
                Assert.Equal(
                    0,
                    output.RootElement.GetProperty(
                        "observedCandidates").GetInt32());
                var rows = output.RootElement.GetProperty(
                    "candidates").EnumerateArray().ToArray();
                var triage = Assert.Single(
                    rows,
                    candidate => candidate.GetProperty(
                        "source").GetString() == "triage");
                var library = Assert.Single(
                    rows,
                    candidate => candidate.GetProperty(
                        "source").GetString() == "library"
                        && candidate.GetProperty(
                            "method").GetString()!
                            .EndsWith(
                                ".AllocateOne()",
                                StringComparison.Ordinal));
                Assert.Equal(
                    0,
                    triage.GetProperty(
                        "runtimeBytes").GetInt64());
                Assert.Equal(
                    "cold-for-this-workload",
                    library.GetProperty(
                        "status").GetString());
            }

            File.WriteAllText(
                speedscopePath,
                """
                {"shared":{"frames":[{"name":"RunFaster.AllocationFixture.Program.Main()"},{"name":"RunFaster.AllocationFixture.Program.AllocateOne()"}]},"profiles":[{"type":"sampled","samples":[[0,1]],"weights":[100]}]}
                """);
            var sampled = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--input",
                speedscopePath,
                "--json");

            Assert.Equal(0, sampled.ExitCode);
            Assert.Empty(sampled.Error);
            using var sampledOutput =
                JsonDocument.Parse(sampled.Output);
            var sampledRows = sampledOutput.RootElement
                .GetProperty("candidates")
                .EnumerateArray()
                .ToArray();
            var sampledTriage = Assert.Single(
                sampledRows,
                candidate => candidate.GetProperty(
                    "source").GetString() == "triage");
            var sampledLibrary = Assert.Single(
                sampledRows,
                candidate => candidate.GetProperty(
                    "source").GetString() == "library"
                    && candidate.GetProperty(
                        "method").GetString()!
                        .EndsWith(
                            ".AllocateOne()",
                            StringComparison.Ordinal));
            Assert.Equal(
                1,
                sampledTriage.GetProperty(
                    "runtimeHits").GetInt32());
            Assert.Equal(
                100,
                sampledTriage.GetProperty(
                    "runtimeWeight").GetDouble());
            Assert.Equal(
                0,
                sampledLibrary.GetProperty(
                    "runtimeWeight").GetDouble());
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
            File.Delete(speedscopePath);
        }
    }

    [Fact]
    public void Correlate_ProjectedColdSiteRemainsWorkloadCold()
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var method =
            typeof(RunFaster.AllocationFixture.Program).GetMethod(
                "AllocateTwo",
                BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var occurrence = Assert.Single(
            LibraryBodyIndex.Open(assemblyPath)
                .GetAllocationOccurrences()[
                    method.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.AllocateTwo()","assembly":"{{{occurrence.Method.AssemblyName}}}","module_version_id":"{{{occurrence.Method.ModuleVersionId:D}}}","method_token":"0x{{{method.MetadataToken:X8}}}","shape":"object-allocation","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.String"}]}}
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
            using var output =
                JsonDocument.Parse(result.Output);
            var rows = output.RootElement
                .GetProperty("candidates")
                .EnumerateArray()
                .Where(candidate =>
                    candidate.GetProperty(
                        "method").GetString()!
                        .EndsWith(
                            ".AllocateTwo()",
                            StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(2, rows.Length);
            Assert.All(
                rows,
                candidate =>
                {
                    Assert.Equal(
                        "cold-for-this-workload",
                        candidate.GetProperty(
                            "status").GetString());
                    Assert.False(
                        candidate.TryGetProperty(
                            "multiplicityCheck",
                            out _));
                });
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_MethodTextNeverClaimsExactCoordinate()
    {
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
                """
                {"performance":{"objects":[{"member":"Fixture.Type.M()","assembly":"Fixture","method_token":"0x06000001","evidence_method":"0x06000001","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"}]}}
                """);
            File.WriteAllText(
                logPath,
                "Fixture.Type.M() IL_0005 101 bytes");

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output =
                JsonDocument.Parse(result.Output);
            var candidate = Assert.Single(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.Equal(
                "method-hot",
                candidate.GetProperty(
                    "status").GetString());
            Assert.False(
                candidate.GetProperty(
                    "exactOffsetObserved").GetBoolean());
            Assert.Equal(
                101,
                candidate.GetProperty(
                    "runtimeBytes").GetInt64());
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
        }
    }

    [Fact]
    public void Correlate_ChromiumTraceSupportsSparseFrameIds()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        string tracePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-{Guid.NewGuid():N}.chromium.json");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {"performance":{"objects":[{"member":"Ns.Alpha.Hot(System.String)","assembly":"Fixture","method_token":"0x06000001","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"}]}}
                """);
            File.WriteAllText(
                tracePath,
                """
                {"stackFrames":{"1":{"name":"Ns.Alpha.Hot(System.String)"},"3":{"name":"Other.Noise()"}},"traceEvents":[{"ph":"B","sf":1,"ts":0},{"ph":"E","sf":1,"ts":50}]}
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                tracePath,
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output =
                JsonDocument.Parse(result.Output);
            Assert.Equal(
                1,
                output.RootElement.GetProperty(
                    "observedCandidates").GetInt32());
            var candidate = Assert.Single(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.Equal(
                50,
                candidate.GetProperty(
                    "runtimeWeight").GetDouble());
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(tracePath);
        }
    }

    [Fact]
    public void Correlate_ProfileWeightIsFullForDistinctFrames()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        string speedscopePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-{Guid.NewGuid():N}.speedscope.json");
        try
        {
            File.WriteAllText(
                triagePath,
                """
                {"performance":{"objects":[{"member":"Fixture.Type.First()","assembly":"Fixture","method_token":"0x06000001","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"},{"member":"Fixture.Type.Second()","assembly":"Fixture","method_token":"0x06000002","shape":"object-allocation","il":"IL_0005","allocation":"System.String"}]}}
                """);
            File.WriteAllText(
                speedscopePath,
                """
                {"shared":{"frames":[{"name":"Fixture.Type.First()"},{"name":"Fixture.Type.Second()"},{"name":"Fixture.Type.First()"}]},"profiles":[{"type":"sampled","samples":[[0,1,2]],"weights":[100]}]}
                """);

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--input",
                speedscopePath,
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output =
                JsonDocument.Parse(result.Output);
            var candidates = output.RootElement
                .GetProperty("candidates")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(2, candidates.Length);
            var weights = candidates.ToDictionary(
                candidate =>
                    candidate.GetProperty(
                        "method").GetString()!,
                candidate =>
                    candidate.GetProperty(
                        "runtimeWeight").GetDouble());
            Assert.Equal(
                200,
                weights["Fixture.Type.First()"]);
            Assert.Equal(
                100,
                weights["Fixture.Type.Second()"]);
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(speedscopePath);
        }
    }

    [Fact]
    public void Correlate_ProfileLogicalDuplicates_PreserveMethodWeight()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        string speedscopePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-{Guid.NewGuid():N}.speedscope.json");
        try
        {
            const string row =
                """{"member":"Fixture.Type.First()","assembly":"Fixture","method_token":"0x06000001","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"}""";
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"objects":[{{{row}}},{{{row}}}]}}
                """);
            File.WriteAllText(
                speedscopePath,
                """
                {"shared":{"frames":[{"name":"Fixture.Type.First()"}]},"profiles":[{"type":"sampled","samples":[[0]],"weights":[100]}]}
                """);

            var json = RunCorrelate(
                "--triage",
                triagePath,
                "--input",
                speedscopePath,
                "--json");

            Assert.Equal(0, json.ExitCode);
            Assert.Empty(json.Error);
            using var output =
                JsonDocument.Parse(json.Output);
            Assert.Equal(
                100,
                output.RootElement
                    .GetProperty("candidates")
                    .EnumerateArray()
                    .Sum(candidate =>
                        candidate.GetProperty(
                            "runtimeWeight").GetDouble()));

            var markdown = RunCorrelate(
                "--triage",
                triagePath,
                "--input",
                speedscopePath);
            Assert.Equal(0, markdown.ExitCode);
            Assert.Empty(markdown.Error);
            Assert.Contains(
                "sample weight 100",
                markdown.Output);
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(speedscopePath);
        }
    }

    [Theory]
    [InlineData("-1 bytes")]
    [InlineData(
        "999999999999999999999999999999999999999999999 bytes")]
    public void Correlate_RejectsInvalidTextByteValues(
        string byteText)
    {
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
                """
                {"performance":{"objects":[{"member":"Fixture.Type.M()","assembly":"Fixture","method_token":"0x06000001","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"}]}}
                """);
            File.WriteAllText(
                logPath,
                $"Fixture.Type.M() {byteText}");

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "byte value",
                result.Error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
        }
    }

    [Fact]
    public void Correlate_RejectsCumulativeTextByteOverflow()
    {
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
                """
                {"performance":{"objects":[{"member":"Fixture.Type.M()","assembly":"Fixture","method_token":"0x06000001","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"}]}}
                """);
            File.WriteAllText(
                logPath,
                "Fixture.Type.M() 9223372036854775807 bytes"
                + Environment.NewLine
                + "Fixture.Type.M() 1 bytes");

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "overflow",
                result.Error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
        }
    }

    [Fact]
    public void Correlate_MissingSourceTokenDoesNotRelaxOffsetMatch()
    {
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
                """
                {"performance":{"objects":[{"member":"Fixture.Type.M()","assembly":"Fixture","evidence_method":"0x06000010","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"}]}}
                """);
            File.WriteAllText(
                logPath,
                "Fixture.Type.M() IL_0009 4096 bytes");

            var mismatch = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, mismatch.ExitCode);
            Assert.Empty(mismatch.Error);
            using (var output =
                JsonDocument.Parse(mismatch.Output))
            {
                Assert.Equal(
                    0,
                    output.RootElement.GetProperty(
                        "observedCandidates").GetInt32());
            }

            File.WriteAllText(
                logPath,
                "Fixture.Type.M() IL_0005 4096 bytes");
            var matchingOffset = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, matchingOffset.ExitCode);
            Assert.Empty(matchingOffset.Error);
            using var matchingOutput =
                JsonDocument.Parse(matchingOffset.Output);
            var candidate = Assert.Single(
                matchingOutput.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.Equal(
                "method-hot",
                candidate.GetProperty(
                    "status").GetString());
            Assert.False(
                candidate.GetProperty(
                    "exactOffsetObserved").GetBoolean());
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
        }
    }

    [Fact]
    public void Correlate_RejectedBuildCoordinateDoesNotUseMethodFallback()
    {
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
                """
                {"performance":{"objects":[{"member":"Fixture.Type.M()","assembly":"Fixture","module_version_id":"11111111-1111-1111-1111-111111111111","method_token":"0x06000001","evidence_method":"0x06000003","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"},{"member":"Fixture.Type.M()","assembly":"Fixture","module_version_id":"22222222-2222-2222-2222-222222222222","method_token":"0x06000002","evidence_method":"0x06000003","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"}]}}
                """);
            File.WriteAllText(
                logPath,
                "0x06000003+0005 Fixture.Type.M() 512 bytes");

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
            Assert.All(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray(),
                candidate =>
                {
                    Assert.Equal(
                        0,
                        candidate.GetProperty(
                            "runtimeBytes").GetInt64());
                    Assert.Equal(
                        "cold-for-this-workload",
                        candidate.GetProperty(
                            "status").GetString());
                });
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
        }
    }

    [Fact]
    public void Correlate_UnknownBuildTriageInputsDoNotCollapse()
    {
        string firstTriagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        string secondTriagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-log-{Guid.NewGuid():N}.txt");
        try
        {
            const string triage =
                """
                {"performance":{"objects":[{"candidate":"same","member":"Fixture.Type.M()","assembly":"Fixture","method_token":"0x06000001","shape":"object-allocation","operation":"newobj","token":"0x0A000001","il":"IL_0005","allocation":"System.Object","provenance":"exact"}]}}
                """;
            File.WriteAllText(
                firstTriagePath,
                triage);
            File.WriteAllText(
                secondTriagePath,
                triage);
            File.WriteAllText(
                logPath,
                "0x06000001+0005 Fixture.Type.M() 512 bytes");

            var result = RunCorrelate(
                "--triage",
                firstTriagePath,
                secondTriagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output =
                JsonDocument.Parse(result.Output);
            Assert.Equal(
                0,
                output.RootElement.GetProperty(
                    "observedCandidates").GetInt32());
            Assert.Equal(
                2,
                output.RootElement.GetProperty(
                    "candidates").GetArrayLength());
            Assert.All(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray(),
                candidate =>
                {
                    Assert.Equal(
                        "cold-for-this-workload",
                        candidate.GetProperty(
                            "status").GetString());
                });
        }
        finally
        {
            File.Delete(firstTriagePath);
            File.Delete(secondTriagePath);
            File.Delete(logPath);
        }
    }

    [Fact]
    public void Correlate_RejectedCoordinateDoesNotFallBackToAnotherSite()
    {
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
                """
                {"performance":{"objects":[{"member":"Fixture.Type.M()","assembly":"Fixture","module_version_id":"11111111-1111-1111-1111-111111111111","method_token":"0x06000001","evidence_method":"0x06000003","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"},{"member":"Fixture.Type.M()","assembly":"Fixture","module_version_id":"22222222-2222-2222-2222-222222222222","method_token":"0x06000002","evidence_method":"0x06000003","shape":"object-allocation","il":"IL_0005","allocation":"System.Object"},{"member":"Fixture.Type.M()","assembly":"Fixture","method_token":"0x06000004","evidence_method":"0x06000004","shape":"object-allocation","il":"IL_0009","allocation":"System.String"}]}}
                """);
            File.WriteAllText(
                logPath,
                "0x06000003+0005 Fixture.Type.M() 512 bytes");

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output =
                JsonDocument.Parse(result.Output);
            Assert.Equal(
                0,
                output.RootElement.GetProperty(
                    "observedCandidates").GetInt32());
            Assert.All(
                output.RootElement.GetProperty(
                    "candidates").EnumerateArray(),
                candidate =>
                    Assert.Equal(
                        0,
                        candidate.GetProperty(
                            "runtimeBytes").GetInt64()));
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
    public void FlattenedPerformanceTriageJsonl_RetainsSupportingCallSite()
    {
        string inspectDll =
            Path.Combine(
                AppContext.BaseDirectory,
                "dotnet-inspect.dll");
        var produced = RunTool(
            inspectDll,
            "type",
            Path.Combine(
                AppContext.BaseDirectory,
                "runfaster.Tests.dll"),
            "runfaster.Tests.E2EFixtureTests.FlattenedScanFixture",
            "-S",
            "Performance Triage",
            "--jsonl");

        Assert.Equal(0, produced.ExitCode);
        Assert.Empty(produced.Error);
        var rows = produced.Output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries)
            .Select(static text =>
                JsonDocument.Parse(text))
            .ToArray();
        try
        {
            var row = Assert.Single(
                rows,
                document => document.RootElement
                    .GetProperty("shape")
                    .GetString()
                    == "scan-method-in-loop-call");
            Assert.Equal(
                "analysis.call-site",
                row.RootElement.GetProperty(
                        "supporting_finding")
                    .GetString());
            Assert.Equal(
                "newobj",
                row.RootElement.GetProperty(
                        "supporting_operation")
                    .GetString());
            Assert.StartsWith(
                "0x",
                row.RootElement.GetProperty(
                        "supporting_token")
                    .GetString(),
                StringComparison.Ordinal);
            Assert.StartsWith(
                "0x06",
                row.RootElement.GetProperty(
                        "supporting_evidence_method")
                    .GetString(),
                StringComparison.Ordinal);
            Assert.StartsWith(
                "IL_",
                row.RootElement.GetProperty(
                        "supporting_il")
                    .GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            foreach (var row in rows)
                row.Dispose();
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
        string member = Assert.IsType<string>(
            producedRow.RootElement.GetProperty(
                "member").GetString());
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

            File.WriteAllText(
                logPath,
                $"{member} {ilOffset} 512 bytes");
            var methodResult = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--json");

            Assert.Equal(0, methodResult.ExitCode);
            Assert.Empty(methodResult.Error);
            using var methodOutput =
                JsonDocument.Parse(methodResult.Output);
            var methodCandidate = Assert.Single(
                methodOutput.RootElement.GetProperty(
                    "candidates").EnumerateArray());
            Assert.False(
                methodCandidate.GetProperty(
                    "runtimeCorrelatable").GetBoolean());
            Assert.False(
                methodCandidate.GetProperty(
                    "exactOffsetObserved").GetBoolean());
            Assert.Equal(
                "method-hot",
                methodCandidate.GetProperty(
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
    public void Correlate_AggregateSupportingCallSite_PromotesExactLibraryEvidence()
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var allocateOne =
            typeof(RunFaster.AllocationFixture.Program)
                .GetMethod(
                    "AllocateOne",
                    BindingFlags.Public
                        | BindingFlags.Static);
        Assert.NotNull(allocateOne);
        var occurrence = Assert.Single(
            LibraryBodyIndex.Open(assemblyPath)
                .GetAllocationOccurrences()[
                    allocateOne.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"loop_hot_paths":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","moduleVersionId":"{{{occurrence.Method.ModuleVersionId:D}}}","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","candidate":"pt~aggregate","shape":"scan-method-in-loop-call","provenance":"aggregate","supporting_finding":"analysis.call-site","supporting_operation":"newobj","supporting_token":"0x0A000001","supporting_evidence_method":"0x{{{allocateOne.MetadataToken:X8}}}","supporting_il":"IL_{{{occurrence.ILOffset:X4}}}"}]}}
                """);

            var result = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation
                    .AssetPath("fixture.nettrace"),
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output =
                JsonDocument.Parse(result.Output);
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
                "supporting-call-site",
                triage.GetProperty("coordinateRole")
                    .GetString());
            Assert.Equal(
                occurrence.ILOffset,
                triage.GetProperty(
                    "supportingIlOffset")
                    .GetInt32());
            Assert.Equal(
                allocateOne.MetadataToken,
                triage.GetProperty(
                    "supportingEvidenceMethodToken")
                    .GetInt32());
            Assert.False(
                triage.TryGetProperty(
                    "ilOffset",
                    out _));
            Assert.False(
                triage.TryGetProperty(
                    "operation",
                    out _));
            Assert.Equal(
                1_167_872,
                triage.GetProperty("allocationBytes")
                    .GetInt64());
            Assert.Equal(
                "il-offset-hot",
                triage.GetProperty("status")
                    .GetString());
            Assert.Equal(
                0,
                library.GetProperty("allocationBytes")
                    .GetInt64());
            Assert.Equal(
                "superseded-by-triage",
                library.GetProperty("status")
                    .GetString());
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_NonAllocationSupport_DoesNotShadowNearestLibrarySite()
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var allocateOne =
            typeof(RunFaster.AllocationFixture.Program)
                .GetMethod(
                    "AllocateOne",
                    BindingFlags.Public
                        | BindingFlags.Static);
        Assert.NotNull(allocateOne);
        var occurrence = Assert.Single(
            LibraryBodyIndex.Open(assemblyPath)
                .GetAllocationOccurrences()[
                    allocateOne.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"loop_hot_paths":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","moduleVersionId":"{{{occurrence.Method.ModuleVersionId:D}}}","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","candidate":"pt~aggregate","shape":"scan-method-in-loop-call","provenance":"aggregate","supporting_finding":"analysis.call-site","supporting_operation":"call","supporting_token":"0x0A000001","supporting_evidence_method":"0x{{{allocateOne.MetadataToken:X8}}}","supporting_il":"IL_{{{occurrence.ILOffset + 1:X4}}}"}]}}
                """);

            var result = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation
                    .AssetPath("fixture.nettrace"),
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output =
                JsonDocument.Parse(result.Output);
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
                0,
                triage.GetProperty("allocationBytes")
                    .GetInt64());
            Assert.Equal(
                "cold-for-this-workload",
                triage.GetProperty("status")
                    .GetString());
            Assert.Equal(
                1_167_872,
                library.GetProperty("allocationBytes")
                    .GetInt64());
            Assert.Equal(
                "shape-hot",
                library.GetProperty("status")
                    .GetString());
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public void Correlate_ExactAndAggregateSites_RespectCoordinateAndBuild(
        int exactOffsetDelta,
        bool sameBuild)
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var allocateOne =
            typeof(RunFaster.AllocationFixture.Program)
                .GetMethod(
                    "AllocateOne",
                    BindingFlags.Public
                        | BindingFlags.Static);
        Assert.NotNull(allocateOne);
        var occurrence = Assert.Single(
            LibraryBodyIndex.Open(assemblyPath)
                .GetAllocationOccurrences()[
                    allocateOne.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            Guid exactMvid = sameBuild
                ? occurrence.Method.ModuleVersionId
                : Guid.Parse(
                    "99999999-9999-9999-9999-999999999999");
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"objects":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","moduleVersionId":"{{{exactMvid:D}}}","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","candidate":"pt~exact","shape":"object-allocation","provenance":"exact","operation":"newobj","token":"0x0A000001","il":"IL_{{{occurrence.ILOffset + exactOffsetDelta:X4}}}","allocation":"System.Object"}],"loop_hot_paths":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","moduleVersionId":"{{{occurrence.Method.ModuleVersionId:D}}}","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","candidate":"pt~aggregate","shape":"scan-method-in-loop-call","provenance":"aggregate","supporting_finding":"analysis.call-site","supporting_operation":"newobj","supporting_token":"0x0A000001","supporting_evidence_method":"0x{{{allocateOne.MetadataToken:X8}}}","supporting_il":"IL_{{{occurrence.ILOffset:X4}}}"}]}}
                """);

            var result = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation
                    .AssetPath("fixture.nettrace"),
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output =
                JsonDocument.Parse(result.Output);
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
            var aggregate = Assert.Single(
                sameMethod,
                candidate => candidate.TryGetProperty(
                        "candidate",
                        out var id)
                    && id.GetString()
                        == "pt~aggregate");
            var exact = Assert.Single(
                sameMethod,
                candidate => candidate.TryGetProperty(
                        "candidate",
                        out var id)
                    && id.GetString() == "pt~exact");
            if (sameBuild
                && exactOffsetDelta == 0)
            {
                Assert.Equal(
                    1_167_872,
                    aggregate.GetProperty(
                            "allocationBytes")
                        .GetInt64());
                Assert.Equal(
                    0,
                    exact.GetProperty("allocationBytes")
                        .GetInt64());
                Assert.Equal(
                    "superseded-by-triage",
                    exact.GetProperty("status")
                        .GetString());
            }
            else if (sameBuild)
            {
                Assert.Equal(
                    0,
                    aggregate.GetProperty(
                            "allocationBytes")
                        .GetInt64());
                Assert.Equal(
                    1_167_872,
                    exact.GetProperty("allocationBytes")
                        .GetInt64());
                Assert.NotEqual(
                    "superseded-by-triage",
                    exact.GetProperty("status")
                        .GetString());
            }
            else
            {
                Assert.True(
                    aggregate.GetProperty(
                            "allocationBytes")
                        .GetInt64() > 0);
                Assert.True(
                    exact.GetProperty("allocationBytes")
                        .GetInt64() > 0);
                Assert.NotEqual(
                    "superseded-by-triage",
                    exact.GetProperty("status")
                        .GetString());
            }
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_AmbiguousAggregateSupports_DoNotClaimLibraryEvidence()
    {
        string assemblyPath =
            FixtureCatalog.RunFasterAllocation.AssemblyPath();
        var allocateOne =
            typeof(RunFaster.AllocationFixture.Program)
                .GetMethod(
                    "AllocateOne",
                    BindingFlags.Public
                        | BindingFlags.Static);
        Assert.NotNull(allocateOne);
        var occurrence = Assert.Single(
            LibraryBodyIndex.Open(assemblyPath)
                .GetAllocationOccurrences()[
                    allocateOne.MetadataToken]);
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        string stackPath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-stack-{Guid.NewGuid():N}.txt");
        try
        {
            string rowProperties =
                $$$"""
                "member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","moduleVersionId":"{{{occurrence.Method.ModuleVersionId:D}}}","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","shape":"scan-method-in-loop-call","provenance":"aggregate","supporting_finding":"analysis.call-site","supporting_operation":"newobj","supporting_token":"0x0A000001","supporting_evidence_method":"0x{{{allocateOne.MetadataToken:X8}}}","supporting_il":"IL_{{{occurrence.ILOffset:X4}}}"
                """;
            File.WriteAllText(
                triagePath,
                string.Concat(
                    "{\"performance\":{\"loop_hot_paths\":[{",
                    rowProperties,
                    ",\"candidate\":\"pt~first\"},{",
                    rowProperties,
                    ",\"candidate\":\"pt~second\"}]}}"));
            File.WriteAllText(
                stackPath,
                "RunFaster.AllocationFixture.Program.AllocateOne()");

            var result = RunCorrelate(
                "--library",
                assemblyPath,
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation
                    .AssetPath("fixture.nettrace"),
                "--stack",
                stackPath,
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var output =
                JsonDocument.Parse(result.Output);
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
            Assert.All(
                sameMethod.Where(candidate =>
                    candidate.GetProperty("source")
                        .GetString() == "triage"),
                candidate => Assert.Equal(
                    0,
                    candidate.GetProperty(
                        "allocationBytes")
                        .GetInt64()));
            var library = Assert.Single(
                sameMethod,
                candidate => candidate
                    .GetProperty("source")
                    .GetString() == "library");
            Assert.Equal(
                1_167_872,
                library.GetProperty("allocationBytes")
                    .GetInt64());
            Assert.NotEqual(
                "superseded-by-triage",
                library.GetProperty("status")
                    .GetString());
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(stackPath);
        }
    }

    [Theory]
    [InlineData(
        "\"supporting_evidence_method\":\"0x06000001\"",
        "must be supplied together")]
    [InlineData(
        "\"supporting_evidence_method\":\"not-a-token\",\"supporting_il\":\"IL_0000\"",
        "Invalid supporting evidence method")]
    [InlineData(
        "\"supporting_evidence_method\":\"0x06000001\",\"supporting_il\":\"IL_0000\",\"il\":\"IL_0000\"",
        "cannot be combined with IL or Evidence Method")]
    [InlineData(
        "\"supporting_evidence_method\":\"0x06000001\",\"supportingEvidenceMethod\":\"0x06000002\",\"supporting_il\":\"IL_0000\"",
        "Conflicting supporting evidence method values")]
    [InlineData(
        "\"supporting_evidence_method\":\"0x06000001\",\"supporting_il\":\"IL_0000\",\"supportingIL\":\"IL_0001\"",
        "Conflicting supporting IL values")]
    public void Correlate_InvalidSupportingCoordinate_FailsVisibly(
        string coordinateProperties,
        string expectedError)
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                triagePath,
                string.Concat(
                    "{\"performance\":{\"loop_hot_paths\":[{",
                    "\"member\":\"RunFaster.AllocationFixture.Program.AllocateOne()\",",
                    "\"assembly\":\"RunFaster.AllocationFixture\",",
                    "\"method_token\":\"0x06000002\",",
                    "\"shape\":\"scan-method-in-loop-call\",",
                    coordinateProperties,
                    "}]}}"));

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation
                    .AssetPath("fixture.nettrace"));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                expectedError,
                result.Error,
                StringComparison.Ordinal);
            Assert.Empty(result.Output);
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
                {"performance":{"arrays":[{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","module_version_id":"99999999-9999-9999-9999-999999999999","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","shape":"fixture-object","operation":"newobj","token":"0x0A000001","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.Object","provenance":"exact"},{"member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","module_version_id":"88888888-8888-8888-8888-888888888888","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","shape":"fixture-object","operation":"newobj","token":"0x0A000002","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.Object","provenance":"exact"}]}}
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
            Assert.Equal(3, sameMethod.Length);
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
            var attributedBytes = sameMethod
                .Select(candidate =>
                    candidate.GetProperty(
                        "allocationBytes").GetInt64())
                .ToArray();
            Assert.True(
                attributedBytes.Max()
                    - attributedBytes.Min() <= 1,
                $"Expected fair cumulative attribution: {string.Join(", ", attributedBytes)}");
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_NetTraceLogicalDuplicates_DoNotInflateTicksOrAmbiguity()
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
            string row = $$$"""
                {"candidate_id":"duplicate","member":"RunFaster.AllocationFixture.Program.AllocateOne()","assembly":"RunFaster.AllocationFixture","method_token":"0x{{{allocateOne.MetadataToken:X8}}}","shape":"fixture-object","operation":"newobj","token":"0x0A000001","il":"IL_{{{occurrence.ILOffset:X4}}}","allocation":"System.Object","provenance":"exact"}
                """;
            File.WriteAllText(
                triagePath,
                $$$"""
                {"performance":{"arrays":[{{{row}}},{{{row}}}]}}
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
            using var output =
                JsonDocument.Parse(result.Output);
            var candidates = output.RootElement
                .GetProperty("candidates")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(2, candidates.Length);
            Assert.Equal(
                11,
                candidates.Sum(
                    candidate =>
                        candidate.GetProperty(
                            "allocationHits").GetInt64()));
            Assert.Equal(
                1_167_872,
                candidates.Sum(
                    candidate =>
                        candidate.GetProperty(
                            "allocationBytes").GetInt64()));
            Assert.All(
                candidates,
                candidate =>
                {
                    Assert.False(
                        candidate.GetProperty(
                            "ambiguousIlOffsetJoin")
                            .GetBoolean());
                    Assert.False(
                        candidate.GetProperty(
                            "rowAmbiguous")
                            .GetBoolean());
                    Assert.Equal(
                        1,
                        candidate.GetProperty(
                            "sameMethodShapeRows")
                            .GetInt32());
                    Assert.Equal(
                        "shape-hot",
                        candidate.GetProperty(
                            "status").GetString());
                });

            var markdown = RunCorrelate(
                "--triage",
                triagePath,
                "--trace",
                FixtureCatalog.RunFasterAllocation.AssetPath(
                    "fixture.nettrace"));
            Assert.Equal(0, markdown.ExitCode);
            Assert.Empty(markdown.Error);
            Assert.Contains(
                "11 alloc ticks / 1.11 MB",
                markdown.Output);
        }
        finally
        {
            File.Delete(triagePath);
        }
    }

    [Fact]
    public void Correlate_MarkdownSummariesIncludeObservedRowsBeyondTop()
    {
        string triagePath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-triage-{Guid.NewGuid():N}.json");
        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"runfaster-log-{Guid.NewGuid():N}.txt");
        try
        {
            const string row = """
                {"candidate_id":"duplicate","member":"Fixture.Type.M()","assembly":"Fixture","method_token":"0x06000001","shape":"object-allocation","operation":"newobj","token":"0x0A000001","il":"IL_0005","allocation":"System.Object","provenance":"exact"}
                """;
            File.WriteAllText(
                triagePath,
                """{"performance":{"objects":["""
                + string.Join(
                    ",",
                    Enumerable.Repeat(row, 26))
                + "]}}");
            File.WriteAllText(
                logPath,
                "0x06000001+0005 Fixture.Type.M() 26 bytes");

            var result = RunCorrelate(
                "--triage",
                triagePath,
                "--log",
                logPath,
                "--top",
                "3");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            Assert.Contains(
                "Runtime-observed candidates: 26",
                result.Output);
            Assert.Contains(
                "| object-allocation | 26 B |",
                result.Output);
            Assert.Contains(
                "| sample weight 1 / 26 B | `Fixture.Type.M()` | 26 |",
                result.Output);
            Assert.Equal(
                3,
                result.Output.Split('\n').Count(
                    static line => line.StartsWith(
                        "| confirmed-hot |",
                        StringComparison.Ordinal)));
        }
        finally
        {
            File.Delete(triagePath);
            File.Delete(logPath);
        }
    }

    public static class FlattenedScanFixture
    {
        public static int FilterThenFirstOrDefault(
            IEnumerable<int> source,
            int key)
            => Enumerable.FirstOrDefault(
                Enumerable.Where(
                    source,
                    value => value == key));

        public static int CallFilterInLoop(
            IEnumerable<int> source,
            int[] keys)
        {
            int result = 0;
            foreach (int key in keys)
            {
                result += FilterThenFirstOrDefault(
                    source,
                    key);
            }
            return result;
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
