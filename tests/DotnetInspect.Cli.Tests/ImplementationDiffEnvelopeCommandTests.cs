using System.CommandLine;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ImplementationDiffEnvelopeCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PairedJson_PreservesCompleteDocumentAndEnvelopeContent(
        bool compact)
    {
        string[] formatting = compact ? ["--compact"] : [];
        var content = await Run([
            "--format=json",
            .. formatting,
            "-S",
            "Implementation Diff",
            "--type",
            "DiffSample",
        ]);
        var envelope = await Run([
            "--envelope",
            .. formatting,
            "-S",
            "Implementation Diff",
            "--type",
            "DiffSample",
        ]);

        Assert.True(content.Exit == 0, content.Error);
        Assert.True(envelope.Exit == 0, envelope.Error);
        Assert.Empty(content.Error);
        Assert.Empty(envelope.Error);
        using JsonDocument contentJson = JsonDocument.Parse(content.Output);
        using JsonDocument envelopeJson = JsonDocument.Parse(envelope.Output);
        JsonElement root = envelopeJson.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            "implementation-diff",
            root.GetProperty("result_kind").GetString());
        Assert.True(JsonElement.DeepEquals(
            contentJson.RootElement,
            root.GetProperty("content")));

        JsonElement document = root.GetProperty("content");
        AssertPopulationContext(contentJson.RootElement);
        AssertPopulationContext(document);
        Assert.Equal(
            "ExactLibraryPair",
            document.GetProperty("request").GetProperty("scope").GetString());
        Assert.Equal(
            "DiffSample",
            Assert.Single(
                document.GetProperty("request")
                    .GetProperty("typeFilters")
                    .EnumerateArray()).GetString());
        Assert.Equal(
            "DiffFixtureSample",
            document.GetProperty("before")
                .GetProperty("assemblyIdentity")
                .GetProperty("name")
                .GetString());
        Assert.NotEqual(
            Guid.Empty,
            document.GetProperty("before")
                .GetProperty("moduleVersionId")
                .GetGuid());
        Assert.Equal(
            "local",
            document.GetProperty("before")
                .GetProperty("provenance")
                .GetProperty("kind")
                .GetString());
        Assert.Contains(
            document.GetProperty("members").EnumerateArray(),
            member => member.GetProperty("subject")
                    .GetProperty("memberName")
                    .GetString()
                == "ConstantValue"
                && member.GetProperty("evidence")
                    .EnumerateArray()
                    .Any(evidence =>
                        evidence.TryGetProperty("cSharpRow", out _)));
        Assert.Contains(
            document.GetProperty("complexity")
                .GetProperty("changes")
                .EnumerateArray(),
            change => change.GetProperty("subject")
                    .GetProperty("memberName")
                    .GetString()
                == "RegressesAllocInLoop"
                && change.GetProperty("delta").GetInt32() == 1);
        Assert.Equal(
            "nonProjectable",
            root.GetProperty("share").GetProperty("kind").GetString());
        Assert.Equal(
            "comparison/endpoints",
            root.GetProperty("share").GetProperty("path").GetString());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
        Assert.DoesNotContain(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            envelope.Output,
            StringComparison.Ordinal);
        if (compact)
            Assert.DoesNotContain('\n', envelope.Output.TrimEnd('\r', '\n'));
    }

    [Fact]
    public async Task OrdinaryImplementationDiff_RemainsRenderedProjection()
    {
        var result = await Run(
            "-S",
            "Implementation Diff",
            "--type",
            "DiffSample");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.StartsWith(
            "# Implementation Diff:",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("| Member | Mechanism |", result.Output);
    }

    [Fact]
    public async Task WildcardSelection_DoesNotActivateCompleteTransport()
    {
        var result = await Run(
            "--format=json",
            "-S",
            "*Implementation*",
            "--type",
            "DiffSample",
            "--rows",
            "1..1");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.TryGetProperty("request", out _));
        Assert.True(
            json.RootElement.TryGetProperty("implementation_diff", out _));
    }

    [Fact]
    public async Task MemberSelector_IsRecordedAsSemanticRequestInput()
    {
        var result = await Run(
            "--format=json",
            "-S",
            "Implementation Diff",
            "--type",
            "DiffSample",
            "--member",
            "ConstantValue");

        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        JsonElement request = json.RootElement.GetProperty("request");
        Assert.NotEmpty(
            request.GetProperty("memberTargetIdentities").EnumerateArray());
        Assert.All(
            json.RootElement.GetProperty("members").EnumerateArray(),
            member => Assert.Equal(
                "ConstantValue",
                member.GetProperty("subject")
                    .GetProperty("memberName")
                    .GetString()));
    }

    [Theory]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .ConventionModifiers)]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .SignatureHeader)]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .UnsupportedModifier)]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .RequiredModifier)]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .DuplicateConventionModifier)]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .CoreLibraryLookalikeModifier)]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .CoreLibrarySuppressGcTransitionLookalikeModifier)]
    [InlineData(
        FunctionPointerConventionReturnOverloadFixture.IdentityCase
            .MixedSuppressGcTransitionModifier)]
    public async Task
        FunctionPointerReturnOverloads_SelectByExactBodyIdentity(
            FunctionPointerConventionReturnOverloadFixture.IdentityCase
                identityCase)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-function-pointer-convention-cli-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string oldPath = Path.Combine(directory, "before.dll");
            string newPath = Path.Combine(directory, "after.dll");
            File.WriteAllBytes(
                oldPath,
                FunctionPointerConventionReturnOverloadFixture.Build(
                    returnOne: false,
                    identityCase: identityCase));
            File.WriteAllBytes(
                newPath,
                FunctionPointerConventionReturnOverloadFixture.Build(
                    returnOne: true,
                    identityCase: identityCase));

            var unfiltered = await RunFunctionPointerConventionPair(
                oldPath,
                newPath,
                identityCase);
            var first = await RunFunctionPointerConventionPair(
                oldPath,
                newPath,
                identityCase,
                "Changed:1");
            var second = await RunFunctionPointerConventionPair(
                oldPath,
                newPath,
                identityCase,
                "Changed:2");

            Assert.True(unfiltered.Exit == 0, unfiltered.Error);
            Assert.True(first.Exit == 0, first.Error);
            Assert.True(second.Exit == 0, second.Error);
            using JsonDocument unfilteredJson =
                JsonDocument.Parse(unfiltered.Output);
            using JsonDocument firstJson = JsonDocument.Parse(first.Output);
            using JsonDocument secondJson = JsonDocument.Parse(second.Output);
            string[] allIds =
            [
                .. unfilteredJson.RootElement.GetProperty("members")
                    .EnumerateArray()
                    .Where(member => member.GetProperty("subject")
                        .GetProperty("memberName")
                        .GetString() == "Changed")
                    .Select(member => member.GetProperty("subject")
                        .GetProperty("id")
                        .GetString()!),
            ];
            string firstId = Assert.Single(
                firstJson.RootElement.GetProperty("members")
                    .EnumerateArray(),
                member => member.GetProperty("subject")
                    .GetProperty("memberName")
                    .GetString() == "Changed")
                .GetProperty("subject")
                .GetProperty("id")
                .GetString()!;
            string secondId = Assert.Single(
                secondJson.RootElement.GetProperty("members")
                    .EnumerateArray(),
                member => member.GetProperty("subject")
                    .GetProperty("memberName")
                    .GetString() == "Changed")
                .GetProperty("subject")
                .GetProperty("id")
                .GetString()!;

            Assert.Equal(2, allIds.Length);
            Assert.NotEqual(firstId, secondId);
            Assert.Equal(
                allIds.Order(StringComparer.Ordinal),
                new[] { firstId, secondId }.Order(StringComparer.Ordinal));
            string[] firstTargets =
            [
                .. firstJson.RootElement.GetProperty("request")
                    .GetProperty("memberTargetIdentities")
                    .EnumerateArray()
                    .Select(identity => identity.GetString()!),
            ];
            string[] secondTargets =
            [
                .. secondJson.RootElement.GetProperty("request")
                    .GetProperty("memberTargetIdentities")
                    .EnumerateArray()
                    .Select(identity => identity.GetString()!),
            ];
            Assert.Contains(firstId, firstTargets);
            Assert.DoesNotContain(secondId, firstTargets);
            Assert.Contains(secondId, secondTargets);
            Assert.DoesNotContain(firstId, secondTargets);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SameLibrary_PreservesExactEmptyCoverage()
    {
        string path = FixtureCatalog.DiffPair.OldAssemblyPath();
        var result = await Invoke(
            "--library",
            $"{path}..{path}",
            "--format=json",
            "-S",
            "Implementation Diff",
            "--type",
            "DiffSample");

        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        Assert.Empty(
            json.RootElement.GetProperty("members").EnumerateArray());
        JsonElement coverage = json.RootElement.GetProperty("coverage");
        Assert.True(coverage.GetProperty("isComplete").GetBoolean());
        Assert.All(
            coverage.GetProperty("mechanisms").EnumerateArray(),
            mechanism =>
            {
                Assert.Equal(
                    0,
                    mechanism.GetProperty("unavailableSubjectCount")
                        .GetInt32());
                Assert.Equal(
                    0,
                    mechanism.GetProperty("failedSubjectCount")
                        .GetInt32());
            });
        Assert.Contains(
            coverage.GetProperty("mechanisms").EnumerateArray(),
            mechanism => mechanism.GetProperty("exactSubjectCount")
                .GetInt32() > 0);
    }

    [Fact]
    public async Task OneSidedMethods_PreserveIlEvidenceAndCompleteCoverage()
    {
        var result = await Run(
            "--format=json",
            "-S",
            "Implementation Diff",
            "--type",
            "MethodRemovalSample");

        Assert.True(result.Exit == 0, result.Error);
        using JsonDocument json = JsonDocument.Parse(result.Output);
        JsonElement[] members = [
            .. json.RootElement.GetProperty("members")
                .EnumerateArray()
                .Where(member => member.GetProperty("subject")
                    .GetProperty("memberName")
                    .GetString() == "Removed"),
        ];
        Assert.Equal(2, members.Length);
        Assert.All(members, member =>
        {
            JsonElement comparison =
                member.GetProperty("ilFindingComparison");
            JsonElement topology =
                comparison.GetProperty("inspectionTransition");
            Assert.Equal(
                "Complete",
                topology.GetProperty("old").GetString());
            Assert.Equal(
                "SubjectAbsent",
                topology.GetProperty("new").GetString());
            JsonElement[] operations = [
                .. comparison.GetProperty("operations").EnumerateArray(),
            ];
            Assert.NotEmpty(operations);
            Assert.All(
                operations,
                operation =>
                {
                    Assert.Equal(
                        "Removed",
                        operation.GetProperty("kind").GetString());
                    Assert.Equal(
                        "None",
                        operation.GetProperty("difference").GetString());
                    Assert.False(string.IsNullOrWhiteSpace(
                        operation.GetProperty("old")
                            .GetProperty("operation")
                            .GetProperty("opcodeFamily")
                            .GetString()));
                    Assert.False(operation.TryGetProperty("new", out _));
                });
        });

        JsonElement complexity = Assert.Single(
            json.RootElement.GetProperty("coverage")
                .GetProperty("mechanisms")
                .EnumerateArray(),
            mechanism => mechanism.GetProperty("mechanism").GetString()
                == "Complexity");
        Assert.Equal(
            2,
            complexity.GetProperty("changedSubjectCount").GetInt32());
        Assert.Equal(
            0,
            complexity.GetProperty("incompleteSubjectCount").GetInt32());
        Assert.True(
            json.RootElement.GetProperty("coverage")
                .GetProperty("isComplete")
                .GetBoolean());
    }

    [Theory]
    [InlineData("--envelope", "--rows", "1..1")]
    [InlineData("--envelope", "--format=table", null)]
    [InlineData("--envelope", "--pdb-source", null)]
    [InlineData("--envelope", "--repo", "https://github.com/example/repo")]
    [InlineData("--envelope", "--changed", null)]
    [InlineData("--format=json", "--rows", "1..1")]
    [InlineData("--format=json", "--pdb-source", null)]
    [InlineData("--format=json", "--repo", "https://github.com/example/repo")]
    [InlineData("--format=json", "--changed", null)]
    public async Task CompleteTransport_RejectsPresentationOrOtherOperations(
        string output,
        string option,
        string? value)
    {
        var result = await Invoke([
            "--library",
            "missing-before.dll..missing-after.dll",
            output,
            "-S",
            "Implementation Diff",
            option,
            .. value is null ? Array.Empty<string>() : [value]]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.True(
            result.Error.Contains(
                "Complete Implementation Diff transport",
                StringComparison.Ordinal)
            || result.Error.Contains(
                "cannot be combined",
                StringComparison.Ordinal),
            result.Error);
        Assert.DoesNotContain(
            "Error resolving",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--package", "Missing.Package@1.0.0..2.0.0")]
    [InlineData("--platform", "System.Runtime@9.0.0..10.0.0")]
    public async Task CompleteTransport_RejectsNonLocalSourcesBeforeAcquisition(
        string source,
        string range)
    {
        var result = await Invoke(
            source,
            range,
            "--envelope",
            "-S",
            "Implementation Diff");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "local --library pair",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Error resolving",
            result.Error,
            StringComparison.Ordinal);
    }

    static Task<(int Exit, string Output, string Error)> Run(
        params string[] options)
        => Invoke([
            "--library",
            $"{FixtureCatalog.DiffPair.OldAssemblyPath()}.."
                + FixtureCatalog.DiffPair.NewAssemblyPath(),
            .. options]);

    static Task<(int Exit, string Output, string Error)>
        RunFunctionPointerConventionPair(
            string oldPath,
            string newPath,
            FunctionPointerConventionReturnOverloadFixture.IdentityCase
                identityCase,
            string? member = null)
        => Invoke([
            "--library",
            $"{oldPath}..{newPath}",
            "--format=json",
            "-S",
            "Implementation Diff",
            "--type",
            FunctionPointerConventionReturnOverloadFixture.GetTypeName(
                identityCase),
            .. member is null
                ? Array.Empty<string>()
                : ["--member", member],
        ]);

    static void AssertPopulationContext(JsonElement document)
    {
        JsonElement change = Assert.Single(
            document.GetProperty("complexity")
                .GetProperty("changes")
                .EnumerateArray(),
            change => change.GetProperty("subject")
                    .GetProperty("memberName")
                    .GetString()
                == "RegressesAllocInLoop"
                && change.GetProperty("delta").GetInt32() == 1);
        JsonElement populationContext =
            change.GetProperty("populationContext");

        Assert.True(
            populationContext.GetProperty("populationSize").GetInt32() > 0);
        Assert.InRange(
            populationContext.GetProperty("percentileRank").GetDouble(),
            0,
            100);
    }

    static Task<(int Exit, string Output, string Error)> Invoke(
        params string[] arguments)
        => ConsoleCapture.RunAsync(() =>
        {
            string[] normalized = CommandLineBuilder.PreprocessArgs(
                ["diff", .. arguments, "--tips", "q"]);
            return CommandLineBuilder.InvokeWithLineWindowAsync(
                CommandLineBuilder.CreateRootCommand().Parse(normalized),
                normalized);
        });
}
