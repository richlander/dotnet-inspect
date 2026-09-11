using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public interface AssemblyContextTypeDependencyLeaf
{
}

public interface AssemblyContextTypeDependencyRoot :
    AssemblyContextTypeDependencyLeaf
{
}

public sealed class AssemblyContextTypeDependencyQueryTests
{
    [Fact]
    public void Execute_FoundAndAllHealthyIsComplete()
    {
        var policy = new TestBindingPolicy();
        TestAssembly source =
            TestAssembly.Create("healthy", policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);

        AssemblyContextTypeDependencyResult result =
            AssemblyContextTypeDependencyQuery.Execute(
                group,
                typeof(AssemblyContextTypeDependencyRoot)
                    .FullName!);

        Assert.True(result.HasSurvivingParticipant);
        Assert.True(result.IsComplete);
        Assert.True(result.Dependency.Found);
        Assert.Contains(
            result.Dependency.Relationships,
            static relationship =>
                relationship.TargetTypeName.EndsWith(
                    nameof(AssemblyContextTypeDependencyLeaf),
                    StringComparison.Ordinal));
        var completed =
            Assert.IsType<
                AssemblyContextTypeDependencyEntry.Completed>(
                    Assert.Single(result.Participants));
        Assert.Same(
            source.Participant.Assembly.Registration,
            completed.Subject.Registration);
    }

    [Fact]
    public void Execute_HealthyMissIsCertified()
    {
        var policy = new TestBindingPolicy();
        TestAssembly source =
            TestAssembly.Create("healthy miss", policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);

        AssemblyContextTypeDependencyResult result =
            AssemblyContextTypeDependencyQuery.Execute(
                group,
                "No.Such.Type");

        Assert.True(result.HasSurvivingParticipant);
        Assert.True(result.IsComplete);
        Assert.False(result.Dependency.Found);
        Assert.IsType<
            AssemblyContextTypeDependencyEntry.Completed>(
                Assert.Single(result.Participants));
    }

    [Fact]
    public void Execute_PreservesAcquisitionRejectionBesideSurvivingGraph()
    {
        var policy = new TestBindingPolicy();
        TestAssembly rejected =
            TestAssembly.Create(
                "rejected",
                policy,
                selectedName: "WrongIdentity");
        TestAssembly healthy =
            TestAssembly.Create("healthy", policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    rejected.Participant,
                    healthy.Participant,
                ]);

        AssemblyContextTypeDependencyResult result =
            AssemblyContextTypeDependencyQuery.Execute(
                group,
                typeof(AssemblyContextTypeDependencyRoot)
                    .FullName!);

        Assert.True(result.HasSurvivingParticipant);
        Assert.True(result.Dependency.Found);
        Assert.False(result.IsComplete);
        var rejectedOutcome =
            Assert.IsType<
                AssemblyContextTypeDependencyEntry.Rejected>(
                    result.Participants[0]);
        Assert.Same(
            rejected.Participant.Assembly.Registration,
            rejectedOutcome.Subject.Registration);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejectedOutcome.Failure.Kind);
        var completed =
            Assert.IsType<
                AssemblyContextTypeDependencyEntry.Completed>(
                    result.Participants[1]);
        Assert.Same(
            healthy.Participant.Assembly.Registration,
            completed.Subject.Registration);
    }

    [Fact]
    public void Execute_MapsMetadataRejectionByRegistration()
    {
        var policy = new TestBindingPolicy();
        Type target =
            typeof(AssemblyContextTypeDependencyRoot);
        TestAssembly rejected =
            TestAssembly.CreateMalformedRelationship(
                "metadata rejected",
                policy,
                target.Namespace!,
                target.Name);
        TestAssembly healthy =
            TestAssembly.Create("healthy", policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    rejected.Participant,
                    healthy.Participant,
                ]);

        AssemblyContextTypeDependencyResult result =
            AssemblyContextTypeDependencyQuery.Execute(
                group,
                target.FullName!);

        Assert.True(result.HasSurvivingParticipant);
        Assert.True(result.Dependency.Found);
        Assert.False(result.IsComplete);
        var rejectedOutcome =
            Assert.IsType<
                AssemblyContextTypeDependencyEntry.Rejected>(
                    result.Participants[0]);
        Assert.Same(
            rejected.Participant.Assembly.Registration,
            rejectedOutcome.Subject.Registration);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejectedOutcome.Failure.Kind);
        Assert.IsType<
            AssemblyContextTypeDependencyEntry.Completed>(
                result.Participants[1]);
        Assert.Equal(1, rejected.OpenCount);
        Assert.Equal(1, healthy.OpenCount);
    }

    [Fact]
    public void Execute_AllRejectedIsUnavailable()
    {
        var policy = new TestBindingPolicy();
        TestAssembly first =
            TestAssembly.Create(
                "first",
                policy,
                selectedName: "WrongFirst");
        TestAssembly second =
            TestAssembly.Create(
                "second",
                policy,
                selectedName: "WrongSecond");
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    first.Participant,
                    second.Participant,
                ]);

        AssemblyContextTypeDependencyResult result =
            AssemblyContextTypeDependencyQuery.Execute(
                group,
                "No.Such.Type");

        Assert.False(result.HasSurvivingParticipant);
        Assert.False(result.IsComplete);
        Assert.False(result.Dependency.Found);
        Assert.Equal(
            [
                first.Participant.Assembly.Registration,
                second.Participant.Assembly.Registration,
            ],
            result.Participants.Select(
                static participant =>
                    participant.Subject.Registration));
        Assert.All(
            result.Participants,
            static participant =>
                Assert.IsType<
                    AssemblyContextTypeDependencyEntry.Rejected>(
                        participant));
    }

    [Fact]
    public void Execute_PreservesParticipantOrderAndReusesSnapshots()
    {
        var policy = new TestBindingPolicy();
        TestAssembly first =
            TestAssembly.Create("first", policy);
        TestAssembly second =
            TestAssembly.Create("second", policy);
        TestAssembly third =
            TestAssembly.Create("third", policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    first.Participant,
                    second.Participant,
                    third.Participant,
                ]);

        AssemblyContextTypeDependencyResult firstResult =
            AssemblyContextTypeDependencyQuery.Execute(
                group,
                typeof(AssemblyContextTypeDependencyRoot)
                    .FullName!);
        AssemblyContextTypeDependencyResult secondResult =
            AssemblyContextTypeDependencyQuery.Execute(
                group,
                typeof(AssemblyContextTypeDependencyRoot)
                    .FullName!);

        Assert.True(firstResult.IsComplete);
        Assert.True(secondResult.IsComplete);
        Assert.Equal(
            group.Participants.Select(
                static participant =>
                    participant.Assembly.Registration),
            firstResult.Participants.Select(
                static participant =>
                    participant.Subject.Registration));
        Assert.Equal(1, first.OpenCount);
        Assert.Equal(1, second.OpenCount);
        Assert.Equal(1, third.OpenCount);
    }

    [Fact]
    public void ExecuteParticipant_SelectsTheExactSameNamedRoot()
    {
        const string typeNamespace =
            "DotnetInspector.Queries.Tests.Duplicate";
        const string typeName = "Root";
        string fullName = $"{typeNamespace}.{typeName}";
        var policy = new TestBindingPolicy();
        TestAssembly first =
            TestAssembly.CreateWithInterface(
                "first duplicate",
                policy,
                "FirstDuplicate",
                typeNamespace,
                typeName,
                typeof(IAsyncDisposable));
        TestAssembly selected =
            TestAssembly.CreateWithInterface(
                "selected duplicate",
                policy,
                "SelectedDuplicate",
                typeNamespace,
                typeName,
                typeof(IDisposable));
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    first.Participant,
                    selected.Participant,
                ]);

        AssemblyContextTypeDependencyResult result =
            AssemblyContextTypeDependencyQuery.ExecuteParticipant(
                group,
                selected.Participant,
                fullName);

        Assert.True(result.Dependency.Found);
        Assert.Contains(
            result.Dependency.Relationships,
            relationship =>
                relationship.TargetTypeName
                    == typeof(IDisposable).FullName);
        Assert.DoesNotContain(
            result.Dependency.Relationships,
            relationship =>
                relationship.TargetTypeName
                    == typeof(IAsyncDisposable).FullName);
        Assert.Equal(
            [
                first.Participant.Assembly.Registration,
                selected.Participant.Assembly.Registration,
            ],
            result.Participants.Select(
                participant =>
                    participant.Subject.Registration));
    }

    [Fact]
    public void ExecuteParticipant_DoesNotBorrowSameNamedRoot()
    {
        const string typeNamespace =
            "DotnetInspector.Queries.Tests.Duplicate";
        const string typeName = "Root";
        string fullName = $"{typeNamespace}.{typeName}";
        var policy = new TestBindingPolicy();
        TestAssembly other =
            TestAssembly.CreateWithInterface(
                "public duplicate",
                policy,
                "PublicDuplicate",
                typeNamespace,
                typeName,
                typeof(IAsyncDisposable));
        TestAssembly selected =
            TestAssembly.CreateWithInterface(
                "non-public selected duplicate",
                policy,
                "NonPublicSelectedDuplicate",
                typeNamespace,
                typeName,
                typeof(IDisposable),
                isPublic: false);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    other.Participant,
                    selected.Participant,
                ]);

        AssemblyContextTypeDependencyResult result =
            AssemblyContextTypeDependencyQuery.ExecuteParticipant(
                group,
                selected.Participant,
                fullName);

        Assert.False(result.Dependency.Found);
        Assert.Empty(result.Dependency.Relationships);
        Assert.All(
            result.Participants,
            participant =>
                Assert.IsType<
                    AssemblyContextTypeDependencyEntry.Completed>(
                        participant));
    }

    [Fact]
    public void ExecuteParticipant_DoesNotFuzzyMatchWithinSelectedParticipant()
    {
        const string typeNamespace =
            "DotnetInspector.Queries.Tests.Fuzzy";
        const string typeName = "Widget";
        string fullName = $"{typeNamespace}.{typeName}";
        var policy = new TestBindingPolicy();
        TestAssembly selected =
            TestAssembly.CreateWithGenericCollision(
                "selected fuzzy collision",
                policy,
                "SelectedFuzzyCollision",
                typeNamespace,
                typeName,
                typeof(IDisposable),
                typeof(IAsyncDisposable));
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [selected.Participant]);

        AssemblyContextTypeDependencyResult result =
            AssemblyContextTypeDependencyQuery.ExecuteParticipant(
                group,
                selected.Participant,
                fullName);

        Assert.False(result.Dependency.Found);
        Assert.Empty(result.Dependency.Relationships);
        Assert.IsType<
            AssemblyContextTypeDependencyEntry.Completed>(
                Assert.Single(result.Participants));
    }

    sealed class TestAssembly
    {
        private int openCount;

        private TestAssembly(
            byte[] bytes,
            AssemblyContextParticipant participant)
        {
            Bytes = bytes;
            Participant = participant;
        }

        internal byte[] Bytes { get; }
        internal AssemblyContextParticipant Participant { get; }
        internal int OpenCount => Volatile.Read(ref openCount);

        internal static TestAssembly Create(
            string label,
            IAssemblyBindingPolicy policy,
            string? selectedName = null)
        {
            string path =
                typeof(AssemblyContextTypeDependencyQueryTests)
                    .Assembly.Location;
            byte[] bytes = File.ReadAllBytes(path);
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local(label));
            return Create(
                bytes,
                source.Identity,
                label,
                policy,
                selectedName);
        }

        internal static TestAssembly CreateMalformedRelationship(
            string label,
            IAssemblyBindingPolicy policy,
            string typeNamespace,
            string typeName)
        {
            byte[] bytes =
                BuildMalformedRelationshipImage(
                    typeNamespace,
                    typeName);
            using var peReader =
                new PEReader(
                    new MemoryStream(bytes, writable: false));
            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            return Create(
                bytes,
                identity,
                label,
                policy,
                selectedName: null);
        }

        internal static TestAssembly CreateWithInterface(
            string label,
            IAssemblyBindingPolicy policy,
            string assemblyName,
            string typeNamespace,
            string typeName,
            Type interfaceType,
            bool isPublic = true)
        {
            var assembly = new PersistedAssemblyBuilder(
                new AssemblyName(assemblyName),
                typeof(object).Assembly);
            ModuleBuilder module =
                assembly.DefineDynamicModule(assemblyName);
            TypeBuilder type = module.DefineType(
                $"{typeNamespace}.{typeName}",
                (isPublic
                    ? TypeAttributes.Public
                    : TypeAttributes.NotPublic)
                    | TypeAttributes.Abstract
                    | TypeAttributes.Class);
            type.AddInterfaceImplementation(interfaceType);
            type.CreateType();
            using var stream = new MemoryStream();
            assembly.Save(stream);
            byte[] bytes = stream.ToArray();
            using var peReader =
                new PEReader(
                    new MemoryStream(bytes, writable: false));
            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            return Create(
                bytes,
                identity,
                label,
                policy,
                selectedName: null);
        }

        internal static TestAssembly CreateWithGenericCollision(
            string label,
            IAssemblyBindingPolicy policy,
            string assemblyName,
            string typeNamespace,
            string typeName,
            Type selectedInterface,
            Type fuzzyInterface)
        {
            var assembly = new PersistedAssemblyBuilder(
                new AssemblyName(assemblyName),
                typeof(object).Assembly);
            ModuleBuilder module =
                assembly.DefineDynamicModule(assemblyName);
            TypeBuilder selected = module.DefineType(
                $"{typeNamespace}.{typeName}",
                TypeAttributes.NotPublic
                    | TypeAttributes.Abstract
                    | TypeAttributes.Class);
            selected.AddInterfaceImplementation(selectedInterface);
            selected.CreateType();
            TypeBuilder fuzzy = module.DefineType(
                $"{typeNamespace}.{typeName}`1",
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Class);
            fuzzy.DefineGenericParameters("T");
            fuzzy.AddInterfaceImplementation(fuzzyInterface);
            fuzzy.CreateType();
            using var stream = new MemoryStream();
            assembly.Save(stream);
            byte[] bytes = stream.ToArray();
            using var peReader =
                new PEReader(
                    new MemoryStream(bytes, writable: false));
            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    peReader.GetMetadataReader());
            return Create(
                bytes,
                identity,
                label,
                policy,
                selectedName: null);
        }

        static TestAssembly Create(
            byte[] bytes,
            AssemblyReferenceIdentity identity,
            string label,
            IAssemblyBindingPolicy policy,
            string? selectedName)
        {
            TestAssembly? testAssembly = null;
            ResolvedAssemblyReference descriptor =
                ResolvedAssemblyReference.Create(
                    identity with
                    {
                        Name = selectedName
                            ?? identity.Name,
                    },
                    path: null,
                    () =>
                    {
                        Interlocked.Increment(
                            ref testAssembly!.openCount);
                        return new MemoryStream(
                            testAssembly.Bytes,
                            writable: false);
                    },
                    AssemblyResolutionProvenance.Local(label));
            var participant =
                new AssemblyContextParticipant(
                    descriptor,
                    policy);
            testAssembly =
                new TestAssembly(bytes, participant);
            return testAssembly;
        }

        static byte[] BuildMalformedRelationshipImage(
            string typeNamespace,
            string typeName)
        {
            var metadata = new MetadataBuilder();
            metadata.AddAssembly(
                metadata.GetOrAddString(
                    "MalformedRelationship"),
                new Version(1, 0, 0, 0),
                default,
                default,
                0,
                default);
            metadata.AddModule(
                0,
                metadata.GetOrAddString(
                    "MalformedRelationship.dll"),
                metadata.GetOrAddGuid(Guid.NewGuid()),
                default,
                default);
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                metadata.GetOrAddString(typeNamespace),
                metadata.GetOrAddString(typeName),
                MetadataTokens.TypeReferenceHandle(999),
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));

            var pe = new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                new BlobBuilder(),
                flags: CorFlags.ILOnly);
            var image = new BlobBuilder();
            pe.Serialize(image);
            return image.ToArray();
        }
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind
                            .CandidateUnavailable)));
    }
}
