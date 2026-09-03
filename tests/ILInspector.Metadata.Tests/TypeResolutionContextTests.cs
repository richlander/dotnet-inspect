using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class TypeResolutionContextTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    [Fact]
    public async Task Dispose_WaitsForActiveApiExtraction()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = BuildAssembly(
            "Definitions",
            definesType: true);
        using var openEntered = new ManualResetEventSlim();
        using var releaseOpen = new ManualResetEventSlim();
        ResolvedAssemblyReference source = Descriptor(
            image,
            () =>
            {
                openEntered.Set();
                releaseOpen.Wait(cancellationToken);
            });
        var catalog = new TypeResolutionCatalog();
        Task<ResolutionAwareApiSurfaceOutcome> extraction =
            Task.Run(
                () => catalog.ExtractApiSurface(
                    source,
                    new RecordingPolicy(
                        _ => AssemblyBindingSelection.NotFound())),
                cancellationToken);
        openEntered.Wait(cancellationToken);

        using var disposeStarted = new ManualResetEventSlim();
        Task dispose = Task.Run(() =>
        {
            disposeStarted.Set();
            catalog.Dispose();
        }, cancellationToken);

        disposeStarted.Wait(cancellationToken);
        await Task.Delay(100, cancellationToken);
        Assert.False(dispose.IsCompleted);

        releaseOpen.Set();
        Assert.IsType<ResolutionAwareApiSurfaceOutcome.Read>(
            await extraction);
        await dispose.WaitAsync(
            TimeSpan.FromSeconds(5),
            cancellationToken);
        Assert.Throws<ObjectDisposedException>(() =>
            catalog.ExtractApiSurface(
                source,
                new RecordingPolicy(
                    _ => AssemblyBindingSelection.NotFound())));
    }

    [Fact]
    public void ExtractApiSurface_MalformedRootAdjacencyIsNotDuplicated()
    {
        byte[] image = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: Identity("Target"),
            omitForwarderFlag: true);
        ResolvedAssemblyReference source = Descriptor(image);
        using var catalog = new TypeResolutionCatalog();

        var read = Assert.IsType<ResolutionAwareApiSurfaceOutcome.Read>(
            catalog.ExtractApiSurface(
                source,
                new RecordingPolicy(
                    _ => AssemblyBindingSelection.NotFound()),
                includeAll: true,
                typesOnly: true));

        ApiSurfaceInspectionFailure failure =
            Assert.Single(read.Surface.InspectionFailures);
        Assert.Equal(
            ApiSurfaceInspectionFailure.TypeForwarderIdentityOperation,
            failure.Operation);
        Assert.Equal(0x27000001, failure.SubjectToken);
    }

    [Fact]
    public void DirectDefinition_ResolvesAndCachesFrozenOutcome()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.NotFound());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [assembly],
            [request]);
        TypeResolutionOutcome first = context.Resolve(request);
        TypeResolutionOutcome second = context.Resolve(request);

        var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(first);
        Assert.Same(first, second);
        Assert.Same(assembly, resolved.Definition.Assembly.Assembly);
        Assert.Equal(
            ReadMvid(image),
            resolved.Definition.Address.ModuleVersionId);
        Assert.Equal(context.Catalog, resolved.Definition.Key.Catalog);
        Assert.Empty(resolved.Hops);
        Assert.Empty(policy.Requests);
    }

    [Fact]
    public void DefinitionCorrespondence_IsCatalogOwnedAndTokenExact()
    {
        byte[] image = BuildAssembly(
            "Definitions",
            definesType: true,
            definesOtherType: true);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest type = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        TypeResolutionRequest other = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName("Other"));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(_ => AssemblyBindingSelection.NotFound()),
            [assembly],
            [type, other]);
        ResolvedTypeDefinitionKey typeKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(type)).Definition.Key;
        ResolvedTypeDefinitionKey otherKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(other)).Definition.Key;

        Assert.IsType<DefinitionCorrespondence.Same>(
            catalog.Compare(typeKey, typeKey));
        Assert.IsType<DefinitionCorrespondence.Different>(
            catalog.Compare(typeKey, otherKey));
    }

    [Fact]
    public void DefinitionCorrespondence_DuplicateArtifactIsClassScoped()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        ResolvedAssemblyReference first = Descriptor(image);
        ResolvedAssemblyReference second = Descriptor(image);
        ResolvedAssemblyReference third = Descriptor(image);
        ResolvedAssemblyReference[] assemblies = [first, second, third];
        TypeResolutionRequest[] requests = assemblies
            .Select(assembly => TypeResolutionRequest.FromAssembly(
                assembly,
                AssemblyResolutionScope.Any,
                TypeName()))
            .ToArray();
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(_ => AssemblyBindingSelection.NotFound()),
            assemblies,
            requests);
        ResolvedTypeDefinitionKey[] keys = requests
            .Select(request =>
                Assert.IsType<TypeResolutionOutcome.Resolved>(
                    context.Resolve(request)).Definition.Key)
            .ToArray();

        var duplicate = Assert.IsType<
            DefinitionCorrespondence.IndeterminateDuplicateArtifact>(
                catalog.Compare(keys[0], keys[1]));

        Assert.Equal(
            assemblies.Select(assembly => assembly.Registration).ToHashSet(),
            duplicate.Evidence.Candidates
                .Select(candidate => candidate.Assembly.Registration)
                .ToHashSet());
        Assert.All(
            duplicate.Evidence.Candidates,
            candidate => Assert.Equal(
                ReadMvid(image),
                candidate.Address.ModuleVersionId));
    }

    [Fact]
    public void DefinitionCorrespondence_CrossCatalogAndStaleAreVisible()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.NotFound());
        using var firstCatalog = new TypeResolutionCatalog();
        using var secondCatalog = new TypeResolutionCatalog();
        using TypeResolutionContext first =
            firstCatalog.CreateContext(policy, [assembly], [request]);
        ResolvedTypeDefinitionKey oldKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                first.Resolve(request)).Definition.Key;
        using TypeResolutionContext other =
            secondCatalog.CreateContext(policy, [assembly], [request]);
        ResolvedTypeDefinitionKey otherKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                other.Resolve(request)).Definition.Key;

        Assert.IsType<DefinitionCorrespondence.IncomparableCatalogs>(
            firstCatalog.Compare(oldKey, otherKey));

        using TypeResolutionContext current =
            firstCatalog.CreateContext(policy, [assembly], [request]);
        ResolvedTypeDefinitionKey currentKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                current.Resolve(request)).Definition.Key;

        Assert.IsType<DefinitionCorrespondence.StaleGeneration>(
            firstCatalog.Compare(oldKey, currentKey));
        Assert.IsType<DefinitionCorrespondence.Same>(
            firstCatalog.Compare(currentKey, currentKey));
    }

    [Fact]
    public void DefinitionJoinToken_IsStableExactAndStructurallyHashable()
    {
        byte[] image = BuildAssembly(
            "Definitions",
            definesType: true,
            definesOtherType: true);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest type = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        TypeResolutionRequest other = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName("Other"));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(_ => AssemblyBindingSelection.NotFound()),
            [assembly],
            [type, other]);
        ResolvedTypeDefinitionKey typeKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(type)).Definition.Key;
        ResolvedTypeDefinitionKey otherKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(other)).Definition.Key;

        DefinitionJoinToken first =
            IssuedToken(catalog, typeKey);
        DefinitionJoinToken second =
            IssuedToken(catalog, typeKey);
        DefinitionJoinToken otherToken =
            IssuedToken(catalog, otherKey);
        var equivalent = new DefinitionJoinToken(
            first.Catalog,
            first.Generation,
            first.Value,
            first.Kind,
            first.Evidence);

        Assert.Same(first, second);
        Assert.Equal(DefinitionJoinKind.Exact, first.Kind);
        Assert.Null(first.Evidence);
        Assert.Equal(first, equivalent);
        Assert.True(first == equivalent);
        Assert.False(first != equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(first, otherToken);
        Assert.Contains(equivalent, new HashSet<DefinitionJoinToken> { first });
    }

    [Fact]
    public void DefinitionJoinToken_DuplicateArtifactUsesOneEvidenceClass()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        ResolvedAssemblyReference firstAssembly = Descriptor(image);
        ResolvedAssemblyReference secondAssembly = Descriptor(image);
        ResolvedAssemblyReference thirdAssembly = Descriptor(image);
        ResolvedAssemblyReference[] assemblies =
            [firstAssembly, secondAssembly, thirdAssembly];
        TypeResolutionRequest[] requests = assemblies
            .Select(assembly => TypeResolutionRequest.FromAssembly(
                assembly,
                AssemblyResolutionScope.Any,
                TypeName()))
            .ToArray();
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(_ => AssemblyBindingSelection.NotFound()),
            assemblies,
            requests);
        DefinitionJoinToken[] tokens = requests
            .Select(request => IssuedToken(
                catalog,
                Assert.IsType<TypeResolutionOutcome.Resolved>(
                    context.Resolve(request)).Definition.Key))
            .ToArray();

        Assert.All(tokens, token => Assert.Same(tokens[0], token));
        Assert.Equal(
            DefinitionJoinKind.IndeterminateDuplicateArtifact,
            tokens[0].Kind);
        DuplicateArtifactEvidence evidence =
            Assert.IsType<DuplicateArtifactEvidence>(tokens[0].Evidence);
        Assert.Equal(
            assemblies.Select(assembly => assembly.Registration).ToHashSet(),
            evidence.Candidates
                .Select(candidate => candidate.Assembly.Registration)
                .ToHashSet());

        var differentEvidence = new DefinitionJoinToken(
            tokens[0].Catalog,
            tokens[0].Generation,
            tokens[0].Value,
            tokens[0].Kind,
            new DuplicateArtifactEvidence([]));
        var differentKind = new DefinitionJoinToken(
            tokens[0].Catalog,
            tokens[0].Generation,
            tokens[0].Value,
            DefinitionJoinKind.Exact,
            evidence: null);
        Assert.Equal(tokens[0], differentEvidence);
        Assert.Equal(tokens[0].GetHashCode(), differentEvidence.GetHashCode());
        Assert.NotEqual(tokens[0], differentKind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefinitionJoinToken_RequiresBothIdentityAndMvid(
        bool sameIdentity)
    {
        Guid mvid = Guid.NewGuid();
        byte[] firstImage = BuildAssembly(
            "Definitions",
            definesType: true,
            moduleVersionId: mvid);
        byte[] secondImage = BuildAssembly(
            sameIdentity ? "Definitions" : "OtherDefinitions",
            definesType: true,
            moduleVersionId: sameIdentity ? Guid.NewGuid() : mvid);
        ResolvedAssemblyReference firstAssembly = Descriptor(firstImage);
        ResolvedAssemblyReference secondAssembly = Descriptor(secondImage);
        TypeResolutionRequest firstRequest =
            TypeResolutionRequest.FromAssembly(
                firstAssembly,
                AssemblyResolutionScope.Any,
                TypeName());
        TypeResolutionRequest secondRequest =
            TypeResolutionRequest.FromAssembly(
                secondAssembly,
                AssemblyResolutionScope.Any,
                TypeName());
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(_ => AssemblyBindingSelection.NotFound()),
            [firstAssembly, secondAssembly],
            [firstRequest, secondRequest]);
        ResolvedTypeDefinitionKey firstKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(firstRequest)).Definition.Key;
        ResolvedTypeDefinitionKey secondKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(secondRequest)).Definition.Key;

        DefinitionJoinToken first = IssuedToken(catalog, firstKey);
        DefinitionJoinToken second = IssuedToken(catalog, secondKey);

        Assert.Equal(DefinitionJoinKind.Exact, first.Kind);
        Assert.Equal(DefinitionJoinKind.Exact, second.Kind);
        Assert.NotEqual(first, second);
        Assert.IsType<DefinitionCorrespondence.Different>(
            catalog.Compare(firstKey, secondKey));
    }

    [Fact]
    public async Task DefinitionJoinToken_ConcurrentIssuanceReturnsOneToken()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(_ => AssemblyBindingSelection.NotFound()),
            [assembly],
            [request]);
        ResolvedTypeDefinitionKey key =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(request)).Definition.Key;

        DefinitionJoinToken[] tokens = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => Task.Run(
                    () => IssuedToken(catalog, key),
                    TestContext.Current.CancellationToken)));

        Assert.All(tokens, token => Assert.Same(tokens[0], token));
    }

    [Fact]
    public void DefinitionJoinToken_RejectsCrossCatalogAndStaleKeys()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.NotFound());
        using var firstCatalog = new TypeResolutionCatalog();
        using var secondCatalog = new TypeResolutionCatalog();
        using TypeResolutionContext first =
            firstCatalog.CreateContext(policy, [assembly], [request]);
        ResolvedTypeDefinitionKey oldKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                first.Resolve(request)).Definition.Key;
        DefinitionJoinToken oldToken =
            IssuedToken(firstCatalog, oldKey);

        var incomparable = Assert.IsType<
            DefinitionJoinTokenProjection.IncomparableCatalogs>(
                secondCatalog.ProjectDefinitionJoinToken(oldKey));
        Assert.Equal(secondCatalog.Id, incomparable.Catalog);
        Assert.Equal(firstCatalog.Id, incomparable.DefinitionCatalog);

        using TypeResolutionContext current =
            firstCatalog.CreateContext(policy, [assembly], [request]);
        ResolvedTypeDefinitionKey currentKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                current.Resolve(request)).Definition.Key;
        DefinitionJoinToken currentToken =
            IssuedToken(firstCatalog, currentKey);

        var stale = Assert.IsType<
            DefinitionJoinTokenProjection.StaleGeneration>(
                firstCatalog.ProjectDefinitionJoinToken(oldKey));
        Assert.Same(oldToken.Generation, stale.DefinitionGeneration);
        Assert.Same(currentToken.Generation, stale.CurrentGeneration);
        Assert.NotEqual(oldToken, currentToken);
        Assert.NotEqual(
            oldToken,
            new DefinitionJoinToken(
                oldToken.Catalog,
                new AssemblyCatalogGenerationId(),
                oldToken.Value,
                oldToken.Kind,
                oldToken.Evidence));
        Assert.NotEqual(
            oldToken,
            new DefinitionJoinToken(
                new AssemblyCatalogId(Guid.NewGuid()),
                oldToken.Generation,
                oldToken.Value,
                oldToken.Kind,
                oldToken.Evidence));
    }

    [Fact]
    public void DefinitionJoinToken_ReclassifiesCopiesInANewGeneration()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        ResolvedAssemblyReference firstAssembly = Descriptor(image);
        ResolvedAssemblyReference copiedAssembly = Descriptor(image);
        TypeResolutionRequest firstRequest =
            TypeResolutionRequest.FromAssembly(
                firstAssembly,
                AssemblyResolutionScope.Any,
                TypeName());
        TypeResolutionRequest copiedRequest =
            TypeResolutionRequest.FromAssembly(
                copiedAssembly,
                AssemblyResolutionScope.Any,
                TypeName());
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.NotFound());
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext firstContext =
            catalog.CreateContext(
                policy,
                [firstAssembly],
                [firstRequest]);
        ResolvedTypeDefinitionKey oldKey =
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                firstContext.Resolve(firstRequest)).Definition.Key;
        DefinitionJoinToken exact = IssuedToken(catalog, oldKey);
        Assert.Equal(DefinitionJoinKind.Exact, exact.Kind);

        using TypeResolutionContext copiedContext =
            catalog.CreateContext(
                policy,
                [firstAssembly, copiedAssembly],
                [firstRequest, copiedRequest]);
        DefinitionJoinToken firstCopy = IssuedToken(
            catalog,
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                copiedContext.Resolve(firstRequest)).Definition.Key);
        DefinitionJoinToken secondCopy = IssuedToken(
            catalog,
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                copiedContext.Resolve(copiedRequest)).Definition.Key);

        Assert.IsType<DefinitionJoinTokenProjection.StaleGeneration>(
            catalog.ProjectDefinitionJoinToken(oldKey));
        Assert.NotEqual(exact, firstCopy);
        Assert.Same(firstCopy, secondCopy);
        Assert.Equal(
            DefinitionJoinKind.IndeterminateDuplicateArtifact,
            firstCopy.Kind);
        DuplicateArtifactEvidence evidence =
            Assert.IsType<DuplicateArtifactEvidence>(firstCopy.Evidence);
        Assert.Equal(
            new[]
            {
                firstAssembly.Registration,
                copiedAssembly.Registration,
            }.ToHashSet(),
            evidence.Candidates
                .Select(candidate => candidate.Assembly.Registration)
                .ToHashSet());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnresolvedBindingKey_IsStableForOneCompleteBinding(
        bool policyUnavailable)
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        AssemblyBindingOrigin origin =
            AssemblyBindingOrigin.FromAssembly(owner);
        AssemblyReferenceIdentity target = Identity("Missing");
        TypeResolutionRequest firstRequest =
            TypeResolutionRequest.FromReference(
                target,
                origin,
                AssemblyResolutionScope.Any,
                TypeName());
        TypeResolutionRequest secondRequest =
            TypeResolutionRequest.FromReference(
                target,
                origin,
                AssemblyResolutionScope.Any,
                TypeName("Other"));
        AssemblyBindingFailure? failure = policyUnavailable
            ? new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable)
            : null;
        var policy = new RecordingPolicy(
            _ => failure is null
                ? AssemblyBindingSelection.NotFound()
                : AssemblyBindingSelection.CannotSelect(failure));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            [owner],
            [firstRequest, secondRequest]);

        TypeResolutionOutcome firstOutcome = context.Resolve(firstRequest);
        TypeResolutionOutcome secondOutcome = context.Resolve(secondRequest);
        if (failure is null)
        {
            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(firstOutcome);
            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(secondOutcome);
        }
        else
        {
            Assert.Same(
                failure,
                Assert.IsType<TypeResolutionOutcome.Unavailable>(firstOutcome)
                    .Failure);
            Assert.Same(
                failure,
                Assert.IsType<TypeResolutionOutcome.Unavailable>(secondOutcome)
                    .Failure);
        }

        UnresolvedBindingKey first = IssuedBindingKey(
            catalog,
            UnresolvedBinding(firstOutcome));
        UnresolvedBindingKey second = IssuedBindingKey(
            catalog,
            UnresolvedBinding(secondOutcome));
        var equivalent = new UnresolvedBindingKey(
            first.Catalog,
            first.Generation,
            first.Value);

        Assert.Same(first, second);
        Assert.Equal(first, equivalent);
        Assert.True(first == equivalent);
        Assert.False(first != equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Contains(
            equivalent,
            new HashSet<UnresolvedBindingKey> { first });
        Assert.Single(policy.Requests);
    }

    [Fact]
    public void UnresolvedBindingKey_PreservesEveryApplicableBindingCoordinate()
    {
        byte[] firstOwnerImage =
            BuildAssembly("FirstOwner", definesType: false);
        byte[] secondOwnerImage =
            BuildAssembly("SecondOwner", definesType: false);
        ResolvedAssemblyReference firstOwner = Descriptor(firstOwnerImage);
        ResolvedAssemblyReference secondOwner = Descriptor(secondOwnerImage);
        AssemblyBindingOrigin firstOrigin =
            AssemblyBindingOrigin.FromAssembly(firstOwner);
        AssemblyReferenceIdentity target = Identity("Target");
        TypeResolutionRequest baseline =
            TypeResolutionRequest.FromReference(
                target,
                firstOrigin,
                AssemblyResolutionScope.Any,
                TypeName());
        TypeResolutionRequest equivalent =
            TypeResolutionRequest.FromReference(
                new AssemblyReferenceIdentity(
                    "Target",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                AssemblyBindingOrigin.FromAssembly(firstOwner),
                AssemblyResolutionScope.Any,
                TypeName("Equivalent"));
        TypeResolutionRequest[] closeNegatives =
        [
            TypeResolutionRequest.FromReference(
                new AssemblyReferenceIdentity(
                    "Other",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                firstOrigin,
                AssemblyResolutionScope.Any,
                TypeName()),
            TypeResolutionRequest.FromReference(
                new AssemblyReferenceIdentity(
                    "Target",
                    new Version(2, 0, 0, 0),
                    null,
                    null),
                firstOrigin,
                AssemblyResolutionScope.Any,
                TypeName()),
            TypeResolutionRequest.FromReference(
                new AssemblyReferenceIdentity(
                    "Target",
                    new Version(1, 0, 0, 0),
                    "fr",
                    null),
                firstOrigin,
                AssemblyResolutionScope.Any,
                TypeName()),
            TypeResolutionRequest.FromReference(
                new AssemblyReferenceIdentity(
                    "Target",
                    new Version(1, 0, 0, 0),
                    null,
                    "0011223344556677"),
                firstOrigin,
                AssemblyResolutionScope.Any,
                TypeName()),
            TypeResolutionRequest.FromReference(
                target,
                AssemblyBindingOrigin.FromAssembly(secondOwner),
                AssemblyResolutionScope.Any,
                TypeName()),
            TypeResolutionRequest.FromReference(
                target,
                AssemblyBindingOrigin.Global(),
                AssemblyResolutionScope.Any,
                TypeName()),
            TypeResolutionRequest.FromReference(
                target,
                firstOrigin,
                AssemblyResolutionScope.Platform,
                TypeName()),
        ];
        TypeResolutionRequest[] requests =
            [baseline, equivalent, .. closeNegatives];
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(_ => AssemblyBindingSelection.NotFound()),
            [firstOwner, secondOwner],
            requests);

        UnresolvedBindingKey baselineKey = IssuedBindingKey(
            catalog,
            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
                context.Resolve(baseline)).Binding);
        UnresolvedBindingKey equivalentKey = IssuedBindingKey(
            catalog,
            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
                context.Resolve(equivalent)).Binding);
        UnresolvedBindingKey[] negativeKeys = closeNegatives
            .Select(request => IssuedBindingKey(
                catalog,
                Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
                    context.Resolve(request)).Binding))
            .ToArray();

        Assert.Same(baselineKey, equivalentKey);
        Assert.Equal(
            negativeKeys.Length,
            negativeKeys.Distinct().Count());
        Assert.DoesNotContain(baselineKey, negativeKeys);
    }

    [Fact]
    public void UnresolvedBindingKey_UsesTheTerminalForwardedBinding()
    {
        AssemblyReferenceIdentity target = Identity("Missing");
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: target);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        TypeResolutionRequest forwarded =
            TypeResolutionRequest.FromAssembly(
                facade,
                AssemblyResolutionScope.Any,
                TypeName());
        TypeResolutionRequest direct =
            TypeResolutionRequest.FromReference(
                target,
                AssemblyBindingOrigin.FromAssembly(facade),
                AssemblyResolutionScope.Any,
                TypeName("Other"));
        TypeResolutionRequest differentScope =
            TypeResolutionRequest.FromReference(
                target,
                AssemblyBindingOrigin.FromAssembly(facade),
                AssemblyResolutionScope.Platform,
                TypeName());
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(_ => AssemblyBindingSelection.NotFound()),
            [facade],
            [forwarded, direct, differentScope]);

        var forwardedOutcome =
            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
                context.Resolve(forwarded));
        var directOutcome =
            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
                context.Resolve(direct));
        var scopedOutcome =
            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
                context.Resolve(differentScope));

        Assert.Single(forwardedOutcome.Hops);
        Assert.Same(
            IssuedBindingKey(catalog, forwardedOutcome.Binding),
            IssuedBindingKey(catalog, directOutcome.Binding));
        Assert.NotEqual(
            IssuedBindingKey(catalog, forwardedOutcome.Binding),
            IssuedBindingKey(catalog, scopedOutcome.Binding));
    }

    [Fact]
    public async Task UnresolvedBindingKey_ConcurrentIssuanceReturnsOneKey()
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        TypeResolutionRequest request =
            TypeResolutionRequest.FromReference(
                Identity("Missing"),
                AssemblyBindingOrigin.FromAssembly(owner),
                AssemblyResolutionScope.Any,
                TypeName());
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(_ => AssemblyBindingSelection.NotFound()),
            [owner],
            [request]);
        UnresolvedBindingReference binding =
            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
                context.Resolve(request)).Binding;

        UnresolvedBindingKey[] keys = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => Task.Run(
                    () => IssuedBindingKey(catalog, binding),
                    TestContext.Current.CancellationToken)));

        Assert.All(keys, key => Assert.Same(keys[0], key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnresolvedBindingKey_RejectsCrossCatalogAndStaleReferences(
        bool policyUnavailable)
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        TypeResolutionRequest request =
            TypeResolutionRequest.FromReference(
                Identity("Missing"),
                AssemblyBindingOrigin.FromAssembly(owner),
                AssemblyResolutionScope.Any,
                TypeName());
        var failure = new AssemblyBindingFailure(
            AssemblyBindingFailureKind.CandidateUnavailable);
        var policy = new RecordingPolicy(
            _ => policyUnavailable
                ? AssemblyBindingSelection.CannotSelect(failure)
                : AssemblyBindingSelection.NotFound());
        using var firstCatalog = new TypeResolutionCatalog();
        using var secondCatalog = new TypeResolutionCatalog();
        using TypeResolutionContext first =
            firstCatalog.CreateContext(policy, [owner], [request]);
        TypeResolutionOutcome oldOutcome = first.Resolve(request);
        UnresolvedBindingReference oldBinding =
            UnresolvedBinding(oldOutcome);
        UnresolvedBindingKey oldKey =
            IssuedBindingKey(firstCatalog, oldBinding);

        var incomparable = Assert.IsType<
            UnresolvedBindingKeyProjection.IncomparableCatalogs>(
                secondCatalog.ProjectUnresolvedBindingKey(oldBinding));
        Assert.Equal(secondCatalog.Id, incomparable.Catalog);
        Assert.Equal(firstCatalog.Id, incomparable.BindingCatalog);

        using TypeResolutionContext current =
            firstCatalog.CreateContext(policy, [owner], [request]);
        TypeResolutionOutcome currentOutcome = current.Resolve(request);
        UnresolvedBindingReference currentBinding =
            UnresolvedBinding(currentOutcome);
        UnresolvedBindingKey currentKey =
            IssuedBindingKey(firstCatalog, currentBinding);
        var stale = Assert.IsType<
            UnresolvedBindingKeyProjection.StaleGeneration>(
                firstCatalog.ProjectUnresolvedBindingKey(oldBinding));

        Assert.NotSame(oldOutcome, currentOutcome);
        Assert.NotSame(oldBinding, currentBinding);
        Assert.Same(oldKey.Generation, stale.BindingGeneration);
        Assert.Same(currentKey.Generation, stale.CurrentGeneration);
        Assert.NotEqual(oldKey, currentKey);
        Assert.NotEqual(
            oldKey,
            new UnresolvedBindingKey(
                oldKey.Catalog,
                new AssemblyCatalogGenerationId(),
                oldKey.Value));
        Assert.NotEqual(
            oldKey,
            new UnresolvedBindingKey(
                new AssemblyCatalogId(Guid.NewGuid()),
                oldKey.Generation,
                oldKey.Value));
        Assert.Single(policy.Requests);
    }

    [Fact]
    public void DefinitionAddress_ResolvesOnlyAgainstMatchingModuleAndRow()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle expected = reader.TypeDefinitions.Single(
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "Type");
        var address = new MetadataTypeDefinitionAddress(
            ReadMvid(image),
            TypeDefinitionToken.FromHandle(reader, expected));

        Assert.True(address.TryResolve(reader, out TypeDefinitionHandle resolved));
        Assert.Equal(expected, resolved);

        Assert.False(
            (address with { ModuleVersionId = Guid.NewGuid() })
                .TryResolve(reader, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x01000001)]
    [InlineData(0x02000000)]
    [InlineData(0x02FFFFFF)]
    public void DefinitionAddress_RejectsInvalidToken(int token)
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var address = new MetadataTypeDefinitionAddress(
            ReadMvid(image),
            RawTypeDefinitionToken(token));

        Assert.False(address.TryResolve(reader, out _));
    }

    [Fact]
    public void Forwarder_UsesPolicyOnceAndPreservesHopEvidence()
    {
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: ReadIdentity(targetImage));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var policy = new RecordingPolicy(
            request => request.Target
                    is AssemblyBindingTarget.AssemblyReference reference
                && reference.Identity.Name == "Target"
                    ? AssemblyBindingSelection.Found(target)
                    : AssemblyBindingSelection.NotFound());
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [facade],
            [request]);
        var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(
            context.Resolve(request));

        Assert.Same(target, resolved.Definition.Assembly.Assembly);
        TypeForwardingHop hop = Assert.Single(resolved.Hops);
        Assert.Same(facade, hop.SourceAssembly.Assembly);
        Assert.Equal("Target", hop.TargetReference.Name);
        Assert.Equal(AssemblyResolutionScope.Any, hop.Scope);
        Assert.Single(policy.Requests);
        Assert.IsType<AssemblyBindingOutcome.Resolved>(
            context.Bind(policy.Requests[0]));
    }

    [Fact]
    public void NestedForwarder_ResolvesFullDeclarationChain()
    {
        ImmutableArray<string> segments = ["Outer", "Inner"];
        byte[] targetImage = BuildAssembly(
            "Target",
            definesType: true,
            typeSegments: segments);
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: ReadIdentity(targetImage),
            typeSegments: segments);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        MetadataTypeDefinitionName nestedName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("N", segments))
            .Name;
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            nestedName);

        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                _ => AssemblyBindingSelection.Found(target)),
            [facade],
            [request]);
        var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(
            context.Resolve(request));

        TypeForwardingHop hop = Assert.Single(resolved.Hops);
        Assert.Equal(2, hop.Declarations.Length);
        Assert.Equal(nestedName, resolved.Definition.Type);
        Assert.Same(target, resolved.Definition.Assembly.Assembly);
        using var stream = new MemoryStream(targetImage, writable: false);
        using var peReader = new PEReader(stream);
        Assert.True(
            resolved.Definition.Address.TryResolve(
                peReader.GetMetadataReader(),
                out TypeDefinitionHandle definition));
        Assert.Equal(
            "Inner",
            peReader.GetMetadataReader().GetString(
                peReader.GetMetadataReader()
                    .GetTypeDefinition(definition)
                    .Name));
    }

    [Fact]
    public void MissingForwarderTarget_IsUnboundBinding()
    {
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: Identity("Missing"));
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.NotFound());
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [facade],
            [request]);
        var unbound = Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
            context.Resolve(request));

        Assert.Equal("Missing",
            Assert.IsType<AssemblyBindingTarget.AssemblyReference>(
                unbound.Target).Identity.Name);
        Assert.Single(unbound.Hops);
    }

    [Fact]
    public void MissingDirectDeclaration_IsNotFound()
    {
        byte[] image = BuildAssembly("Definitions", definesType: false);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                _ => throw new InvalidOperationException("Must not be called.")),
            [assembly],
            [request]);

        Assert.Same(
            assembly,
            Assert.IsType<TypeResolutionOutcome.NotFound>(
                context.Resolve(request)).LastAssembly.Assembly);
    }

    [Fact]
    public void PolicyUnavailable_RemainsDistinctFromMissing()
    {
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: Identity("Target"));
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var failure = new AssemblyBindingFailure(
            AssemblyBindingFailureKind.IdentityPolicyRequired);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                _ => AssemblyBindingSelection.CannotSelect(failure)),
            [facade],
            [request]);

        Assert.Same(
            failure,
            Assert.IsType<TypeResolutionOutcome.Unavailable>(
                context.Resolve(request)).Failure);
    }

    [Fact]
    public void AmbiguousBinding_PreservesAllCandidates()
    {
        byte[] firstImage = BuildAssembly("Target", definesType: true);
        byte[] secondImage = BuildAssembly("Target", definesType: true);
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: ReadIdentity(firstImage));
        ResolvedAssemblyReference first = Descriptor(firstImage);
        ResolvedAssemblyReference second = Descriptor(secondImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Multiple([first, second]));
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [facade],
            [request]);
        var ambiguous = Assert.IsType<TypeResolutionOutcome.Ambiguous>(
            context.Resolve(request));
        var binding =
            Assert.IsType<TypeResolutionAmbiguity.AssemblyBinding>(
                ambiguous.Ambiguity);

        Assert.Equal(2, binding.Candidates.Length);
        Assert.Single(ambiguous.Hops);
    }

    [Fact]
    public void DuplicateForwardersToSameTarget_AreOneEvidenceBearingHop()
    {
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: ReadIdentity(targetImage),
            forwarderCount: 2);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                _ => AssemblyBindingSelection.Found(target)),
            [facade],
            [request]);
        TypeForwardingHop hop = Assert.Single(
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(request)).Hops);

        Assert.Equal(2, hop.Declarations.Length);
    }

    [Fact]
    public void CrossAssemblyCycle_IsRejectedWithBothHops()
    {
        byte[] firstImage = BuildAssembly(
            "First",
            definesType: false,
            forwardTarget: Identity("Second"));
        byte[] secondImage = BuildAssembly(
            "Second",
            definesType: false,
            forwardTarget: Identity("First"));
        ResolvedAssemblyReference first = Descriptor(firstImage);
        ResolvedAssemblyReference second = Descriptor(secondImage);
        var policy = new RecordingPolicy(
            request => Assert.IsType<
                    AssemblyBindingTarget.AssemblyReference>(request.Target)
                .Identity.Name switch
            {
                "First" => AssemblyBindingSelection.Found(first),
                "Second" => AssemblyBindingSelection.Found(second),
                _ => AssemblyBindingSelection.NotFound(),
            });
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            first,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [first],
            [request]);
        var rejected = Assert.IsType<TypeResolutionOutcome.Rejected>(
            context.Resolve(request));

        Assert.IsType<TypeResolutionFailure.ForwarderCycle>(
            rejected.Failure);
        Assert.Equal(2, rejected.Hops.Length);
        Assert.Equal(2, policy.Requests.Count);
    }

    [Fact]
    public void HopBudget_StopsBeforeNextPolicyCall()
    {
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: Identity("Target"));
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var policy = new RecordingPolicy(
            _ => throw new InvalidOperationException("Must not be called."));
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [facade],
            [request],
            new TypeResolutionContextOptions { MaxForwarderHops = 0 });
        var rejected = Assert.IsType<TypeResolutionOutcome.Rejected>(
            context.Resolve(request));

        Assert.Equal(
            0,
            Assert.IsType<TypeResolutionFailure.HopBudgetExceeded>(
                rejected.Failure).Budget);
        Assert.Single(rejected.Hops);
        Assert.Empty(policy.Requests);
    }

    [Fact]
    public void HopBudgetOutcome_IsReusableAcrossGenerations()
    {
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: Identity("Target"));
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var policy = new RecordingPolicy(
            _ => throw new InvalidOperationException("Must not be called."));
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());
        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions { MaxForwarderHops = 0 });

        using TypeResolutionContext first =
            catalog.CreateContext(policy, [facade], [request]);
        using TypeResolutionContext second =
            catalog.CreateContext(policy, [facade], [request]);

        Assert.IsType<TypeResolutionFailure.HopBudgetExceeded>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                first.Resolve(request)).Failure);
        Assert.IsType<TypeResolutionFailure.HopBudgetExceeded>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                second.Resolve(request)).Failure);
        Assert.Empty(policy.Requests);
    }

    [Fact]
    public void TypeRequestBudget_RejectsExcessManifestRequests()
    {
        byte[] image = BuildAssembly(
            "Definitions",
            definesType: true,
            definesOtherType: true);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest first = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        TypeResolutionRequest second = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName("Other"));

        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions
            {
                MaxTypeResolutionRequests = 1,
            });
        using TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(
                _ => AssemblyBindingSelection.NotFound()),
            [assembly],
            [first, second]);

        Assert.IsType<TypeResolutionOutcome.Resolved>(
            context.Resolve(first));
        Assert.Equal(
            1,
            Assert.IsType<TypeResolutionFailure.RequestBudgetExceeded>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    context.Resolve(second)).Failure).Budget);

        using TypeResolutionContext next =
            catalog.CreateContext(
                new RecordingPolicy(
                    _ => AssemblyBindingSelection.NotFound()),
                [assembly],
                [second]);
        Assert.IsType<TypeResolutionOutcome.Resolved>(
            next.Resolve(second));
    }

    [Fact]
    public void PlatformReference_TightensScopeAtForwarderHop()
    {
        const string token = "b03f5f7f11d50a3a";
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: Identity("Target", token));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var policy = new RecordingPolicy(request =>
        {
            Assert.Equal(AssemblyResolutionScope.Platform, request.Scope);
            return AssemblyBindingSelection.Found(target);
        });
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [facade],
            [request]);
        var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(
            context.Resolve(request));

        Assert.Equal(
            AssemblyResolutionScope.Platform,
            Assert.Single(resolved.Hops).Scope);
    }

    [Fact]
    public void PlatformScope_NeverLoosensAcrossUnsignedForwarder()
    {
        const string token = "b03f5f7f11d50a3a";
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        byte[] secondFacadeImage = BuildAssembly(
            "SecondFacade",
            definesType: false,
            forwardTarget: Identity("Target"));
        byte[] firstFacadeImage = BuildAssembly(
            "FirstFacade",
            definesType: false,
            forwardTarget: Identity("SecondFacade", token));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference secondFacade = Descriptor(secondFacadeImage);
        ResolvedAssemblyReference firstFacade = Descriptor(firstFacadeImage);
        var policy = new RecordingPolicy(request =>
        {
            Assert.Equal(AssemblyResolutionScope.Platform, request.Scope);
            string name = Assert.IsType<
                AssemblyBindingTarget.AssemblyReference>(request.Target)
                .Identity.Name;
            return name == "SecondFacade"
                ? AssemblyBindingSelection.Found(secondFacade)
                : AssemblyBindingSelection.Found(target);
        });
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            firstFacade,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [firstFacade],
            [request]);
        var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(
            context.Resolve(request));

        Assert.Equal(2, resolved.Hops.Length);
        Assert.All(
            resolved.Hops,
            hop => Assert.Equal(
                AssemblyResolutionScope.Platform,
                hop.Scope));
    }

    [Fact]
    public void CoreLibraryStart_UsesDistinctBindingTarget()
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        byte[] coreImage = BuildAssembly("Core", definesType: true);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        ResolvedAssemblyReference core = Descriptor(coreImage);
        var policy = new RecordingPolicy(request =>
            request.Target is AssemblyBindingTarget.IntrinsicCoreLibrary
                ? AssemblyBindingSelection.Found(core)
                : AssemblyBindingSelection.NotFound());
        TypeResolutionRequest request =
            TypeResolutionRequest.FromCoreLibrary(
                owner,
                AssemblyResolutionScope.Platform,
                TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [owner],
            [request]);

        Assert.IsType<TypeResolutionOutcome.Resolved>(
            context.Resolve(request));
        Assert.IsType<AssemblyBindingTarget.IntrinsicCoreLibrary>(
            Assert.Single(policy.Requests).Target);
    }

    [Fact]
    public void ModuleStart_IsExplicitlyUnsupported()
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        TypeResolutionRequest request = TypeResolutionRequest.FromModule(
            owner,
            "Other.netmodule",
            TypeName());
        var policy = new RecordingPolicy(
            _ => throw new InvalidOperationException("Must not be called."));

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [owner],
            [request]);
        var rejected = Assert.IsType<TypeResolutionOutcome.Rejected>(
            context.Resolve(request));

        Assert.Equal(
            "Other.netmodule",
            Assert.IsType<
                TypeResolutionFailure.UnsupportedModuleReference>(
                    rejected.Failure).ModuleName);
    }

    [Fact]
    public void ModuleExport_IsExplicitlyUnsupportedWithEvidence()
    {
        byte[] image = BuildModuleExport("Facade", "Part.netmodule");
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                _ => throw new InvalidOperationException("Must not be called.")),
            [assembly],
            [request]);
        var failure =
            Assert.IsType<TypeResolutionFailure.UnsupportedModuleExport>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    context.Resolve(request)).Failure);

        Assert.Equal("Part.netmodule", failure.Module.Name);
        Assert.True(failure.Module.ContainsMetadata);
        Assert.Equal([1, 2, 3], failure.Module.Hash);
    }

    [Fact]
    public void UnregisteredOrigin_IsRejectedWithoutPolicyCall()
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        TypeResolutionRequest request =
            TypeResolutionRequest.FromCoreLibrary(
                owner,
                AssemblyResolutionScope.Platform,
                TypeName());
        var policy = new RecordingPolicy(
            _ => throw new InvalidOperationException("Must not be called."));

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [],
            [request]);
        TypeResolutionOutcome first = context.Resolve(request);
        TypeResolutionOutcome second = context.Resolve(request);
        var rejected = Assert.IsType<TypeResolutionOutcome.Rejected>(first);

        Assert.Same(first, second);
        Assert.Same(
            owner.Registration,
            Assert.IsType<TypeResolutionFailure.UnregisteredAssembly>(
                rejected.Failure).Registration);
        Assert.Empty(policy.Requests);
    }

    [Fact]
    public void RegisteredButUnreadableOrigin_IsTerminalForTypesAndBindings()
    {
        ResolvedAssemblyReference owner = ResolvedAssemblyReference.Create(
            Identity("Owner"),
            path: null,
            openRead: () => throw new IOException("unreadable"),
            provenance: AssemblyResolutionProvenance.Local("test"));
        AssemblyBindingOrigin origin =
            AssemblyBindingOrigin.FromAssembly(owner);
        TypeResolutionRequest referenceRequest =
            TypeResolutionRequest.FromReference(
                Identity("Target"),
                origin,
                AssemblyResolutionScope.Any,
                TypeName());
        TypeResolutionRequest coreRequest =
            TypeResolutionRequest.FromCoreLibrary(
                owner,
                AssemblyResolutionScope.Platform,
                TypeName());
        var bindingRequest = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(Identity("Target")),
            origin,
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(
            _ => throw new InvalidOperationException("Must not be called."));
        using var catalog = new TypeResolutionCatalog();

        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [owner],
            bindingRequests: [bindingRequest],
            requests: [referenceRequest, coreRequest]);
        using TypeResolutionContext second = catalog.CreateContext(
            policy,
            roots: [owner],
            bindingRequests: [bindingRequest],
            requests: [referenceRequest, coreRequest]);

        Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                context.Resolve(referenceRequest)).Failure);
        Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                context.Resolve(coreRequest)).Failure);
        Assert.Equal(
            AssemblyBindingFailureKind.CandidateUnavailable,
            Assert.IsType<AssemblyBindingOutcome.Unavailable>(
                context.Bind(bindingRequest)).Failure.Kind);
        Assert.Equal(
            CandidateOpenFailureKind.Unreadable,
            Assert.IsType<AssemblyBindingOutcome.Unavailable>(
                context.Bind(bindingRequest))
                .Failure.CandidateFailureKind);
        Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                second.Resolve(referenceRequest)).Failure);
        Assert.IsType<AssemblyBindingOutcome.Unavailable>(
            second.Bind(bindingRequest));
        Assert.Empty(policy.Requests);
    }

    [Fact]
    public void BindingFailure_PreservesMalformedCandidateReason()
    {
        byte[] ownerImage = BuildAssembly(
            "Owner",
            definesType: false);
        byte[] targetImage = BuildAssembly(
            "Target",
            definesType: true);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        ResolvedAssemblyReference malformed =
            ResolvedAssemblyReference.Create(
                ReadIdentity(targetImage),
                path: null,
                () => new MemoryStream(
                    MetadataAdmissionCleanupTests
                        .BuildMalformedMetadataRoot(),
                    writable: false),
                AssemblyResolutionProvenance.Designated(
                    "malformed selected assembly"));
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                ReadIdentity(targetImage)),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Found(malformed));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context =
            catalog.CreateContext(
                policy,
                roots: [owner],
                bindingRequests: [binding],
                requests: []);

        AssemblyBindingFailure failure =
            Assert.IsType<AssemblyBindingOutcome.Unavailable>(
                context.Bind(binding)).Failure;

        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            failure.CandidateFailureKind);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            failure.MetadataRootReason);
    }

    [Fact]
    public void BindingFailure_PreservesMalformedOriginReason()
    {
        byte[] targetImage = BuildAssembly(
            "Target",
            definesType: true);
        ResolvedAssemblyReference owner =
            ResolvedAssemblyReference.Create(
                Identity("Owner"),
                path: null,
                () => new MemoryStream(
                    MetadataAdmissionCleanupTests
                        .BuildMalformedMetadataRoot(),
                    writable: false),
                AssemblyResolutionProvenance.Local("test"));
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                ReadIdentity(targetImage)),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(
            _ => throw new InvalidOperationException(
                "Policy must not run."));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context =
            catalog.CreateContext(
                policy,
                roots: [owner],
                bindingRequests: [binding],
                requests: []);

        AssemblyBindingFailure failure =
            Assert.IsType<AssemblyBindingOutcome.Unavailable>(
                context.Bind(binding)).Failure;

        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            failure.CandidateFailureKind);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            failure.MetadataRootReason);
        Assert.Empty(policy.Requests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MultipleBindingFailure_PrefersTypedAdmissionFailureRegardlessOfOrder(
        bool malformed,
        bool admissionFirst)
    {
        byte[] ownerImage = BuildAssembly(
            "Owner",
            definesType: false);
        byte[] targetImage = BuildAssembly(
            "Target",
            definesType: true);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        ResolvedAssemblyReference formatRejected =
            ResolvedAssemblyReference.Create(
                ReadIdentity(targetImage),
                path: null,
                () => new MemoryStream(
                    malformed
                        ? MetadataAdmissionCleanupTests
                            .BuildMalformedMetadataRoot()
                        : MetadataAdmissionCleanupTests
                            .BuildManagedWindowsMetadata(),
                    writable: false),
                AssemblyResolutionProvenance.Local("format rejected"));
        ResolvedAssemblyReference unreadable =
            ResolvedAssemblyReference.Create(
                ReadIdentity(targetImage),
                path: null,
                () => throw new IOException("unreadable"),
                AssemblyResolutionProvenance.Local("unreadable"));
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                ReadIdentity(targetImage)),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Multiple(
                admissionFirst
                    ? [formatRejected, unreadable]
                    : [unreadable, formatRejected]));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context =
            catalog.CreateContext(
                policy,
                roots: [owner],
                bindingRequests: [binding],
                requests: []);

        AssemblyBindingFailure failure =
            Assert.IsType<AssemblyBindingOutcome.Unavailable>(
                context.Bind(binding)).Failure;

        Assert.Equal(
            malformed
                ? CandidateOpenFailureKind.InvalidImage
                : CandidateOpenFailureKind.UnsupportedMetadataFormat,
            failure.CandidateFailureKind);
        Assert.Equal(
            malformed
                ? MetadataRootMalformedReason.InvalidSignature
                : null,
            failure.MetadataRootReason);
    }

    [Fact]
    public void ProjectionFailure_TracksEachGenerationsOriginState()
    {
        ResolvedAssemblyReference owner = ResolvedAssemblyReference.Create(
            Identity("Owner"),
            path: null,
            openRead: () => throw new IOException("unreadable"),
            provenance: AssemblyResolutionProvenance.Local("test"));
        TypeResolutionRequest request =
            TypeResolutionRequest.FromReference(
                Identity("Target"),
                AssemblyBindingOrigin.FromAssembly(owner),
                AssemblyResolutionScope.Any,
                TypeName());
        var policy = new RecordingPolicy(
            _ => throw new InvalidOperationException("Must not be called."));

        using (var absentFirst = new TypeResolutionCatalog())
        {
            using TypeResolutionContext absentContext =
                absentFirst.CreateContext(policy, [], [request]);
            using TypeResolutionContext failedContext =
                absentFirst.CreateContext(policy, [owner], [request]);

            Assert.IsType<TypeResolutionFailure.UnregisteredAssembly>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    absentContext.Resolve(request)).Failure);
            Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    failedContext.Resolve(request)).Failure);
        }

        using var failedFirst = new TypeResolutionCatalog();
        using TypeResolutionContext failed =
            failedFirst.CreateContext(policy, [owner], [request]);
        using TypeResolutionContext absent =
            failedFirst.CreateContext(policy, [], [request]);

        Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                failed.Resolve(request)).Failure);
        Assert.IsType<TypeResolutionFailure.UnregisteredAssembly>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                absent.Resolve(request)).Failure);
        Assert.Empty(policy.Requests);
    }

    [Fact]
    public void CandidateBudgetRejectedOrigin_IsNotExpansionRequired()
    {
        ResolvedAssemblyReference first =
            Descriptor(BuildAssembly("First", definesType: false));
        ResolvedAssemblyReference owner =
            Descriptor(BuildAssembly("Owner", definesType: false));
        AssemblyBindingOrigin origin =
            AssemblyBindingOrigin.FromAssembly(owner);
        TypeResolutionRequest request =
            TypeResolutionRequest.FromReference(
                Identity("Target"),
                origin,
                AssemblyResolutionScope.Any,
                TypeName());
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(Identity("Target")),
            origin,
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(
            _ => throw new InvalidOperationException("Must not be called."));
        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions { MaxCandidates = 1 });

        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [first, owner],
            bindingRequests: [binding],
            requests: [request]);

        Assert.Equal(
            1,
            Assert.IsType<TypeResolutionFailure.DiscoveryBudgetExceeded>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    context.Resolve(request)).Failure).Budget);
        AssemblyBindingFailure failure =
            Assert.IsType<AssemblyBindingOutcome.Unavailable>(
                context.Bind(binding)).Failure;
        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            failure.CandidateFailureKind);
        Assert.Empty(policy.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleBindingFailure_ResourceBudgetOutranksFormatFailure(
        bool budgetFirst)
    {
        byte[] ownerImage = BuildAssembly(
            "Owner",
            definesType: false);
        byte[] targetImage = BuildAssembly(
            "Target",
            definesType: true);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        ResolvedAssemblyReference malformed =
            ResolvedAssemblyReference.Create(
                ReadIdentity(targetImage),
                path: null,
                () => new MemoryStream(
                    MetadataAdmissionCleanupTests
                        .BuildMalformedMetadataRoot(),
                    writable: false),
                AssemblyResolutionProvenance.Local("malformed"));
        ResolvedAssemblyReference overBudget = Descriptor(targetImage);
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                ReadIdentity(targetImage)),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Multiple(
                budgetFirst
                    ? [overBudget, malformed]
                    : [malformed, overBudget]));
        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions { MaxCandidates = 1 });
        using TypeResolutionContext context =
            catalog.CreateContext(
                policy,
                roots: [malformed, owner, overBudget],
                bindingRequests: [binding],
                requests: []);

        AssemblyBindingFailure failure =
            Assert.IsType<AssemblyBindingOutcome.Unavailable>(
                context.Bind(binding)).Failure;

        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            failure.CandidateFailureKind);
        Assert.Null(failure.MetadataRootReason);
    }

    [Fact]
    public void RequestOutsideFrozenManifest_RequiresExpansion()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest included = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        TypeResolutionRequest absent = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName("Other"));
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.NotFound());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [assembly],
            [included]);
        var rejected = Assert.IsType<TypeResolutionOutcome.Rejected>(
            context.Resolve(absent));

        Assert.Same(
            absent,
            Assert.IsType<ResolutionPlanRequest.Type>(
                Assert.IsType<TypeResolutionFailure.PlanExpansionRequired>(
                    rejected.Failure).Request).Request);
    }

    [Fact]
    public void InvalidPolicyResult_IsTypedRejection()
    {
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: Identity("Target"));
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var policy = new RecordingPolicy(_ => null!);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [facade],
            [request]);
        var rejected = Assert.IsType<TypeResolutionOutcome.Rejected>(
            context.Resolve(request));

        Assert.Equal(
            AssemblyBindingFailureKind.InvalidPolicyResult,
            Assert.IsType<TypeResolutionFailure.InvalidBindingPolicy>(
                rejected.Failure).Failure.Kind);
        Assert.Equal(
            AssemblyBindingFailureKind.InvalidPolicyResult,
            Assert.IsType<AssemblyBindingOutcome.Rejected>(
                context.Bind(Assert.Single(policy.Requests))).Failure.Kind);
        Assert.Single(policy.Requests);
    }

    [Fact]
    public void SharedCatalog_ReusesCandidateAndDeclarationAcrossGenerations()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        int opens = 0;
        ResolvedAssemblyReference assembly = Descriptor(
            image,
            () => Interlocked.Increment(ref opens));
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.NotFound());
        using var catalog = new TypeResolutionCatalog();

        ResolvedAssemblyCandidate firstCandidate;
        using (TypeResolutionContext first =
            catalog.CreateContext(policy, [assembly], [request]))
        {
            firstCandidate = Assert.IsType<TypeResolutionOutcome.Resolved>(
                first.Resolve(request)).Definition.Assembly;
        }

        using TypeResolutionContext second =
            catalog.CreateContext(policy, [assembly], [request]);
        var secondResolved = Assert.IsType<TypeResolutionOutcome.Resolved>(
            second.Resolve(request));

        Assert.Same(firstCandidate, secondResolved.Definition.Assembly);
        Assert.Equal(catalog.Id, second.Catalog);
        Assert.Equal(2, opens);
    }

    [Fact]
    public void SharedCatalog_ReusesStablePolicyResolutionRecipe()
    {
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: ReadIdentity(targetImage));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Found(target));
        using var catalog = new TypeResolutionCatalog();

        using TypeResolutionContext first =
            catalog.CreateContext(policy, [facade], [request]);
        using TypeResolutionContext second =
            catalog.CreateContext(policy, [facade], [request]);
        var firstResolved = Assert.IsType<TypeResolutionOutcome.Resolved>(
            first.Resolve(request));
        var secondResolved = Assert.IsType<TypeResolutionOutcome.Resolved>(
            second.Resolve(request));

        Assert.NotEqual(first.Generation, second.Generation);
        Assert.NotSame(firstResolved, secondResolved);
        Assert.Same(
            firstResolved.Definition.Assembly,
            secondResolved.Definition.Assembly);
        Assert.Single(policy.Requests);
        Assert.IsType<AssemblyBindingOutcome.Resolved>(
            second.Bind(policy.Requests[0]));
    }

    [Fact]
    public void SharedCatalog_ReusesBindingManifestAndShadowsAcrossGenerations()
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        ResolvedAssemblyReference target = Descriptor(
            targetImage,
            provenance: AssemblyResolutionProvenance.Designated(
                "selected test assembly"));
        int shadowOpens = 0;
        ResolvedAssemblyReference shadow = Descriptor(
            targetImage,
            () => shadowOpens++,
            AssemblyResolutionProvenance.Platform(
                "test platform",
                frameworkVersion: null,
                "shadow test assembly"));
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(Identity("Target")),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Found(target, [shadow]));
        using var catalog = new TypeResolutionCatalog();

        using TypeResolutionContext first = catalog.CreateContext(
            policy,
            roots: [owner],
            bindingRequests: [binding],
            requests: []);
        using TypeResolutionContext second = catalog.CreateContext(
            policy,
            roots: [owner],
            bindingRequests: [binding],
            requests: []);

        var firstResolved = Assert.IsType<AssemblyBindingOutcome.Resolved>(
            first.Bind(binding));
        Assert.Same(
            shadow,
            Assert.Single(firstResolved.ShadowedAssemblies));
        Assert.Same(
            target,
            Assert.IsType<AssemblyBindingOutcome.Resolved>(
                second.Bind(binding)).Candidate.Assembly);
        Assert.Same(
            shadow,
            Assert.Single(
                Assert.IsType<AssemblyBindingOutcome.Resolved>(
                    second.Bind(binding)).ShadowedAssemblies));
        Assert.Equal(0, shadowOpens);
        Assert.Single(policy.Requests);
    }

    [Fact]
    public void IntrinsicBindingMiss_IsRejectedBeforeFreezing()
    {
        ResolvedAssemblyReference owner =
            Descriptor(BuildAssembly("Owner", definesType: false));
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Platform);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.NameNotOwned());
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [owner],
            bindingRequests: [binding],
            requests: []);

        var rejected = Assert.IsType<AssemblyBindingOutcome.Rejected>(
            context.Bind(binding));

        Assert.Equal(
            AssemblyBindingFailureKind.InvalidPolicyResult,
            rejected.Failure.Kind);
        Assert.Single(policy.Requests);
    }

    [Fact]
    public void BindingFailure_PreservesShadowsWithoutOpeningThem()
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        ResolvedAssemblyReference selected =
            ResolvedAssemblyReference.Create(
                ReadIdentity(targetImage),
                path: null,
                () => throw new IOException("unreadable"),
                AssemblyResolutionProvenance.Designated(
                    "unreadable selected assembly"));
        int shadowOpens = 0;
        ResolvedAssemblyReference shadow = Descriptor(
            targetImage,
            () => shadowOpens++,
            AssemblyResolutionProvenance.Platform(
                "test platform",
                frameworkVersion: null,
                "shadow test assembly"));
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(ReadIdentity(targetImage)),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Platform);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Found(selected, [shadow]));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [owner],
            bindingRequests: [binding],
            requests: []);

        var unavailable = Assert.IsType<AssemblyBindingOutcome.Unavailable>(
            context.Bind(binding));

        Assert.Same(
            shadow,
            Assert.Single(unavailable.ShadowedAssemblies));
        Assert.Equal(0, shadowOpens);
    }

    [Fact]
    public void SharedCatalog_ResolvesNewTypeThroughCachedBinding()
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        byte[] targetImage = BuildAssembly(
            "Target",
            definesType: true,
            definesOtherType: true);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        AssemblyBindingOrigin origin =
            AssemblyBindingOrigin.FromAssembly(owner);
        TypeResolutionRequest firstRequest =
            TypeResolutionRequest.FromReference(
                ReadIdentity(targetImage),
                origin,
                AssemblyResolutionScope.Any,
                TypeName());
        TypeResolutionRequest secondRequest =
            TypeResolutionRequest.FromReference(
                ReadIdentity(targetImage),
                origin,
                AssemblyResolutionScope.Any,
                TypeName("Other"));
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Found(target));
        using var catalog = new TypeResolutionCatalog();

        using TypeResolutionContext first =
            catalog.CreateContext(policy, [owner], [firstRequest]);
        using TypeResolutionContext second =
            catalog.CreateContext(policy, [owner], [secondRequest]);

        Assert.IsType<TypeResolutionOutcome.Resolved>(
            first.Resolve(firstRequest));
        Assert.IsType<TypeResolutionOutcome.Resolved>(
            second.Resolve(secondRequest));
        Assert.Single(policy.Requests);
    }

    [Fact]
    public void BindingManifest_FreezesSourceRelativeBinding()
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        AssemblyReferenceIdentity reference = Identity("Target");
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(reference),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Found(target));
        using var catalog = new TypeResolutionCatalog();

        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [owner],
            bindingRequests: [binding],
            requests: []);

        Assert.Same(
            target,
            Assert.IsType<AssemblyBindingOutcome.Resolved>(
                context.Bind(binding)).Candidate.Assembly);
        Assert.Single(policy.Requests);
    }

    [Fact]
    public void BindingManifest_ExpandsForwarderAdjacency()
    {
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: ReadIdentity(targetImage));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var rootBinding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(Identity("Facade")),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(request =>
        {
            string name = Assert.IsType<
                AssemblyBindingTarget.AssemblyReference>(request.Target)
                .Identity.Name;
            return name == "Facade"
                ? AssemblyBindingSelection.Found(facade)
                : AssemblyBindingSelection.Found(target);
        });
        using var catalog = new TypeResolutionCatalog();

        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [rootBinding],
            requests: []);
        var forwardedBinding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(ReadIdentity(targetImage)),
            AssemblyBindingOrigin.FromAssembly(facade),
            AssemblyResolutionScope.Any);

        Assert.IsType<AssemblyBindingOutcome.Resolved>(
            context.Bind(rootBinding));
        Assert.Same(
            target,
            Assert.IsType<AssemblyBindingOutcome.Resolved>(
                context.Bind(forwardedBinding)).Candidate.Assembly);
        Assert.Equal(2, policy.Requests.Count);
    }

    [Fact]
    public void BindingManifest_DenseGraphUsesBoundedCallStack()
    {
        const int candidateCount = 256;
        string[] names = Enumerable.Range(0, candidateCount)
            .Select(static i => $"A{i}")
            .ToArray();
        var candidates = names.ToDictionary(
            static name => name,
            name => Descriptor(BuildForwarderFanout(name, names)),
            StringComparer.Ordinal);
        var policy = new RecordingPolicy(request =>
        {
            string name = Assert.IsType<
                AssemblyBindingTarget.AssemblyReference>(request.Target)
                .Identity.Name;
            return AssemblyBindingSelection.Found(candidates[name]);
        });
        var root = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(Identity(names[0])),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        using var catalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions
            {
                MaxCandidates = candidateCount,
            });

        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [root],
            requests: []);

        Assert.IsType<AssemblyBindingOutcome.Resolved>(
            context.Bind(root));
        Assert.Equal(
            1 + (candidateCount * candidateCount),
            policy.Requests.Count);
    }

    [Fact]
    public void SourceRelativeBindings_HaveIndependentCacheDomains()
    {
        byte[] firstOwnerImage = BuildAssembly("FirstOwner", definesType: false);
        byte[] secondOwnerImage =
            BuildAssembly("SecondOwner", definesType: false);
        byte[] firstTargetImage = BuildAssembly("FirstTarget", definesType: true);
        byte[] secondTargetImage =
            BuildAssembly("SecondTarget", definesType: true);
        ResolvedAssemblyReference firstOwner = Descriptor(firstOwnerImage);
        ResolvedAssemblyReference secondOwner = Descriptor(secondOwnerImage);
        ResolvedAssemblyReference firstTarget = Descriptor(firstTargetImage);
        ResolvedAssemblyReference secondTarget = Descriptor(secondTargetImage);
        AssemblyReferenceIdentity sharedReference = Identity("Shared");
        TypeResolutionRequest firstRequest = TypeResolutionRequest.FromReference(
            sharedReference,
            AssemblyBindingOrigin.FromAssembly(firstOwner),
            AssemblyResolutionScope.Any,
            TypeName());
        TypeResolutionRequest secondRequest =
            TypeResolutionRequest.FromReference(
                sharedReference,
                AssemblyBindingOrigin.FromAssembly(secondOwner),
                AssemblyResolutionScope.Any,
                TypeName());
        var policy = new RecordingPolicy(request =>
            Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(
                    request.Origin).Registration == firstOwner.Registration
                ? AssemblyBindingSelection.Found(firstTarget)
                : AssemblyBindingSelection.Found(secondTarget));

        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [firstOwner, secondOwner],
            [firstRequest, secondRequest]);

        Assert.Same(
            firstTarget,
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(firstRequest))
                .Definition.Assembly.Assembly);
        Assert.Same(
            secondTarget,
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(secondRequest))
                .Definition.Assembly.Assembly);
        Assert.Equal(2, policy.Requests.Count);
    }

    [Fact]
    public void AmbiguousBinding_PreservesReadableCandidatesPastUnreadableOne()
    {
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: Identity("Target"));
        byte[] firstImage = BuildAssembly("First", definesType: true);
        byte[] secondImage = BuildAssembly("Second", definesType: true);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        ResolvedAssemblyReference unreadable = ResolvedAssemblyReference.Create(
            Identity("Unreadable"),
            path: null,
            openRead: () => throw new IOException("unreadable"),
            provenance: AssemblyResolutionProvenance.Local("test"));
        ResolvedAssemblyReference first = Descriptor(firstImage);
        ResolvedAssemblyReference second = Descriptor(secondImage);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Multiple(
                [unreadable, first, second]));
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());

        TypeResolutionRequest unreadableRequest =
            TypeResolutionRequest.FromAssembly(
                unreadable,
                AssemblyResolutionScope.Any,
                TypeName());
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext firstContext =
            catalog.CreateContext(policy, [facade], [request]);
        using TypeResolutionContext secondContext =
            catalog.CreateContext(policy, [facade], [request]);
        var ambiguous = Assert.IsType<TypeResolutionOutcome.Ambiguous>(
            secondContext.Resolve(request));
        var binding =
            Assert.IsType<TypeResolutionAmbiguity.AssemblyBinding>(
                ambiguous.Ambiguity);

        Assert.Equal(2, binding.Candidates.Length);
        Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                firstContext.Resolve(unreadableRequest)).Failure);
        Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                secondContext.Resolve(unreadableRequest)).Failure);
        Assert.Single(policy.Requests);
    }

    [Fact]
    public void CandidateBudgetFailure_IsDistinctFromSessionBudgetFailure()
    {
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: ReadIdentity(targetImage));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Found(target));
        TypeResolutionRequest forwardedRequest =
            TypeResolutionRequest.FromAssembly(
                facade,
                AssemblyResolutionScope.Any,
                TypeName());

        using var candidateCatalog = new TypeResolutionCatalog(
            new TypeResolutionContextOptions { MaxCandidates = 1 });
        using TypeResolutionContext candidateLimited =
            candidateCatalog.CreateContext(
                policy,
                [facade],
                [forwardedRequest]);
        Assert.Equal(
            1,
            Assert.IsType<TypeResolutionFailure.DiscoveryBudgetExceeded>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    candidateLimited.Resolve(forwardedRequest)).Failure).Budget);
        TypeResolutionRequest unmanifestedRequest =
            TypeResolutionRequest.FromAssembly(
                target,
                AssemblyResolutionScope.Any,
                TypeName("Other"));
        Assert.Equal(
            1,
            Assert.IsType<TypeResolutionFailure.DiscoveryBudgetExceeded>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    candidateLimited.Resolve(unmanifestedRequest)).Failure)
                .Budget);
        using TypeResolutionContext secondCandidateLimited =
            candidateCatalog.CreateContext(
                policy,
                [facade],
                [forwardedRequest]);
        Assert.Equal(
            1,
            Assert.IsType<TypeResolutionFailure.DiscoveryBudgetExceeded>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    secondCandidateLimited.Resolve(unmanifestedRequest))
                    .Failure)
                .Budget);

        TypeResolutionRequest directRequest =
            TypeResolutionRequest.FromAssembly(
                target,
                AssemblyResolutionScope.Any,
                TypeName());
        using TypeResolutionContext imageLimited =
            TypeResolutionContext.Create(
                new RecordingPolicy(
                    _ => AssemblyBindingSelection.NotFound()),
                [target],
                [directRequest],
                new TypeResolutionContextOptions
                {
                    MaxRetainedImageBytes = 0,
                });
        var openFailure =
            Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
                Assert.IsType<TypeResolutionOutcome.Rejected>(
                    imageLimited.Resolve(directRequest)).Failure);

        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            openFailure.Failure.Kind);
    }

    [Fact]
    public void BindWithUnregisteredOrigin_RequiresExpansion()
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Platform);
        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                _ => throw new InvalidOperationException("Must not be called.")),
            [],
            []);

        Assert.Same(
            request,
            Assert.IsType<AssemblyBindingOutcome.ExpansionRequired>(
                context.Bind(request)).Request);
    }

    [Fact]
    public void PolicyVersionChange_DuringDiscoveryRejectsFreeze()
    {
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: Identity("Target"));
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());
        var policy = new VersionChangingPolicy();

        Assert.Throws<InvalidOperationException>(
            () => TypeResolutionContext.Create(
                policy,
                [facade],
                [request]));
        Assert.Equal(1, policy.CallCount);
    }

    [Theory]
    [InlineData(AssemblyBindingMissDisposition.Undifferentiated)]
    [InlineData(AssemblyBindingMissDisposition.NoNameOwner)]
    [InlineData(AssemblyBindingMissDisposition.NameOwnedNoMatch)]
    public void AssemblyBindingMissDisposition_SurvivesInterningAndFrozenReuse(
        AssemblyBindingMissDisposition disposition)
    {
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(Identity("Missing")),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(_ => Missing(disposition));
        using var catalog = new TypeResolutionCatalog();

        using TypeResolutionContext first = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [request],
            requests: []);
        using TypeResolutionContext second = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [request],
            requests: []);

        Assert.Equal(
            disposition,
            Assert.IsType<AssemblyBindingOutcome.Missing>(
                first.Bind(request)).Disposition);
        Assert.Equal(
            disposition,
            Assert.IsType<AssemblyBindingOutcome.Missing>(
                second.Bind(request)).Disposition);
        Assert.Single(policy.Requests);
    }

    [Fact]
    public void AssemblyBindingMissDisposition_OriginScopesRemainDistinct()
    {
        ResolvedAssemblyReference owner =
            Descriptor(BuildAssembly("Owner", definesType: false));
        var global = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(Identity("Missing")),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        var requesting = new AssemblyBindingRequest(
            global.Target,
            AssemblyBindingOrigin.FromAssembly(owner),
            global.Scope);
        var policy = new RecordingPolicy(
            request => request.Origin
                    is AssemblyBindingOrigin.GlobalOrigin
                ? AssemblyBindingSelection.NameNotOwned()
                : AssemblyBindingSelection.NameOwnedButNoMatch());
        using var catalog = new TypeResolutionCatalog();

        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [owner],
            bindingRequests: [global, requesting],
            requests: []);

        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            Assert.IsType<AssemblyBindingOutcome.Missing>(
                context.Bind(global)).Disposition);
        Assert.Equal(
            AssemblyBindingMissDisposition.NameOwnedNoMatch,
            Assert.IsType<AssemblyBindingOutcome.Missing>(
                context.Bind(requesting)).Disposition);
        Assert.Equal(2, policy.Requests.Count);
    }

    [Fact]
    public void AssemblyBindingMissDisposition_ObservedVersionChangeRefreshesDisposition()
    {
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(Identity("Missing")),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        var policy = new VersionedDispositionPolicy(
            AssemblyBindingMissDisposition.NoNameOwner);
        using var catalog = new TypeResolutionCatalog();

        using TypeResolutionContext first = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [request],
            requests: []);
        policy.Advance(
            AssemblyBindingMissDisposition.NameOwnedNoMatch);
        using TypeResolutionContext second = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [request],
            requests: []);

        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            Assert.IsType<AssemblyBindingOutcome.Missing>(
                first.Bind(request)).Disposition);
        Assert.Equal(
            AssemblyBindingMissDisposition.NameOwnedNoMatch,
            Assert.IsType<AssemblyBindingOutcome.Missing>(
                second.Bind(request)).Disposition);
        Assert.Equal(2, policy.CallCount);
    }

    [Fact]
    public void ResolutionKeysAndGenerations_CannotBePubliclyForged()
    {
        Assert.Empty(
            typeof(AssemblyCatalogGenerationId)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(ResolvedTypeDefinitionKey)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(DefinitionJoinToken)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(DefinitionJoinToken)
                .GetFields(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(
            [nameof(DefinitionJoinToken.Evidence), nameof(DefinitionJoinToken.Kind)],
            typeof(DefinitionJoinToken)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        ConstructorInfo projectionConstructor = Assert.Single(
            typeof(DefinitionJoinTokenProjection)
                .GetConstructors(
                    BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.True(projectionConstructor.IsFamilyAndAssembly);
        Assert.All(
            typeof(DefinitionJoinTokenProjection).GetNestedTypes(),
            type => Assert.Empty(
                type.GetConstructors(
                    BindingFlags.Public | BindingFlags.Instance)));
        Assert.Empty(
            typeof(UnresolvedBindingReference)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(UnresolvedBindingReference)
                .GetFields(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(UnresolvedBindingReference)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(UnresolvedBindingKey)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(UnresolvedBindingKey)
                .GetFields(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            typeof(UnresolvedBindingKey)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance));
        ConstructorInfo bindingProjectionConstructor = Assert.Single(
            typeof(UnresolvedBindingKeyProjection)
                .GetConstructors(
                    BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.True(bindingProjectionConstructor.IsFamilyAndAssembly);
        Assert.All(
            typeof(UnresolvedBindingKeyProjection).GetNestedTypes(),
            type => Assert.Empty(
                type.GetConstructors(
                    BindingFlags.Public | BindingFlags.Instance)));
        Assert.Equal(
            [
                typeof(TypeResolutionOutcome.Unavailable),
                typeof(TypeResolutionOutcome.UnboundBinding),
            ],
            typeof(TypeResolutionOutcome).Assembly
                .GetExportedTypes()
                .SelectMany(type => type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance))
                .Where(property =>
                    property.PropertyType
                        == typeof(UnresolvedBindingReference))
                .Select(property => property.DeclaringType)
                .OrderBy(type => type!.FullName, StringComparer.Ordinal));
        Assert.Empty(
            typeof(ResolvedAssemblyCandidate)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    static DefinitionJoinToken IssuedToken(
        TypeResolutionCatalog catalog,
        ResolvedTypeDefinitionKey definition) =>
        Assert.IsType<DefinitionJoinTokenProjection.Issued>(
            catalog.ProjectDefinitionJoinToken(definition)).Token;

    static UnresolvedBindingReference UnresolvedBinding(
        TypeResolutionOutcome outcome) =>
        outcome switch
        {
            TypeResolutionOutcome.UnboundBinding unbound => unbound.Binding,
            TypeResolutionOutcome.Unavailable unavailable =>
                unavailable.Binding,
            _ => throw new Xunit.Sdk.XunitException(
                "The outcome does not carry an unresolved binding."),
        };

    static UnresolvedBindingKey IssuedBindingKey(
        TypeResolutionCatalog catalog,
        UnresolvedBindingReference binding) =>
        Assert.IsType<UnresolvedBindingKeyProjection.Issued>(
            catalog.ProjectUnresolvedBindingKey(binding)).Key;

    static AssemblyBindingSelection Missing(
        AssemblyBindingMissDisposition disposition) =>
        disposition switch
        {
            AssemblyBindingMissDisposition.Undifferentiated =>
                AssemblyBindingSelection.NotFound(),
            AssemblyBindingMissDisposition.NoNameOwner =>
                AssemblyBindingSelection.NameNotOwned(),
            AssemblyBindingMissDisposition.NameOwnedNoMatch =>
                AssemblyBindingSelection.NameOwnedButNoMatch(),
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };

    [Fact]
    public async Task FrozenContext_IsConcurrentAndDoesNotReinvokePolicy()
    {
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: ReadIdentity(targetImage));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Found(target));
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());
        using TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [facade],
            [request]);

        TypeResolutionOutcome[] outcomes = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => Task.Run(
                    () => context.Resolve(request),
                    TestContext.Current.CancellationToken)));

        Assert.All(outcomes, outcome => Assert.Same(outcomes[0], outcome));
        Assert.Single(policy.Requests);
    }

    [Fact]
    public void DisposedContext_RejectsResolutionAndBinding()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.NotFound());
        TypeResolutionContext context = TypeResolutionContext.Create(
            policy,
            [assembly],
            [request]);
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.Resolve(request));
        Assert.Throws<ObjectDisposedException>(
            () => context.Bind(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(Identity("Other")),
                    AssemblyBindingOrigin.Global(),
                    AssemblyResolutionScope.Any)));
    }

    [Fact]
    public void DisposedCatalog_InvalidatesItsFrozenContexts()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.NotFound());
        var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context =
            catalog.CreateContext(policy, [assembly], [request]);

        catalog.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.Resolve(request));
        Assert.Throws<ObjectDisposedException>(
            () => catalog.CreateContext(policy, [assembly], [request]));
    }

    [Fact]
    public void DisposedCatalog_ReleasesLatestCandidateImages()
    {
        (TypeResolutionCatalog catalog, WeakReference image) =
            CreateDisposedCatalogWithWeakImage();

        for (int attempt = 0; attempt < 3 && image.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(image.IsAlive);
        GC.KeepAlive(catalog);
    }

    [Fact]
    public void DisposedContext_ReleasesCandidateImages()
    {
        (TypeResolutionContext context, WeakReference image) =
            CreateDisposedContextWithWeakImage();

        for (int attempt = 0; attempt < 3 && image.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(image.IsAlive);
        GC.KeepAlive(context);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (TypeResolutionCatalog Catalog, WeakReference Image)
        CreateDisposedCatalogWithWeakImage()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        var weakImage = new WeakReference(image);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        var catalog = new TypeResolutionCatalog();
        using (TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(
                _ => AssemblyBindingSelection.NotFound()),
            [assembly],
            [request]))
        {
            Assert.IsType<TypeResolutionOutcome.Resolved>(
                context.Resolve(request));
        }
        catalog.Dispose();
        return (catalog, weakImage);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (TypeResolutionContext Context, WeakReference Image)
        CreateDisposedContextWithWeakImage()
    {
        byte[] image = BuildAssembly("Definitions", definesType: true);
        var weakImage = new WeakReference(image);
        ResolvedAssemblyReference assembly = Descriptor(image);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            assembly,
            AssemblyResolutionScope.Any,
            TypeName());
        var catalog = new TypeResolutionCatalog();
        TypeResolutionContext context = catalog.CreateContext(
            new RecordingPolicy(
                _ => AssemblyBindingSelection.NotFound()),
            [assembly],
            [request]);
        Assert.IsType<TypeResolutionOutcome.Resolved>(
            context.Resolve(request));
        context.Dispose();
        catalog.Dispose();
        return (context, weakImage);
    }

    [Fact]
    public void CanceledGeneration_DoesNotPoisonCatalog()
    {
        var policy = new RecordingPolicy(
            _ => throw new InvalidOperationException("Must not be called."));
        using var catalog = new TypeResolutionCatalog();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => catalog.CreateContextWithCancellation(
                policy,
                roots: [],
                bindingRequests: [],
                requests: [],
                cancellation.Token));

        using TypeResolutionContext context =
            catalog.CreateContext(policy, [], []);
        Assert.Equal(catalog.Id, context.Catalog);
    }

    [Fact]
    public void PartiallyCanceledGeneration_DoesNotPromotePolicyCaches()
    {
        byte[] facadeImage = BuildAssembly(
            "Facade",
            definesType: false,
            forwardTarget: Identity("Target"));
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        TypeResolutionRequest request = TypeResolutionRequest.FromAssembly(
            facade,
            AssemblyResolutionScope.Any,
            TypeName());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        int calls = 0;
        var policy = new RecordingPolicy(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                cancellation.Cancel();
            return AssemblyBindingSelection.NotFound();
        });
        using var catalog = new TypeResolutionCatalog();

        Assert.ThrowsAny<OperationCanceledException>(
            () => catalog.CreateContextWithCancellation(
                policy,
                roots: [facade],
                bindingRequests: [],
                requests: [request],
                cancellation.Token));

        using TypeResolutionContext context =
            catalog.CreateContext(policy, [facade], [request]);
        Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
            context.Resolve(request));
        Assert.Equal(2, calls);
    }

    static MetadataTypeDefinitionName TypeName(string name = "Type") =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", [name])).Name;

    static AssemblyReferenceIdentity Identity(
        string name,
        string? publicKeyToken = null) =>
        new(name, new Version(1, 0, 0, 0), null, publicKeyToken);

    static ResolvedAssemblyReference Descriptor(
        byte[] image,
        Action? opened = null,
        AssemblyResolutionProvenance? provenance = null) =>
        ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () =>
            {
                opened?.Invoke();
                return new MemoryStream(image, writable: false);
            },
            provenance: provenance
                ?? AssemblyResolutionProvenance.Local("test"));

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    static Guid ReadMvid(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    static TypeDefinitionToken RawTypeDefinitionToken(int value) =>
        (TypeDefinitionToken)typeof(TypeDefinitionToken)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single()
            .Invoke([value]);

    static byte[] BuildAssembly(
        string assemblyName,
        bool definesType,
        AssemblyReferenceIdentity? forwardTarget = null,
        int forwarderCount = 1,
        bool definesOtherType = false,
        Guid? moduleVersionId = null,
        ImmutableArray<string> typeSegments = default,
        bool omitForwarderFlag = false)
    {
        ImmutableArray<string> segments = typeSegments.IsDefault
            ? ["Type"]
            : typeSegments;
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(moduleVersionId ?? Guid.NewGuid()),
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
            TypeDefinitionHandle enclosing = default;
            for (int i = 0; i < segments.Length; i++)
            {
                TypeDefinitionHandle definition =
                    metadata.AddTypeDefinition(
                        i == 0
                            ? TypeAttributes.Public
                            : TypeAttributes.NestedPublic,
                        i == 0
                            ? metadata.GetOrAddString("N")
                            : default,
                        metadata.GetOrAddString(segments[i]),
                        baseType: default,
                        fieldList: MetadataTokens.FieldDefinitionHandle(1),
                        methodList: MetadataTokens.MethodDefinitionHandle(1));
                if (!enclosing.IsNil)
                    metadata.AddNestedType(definition, enclosing);
                enclosing = definition;
            }
        }
        if (definesOtherType)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Other"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        if (forwardTarget is not null)
        {
            BlobHandle token = forwardTarget.PublicKeyToken is null
                ? default
                : metadata.GetOrAddBlob(
                    Convert.FromHexString(forwardTarget.PublicKeyToken));
            AssemblyReferenceHandle target =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString(forwardTarget.Name),
                    forwardTarget.Version ?? new Version(1, 0, 0, 0),
                    culture: default,
                    publicKeyOrToken: token,
                    flags: default,
                    hashValue: default);
            for (int i = 0; i < forwarderCount; i++)
            {
                EntityHandle implementation = target;
                for (int segment = 0;
                    segment < segments.Length;
                    segment++)
                {
                    implementation = metadata.AddExportedType(
                        segment == 0
                            ? TypeAttributes.Public
                                | (omitForwarderFlag ? 0 : Forwarder)
                            : TypeAttributes.NestedPublic,
                        segment == 0
                            ? metadata.GetOrAddString("N")
                            : default,
                        metadata.GetOrAddString(segments[segment]),
                        implementation,
                        typeDefinitionId: 0);
                }
            }
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildForwarderFanout(
        string assemblyName,
        IEnumerable<string> targets)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
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

        int index = 0;
        foreach (string targetName in targets)
        {
            AssemblyReferenceHandle target =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString(targetName),
                    new Version(1, 0, 0, 0),
                    culture: default,
                    publicKeyOrToken: default,
                    flags: default,
                    hashValue: default);
            metadata.AddExportedType(
                TypeAttributes.Public | Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString($"T{index++}"),
                target,
                typeDefinitionId: 0);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildModuleExport(
        string assemblyName,
        string moduleName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
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
        AssemblyFileHandle module = metadata.AddAssemblyFile(
            metadata.GetOrAddString(moduleName),
            metadata.GetOrAddBlob(new byte[] { 1, 2, 3 }),
            containsMetadata: true);
        metadata.AddExportedType(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"),
            module,
            typeDefinitionId: 1);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    sealed class RecordingPolicy(
        Func<AssemblyBindingRequest, AssemblyBindingSelection> select)
        : IAssemblyBindingPolicy
    {
        readonly object _gate = new();

        public AssemblyBindingPolicyVersion Version { get; } = new();
        public List<AssemblyBindingRequest> Requests { get; } = [];

        public AssemblyBindingSelection Select(AssemblyBindingRequest request)
        {
            lock (_gate)
                Requests.Add(request);
            return select(request);
        }
    }

    sealed class VersionChangingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; private set; } =
            new();
        public int CallCount { get; private set; }

        public AssemblyBindingSelection Select(AssemblyBindingRequest request)
        {
            CallCount++;
            Version = new AssemblyBindingPolicyVersion();
            return AssemblyBindingSelection.NotFound();
        }
    }

    sealed class VersionedDispositionPolicy(
        AssemblyBindingMissDisposition disposition)
        : IAssemblyBindingPolicy
    {
        AssemblyBindingMissDisposition _disposition = disposition;

        public AssemblyBindingPolicyVersion Version { get; private set; } =
            new();
        public int CallCount { get; private set; }

        public AssemblyBindingSelection Select(AssemblyBindingRequest request)
        {
            CallCount++;
            return Missing(_disposition);
        }

        public void Advance(AssemblyBindingMissDisposition next)
        {
            _disposition = next;
            Version = new AssemblyBindingPolicyVersion();
        }
    }
}
