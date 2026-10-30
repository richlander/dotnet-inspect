using System.Text.Json;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class MemberFindSemanticEvaluationTests
{
    [Fact]
    public void Question_RejectsInvalidPatternsFiltersAndLimits()
    {
        Assert.Throws<ArgumentException>(
            () => MemberFindQuestion.Create(
                Array.Empty<string>(),
                FindVisibility.Public));
        Assert.Throws<ArgumentException>(
            () => MemberFindQuestion.Create(
                [" "],
                FindVisibility.Public));
        Assert.Throws<ArgumentException>(
            () => MemberFindQuestion.Create(
                ["Member"],
                FindVisibility.Public,
                declaringTypeFilter: " "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MemberFindQuestion.Create(
                ["Member"],
                FindVisibility.Public,
                maximumMatches: 0));
        Assert.Throws<ArgumentException>(
            () => MemberFindQuestion.Create(
                [MemberFindPattern.Create(1, "Member")],
                FindVisibility.Public));

        FindQuestion question =
            MemberFindQuestion.Create(
                ["Member"],
                FindVisibility.Public);
        string json = JsonSerializer.Serialize(question);
        Assert.IsType<MemberFindQuestion>(
            JsonSerializer.Deserialize<FindQuestion>(json));
    }

    [Fact]
    public async Task Evaluation_PreservesGrammarMultiplicityAndExactIdentity()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                [
                    "WorkspaceQueryMember",
                    "WorkspaceQuery*",
                    "WorkspaceQueryMember",
                ],
                FindVisibility.Public,
                typeof(WorkspaceQueryImplementation).FullName);

        MemberFindSemanticPopulation population =
            EvaluateLocal(question, group);
        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                population);

        Assert.Equal(3, block.Matches.Length);
        Assert.Equal(
            [0, 1, 2],
            block.Matches.Select(
                static match => match.Pattern.Ordinal));
        Assert.Equal(
            [
                MemberFindSemanticMatchKind.Direct,
                MemberFindSemanticMatchKind.Glob,
                MemberFindSemanticMatchKind.Direct,
            ],
            block.Matches.Select(static match => match.Match));
        Assert.Single(
            block.Matches.Select(
                    static match =>
                        match.Declaration.Member.StableSelector)
                .Distinct());
        Assert.All(
            block.Matches,
            match =>
            {
                Assert.Equal(
                    typeof(WorkspaceQueryImplementation).FullName,
                    match.Declaration.DeclaringType
                        .ToMetadataFullName());
                Assert.Equal(
                    "WorkspaceQueryMember",
                    match.Declaration.Member.MemberName);
                Assert.IsType<
                    ExactLibrarySourceCoordinate.Local>(
                        match.Declaration.Source.Coordinate);
            });
        Assert.True(block.IsSourceCoverageComplete);
        Assert.Equal(
            FindMatchCompletion.Exhausted,
            block.MatchCompletion);

        string json = JsonSerializer.Serialize(block);
        MemberFindBlock roundTrip =
            Assert.IsType<MemberFindBlock>(
                JsonSerializer.Deserialize<MemberFindBlock>(json));
        MemberFindSemanticMatch roundTripMatch =
            roundTrip.Matches[0];
        Assert.Equal(
            block.Matches[0].Declaration.Member,
            roundTripMatch.Declaration.Member);
        Assert.Equal(
            block.Matches[0].Declaration.DeclaringType,
            roundTripMatch.Declaration.DeclaringType);
        Assert.IsType<ExactLibrarySourceCoordinate.Local>(
            roundTripMatch.Declaration.Source.Coordinate);
        Assert.Equal(
            block.Matches[0].Declaration.Source.Selection,
            roundTripMatch.Declaration.Source.Selection);
        Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(
            roundTripMatch.Declaration.Source.Selection);
    }

    [Fact]
    public async Task Evaluation_LimitLeavesLaterPatternUnevaluated()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                [
                    "WorkspaceQueryMember",
                    "WorkspaceQueryExtension",
                ],
                FindVisibility.Public,
                maximumMatches: 1);

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                EvaluateLocal(question, group));

        Assert.Single(block.Matches);
        Assert.Equal(
            FindPatternSettlementKind.Matched,
            block.Settlements[0].Kind);
        Assert.Equal(
            FindPatternSettlementKind.NotEvaluated,
            block.Settlements[1].Kind);
        Assert.Equal(
            FindMatchCompletion.MatchLimitReached,
            block.MatchCompletion);
    }

    [Fact]
    public async Task Evaluation_LaterPatternLimitPreservesEarlierCompleteMiss()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                [
                    "NoSuchMemberXyzzy",
                    "WorkspaceQueryMember",
                ],
                FindVisibility.Public,
                maximumMatches: 1);

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                EvaluateLocal(question, group));

        Assert.Equal(
            FindPatternSettlementKind.NoMatch,
            block.Settlements[0].Kind);
        Assert.Equal(
            FindPatternSettlementKind.Matched,
            block.Settlements[1].Kind);
        Assert.Equal(
            FindMatchCompletion.MatchLimitReached,
            block.MatchCompletion);
    }

    [Fact]
    public async Task Evaluation_LimitDoesNotOverrideOwnerIncompleteness()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                [
                    "NoSuchMemberXyzzy",
                    "WorkspaceQueryMember",
                ],
                FindVisibility.Public,
                maximumMatches: 1);
        MemberFindSemanticPopulation limited =
            EvaluateLocal(question, group);

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                MemberFindSemanticPopulation.Create(
                    question,
                    limited.Sources,
                    limited.Gaps,
                    ownerReportsComplete: false));

        Assert.Equal(
            FindPatternSettlementKind.Inconclusive,
            block.Settlements[0].Kind);
        Assert.Equal(
            FindPatternSettlementKind.Matched,
            block.Settlements[1].Kind);
        Assert.Equal(
            FindMatchCompletion.MatchLimitReached,
            block.MatchCompletion);
        Assert.False(block.IsSourceCoverageComplete);
    }

    [Fact]
    public async Task Evaluation_CompleteMissIsConclusive()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                ["NoSuchMemberXyzzy"],
                FindVisibility.Public);
        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                EvaluateLocal(question, group));

        Assert.Equal(
            FindPatternSettlementKind.NoMatch,
            Assert.Single(block.Settlements).Kind);
        Assert.True(block.IsSourceCoverageComplete);
    }

    [Fact]
    public void Evaluation_IncompleteEmptyPopulationWithholdsNoMatch()
    {
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                ["NoSuchMemberXyzzy"],
                FindVisibility.Public);

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                MemberFindSemanticPopulation.Create(
                    question,
                    [],
                    ownerReportsComplete: false));

        Assert.Equal(
            FindPatternSettlementKind.Inconclusive,
            Assert.Single(block.Settlements).Kind);
        Assert.False(block.IsSourceCoverageComplete);
    }

    [Fact]
    public async Task Evaluation_SourceCanJoinTwoPopulations()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                ["WorkspaceQueryMember"],
                FindVisibility.Public);
        MemberFindSourceEvaluation source =
            Assert.Single(
                EvaluateLocal(question, group).Sources);

        MemberFindBlock first =
            FindSemanticReducer.ReduceMember(
                question,
                MemberFindSemanticPopulation.Create(
                    question,
                    [source]));
        MemberFindBlock second =
            FindSemanticReducer.ReduceMember(
                question,
                MemberFindSemanticPopulation.Create(
                    question,
                    [source]));

        Assert.Single(first.Matches);
        Assert.Equal(first.Matches, second.Matches);
        Assert.Same(
            first.Matches[0].Declaration.Source.Coordinate,
            second.Matches[0].Declaration.Source.Coordinate);
    }

    [Fact]
    public async Task Evaluation_RejectedSourceWithholdsNoMatch()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(path);
        AssemblyReferenceIdentity actualIdentity =
            ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local("actual"))
                .Identity;
        ResolvedAssemblyReference rejected =
            ResolvedAssemblyReference.Create(
                actualIdentity with { Name = "WrongIdentity" },
                path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Local("rejected"));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        rejected,
                        NoResolverAssemblyBindingPolicy.Instance),
                ]);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                ["NoSuchMemberXyzzy"],
                FindVisibility.Public);

        MemberFindSemanticPopulation population =
            EvaluateLocal(question, group);
        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                population);

        Assert.IsType<MemberFindSourceEvaluation.Rejected>(
            Assert.Single(population.Sources));
        Assert.Equal(
            FindPatternSettlementKind.Inconclusive,
            Assert.Single(block.Settlements).Kind);
        Assert.False(block.IsSourceCoverageComplete);
    }

    [Fact]
    public async Task Evaluation_InspectionFailureWithholdsNoMatch()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"member-semantic-partial-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(
            path,
            AssemblyContextSearchQueryTests
                .BuildPartialSurfaceImage());
        try
        {
            await using var workspace = new InspectionWorkspace();
            using AssemblyContextGroup group =
                CreateGroup(workspace, path);
            MemberFindQuestion question =
                MemberFindQuestion.Create(
                    ["NoSuchMemberXyzzy"],
                    FindVisibility.Public);

            MemberFindSemanticPopulation population =
                EvaluateLocal(question, group);
            MemberFindBlock block =
                FindSemanticReducer.ReduceMember(
                    question,
                    population);

            MemberFindSourceEvaluation source =
                Assert.IsType<
                    MemberFindSourceEvaluation.Available>(
                        Assert.Single(population.Sources));
            Assert.Equal(
                MemberFindSourceCoverageKind
                    .InspectionFailures,
                source.Coverage.Kind);
            Assert.Single(
                source.Coverage.InspectionFailures);
            Assert.Equal(
                FindPatternSettlementKind.Inconclusive,
                Assert.Single(block.Settlements).Kind);
            Assert.False(block.IsSourceCoverageComplete);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Evaluation_RuntimeWriteLineRetainsOverloadAnchors()
    {
        string path = typeof(Console).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                ["WriteLine"],
                FindVisibility.Public,
                declaringTypeFilter: "System.Console");

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                MemberFindSourceEvaluator.EvaluateAssemblyContext(
                    question,
                    group,
                    static (subject, memberOrder) =>
                        new(
                            new ExactLibrarySourceCoordinate
                                .Platform(
                                    new(
                                        PlatformFamily
                                            .DotNetRuntime),
                                    new(
                                        subject.Identity)),
                            subject.Provenance,
                            packageRequest: null,
                            contextOrder: 0,
                            memberOrder,
                            subject.Identity)));

        Assert.True(block.Matches.Length > 1);
        Assert.Equal(
            block.Matches.Length,
            block.Matches.Select(
                    static match =>
                        match.Declaration.Member
                            .CanonicalSignature)
                .Distinct()
                .Count());
        Assert.All(
            block.Matches,
            static match =>
                Assert.Equal(
                    "System.Console",
                    match.Declaration.DeclaringType
                        .ToMetadataFullName()));
    }

    [Fact]
    public async Task Evaluation_DistinguishesEqualPackageAndPlatformMembers()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        ResolvedAssemblyReference packageAssembly =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("package"));
        ResolvedAssemblyReference platformAssembly =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("platform"));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new(
                        packageAssembly,
                        NoResolverAssemblyBindingPolicy.Instance),
                    new(
                        platformAssembly,
                        NoResolverAssemblyBindingPolicy.Instance),
                ]);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                [
                    "WorkspaceQueryMember",
                    "WorkspaceQueryExtension",
                ],
                FindVisibility.Public);

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                MemberFindSourceEvaluator.EvaluateAssemblyContext(
                    question,
                    group,
                    static (subject, memberOrder) =>
                        new(
                            memberOrder == 0
                                ? new ExactLibrarySourceCoordinate
                                    .Package(
                                        PackageSourceCoordinate
                                            .Create(
                                                "Semantic.Test",
                                                "1.0.0"),
                                        new(
                                            subject.Identity))
                                : new ExactLibrarySourceCoordinate
                                    .Platform(
                                        new(
                                            PlatformFamily
                                                .DotNetRuntime),
                                        new(
                                            subject.Identity)),
                            subject.Provenance,
                            memberOrder == 0
                                ? new(
                                        "net11.0",
                                        requestedRuntimeIdentifier: null)
                                : null,
                            contextOrder: 0,
                            memberOrder,
                            subject.Identity)));

        Assert.Equal(6, block.Matches.Length);
        Assert.Equal(
            [0, 0, 1, 1, 1, 1],
            block.Matches.Select(
                static match => match.Pattern.Ordinal));
        Assert.Equal(
            [0, 1, 0, 0, 1, 1],
            block.Matches.Select(
                static match =>
                    match.Declaration.Source.MemberOrder));
        Assert.Equal(
            block.Matches[0].Declaration.Member,
            block.Matches[1].Declaration.Member);
        Assert.Equal(
            block.Matches[2].Declaration.Member,
            block.Matches[4].Declaration.Member);
        Assert.Equal(
            block.Matches[3].Declaration.Member,
            block.Matches[5].Declaration.Member);
        Assert.IsType<
            ExactLibrarySourceCoordinate.Package>(
                block.Matches[0].Declaration.Source
                    .Coordinate);
        Assert.IsType<
            ExactLibrarySourceCoordinate.Platform>(
                block.Matches[1].Declaration.Source
                    .Coordinate);
    }

    [Fact]
    public void SourceIdentity_DistinguishesExactSelections()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        AssemblyReferenceIdentity assemblyIdentity =
            ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local(
                        "selection identity"))
                .Identity;
        var libraryIdentity =
            new ManagedMetadataIdentity.Assembly(
                assemblyIdentity);
        var packageCoordinate =
            new ExactLibrarySourceCoordinate.Package(
                PackageSourceCoordinate.Create(
                    "Semantic.Test",
                    "1.0.0"),
                libraryIdentity);
        var packageNet8 =
            new FindSourceIdentity(
                packageCoordinate,
                AssemblyResolutionProvenance.Package(
                    "Semantic.Test",
                    "1.0.0",
                    "net8.0",
                    rid: null,
                    assetPath:
                        "ref/net8.0/Semantic.Test.dll"),
                new(
                    "net8.0",
                    requestedRuntimeIdentifier: null),
                contextOrder: 0,
                memberOrder: 0,
                assemblyIdentity);
        var packageNet10 =
            new FindSourceIdentity(
                packageCoordinate,
                AssemblyResolutionProvenance.Package(
                    "Semantic.Test",
                    "1.0.0",
                    "net10.0",
                    rid: null,
                    assetPath:
                        "ref/net10.0/Semantic.Test.dll"),
                new(
                    "net10.0",
                    requestedRuntimeIdentifier: null),
                contextOrder: 0,
                memberOrder: 0,
                assemblyIdentity);
        Assert.Equal(
            packageNet8.Coordinate,
            packageNet10.Coordinate);
        Assert.NotEqual(packageNet8, packageNet10);

        AssemblyResolutionProvenance compatibleSelection =
            AssemblyResolutionProvenance.Package(
                "Semantic.Test",
                "1.0.0",
                "net8.0",
                rid: null,
                assetPath:
                    "ref/net8.0/Semantic.Test.dll");
        var compatibleNet8 =
            new FindSourceIdentity(
                packageCoordinate,
                compatibleSelection,
                new(
                    "net8.0",
                    requestedRuntimeIdentifier: null),
                contextOrder: 0,
                memberOrder: 0,
                assemblyIdentity);
        var compatibleNet10 =
            new FindSourceIdentity(
                packageCoordinate,
                compatibleSelection,
                new(
                    "net10.0",
                    requestedRuntimeIdentifier: null),
                contextOrder: 0,
                memberOrder: 0,
                assemblyIdentity);
        Assert.Equal(
            compatibleNet8.Selection,
            compatibleNet10.Selection);
        Assert.NotEqual(compatibleNet8, compatibleNet10);

        var platformCoordinate =
            new ExactLibrarySourceCoordinate.Platform(
                new(PlatformFamily.DotNetRuntime),
                libraryIdentity);
        var platform100 =
            new FindSourceIdentity(
                platformCoordinate,
                AssemblyResolutionProvenance.Platform(
                    "Microsoft.NETCore.App",
                    "10.0.12",
                    "selection identity"),
                packageRequest: null,
                contextOrder: 0,
                memberOrder: 0,
                assemblyIdentity);
        var platform101 =
            new FindSourceIdentity(
                platformCoordinate,
                AssemblyResolutionProvenance.Platform(
                    "Microsoft.NETCore.App",
                    "10.0.13",
                    "selection identity"),
                packageRequest: null,
                contextOrder: 0,
                memberOrder: 0,
                assemblyIdentity);
        Assert.Equal(
            platform100.Coordinate,
            platform101.Coordinate);
        Assert.NotEqual(platform100, platform101);
    }

    [Fact]
    public async Task Evaluation_RejectsMismatchedSelectionEvidence()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                ["WorkspaceQueryMember"],
                FindVisibility.Public);

        Assert.Throws<ArgumentException>(
            () => MemberFindSourceEvaluator
                .EvaluateAssemblyContext(
                    question,
                    group,
                    static (subject, memberOrder) =>
                        new(
                            new ExactLibrarySourceCoordinate.Local(
                                new(subject.Identity)),
                            AssemblyResolutionProvenance.Local(
                                "different selection"),
                            packageRequest: null,
                            contextOrder: 0,
                            memberOrder,
                            subject.Identity)));
    }

    [Fact]
    public async Task Evaluation_IndexerAliasIsDirect()
    {
        string path = typeof(string).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                ["this[]"],
                FindVisibility.Public,
                declaringTypeFilter: "System.String",
                maximumMatches: 1);

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                EvaluateLocal(question, group));

        MemberFindSemanticMatch match =
            Assert.Single(block.Matches);
        Assert.Equal(
            MemberFindSemanticMatchKind.Direct,
            match.Match);
        Assert.Contains(
            match.MemberName,
            new[] { "Chars", "Item" });
        Assert.Equal(
            "System.String",
            match.Declaration.DeclaringType
                .ToMetadataFullName());
    }

    [Fact]
    public async Task Evaluation_AcceptedWindowMatchesCompleteSemanticOrder()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    Participant(path, "first"),
                    Participant(path, "second"),
                ]);
        MemberFindQuestion completeQuestion =
            MemberFindQuestion.Create(
                [
                    "WorkspaceQueryMember",
                    "WorkspaceQueryExtension",
                ],
                FindVisibility.Public);
        MemberFindBlock complete =
            FindSemanticReducer.ReduceMember(
                completeQuestion,
                EvaluateLocal(completeQuestion, group));
        MemberFindQuestion windowQuestion =
            MemberFindQuestion.Create(
                [
                    "WorkspaceQueryMember",
                    "WorkspaceQueryExtension",
                ],
                FindVisibility.Public,
                maximumMatches: 4,
                acceptedRows: new(
                    start: 2,
                    end: 4,
                    materializeRows: true));

        MemberFindBlock window =
            FindSemanticReducer.ReduceMember(
                windowQuestion,
                EvaluateLocal(windowQuestion, group));

        Assert.Equal(4, window.AcceptedCount);
        Assert.Equal(
            complete.Matches.Skip(1).Take(3),
            window.Matches);
        Assert.Equal(
            FindMatchCompletion.MatchLimitReached,
            window.MatchCompletion);
    }

    [Fact]
    public async Task Evaluation_CountRetainsAcceptedEvidenceWithoutRows()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                ["WorkspaceQueryMember"],
                FindVisibility.Public,
                acceptedRows: new(
                    start: 1,
                    end: null,
                    materializeRows: false));

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                EvaluateLocal(question, group));

        Assert.Empty(block.Matches);
        Assert.Equal(1, block.AcceptedCount);
        Assert.Equal(
            FindPatternSettlementKind.Matched,
            Assert.Single(block.Settlements).Kind);
        Assert.Equal(
            FindMatchCompletion.Exhausted,
            block.MatchCompletion);
        Assert.True(block.IsSourceCoverageComplete);
    }

    [Fact]
    public async Task Evaluation_WindowSettlesMatchesBeforeItsStart()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                [
                    "WorkspaceQueryMember",
                    "WorkspaceQueryExtension",
                    "NoSuchLaterPattern",
                ],
                FindVisibility.Public,
                maximumMatches: 2,
                acceptedRows: new(
                    start: 2,
                    end: 2,
                    materializeRows: true));

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                EvaluateLocal(question, group));

        Assert.Single(block.Matches);
        Assert.Equal(
            "WorkspaceQueryExtension",
            block.Matches[0].MemberName);
        Assert.Equal(
            [
                FindPatternSettlementKind.Matched,
                FindPatternSettlementKind.Matched,
                FindPatternSettlementKind.NotEvaluated,
            ],
            block.Settlements.Select(
                static settlement => settlement.Kind));
    }

    [Fact]
    public async Task Evaluation_WindowDoesNotOpenLaterParticipantAfterEnd()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(path);
        AssemblyReferenceIdentity identity =
            ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local("identity"))
                .Identity;
        int laterOpens = 0;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new(
                        ResolvedAssemblyReference.Create(
                            identity,
                            path: null,
                            () => new MemoryStream(
                                bytes,
                                writable: false),
                            AssemblyResolutionProvenance.Local(
                                "first")),
                        NoResolverAssemblyBindingPolicy.Instance),
                    new(
                        ResolvedAssemblyReference.Create(
                            identity,
                            path: null,
                            () =>
                            {
                                laterOpens++;
                                return new MemoryStream(
                                    bytes,
                                    writable: false);
                            },
                            AssemblyResolutionProvenance.Local(
                                "later")),
                        NoResolverAssemblyBindingPolicy.Instance),
                ]);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                ["WorkspaceQueryMember"],
                FindVisibility.Public,
                maximumMatches: 1,
                acceptedRows: new(
                    start: 1,
                    end: 1,
                    materializeRows: true));

        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                EvaluateLocal(question, group));

        Assert.Single(block.Matches);
        Assert.Equal(0, laterOpens);
        Assert.Single(block.SourceCoverage);
        Assert.Single(block.PopulationGaps);
    }

    [Fact]
    public async Task Evaluation_FailureBeforeWindowEndRemainsIncomplete()
    {
        string path =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(path);
        AssemblyReferenceIdentity identity =
            ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local("identity"))
                .Identity;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new(
                        ResolvedAssemblyReference.Create(
                            identity with { Name = "WrongIdentity" },
                            path: null,
                            () => new MemoryStream(
                                bytes,
                                writable: false),
                            AssemblyResolutionProvenance.Local(
                                "rejected")),
                        NoResolverAssemblyBindingPolicy.Instance),
                    Participant(path, "available"),
                ]);
        MemberFindQuestion question =
            MemberFindQuestion.Create(
                ["WorkspaceQueryMember"],
                FindVisibility.Public,
                maximumMatches: 1,
                acceptedRows: new(
                    start: 1,
                    end: 1,
                    materializeRows: true));

        MemberFindSemanticPopulation population =
            EvaluateLocal(question, group);
        MemberFindBlock block =
            FindSemanticReducer.ReduceMember(
                question,
                population);

        Assert.IsType<MemberFindSourceEvaluation.Rejected>(
            population.Sources[0]);
        Assert.IsType<MemberFindSourceEvaluation.Available>(
            population.Sources[1]);
        Assert.Single(block.Matches);
        Assert.Equal(1, block.AcceptedCount);
        Assert.False(block.IsSourceCoverageComplete);
    }

    private static MemberFindSemanticPopulation EvaluateLocal(
        MemberFindQuestion question,
        AssemblyContextGroup group) =>
        MemberFindSourceEvaluator.EvaluateAssemblyContext(
            question,
            group,
            static (subject, memberOrder) =>
                new(
                    new ExactLibrarySourceCoordinate.Local(
                        new(subject.Identity)),
                    subject.Provenance,
                    packageRequest: null,
                    contextOrder: 0,
                    memberOrder,
                    subject.Identity));

    private static AssemblyContextParticipant Participant(
        string path,
        string provenance) =>
        new(
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    provenance)),
            NoResolverAssemblyBindingPolicy.Instance);

    private static AssemblyContextGroup CreateGroup(
        InspectionWorkspace workspace,
        string path) =>
        workspace.CreateAssemblyContextGroup(
            [
                Participant(
                    path,
                    "Member semantic tests"),
            ]);
}
