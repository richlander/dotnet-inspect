using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.Metadata.StateMachineFixtures;

using StateMachineFixtureMethods =
    ILInspector.Metadata.StateMachineFixtures.StateMachineFixtures;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Gates <see cref="StateMachineSourceResolver"/> taking kickoff,
/// state-machine type, and execution identity from the
/// <see cref="StateMachineRelationshipIndex"/> certificate (#10235).
/// </summary>
public sealed class StateMachineSourceResolverCertificateTests
{
    static string FixturePath =>
        typeof(ExplicitGenericStateMachines).Assembly.Location;

    /// <summary>
    /// An explicit implementation of a two-argument generic interface puts an
    /// escaped comma in the serialized state-machine name. Both the
    /// assembly-wide and targeted forward lookups resolve it, and generated
    /// expansion attributes its execution body to the kickoff.
    /// </summary>
    [Theory]
    [InlineData("GetAsync")]
    [InlineData("get_Items")]
    public void EscapedCommaStateMachine_ResolvesKickoffToExecution(
        string member)
    {
        byte[] image = File.ReadAllBytes(FixturePath);
        using var subject = new Subject(image);
        (MethodDefinitionHandle kickoff,
            TypeDefinitionHandle stateMachine,
            MethodDefinitionHandle moveNext) =
                subject.ExplicitGenericStateMachine(member);

        subject.AssertForward(kickoff, stateMachine, moveNext);
    }

    /// <summary>
    /// The classic escaped-comma state machine also resolves from its
    /// <c>MoveNext</c> back to the kickoff.
    /// </summary>
    [Fact]
    public void EscapedCommaClassicAsync_ResolvesExecutionToKickoff()
    {
        byte[] image = File.ReadAllBytes(FixturePath);
        using var subject = new Subject(image);
        (MethodDefinitionHandle kickoff, _, MethodDefinitionHandle moveNext) =
            subject.ExplicitGenericStateMachine("GetAsync");

        subject.AssertReverse(kickoff, moveNext);
    }

    /// <summary>
    /// A classic async certificate whose <c>SetStateMachine</c> role is
    /// <c>AbsentFromArtifact</c> still resolves in both directions.
    /// </summary>
    [Theory]
    [InlineData(nameof(StateMachineFixtureMethods.ClassicAsync))]
    [InlineData("GetAsync")]
    public void ClassicAsyncWithoutSetStateMachine_ResolvesBothDirections(
        string member)
    {
        byte[] image = WithoutSetStateMachine(
            File.ReadAllBytes(FixturePath));
        using var subject = new Subject(image);
        (MethodDefinitionHandle kickoff,
            TypeDefinitionHandle stateMachine,
            MethodDefinitionHandle moveNext) =
                member == "GetAsync"
                    ? subject.ExplicitGenericStateMachine(member)
                    : subject.StaticFixtureStateMachine(member);

        var certificate =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                StateMachineRelationshipIndex.Create(subject.Reader)
                    .GetByKickoff(kickoff));
        Assert.Equal(
            StateMachineClaimKind.ClassicAsync,
            certificate.Relationship.Kind);
        Assert.IsType<StateMachineRoleDisposition.AbsentFromArtifact>(
            certificate.Relationship.GetRole(
                StateMachineMethodRole.SetStateMachine));

        subject.AssertForward(kickoff, stateMachine, moveNext);
        subject.AssertReverse(kickoff, moveNext);
    }

    /// <summary>
    /// Full trim removes <c>IAsyncStateMachine.SetStateMachine</c> from async
    /// iterators too. The async iterator certificate then reports the role as
    /// <c>AbsentFromArtifact</c>, and the resolver still resolves both
    /// directions.
    /// </summary>
    [Fact]
    public void AsyncIteratorWithoutSetStateMachine_ResolvesBothDirections()
    {
        byte[] image = WithoutSetStateMachine(
            File.ReadAllBytes(FixturePath));
        using var subject = new Subject(image);
        (MethodDefinitionHandle kickoff,
            TypeDefinitionHandle stateMachine,
            MethodDefinitionHandle moveNext) =
                subject.StaticFixtureStateMachine(
                    nameof(StateMachineFixtureMethods.AsyncIterator));

        var certificate =
            Assert.IsType<StateMachineRelationshipResult.Resolved>(
                StateMachineRelationshipIndex.Create(subject.Reader)
                    .GetByKickoff(kickoff));
        Assert.Equal(
            StateMachineClaimKind.AsyncIterator,
            certificate.Relationship.Kind);
        Assert.IsType<StateMachineRoleDisposition.AbsentFromArtifact>(
            certificate.Relationship.GetRole(
                StateMachineMethodRole.SetStateMachine));

        subject.AssertForward(kickoff, stateMachine, moveNext);
        subject.AssertReverse(kickoff, moveNext);
    }

    [Fact]
    public void ExactOrdinaryMethod_DoesNotBuildWholeModuleCertificate()
    {
        byte[] image = File.ReadAllBytes(FixturePath);
        using var subject = new Subject(image);
        TypeDefinitionHandle owner =
            subject.FindType("StateMachineFixtures");
        MethodDefinitionHandle method =
            subject.FindMethod(
                owner,
                name => name
                    == nameof(StateMachineFixtureMethods.Synchronous));

        subject.AssertExactOrdinary(method);
    }

    /// <summary>
    /// Renames every <c>#Strings</c> entry that ends in
    /// <c>SetStateMachine</c>. The state machines then have neither an
    /// explicit nor an implicit <c>SetStateMachine</c> candidate, which is the
    /// ordinary full-trim shape.
    /// </summary>
    static byte[] WithoutSetStateMachine(byte[] image)
    {
        int heapStart;
        int heapSize;
        using (var peReader = new PEReader(
            new MemoryStream(image, writable: false)))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            heapStart = peReader.PEHeaders.MetadataStartOffset
                + reader.GetHeapMetadataOffset(HeapIndex.String);
            heapSize = reader.GetHeapSize(HeapIndex.String);
        }

        byte[] patched = (byte[])image.Clone();
        Span<byte> heap = patched.AsSpan(heapStart, heapSize);
        ReadOnlySpan<byte> name = "SetStateMachine\0"u8;
        int renamed = 0;
        int offset = 0;
        while (heap[offset..].IndexOf(name) is var found and >= 0)
        {
            offset += found + name.Length;
            heap[offset - 2] = (byte)'X';
            renamed++;
        }
        Assert.True(renamed > 0);
        return patched;
    }

    sealed class Subject : IDisposable
    {
        readonly PEReader _peReader;
        readonly DeclaredSourceAttribution _attribution;

        internal Subject(byte[] image)
        {
            _peReader = new PEReader(
                ImmutableArray.Create(image));
            Reader = _peReader.GetMetadataReader();
            _attribution = new DeclaredSourceAttribution(
                FixturePath,
                Reader,
                _peReader,
                stateMachineExecutionMethodsBuilt:
                    () => CertificateBuilds++,
                generatedExpansionWork:
                    new MethodDefinitionGeneratedExpansionWork(
                        MethodDefinitionGeneratedExpansionLimits.Default));
        }

        internal MetadataReader Reader { get; }

        internal int CertificateBuilds { get; private set; }

        StateMachineSourceResolver Resolver =>
            _attribution.StateMachineSourceResolver;

        public void Dispose() => _peReader.Dispose();

        internal (
            MethodDefinitionHandle Kickoff,
            TypeDefinitionHandle StateMachine,
            MethodDefinitionHandle MoveNext)
            ExplicitGenericStateMachine(string member)
        {
            TypeDefinitionHandle owner =
                FindType(nameof(ExplicitGenericStateMachines));
            MethodDefinitionHandle kickoff = FindMethod(
                owner,
                name => name.EndsWith(
                    ">." + member,
                    StringComparison.Ordinal));
            TypeDefinitionHandle stateMachine = FindNestedType(
                owner,
                name => name.Contains(
                    ">-" + member + ">",
                    StringComparison.Ordinal));
            Assert.Contains(
                ',',
                Reader.GetString(
                    Reader.GetTypeDefinition(stateMachine).Name));
            return (
                kickoff,
                stateMachine,
                FindMethod(stateMachine, name => name == "MoveNext"));
        }

        internal (
            MethodDefinitionHandle Kickoff,
            TypeDefinitionHandle StateMachine,
            MethodDefinitionHandle MoveNext)
            StaticFixtureStateMachine(string member)
        {
            TypeDefinitionHandle owner =
                FindType("StateMachineFixtures");
            MethodDefinitionHandle kickoff =
                FindMethod(owner, name => name == member);
            TypeDefinitionHandle stateMachine = FindNestedType(
                owner,
                name => name.StartsWith(
                    "<" + member + ">d__",
                    StringComparison.Ordinal));
            return (
                kickoff,
                stateMachine,
                FindMethod(stateMachine, name => name == "MoveNext"));
        }

        internal void AssertForward(
            MethodDefinitionHandle kickoff,
            TypeDefinitionHandle stateMachine,
            MethodDefinitionHandle moveNext)
        {
            MethodDefinition kickoffDefinition =
                Reader.GetMethodDefinition(kickoff);

            // The kickoff is itself the declared source: no other source.
            Assert.Null(
                Resolver.ResolveSourceMethod(
                    CreateIdentity(kickoff, kickoffDefinition),
                    kickoffDefinition,
                    typeSourceGenerated: false));

            Assert.True(
                Resolver.TryResolveStateMachineExecutionMethod(
                    kickoff,
                    kickoffDefinition,
                    out MethodDefinitionHandle wide));
            Assert.Equal(moveNext, wide);

            Assert.True(
                Resolver.TryResolveTargetedStateMachineExecutionMethod(
                    kickoff,
                    kickoffDefinition,
                    new MethodDefinitionGeneratedExpansionWork(
                        MethodDefinitionGeneratedExpansionLimits.Default),
                    out MethodDefinitionHandle targeted,
                    out TypeDefinitionHandle targetedType));
            Assert.Equal(moveNext, targeted);
            Assert.Equal(stateMachine, targetedType);

            MethodDefinitionGeneratedExpansionResult expansion =
                _attribution.ExpandGeneratedExecutionBodies([kickoff]);
            Assert.Contains(
                expansion.Coverage.Origins,
                origin => origin.Method == moveNext
                    && origin.Kind
                        == MethodDefinitionGeneratedExpansionOriginKind
                            .StateMachineExecutionBody);
        }

        internal void AssertReverse(
            MethodDefinitionHandle kickoff,
            MethodDefinitionHandle moveNext)
        {
            MethodDefinition moveNextDefinition =
                Reader.GetMethodDefinition(moveNext);
            MethodIdentity moveNextIdentity =
                CreateIdentity(moveNext, moveNextDefinition);
            int certificateBuildsBeforeExact =
                CertificateBuilds;

            var exactWork =
                new MethodDefinitionGeneratedExpansionWork(
                    MethodDefinitionGeneratedExpansionLimits.Default);
            Assert.Equal(
                AsyncSourceResolution.Resolved,
                Resolver.ResolveExactSourceOwnership(
                    moveNext,
                    moveNextIdentity,
                    moveNextDefinition,
                    typeSourceGenerated: false,
                    exactWork,
                    out MethodIdentity? exactSource));
            Assert.Equal(
                MetadataTokens.GetToken(kickoff),
                exactSource!.MetadataToken);
            Assert.Equal(
                certificateBuildsBeforeExact,
                CertificateBuilds);
            Assert.True(
                exactWork.Build()
                    .CandidateDefinitionsExamined.Count > 0);

            Assert.Equal(
                AsyncSourceResolution.Resolved,
                Resolver.ResolveSourceOwnership(
                    moveNextIdentity,
                    moveNextDefinition,
                    typeSourceGenerated: false,
                    out MethodIdentity? source));
            Assert.Equal(
                MetadataTokens.GetToken(kickoff),
                source!.MetadataToken);
            Assert.Equal(
                MetadataTokens.GetToken(kickoff),
                Resolver.ResolveSourceMethod(
                    moveNextIdentity,
                    moveNextDefinition,
                    typeSourceGenerated: false)!.MetadataToken);
            Assert.Equal(
                MetadataTokens.GetToken(kickoff),
                Resolver.ExecutionSourceMethodsByMoveNextToken()[
                    MetadataTokens.GetToken(moveNext)].MetadataToken);
            Assert.Equal(1, CertificateBuilds);
        }

        internal void AssertExactOrdinary(
            MethodDefinitionHandle method)
        {
            MethodDefinition definition =
                Reader.GetMethodDefinition(method);
            var work =
                new MethodDefinitionGeneratedExpansionWork(
                    MethodDefinitionGeneratedExpansionLimits.Default);

            Assert.Equal(
                AsyncSourceResolution.None,
                Resolver.ResolveExactSourceOwnership(
                    method,
                    CreateIdentity(method, definition),
                    definition,
                    typeSourceGenerated: false,
                    work,
                    out MethodIdentity? source));
            Assert.Null(source);
            Assert.Equal(0, CertificateBuilds);
            Assert.Equal(
                0,
                work.Build()
                    .CandidateDefinitionsExamined.Count);
        }

        MethodIdentity CreateIdentity(
            MethodDefinitionHandle method,
            MethodDefinition definition)
        {
            TypeDefinitionHandle owner = definition.GetDeclaringType();
            return _attribution.PrimaryMetadataResolver.CreateMethodIdentity(
                owner,
                method,
                definition,
                _attribution.PrimaryMetadataResolver.CreateScope(
                    Reader.GetTypeDefinition(owner),
                    definition));
        }

        internal TypeDefinitionHandle FindType(string name) =>
            Reader.TypeDefinitions.Single(handle =>
            {
                TypeDefinition type = Reader.GetTypeDefinition(handle);
                return type.GetDeclaringType().IsNil
                    && Reader.StringComparer.Equals(type.Name, name)
                    && Reader.StringComparer.Equals(
                        type.Namespace,
                        typeof(ExplicitGenericStateMachines).Namespace!);
            });

        TypeDefinitionHandle FindNestedType(
            TypeDefinitionHandle owner,
            Func<string, bool> match) =>
            Reader.GetTypeDefinition(owner).GetNestedTypes().Single(
                handle => match(Reader.GetString(
                    Reader.GetTypeDefinition(handle).Name)));

        internal MethodDefinitionHandle FindMethod(
            TypeDefinitionHandle owner,
            Func<string, bool> match) =>
            Reader.GetTypeDefinition(owner).GetMethods().Single(
                handle => match(Reader.GetString(
                    Reader.GetMethodDefinition(handle).Name)));
    }
}
