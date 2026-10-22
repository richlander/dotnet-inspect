using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Analysis.UnsafeMemberFindingFixtures;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

// Gates for the all-definition lowering of the declared-source relation:
// docs/design/method-query-source.md#declared-source-relation.
public sealed class DeclaredSourceRelationTests
{
    static string PathOf(string fixture) =>
        fixture switch
        {
            "new" => FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            "legacy" => FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath(),
            "analysis" => typeof(ClassicAsyncUnsafeFixtures).Assembly.Location,
            "iterators" => FixtureCatalog.AnalysisIterators.AssemblyPath(),
            _ => typeof(System.Text.Json.JsonSerializer).Assembly.Location,
        };

    // Both lowerings publish the same outcomes for the same declared owners:
    // the all-definition relation equals the union of the exact-type
    // expansions over the image's user types.
    [Theory]
    [InlineData("new")]
    [InlineData("legacy")]
    [InlineData("analysis")]
    [InlineData("iterators")]
    [InlineData("System.Text.Json")]
    public void AllDefinitionRelationEqualsTheExactTypeUnion(string fixture)
    {
        string path = PathOf(fixture);
        using AssemblyInspectionSession session = OpenSession(path);

        RelationRead all = Run(
            session,
            path,
            MethodDefinitionSourceBreadth.AllDefinitions
                .IncludeGeneratedExecutionBodies(
                    roles: MethodDefinitionGeneratedBodyRoles.All));
        MethodDefinitionGeneratedExpansionCoverage relation =
            all.Coverage.GeneratedExpansion;
        Assert.False(relation.IsBounded);
        Assert.NotEmpty(relation.Origins);

        var origins = new HashSet<MethodDefinitionGeneratedExpansionOrigin>();
        var outside = new HashSet<MethodDefinitionGeneratedExpansionOrigin>();
        var unauthenticated = new HashSet<MethodDefinitionHandle>();
        foreach (TypeDefinitionHandle type in UserTypes(path))
        {
            MethodDefinitionGeneratedExpansionCoverage exact = Run(
                    session,
                    path,
                    MethodDefinitionSourceBreadth.ExactTypes(type)
                        .IncludeGeneratedExecutionBodies(
                            roles: MethodDefinitionGeneratedBodyRoles.All))
                .Coverage.GeneratedExpansion;
            Assert.False(exact.IsBounded);
            origins.UnionWith(exact.Origins);
            outside.UnionWith(exact.AttributedOutsideRoles);
            unauthenticated.UnionWith(exact.UnauthenticatedCandidates);
        }

        Assert.Equal(Ordered(origins), relation.Origins);
        Assert.Equal(Ordered(outside), relation.AttributedOutsideRoles);
        Assert.Equal(
            unauthenticated.OrderBy(static handle => MetadataTokens.GetRowNumber(handle)),
            relation.UnauthenticatedCandidates);

        // Every unit's completion-time outcome is the relation's.
        var expected = new MethodDefinitionDeclaredSources(relation);
        Assert.All(
            all.Sources,
            entry => Assert.Equal(
                expected.Of(MetadataTokens.MethodDefinitionHandle(entry.Key)),
                entry.Value));
    }

    // The relation reads the operands traversal recorded: every body is
    // scanned once from the traversal, and settling probes none of them
    // again. A producer that acquires no body leaves the relation to read
    // each body itself, and the receipt says so.
    [Fact]
    public void AllDefinitionRelationReadsTraversalOperandsWithoutProbes()
    {
        string path = PathOf("iterators");
        using AssemblyInspectionSession session = OpenSession(path);
        RelationRead read = Run(
            session,
            path,
            MethodDefinitionSourceBreadth.AllDefinitions
                .IncludeGeneratedExecutionBodies(
                    roles: MethodDefinitionGeneratedBodyRoles.All));
        MethodDefinitionGeneratedExpansionCoverage relation =
            read.Coverage.GeneratedExpansion;

        int bodies = ManagedBodies(path, out long encodedIlBytes, out _);
        Assert.Equal(bodies, relation.TraversalReferenceBodies);
        Assert.Equal(bodies, relation.TraversalReferenceBodiesAcquired);
        Assert.Equal(encodedIlBytes, relation.TraversalReferenceEncodedIlBytes);
        Assert.Equal(0, relation.ProbeBodiesAttempted.Count);
        Assert.Equal(0, relation.ProbeEncodedIlBytes);

        RelationRead exact = Run(
            session,
            path,
            MethodDefinitionSourceBreadth.ExactTypes(UserTypes(path))
                .IncludeGeneratedExecutionBodies(
                    roles: MethodDefinitionGeneratedBodyRoles.All));
        Assert.Equal(0, exact.Coverage.GeneratedExpansion.TraversalReferenceBodies);
        Assert.NotEqual(0, exact.Coverage.GeneratedExpansion.ProbeBodiesAttempted.Count);
    }

    // A producer that acquires the body leaves the relation to scan it
    // without a second read: only bodies no producer acquired are the
    // relation's own reads.
    [Fact]
    public void AllDefinitionRelationScansTheBodyAProducerAcquired()
    {
        string path = PathOf("iterators");
        MethodDefinitionSourceRequest<int> request =
            MethodDefinitionSourceRequest<int>.Create(
                Plan(BodyReader.Instance),
                BodyReader.Instance,
                MethodDefinitionSourceBreadth.AllDefinitions
                    .IncludeGeneratedExecutionBodies(
                        roles: MethodDefinitionGeneratedBodyRoles.All));
        var operation = AssemblyAnalysisOperation<int>.Create(path, request);
        using AssemblyInspectionSession session = OpenSession(path);
        AssemblyAnalysisExecution<int> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<int>.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access => AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access)))
                .Execution;
        Assert.True(execution.ResultOf(BodyReader.Instance).HasValue);
        MethodDefinitionGeneratedExpansionCoverage relation =
            execution.SourceReceipt.Coverage.GeneratedExpansion;

        int bodies = ManagedBodies(path, out _, out int unacquirable);
        Assert.Equal(bodies, relation.TraversalReferenceBodies);
        Assert.Equal(unacquirable, relation.TraversalReferenceBodiesAcquired);
        Assert.Equal(0, relation.ProbeBodiesAttempted.Count);
    }

    // A producer whose visits stopped at a settled terminal still reads the
    // relation at completion, so a bound reached at settle bounds it too: it
    // answered only over the retained prefix.
    [Theory]
    [InlineData(ProducerTerminal.Rows, 1)]
    [InlineData(ProducerTerminal.Exists, null)]
    [InlineData(ProducerTerminal.Rows, null)]
    public void AllDefinitionRelationBoundReachesAProducerThatStoppedEarly(
        ProducerTerminal terminal,
        int? rowLimit)
    {
        string path = PathOf("iterators");
        var limits = new MethodDefinitionGeneratedExpansionLimits(
            maximumCandidateDefinitions: 0xFFFFFF,
            maximumGeneratedMethods: 0xFFFFFF,
            maximumProbeBodies: 0xFFFFFF,
            maximumProbeEncodedIlBytes: 1L << 30,
            maximumRelationshipNodes: 1);
        WorkDescription description =
            Assert.IsType<ProducerPlanResult.Accepted>(
                    ProducerPlanner.Plan(
                    [
                        new ProducerRequest(
                            StoppingReader.Instance,
                            terminal,
                            rowLimit),
                    ]))
                .Description;
        MethodDefinitionSourceRequest<bool> request =
            MethodDefinitionSourceRequest<bool>.Create(
                description,
                StoppingReader.Instance,
                MethodDefinitionSourceBreadth.AllDefinitions
                    .IncludeGeneratedExecutionBodies(
                        limits,
                        MethodDefinitionGeneratedBodyRoles.All));
        var operation = AssemblyAnalysisOperation<bool>.Create(path, request);
        using AssemblyInspectionSession session = OpenSession(path);
        AssemblyAnalysisExecution<bool> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<bool>.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access => AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access)))
                .Execution;

        Assert.True(execution.SourceReceipt.Coverage.GeneratedExpansion.IsBounded);
        ProducerResult<bool> result = execution.ResultOf(StoppingReader.Instance);
        Assert.Equal(ProducerOutcome.SourceBounded, result.Outcome);
        Assert.False(result.HasValue);
    }

    // Over all-definition breadth the relation settles at source completion,
    // so a visit that reads it fails the request's contract.
    [Fact]
    public void AllDefinitionRelationIsNotReadableDuringAVisit()
    {
        string path = PathOf("iterators");
        MethodDefinitionSourceRequest<int> request =
            MethodDefinitionSourceRequest<int>.Create(
                Plan(VisitReader.Instance),
                VisitReader.Instance,
                MethodDefinitionSourceBreadth.AllDefinitions
                    .IncludeGeneratedExecutionBodies());
        var operation = AssemblyAnalysisOperation<int>.Create(path, request);
        using AssemblyInspectionSession session = OpenSession(path);

        var exception = Assert.ThrowsAny<Exception>(
            () => session.SnapshotOperation(
                operation,
                access => AssemblyAnalysisService.Instance.Execute(
                    operation,
                    access)));
        Assert.Contains(
            "settles at source completion",
            (exception as ProducerContractException ?? exception.InnerException)?.Message,
            StringComparison.Ordinal);
    }

    // All-definition breadth already visits every body, so the relation
    // takes no unauthenticated candidates, and it defaults to bounds sized to
    // the metadata tables.
    [Fact]
    public void AllDefinitionRelationDeclaresNoCandidatesAndModuleBounds()
    {
        Assert.Throws<ArgumentException>(
            () => MethodDefinitionSourceBreadth.AllDefinitions
                .IncludeGeneratedExecutionBodies(
                    includeUnauthenticatedCandidates: true));
        Assert.Same(
            MethodDefinitionGeneratedExpansionLimits.AllDefinitions,
            MethodDefinitionSourceBreadth.AllDefinitions
                .IncludeGeneratedExecutionBodies()
                .GeneratedExpansionLimits);
    }

    static MethodDefinitionSourceRequest<RelationRead> Request(
        MethodDefinitionSourceBreadth breadth) =>
        MethodDefinitionSourceRequest<RelationRead>.Create(
            Plan(RelationReader.Instance),
            RelationReader.Instance,
            breadth);

    static RelationRead Run(
        AssemblyInspectionSession session,
        string path,
        MethodDefinitionSourceBreadth breadth)
    {
        var operation = AssemblyAnalysisOperation<RelationRead>.Create(
            path,
            Request(breadth));
        AssemblyAnalysisExecution<RelationRead> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<RelationRead>.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access => AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access)))
                .Execution;
        ProducerResult<RelationRead> result =
            execution.ResultOf(RelationReader.Instance);
        Assert.True(result.HasValue, result.Failure?.Message);
        return result.Value! with { Coverage = execution.SourceReceipt.Coverage };
    }

    static WorkDescription Plan<TResult>(ProducerDeclaration<TResult> producer) =>
        Assert.IsType<ProducerPlanResult.Accepted>(
                ProducerPlanner.Plan([new ProducerRequest(producer)]))
            .Description;

    static AssemblyInspectionSession OpenSession(string path) =>
        AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(File.ReadAllBytes(path), writable: false));

    static ImmutableArray<TypeDefinitionHandle> UserTypes(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var attribution = new DeclaredSourceAttribution(path, reader, pe);
        return
        [
            .. reader.TypeDefinitions.Where(handle =>
                !attribution.PrimaryMetadataResolver
                    .IsCompilerGeneratedTypeOrEnclosing(handle)),
        ];
    }

    // Bodies with an RVA, their encoded IL bytes, and those a producer's body
    // layer cannot acquire because they are not managed IL.
    static int ManagedBodies(
        string path,
        out long encodedIlBytes,
        out int unacquirable)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        int bodies = 0;
        encodedIlBytes = 0;
        unacquirable = 0;
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            bodies++;
            if (!MethodDefinitionShapes.HasManagedIlBody(method.ImplAttributes))
                unacquirable++;
            encodedIlBytes += pe.GetMethodBody(method.RelativeVirtualAddress)
                .GetILReader().Length;
        }
        return bodies;
    }

    static ImmutableArray<MethodDefinitionGeneratedExpansionOrigin> Ordered(
        IEnumerable<MethodDefinitionGeneratedExpansionOrigin> origins) =>
        [
            .. origins
                .OrderBy(static origin => MetadataTokens.GetRowNumber(origin.Method))
                .ThenBy(static origin => origin.Kind)
                .ThenBy(static origin => MetadataTokens.GetRowNumber(origin.DeclaredOwner))
                .ThenBy(static origin => origin.Role),
        ];

    sealed record RelationRead(
        ImmutableDictionary<int, MethodDefinitionDeclaredSource> Sources)
    {
        public MethodDefinitionSourceCoverage Coverage { get; init; } = null!;
    }

    // Reads every visited unit's outcome at completion, and no body.
    sealed class RelationReader
        : MethodDefinitionProducer<int, List<int>, RelationRead>
    {
        RelationReader()
            : base(
                "Test.DeclaredSourceRelation.CompletionReader",
                version: 1,
                tier: 1,
                MethodDefinitionLayers.Declaration)
        {
        }

        public static RelationReader Instance { get; } = new();

        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token;

        internal override List<int> Seed() => [];

        internal override List<int> Accumulate(List<int> accumulator, int fact)
        {
            accumulator.Add(fact);
            return accumulator;
        }

        internal override RelationRead Complete(
            List<int> accumulator,
            MethodDefinitionCompletionView completion)
        {
            var sources =
                ImmutableDictionary.CreateBuilder<int, MethodDefinitionDeclaredSource>();
            foreach (int token in accumulator)
            {
                sources[token] = completion.DeclaredSourceOf(
                    MetadataTokens.MethodDefinitionHandle(token & 0x00FFFFFF));
            }
            return new(sources.ToImmutable());
        }
    }

    // Acquires every managed body, and reads no relation.
    sealed class BodyReader
        : MethodDefinitionProducer<int, int, int>
    {
        BodyReader()
            : base(
                "Test.DeclaredSourceRelation.BodyReader",
                version: 1,
                tier: 1,
                MethodDefinitionLayers.Declaration | MethodDefinitionLayers.Body)
        {
        }

        public static BodyReader Instance { get; } = new();

        internal override int Visit(scoped MethodDefinitionView view) =>
            view.HasManagedBody ? view.GetBody().Size : 0;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }

    // Settles on its first fact, so its visits stop early, and reads the
    // relation at completion.
    sealed class StoppingReader
        : MethodDefinitionProducer<int, int, bool>
    {
        StoppingReader()
            : base(
                "Test.DeclaredSourceRelation.StoppingReader",
                version: 1,
                tier: 1,
                MethodDefinitionLayers.Declaration)
        {
        }

        public static StoppingReader Instance { get; } = new();

        internal override int Visit(scoped MethodDefinitionView view) =>
            view.Token;

        internal override bool Settles(int fact) => true;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            fact;

        internal override bool Complete(
            int accumulator,
            MethodDefinitionCompletionView completion)
        {
            _ = completion.DeclaredSourceOf(
                MetadataTokens.MethodDefinitionHandle(accumulator & 0x00FFFFFF));
            return completion.IsSourceBounded;
        }
    }

    sealed class VisitReader
        : MethodDefinitionProducer<int, int, int>
    {
        VisitReader()
            : base(
                "Test.DeclaredSourceRelation.VisitReader",
                version: 1,
                tier: 1,
                MethodDefinitionLayers.Declaration)
        {
        }

        public static VisitReader Instance { get; } = new();

        internal override int Visit(scoped MethodDefinitionView view) =>
            view.DeclaredSource is MethodDefinitionDeclaredSource.Direct ? 0 : 1;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            accumulator;
    }
}
