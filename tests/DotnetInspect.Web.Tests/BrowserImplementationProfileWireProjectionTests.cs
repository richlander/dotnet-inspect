using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Web.Interop.Analysis;
using ILInspector.Metadata;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserImplementationProfileWireProjectionTests
{
    static readonly BrowserCompileLibraryAvailability s_compileLibrary =
        new(
            BrowserCompileLibraryStatus.Selected,
            "net11.0",
            null);

    [Fact]
    public async Task AvailableInspectionPreservesProfilesAndEnvelope()
    {
        byte[] content = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, content);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(
                group,
                participant,
                "ImplementationProfileSample",
                "AnalyzeAsync");

        InspectionEnvelope<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>>
                inspection =
                    ImplementationProfileFamilyInspectionOperation.Execute(
                        group,
                        participant,
                        selection);

        BrowserImplementationProfiles browser =
            BrowserImplementationProfileWireProjection.Project(
                inspection,
                s_compileLibrary);

        Assert.Equal(2, browser.SchemaVersion);
        Assert.Equal("available", browser.Outcome);
        Assert.Null(browser.Failure);
        Assert.NotNull(browser.Subject);
        Assert.Equal(
            Assert.IsType<InspectionShare.NonProjectable>(
                inspection.Share).Path,
            Assert.IsType<BrowserAnalysisInspectionShare>(
                browser.Share).Path);
        Assert.Equal(
            inspection.Diagnostics.Length,
            browser.Diagnostics.Length);

        AssemblyImplementationProfileFamilyInspection expected =
            Assert.IsType<
                AssemblyContextEntry<
                    AssemblyImplementationProfileFamilyInspection>.Available>(
                        inspection.Content)
                .Value;
        BrowserImplementationProfileContent projected =
            Assert.IsType<BrowserImplementationProfileContent>(
                browser.Content);
        Assert.Equal(expected.Members.Length, projected.Members.Length);
        Assert.Equal(
            selection.StableSelectors,
            projected.Members.Select(member => member.StableSelector));
        Assert.Equal(expected.Profiles.Length, projected.Profiles.Length);
        Assert.Equal(
            expected.Coverage.DeclaredMethods.Length,
            projected.Coverage.DeclaredMethodKeys.Length);
        Assert.Equal(
            expected.Coverage.ManagedMethodBodies.Length,
            projected.Coverage.ManagedMethodBodyKeys.Length);
        Assert.Equal(
            expected.Coverage.ProfiledEvidenceBodies.Length,
            projected.Coverage.ProfiledEvidenceBodyKeys.Length);
        Assert.Equal(
            expected.OverloadRelationships.Length,
            projected.OverloadRelationships.Length);
        Assert.Equal(
            projected.Methods.Length,
            projected.Methods.Select(method => method.Key).Distinct().Count());

        BrowserImplementationProfile generated = Assert.Single(
            projected.Profiles,
            profile =>
            {
                BrowserImplementationProfileMethod method =
                    projected.Methods.Single(
                        candidate => candidate.Key == profile.MethodKey);
                return method.DeclaringType.EndsWith(
                        "ImplementationProfileSample",
                        StringComparison.Ordinal)
                    && method.Name == "AnalyzeAsync"
                    && method.ParameterTypes is [string parameter]
                    && parameter is "int" or "System.Int32"
                    && profile.Async;
            });
        Assert.NotEqual(
            generated.MethodKey,
            generated.EvidenceMethodKey);
        BrowserImplementationProfilePublicMember generatedOwner =
            Assert.Single(generated.PublicMembers);
        Assert.Equal("AnalyzeAsync", generatedOwner.Member);
        Assert.Contains(
            projected.Methods.Single(
                method => method.Key == generated.EvidenceMethodKey)
                .MetadataToken,
            generatedOwner.BodyTokens);

        string json = JsonSerializer.Serialize(
            browser,
            BrowserAnalysisJsonContext.Default.BrowserImplementationProfiles);
        BrowserImplementationProfiles roundTrip =
            JsonSerializer.Deserialize(
                json,
                BrowserAnalysisJsonContext.Default
                    .BrowserImplementationProfiles)
            ?? throw new InvalidOperationException(
                "Implementation-profile Browser wire round trip returned null.");
        Assert.Equal(browser.SchemaVersion, roundTrip.SchemaVersion);
        Assert.Equal(browser.Outcome, roundTrip.Outcome);
        Assert.Equal(
            projected.Profiles.Length,
            Assert.IsType<BrowserImplementationProfileContent>(
                roundTrip.Content).Profiles.Length);
        Assert.Equal(
            projected.Members.Length,
            Assert.IsType<BrowserImplementationProfileContent>(
                roundTrip.Content).Members.Length);
    }

    [Fact]
    public async Task AvailableInspectionPreservesBodylessFamilyRoster()
    {
        byte[] content = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, content);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(
                group,
                participant,
                "IImplementationProfileBodylessSample",
                "Route");

        BrowserImplementationProfiles browser =
            BrowserImplementationProfileWireProjection.Project(
                ImplementationProfileFamilyInspectionOperation.Execute(
                    group,
                    participant,
                    selection),
                s_compileLibrary);

        BrowserImplementationProfileContent projected =
            Assert.IsType<BrowserImplementationProfileContent>(
                browser.Content);
        Assert.Equal(2, projected.Members.Length);
        Assert.All(
            projected.Members,
            member => Assert.Empty(member.BodyTokens));
        Assert.Empty(projected.Profiles);
    }

    [Fact]
    public async Task RejectedInspectionPreservesFailureAndDiagnostic()
    {
        byte[] content = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        ImplementationProfileFamilySelection selection;
        await using (var selectionWorkspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup selectionGroup =
                Group(selectionWorkspace, content);
            AssemblyContextParticipant selectionParticipant =
                Assert.Single(selectionGroup.Participants);
            selection = Selection(
                selectionGroup,
                selectionParticipant,
                "ImplementationProfileSample",
                "AnalyzeAsync");
        }
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            content,
            mutateIdentity: true);

        InspectionEnvelope<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>>
                inspection =
                    ImplementationProfileFamilyInspectionOperation.Execute(
                        group,
                        Assert.Single(group.Participants),
                        selection);

        BrowserImplementationProfiles browser =
            BrowserImplementationProfileWireProjection.Project(
                inspection,
                s_compileLibrary);

        Assert.Equal("rejected", browser.Outcome);
        Assert.Null(browser.Content);
        BrowserImplementationProfileFailure failure =
            Assert.IsType<BrowserImplementationProfileFailure>(
                browser.Failure);
        Assert.Equal("InvalidImage", failure.Kind);
        BrowserAnalysisInspectionDiagnostic diagnostic =
            Assert.Single(browser.Diagnostics);
        Assert.Equal(
            "implementation-profiles.participant-rejected",
            diagnostic.Code);
    }

    [Fact]
    public async Task FailedSelectionDoesNotProjectAsEmpty()
    {
        byte[] content = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, content);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection complete =
            Selection(
                group,
                participant,
                "ImplementationProfileSample",
                "Analyze");
        var partial = new ImplementationProfileFamilySelection(
            complete.TypeDefinitionId,
            complete.StableSelectors.Take(2));

        BrowserImplementationProfiles browser =
            BrowserImplementationProfileWireProjection.Project(
                ImplementationProfileFamilyInspectionOperation.Execute(
                    group,
                    participant,
                    partial),
                s_compileLibrary);

        Assert.Equal("failed", browser.Outcome);
        Assert.Null(browser.Content);
        BrowserImplementationProfileFailure failure =
            Assert.IsType<BrowserImplementationProfileFailure>(
                browser.Failure);
        Assert.Contains(
            "complete public overload family",
            failure.Detail,
            StringComparison.Ordinal);
        Assert.Equal(
            "implementation-profiles.participant-failed",
            Assert.Single(browser.Diagnostics).Code);
    }

    [Fact]
    public void UnavailableResultDoesNotPretendToBeAnEmptyInspection()
    {
        BrowserImplementationProfiles unavailable =
            BrowserImplementationProfileWireProjection.Unavailable(
                "NoImplementationAssembly",
                "No managed implementation assembly.",
                s_compileLibrary);

        Assert.Equal("unavailable", unavailable.Outcome);
        Assert.Null(unavailable.Subject);
        Assert.Null(unavailable.Content);
        Assert.Null(unavailable.Share);
        Assert.Empty(unavailable.Diagnostics);
        Assert.Equal(
            "NoImplementationAssembly",
            Assert.IsType<BrowserImplementationProfileFailure>(
                unavailable.Failure).Kind);
    }

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        byte[] content,
        bool mutateIdentity = false)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(content);
        using var reader = new PEReader(image);
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        if (mutateIdentity)
            identity = identity with { Name = "WrongIdentity" };
        var participant = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "browser-implementation-profile-fixture")),
            new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    static ImplementationProfileFamilySelection Selection(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string typeName,
        string memberName)
    {
        AssemblyApiSurface surface =
            Assert.IsType<
                AssemblyContextEntry<AssemblyApiSurface>.Available>(
                    AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                        group,
                        participant))
                .Value;
        ApiType type = Assert.Single(
            surface.Surface.Types,
            candidate => candidate.Name == typeName);
        return new(
            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
            type.Members
                .Where(member => member.Name == memberName)
                .Select(member =>
                    ApiMemberIdentity
                        .GetMemberAnchor(type, member)
                        .StableSelector));
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
