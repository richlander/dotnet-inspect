using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class TypeResolutionContextTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

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
        Assert.IsType<TypeResolutionFailure.CandidateOpenFailed>(
            Assert.IsType<TypeResolutionOutcome.Rejected>(
                second.Resolve(referenceRequest)).Failure);
        Assert.IsType<AssemblyBindingOutcome.Unavailable>(
            second.Bind(bindingRequest));
        Assert.Empty(policy.Requests);
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
        Assert.IsType<AssemblyBindingOutcome.Unavailable>(
            context.Bind(binding));
        Assert.Empty(policy.Requests);
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
    public void SharedCatalog_ReusesBindingManifestAcrossGenerations()
    {
        byte[] ownerImage = BuildAssembly("Owner", definesType: false);
        byte[] targetImage = BuildAssembly("Target", definesType: true);
        ResolvedAssemblyReference owner = Descriptor(ownerImage);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(Identity("Target")),
            AssemblyBindingOrigin.FromAssembly(owner),
            AssemblyResolutionScope.Any);
        var policy = new RecordingPolicy(
            _ => AssemblyBindingSelection.Found(target));
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

        Assert.IsType<AssemblyBindingOutcome.Resolved>(
            first.Bind(binding));
        Assert.Same(
            target,
            Assert.IsType<AssemblyBindingOutcome.Resolved>(
                second.Bind(binding)).Candidate.Assembly);
        Assert.Single(policy.Requests);
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
            typeof(ResolvedAssemblyCandidate)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

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
        Action? opened = null) =>
        ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () =>
            {
                opened?.Invoke();
                return new MemoryStream(image, writable: false);
            },
            provenance: AssemblyResolutionProvenance.Local("test"));

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
        bool definesOtherType = false)
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

        if (definesType)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
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
                metadata.AddExportedType(
                    TypeAttributes.Public | Forwarder,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Type"),
                    target,
                    typeDefinitionId: 0);
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
}
