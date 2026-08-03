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

    static byte[] BuildTarget()
    {
        var metadata = AssemblyMetadata(
            "Target",
            new Version(1, 0, 0, 0));
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
}
