using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Gates the group-scoped Research projection queries: they project from workspace-owned content
/// with no filesystem path, address an exact <c>MethodDef</c>, carry the whole-assembly fact
/// context path-keyed resolution cannot reach, resolve references through the participant's own
/// binding policy, and report participant failure as a typed entry.
/// </summary>
public sealed class AssemblyContextResearchProjectionQueryTests
{
    [Fact]
    public void TypeProjection_ProjectsFromContentWithoutAFilesystemPath()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        Assert.Null(Assert.Single(group.Participants).Assembly.Path);

        AssemblyContextResult<ResearchViews.TypeProjectionResult> result =
            AssemblyContextTypeProjectionQuery.Execute(
                group,
                new AssemblyContextTypeProjectionRequest(
                    typeof(ResearchProjectionProbe).FullName!));

        ResearchViews.TypeProjectionResult projection = Available(result);
        Assert.Equal(typeof(ResearchProjectionProbe).FullName, projection.Identity.FullName);
        Assert.Equal("class", projection.Identity.Kind);
        Assert.NotNull(projection.Composition);
        Assert.True(projection.Composition!.Methods > 0);
    }

    [Fact]
    public void MemberProjection_ProducesAnAnnotatedSourceDocumentFromContent()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        Assert.Null(projection.Projection.SourceDocumentFailure);
        Assert.Null(projection.ContextLimitation);
        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.NotEmpty(document.Text);
        Assert.Contains(document.Nodes, node => node.Medium == SourceLineKind.CSharp);
        Assert.Contains(document.Nodes, node => node.Medium == SourceLineKind.Il);
    }

    [Fact]
    public void MemberProjection_CarriesTheWholeAssemblyFactContext()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        // Boxing is an assembly-scoped Analysis fact. Path-keyed resolution cannot reach a
        // snapshot, so its presence proves the query supplied the context rather than letting
        // the projection observe a consistent absence.
        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.Contains(document.Facts, fact => fact.Descriptor == "alloc.box");
    }

    [Fact]
    public void MemberProjection_MapsAnInvocationNodeToItsTypedCallee()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(nameof(ResearchProjectionProbe.InvokeLocal))));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        AssemblyMemberInvocationDestination destination =
            Assert.Single(projection.InvocationDestinations);
        AnnotatedSourceNode node = document.Nodes[destination.NodeId];
        Assert.Equal("InvocationExpression", node.Kind);
        Assert.Equal(SourceLineKind.CSharp, node.Medium);
        Assert.Equal(
            nameof(ResearchProjectionProbe.LocalCallee),
            destination.Target.Member.Name);
        Assert.Equal(
            typeof(ResearchProjectionProbe).FullName,
            destination.Target.Member.DeclaringType.ToQualifiedDisplayString());
    }

    [Fact]
    public void MemberProjection_MapsAnExternalInvocationWithoutParsingSource()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(nameof(ResearchProjectionProbe.InvokeExternal))));

        AssemblyMemberInvocationDestination destination =
            Assert.Single(projection.InvocationDestinations);
        Assert.Equal(nameof(Math.Abs), destination.Target.Member.Name);
        Assert.Equal(
            typeof(Math).FullName,
            destination.Target.Member.DeclaringType.ToQualifiedDisplayString());
    }

    [Fact]
    public void MemberProjection_MapsNestedInvocationsToTheirOwnCallees()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(nameof(ResearchProjectionProbe.InvokeNested))));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Dictionary<string, AnnotatedSourceNode> nodesByCallee =
            projection.InvocationDestinations.ToDictionary(
                destination => destination.Target.Member.Name,
                destination => document.Nodes[destination.NodeId]);
        Assert.Equal(2, nodesByCallee.Count);
        Assert.Contains(
            "Math.Abs(Identity(value))",
            NodeText(document, nodesByCallee[nameof(Math.Abs)]),
            StringComparison.Ordinal);
        Assert.Contains(
            "Identity(value)",
            NodeText(document, nodesByCallee[nameof(ResearchProjectionProbe.Identity)]),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MemberProjection_DoesNotConfusePropertyArgumentsWithTheirInvocation()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(
                    nameof(ResearchProjectionProbe.InvokeWithPropertyArguments))));

        AssemblyMemberInvocationDestination destination =
            Assert.Single(projection.InvocationDestinations);
        Assert.Equal(
            nameof(ResearchProjectionProbe.PropertyArgumentCallee),
            destination.Target.Member.Name);
        Assert.DoesNotContain(
            projection.InvocationDestinations,
            item => item.Target.Member.Name
                == $"get_{nameof(ResearchProjectionValue.Value)}");
    }

    [Fact]
    public void MemberProjection_RetainsRepeatedCallSiteNodes()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                InvocationRequest(nameof(ResearchProjectionProbe.InvokeRepeated))));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.True(
            projection.InvocationDestinations.Count == 2,
            $"Expected two invocation destinations; projected nodes: {string.Join(
                ", ",
                document.Nodes
                    .Where(node => node.Medium == SourceLineKind.CSharp)
                    .Select(node =>
                        $"{node.Id}:{node.Kind}:"
                        + $"{string.Join("/", node.Provenance?.IlOffsets ?? [])}"))}");
        Assert.Equal(
            2,
            projection.InvocationDestinations
                .Select(destination => destination.NodeId)
                .Distinct()
                .Count());
        Assert.All(
            projection.InvocationDestinations,
            destination => Assert.Equal(
                nameof(Math.Abs),
                destination.Target.Member.Name));
    }

    [Fact]
    public void MemberProjection_RetainsVersionDistinctInvocationTargets()
    {
        ImmutableArray<byte> image = BuildVersionedInvocationImage();
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [Participant(image, ContentIdentity(image), policy)]);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                new AssemblyContextMemberProjectionRequest(
                    "Shared.Entry",
                    "RunVersioned",
                    SourceDocument: true)
                {
                    MethodToken = MethodToken(
                        image,
                        "Shared",
                        "Entry",
                        "RunVersioned"),
                    InvocationDestinations = true,
                }));

        Assert.Equal(2, projection.InvocationDestinations.Count);
        Assert.Equal(
            [new Version(1, 0, 0, 0), new Version(2, 0, 0, 0)],
            projection.InvocationDestinations
                .Select(destination =>
                    Assert.IsType<TypeReferenceOrigin.AssemblyReference>(
                        destination.Target.Member.DeclaringType
                            .Resolution?.Origin)
                    .Assembly.Version!)
                .Order()
                .ToArray());
        Assert.Equal(
            [new Version(1, 0, 0, 0), new Version(2, 0, 0, 0)],
            projection.InvocationDestinations
                .Select(destination =>
                    Assert.IsType<AssemblyReferenceIdentity>(
                        destination.Target.OccurrenceAssemblyIdentity)
                    .Version!)
                .Order()
                .ToArray());
        Assert.All(
            projection.InvocationDestinations,
            destination => Assert.Null(
                destination.Target.DefinitionAssemblyIdentity));
    }

    [Fact]
    public void MemberProjection_RequiresSourceForInvocationDestinations()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.InvokeLocal)) with
                {
                    SourceDocument = false,
                    InvocationDestinations = true,
                }));

        Assert.Contains("source document", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberProjection_MethodTokenAddressesTheExactOverload()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        int token = typeof(ResearchProjectionProbe)
            .GetMethod(
                nameof(ResearchProjectionProbe.Overloaded),
                BindingFlags.Public | BindingFlags.Static,
                [typeof(string)])!
            .MetadataToken;

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.Overloaded)) with
                {
                    MethodToken = token,
                }));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.Contains("string", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Overloaded(int", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_ResolvesReferencesThroughTheParticipantBindingPolicy()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        AssemblyContextParticipant participant = Assert.Single(group.Participants);

        Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        // Reference resolution is a policy question, not a name match: every request the
        // projection made names the participant it came from, so a sibling is selected by the
        // group's binding snapshot rather than by a matching simple name.
        Assert.NotEmpty(policy.Requests);
        Assert.All(
            policy.Requests,
            request => Assert.Same(
                participant.Assembly.Registration,
                Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(request.Origin)
                    .Registration));
    }

    [Fact]
    public void Projection_DoesNotAcquireAPolicySelectionOutsideTheGroup()
    {
        ImmutableArray<byte> image = SelfImage();
        int outsiderOpens = 0;
        ResolvedAssemblyReference outsider = ResolvedAssemblyReference.Create(
            ContentIdentity(image),
            path: null,
            () =>
            {
                outsiderOpens++;
                return new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false);
            },
            AssemblyResolutionProvenance.Package(
                "outsider",
                "1.0.0",
                "net11.0",
                rid: null));
        var policy = new SelectingBindingPolicy(outsider);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        Assert.NotEmpty(policy.Requests);
        Assert.Equal(0, outsiderOpens);
    }

    [Fact]
    public void Execute_CarriesRejectedParticipantBesideLaterResultsInGroupOrder()
    {
        ImmutableArray<byte> image = SelfImage();
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                Participant(image, ContentIdentity(image) with { Name = "WrongIdentity" }, policy),
                Participant(image, ContentIdentity(image), policy),
            ]);

        AssemblyContextResult<ResearchViews.TypeProjectionResult> result =
            AssemblyContextTypeProjectionQuery.Execute(
                group,
                new AssemblyContextTypeProjectionRequest(
                    typeof(ResearchProjectionProbe).FullName!));

        Assert.False(result.IsComplete);
        var rejected = Assert.IsType<
            AssemblyContextEntry<ResearchViews.TypeProjectionResult>.Rejected>(
            result.Assemblies[0]);
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
        Assert.IsType<AssemblyContextEntry<ResearchViews.TypeProjectionResult>.Available>(
            result.Assemblies[1]);
    }

    [Fact]
    public void TypeProjection_ReportsAMissingTypeAsATypedParticipantFailure()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyContextResult<ResearchViews.TypeProjectionResult> result =
            AssemblyContextTypeProjectionQuery.Execute(
                group,
                new AssemblyContextTypeProjectionRequest("No.Such.Type"));

        var failed = Assert.IsType<
            AssemblyContextEntry<ResearchViews.TypeProjectionResult>.Failed>(
            Assert.Single(result.Assemblies));
        Assert.Contains("No.Such.Type", failed.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteParticipant_RefusesAParticipantOutsideTheGroup()
    {
        ImmutableArray<byte> image = SelfImage();
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        AssemblyContextParticipant outsider =
            Participant(image, ContentIdentity(image), policy);

        Assert.Throws<ArgumentException>(
            () => AssemblyContextTypeProjectionQuery.ExecuteParticipant(
                group,
                outsider,
                new AssemblyContextTypeProjectionRequest(
                    typeof(ResearchProjectionProbe).FullName!)));
    }

    [Fact]
    public void ExecuteParticipant_ProjectsOnlyTheRequestedParticipant()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyContextEntry<AssemblyMemberProjection> entry =
            AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                group,
                Assert.Single(group.Participants),
                Request(nameof(ResearchProjectionProbe.BoxInt)));

        var available =
            Assert.IsType<AssemblyContextEntry<AssemblyMemberProjection>.Available>(entry);
        Assert.IsType<AnnotatedSourceDocument>(
            available.Value.Projection.SourceDocument);
    }

    static AssemblyContextMemberProjectionRequest Request(string member) =>
        new(
            typeof(ResearchProjectionProbe).FullName!,
            member,
            SourceDocument: true);

    static AssemblyContextMemberProjectionRequest InvocationRequest(string member)
    {
        MethodInfo method = typeof(ResearchProjectionProbe).GetMethod(
            member,
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Missing invocation probe {member}.");
        return Request(member) with
        {
            MethodToken = method.MetadataToken,
            InvocationDestinations = true,
        };
    }

    static string NodeText(
        AnnotatedSourceDocument document,
        AnnotatedSourceNode node) =>
        string.Concat(
            node.Spans.Select(span =>
                document.Text.Substring(span.Start, span.Length)));

    static int MethodToken(
        ImmutableArray<byte> image,
        string typeNamespace,
        string typeName,
        string methodName)
    {
        using var reader = new PEReader(image);
        MetadataReader metadata = reader.GetMetadataReader();
        TypeDefinitionHandle typeHandle = metadata.TypeDefinitions.Single(handle =>
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            return metadata.GetString(type.Namespace) == typeNamespace
                && metadata.GetString(type.Name) == typeName;
        });
        TypeDefinition definition = metadata.GetTypeDefinition(typeHandle);
        MethodDefinitionHandle methodHandle =
            definition.GetMethods().Single(handle =>
                metadata.GetString(metadata.GetMethodDefinition(handle).Name)
                    == methodName);
        return MetadataTokens.GetToken(methodHandle);
    }

    static ImmutableArray<byte> BuildVersionedInvocationImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("VersionedInvocation.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("VersionedInvocation"),
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

        AssemblyReferenceHandle v1 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Versioned.Target"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        AssemblyReferenceHandle v2 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Versioned.Target"),
            new Version(2, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        TypeReferenceHandle targetV1 = metadata.AddTypeReference(
            v1,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddString("Api"));
        TypeReferenceHandle targetV2 = metadata.AddTypeReference(
            v2,
            metadata.GetOrAddString("Target"),
            metadata.GetOrAddString("Api"));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        MemberReferenceHandle pingV1 = metadata.AddMemberReference(
            targetV1,
            metadata.GetOrAddString("Ping"),
            signatureHandle);
        MemberReferenceHandle pingV2 = metadata.AddMemberReference(
            targetV2,
            metadata.GetOrAddString("Ping"),
            signatureHandle);

        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("Shared"),
            metadata.GetOrAddString("Entry"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var il = new BlobBuilder();
        var instructions = new InstructionEncoder(
            il,
            new ControlFlowBuilder());
        instructions.Call(pingV1);
        instructions.Call(pingV2);
        instructions.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies)
            .AddMethodBody(instructions, maxStack: 0);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("RunVersioned"),
            signatureHandle,
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return ImmutableCollectionsMarshal.AsImmutableArray(image.ToArray());
    }

    static AssemblyContextGroup ContentGroup(
        InspectionWorkspace workspace,
        IAssemblyBindingPolicy policy)
    {
        ImmutableArray<byte> image = SelfImage();
        return workspace.CreateAssemblyContextGroup(
            [Participant(image, ContentIdentity(image), policy)]);
    }

    static AssemblyContextParticipant Participant(
        ImmutableArray<byte> image,
        AssemblyReferenceIdentity identity,
        IAssemblyBindingPolicy policy)
        => new(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false),
                AssemblyResolutionProvenance.Package("probe", "1.0.0", "net11.0", rid: null)),
            policy);

    static ImmutableArray<byte> SelfImage() =>
        ImmutableCollectionsMarshal.AsImmutableArray(
            File.ReadAllBytes(
                typeof(AssemblyContextResearchProjectionQueryTests).Assembly.Location));

    static AssemblyReferenceIdentity ContentIdentity(ImmutableArray<byte> image)
    {
        using var reader = new PEReader(image);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
    }

    static TValue Available<TValue>(AssemblyContextResult<TValue> result)
        => Assert.IsType<AssemblyContextEntry<TValue>.Available>(
                Assert.Single(result.Assemblies))
            .Value;

    sealed class RecordingBindingPolicy : IAssemblyBindingPolicy
    {
        readonly List<AssemblyBindingRequest> _requests = [];

        public AssemblyBindingPolicyVersion Version { get; } = new();

        internal IReadOnlyList<AssemblyBindingRequest> Requests => _requests;

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                _requests.Add(request);
                return AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable));

            }
        }
    }

    sealed class SelectingBindingPolicy(ResolvedAssemblyReference selection)
        : IAssemblyBindingPolicy
    {
        readonly List<AssemblyBindingRequest> _requests = [];

        public AssemblyBindingPolicyVersion Version { get; } = new();

        internal IReadOnlyList<AssemblyBindingRequest> Requests => _requests;

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                _requests.Add(request);
                return AssemblyBindingSelection.Found(selection);

            }
        }
    }
}

/// <summary>Probe members the group-scoped Research projections address.</summary>
public static class ResearchProjectionProbe
{
    static object? _accessorValue;

    public static object BoxInt(int value) => value;

    public static bool GenericObjectEqualsInLocal<T>(
        T left,
        T right)
    {
        return EqualsCore(left, right);

        static bool EqualsCore(T x, T y) =>
            x!.Equals(y);
    }

    public static object BoxedValue => 42;

    public static object AccessorBoxedValue
    {
        get => _accessorValue ?? 42;
        set => _accessorValue = value ?? 43;
    }

    public static int Overloaded(int value) => value + 1;

    public static int Overloaded(string value) => value.Length;

    public static int InvokeLocal(int value) => LocalCallee(value);

    public static int LocalCallee(int value) => value + 1;

    public static int InvokeExternal(int value) => Math.Abs(value);

    public static int InvokeNested(int value) => Math.Abs(Identity(value));

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static int Identity(int value) => value;

    public static int InvokeWithPropertyArguments(
        ResearchProjectionValue first,
        ResearchProjectionValue second) =>
        PropertyArgumentCallee(first.Value, second.Value);

    public static int PropertyArgumentCallee(int first, int second) =>
        first + second;

    public static int InvokeRepeated(int value)
    {
        int first = Math.Abs(value);
        int second = Math.Abs(value);
        return first + second;
    }

    public static class Nested
    {
        public static object BoxNested(int value) => value;
    }
}

public sealed class ResearchProjectionValue
{
    public int Value { get; init; }
}
