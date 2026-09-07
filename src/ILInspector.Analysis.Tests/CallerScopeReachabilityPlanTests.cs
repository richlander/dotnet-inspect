using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class CallerScopeReachabilityPlanTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardedDifferentTarget_RulesOutTheCaller(bool issueContinuation)
    {
        byte[] targetImage = BuildTarget(new Version(2, 0, 0, 0));
        var target = Descriptor(targetImage);
        var different = Descriptor(BuildTarget());
        var facade = Descriptor(BuildFacade(new Version(1, 0, 0, 0), different.Identity));
        byte[] callerImage = BuildCaller(facade.Identity);
        var caller = Descriptor(callerImage);
        var fallback = new ExactPolicy([facade, different], issueContinuation);

        CallerScopeReachabilityPlan plan = CallerScopeReachabilityPlan.Create(
            fallback,
            target,
            ReadTargetDefinition(targetImage),
            [caller]);

        Assert.Empty(plan.DirectCandidates);
        Assert.Empty(plan.GraphCandidates);
        Assert.IsType<CandidateTypeRelation.DifferentDefinition>(
            plan.Resolution.GetRelation(caller, ReadCallerReference(callerImage)));
    }

    [Fact]
    public void ExactFacadeIdentity_ResolvesToTargetDefinition()
    {
        byte[] targetImage = BuildTarget();
        AssemblyReferenceIdentity targetIdentity =
            ReadIdentity(targetImage);
        byte[] facadeImage = BuildFacade(
            new Version(1, 0, 0, 0),
            targetIdentity);
        AssemblyReferenceIdentity facadeIdentity =
            ReadIdentity(facadeImage);
        byte[] callerImage = BuildCaller(facadeIdentity);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        ResolvedAssemblyReference caller = Descriptor(callerImage);
        TypeRef targetType = ReadTargetDefinition(targetImage);
        TypeRef callerType = ReadCallerReference(callerImage);
        var policy = new ExactPolicy([facade]);

        CallerScopeReachabilityPlan plan =
            CallerScopeReachabilityPlan.Create(
                policy,
                target,
                targetType,
                [caller]);

        Assert.Contains(caller, plan.DirectCandidates);
        Assert.Contains(caller, plan.GraphCandidates);
        Assert.IsType<CandidateTypeRelation.SameDefinition>(
            plan.Resolution.GetRelation(caller, callerType));
    }

    [Fact]
    public void SameNamedFacadeWithDifferentIdentity_CannotVouchForCaller()
    {
        byte[] targetImage = BuildTarget();
        AssemblyReferenceIdentity targetIdentity =
            ReadIdentity(targetImage);
        byte[] expectedFacadeImage = BuildFacade(
            new Version(1, 0, 0, 0),
            targetIdentity);
        byte[] wrongFacadeImage = BuildFacade(
            new Version(2, 0, 0, 0),
            targetIdentity);
        byte[] callerImage = BuildCaller(
            ReadIdentity(expectedFacadeImage));
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference wrongFacade =
            Descriptor(wrongFacadeImage);
        ResolvedAssemblyReference caller = Descriptor(callerImage);
        TypeRef callerType = ReadCallerReference(callerImage);
        var policy = new ExactPolicy([wrongFacade]);

        CallerScopeReachabilityPlan plan =
            CallerScopeReachabilityPlan.Create(
                policy,
                target,
                ReadTargetDefinition(targetImage),
                [caller, wrongFacade]);

        Assert.Contains(caller, plan.DirectCandidates);
        Assert.IsType<CandidateTypeRelation.Indeterminate>(
            plan.Resolution.GetRelation(caller, callerType));
    }

    [Fact]
    public void TargetIdentityBinding_PrefersSelectedTargetOverScopeCopy()
    {
        byte[] targetImage = BuildTarget();
        AssemblyReferenceIdentity targetIdentity =
            ReadIdentity(targetImage);
        byte[] callerImage = BuildCaller(targetIdentity);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference duplicate = Descriptor(targetImage);
        ResolvedAssemblyReference caller = Descriptor(callerImage);
        TypeRef targetType = ReadTargetDefinition(targetImage);
        var policy = new ExactPolicy([]);

        CallerScopeReachabilityPlan plan =
            CallerScopeReachabilityPlan.Create(
                policy,
                target,
                targetType,
                [caller, duplicate]);

        Assert.IsType<CandidateTypeRelation.SameDefinition>(
            plan.Resolution.GetRelation(
                caller,
                ReadCallerReference(callerImage)));
        Assert.IsType<CandidateTypeRelation.Indeterminate>(
            plan.Resolution.GetRelation(
                duplicate,
                targetType));
    }

    [Fact]
    public void ExplicitTargetRollForward_RemainsSameDefinition()
    {
        byte[] targetV1Image = BuildTarget(
            new Version(1, 0, 0, 0));
        byte[] targetV2Image = BuildTarget(
            new Version(2, 0, 0, 0));
        byte[] callerImage = BuildCaller(
            ReadIdentity(targetV1Image));
        ResolvedAssemblyReference targetV2 =
            Descriptor(targetV2Image);
        ResolvedAssemblyReference caller = Descriptor(callerImage);
        TypeRef callerType = ReadCallerReference(callerImage);
        var policy = new SelectedPolicy(targetV2);

        CallerScopeReachabilityPlan plan =
            CallerScopeReachabilityPlan.Create(
                policy,
                targetV2,
                ReadTargetDefinition(targetV2Image),
                [caller]);

        Assert.Contains(caller, plan.GraphCandidates);
        Assert.IsType<CandidateTypeRelation.SameDefinition>(
            plan.Resolution.GetRelation(caller, callerType));
    }

    [Fact]
    public void ScopeFirstBindingPolicy_PreservesDelegatedTerminalResults()
    {
        ResolvedAssemblyReference requested =
            Descriptor(BuildTarget(new Version(1, 0, 0, 0)));
        ResolvedAssemblyReference target =
            Descriptor(BuildTarget(new Version(2, 0, 0, 0)));
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested.Identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        AssemblyBindingSelection[] terminalResults =
        [
            AssemblyBindingSelection.Found(requested),
            AssemblyBindingSelection.NotFound(),
            AssemblyBindingSelection.NameOwnedButNoMatch(),
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable)),
            AssemblyBindingSelection.Multiple([requested, target]),
            AssemblyBindingSelection.Invalid(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.InvalidPolicyResult)),
        ];

        foreach (AssemblyBindingSelection terminal in terminalResults)
        {
            var policy =
                new CallerScopeReachabilityPlan.ScopeFirstBindingPolicy(
                    new FixedPolicy(terminal),
                    target,
                    []);

            AssemblyBindingSelection actual = policy.Select(request).Selection;
            if (terminal is AssemblyBindingSelection.Selected selected)
            {
                var transformed = Assert.IsType<AssemblyBindingSelection.Selected>(actual);
                Assert.Same(selected.Assembly, transformed.Assembly);
                Assert.Same(policy.Version, transformed.Occurrence.Lineage.Version);
            }
            else
            {
                Assert.Same(terminal, actual);
            }
        }
    }

    [Fact]
    public void ScopeFirstBindingPolicy_PreservesNestedContinuationAndShadows()
    {
        var target = Descriptor(BuildTarget());
        var facade = Descriptor(BuildFacade(new Version(1, 0, 0, 0), target.Identity));
        var shadow = Descriptor(BuildFacade(new Version(1, 0, 0, 0), target.Identity));
        var fallback = new FixedPolicy(AssemblyBindingSelection.NotFound());
        AssemblyBindingOccurrence delegated = new TestLineage(fallback.Version).Issue(facade);
        fallback.SnapshotFactory = _ => new(
            fallback.Version,
            AssemblyBindingSelection.FoundOccurrence(delegated, [shadow]));
        var policy = new CallerScopeReachabilityPlan.ScopeFirstBindingPolicy(
            fallback, target, []);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(facade.Identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);

        AssemblyBindingSelectionSnapshot first = policy.Select(request);
        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(first.Selection);
        Assert.NotSame(fallback.Version, first.Version);
        Assert.Same(first.Version, selected.Occurrence.Lineage.Version);
        Assert.Equal([shadow], selected.ShadowedAssemblies);
        fallback.SnapshotFactory = continued =>
        {
            var origin = Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(continued.Origin);
            Assert.Same(delegated, origin.Occurrence);
            return new(fallback.Version, AssemblyBindingSelection.FoundOccurrence(delegated));
        };
        var next = new AssemblyBindingRequest(
            request.Target,
            AssemblyBindingOrigin.FromOccurrence(selected.Occurrence),
            request.Scope);

        AssemblyBindingSelectionSnapshot second = policy.Select(next);

        Assert.Same(first.Version, second.Version);
        Assert.Equal(
            selected.Occurrence.Lineage,
            Assert.IsType<AssemblyBindingSelection.Selected>(second.Selection).Occurrence.Lineage);
        var other = new CallerScopeReachabilityPlan.ScopeFirstBindingPolicy(
            fallback, target, []);
        Assert.Equal(
            AssemblyBindingFailureKind.InvalidBindingOrigin,
            Assert.IsType<AssemblyBindingSelection.Rejected>(other.Select(next).Selection).Failure.Kind);
        Assert.Equal(2, fallback.CallCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ScopeFirstBindingPolicy_ForeignSnapshotRetiresStateBeforeComposition(
        bool changesAdvertisedVersion,
        bool intrinsic)
    {
        var target = Descriptor(BuildTarget(new Version(2, 0, 0, 0)));
        var fallback = new FixedPolicy(AssemblyBindingSelection.NameNotOwned());
        var policy = new CallerScopeReachabilityPlan.ScopeFirstBindingPolicy(
            fallback, target, []);
        var canonical = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(target.Identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        AssemblyBindingSelectionSnapshot original = policy.Select(canonical);
        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(original.Selection);
        var foreign = new AssemblyBindingSelectionSnapshot(
            new(), AssemblyBindingSelection.NameNotOwned());
        fallback.SnapshotFactory = _ =>
        {
            if (changesAdvertisedVersion)
                fallback.Version = foreign.Version;
            return foreign;
        };
        var request = new AssemblyBindingRequest(
            intrinsic
                ? AssemblyBindingTarget.CoreLibrary()
                : AssemblyBindingTarget.Reference(
                    target.Identity with { Version = new Version(1, 0, 0, 0) }),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);

        Assert.Same(foreign, policy.Select(request));
        Assert.NotSame(original.Version, policy.Version);
        var retired = new AssemblyBindingRequest(
            canonical.Target,
            AssemblyBindingOrigin.FromOccurrence(selected.Occurrence),
            canonical.Scope);
        Assert.Equal(
            AssemblyBindingFailureKind.InvalidBindingOrigin,
            Assert.IsType<AssemblyBindingSelection.Rejected>(policy.Select(retired).Selection).Failure.Kind);
        Assert.Equal(1, fallback.CallCount);
        fallback.SnapshotFactory = null;

        AssemblyBindingSelectionSnapshot refreshed = policy.Select(request);

        Assert.Same(policy.Version, refreshed.Version);
        Assert.NotSame(original.Version, refreshed.Version);
        Assert.NotSame(foreign.Version, refreshed.Version);
        Assert.Same(refreshed.Version, policy.Select(canonical).Version);
    }

    [Fact]
    public void ScopeFirstBindingPolicy_FallbackDriftRefreshesCanonicalSelections()
    {
        var target = Descriptor(BuildTarget());
        var fallback = new FixedPolicy(AssemblyBindingSelection.NotFound());
        var policy = new CallerScopeReachabilityPlan.ScopeFirstBindingPolicy(
            fallback, target, []);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(target.Identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        AssemblyBindingSelectionSnapshot before = policy.Select(request);
        fallback.Version = new();

        AssemblyBindingSelectionSnapshot after = policy.Select(request);

        Assert.NotSame(before.Version, after.Version);
        Assert.NotSame(fallback.Version, after.Version);
        Assert.Same(policy.Version, after.Version);
        Assert.Same(target, Assert.IsType<AssemblyBindingSelection.Selected>(after.Selection).Assembly);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public void ScopeFirstBindingPolicy_NullSnapshotRemainsInvalid()
    {
        var target = Descriptor(BuildTarget());
        var fallback = new FixedPolicy(AssemblyBindingSelection.NotFound())
        {
            SnapshotFactory = _ => null!,
        };
        var policy = new CallerScopeReachabilityPlan.ScopeFirstBindingPolicy(
            fallback, target, []);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);

        Assert.Equal(
            AssemblyBindingFailureKind.InvalidPolicyResult,
            Assert.IsType<AssemblyBindingSelection.Rejected>(policy.Select(request).Selection).Failure.Kind);
    }

    [Fact]
    public void ScopeFirstBindingPolicy_NoNameOwnerRequiresIdentityPolicy()
    {
        ResolvedAssemblyReference requested =
            Descriptor(BuildTarget(new Version(1, 0, 0, 0)));
        ResolvedAssemblyReference target =
            Descriptor(BuildTarget(new Version(2, 0, 0, 0)));
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(requested.Identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        var policy =
            new CallerScopeReachabilityPlan.ScopeFirstBindingPolicy(
                new FixedPolicy(
                    AssemblyBindingSelection.NameNotOwned()),
                target,
                []);

        var unavailable =
            Assert.IsType<AssemblyBindingSelection.Unavailable>(
                policy.Select(request).Selection);
        Assert.Equal(
            AssemblyBindingFailureKind.IdentityPolicyRequired,
            unavailable.Failure.Kind);
    }

    [Fact]
    public void ScopeFirstBindingPolicy_ExactRootWinsOverSameNameTargetSkew()
    {
        ResolvedAssemblyReference target =
            Descriptor(BuildTarget(new Version(2, 0, 0, 0)));
        ResolvedAssemblyReference root =
            Descriptor(BuildTarget(new Version(1, 0, 0, 0)));
        var fallback = new FixedPolicy(
            AssemblyBindingSelection.NameNotOwned());
        var policy =
            new CallerScopeReachabilityPlan.ScopeFirstBindingPolicy(
                fallback,
                target,
                [root]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(root.Identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);

        var selected = Assert.IsType<AssemblyBindingSelection.Selected>(
            policy.Select(request).Selection);

        Assert.Same(root, selected.Assembly);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public void ScopeFirstBindingPolicy_SameNameOwnersRemainAmbiguous()
    {
        ResolvedAssemblyReference target =
            Descriptor(BuildTarget(new Version(3, 0, 0, 0)));
        ResolvedAssemblyReference root =
            Descriptor(BuildTarget(new Version(2, 0, 0, 0)));
        var fallback = new FixedPolicy(
            AssemblyBindingSelection.NameNotOwned());
        var policy =
            new CallerScopeReachabilityPlan.ScopeFirstBindingPolicy(
                fallback,
                target,
                [root]);
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                target.Identity with
                {
                    Version = new Version(1, 0, 0, 0),
                }),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);

        var ambiguous =
            Assert.IsType<AssemblyBindingSelection.Ambiguous>(
                policy.Select(request).Selection);

        Assert.Equal([target, root], ambiguous.Assemblies);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public void ScopeFirstBindingPolicy_SkewedRootRequiresIdentityPolicy()
    {
        ResolvedAssemblyReference target = Descriptor(BuildTarget());
        ResolvedAssemblyReference root =
            Descriptor(
                BuildFacade(
                    new Version(2, 0, 0, 0),
                    target.Identity));
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                root.Identity with
                {
                    Version = new Version(1, 0, 0, 0),
                }),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        var fallback = new FixedPolicy(
            AssemblyBindingSelection.NameNotOwned());
        var policy =
            new CallerScopeReachabilityPlan.ScopeFirstBindingPolicy(
                fallback,
                target,
                [root]);

        var unavailable =
            Assert.IsType<AssemblyBindingSelection.Unavailable>(
                policy.Select(request).Selection);

        Assert.Equal(
            AssemblyBindingFailureKind.IdentityPolicyRequired,
            unavailable.Failure.Kind);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public void VersionSkewedFacadeRoots_ReportAmbiguous()
    {
        byte[] targetImage = BuildTarget();
        ResolvedAssemblyReference target = Descriptor(targetImage);
        byte[] firstFacadeImage = BuildFacade(
            new Version(2, 0, 0, 0),
            target.Identity);
        byte[] secondFacadeImage = BuildFacade(
            new Version(3, 0, 0, 0),
            target.Identity);
        ResolvedAssemblyReference first = Descriptor(firstFacadeImage);
        ResolvedAssemblyReference second = Descriptor(secondFacadeImage);
        byte[] callerImage = BuildCaller(
            first.Identity with
            {
                Version = new Version(1, 0, 0, 0),
            });
        ResolvedAssemblyReference caller = Descriptor(callerImage);
        var fallback = new FixedPolicy(
            AssemblyBindingSelection.NameNotOwned());

        CallerScopeReachabilityPlan plan =
            CallerScopeReachabilityPlan.Create(
                fallback,
                target,
                ReadTargetDefinition(targetImage),
                [caller, first, second]);

        var relation =
            Assert.IsType<CandidateTypeRelation.Indeterminate>(
                plan.Resolution.GetRelation(
                    caller,
                    ReadCallerReference(callerImage)));
        var resolution =
            Assert.IsType<TypeCorrespondenceFailure.Resolution>(
                relation.Failure);
        Assert.IsType<TypeResolutionOutcome.Ambiguous>(
            resolution.NonSuccess);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public void EcmaEquivalentTargetIdentity_ResolvesToTargetDefinition()
    {
        byte[] targetImage = BuildTarget();
        ResolvedAssemblyReference target = Descriptor(targetImage);
        AssemblyReferenceIdentity equivalent =
            target.Identity with
            {
                Name = target.Identity.Name.ToLowerInvariant(),
                Culture = "neutral",
            };
        byte[] callerImage = BuildCaller(equivalent);
        ResolvedAssemblyReference caller = Descriptor(callerImage);
        var fallback = new FixedPolicy(
            AssemblyBindingSelection.NameNotOwned());

        CallerScopeReachabilityPlan plan =
            CallerScopeReachabilityPlan.Create(
                fallback,
                target,
                ReadTargetDefinition(targetImage),
                [caller]);

        Assert.IsType<CandidateTypeRelation.SameDefinition>(
            plan.Resolution.GetRelation(
                caller,
                ReadCallerReference(callerImage)));
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public void EcmaEquivalentFacadeIdentity_ResolvesToTargetDefinition()
    {
        byte[] targetImage = BuildTarget();
        ResolvedAssemblyReference target = Descriptor(targetImage);
        byte[] facadeImage = BuildFacade(
            new Version(1, 0, 0, 0),
            target.Identity);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        AssemblyReferenceIdentity equivalent =
            facade.Identity with
            {
                Name = facade.Identity.Name.ToLowerInvariant(),
                Culture = "neutral",
            };
        byte[] callerImage = BuildCaller(equivalent);
        ResolvedAssemblyReference caller = Descriptor(callerImage);
        var fallback = new FixedPolicy(
            AssemblyBindingSelection.NameNotOwned());

        CallerScopeReachabilityPlan plan =
            CallerScopeReachabilityPlan.Create(
                fallback,
                target,
                ReadTargetDefinition(targetImage),
                [caller, facade]);

        Assert.IsType<CandidateTypeRelation.SameDefinition>(
            plan.Resolution.GetRelation(
                caller,
                ReadCallerReference(callerImage)));
        Assert.Equal(0, fallback.CallCount);
    }

    static TypeRef ReadTargetDefinition(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle handle = reader.TypeDefinitions.Single(
            candidate =>
                reader.GetString(
                    reader.GetTypeDefinition(candidate).Name) == "Type");
        return TypeRefDecoder.Instance.GetTypeFromDefinition(
            reader,
            handle,
            0);
    }

    static TypeRef ReadCallerReference(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        return TypeRefDecoder.Instance.GetTypeFromReference(
            pe.GetMetadataReader(),
            MetadataTokens.TypeReferenceHandle(1),
            0);
    }

    static byte[] BuildTarget() =>
        BuildTarget(new Version(1, 0, 0, 0));

    static byte[] BuildTarget(Version version)
    {
        var metadata = AssemblyMetadata(
            "Target",
            version);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildFacade(
        Version version,
        AssemblyReferenceIdentity target)
    {
        var metadata = AssemblyMetadata("Facade", version);
        AssemblyReferenceHandle implementation =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(target.Name),
                target.Version ?? new Version(0, 0, 0, 0),
                target.Culture is null
                    ? default
                    : metadata.GetOrAddString(target.Culture),
                target.PublicKeyToken is null
                    ? default
                    : metadata.GetOrAddBlob(
                        Convert.FromHexString(target.PublicKeyToken)),
                flags: default,
                hashValue: default);
        metadata.AddExportedType(
            Forwarder,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"),
            implementation,
            typeDefinitionId: 0);
        return Serialize(metadata);
    }

    static byte[] BuildCaller(AssemblyReferenceIdentity facade)
    {
        var metadata = AssemblyMetadata(
            "Caller",
            new Version(1, 0, 0, 0));
        AssemblyReferenceHandle reference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(facade.Name),
                facade.Version ?? new Version(0, 0, 0, 0),
                facade.Culture is null
                    ? default
                    : metadata.GetOrAddString(facade.Culture),
                facade.PublicKeyToken is null
                    ? default
                    : metadata.GetOrAddBlob(
                        Convert.FromHexString(facade.PublicKeyToken)),
                flags: default,
                hashValue: default);
        metadata.AddTypeReference(
            reference,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"));
        return Serialize(metadata);
    }

    static MetadataBuilder AssemblyMetadata(
        string name,
        Version version)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            version,
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
        return metadata;
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static ResolvedAssemblyReference Descriptor(byte[] image) =>
        ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () =>
                new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("test"));

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    sealed class ExactPolicy : IAssemblyBindingPolicy
    {
        readonly ImmutableDictionary<
            AssemblyReferenceIdentity,
            ResolvedAssemblyReference> _assemblies;
        readonly bool _issueContinuation;

        internal ExactPolicy(
            IEnumerable<ResolvedAssemblyReference> assemblies,
            bool issueContinuation = false)
        {
            _assemblies = assemblies.ToImmutableDictionary(
                assembly => assembly.Identity);
            _issueContinuation = issueContinuation;
        }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && _assemblies.TryGetValue(
                reference.Identity,
                out ResolvedAssemblyReference? assembly)
                ? _issueContinuation
                    ? AssemblyBindingSelection.FoundOccurrence(
                        new TestLineage(Version).Issue(assembly))
                    : AssemblyBindingSelection.Found(assembly)
                : AssemblyBindingSelection.NotFound();
        }
    }

    sealed record TestLineage(AssemblyBindingPolicyVersion IssuingVersion)
        : AssemblyBindingLineage(IssuingVersion)
    {
        internal AssemblyBindingOccurrence Issue(
            ResolvedAssemblyReference assembly) => CreateOccurrence(assembly);
    }

    sealed class SelectedPolicy(
        ResolvedAssemblyReference selected)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.Found(selected);
        }
    }

    sealed class FixedPolicy(AssemblyBindingSelection selection)
        : IAssemblyBindingPolicy
    {
        public int CallCount { get; private set; }

        public AssemblyBindingPolicyVersion Version { get; set; } = new();

        internal Func<AssemblyBindingRequest, AssemblyBindingSelectionSnapshot>? SnapshotFactory
        {
            get;
            set;
        }

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            CallCount++;
            if (SnapshotFactory is not null)
                return SnapshotFactory(request);

            return new AssemblyBindingSelectionSnapshot(
                Version,
                selection);
        }
    }
}
