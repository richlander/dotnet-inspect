using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed partial class ExactTypeInspectionOperationTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;
    const string PackageId = "dotnet-inspector.sections.test";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string SourceUrl = "https://example.test/v3/index.json";

    static readonly PackageSource Source =
        new("test", SourceUrl);

    [Fact]
    public async Task ExecuteAsync_ReturnsDetachedEquivalentEnvelopeAcrossColdRealizations()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactTypeInspectionRequest(
            PackageId,
            Version,
            Framework,
            typeof(ExactTypeInspectionOperation).FullName!);
        WorkspaceContextLoadOptions capabilities =
            LoadOptions(client, store);

        InspectionEnvelope<ExactTypeInspectionResult> first =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                capabilities,
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> second =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                capabilities,
                TestContext.Current.CancellationToken);

        AssertAvailable(first);
        AssertAvailable(second);
        Assert.Equal(first.Content.Outcome, second.Content.Outcome);
        Assert.Equal(first.Content.MatchedType, second.Content.MatchedType);
        Assert.Equal(
            first.Content.RequestedAssembly,
            second.Content.RequestedAssembly);
        Assert.Equal(
            first.Content.SupplierAssembly,
            second.Content.SupplierAssembly);
        Assert.Equal(first.Content.ForwardingHops, second.Content.ForwardingHops);
        Assert.Equal(first.Content.Failures, second.Content.Failures);
        Assert.Equal(first.PortableProjection, second.PortableProjection);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Equal(
            JsonSerializer.Serialize(first.Content.Type),
            JsonSerializer.Serialize(second.Content.Type));
    }

    [Fact]
    public async Task ExecuteAsync_NotFoundRetainsShareAndTypedDiagnostic()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Missing.Type"),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.NotFound,
            envelope.Content.Outcome);
        Assert.IsType<InspectionPortableProjection.Available>(envelope.PortableProjection);
        InspectionDiagnostic diagnostic = Assert.Single(
            envelope.Diagnostics,
            diagnostic => diagnostic.Code == "exact-type.not-found");
        Assert.Equal(
            InspectionDiagnosticSeverity.Error,
            diagnostic.Severity);
    }

    [Fact]
    public async Task ExecuteAsync_ContextFailureUsesStableDiagnosticCode()
    {
        using var client = new HttpClient(new NotFoundHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    typeof(ExactTypeInspectionOperation).FullName!),
                LoadOptions(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic => diagnostic.Code == "exact-type.context-load"
                && diagnostic.Severity
                    == InspectionDiagnosticSeverity.Error);
    }

    [Fact]
    public void ExactTypeEnvelope_PublicSurfaceCarriesNoLiveAuthorityOrContent()
    {
        var visited = new HashSet<Type>();
        Visit(typeof(InspectionEnvelope<ExactTypeInspectionResult>));

        void Visit(Type type)
        {
            if (!visited.Add(type)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(Guid)
                || type == typeof(Version))
            {
                return;
            }

            Assert.False(typeof(Stream).IsAssignableFrom(type));
            Assert.False(typeof(Delegate).IsAssignableFrom(type));
            Assert.False(typeof(InspectionWorkspace).IsAssignableFrom(type));
            Assert.False(
                typeof(WorkspaceRealizationOperationLease)
                    .IsAssignableFrom(type));
            Assert.False(
                typeof(AssemblyContextGroup).IsAssignableFrom(type));
            Assert.False(type.IsByRefLike);
            foreach (Type argument in type.GetGenericArguments())
                Visit(argument);
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain(
                    "Path",
                    property.Name,
                    StringComparison.OrdinalIgnoreCase);
                Visit(property.PropertyType);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ForwardedTypeRetainsRequestedAndSupplierIdentity()
    {
        const string typeNamespace = "Exact.Type";
        const string typeName = "Forwarded";
        const string facadeName = "AFacade";
        const string supplierName = "ZSupplier";
        Guid supplierMvid = Guid.NewGuid();
        byte[] supplier = BuildMetadataAssembly(
            supplierName,
            supplierMvid,
            definesType: true,
            typeNamespace,
            typeName);
        byte[] facade = BuildMetadataAssembly(
            facadeName,
            Guid.NewGuid(),
            definesType: false,
            typeNamespace,
            typeName,
            new AssemblyReferenceIdentity(
                supplierName,
                new Version(1, 0, 0, 0),
                null,
                null));
        var store = await CachedStoreAsync(
            ($"lib/net11.0/{facadeName}.dll", facade),
            ($"lib/net11.0/{supplierName}.dll", supplier));
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    $"{typeNamespace}.{typeName}"),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        ExactTypeInspectionResult result = envelope.Content;
        Assert.Equal(ExactTypeInspectionOutcome.Available, result.Outcome);
        Assert.Equal(facadeName, result.RequestedAssembly?.Identity.Name);
        Assert.Equal(supplierName, result.SupplierAssembly?.Identity.Name);
        Assert.Equal(supplierMvid, result.SupplierAssembly?.ModuleVersionId);
        Assert.True(result.Type?.IsForwarded);
        ExactTypeForwardingHop hop = Assert.Single(result.ForwardingHops);
        Assert.Equal(facadeName, hop.Source.Name);
        Assert.Equal(supplierName, hop.Target.Name);
    }

    [Fact]
    public async Task ExecuteAsync_UnresolvedMatchingRootMakesSelectionUnavailable()
    {
        const string typeNamespace = "Exact";
        const string typeName = "Widget";
        byte[] definition = BuildMetadataAssembly(
            "Good",
            Guid.NewGuid(),
            definesType: true,
            typeNamespace,
            typeName);
        byte[] unresolvedForwarder = BuildMetadataAssembly(
            "Facade",
            Guid.NewGuid(),
            definesType: false,
            typeNamespace,
            typeName,
            new AssemblyReferenceIdentity(
                "Missing",
                new Version(1, 0, 0, 0),
                null,
                null));
        var store = await CachedStoreAsync(
            ("lib/net11.0/Facade.dll", unresolvedForwarder),
            ("lib/net11.0/Good.dll", definition));
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactTypeInspectionRequest(
            PackageId,
            Version,
            Framework,
            $"{typeNamespace}.{typeName}");

        InspectionEnvelope<ExactTypeInspectionResult> unbounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> bounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 10,
                    maxTypes: 100,
                    maxMembers: 100,
                    maxInspectionFailures: 100,
                    maxTypeForwarders: 100,
                    maxMetadataRows: 10_000),
                TestContext.Current.CancellationToken);

        Assert.All(
            new[] { unbounded, bounded },
            envelope =>
            {
                Assert.Equal(
                    ExactTypeInspectionOutcome.Unavailable,
                    envelope.Content.Outcome);
                Assert.Null(envelope.Content.Type);
                Assert.Contains(
                    envelope.Content.Failures,
                    failure => failure.Kind
                        == ExactTypeInspectionFailureKind
                            .TypeResolutionUnavailable);
            });
    }

    [Fact]
    public async Task ExecuteAsync_UnresolvedMatchingRootTakesPrecedenceOverAmbiguity()
    {
        const string typeNamespace = "Exact";
        const string typeName = "Collision";
        byte[] first = BuildMetadataAssembly(
            "First",
            Guid.NewGuid(),
            definesType: true,
            typeNamespace,
            typeName);
        byte[] second = BuildMetadataAssembly(
            "Second",
            Guid.NewGuid(),
            definesType: true,
            typeNamespace,
            typeName);
        byte[] unresolvedForwarder = BuildMetadataAssembly(
            "Facade",
            Guid.NewGuid(),
            definesType: false,
            typeNamespace,
            typeName,
            new AssemblyReferenceIdentity(
                "Missing",
                new Version(1, 0, 0, 0),
                null,
                null));
        var store = await CachedStoreAsync(
            ("lib/net11.0/Facade.dll", unresolvedForwarder),
            ("lib/net11.0/First.dll", first),
            ("lib/net11.0/Second.dll", second));
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactTypeInspectionRequest(
            PackageId,
            Version,
            Framework,
            $"{typeNamespace}.{typeName}");

        InspectionEnvelope<ExactTypeInspectionResult> unbounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> bounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 10,
                    maxTypes: 100,
                    maxMembers: 100,
                    maxInspectionFailures: 100,
                    maxTypeForwarders: 100,
                    maxMetadataRows: 10_000),
                TestContext.Current.CancellationToken);

        Assert.All(
            new[] { unbounded, bounded },
            envelope =>
            {
                Assert.Equal(
                    ExactTypeInspectionOutcome.Unavailable,
                    envelope.Content.Outcome);
                Assert.Null(envelope.Content.Type);
                Assert.Contains(
                    envelope.Content.Failures,
                    failure => failure.Kind
                        == ExactTypeInspectionFailureKind
                            .TypeResolutionUnavailable);
                Assert.DoesNotContain(
                    envelope.Diagnostics,
                    diagnostic => diagnostic.Code
                        == "exact-type.ambiguous");
            });
    }

    [Fact]
    public async Task ExecuteAsync_DistinctExactDefinitionsAreAmbiguous()
    {
        const string typeName = "Exact.Type.Ambiguous";
        var store = await CachedStoreAsync(
            ("lib/net11.0/First.dll",
                BuildAssembly("First", typeName, typeof(IDisposable))),
            ("lib/net11.0/Second.dll",
                BuildAssembly("Second", typeName, typeof(IAsyncDisposable))));
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    typeName),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Ambiguous,
            envelope.Content.Outcome);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic => diagnostic.Code == "exact-type.ambiguous");
    }

    [Fact]
    public async Task ExecuteAsync_DistinctSimpleNameDefinitionsAreAmbiguous()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Collision.dll",
                BuildAssembly(
                    "Collision",
                    ("First.Widget", typeof(IDisposable)),
                    ("Second.Widget", typeof(IAsyncDisposable)))));
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Widget"),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Ambiguous,
            envelope.Content.Outcome);
        Assert.Null(envelope.Content.MatchedType);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic => diagnostic.Code == "exact-type.ambiguous");
    }

    [Fact]
    public async Task ExecuteAsync_PrefersExactShortNameOverGenericFallback()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Arity.dll",
                BuildAssembly(
                    "Arity",
                    ("N.Widget", typeof(IDisposable)),
                    ("N.Widget`1", typeof(IAsyncDisposable)))));
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Widget"),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Available,
            envelope.Content.Outcome);
        ExactTypeApi type = Assert.IsType<ExactTypeApi>(
            envelope.Content.Type);
        Assert.Equal("N.Widget", type.FullName);
        Assert.Contains(typeof(IDisposable).FullName!, type.Interfaces);
        Assert.DoesNotContain(
            typeof(IAsyncDisposable).FullName!,
            type.Interfaces);
    }

    [Fact]
    public async Task ExecuteAsync_PrefersFullNameOverNamespaceSuffix()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/FullName.dll",
                BuildAssembly(
                    "FullName",
                    ("N.Widget", typeof(IDisposable)),
                    ("Other.N.Widget", typeof(IAsyncDisposable)))));
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "N.Widget"),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Available,
            envelope.Content.Outcome);
        ExactTypeApi type = Assert.IsType<ExactTypeApi>(
            envelope.Content.Type);
        Assert.Equal("N.Widget", type.FullName);
        Assert.Contains(typeof(IDisposable).FullName!, type.Interfaces);
        Assert.DoesNotContain(
            typeof(IAsyncDisposable).FullName!,
            type.Interfaces);
    }

    [Fact]
    public async Task ExecuteAsync_HiddenExactDefinitionPreventsFalseUniqueness()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Public.dll",
                BuildAssembly(
                    "Public",
                    TypeAttributes.Public,
                    ("N.Widget", typeof(IDisposable)))),
            ("lib/net11.0/Internal.dll",
                BuildAssembly(
                    "Internal",
                    TypeAttributes.NotPublic,
                    ("N.Widget", typeof(IAsyncDisposable)))));
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "N.Widget"),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Ambiguous,
            envelope.Content.Outcome);
        Assert.Equal("N.Widget", envelope.Content.MatchedType);
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesFrameworkAssociation()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Framework.dll",
                BuildAssembly(
                    "Framework",
                    "N.Widget",
                    typeof(IDisposable))));
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework.ToUpperInvariant(),
                    "N.Widget"),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Available,
            envelope.Content.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_ShareUsesNestedDefinitionIdentity()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Nested.dll",
                BuildNestedAssembly()));
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "N.Outer.Inner"),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Available,
            envelope.Content.Outcome);
        Assert.Equal("N.Outer+Inner", envelope.Content.MatchedType);
        WorkspaceSharePacket packet =
            WorkspaceSharePacketCodec.Decode(
                Assert.IsType<InspectionPortableProjection.Available>(
                    envelope.PortableProjection).Packet,
                TestContext.Current.CancellationToken);
        Assert.Equal("N.Outer+Inner", packet.Type);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedSelectedTypeIsUnavailable()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Malformed.dll",
                BuildMalformedTypeAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactTypeInspectionRequest(
            PackageId,
            Version,
            Framework,
            "Exact.Type.Malformed");

        InspectionEnvelope<ExactTypeInspectionResult> unbounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> bounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 10,
                    maxTypes: 100,
                    maxMembers: 100,
                    maxInspectionFailures: 100,
                    maxTypeForwarders: 100,
                    maxMetadataRows: 10_000),
                TestContext.Current.CancellationToken);

        Assert.All(
            new[] { unbounded, bounded },
            envelope =>
            {
                Assert.Equal(
                    ExactTypeInspectionOutcome.Unavailable,
                    envelope.Content.Outcome);
                Assert.NotEmpty(envelope.Content.InspectionFailures);
                Assert.Contains(
                    envelope.Content.Failures,
                    failure => failure.Kind
                        == ExactTypeInspectionFailureKind
                            .InspectionIncomplete);
                Assert.Contains(
                    envelope.Diagnostics,
                    diagnostic => diagnostic.Code
                            == "exact-type.inspection-incomplete"
                        && diagnostic.Severity
                            == InspectionDiagnosticSeverity.Warning);
            });
    }

    [Fact]
    public async Task ExecuteAsync_MatchingMalformedDeclarationPreventsSelection()
    {
        const string typeName = "Exact.Type.Malformed";
        var store = await CachedStoreAsync(
            ("lib/net11.0/Healthy.dll",
                BuildAssembly(
                    "Healthy",
                    typeName,
                    typeof(IDisposable))),
            ("lib/net11.0/Malformed.dll",
                BuildMalformedTypeAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactTypeInspectionRequest(
            PackageId,
            Version,
            Framework,
            typeName);

        InspectionEnvelope<ExactTypeInspectionResult> unbounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> bounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 10,
                    maxTypes: 100,
                    maxMembers: 100,
                    maxInspectionFailures: 100,
                    maxTypeForwarders: 100,
                    maxMetadataRows: 10_000),
                TestContext.Current.CancellationToken);

        Assert.All(
            new[] { unbounded, bounded },
            envelope =>
            {
                Assert.Equal(
                    ExactTypeInspectionOutcome.Unavailable,
                    envelope.Content.Outcome);
                Assert.Null(envelope.Content.Type);
                Assert.Contains(
                    envelope.Content.Failures,
                    failure => failure.Kind
                        == ExactTypeInspectionFailureKind
                            .InspectionIncomplete);
            });
    }

    [Fact]
    public async Task ExecuteAsync_UnboundedMalformedForwarderIsUnavailable()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/MalformedForwarder.dll",
                BuildMalformedForwarderAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactTypeInspectionRequest(
            PackageId,
            Version,
            Framework,
            "Exact.Type.");

        InspectionEnvelope<ExactTypeInspectionResult> unbounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> bounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 10,
                    maxTypes: 100,
                    maxMembers: 100,
                    maxInspectionFailures: 100,
                    maxTypeForwarders: 100,
                    maxMetadataRows: 10_000),
                TestContext.Current.CancellationToken);

        Assert.All(
            new[] { unbounded, bounded },
            envelope =>
            {
                Assert.Equal(
                    ExactTypeInspectionOutcome.Unavailable,
                    envelope.Content.Outcome);
                Assert.Contains(
                    envelope.Content.InspectionFailures,
                    failure => failure.Operation
                        == ApiSurfaceInspectionFailure
                            .TypeForwarderIdentityOperation);
            });
    }

    [Fact]
    public async Task Execute_AssemblyBindingAmbiguityIsUnavailable()
    {
        const string typeNamespace = "N";
        const string typeName = "Type";
        var target = new AssemblyReferenceIdentity(
            "Target",
            new Version(1, 0, 0, 0),
            null,
            null);
        byte[] facadeBytes = BuildMetadataAssembly(
            "Facade",
            Guid.NewGuid(),
            definesType: false,
            typeNamespace,
            typeName,
            target);
        byte[] definingTargetBytes = BuildMetadataAssembly(
            "Target",
            Guid.NewGuid(),
            definesType: true,
            typeNamespace,
            typeName);
        byte[] emptyTargetBytes = BuildMetadataAssembly(
            "Target",
            Guid.NewGuid(),
            definesType: false,
            typeNamespace,
            typeName);
        ResolvedAssemblyReference facade =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "Facade",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(facadeBytes, writable: false),
                AssemblyResolutionProvenance.Local("facade"));
        ResolvedAssemblyReference definingTarget =
            ResolvedAssemblyReference.Create(
                target,
                path: null,
                () => new MemoryStream(
                    definingTargetBytes,
                    writable: false),
                AssemblyResolutionProvenance.Local("defining"));
        ResolvedAssemblyReference emptyTarget =
            ResolvedAssemblyReference.Create(
                target,
                path: null,
                () => new MemoryStream(
                    emptyTargetBytes,
                    writable: false),
                AssemblyResolutionProvenance.Local("empty"));
        IAcquisitionFreeAssemblyBindingPolicy policy =
            SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
                new[] { facade, definingTarget, emptyTarget }.Select(
                    assembly => (
                        assembly,
                        (IAcquisitionFreeAssemblyBindingPolicy)
                            NoResolverAssemblyBindingPolicy.Instance)));
        WorkspaceContextInput input = Input();
        WorkspaceMemberCoordinate declared = Assert.Single(input.Members);
        var realized = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            Framework,
            runtimeIdentifier: null);
        await using var coordinator =
            new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            Assert.IsType<WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(
                    new WorkspacePlan([], [input]),
                    TestContext.Current.CancellationToken))
            .Candidate;
        WorkspaceContextLoadOutcome.Loaded loaded;
        using (WorkspaceRealizationConstructionLease construction =
            candidate.EnterConstruction())
        {
            AssemblyContextParticipant facadeParticipant =
                new(facade, policy);
            AssemblyContextParticipant definingParticipant =
                new(definingTarget, policy);
            AssemblyContextParticipant emptyParticipant =
                new(emptyTarget, policy);
            AssemblyContextGroup group =
                construction.Workspace.CreateAssemblyContextGroup(
                    [
                        facadeParticipant,
                        definingParticipant,
                        emptyParticipant,
                    ]);
            loaded = new WorkspaceContextLoadOutcome.Loaded(
                construction.Workspace.Identity,
                group,
                [
                    new WorkspaceContextMember(
                        declared,
                        realized,
                        facadeParticipant),
                    new WorkspaceContextMember(
                        declared,
                        realized,
                        definingParticipant),
                    new WorkspaceContextMember(
                        declared,
                        realized,
                        emptyParticipant),
                ],
                [],
                Framework,
                runtimeIdentifier: null);
        }
        _ = await coordinator.CompleteCandidateAsync(
            candidate,
            TestContext.Current.CancellationToken);
        _ = coordinator.CutOver(candidate);
        using WorkspaceRealizationOperationLease authority =
            Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
                await coordinator.EnterOperationAsync(
                    TestContext.Current.CancellationToken))
            .Lease;

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            ExactTypeInspectionOperation.Execute(
                authority,
                loaded,
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    $"{typeNamespace}.{typeName}"));

        Assert.Equal(
            ExactTypeInspectionOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Null(envelope.Content.Type);
        Assert.Contains(
            envelope.Content.Failures,
            failure => failure.Kind
                == ExactTypeInspectionFailureKind
                    .TypeResolutionUnavailable);
    }

    [Fact]
    public async Task ExecuteAsync_DefinitionIdentityUsesOrdinalMatching()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/CaseDistinct.dll",
                BuildAssembly(
                    "CaseDistinct",
                    ("Exact.Type.Widget", typeof(IDisposable)),
                    ("Exact.Type.widget", typeof(IAsyncDisposable)))));
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Exact.Type.Widget",
                    ExactTypeSelectionKind.DefinitionIdentity),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Available,
            envelope.Content.Outcome);
        ExactTypeApi type = Assert.IsType<ExactTypeApi>(
            envelope.Content.Type);
        Assert.Equal("Exact.Type", type.DefinitionIdentity.Namespace);
        Assert.Equal(["Widget"], type.DefinitionIdentity.Segments);
        Assert.Equal([0], type.IntroducedTypeParameterCounts);
        Assert.Contains(typeof(IDisposable).FullName!, type.Interfaces);
        Assert.DoesNotContain(
            typeof(IAsyncDisposable).FullName!,
            type.Interfaces);
    }

    [Fact]
    public async Task Execute_DisposedLeaseIsRejected()
    {
        const string typeName = "Exact.Type.DisposedLease";
        var store = await CachedStoreAsync(
            ("lib/net11.0/DisposedLease.dll",
                BuildAssembly(
                    "DisposedLease",
                    typeName,
                    typeof(IDisposable))));
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextInput input = Input();
        await using var coordinator =
            new WorkspaceReplacementCoordinator();
        (WorkspaceRealizationCandidate candidate,
            WorkspaceContextLoadOutcome.Loaded loaded) =
            await PrepareCandidateAsync(
                coordinator,
                new WorkspacePlan([], [input]),
                input,
                LoadOptions(client, store));
        _ = await coordinator.CompleteCandidateAsync(
            candidate,
            TestContext.Current.CancellationToken);
        _ = coordinator.CutOver(candidate);
        WorkspaceRealizationOperationLease authority =
            Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
                await coordinator.EnterOperationAsync(
                    TestContext.Current.CancellationToken))
            .Lease;
        authority.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => ExactTypeInspectionOperation.Execute(
                authority,
                loaded,
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    typeName)));
    }

    [Fact]
    public async Task ExecuteAsync_UnrelatedMalformedTypeDoesNotPreventNotFound()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Malformed.dll",
                BuildMalformedTypeAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactTypeInspectionRequest(
            PackageId,
            Version,
            Framework,
            "Exact.Type.Absent");

        InspectionEnvelope<ExactTypeInspectionResult> unbounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> bounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 10,
                    maxTypes: 100,
                    maxMembers: 100,
                    maxInspectionFailures: 100,
                    maxTypeForwarders: 100,
                    maxMetadataRows: 10_000),
                TestContext.Current.CancellationToken);

        Assert.All(
            new[] { unbounded, bounded },
            envelope =>
            {
                Assert.Equal(
                    ExactTypeInspectionOutcome.NotFound,
                    envelope.Content.Outcome);
                Assert.NotEmpty(envelope.Content.InspectionFailures);
                Assert.Contains(
                    envelope.Diagnostics,
                    diagnostic => diagnostic.Code
                        == "exact-type.not-found");
            });
    }

    [Fact]
    public async Task ExecuteAsync_BoundedProjectionPreventsFalseSelectionAndAbsence()
    {
        const string selectedType = "Exact.Type.Selected";
        const string omittedType = "Exact.Type.Omitted";
        var store = await CachedStoreAsync(
            ("lib/net11.0/First.dll",
                BuildAssembly(
                    "First",
                    selectedType,
                    typeof(IDisposable))),
            ("lib/net11.0/Second.dll",
                BuildAssembly(
                    "Second",
                    omittedType,
                    typeof(IAsyncDisposable))));
        using var client = new HttpClient(new FailingHandler());
        var limits = new ApiSurfaceProjectionLimits(
            maxParticipants: 2,
            maxTypes: 1,
            maxMembers: 100,
            maxInspectionFailures: 100,
            maxTypeForwarders: 100,
            maxMetadataRows: 10_000);

        InspectionEnvelope<ExactTypeInspectionResult> selected =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    selectedType),
                LoadOptions(client, store),
                limits,
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> unavailable =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    omittedType),
                LoadOptions(client, store),
                limits,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Unavailable,
            selected.Content.Outcome);
        Assert.Null(selected.Content.Type);
        Assert.False(selected.Content.IsComplete);
        Assert.Contains(
            selected.Content.Failures,
            failure => failure.Kind
                == ExactTypeInspectionFailureKind.ProjectionTruncated);
        Assert.Contains(
            selected.Diagnostics,
            diagnostic => diagnostic.Code
                    == "exact-type.projection-truncated"
                && diagnostic.Severity
                    == InspectionDiagnosticSeverity.Warning);
        Assert.Equal(
            ExactTypeInspectionOutcome.Unavailable,
            unavailable.Content.Outcome);
        Assert.DoesNotContain(
            unavailable.Diagnostics,
            diagnostic => diagnostic.Code == "exact-type.not-found");
        Assert.Contains(
            unavailable.Diagnostics,
            diagnostic => diagnostic.Code
                == "exact-type.projection-truncated");
    }

    [Fact]
    public async Task ExecuteAsync_BoundedProjectionDoesNotHideDistinctExactDefinition()
    {
        const string typeName = "Exact.Type.Collision";
        var store = await CachedStoreAsync(
            ("lib/net11.0/First.dll",
                BuildAssembly("First", typeName, typeof(IDisposable))),
            ("lib/net11.0/Second.dll",
                BuildAssembly("Second", typeName, typeof(IAsyncDisposable))));
        using var client = new HttpClient(new FailingHandler());

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            await ExactTypeInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    typeName),
                LoadOptions(client, store),
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 2,
                    maxTypes: 1,
                    maxMembers: 100,
                    maxInspectionFailures: 100,
                    maxTypeForwarders: 100,
                    maxMetadataRows: 10_000),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactTypeInspectionOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Null(envelope.Content.Type);
        Assert.Contains(
            envelope.Content.Failures,
            failure => failure.Kind
                == ExactTypeInspectionFailureKind.ProjectionTruncated);
        Assert.DoesNotContain(
            envelope.Diagnostics,
            diagnostic => diagnostic.Code == "exact-type.ambiguous");
    }

    [Fact]
    public async Task ExecuteAsync_ConstraintFailureIsVisibleAndNonfatal()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Constraint.dll",
                BuildModuleConstraintAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactTypeInspectionRequest(
            PackageId,
            Version,
            Framework,
            "N.Holder<T>");
        WorkspaceContextLoadOptions capabilities =
            LoadOptions(client, store);
        InspectionEnvelope<ExactTypeInspectionResult> unbounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                capabilities,
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> bounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                capabilities,
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 10,
                    maxTypes: 100,
                    maxMembers: 100,
                    maxInspectionFailures: 100,
                    maxTypeForwarders: 100,
                    maxMetadataRows: 10_000),
                TestContext.Current.CancellationToken);

        Assert.All(
            new[] { unbounded, bounded },
            envelope =>
            {
                Assert.Equal(
                    ExactTypeInspectionOutcome.Available,
                    envelope.Content.Outcome);
                Assert.True(envelope.Content.IsComplete);
                Assert.Contains(
                    envelope.Content.InspectionFailures,
                    failure => failure.Operation
                        == ApiSurface.ConstraintResolutionOperation);
                Assert.Contains(
                    envelope.Diagnostics,
                    diagnostic => diagnostic.Code
                            == "exact-type.constraint-resolution-incomplete"
                        && diagnostic.Severity
                            == InspectionDiagnosticSeverity.Warning);
            });
    }

    [Fact]
    public async Task ExecuteAsync_UnrelatedConstraintFailureIsNotProjected()
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Constraint.dll",
                BuildModuleConstraintAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactTypeInspectionRequest(
            PackageId,
            Version,
            Framework,
            "N.Selected");
        WorkspaceContextLoadOptions capabilities =
            LoadOptions(client, store);

        InspectionEnvelope<ExactTypeInspectionResult> unbounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                capabilities,
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactTypeInspectionResult> bounded =
            await ExactTypeInspectionOperation.ExecuteAsync(
                request,
                capabilities,
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 10,
                    maxTypes: 100,
                    maxMembers: 100,
                    maxInspectionFailures: 100,
                    maxTypeForwarders: 100,
                    maxMetadataRows: 10_000),
                TestContext.Current.CancellationToken);

        Assert.All(
            new[] { unbounded, bounded },
            envelope =>
            {
                Assert.Equal(
                    ExactTypeInspectionOutcome.Available,
                    envelope.Content.Outcome);
                Assert.True(envelope.Content.IsComplete);
                Assert.DoesNotContain(
                    envelope.Content.InspectionFailures,
                    failure => failure.Operation
                        == ApiSurface.ConstraintResolutionOperation);
                Assert.DoesNotContain(
                    envelope.Diagnostics,
                    diagnostic => diagnostic.Code
                        == "exact-type.constraint-resolution-incomplete");
            });
    }

    [Fact]
    public async Task Execute_RejectedParticipantProducesVisibleUnavailableResult()
    {
        string typeName =
            typeof(ExactTypeInspectionOperation).FullName!;
        string path =
            typeof(ExactTypeInspectionOperation).Assembly.Location;
        byte[] bytes = await File.ReadAllBytesAsync(
            path,
            TestContext.Current.CancellationToken);
        ResolvedAssemblyReference healthy =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("available"));
        ResolvedAssemblyReference rejected =
            ResolvedAssemblyReference.Create(
                healthy.Identity with { Name = "WrongIdentity" },
                path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Local("rejected"));
        IAcquisitionFreeAssemblyBindingPolicy policy =
            SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
                [
                    (rejected, NoResolverAssemblyBindingPolicy.Instance),
                    (healthy, NoResolverAssemblyBindingPolicy.Instance),
                ]);
        WorkspaceContextInput input = Input();
        WorkspaceMemberCoordinate declared = Assert.Single(input.Members);
        var realized = new RealizedMemberCoordinate.Package(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            Framework,
            runtimeIdentifier: null);
        await using var coordinator =
            new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            Assert.IsType<WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(
                    new WorkspacePlan([], [input]),
                    TestContext.Current.CancellationToken))
            .Candidate;
        WorkspaceContextLoadOutcome.Loaded loaded;
        using (WorkspaceRealizationConstructionLease construction =
            candidate.EnterConstruction())
        {
            AssemblyContextParticipant rejectedParticipant =
                new(rejected, policy);
            AssemblyContextParticipant healthyParticipant =
                new(healthy, policy);
            AssemblyContextGroup group =
                construction.Workspace.CreateAssemblyContextGroup(
                    [rejectedParticipant, healthyParticipant]);
            loaded = new WorkspaceContextLoadOutcome.Loaded(
                construction.Workspace.Identity,
                group,
                [
                    new WorkspaceContextMember(
                        declared,
                        realized,
                        rejectedParticipant),
                    new WorkspaceContextMember(
                        declared,
                        realized,
                        healthyParticipant),
                ],
                [],
                Framework,
                runtimeIdentifier: null);
        }
        _ = Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await coordinator.CompleteCandidateAsync(
                candidate,
                TestContext.Current.CancellationToken));
        _ = Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
            coordinator.CutOver(candidate));
        using WorkspaceRealizationOperationLease authority =
            Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
                await coordinator.EnterOperationAsync(
                    TestContext.Current.CancellationToken))
            .Lease;

        InspectionEnvelope<ExactTypeInspectionResult> envelope =
            ExactTypeInspectionOperation.Execute(
                authority,
                loaded,
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    typeName));

        Assert.Equal(
            ExactTypeInspectionOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Contains(
            envelope.Content.Failures,
            failure => failure.Kind
                == ExactTypeInspectionFailureKind.ParticipantRejected);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic => diagnostic.Code
                    == "exact-type.participant-rejected"
                && diagnostic.Severity
                    == InspectionDiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task ExactQueryUsesAdmittedPredecessorAfterSuccessorCutover()
    {
        const string typeName = "Exact.Type.Generation";
        var predecessorStore = await CachedStoreAsync(
            ("lib/net11.0/Generation.dll",
                BuildAssembly(
                    "Generation",
                    typeName,
                    typeof(IDisposable))));
        var successorStore = await CachedStoreAsync(
            ("lib/net11.0/Generation.dll",
                BuildAssembly(
                    "Generation",
                    typeName,
                    typeof(IAsyncDisposable))));
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactTypeInspectionRequest(
            PackageId,
            Version,
            Framework,
            typeName);
        WorkspaceContextInput input = Input();
        var plan = new WorkspacePlan([], [input]);
        await using var coordinator = new WorkspaceReplacementCoordinator();

        (WorkspaceRealizationCandidate predecessorCandidate,
            WorkspaceContextLoadOutcome.Loaded predecessorLoaded) =
            await PrepareCandidateAsync(
                coordinator,
                plan,
                input,
                LoadOptions(client, predecessorStore));
        _ = await coordinator.CompleteCandidateAsync(
            predecessorCandidate,
            TestContext.Current.CancellationToken);
        _ = Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
            coordinator.CutOver(predecessorCandidate));
        WorkspaceRealizationOperationLease predecessor =
            Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
                await coordinator.EnterOperationAsync(
                    TestContext.Current.CancellationToken))
            .Lease;

        (WorkspaceRealizationCandidate successorCandidate,
            WorkspaceContextLoadOutcome.Loaded successorLoaded) =
            await PrepareCandidateAsync(
                coordinator,
                plan,
                input,
                LoadOptions(client, successorStore));
        _ = await coordinator.CompleteCandidateAsync(
            successorCandidate,
            TestContext.Current.CancellationToken);
        var cutover =
            Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
                coordinator.CutOver(successorCandidate));

        Assert.Throws<ArgumentException>(
            () => ExactTypeInspectionOperation.Execute(
                predecessor,
                successorLoaded,
                request));
        InspectionEnvelope<ExactTypeInspectionResult> predecessorResult =
            ExactTypeInspectionOperation.Execute(
                predecessor,
                predecessorLoaded,
                request);
        using WorkspaceRealizationOperationLease successor =
            Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
                await coordinator.EnterOperationAsync(
                    TestContext.Current.CancellationToken))
            .Lease;
        InspectionEnvelope<ExactTypeInspectionResult> successorResult =
            ExactTypeInspectionOperation.Execute(
                successor,
                successorLoaded,
                request);

        Assert.Contains(
            typeof(IDisposable).FullName!,
            predecessorResult.Content.Type!.Interfaces);
        Assert.DoesNotContain(
            typeof(IAsyncDisposable).FullName!,
            predecessorResult.Content.Type.Interfaces);
        Assert.Contains(
            typeof(IAsyncDisposable).FullName!,
            successorResult.Content.Type!.Interfaces);
        Assert.DoesNotContain(
            typeof(IDisposable).FullName!,
            successorResult.Content.Type.Interfaces);
        Assert.False(cutover.Predecessor!.Completion.IsCompleted);

        predecessor.Dispose();
        WorkspaceRealizationSettlement settlement =
            await cutover.Predecessor.Completion;
        Assert.Equal(
            WorkspaceRealizationRetirementReason.Replaced,
            settlement.Reason);
    }

    static void AssertAvailable(
        InspectionEnvelope<ExactTypeInspectionResult> envelope)
    {
        ExactTypeInspectionResult result = envelope.Content;
        Assert.Equal(ExactTypeInspectionOutcome.Available, result.Outcome);
        Assert.Equal(
            typeof(ExactTypeInspectionOperation).FullName,
            result.MatchedType);
        Assert.Equal(result.MatchedType, result.Type?.FullName);
        Assert.Equal(
            typeof(ExactTypeInspectionOperation).Assembly
                .GetName().Name,
            result.SupplierAssembly?.Identity.Name);
        Assert.Equal(
            typeof(ExactTypeInspectionOperation).Module.ModuleVersionId,
            result.SupplierAssembly?.ModuleVersionId);
        Assert.IsType<InspectionPortableProjection.Available>(envelope.PortableProjection);
    }

    static WorkspaceContextLoadOptions LoadOptions(
        HttpClient client,
        IPackageStore store) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization([Source]),
            PackageStore = store,
        };

    static async Task<IPackageStore> CachedStoreAsync()
        => await CachedStoreAsync(
            ($"lib/{Framework}/DotnetInspector.Sections.dll",
                await File.ReadAllBytesAsync(
                    typeof(ExactTypeInspectionOperation).Assembly.Location,
                    TestContext.Current.CancellationToken)));

    static async Task<IPackageStore> CachedStoreAsync(
        params (string EntryPath, byte[] Content)[] entries)
    {
        var store = new InMemoryPackageStore();
        byte[] package = Archive(entries);
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            new MemoryStream(package),
            TestContext.Current.CancellationToken);
        return store;
    }

    static WorkspaceContextInput Input() =>
        new()
        {
            Framework = Framework,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    PackageId,
                    Version,
                    Framework),
            ],
        };

    static async Task<(
        WorkspaceRealizationCandidate Candidate,
        WorkspaceContextLoadOutcome.Loaded Loaded)> PrepareCandidateAsync(
            WorkspaceReplacementCoordinator coordinator,
            WorkspacePlan plan,
            WorkspaceContextInput input,
            WorkspaceContextLoadOptions options)
    {
        WorkspaceRealizationCandidate candidate =
            Assert.IsType<WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(plan))
            .Candidate;
        WorkspaceContextLoadOutcome load;
        using (WorkspaceRealizationConstructionLease construction =
            candidate.EnterConstruction())
        {
            load = await WorkspaceContextLoader.LoadAsync(
                construction.Workspace,
                input,
                options,
                TestContext.Current.CancellationToken);
        }

        return (
            candidate,
            Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(load));
    }

    static byte[] BuildAssembly(
        string assemblyName,
        string typeName,
        Type implementedInterface) =>
        BuildAssembly(
            assemblyName,
            (typeName, implementedInterface));

    static byte[] BuildAssembly(
        string assemblyName,
        params (string TypeName, Type ImplementedInterface)[] types)
        => BuildAssembly(
            assemblyName,
            TypeAttributes.Public,
            types);

    static byte[] BuildAssembly(
        string assemblyName,
        TypeAttributes visibility,
        params (string TypeName, Type ImplementedInterface)[] types)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
        foreach ((string typeName, Type implementedInterface) in types)
        {
            TypeBuilder type = module.DefineType(
                typeName,
                visibility
                    | TypeAttributes.Abstract
                    | TypeAttributes.Class);
            int aritySeparator = typeName.LastIndexOf('`');
            if (aritySeparator >= 0
                && int.TryParse(
                    typeName.AsSpan(aritySeparator + 1),
                    out int arity)
                && arity > 0)
            {
                type.DefineGenericParameters(
                    [
                        .. Enumerable.Range(0, arity)
                            .Select(index => "T" + index),
                    ]);
            }
            type.AddInterfaceImplementation(implementedInterface);
            type.CreateType();
        }

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    static byte[] BuildNestedAssembly()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Nested"),
            typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule("Nested");
        TypeBuilder outer = module.DefineType(
            "N.Outer",
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        TypeBuilder nested = outer.DefineNestedType(
            "Inner",
            TypeAttributes.NestedPublic
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        nested.AddInterfaceImplementation(typeof(IDisposable));
        nested.CreateType();
        outer.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    static byte[] BuildMetadataAssembly(
        string assemblyName,
        Guid moduleVersionId,
        bool definesType,
        string typeNamespace,
        string typeName,
        AssemblyReferenceIdentity? forwardTarget = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(moduleVersionId),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        if (definesType)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString(typeNamespace),
                metadata.GetOrAddString(typeName),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }
        if (forwardTarget is not null)
        {
            AssemblyReferenceHandle target =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString(forwardTarget.Name),
                    forwardTarget.Version!,
                    culture: default,
                    publicKeyOrToken: default,
                    flags: default,
                    hashValue: default);
            metadata.AddExportedType(
                TypeAttributes.Public | Forwarder,
                metadata.GetOrAddString(typeNamespace),
                metadata.GetOrAddString(typeName),
                target,
                typeDefinitionId: 0);
        }

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildMalformedTypeAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Malformed.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Malformed"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        TypeSpecificationHandle malformedBase =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(new byte[] { 0x15 }));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Exact.Type"),
            metadata.GetOrAddString("Good"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Exact.Type"),
            metadata.GetOrAddString("Malformed"),
            baseType: malformedBase,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildMalformedForwarderAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("MalformedForwarder.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedForwarder"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle target =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Missing"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        metadata.AddExportedType(
            TypeAttributes.Public | Forwarder,
            metadata.GetOrAddString("Exact.Type"),
            metadata.GetOrAddString(""),
            target,
            typeDefinitionId: 0);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildModuleConstraintAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Constraint.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Constraint"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        ModuleReferenceHandle module =
            metadata.AddModuleReference(
                metadata.GetOrAddString("Other.netmodule"));
        TypeReferenceHandle constraint =
            metadata.AddTypeReference(
                module,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Constraint"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle holder =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Holder`1"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                holder,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(parameter, constraint);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Selected"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] Archive(
        params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string entryPath, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(entryPath).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }

    sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(
                    System.Net.HttpStatusCode.NotFound));
    }
}
