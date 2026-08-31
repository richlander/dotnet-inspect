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

            Assert.Same(terminal, policy.Select(request));
        }
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
                policy.Select(request));
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
            policy.Select(request));

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
                policy.Select(request));

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
                policy.Select(request));

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

        internal ExactPolicy(
            IEnumerable<ResolvedAssemblyReference> assemblies) =>
            _assemblies = assemblies.ToImmutableDictionary(
                assembly => assembly.Identity);

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && _assemblies.TryGetValue(
                    reference.Identity,
                    out ResolvedAssemblyReference? assembly)
                    ? AssemblyBindingSelection.Found(assembly)
                    : AssemblyBindingSelection.NotFound();
    }

    sealed class SelectedPolicy(
        ResolvedAssemblyReference selected)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.Found(selected);
    }

    sealed class FixedPolicy(AssemblyBindingSelection selection)
        : IAssemblyBindingPolicy
    {
        public int CallCount { get; private set; }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            CallCount++;
            return selection;
        }
    }
}
