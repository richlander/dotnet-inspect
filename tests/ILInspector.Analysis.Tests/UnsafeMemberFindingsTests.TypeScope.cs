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

    // Every finding agrees with the Library Body Analysis census for the
    // same type, except that a body that census leaves unattributed and the
    // declared-source relation authenticates now folds into its owner. In
    // these fixtures those are exactly the synchronous iterator bodies.
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
        UnsafeMemberCensus library =
            BodyAnalysisTestExecution.Open(path).Safety.MemberCensus;
        int folds = 0;

        foreach ((TypeDefinitionHandle type, ImmutableHashSet<int> methods)
            in SourceTypes(path))
        {
            var scoped = Assert.IsType<UnsafeMemberCensusResult.Available>(
                TypeScopeCensus(path, [type]));
            MethodDefinitionSourceCoverage coverage =
                scoped.SourceReceipt.Coverage;
            Dictionary<int, int> owners = coverage.GeneratedExpansion.Origins
                .Concat(coverage.GeneratedExpansion.AttributedOutsideRoles)
                .ToDictionary(
                    static origin => MetadataTokens.GetToken(origin.Method),
                    static origin => MetadataTokens.GetToken(origin.DeclaredOwner));
            bool Selected(int token) =>
                coverage.MethodsSelected.Contains(
                    MetadataTokens.MethodDefinitionHandle(token & 0x00FFFFFF));

            var folded = library.Limitations
                .Where(limitation =>
                    limitation.Reason
                        == UnsafeMemberLimitationReason.UnattributedGeneratedBody
                    && limitation.BodyToken is int body
                    && Selected(body)
                    && owners.ContainsKey(body))
                .ToList();
            Assert.All(
                folded,
                limitation => Assert.Contains(
                    ">d__",
                    limitation.Body!.DeclaringType.Name,
                    StringComparison.Ordinal));
            folds += folded.Count;

            var expected = library.Members
                .Where(finding => methods.Contains(finding.Member.MetadataToken))
                .ToDictionary(static finding => finding.Member.MetadataToken);
            foreach (UnsafeMemberLimitation limitation in folded)
            {
                int owner = owners[limitation.BodyToken!.Value];
                UnsafeMemberFinding actualOwner = Assert.Single(
                    scoped.Census.Members,
                    finding => finding.Member.MetadataToken == owner);
                expected[owner] = expected.TryGetValue(owner, out UnsafeMemberFinding? prior)
                    ? prior with { Evidence = [.. prior.Evidence, .. limitation.Evidence] }
                    : actualOwner with { Evidence = limitation.Evidence };
            }

            Assert.Equal(
                expected.Values
                    .OrderBy(static finding => finding.Member.MetadataToken)
                    .Select(FindingKey),
                scoped.Census.Members.Select(FindingKey));
            Assert.Equal(
                library.Limitations
                    .Where(limitation =>
                        limitation.BodyToken is int body
                        && Selected(body)
                        && !folded.Contains(limitation)),
                scoped.Census.Limitations);
        }

        if (fixture is "new" or "legacy" or "iterators")
            Assert.True(folds > 0, "The fixture must exercise an iterator fold.");
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

    // Retained-prefix expansion bounds belong to #9864. Until they land, a
    // reached bound fails the census visibly instead of presenting the
    // empty breadth as a negative.
    [Fact]
    public void TypeScopeExpansionBoundFailsTheCensusVisibly()
    {
        var limits = new MethodDefinitionGeneratedExpansionLimits(
            maximumCandidateDefinitions: 1,
            maximumGeneratedMethods: 1,
            maximumProbeBodies: 1,
            maximumProbeEncodedIlBytes: 1,
            maximumRelationshipNodes: 1);

        var failed = Assert.IsType<UnsafeMemberCensusResult.Failed>(
            TypeScopeCensus(
                IteratorFixturePath,
                [FixtureType(IteratorFixturePath, "GeneratedIteratorLiftedUnsafeSample")],
                limits));

        Assert.Equal(ProducerOutcome.Failed, failed.Outcome);
        Assert.Contains("limit was exhausted", failed.Error.Message, StringComparison.Ordinal);
    }

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
