using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using DotnetInspector.QueriesConsumer;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class ApiCoordinateMatchInspectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(".ctor")]
    public async Task Avalonia_MoveUsesTheExplicitMarkupToBaseForwarder(string? member)
    {
        InspectionEnvelope<ApiCoordinateMatchContent> envelope = await Match(
            "Avalonia", "11.3.14", "12.1.2", "Avalonia.Data.MultiBinding", member, "net8.0");

        AssertStatus(ApiCoordinateMatchStatus.Exact, envelope);
        ApiCoordinateMatchContent content = envelope.Content;
        Assert.Equal("Avalonia.Markup", content.Source!.Assembly.Name.ToString());
        Assert.Equal("ref/net8.0/Avalonia.Markup.dll", content.DestinationEntry!.Asset!.Value.ToString());
        Assert.Equal("Avalonia.Base", content.Destination!.Assembly.Name.ToString());
        Assert.Equal("ref/net8.0/Avalonia.Base.dll", content.Destination.Asset!.Value.ToString());
        ApiCoordinateMatchHop hop = Assert.Single(content.ForwardingHops);
        Assert.Equal("Avalonia.Markup", hop.Source.Assembly.Name.ToString());
        Assert.Equal("Avalonia.Base", hop.Target.Name.ToString());
        Assert.NotEmpty(hop.ExportedTypeTokens);
        Assert.NotNull(content.Source.MetadataToken);
        Assert.NotNull(content.Destination.MetadataToken);
        Assert.NotEqual(content.Source.ModuleVersionId, content.Destination.ModuleVersionId);
        Assert.Equal(member is null ? ApiDeclarationKind.Type : ApiDeclarationKind.Method,
            content.Destination.DeclarationKind);
        Assert.IsType<InspectionPortableProjection.NonProjectable>(envelope.PortableProjection);
        Assert.Empty(envelope.Diagnostics);
        ApiCoordinateCorrespondenceEvidence evidence =
            Assert.IsType<ApiCoordinateCorrespondenceEvidence>(content.Evidence);
        Assert.False(evidence.IsCompleteDestinationAbsence);
        Assert.Equal("Avalonia.Markup", evidence.Source.Library.Assembly.Assembly.Name);
        Assert.Equal("Avalonia.Base", evidence.Destination!.Library.Assembly.Assembly.Name);
        Assert.Equal("ref/net8.0/Avalonia.Base.dll", evidence.Destination.Library.Asset!.Path);
        CoordinateTypeResolutionOutcomeEvidence.Resolved resolved =
            Assert.IsType<CoordinateTypeResolutionOutcomeEvidence.Resolved>(
                Assert.IsType<CoordinateTypeResolutionEvidence.Available>(
                    evidence.Resolution).Outcome);
        Assert.Single(resolved.Hops);
        Assert.Same(
            evidence.Destination.Library.Assembly.Registration,
            resolved.Definition.Assembly.Assembly.Library!.Assembly.Registration);
    }

    [Fact]
    public async Task SystemTextJson_StringOptionsGenericOverload_HasAnExactDestinationAnchor()
    {
        InspectionEnvelope<ApiCoordinateMatchContent> envelope = await Match(
            "System.Text.Json", "9.0.0", "10.0.0",
            "System.Text.Json.JsonSerializer", "Deserialize~25fdd4cc7c");

        AssertStatus(ApiCoordinateMatchStatus.Exact, envelope);
        ApiCoordinateMatchContent content = envelope.Content;
        Assert.Equal("9.0.0", content.Source!.Package!.Version.ToString());
        Assert.Equal("10.0.0", content.Destination!.Package!.Version.ToString());
        Assert.Contains("25fdd4cc7c", content.Source.Member!.Value.ToString());
        Assert.Contains("25fdd4cc7c", content.Destination.Member!.Value.ToString());
        Assert.Empty(content.ForwardingHops);
        Assert.NotEqual(content.Source.ModuleVersionId, content.Destination.ModuleVersionId);
        Assert.Equal(ApiCoordinateMatchStage.DeclarationCorrespondence, content.Stage);

        string json = JsonSerializer.Serialize(content, ApiCoordinateMatchJsonContext.Default.ApiCoordinateMatchContent);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal("Exact", parsed.RootElement.GetProperty("status").GetString());
        Assert.Equal("10.0.0", parsed.RootElement.GetProperty("destination").GetProperty("package")
            .GetProperty("version").GetString());
        Assert.Equal(content.Stages.Length, parsed.RootElement.GetProperty("stages").GetArrayLength());
        using var markdown = new StringWriter();
        ApiCoordinateMatchPresentation.Render(content, markdown);
        Assert.Contains("Exact counterpart found.", markdown.ToString());
        Assert.Contains("System.Text.Json@10.0.0", markdown.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EqualDisplayDigest_DoesNotEraseExternalReferenceVersion()
    {
        InspectionEnvelope<ApiCoordinateMatchContent> envelope = await Match(
            "System.Text.Json", "9.0.0", "10.0.0",
            "System.Text.Json.JsonSerializer", "Deserialize~e1d907c309");

        AssertStatus(ApiCoordinateMatchStatus.Absent, envelope);
        Assert.Equal("net9.0", envelope.Content.Before.TargetFramework!.Value.ToString());
        Assert.Equal("net10.0", envelope.Content.After.TargetFramework!.Value.ToString());
        Assert.Contains(envelope.Content.Stages, stage =>
            stage.Stage == ApiCoordinateMatchStage.DeclarationCorrespondence
            && stage.Reason == nameof(ApiDeclarationCorrespondenceReason.NoExactDeclarationUnderProfile));
        Assert.Null(envelope.Content.Destination);
        Assert.Empty(envelope.Diagnostics);
        ApiCoordinateCorrespondenceEvidence evidence =
            Assert.IsType<ApiCoordinateCorrespondenceEvidence>(
                envelope.Content.Evidence);
        Assert.True(evidence.IsCompleteDestinationAbsence);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Absent,
            Assert.IsType<ApiDeclarationCorrespondenceResult>(
                evidence.Correspondence).Status);
        Assert.IsType<CoordinateTypeResolutionOutcomeEvidence.Resolved>(
            Assert.IsType<CoordinateTypeResolutionEvidence.Available>(
                evidence.Resolution).Outcome);
        using var markdown = new StringWriter();
        ApiCoordinateMatchPresentation.Render(envelope.Content, markdown);
        Assert.DoesNotContain("## Candidates", markdown.ToString());
        Assert.Contains($"Evaluated {envelope.Content.Candidates.Length} candidate declarations",
            markdown.ToString());
        Assert.Contains("| Entry |", markdown.ToString());
    }

    [Fact]
    public async Task DescendingEndpoints_ProveTypeAbsenceWithoutReversingTheRequest()
    {
        InspectionEnvelope<ApiCoordinateMatchContent> envelope = await Match(
            "System.Text.Json", "9.0.0", "8.0.6", "System.Text.Json.Schema.JsonSchemaExporter");

        AssertStatus(ApiCoordinateMatchStatus.Absent, envelope);
        Assert.Equal("9.0.0", envelope.Content.Before.Version.ToString());
        Assert.Equal("8.0.6", envelope.Content.After.Version.ToString());
        Assert.Equal(ApiCoordinateMatchStage.TypeResolution, envelope.Content.Stage);
        Assert.Contains(envelope.Content.Stages, stage =>
            stage.Stage == ApiCoordinateMatchStage.TypeResolution && stage.Outcome == "NotFound");
        Assert.Empty(envelope.Content.ForwardingHops);
        ApiCoordinateCorrespondenceEvidence evidence =
            Assert.IsType<ApiCoordinateCorrespondenceEvidence>(
                envelope.Content.Evidence);
        Assert.True(evidence.IsCompleteDestinationAbsence);
        CoordinateTypeResolutionOutcomeEvidence.NotFound notFound =
            Assert.IsType<CoordinateTypeResolutionOutcomeEvidence.NotFound>(
                Assert.IsType<CoordinateTypeResolutionEvidence.Available>(
                    evidence.Resolution).Outcome);
        Assert.Empty(notFound.Hops);
        Assert.Null(evidence.Correspondence);
    }

    [Fact]
    public async Task MissingSourceType_IsNotDestinationAbsenceAndCarriesDiagnostics()
    {
        InspectionEnvelope<ApiCoordinateMatchContent> envelope = await Match(
            "System.Text.Json", "8.0.6", "9.0.0", "System.Text.Json.Schema.JsonSchemaExporter");

        AssertStatus(ApiCoordinateMatchStatus.Refused, envelope);
        Assert.Equal(ApiCoordinateMatchStage.SourceSelection, envelope.Content.Stage);
        Assert.Null(envelope.Content.Destination);
        Assert.IsType<InspectionPortableProjection.NonProjectable>(envelope.PortableProjection);
        Assert.Equal("api-match.sourceselection", Assert.Single(envelope.Diagnostics).Code);
    }

    [Fact]
    public async Task SourceMemberAmbiguity_PreservesCandidateSelectors()
    {
        InspectionEnvelope<ApiCoordinateMatchContent> envelope = await Match(
            "System.Text.Json", "9.0.0", "10.0.0", "System.Text.Json.JsonSerializer", "Deserialize");

        AssertStatus(ApiCoordinateMatchStatus.Ambiguous, envelope);
        Assert.Equal(ApiCoordinateMatchStage.SourceSelection, envelope.Content.Stage);
        Assert.True(envelope.Content.Candidates.Length > 1);
        Assert.All(envelope.Content.Candidates, candidate =>
            Assert.NotNull(candidate.Member));
        Assert.Single(envelope.Diagnostics);
        Assert.True(envelope.Content.Candidates.Length > 10);
        using var markdown = new StringWriter();
        ApiCoordinateMatchPresentation.Render(envelope.Content, markdown);
        Assert.Contains($"Showing 10 of {envelope.Content.Candidates.Length} candidates", markdown.ToString());
        string[] rows = markdown.ToString().Split("## Candidates", StringSplitOptions.None)[1].Split('\n')
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(12, rows.Length);
    }

    [Fact]
    public async Task ForwardedTypeSuccess_DoesNotProveMemberCorrespondence()
    {
        InspectionEnvelope<ApiCoordinateMatchContent> envelope = await Match(
            "Avalonia", "11.3.14", "12.1.2", "Avalonia.Data.MultiBinding", "Converter", "net8.0");

        AssertStatus(ApiCoordinateMatchStatus.Absent, envelope);
        Assert.Equal(ApiCoordinateMatchStage.DeclarationCorrespondence, envelope.Content.Stage);
        Assert.Single(envelope.Content.ForwardingHops);
        Assert.Contains(envelope.Content.Stages, stage =>
            stage.Stage == ApiCoordinateMatchStage.TypeResolution && stage.Outcome == "Resolved");
        Assert.Equal("Avalonia.Base", Assert.Single(envelope.Content.Candidates).Assembly.Name.ToString());
        Assert.Null(envelope.Content.Destination);
        ApiCoordinateCorrespondenceEvidence evidence =
            Assert.IsType<ApiCoordinateCorrespondenceEvidence>(
                envelope.Content.Evidence);
        Assert.True(evidence.IsCompleteDestinationAbsence);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Absent,
            Assert.IsType<ApiDeclarationCorrespondenceResult>(
                evidence.Correspondence).Status);
        CoordinateTypeResolutionOutcomeEvidence.Resolved resolved =
            Assert.IsType<CoordinateTypeResolutionOutcomeEvidence.Resolved>(
                Assert.IsType<CoordinateTypeResolutionEvidence.Available>(
                    evidence.Resolution).Outcome);
        Assert.Single(resolved.Hops);
    }

    [Fact]
    public async Task ForwarderTargetOmittedFromSelectedPopulation_IsNotApiAbsence()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding before = FacadeOnlyFixture("1.0.0", "11.3.14");
        PackageRootBinding after = FacadeOnlyFixture("2.0.0", "12.1.2");
        WorkspaceScopeSnapshot initial = Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot scope = Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.ReplaceScopeAsync(initial.Revision, [before, after],
                DateTimeOffset.UtcNow.AddMinutes(1), TestContext.Current.CancellationToken)).Snapshot;
        CoordinatePackageObservation source = await Observe(before);
        CoordinatePackageObservation destination = await Observe(after);
        ApiCoordinateSourceSelectionResult selected = await ApiCoordinateSourceSelectionQuery.ExecuteAsync(
            workspace, source, new("coordinate.sample", "1.0.0", "2.0.0", "Avalonia.Data.MultiBinding"),
            cancellationToken: TestContext.Current.CancellationToken);

        var envelope = await ApiCoordinateMatchInspection.ExecuteAsync(
            workspace, Assert.IsType<StructuralSubjectIdentity.TypeSubject>(selected.Subject),
            source, destination, TestContext.Current.CancellationToken);

        AssertStatus(ApiCoordinateMatchStatus.Refused, envelope);
        Assert.Equal(ApiCoordinateMatchStage.TypeResolution, envelope.Content.Stage);
        Assert.Single(envelope.Content.ForwardingHops);
        ApiCoordinateMatchStageEvidence failure = envelope.Content.Stages[^1];
        Assert.Equal("UnboundBinding", failure.Outcome);
        Assert.Equal("Avalonia.Base", failure.Target!.Name.ToString());
        Assert.Null(envelope.Content.Destination);
        Assert.Single(envelope.Diagnostics);

        async Task<CoordinatePackageObservation> Observe(PackageRootBinding binding) =>
            Assert.IsType<CoordinatePackageObservationResult.Available>(
                await CoordinateLibraryPairingQuery.ObserveAsync(workspace, binding,
                    scope.FindPackageOccurrence(binding)!, TestContext.Current.CancellationToken)).Observation;
    }

    static PackageRootBinding FacadeOnlyFixture(string fixtureVersion, string avaloniaVersion)
    {
        string archivePath = Path.Combine(AppContext.BaseDirectory, "RealAssets", "ApiMatching",
            $"avalonia.{avaloniaVersion}.nupkg");
        const string asset = "ref/net8.0/Avalonia.Markup.dll";
        using ZipArchive original = ZipFile.OpenRead(archivePath);
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream input = (original.GetEntry(asset)
                ?? throw new InvalidOperationException("Pinned Avalonia facade fixture is missing.")).Open();
            using Stream output = archive.CreateEntry(asset).Open();
            input.CopyTo(output);
        }
        PackageProducerIdentity producer = PackageProducerIdentity.NuGetOrg;
        var content = new InMemoryPackageContent(bytes.ToArray(), false, producer.Key);
        return PackageRootBinding.CreateFromSource(new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create("coordinate.sample", fixtureVersion),
            content, producer.Key, producer, PackagePayloadOrigin.Download), "net8.0");
    }

    static async Task<InspectionEnvelope<ApiCoordinateMatchContent>> Match(
        string packageId, string before, string after, string type, string? member = null, string? framework = null)
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        await packages.AddAsync(packageId, before);
        if (before != after)
            await packages.AddAsync(packageId, after);
        return await ApiCoordinateMatchConsumer.MatchAsync(
            new(packageId, before, after, type, member, framework), packages,
            TestContext.Current.CancellationToken);
    }

    static void AssertStatus(
        ApiCoordinateMatchStatus expected, InspectionEnvelope<ApiCoordinateMatchContent> envelope) =>
        Assert.True(expected == envelope.Content.Status,
            $"Expected {expected}, got {envelope.Content.Status}: {envelope.Content.Summary}\n"
            + string.Join("\n", envelope.Content.Stages));
}
