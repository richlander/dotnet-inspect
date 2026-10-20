using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.Planning;
using ILInspector.Analysis.UnsafeMemberFindingFixtures;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

// Gates for the Type-scope census on Assembly Analysis Operation:
// docs/design/unsafe-member-findings.md#execution.
public sealed partial class UnsafeMemberFindingsTests
{
    static string AnalysisFixturePath =>
        typeof(ClassicAsyncUnsafeFixtures).Assembly.Location;

    static string IteratorFixturePath =>
        FixtureCatalog.AnalysisIterators.AssemblyPath();

    // Type and Library scope agree on every finding and limitation of the
    // selected type: both read the same declared-source relation, so a
    // synchronous iterator's bodies fold into the iterator method at both.
    [Theory]
    [InlineData("new")]
    [InlineData("legacy")]
    [InlineData("analysis")]
    [InlineData("iterators")]
    public void TypeScopeAgreesWithTheLibraryCensusOnEveryFinding(string fixture)
    {
        string path = fixture switch
        {
            "new" => UnsafeFixturePath(legacy: false),
            "legacy" => UnsafeFixturePath(legacy: true),
            "analysis" => AnalysisFixturePath,
            _ => IteratorFixturePath,
        };
        UnsafeMemberCensus library = LibraryCensus(path);

        foreach ((TypeDefinitionHandle type, ImmutableHashSet<int> methods)
            in SourceTypes(path))
        {
            var scoped = Assert.IsType<UnsafeMemberCensusResult.Available>(
                TypeScopeCensus(path, [type]));
            MethodDefinitionSourceCoverage coverage =
                scoped.SourceReceipt.Coverage;
            bool Selected(int token) =>
                coverage.MethodsSelected.Contains(
                    MetadataTokens.MethodDefinitionHandle(token & 0x00FFFFFF));

            Assert.Equal(
                library.Members
                    .Where(finding => methods.Contains(finding.Member.MetadataToken))
                    .Select(FindingKey),
                scoped.Census.Members.Select(FindingKey));
            Assert.Equal(
                library.Limitations
                    .Where(limitation =>
                        limitation.BodyToken is int body
                        && Selected(body)),
                scoped.Census.Limitations);
        }

        if (fixture is "new" or "legacy" or "iterators")
        {
            Assert.Contains(
                library.Members,
                finding => finding.Evidence.Any(evidence =>
                    evidence.Body.DeclaringType.Name.Contains(
                        ">d__",
                        StringComparison.Ordinal)));
        }
    }

    // Issue #9755: each shape's generated body folds into the member a reader
    // wrote, with its physical body as provenance.
    [Theory]
    [InlineData("TypeScopeCapturingLambda", "Read", "<Read>b__0")]
    [InlineData("TypeScopeStaticLambda", "Read", "<Read>b__0_0")]
    [InlineData("TypeScopeLocalFunction", "Read", "<Read>g__Local|0_0")]
    [InlineData("TypeScopeAsyncMethod", "ReadAsync", "MoveNext")]
    [InlineData("TypeScopeDirect", "Read", "Read")]
    public void TypeScopeFoldsGeneratedBodiesIntoTheirDeclaredMember(
        string type,
        string member,
        string body)
    {
        UnsafeMemberCensus census = Assert.IsType<UnsafeMemberCensusResult.Available>(
                TypeScopeCensus(IteratorFixturePath, [FixtureType(IteratorFixturePath, type)]))
            .Census;

        Assert.True(census.IsComplete);
        UnsafeMemberFinding finding = Assert.Single(census.Members);
        Assert.Equal(member, finding.Member.Name);
        Assert.Equal(type, finding.Member.DeclaringType.Name);
        UnsafeMemberFindingEvidence evidence = Assert.Single(finding.Evidence);
        Assert.Equal(UnsafeMemberUseKind.ExplicitContractCall, evidence.Kind);
        Assert.Equal(body, evidence.Body.Name);
        Assert.Equal(UnsafeMemberExposure.Public, finding.Exposure);
    }

    // An iterator's MoveNext, its <>m__Finally1 helper, and the lambdas and
    // local function declared in it fold into the iterator method.
    [Theory]
    [InlineData("GeneratedIteratorFinallyUnsafeSample", "FinallyOnlyUnsafe", new[] { "<>m__Finally1" })]
    [InlineData(
        "GeneratedIteratorLiftedUnsafeSample",
        "LiftedInIterator",
        new[] { "<LiftedInIterator>b__0_0", "<LiftedInIterator>b__1", "<LiftedInIterator>g__Local|0_2" })]
    public void TypeScopeFoldsIteratorBodiesIntoTheIteratorMethod(
        string type,
        string member,
        string[] bodies)
    {
        UnsafeMemberCensus census = Assert.IsType<UnsafeMemberCensusResult.Available>(
                TypeScopeCensus(IteratorFixturePath, [FixtureType(IteratorFixturePath, type)]))
            .Census;

        Assert.True(census.IsComplete);
        UnsafeMemberFinding finding = Assert.Single(census.Members);
        Assert.Equal(member, finding.Member.Name);
        Assert.Equal(
            bodies.Order(StringComparer.Ordinal),
            finding.Evidence
                .Select(static evidence => evidence.Body.Name)
                .Order(StringComparer.Ordinal));
        Assert.All(
            finding.Evidence,
            evidence => Assert.NotEqual(
                finding.Member.MetadataToken,
                evidence.Body.MetadataToken));
    }

    [Fact]
    public void TypeScopeUnclaimedGeneratedBodyIsAnUnattributedLimitation()
    {
        UnsafeMemberCensus census = Assert.IsType<UnsafeMemberCensusResult.Available>(
                TypeScopeCensus(
                    IteratorFixturePath,
                    [FixtureType(IteratorFixturePath, "UnclaimedGeneratedBodySample")]))
            .Census;

        Assert.False(census.IsComplete);
        Assert.Empty(census.Members);
        UnsafeMemberLimitation limitation = Assert.Single(census.Limitations);
        Assert.Equal(UnsafeMemberLimitationReason.UnattributedGeneratedBody, limitation.Reason);
        Assert.Equal("Hidden", limitation.Body?.Name);
        Assert.Null(limitation.DeclaredMember);
        Assert.Contains(
            limitation.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.ExplicitContractCall);
    }

    // At Library scope every physical body is visited, so the same unclaimed
    // body is the census's one unattributed limitation.
    [Fact]
    public void LibraryScopeUnclaimedGeneratedBodyIsAnUnattributedLimitation()
    {
        UnsafeMemberCensus census = LibraryCensus(IteratorFixturePath);

        Assert.False(census.IsComplete);
        UnsafeMemberLimitation limitation = Assert.Single(census.Limitations);
        Assert.Equal(UnsafeMemberLimitationReason.UnattributedGeneratedBody, limitation.Reason);
        Assert.Equal("Hidden", limitation.Body?.Name);
        Assert.Null(limitation.DeclaredMember);
        Assert.Contains(
            limitation.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.ExplicitContractCall);
    }

    // A reached expansion bound keeps the retained prefix: the census is
    // available, carries the attribution-bound limitation, and every finding
    // it keeps is evidence the unbounded census also reports for the same
    // member. Sweeping the bound covers a cut at every step of the walk.
    [Theory]
    [InlineData("GeneratedIteratorLiftedUnsafeSample")]
    [InlineData("TypeScopeCapturingLambda")]
    [InlineData("TypeScopeAsyncMethod")]
    public void TypeScopeExpansionBoundKeepsTheRetainedPrefix(string typeName)
    {
        TypeDefinitionHandle type = FixtureType(IteratorFixturePath, typeName);
        var full = Assert.IsType<UnsafeMemberCensusResult.Available>(
            TypeScopeCensus(IteratorFixturePath, [type]));
        Assert.False(full.SourceReceipt.Coverage.GeneratedExpansion.IsBounded);
        Assert.DoesNotContain(
            full.Census.Limitations,
            static limitation => limitation.Reason
                == UnsafeMemberLimitationReason.AttributionBounded);
        HashSet<string> fullEvidence = EvidenceKeys(full.Census);

        int bounded = 0;
        int boundedWithFindings = 0;
        for (int limit = 1; ; limit++)
        {
            var limits = new MethodDefinitionGeneratedExpansionLimits(
                maximumCandidateDefinitions: limit,
                maximumGeneratedMethods: limit,
                maximumProbeBodies: limit,
                maximumProbeEncodedIlBytes: 64L * limit,
                maximumRelationshipNodes: limit);
            var scoped = Assert.IsType<UnsafeMemberCensusResult.Available>(
                TypeScopeCensus(IteratorFixturePath, [type], limits));
            MethodDefinitionSourceReceipt receipt = scoped.SourceReceipt;
            if (!receipt.Coverage.GeneratedExpansion.IsBounded)
            {
                // Once the walk fits, the census is the unbounded one.
                Assert.Equal(
                    MethodDefinitionSourceCompletion.Exhausted,
                    receipt.Completion);
                Assert.Equal(
                    full.Census.Members.Select(FindingKey),
                    scoped.Census.Members.Select(FindingKey));
                break;
            }

            bounded++;
            Assert.Equal(
                MethodDefinitionSourceCompletion.SourceIncomplete,
                receipt.Completion);
            Assert.Equal(
                ProducerOutcome.SourceBounded,
                scoped.Receipt.For(UnsafeMemberCensusProducer.Instance).Outcome);
            UnsafeMemberLimitation limitation = Assert.Single(
                scoped.Census.Limitations,
                static limitation => limitation.Reason
                    == UnsafeMemberLimitationReason.AttributionBounded);
            Assert.Null(limitation.BodyToken);
            Assert.Subset(fullEvidence, EvidenceKeys(scoped.Census));
            if (scoped.Census.Members.Length > 0)
                boundedWithFindings++;
            Assert.True(limit < 1_000, "The expansion never fit its bounds.");
        }

        Assert.True(bounded > 0);
        Assert.True(boundedWithFindings > 0);
    }

    // Each bound dimension cuts the declared-source relation to a prefix of
    // the settled one: every outcome it keeps is one the settled relation
    // publishes, and every origin's body is in breadth. A bound swallowed as
    // a rejection would instead publish an outcome the settled relation does
    // not.
    [Theory]
    [InlineData("GeneratedIteratorLiftedUnsafeSample")]
    [InlineData("TypeScopeCapturingLambda")]
    [InlineData("TypeScopeAsyncMethod")]
    [InlineData("UnclaimedGeneratedBodySample")]
    public void DeclaredSourceRelationBoundIsAPrefixOfTheSettledRelation(
        string typeName)
    {
        TypeDefinitionHandle type = FixtureType(IteratorFixturePath, typeName);
        MethodDefinitionSourceCoverage settled =
            Assert.IsType<UnsafeMemberCensusResult.Available>(
                    TypeScopeCensus(IteratorFixturePath, [type]))
                .SourceReceipt.Coverage;
        Assert.False(settled.GeneratedExpansion.IsBounded);

        MethodDefinitionGeneratedExpansionLimits defaults =
            MethodDefinitionGeneratedExpansionLimits.Default;
        foreach (MethodDefinitionGeneratedExpansionLimitKind dimension
            in Enum.GetValues<MethodDefinitionGeneratedExpansionLimitKind>())
        {
            for (int limit = 1; limit < 1_000; limit++)
            {
                MethodDefinitionGeneratedExpansionLimits limits = dimension switch
                {
                    MethodDefinitionGeneratedExpansionLimitKind.CandidateDefinitions =>
                        new(limit, defaults.MaximumGeneratedMethods, defaults.MaximumProbeBodies, defaults.MaximumProbeEncodedIlBytes, defaults.MaximumRelationshipNodes),
                    MethodDefinitionGeneratedExpansionLimitKind.GeneratedMethods =>
                        new(defaults.MaximumCandidateDefinitions, limit, defaults.MaximumProbeBodies, defaults.MaximumProbeEncodedIlBytes, defaults.MaximumRelationshipNodes),
                    MethodDefinitionGeneratedExpansionLimitKind.ProbeBodies =>
                        new(defaults.MaximumCandidateDefinitions, defaults.MaximumGeneratedMethods, limit, defaults.MaximumProbeEncodedIlBytes, defaults.MaximumRelationshipNodes),
                    MethodDefinitionGeneratedExpansionLimitKind.ProbeEncodedIlBytes =>
                        new(defaults.MaximumCandidateDefinitions, defaults.MaximumGeneratedMethods, defaults.MaximumProbeBodies, 16L * limit, defaults.MaximumRelationshipNodes),
                    _ =>
                        new(defaults.MaximumCandidateDefinitions, defaults.MaximumGeneratedMethods, defaults.MaximumProbeBodies, defaults.MaximumProbeEncodedIlBytes, limit),
                };
                MethodDefinitionSourceCoverage coverage =
                    Assert.IsType<UnsafeMemberCensusResult.Available>(
                            TypeScopeCensus(IteratorFixturePath, [type], limits))
                        .SourceReceipt.Coverage;
                MethodDefinitionGeneratedExpansionCoverage expansion =
                    coverage.GeneratedExpansion;
                if (!expansion.IsBounded)
                {
                    Assert.Equal(settled.GeneratedExpansion.Origins, expansion.Origins);
                    break;
                }

                Assert.Equal(dimension, expansion.ReachedLimit);
                Assert.Subset(
                    settled.GeneratedExpansion.Origins.ToHashSet(),
                    expansion.Origins.ToHashSet());
                Assert.Subset(
                    settled.GeneratedExpansion.AttributedOutsideRoles.ToHashSet(),
                    expansion.AttributedOutsideRoles.ToHashSet());
                Assert.Subset(
                    settled.GeneratedExpansion.UnauthenticatedCandidates.ToHashSet(),
                    expansion.UnauthenticatedCandidates.ToHashSet());
                Assert.All(
                    expansion.Origins,
                    origin => Assert.True(coverage.MethodsSelected.Contains(origin.Method)));
                Assert.All(
                    coverage.MethodsSelected.Ranges.SelectMany(static range =>
                        Enumerable.Range(
                                MetadataTokens.GetRowNumber(range.First),
                                range.Count)
                            .Select(MetadataTokens.MethodDefinitionHandle)),
                    method => Assert.True(settled.MethodsSelected.Contains(method)));
            }
        }
    }

    // During expansion a bound is not a recoverable method failure, so no
    // resolver handler can turn it into a rejected or unauthenticated
    // outcome; even one that is caught still bounds the relation. After
    // expansion settles, a module lookup's new charge and a cached replay of
    // the bound are each that unit's recoverable failure, as before.
    [Fact]
    public void ExpansionBoundIsNotRecoverableAndAlwaysBoundsTheRelation()
    {
        var work = new MethodDefinitionGeneratedExpansionWork(
            new MethodDefinitionGeneratedExpansionLimits(
                maximumCandidateDefinitions: 1,
                maximumGeneratedMethods: 1,
                maximumProbeBodies: 1,
                maximumProbeEncodedIlBytes: 1,
                maximumRelationshipNodes: 1));
        work.RecordCandidateDefinition(MetadataTokens.MethodDefinitionHandle(1));

        var bound = Assert.Throws<MethodDefinitionGeneratedExpansionBoundException>(
            () => work.RecordCandidateDefinition(
                MetadataTokens.MethodDefinitionHandle(2)));
        Assert.False(AnalysisMethodFailures.IsRecoverable(bound));
        Assert.False(LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(bound));
        Assert.Same(bound, work.Reached);
        MethodDefinitionGeneratedExpansionCoverage coverage =
            work.BuildBounded(bound);
        Assert.Equal(
            MethodDefinitionGeneratedExpansionLimitKind.CandidateDefinitions,
            coverage.ReachedLimit);
        Assert.Equal(
            MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(2)),
            coverage.ReachedAtToken);
        Assert.Equal(1, coverage.CandidateDefinitionsExamined.Count);

        // A resolver cache that stored the bound during expansion can replay
        // the same instance after settling; it is then recoverable.
        work.Settle();
        Assert.True(bound.IsSettled);
        Assert.True(AnalysisMethodFailures.IsRecoverable(bound));
        Assert.True(LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(bound));
        var late = Assert.Throws<InvalidOperationException>(
            () => work.RecordCandidateDefinition(
                MetadataTokens.MethodDefinitionHandle(3)));
        Assert.IsNotType<MethodDefinitionGeneratedExpansionBoundException>(late);
        Assert.True(LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(late));
    }

    // A module lookup shares the expansion's attribution, and its resolver
    // caches keep exceptions. After a bounded expansion settles, a lookup
    // that reads a cache which stored the bound replays that bound; it must
    // be a recoverable failure of that lookup, never an escaping one.
    [Fact]
    public void LookupAfterABoundedExpansionFailsRecoverably()
    {
        string path = IteratorFixturePath;
        byte[] bytes = File.ReadAllBytes(path);
        int replays = 0;
        using (var types = new PEReader(ImmutableArray.Create(bytes)))
        {
            MetadataReader typeReader = types.GetMetadataReader();
            foreach (TypeDefinitionHandle type in typeReader.TypeDefinitions)
            {
                TypeDefinition definition = typeReader.GetTypeDefinition(type);
                if (!definition.GetDeclaringType().IsNil
                    || definition.GetMethods().Count == 0)
                {
                    continue;
                }

                for (int limit = 1; limit <= 12; limit++)
                    replays += LookupReplays(path, bytes, type, limit);
            }
        }

        Assert.True(replays > 0, "No lookup read a cache that stored the bound.");
    }

    static int LookupReplays(
        string path,
        byte[] bytes,
        TypeDefinitionHandle type,
        int probeBodies)
    {
        using var pe = new PEReader(ImmutableArray.Create(bytes));
        MetadataReader reader = pe.GetMetadataReader();
        var work = new MethodDefinitionGeneratedExpansionWork(
            new MethodDefinitionGeneratedExpansionLimits(
                maximumCandidateDefinitions: 1_000,
                maximumGeneratedMethods: 1_000,
                maximumProbeBodies: probeBodies,
                maximumProbeEncodedIlBytes: 1L << 30,
                maximumRelationshipNodes: 1_000));
        var attribution = new DeclaredSourceAttribution(
            path,
            reader,
            pe,
            generatedExpansionWork: work);
        MethodDefinitionGeneratedExpansionResult result =
            attribution.ExpandGeneratedExecutionBodies(
                [.. reader.GetTypeDefinition(type).GetMethods()],
                MethodDefinitionGeneratedBodyRoles.All,
                [type],
                includeUnauthenticatedCandidates: true);
        if (result.Bound is not { } bound)
            return 0;

        int replays = 0;
        foreach (MethodDefinitionHandle method in reader.MethodDefinitions)
        {
            MethodDefinition definition = reader.GetMethodDefinition(method);
            if (!CompilerGeneratedNames.IsLocalFunctionOrLambda(
                    reader.GetString(definition.Name)))
            {
                continue;
            }

            TypeDefinitionHandle declaring = definition.GetDeclaringType();
            try
            {
                MethodIdentity identity =
                    attribution.PrimaryMetadataResolver.CreateMethodIdentity(
                        declaring,
                        method,
                        definition,
                        attribution.PrimaryMetadataResolver.CreateScope(
                            reader.GetTypeDefinition(declaring),
                            definition));
                _ = attribution.LiftedSourceOwnerResolver.TryResolve(
                    method,
                    definition,
                    identity,
                    out _);
            }
            catch (Exception ex)
            {
                Assert.True(
                    LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex),
                    $"{ex.GetType().Name}: {ex.Message}");
                if (ReferenceEquals(ex, bound))
                    replays++;
            }
        }
        return replays;
    }

    // One key per piece of evidence and the member it is reported under.
    static HashSet<string> EvidenceKeys(UnsafeMemberCensus census) =>
    [
        .. census.Members.SelectMany(static finding => finding.Evidence.Select(
            evidence =>
                $"{finding.Member.MetadataToken:X8}<-{evidence.Body.MetadataToken:X8}:"
                + $"{evidence.Kind}@{evidence.ILOffset}:{evidence.Detail}")),
    ];

    // Breadth and terminal work stay within the selected type and its
    // declared expansion; the module-wide inputs are execution-scoped
    // capabilities with their own receipts.
    [Fact]
    public void TypeScopeWorkStaysWithinTheSelectedTypeAndItsExpansion()
    {
        TypeDefinitionHandle type =
            FixtureType(AnalysisFixturePath, typeof(ClassicAsyncUnsafeFixtures));
        var available = Assert.IsType<UnsafeMemberCensusResult.Available>(
            TypeScopeCensus(AnalysisFixturePath, [type]));
        MethodDefinitionSourceCoverage coverage = available.SourceReceipt.Coverage;
        Assert.NotEmpty(coverage.GeneratedExpansion.Origins);

        using var stream = File.OpenRead(AnalysisFixturePath);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        HashSet<MethodDefinitionHandle> allowed =
        [
            .. reader.GetTypeDefinition(type).GetMethods(),
            .. coverage.GeneratedExpansion.Origins.Select(static origin => origin.Method),
            .. coverage.GeneratedExpansion.UnauthenticatedCandidates,
        ];
        Assert.True(reader.MethodDefinitions.Count > allowed.Count * 10);
        Assert.Equal(allowed.Count, coverage.MethodsSelected.Count);
        Assert.All(allowed, method => Assert.True(coverage.MethodsSelected.Contains(method)));
        Assert.True(coverage.BodiesAcquired.Count <= allowed.Count);
        Assert.Equal(0, available.SourceReceipt.ModuleLookups);
        Assert.True(coverage.PublicMethodRoots.Constructed);
        Assert.Equal(1, coverage.PublicMethodRoots.Reads);
        Assert.True(coverage.ReferenceAssemblyStatus.Constructed);
        Assert.Equal(1, coverage.ReferenceAssemblyStatus.Reads);
    }

    // docs/design/method-query-source.md#declared-source-relation: only a
    // breadth that declares expansion carries the relation.
    [Fact]
    public void DeclaredSourceRequiresGeneratedExpansion()
    {
        string path = IteratorFixturePath;
        var operation = AssemblyAnalysisOperation<UnsafeMemberCensus>.Create(
            path,
            MethodDefinitionSourceRequest<UnsafeMemberCensus>.Create(
                UnsafeMemberCensusAnalysis.Description,
                UnsafeMemberCensusProducer.Instance,
                MethodDefinitionSourceBreadth.ExactTypes(
                    FixtureType(path, "TypeScopeDirect"))));
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(File.ReadAllBytes(path), writable: false));

        ProducerContractException exception = Assert.Throws<ProducerContractException>(
            () => session.SnapshotOperation(
                operation,
                access => AssemblyAnalysisService.Instance.Execute(
                    operation,
                    access)));
        Assert.Contains("declares no generated-body expansion", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredSourceOfABodyWithTwoOwnersIsUnauthenticated()
    {
        MethodDefinitionHandle body = MetadataTokens.MethodDefinitionHandle(3);
        MethodDefinitionHandle first = MetadataTokens.MethodDefinitionHandle(1);
        MethodDefinitionHandle second = MetadataTokens.MethodDefinitionHandle(2);
        MethodDefinitionHandle lifted = MetadataTokens.MethodDefinitionHandle(4);
        var coverage = MethodDefinitionGeneratedExpansionCoverage.Empty with
        {
            Origins =
            [
                new(body, first, MethodDefinitionGeneratedExpansionOriginKind.LiftedExecutionBody),
                new(body, second, MethodDefinitionGeneratedExpansionOriginKind.LiftedExecutionBody),
                new(lifted, first, MethodDefinitionGeneratedExpansionOriginKind.LiftedExecutionBody),
            ],
        };

        var sources = new MethodDefinitionDeclaredSources(coverage);

        Assert.Same(MethodDefinitionDeclaredSource.Unauthenticated.Instance, sources.Of(body));
        Assert.Equal(
            first,
            Assert.IsType<MethodDefinitionDeclaredSource.Generated>(sources.Of(lifted)).Origin.DeclaredOwner);
        Assert.Same(MethodDefinitionDeclaredSource.Direct.Instance, sources.Of(first));
    }

    static UnsafeMemberCensusResult TypeScopeCensus(
        string path,
        IEnumerable<TypeDefinitionHandle> types,
        MethodDefinitionGeneratedExpansionLimits? limits = null)
    {
        var operation = AssemblyAnalysisOperation<UnsafeMemberCensus>.Create(
            path,
            UnsafeMemberCensusAnalysis.ForTypes(types, limits));
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(File.ReadAllBytes(path), writable: false));
        AssemblyAnalysisExecution<UnsafeMemberCensus> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<UnsafeMemberCensus>.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access => AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access)))
                .Execution;
        return UnsafeMemberCensusAnalysis.Project(execution);
    }

    static UnsafeMemberCensusResult LibraryScopeCensus(string path)
    {
        var operation = AssemblyAnalysisOperation<UnsafeMemberCensus>.Create(
            path,
            UnsafeMemberCensusAnalysis.ForLibrary());
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(File.ReadAllBytes(path), writable: false));
        AssemblyAnalysisExecution<UnsafeMemberCensus> execution =
            Assert.IsType<AssemblyAnalysisServiceResult<UnsafeMemberCensus>.Completed>(
                    session.SnapshotOperation(
                        operation,
                        access => AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access)))
                .Execution;
        return UnsafeMemberCensusAnalysis.Project(execution);
    }

    // Every type with methods whose own name a reader wrote, nested or not.
    static ImmutableArray<(TypeDefinitionHandle Type, ImmutableHashSet<int> Methods)>
        SourceTypes(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return
        [
            .. reader.TypeDefinitions
                .Select(handle => (Handle: handle, Type: reader.GetTypeDefinition(handle)))
                .Where(entry => entry.Type.GetMethods().Count > 0
                    && !reader.GetString(entry.Type.Name).Contains('<'))
                .Select(entry => (
                    entry.Handle,
                    entry.Type.GetMethods()
                        .Select(static method => MetadataTokens.GetToken(method))
                        .ToImmutableHashSet())),
        ];
    }

    static TypeDefinitionHandle FixtureType(string path, Type type) =>
        FixtureType(path, type.Name);

    static TypeDefinitionHandle FixtureType(string path, string name)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return Assert.Single(
            reader.TypeDefinitions,
            handle => reader.StringComparer.Equals(
                reader.GetTypeDefinition(handle).Name,
                name));
    }

    // Evidence order is semantic only within one physical body.
    static string FindingKey(UnsafeMemberFinding finding) =>
        string.Join(
            "|",
            [
                finding.Member.MetadataToken.ToString("X8"),
                finding.HasExplicitUnsafeContract.ToString(),
                finding.Exposure.ToString(),
                string.Join(
                    ";",
                    finding.UninspectedBodies.Select(static gap => gap.BodyToken)),
                .. finding.Evidence
                    .GroupBy(static evidence => evidence.Body.MetadataToken)
                    .OrderBy(static group => group.Key)
                    .Select(static group => string.Join(
                        ",",
                        group.Select(static evidence =>
                            $"{evidence.Body.MetadataToken:X8}:{evidence.Kind}@{evidence.ILOffset}:{evidence.Detail}:{evidence.ContractSource}"))),
            ]);
}
