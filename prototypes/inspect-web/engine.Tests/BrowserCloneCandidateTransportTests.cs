using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspector.Fixtures;
using DotnetInspector.Presentation;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;
using InspectWeb.Engine.AnalysisFacade;

namespace InspectWeb.Engine.Tests;

[CollectionDefinition(
    "Clone candidate transport operations",
    DisableParallelization = true)]
public sealed class BrowserCloneCandidateTransportCollection;

[Collection("Clone candidate transport operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserCloneCandidateTransportTests
{
    const string CloneAssembly =
        "DotnetInspector.CloneSearchFixtures.dll";
    const string CloneTransportAssembly =
        "InspectWeb.CloneTransportFixtures.dll";
    const string MethodBodyAssembly =
        "InspectWeb.MethodBodyFixtures.dll";
    const string Framework = "net11.0";

    [Fact]
    public async Task ExportCarriesBreadthSeedsAndClosedFailure()
    {
        await using Fixture fixture = await Fixture.Open();
        BrowserCloneCandidateResult broad =
            await fixture.Query(
                Seed(BrowserCloneCandidateSeedKind.Library),
                BrowserCloneCandidateBreadth.Everything,
                BrowserCloneCandidateDiscovery.SimilarNames);

        Assert.Equal(
            BrowserCloneCandidateResultKind.Available,
            broad.Kind);
        Assert.Equal(
            BrowserCloneCandidateBreadth.Everything,
            broad.Request.Breadth);
        Assert.Equal(
            BrowserCloneCandidateDiscovery.SimilarNames,
            broad.Request.Discovery);
        BrowserCloneCandidateDocument broadDocument =
            Assert.IsType<BrowserCloneCandidateDocument>(
                broad.Document);
        Assert.Equal(
            BrowserCloneCandidateBreadth.Everything,
            broadDocument.Breadth);
        Assert.Equal(
            BrowserCloneCandidateDiscovery.SimilarNames,
            broadDocument.Discovery);
        Assert.Equal(0.6, broadDocument.NameSimilarityThreshold);
        Assert.Equal(2, broadDocument.Libraries.Length);
        Assert.Equal(2, broadDocument.Receipt.AdmittedLibraries);
        Assert.All(
            broadDocument.Libraries,
            library =>
            {
                Assert.Equal(
                    BrowserCloneCandidateProvenanceKind.Package,
                    library.Participant.Provenance.Kind);
            });
        Assert.Equal(
            [.. new[]
                {
                    fixture.PackageId,
                    fixture.NeighborPackageId,
                }.Order(StringComparer.Ordinal)],
            [.. broadDocument.Libraries
                .Select(library =>
                    library.Participant.Provenance.PackageId!)
                .Order(StringComparer.Ordinal)]);
        Assert.Equal(
            broadDocument.Rows.Length,
            broadDocument.Receipt.ReturnedPairs);
        Assert.Equal(
            broadDocument.Receipt.ResultLimitReached,
            broadDocument.ResultLimitReached);

        BrowserCloneCandidateResult repeated =
            await fixture.Query(
                Seed(BrowserCloneCandidateSeedKind.Library),
                BrowserCloneCandidateBreadth.Everything,
                BrowserCloneCandidateDiscovery.SimilarNames);
        Assert.Equal(
            BrowserCloneCandidateResultKind.Available,
            repeated.Kind);

        BrowserCloneCandidateRequest defaultRequest =
            fixture.Request(
                Seed(BrowserCloneCandidateSeedKind.Library),
                BrowserCloneCandidateBreadth.Self,
                BrowserCloneCandidateDiscovery.All);
        JsonObject defaultJson = Assert.IsType<JsonObject>(
            JsonSerializer.SerializeToNode(
                defaultRequest,
                BrowserAnalysisJsonContext.Default
                    .BrowserCloneCandidateRequest));
        Assert.True(defaultJson.Remove("breadth"));
        Assert.True(defaultJson.Remove("discovery"));
        BrowserCloneCandidateResult defaults =
            await fixture.QueryJson(defaultJson.ToJsonString());
        Assert.Equal(
            BrowserCloneCandidateBreadth.Everything,
            defaults.Request.Breadth);
        Assert.Equal(
            BrowserCloneCandidateDiscovery.SimilarNames,
            defaults.Request.Discovery);

        BrowserCloneCandidateResult ecosystems =
            await fixture.Query(
                Seed(BrowserCloneCandidateSeedKind.Library),
                BrowserCloneCandidateBreadth
                    .SelfAndRegisteredEcosystems,
                BrowserCloneCandidateDiscovery.All);
        BrowserCloneCandidateDocument ecosystemDocument =
            Assert.IsType<BrowserCloneCandidateDocument>(
                ecosystems.Document);
        Assert.Equal(
            BrowserCloneCandidateBreadth
                .SelfAndRegisteredEcosystems,
            ecosystemDocument.Breadth);
        Assert.Equal(
            BrowserCloneCandidateDiscovery.All,
            ecosystemDocument.Discovery);
        Assert.Equal(1, ecosystemDocument.Receipt.AdmittedLibraries);
        Assert.Equal(1, ecosystemDocument.Receipt.ExcludedLibraries);
        Assert.DoesNotContain(
            ecosystemDocument.Libraries,
            library =>
                library.Membership
                    == BrowserCloneCandidateParticipantMembership
                        .RegisteredEcosystem);

        BrowserCloneCandidateResult type =
            await fixture.Query(
                Seed(
                    BrowserCloneCandidateSeedKind.Type,
                    fixture.WidgetType),
                BrowserCloneCandidateBreadth.Self,
                BrowserCloneCandidateDiscovery.All);
        Assert.Equal(
            BrowserCloneCandidateSeedKind.Type,
            type.Document!.Seed.Kind);
        Assert.Equal(
            ["Widget"],
            type.Document.Seed.Type!.Segments);

        BrowserCloneCandidateResult logicalMember =
            await fixture.Query(
                Seed(
                    BrowserCloneCandidateSeedKind.Member,
                    fixture.WidgetType,
                    fixture.ValueAnchor),
                BrowserCloneCandidateBreadth.Self,
                BrowserCloneCandidateDiscovery.All);
        Assert.Equal(
            BrowserCloneCandidateResultKind.Available,
            logicalMember.Kind);
        Assert.Equal(2, logicalMember.Document!.Seeds.Length);

        BrowserCloneCandidateResult accessor =
            await fixture.Query(
                Seed(
                    BrowserCloneCandidateSeedKind.Member,
                    fixture.WidgetType,
                    fixture.ValueAnchor,
                    fixture.ValueGetter),
                BrowserCloneCandidateBreadth.Self,
                BrowserCloneCandidateDiscovery.All);
        Assert.Equal(
            BrowserCloneCandidateResultKind.Available,
            accessor.Kind);
        BrowserCloneCandidateSeedCoverage accessorSeed =
            Assert.Single(accessor.Document!.Seeds);
        Assert.Equal(
            fixture.ValueGetter.MetadataToken,
            accessorSeed.Seed.MethodDefinitionToken);

        BrowserCloneCandidateResult bodyless =
            await fixture.Query(
                Seed(
                    BrowserCloneCandidateSeedKind.Member,
                    fixture.WidgetType,
                    fixture.TagAnchor),
                BrowserCloneCandidateBreadth.Self,
                BrowserCloneCandidateDiscovery.All);
        Assert.Equal(
            BrowserCloneCandidateResultKind.Failed,
            bodyless.Kind);
        Assert.Equal(
            BrowserCloneCandidateFailureKind
                .SeedMemberHasNoMethodBody,
            bodyless.Failure!.Kind);
        Assert.Null(bodyless.Document);
    }

    [Fact]
    public void ProjectionPreservesRejectedAndUnrepresentableOutcomes()
    {
        var identity =
            new AssemblyReferenceIdentity(
                "Clone.Transport",
                new Version(1, 2, 3, 4),
                "neutral",
                "0011223344556677");
        var rejected =
            new CloneCandidatePresentationResult.Rejected(
                identity,
                CandidateOpenFailureKind.UnsupportedMetadataFormat,
                new InertString(
                    TextPolicy.Field,
                    "metadata\nrejected"),
                MetadataRootMalformedReason.InvalidSignature);
        BrowserCloneCandidateResult rejectedWire =
            BrowserCloneCandidateWireProjection.Project(
                ProjectionRequest(),
                rejected);

        Assert.Equal(
            BrowserCloneCandidateResultKind.Rejected,
            rejectedWire.Kind);
        Assert.Equal(
            BrowserCloneCandidateOpenFailureKind
                .UnsupportedMetadataFormat,
            rejectedWire.OpenFailureKind);
        Assert.Equal(
            BrowserMetadataRootMalformedReason.InvalidSignature,
            rejectedWire.MetadataRootReason);
        Assert.DoesNotContain('\n', rejectedWire.Detail!);
        Assert.Equal("1.2.3.4", rejectedWire.SeedLibrary!.Version);

        var unrepresentable =
            new CloneCandidatePresentationResult.Unrepresentable(
                CloneCandidatePresentationRejectionKind
                    .ParticipantModuleInconsistent,
                identity,
                new InertString(
                    TextPolicy.Field,
                    "module mismatch"));
        BrowserCloneCandidateResult unrepresentableWire =
            BrowserCloneCandidateWireProjection.Project(
                ProjectionRequest(),
                unrepresentable);

        Assert.Equal(
            BrowserCloneCandidateResultKind.Unrepresentable,
            unrepresentableWire.Kind);
        Assert.Equal(
            BrowserCloneCandidatePresentationRejectionKind
                .ParticipantModuleInconsistent,
            unrepresentableWire.PresentationRejectionKind);
        Assert.Equal(
            "Clone.Transport",
            unrepresentableWire.Subject!.Name);
    }

    [Fact]
    public async Task ExportRejectsInconsistentExactMemberIdentity()
    {
        await using Fixture fixture = await Fixture.Open();
        BrowserCloneCandidateRequest valid =
            fixture.Request(
                Seed(
                    BrowserCloneCandidateSeedKind.Member,
                    fixture.WidgetType,
                    fixture.ValueAnchor,
                    fixture.ValueGetter),
                BrowserCloneCandidateBreadth.Self,
                BrowserCloneCandidateDiscovery.All);

        InvalidOperationException wrongType =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Query(
                    valid with
                    {
                        Seed = valid.Seed with
                        {
                            TypeDefinitionId = "Cases.Other",
                        },
                    }));
        Assert.Contains("does not contain", wrongType.Message);

        ArgumentOutOfRangeException wrongToken =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                fixture.Query(
                    valid with
                    {
                        Seed = valid.Seed with
                        {
                            Body = valid.Seed.Body! with
                            {
                                MetadataToken = 0x02000001,
                            },
                        },
                    }));
        Assert.Contains("not a MethodDef", wrongToken.Message);

        ArgumentException wrongBody =
            await Assert.ThrowsAsync<ArgumentException>(() =>
                fixture.Query(
                    valid with
                    {
                        Seed = valid.Seed with
                        {
                            Member = fixture.TagAnchor,
                        },
                    }));
        Assert.Contains("does not belong", wrongBody.Message);
    }

    [Fact]
    public async Task ExportAcceptsOwnerIssuedGenericAndAccessorIdentities()
    {
        await using Fixture fixture = await Fixture.Open();
        Assert.NotEqual(
            fixture.GenericCreate.TypeDefinitionId,
            fixture.GenericCreate.Member.TypeFullName);

        BrowserCloneCandidateResult generic =
            await fixture.Query(
                Seed(
                    BrowserCloneCandidateSeedKind.Member,
                    fixture.GenericCreate.TypeDefinitionId,
                    fixture.GenericCreate.Member),
                BrowserCloneCandidateBreadth.Self,
                BrowserCloneCandidateDiscovery.All,
                selectedPackageIndex: 1,
                assembly: CloneTransportAssembly);
        Assert.Equal(
            BrowserCloneCandidateResultKind.Available,
            generic.Kind);
        Assert.Single(generic.Document!.Seeds);

        BrowserCloneCandidateResult explicitAccessor =
            await fixture.Query(
                Seed(
                    BrowserCloneCandidateSeedKind.Member,
                    fixture.ExplicitValue.TypeDefinitionId,
                    fixture.ExplicitValue.Member,
                    fixture.ExplicitValue.Body),
                BrowserCloneCandidateBreadth.Self,
                BrowserCloneCandidateDiscovery.All,
                selectedPackageIndex: 1,
                assembly: CloneTransportAssembly);
        Assert.Equal(
            BrowserCloneCandidateResultKind.Available,
            explicitAccessor.Kind);
        Assert.Equal(
            fixture.ExplicitValue.Body!.MetadataToken,
            Assert.Single(explicitAccessor.Document!.Seeds)
                .Seed.MethodDefinitionToken);
    }

    [Fact]
    public async Task ExportMapsReferenceMembersToImplementationIdentities()
    {
        await using ReferenceFixture fixture =
            await ReferenceFixture.Open();

        BrowserCloneCandidateResult method =
            await fixture.Query(
                Seed(
                    BrowserCloneCandidateSeedKind.Member,
                    fixture.Method.TypeDefinitionId,
                    fixture.Method.Member),
                BrowserCloneCandidateBreadth.Self,
                BrowserCloneCandidateDiscovery.All);
        Assert.Equal(
            BrowserCloneCandidateResultKind.Available,
            method.Kind);
        Assert.NotEqual(
            fixture.Method.Member.CanonicalSignature,
            method.Document!.Seed.Member!.CanonicalSignature);
        Assert.Single(method.Document.Seeds);

        Assert.NotEqual(
            fixture.Value.Body!.MetadataToken,
            fixture.Value.ImplementationBodyToken);
        BrowserCloneCandidateResult accessor =
            await fixture.Query(
                Seed(
                    BrowserCloneCandidateSeedKind.Member,
                    fixture.Value.TypeDefinitionId,
                    fixture.Value.Member,
                    fixture.Value.Body),
                BrowserCloneCandidateBreadth.Self,
                BrowserCloneCandidateDiscovery.All);
        Assert.Equal(
            BrowserCloneCandidateResultKind.Available,
            accessor.Kind);
        Assert.Equal(
            fixture.Value.ImplementationBodyToken,
            Assert.Single(accessor.Document!.Seeds)
                .Seed.MethodDefinitionToken);
    }

    static BrowserCloneCandidateSeedRequest Seed(
        BrowserCloneCandidateSeedKind kind,
        string? type = null,
        BrowserCloneMemberAnchor? member = null,
        BrowserCloneCandidateBodySelection? body = null) =>
        new(kind, type, member, body);

    static BrowserCloneCandidateRequest ProjectionRequest() =>
        new(
            1,
            [new BrowserCloneCandidatePackage(
                "Clone.Transport",
                "1.0.0",
                Framework)],
            0,
            CloneAssembly,
            Seed(BrowserCloneCandidateSeedKind.Library),
            BrowserCloneCandidateBreadth.Everything,
            BrowserCloneCandidateDiscovery.SimilarNames);

    sealed class Fixture : IAsyncDisposable
    {
        Fixture(
            string packageId,
            string neighborPackageId,
            string widgetType,
            BrowserCloneMemberAnchor valueAnchor,
            BrowserCloneMemberAnchor tagAnchor,
            BrowserCloneCandidateBodySelection valueGetter,
            MemberCase genericCreate,
            MemberCase explicitValue)
        {
            PackageId = packageId;
            NeighborPackageId = neighborPackageId;
            WidgetType = widgetType;
            ValueAnchor = valueAnchor;
            TagAnchor = tagAnchor;
            ValueGetter = valueGetter;
            GenericCreate = genericCreate;
            ExplicitValue = explicitValue;
        }

        internal string PackageId { get; }
        internal string NeighborPackageId { get; }
        internal string WidgetType { get; }
        internal BrowserCloneMemberAnchor ValueAnchor { get; }
        internal BrowserCloneMemberAnchor TagAnchor { get; }
        internal BrowserCloneCandidateBodySelection ValueGetter { get; }
        internal MemberCase GenericCreate { get; }
        internal MemberCase ExplicitValue { get; }

        internal static async Task<Fixture> Open()
        {
            string packageId =
                "Clone.Transport." + Guid.NewGuid().ToString("N");
            string neighborPackageId =
                "Clone.Transport.Neighbor."
                    + Guid.NewGuid().ToString("N");
            byte[] clone =
                File.ReadAllBytes(
                    FixtureCatalog.CloneSearchMembers.AssemblyPath());
            byte[] neighbor =
                File.ReadAllBytes(
                    FixtureCatalog.InspectWebCloneTransport.AssemblyPath());
            using var primaryBytes = new MemoryStream();
            using (var archive =
                new ZipArchive(
                    primaryBytes,
                    ZipArchiveMode.Create,
                    leaveOpen: true))
            {
                Write(
                    archive,
                    $"lib/{Framework}/{CloneAssembly}",
                    clone);
            }
            using var neighborBytes = new MemoryStream();
            using (var archive =
                new ZipArchive(
                    neighborBytes,
                    ZipArchiveMode.Create,
                    leaveOpen: true))
            {
                Write(
                    archive,
                    $"lib/{Framework}/{CloneTransportAssembly}",
                    neighbor);
            }
            await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
                new BrowserPackage(
                    packageId,
                    "1.0.0",
                    primaryBytes.ToArray(),
                    fromCache: false));
            await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
                new BrowserPackage(
                    neighborPackageId,
                    "1.0.0",
                    neighborBytes.ToArray(),
                    fromCache: false));

            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(
                    FixtureCatalog.CloneSearchMembers.AssemblyPath());
            ApiSurface surface = session.ApiSurface(includeAll: true);
            ApiType widget =
                Assert.Single(
                    surface.Types,
                    type => type.FullName == "Cases.Widget");
            ApiMember value =
                Assert.Single(
                    widget.Members,
                    member =>
                        member.Name == "Value"
                        && member.Kind == "property");
            ApiMember tag =
                Assert.Single(
                    widget.Members,
                    member =>
                        member.Name == "Tag"
                        && member.Kind == "field");
            CallGraphMemberBodySelector getter =
                Assert.Single(
                    CallGraphMemberResolver
                        .CreateBodySelectors(widget, value),
                    selector => selector.MemberName == "get_Value");
            using AssemblyInspectionSession neighborSession =
                AssemblyInspectionSession.Open(
                    FixtureCatalog.InspectWebCloneTransport.AssemblyPath());
            ApiSurface neighborSurface =
                neighborSession.ApiSurface(includeAll: true);
            ApiType genericType =
                Assert.Single(
                    neighborSurface.Types,
                    type =>
                        type.DefinitionName?.ToMetadataFullName()
                            == "InspectWeb.CloneTransportFixtures.GenericSeed`1");
            ApiMember create =
                Assert.Single(
                    genericType.Members,
                    member => member.Name == "Create");
            ApiType bodyShapeType =
                Assert.Single(
                    neighborSurface.Types,
                    type =>
                        type.DefinitionName?.ToMetadataFullName()
                            == "InspectWeb.CloneTransportFixtures.ExplicitSeed");
            ApiMember explicitValue =
                Assert.Single(
                    bodyShapeType.Members,
                    member =>
                        member.Kind == "property"
                        && member.Name.EndsWith(
                            "IExplicitValue.Value",
                            StringComparison.Ordinal));
            CallGraphMemberBodySelector explicitGetter =
                Assert.Single(
                    CallGraphMemberResolver.CreateBodySelectors(
                        bodyShapeType,
                        explicitValue));

            return new Fixture(
                packageId,
                neighborPackageId,
                widget.DefinitionName!.ToEscapedFullName(),
                Project(ApiMemberIdentity.GetMemberAnchor(widget, value)),
                Project(ApiMemberIdentity.GetMemberAnchor(widget, tag)),
                new BrowserCloneCandidateBodySelection(
                    getter.MemberName,
                    getter.SelectorKey,
                    getter.BodyToken),
                new MemberCase(
                    genericType.DefinitionName!.ToEscapedFullName(),
                    Project(ApiMemberIdentity.GetMemberAnchor(
                        genericType,
                        create)),
                    null),
                new MemberCase(
                    bodyShapeType.DefinitionName!.ToEscapedFullName(),
                    Project(ApiMemberIdentity.GetMemberAnchor(
                        bodyShapeType,
                        explicitValue)),
                    new BrowserCloneCandidateBodySelection(
                        explicitGetter.MemberName,
                        explicitGetter.SelectorKey,
                        explicitGetter.BodyToken)));
        }

        internal async Task<BrowserCloneCandidateResult> Query(
            BrowserCloneCandidateSeedRequest seed,
            BrowserCloneCandidateBreadth breadth,
            BrowserCloneCandidateDiscovery discovery,
            int selectedPackageIndex = 0,
            string assembly = CloneAssembly)
            => await Query(Request(
                seed,
                breadth,
                discovery,
                selectedPackageIndex,
                assembly));

        internal BrowserCloneCandidateRequest Request(
            BrowserCloneCandidateSeedRequest seed,
            BrowserCloneCandidateBreadth breadth,
            BrowserCloneCandidateDiscovery discovery,
            int selectedPackageIndex = 0,
            string assembly = CloneAssembly) =>
            new(
                1,
                [
                    new BrowserCloneCandidatePackage(
                        PackageId,
                        "1.0.0",
                        Framework),
                    new BrowserCloneCandidatePackage(
                        NeighborPackageId,
                        "1.0.0",
                        Framework),
                ],
                selectedPackageIndex,
                assembly,
                seed,
                breadth,
                discovery);

        internal async Task<BrowserCloneCandidateResult> Query(
            BrowserCloneCandidateRequest request)
        {
            string requestJson = JsonSerializer.Serialize(
                request,
                BrowserAnalysisJsonContext.Default
                    .BrowserCloneCandidateRequest);
            Assert.Contains("\"packages\"", requestJson);
            Assert.Contains("\"breadth\":\"", requestJson);
            Assert.Contains("\"discovery\":\"", requestJson);
            return await QueryJson(requestJson);
        }

        internal async Task<BrowserCloneCandidateResult> QueryJson(
            string requestJson)
        {
            string resultJson =
                await AnalysisExports.QueryCloneCandidates(requestJson);
            return JsonSerializer.Deserialize(
                    resultJson,
                    BrowserAnalysisJsonContext.Default
                        .BrowserCloneCandidateResult)
                ?? throw new InvalidOperationException(
                    "The Clone Candidates result was empty.");
        }

        public async ValueTask DisposeAsync()
        {
            BrowserInspectionScope scope;
            BrowserPackageRequest[] requests =
            {
                new(PackageId, "1.0.0", Framework),
                new(NeighborPackageId, "1.0.0", Framework),
            };
            await using (BrowserScopeResolution resolution =
                await BrowserPackageWorkspace
                    .ResolveAndOpenScopeAsync(requests))
                scope = resolution.Scope;
            await BrowserPackageWorkspace.RemoveScopeAsync(scope);
        }

        static BrowserCloneMemberAnchor Project(MemberAnchor anchor) =>
            new(
                anchor.StableSelector,
                anchor.CanonicalSignature,
                anchor.Fingerprint,
                anchor.TypeFullName,
                anchor.MemberName);

        static void Write(
            ZipArchive archive,
            string path,
            byte[] content)
        {
            using Stream entry = archive.CreateEntry(path).Open();
            entry.Write(content);
        }
    }

    sealed class ReferenceFixture : IAsyncDisposable
    {
        ReferenceFixture(
            string packageId,
            MemberCase method,
            MemberCase value)
        {
            PackageId = packageId;
            Method = method;
            Value = value;
        }

        internal string PackageId { get; }
        internal MemberCase Method { get; }
        internal MemberCase Value { get; }

        internal static async Task<ReferenceFixture> Open()
        {
            string packageId =
                "Clone.Transport.Reference."
                    + Guid.NewGuid().ToString("N");
            await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
                new BrowserPackage(
                    packageId,
                    "1.0.0",
                    File.ReadAllBytes(
                        FixtureCatalog.InspectWebMethodBodies
                            .AssetPath("package")),
                    fromCache: false));

            using AssemblyInspectionSession referenceSession =
                AssemblyInspectionSession.Open(
                    FixtureCatalog.InspectWebMethodBodies
                        .AssetPath("reference"));
            ApiSurface reference =
                referenceSession.ApiSurface(includeAll: true);
            ApiType referenceType =
                Assert.Single(
                    reference.Types,
                    type =>
                        type.DefinitionName?.ToMetadataFullName()
                            == "InspectWeb.MethodBodyFixtures.Left");
            ApiMember method =
                Assert.Single(
                    referenceType.Members,
                    member =>
                        member.Name == "Compute"
                        && member.SignatureModel?.Parameters.Count == 1);
            ApiMember value =
                Assert.Single(
                    referenceType.Members,
                    member =>
                        member.Name == "Value"
                        && member.Kind == "property");
            CallGraphMemberBodySelector referenceGetter =
                Assert.Single(
                    CallGraphMemberResolver.CreateBodySelectors(
                        referenceType,
                        value),
                    selector => selector.MemberName == "get_Value");

            using AssemblyInspectionSession implementationSession =
                AssemblyInspectionSession.Open(
                    FixtureCatalog.InspectWebMethodBodies.AssemblyPath());
            ApiSurface implementation =
                implementationSession.ApiSurface(includeAll: true);
            ApiType implementationType =
                Assert.Single(
                    implementation.Types,
                    type =>
                        type.DefinitionName?.ToMetadataFullName()
                            == "InspectWeb.MethodBodyFixtures.Left");
            ApiMember implementationValue =
                Assert.Single(
                    implementationType.Members,
                    member =>
                        member.Name == "Value"
                        && member.Kind == "property");
            CallGraphMemberBodySelector implementationGetter =
                Assert.Single(
                    CallGraphMemberResolver.CreateBodySelectors(
                        implementationType,
                        implementationValue),
                    selector => selector.MemberName == "get_Value");

            return new ReferenceFixture(
                packageId,
                new MemberCase(
                    referenceType.DefinitionName!.ToEscapedFullName(),
                    Project(ApiMemberIdentity.GetMemberAnchor(
                        referenceType,
                        method)),
                    null),
                new MemberCase(
                    referenceType.DefinitionName!.ToEscapedFullName(),
                    Project(ApiMemberIdentity.GetMemberAnchor(
                        referenceType,
                        value)),
                    new BrowserCloneCandidateBodySelection(
                        referenceGetter.MemberName,
                        referenceGetter.SelectorKey,
                        referenceGetter.BodyToken),
                    implementationGetter.BodyToken));
        }

        internal async Task<BrowserCloneCandidateResult> Query(
            BrowserCloneCandidateSeedRequest seed,
            BrowserCloneCandidateBreadth breadth,
            BrowserCloneCandidateDiscovery discovery)
        {
            var request = new BrowserCloneCandidateRequest(
                1,
                [
                    new BrowserCloneCandidatePackage(
                        PackageId,
                        "1.0.0",
                        Framework),
                ],
                0,
                MethodBodyAssembly,
                seed,
                breadth,
                discovery);
            string json =
                await AnalysisExports.QueryCloneCandidates(
                    JsonSerializer.Serialize(
                        request,
                        BrowserAnalysisJsonContext.Default
                            .BrowserCloneCandidateRequest));
            return JsonSerializer.Deserialize(
                    json,
                    BrowserAnalysisJsonContext.Default
                        .BrowserCloneCandidateResult)
                ?? throw new InvalidOperationException(
                    "The Clone Candidates transport returned no result.");
        }

        public async ValueTask DisposeAsync()
        {
            BrowserInspectionScope scope;
            await using (BrowserScopeResolution resolution =
                await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
                    [
                        new BrowserPackageRequest(
                            PackageId,
                            "1.0.0",
                            Framework),
                    ]))
            {
                scope = resolution.Scope;
            }
            await BrowserPackageWorkspace.RemoveScopeAsync(scope);
        }

        static BrowserCloneMemberAnchor Project(MemberAnchor anchor) =>
            new(
                anchor.StableSelector,
                anchor.CanonicalSignature,
                anchor.Fingerprint,
                anchor.TypeFullName,
                anchor.MemberName);
    }

    sealed record MemberCase(
        string TypeDefinitionId,
        BrowserCloneMemberAnchor Member,
        BrowserCloneCandidateBodySelection? Body,
        int? ImplementationBodyToken = null);
}
