using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Xml;
using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class CatalogDirectCallerQueryTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    static XmlReader WrapThroughFacade(XmlReader reader) =>
        XmlReader.Create(reader, new XmlReaderSettings());

    [Fact]
    public void ForwardedParameterTypesJoinCompleteMemberSignatures()
    {
        string? targetPath = PrivateXmlPath();
        Assert.SkipWhen(
            targetPath is null,
            "System.Private.Xml not in the runtime directory.");

        ImmutableArray<CatalogDirectCaller> callers = FindFrameworkCallers(
            targetPath!,
            "XmlReader",
            "XmlReaderSettings");

        Assert.Contains(
            callers,
            caller => caller.Call.Caller.Name
                == nameof(WrapThroughFacade));
    }

    [Fact]
    public void ForwardedParameterTypesDoNotJoinCloseOverloads()
    {
        string? targetPath = PrivateXmlPath();
        Assert.SkipWhen(
            targetPath is null,
            "System.Private.Xml not in the runtime directory.");

        ImmutableArray<CatalogDirectCaller> callers = FindFrameworkCallers(
            targetPath!,
            "Stream",
            "XmlReaderSettings");

        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name
                == nameof(WrapThroughFacade));
    }

    [Fact]
    public void ConstructedGenericCallJoinsOpenDefinition()
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        LibraryBodyIndex source = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        MethodIdentity store = target.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Box`1"
            && method.Name == "Store"
            && method.ParameterTypes is [{ Kind: TypeRefKind.GenericParameter }]);

        ImmutableArray<CatalogDirectCaller> callers = Find(
            target,
            store.MetadataToken,
            source,
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(target.Path)),
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(source.Path)));

        Assert.Contains(
            callers,
            caller => caller.Call.Caller.Name == "UseBox");
        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name == "UseBoxList");
    }

    [Fact]
    public void UnavailableCorrespondenceDoesNotFabricateCaller()
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath());
        LibraryBodyIndex source = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        MethodIdentity ping = target.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Api"
            && method.Name == "Ping"
            && method.ParameterTypes.IsEmpty);

        ImmutableArray<CatalogDirectCaller> callers = Find(
            target,
            ping.MetadataToken,
            source,
            UnavailablePolicy.Instance,
            UnavailablePolicy.Instance);

        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name == "Run");
    }

    [Fact]
    public void MatchingUnresolvedParameterContractsRetainCaller()
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        LibraryBodyIndex source = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        MethodIdentity ping = StringPing(target);

        ImmutableArray<CatalogDirectCaller> callers = Find(
            target,
            ping.MetadataToken,
            source,
            UnavailablePolicy.Instance,
            UnavailablePolicy.Instance);

        Assert.Contains(
            callers,
            caller => caller.Call.Caller.Name == "RunString");
        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name == "RunInt");
    }

    [Fact]
    public void ResolvedAndUnresolvedMatchingParameterContractsRetainCaller()
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        LibraryBodyIndex source = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath());
        MethodIdentity ping = StringPing(target);
        var sourcePolicy = new CountingPolicy(new FrameworkPolicy());

        ImmutableArray<CatalogDirectCaller> callers = Find(
            target,
            ping.MetadataToken,
            source,
            UnavailablePolicy.Instance,
            sourcePolicy);

        Assert.Contains(
            callers,
            caller => caller.Call.Caller.Name == "RunString");
        Assert.DoesNotContain(
            callers,
            caller => caller.Call.Caller.Name == "RunInt");
        Assert.True(sourcePolicy.SelectedCount > 0);
    }

    [Fact]
    public void ReachabilityProvenFacadeVouchesForRepeatedSignatureType()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-direct-callers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            byte[] targetImage = BuildTarget();
            AssemblyReferenceIdentity targetIdentity =
                ReadIdentity(targetImage);
            byte[] facadeImage = BuildFacade(targetIdentity);
            byte[] callerImage = BuildCaller(
                ReadIdentity(facadeImage));
            string targetPath = WriteImage(
                directory,
                "Target.dll",
                targetImage);
            string callerPath = WriteImage(
                directory,
                "Caller.dll",
                callerImage);
            LibraryBodyIndex target = LibraryBodyIndex.Open(targetPath);
            LibraryBodyIndex caller = LibraryBodyIndex.Open(callerPath);
            MethodIdentity echo = target.DeclaredMethods.Single(
                method => method.Name == "Echo");
            ResolvedAssemblyReference targetAssembly =
                Descriptor(target);
            ResolvedAssemblyReference callerAssembly =
                Descriptor(caller);
            ResolvedAssemblyReference facadeAssembly =
                Descriptor(facadeImage);
            var reachability = CallerScopeReachabilityPlan.Create(
                new ExactPolicy(
                    [targetAssembly, facadeAssembly]),
                targetAssembly,
                echo.DeclaringType,
                [callerAssembly]);
            var groupPolicy =
                new SourceRelativeAssemblyGroupBindingPolicy(
                    [
                        (
                            targetAssembly,
                            (IAssemblyBindingPolicy)
                                UnavailablePolicy.Instance),
                        (
                            callerAssembly,
                            (IAssemblyBindingPolicy)
                                UnavailablePolicy.Instance),
                    ]);
            var targetParticipant = new CatalogCallGraphParticipant(
                target,
                targetAssembly);
            var callerParticipant = new CatalogCallGraphParticipant(
                caller,
                callerAssembly);

            Assert.Empty(
                CatalogDirectCallerQuery.Find(
                    groupPolicy,
                    targetParticipant,
                    echo.MetadataToken,
                    [callerParticipant]));
            CatalogDirectCaller match = Assert.Single(
                CatalogDirectCallerQuery.Find(
                    groupPolicy,
                    targetParticipant,
                    echo.MetadataToken,
                    [callerParticipant],
                    reachability.Resolution));
            Assert.Equal("Run", match.Call.Caller.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static ImmutableArray<CatalogDirectCaller> FindFrameworkCallers(
        string targetPath,
        params string[] parameterTypes)
    {
        LibraryBodyIndex target = LibraryBodyIndex.Open(targetPath);
        LibraryBodyIndex source = LibraryBodyIndex.Open(
            typeof(CatalogDirectCallerQueryTests).Assembly.Location);
        MethodIdentity create = target.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "XmlReader"
            && method.Name == "Create"
            && method.ParameterTypes
                .Select(parameter => parameter.Name)
                .SequenceEqual(parameterTypes));
        return Find(
            target,
            create.MetadataToken,
            source,
            new FrameworkPolicy(),
            new FrameworkPolicy());
    }

    static ImmutableArray<CatalogDirectCaller> Find(
        LibraryBodyIndex target,
        int targetMethodToken,
        LibraryBodyIndex source,
        IAssemblyBindingPolicy targetPolicy,
        IAssemblyBindingPolicy sourcePolicy)
    {
        var targetAssembly = Descriptor(target);
        var sourceAssembly = Descriptor(source);
        var policy = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (targetAssembly, targetPolicy),
                (sourceAssembly, sourcePolicy),
            ]);
        return CatalogDirectCallerQuery.Find(
            policy,
            new CatalogCallGraphParticipant(target, targetAssembly),
            targetMethodToken,
            [new CatalogCallGraphParticipant(source, sourceAssembly)]);
    }

    static MethodIdentity StringPing(LibraryBodyIndex target) =>
        target.DeclaredMethods.Single(method =>
            method.DeclaringType.Name == "Api"
            && method.Name == "Ping"
            && method.ParameterTypes is [{ Name: "String" }]);

    static ResolvedAssemblyReference Descriptor(LibraryBodyIndex index) =>
        ResolvedAssemblyReference.CreateFromPath(
            index.Path,
            AssemblyResolutionProvenance.Local(
                "catalog direct-caller test"));

    static ResolvedAssemblyReference Descriptor(byte[] image) =>
        ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () =>
                new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local(
                "catalog direct-caller test"));

    static byte[] BuildTarget()
    {
        var metadata = AssemblyMetadata("Target");
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("Echo"),
            AddEchoSignature(metadata, type),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));
        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildFacade(AssemblyReferenceIdentity target)
    {
        var metadata = AssemblyMetadata("Facade");
        AssemblyReferenceHandle implementation =
            AddAssemblyReference(metadata, target);
        metadata.AddExportedType(
            Forwarder,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"),
            implementation,
            typeDefinitionId: 0);
        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildCaller(AssemblyReferenceIdentity facade)
    {
        var metadata = AssemblyMetadata("Caller");
        AssemblyReferenceHandle reference =
            AddAssemblyReference(metadata, facade);
        TypeReferenceHandle type = metadata.AddTypeReference(
            reference,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type"));
        MemberReferenceHandle echo = metadata.AddMemberReference(
            type,
            metadata.GetOrAddString("Echo"),
            AddEchoSignature(metadata, type));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("Entry"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var il = new BlobBuilder();
        var instructions = new InstructionEncoder(
            il,
            new ControlFlowBuilder());
        instructions.OpCode(ILOpCode.Ldnull);
        instructions.Call(echo);
        instructions.OpCode(ILOpCode.Pop);
        instructions.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(instructions, maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            AddVoidSignature(metadata),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata, methodBodies);
    }

    static MetadataBuilder AssemblyMetadata(string name)
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
        return metadata;
    }

    static AssemblyReferenceHandle AddAssemblyReference(
        MetadataBuilder metadata,
        AssemblyReferenceIdentity identity) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(identity.Name),
            identity.Version ?? new Version(0, 0, 0, 0),
            identity.Culture is null
                ? default
                : metadata.GetOrAddString(identity.Culture),
            identity.PublicKeyToken is null
                ? default
                : metadata.GetOrAddBlob(
                    Convert.FromHexString(identity.PublicKeyToken)),
            flags: default,
            hashValue: default);

    static BlobHandle AddEchoSignature(
        MetadataBuilder metadata,
        EntityHandle type)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                1,
                returnType => returnType.Type().Type(
                    type,
                    isValueType: false),
                parameters => parameters
                    .AddParameter()
                    .Type()
                    .Type(type, isValueType: false));
        return metadata.GetOrAddBlob(signature);
    }

    static BlobHandle AddVoidSignature(MetadataBuilder metadata)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        return metadata.GetOrAddBlob(signature);
    }

    static byte[] Serialize(
        MetadataBuilder metadata,
        BlobBuilder methodBodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    static string WriteImage(
        string directory,
        string name,
        byte[] image)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, image);
        return path;
    }

    static string? PrivateXmlPath()
    {
        string path = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Private.Xml.dll");
        return File.Exists(path) ? path : null;
    }

    sealed class FrameworkPolicy : IAssemblyBindingPolicy
    {
        readonly string _frameworkDirectory =
            Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                if (request.Target
                    is not AssemblyBindingTarget.AssemblyReference reference)
                {
                    return AssemblyBindingSelection.CannotSelect(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind.CandidateUnavailable));
                }

                string path = Path.Combine(
                    _frameworkDirectory,
                    reference.Identity.Name + ".dll");
                if (!File.Exists(path))
                {
                    return AssemblyBindingSelection.CannotSelect(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind.CandidateUnavailable));
                }

                ResolvedAssemblyReference assembly =
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "framework direct-caller test"));
                return assembly.Identity == reference.Identity
                    ? AssemblyBindingSelection.Found(assembly)
                    : AssemblyBindingSelection.CannotSelect(
                        new AssemblyBindingFailure(
                            AssemblyBindingFailureKind.IdentityPolicyRequired));

            }
        }
    }

    sealed class CountingPolicy(IAssemblyBindingPolicy inner)
        : IAssemblyBindingPolicy
    {
        internal int SelectedCount { get; private set; }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                AssemblyBindingSelection selection =
                    inner.Select(request).Selection;
                if (selection is AssemblyBindingSelection.Selected)
                    SelectedCount++;
                return selection;

            }
        }
    }

    sealed class ExactPolicy(
        IEnumerable<ResolvedAssemblyReference> assemblies)
        : IAssemblyBindingPolicy
    {
        readonly ImmutableDictionary<
            AssemblyReferenceIdentity,
            ResolvedAssemblyReference> _assemblies =
                assemblies.ToImmutableDictionary(
                    assembly => assembly.Identity);

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
                ? AssemblyBindingSelection.Found(assembly)
                : AssemblyBindingSelection.NotFound();
        }
    }

    sealed class UnavailablePolicy : IAssemblyBindingPolicy
    {
        internal static UnavailablePolicy Instance { get; } = new();

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable));
        }
    }
}
