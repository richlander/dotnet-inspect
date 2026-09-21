using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspacePortableCoordinateReplacementTests
{
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Envelope_ContainsDerivedShareAndNativeRetention(bool library)
    {
        InMemoryPackageStore store = await StoreAsync(
            ("Avalonia", "11.3.14"), ("Avalonia", "12.1.2"));
        var input = AvaloniaDefinitions(
            library ? new PortableSubjectRequest.Library() : new PortableSubjectRequest.Type(),
            library ? "library.references" : "type.metadata");

        var result = await WorkspacePortableCoordinateReplacementOperation.ExecuteAsync(
            input, new("avalonia", version: "12.1.2"), Options(store),
            TestContext.Current.CancellationToken);

        Assert.True(result.Content.Succeeded, result.Content.Failure?.Detail);
        Assert.Equal(NavigationScopeSettlementKind.Committed, result.Content.Scope!.Kind);
        Assert.Equal("11.3.14", result.Content.Source!.Version);
        Assert.Equal("12.1.2", result.Content.Destination!.Version);
        Assert.Equal("net8.0", result.Content.Destination.Framework);
        Assert.Equal(library ? StructuralSubjectKind.Library : StructuralSubjectKind.Type,
            result.Content.ActiveSubject!.Kind);
        Assert.Equal(
            library ? NavigationCoordinateRetentionDisposition.ActiveLibraryContainmentTruncated
                : NavigationCoordinateRetentionDisposition.ExactPath,
            result.Content.Retention!.Disposition);
        Assert.Contains(result.Content.RetainedPath,
            subject => subject.Kind == StructuralSubjectKind.Library
                && subject.Label == (library
                    ? "ref/net8.0/Avalonia.Markup.dll" : "ref/net8.0/Avalonia.Base.dll"));
        Assert.Equal(library ? "library.references" : "type.metadata",
            result.Content.Inspector!.RequestedFacet);
        Assert.Equal(NavigationLensBasisKind.ExactRequest, result.Content.Inspector.Basis);
        var share = Assert.IsType<InspectionPortableProjection.Available>(result.PortableProjection);
        Assert.Equal("https://dotnet-inspect.net/?w=" + share.Packet, share.FullUrl);
        Assert.NotEqual(Encode(input), share.Packet);
        Assert.Equal(4, WorkspaceSharePacketCodec.Decode(
            share.Packet, TestContext.Current.CancellationToken).FormatVersion);
        if (library)
            Assert.Contains(result.Diagnostics, entry => entry.Code.EndsWith(".fallback"));
        else
            Assert.Empty(result.Diagnostics);

        string json = JsonSerializer.Serialize(result,
            WorkspacePortableCoordinateReplacementJsonContext.Default
                .InspectionEnvelopeWorkspacePortableCoordinateReplacementOutcome);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(
            "outcome",
            document.RootElement.GetProperty("contentKind").GetString());
        Assert.Equal("available", document.RootElement.GetProperty("portableProjection").GetProperty("kind").GetString());
        Assert.Equal(share.Packet,
            document.RootElement.GetProperty("portableProjection").GetProperty("packet").GetString());
        Assert.Equal(library ? "Library" : "Type",
            document.RootElement.GetProperty("content").GetProperty("activeSubject")
                .GetProperty("kind").GetString());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Envelope_MissingMemberEmitsRestorableTypeFallbackAndWarning()
    {
        InMemoryPackageStore store = await StoreAsync(
            ("Avalonia", "11.3.14"), ("Avalonia", "12.1.2"));
        var options = Options(store);
        var input = AvaloniaDefinitions(
            new PortableSubjectRequest.Member(), "member.overview",
            memberSignature: "P:Avalonia.Data.MultiBinding.Converter");
        var result = await WorkspacePortableCoordinateReplacementOperation.ExecuteAsync(
            input, new("avalonia", version: "12.1.2"), options,
            TestContext.Current.CancellationToken);

        Assert.True(result.Content.Succeeded, result.Content.Failure?.Detail);
        Assert.Equal(NavigationCoordinateRetentionDisposition.MemberFallbackToType,
            result.Content.Retention!.Disposition);
        Assert.Equal(ApiCoordinateCorrespondenceStatus.Absent,
            result.Content.Retention.MemberCorrespondence);
        Assert.Equal(StructuralSubjectKind.Type, result.Content.ActiveSubject!.Kind);
        Assert.Equal(NavigationLensBasisKind.Recommendation, result.Content.Inspector!.Basis);
        Assert.Null(result.Content.Inspector.RequestedFacet);
        Assert.Contains(result.Diagnostics, entry => entry.Code.EndsWith(".fallback"));
        var share = Assert.IsType<InspectionPortableProjection.Available>(result.PortableProjection);
        var definitions = WorkspaceSharePacketTransposer.ToCommittedDefinitions(
            WorkspaceSharePacketCodec.Decode(share.Packet, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        TestHost host = await RestoreAsync(definitions, options);
        try
        {
            var resolved = Assert.IsType<CompleteRestorationResolvedState.Version4>(
                host.Activation!.Snapshot.Resolved);
            var state = resolved.States.Single(entry => entry.NavigationId is not null);
            Assert.Equal(StructuralSubjectKind.Type, state.Initialization!.Subject!.Kind);
            Assert.Null(state.Initialization.Context!.Member);
            Assert.Equal("Avalonia.Base", state.Initialization.Context.Type!.Library.Identity.Assembly.Name);
            Assert.Null(state.Initialization.Lens);
        }
        finally
        {
            Assert.True((await host.Workspace!.CloseAsync()).Succeeded);
        }
    }

    [Fact]
    public async Task Envelope_RefusalHasNoSuccessfulShare()
    {
        var input = PackageOnlyDefinitions(InspectionDefinitionSchema.Version4);
        var result = await WorkspacePortableCoordinateReplacementOperation.ExecuteAsync(
            input, new("not-a-navigation-row", version: "12.1.2"),
            Options(new InMemoryPackageStore()), TestContext.Current.CancellationToken);

        Assert.False(result.Content.Succeeded);
        Assert.Equal(WorkspacePortableCoordinateReplacementFailureKind.NavigationSourceMissing,
            result.Content.Failure!.Kind);
        var portableProjection = Assert.IsType<
            InspectionPortableProjection.NonProjectable>(
                result.PortableProjection);
        Assert.Equal(result.Content.Failure.Detail, portableProjection.Explanation);
        Assert.Null(result.Content.Scope);
        Assert.Null(result.Content.Retention);
        Assert.Contains(result.Diagnostics, entry => entry.Severity == InspectionDiagnosticSeverity.Error);
        string json = JsonSerializer.Serialize(result,
            WorkspacePortableCoordinateReplacementJsonContext.Default
                .InspectionEnvelopeWorkspacePortableCoordinateReplacementOutcome);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal("nonProjectable",
            document.RootElement.GetProperty("portableProjection").GetProperty("kind").GetString());
        Assert.Equal("NavigationSourceMissing",
            document.RootElement.GetProperty("content").GetProperty("failure").GetProperty("kind").GetString());
    }
}
