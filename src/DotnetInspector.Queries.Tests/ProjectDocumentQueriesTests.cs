using System.Reflection;

namespace DotnetInspector.Queries.Tests;

public sealed class ProjectDocumentQueriesTests
{
    /// <summary>
    /// The host-neutral project document contract. The surface test below asserts set equality
    /// against the assembly, so a new contract type that skips these gates fails.
    /// </summary>
    private static readonly Type[] ContractTypes =
    [
        typeof(ProjectDependencyCoordinate),
        typeof(ProjectSkillEntry),
        typeof(ProjectAgentGuidanceEntry),
        typeof(ProjectPackageDocumentEntry),
        typeof(ProjectDocumentFailureReason),
        typeof(ProjectDocumentSubjectDisposition),
        typeof(ProjectDocumentFailure),
        typeof(ProjectSkillsRequest),
        typeof(ProjectAgentGuidanceRequest),
        typeof(ProjectPackageDocumentsRequest),
        typeof(ProjectSkillsResult),
        typeof(ProjectAgentGuidanceResult),
        typeof(ProjectPackageDocumentsResult),
        typeof(ProjectSkillsQuery),
        typeof(ProjectAgentGuidanceQuery),
        typeof(ProjectPackageDocumentsQuery),
    ];

    [Fact]
    public void SkillsQuery_OrdersEntriesByPackageThenDocumentPath()
    {
        ProjectSkillsResult result = ProjectSkillsQuery.Execute(
            new ProjectSkillsRequest(
                [
                    Skill("Zulu", "2.0.0", "skills/z/SKILL.md"),
                    Skill("alpha", "1.0.0", "skills/c/SKILL.md"),
                    Skill("Alpha", "1.0.0", "skills/b/SKILL.md"),
                    Skill("Alpha", "1.0.0", "skills/a/SKILL.md"),
                ]));

        Assert.Equal(
            [
                ("Alpha", "skills/a/SKILL.md"),
                ("Alpha", "skills/b/SKILL.md"),
                ("alpha", "skills/c/SKILL.md"),
                ("Zulu", "skills/z/SKILL.md"),
            ],
            result.Skills.Select(entry => (entry.Package.PackageId, entry.DocumentPath)));
    }

    [Fact]
    public void SkillsQuery_KeepsMissingAndUnreadableRowsWithNullContent()
    {
        ProjectSkillsResult result = ProjectSkillsQuery.Execute(
            new ProjectSkillsRequest(
                [
                    Skill("Alpha", "1.0.0", "skills/present/SKILL.md") with
                    {
                        Name = "present",
                        Size = 12,
                        Content = "body",
                    },
                    Skill("Alpha", "1.0.0", "skills/missing/SKILL.md"),
                    Skill("Alpha", "1.0.0", "skills/empty/SKILL.md") with { Content = "" },
                ],
                [
                    ProjectDocumentFailure.Named(
                        Coordinate("Alpha", "1.0.0"),
                        "skills/missing/SKILL.md",
                        ProjectDocumentFailureReason.Missing),
                ]));

        Assert.Equal(
            ["skills/empty/SKILL.md", "skills/missing/SKILL.md", "skills/present/SKILL.md"],
            result.Skills.Select(entry => entry.DocumentPath));
        Assert.Equal("", result.Skills[0].Content);
        Assert.Null(result.Skills[1].Content);
        Assert.Null(result.Skills[1].Size);
        Assert.Null(result.Skills[1].Name);
        Assert.Equal("body", result.Skills[2].Content);

        ProjectDocumentFailure failure = Assert.Single(result.Failures);
        Assert.Equal(ProjectDocumentSubjectDisposition.Named, failure.Disposition);
        Assert.Equal("skills/missing/SKILL.md", failure.DocumentPath);
        Assert.Equal(ProjectDocumentFailureReason.Missing, failure.Reason);
    }

    [Fact]
    public void SkillsQuery_RejectsDuplicateRowIdentity()
    {
        var request = new ProjectSkillsRequest(
            [
                Skill("Alpha", "1.0.0", "skills/a/SKILL.md"),
                Skill("alpha", "1.0.0", "skills/a/SKILL.md"),
            ]);

        InspectionQueryException exception =
            Assert.Throws<InspectionQueryException>(() => ProjectSkillsQuery.Execute(request));
        Assert.Contains("distinct package and document identity", exception.Message);
    }

    [Fact]
    public void SkillsQuery_KeepsDistinctPathsAndVersionsApart()
    {
        ProjectSkillsResult result = ProjectSkillsQuery.Execute(
            new ProjectSkillsRequest(
                [
                    Skill("Alpha", "2.0.0", "skills/a/SKILL.md"),
                    Skill("Alpha", "1.0.0", "skills/a/SKILL.md"),
                    Skill("Alpha", "1.0.0", "skills/b/SKILL.md"),
                ]));

        Assert.Equal(
            [
                ("1.0.0", "skills/a/SKILL.md"),
                ("1.0.0", "skills/b/SKILL.md"),
                ("2.0.0", "skills/a/SKILL.md"),
            ],
            result.Skills.Select(entry => (entry.Package.Version, entry.DocumentPath)));
    }

    [Fact]
    public void AgentGuidanceQuery_KeepsARowForADependencyWithoutGuidance()
    {
        ProjectAgentGuidanceResult result = ProjectAgentGuidanceQuery.Execute(
            new ProjectAgentGuidanceRequest(
                [
                    new ProjectAgentGuidanceEntry(Coordinate("Zulu", "2.0.0")),
                    new ProjectAgentGuidanceEntry(
                        Coordinate("Alpha", "1.0.0"),
                        "AGENTS.md",
                        "alpha",
                        Description: null,
                        Size: 4,
                        Content: "body"),
                    new ProjectAgentGuidanceEntry(
                        Coordinate("Mike", "1.5.0"),
                        "AGENTS.md",
                        Size: 9),
                ],
                [
                    ProjectDocumentFailure.Named(
                        Coordinate("Mike", "1.5.0"),
                        "AGENTS.md",
                        ProjectDocumentFailureReason.Unreadable),
                ]));

        Assert.Equal(
            ["Alpha", "Mike", "Zulu"],
            result.Guidance.Select(entry => entry.Package.PackageId));
        Assert.Equal("body", result.Guidance[0].Content);
        Assert.Null(result.Guidance[1].Content);
        Assert.Equal("AGENTS.md", result.Guidance[1].DocumentPath);
        Assert.Null(result.Guidance[2].Content);
        Assert.Null(result.Guidance[2].DocumentPath);
        Assert.Equal(
            ProjectDocumentFailureReason.Unreadable,
            Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void AgentGuidanceQuery_RejectsDuplicateRowIdentity()
    {
        var request = new ProjectAgentGuidanceRequest(
            [
                new ProjectAgentGuidanceEntry(Coordinate("Alpha", "1.0.0"), "AGENTS.md"),
                new ProjectAgentGuidanceEntry(Coordinate("alpha", "1.0.0"), "docs/AGENTS.md"),
            ]);

        Assert.Throws<InspectionQueryException>(
            () => ProjectAgentGuidanceQuery.Execute(request));
    }

    [Fact]
    public void PackageDocumentsQuery_OrdersDocumentsAndKeepsNullContent()
    {
        ProjectPackageDocumentsResult result = ProjectPackageDocumentsQuery.Execute(
            new ProjectPackageDocumentsRequest(
                [
                    new ProjectPackageDocumentEntry(
                        Coordinate("Zulu", "2.0.0"),
                        "README.md",
                        Size: 1,
                        Content: "z"),
                    new ProjectPackageDocumentEntry(Coordinate("Mike", "1.5.0")),
                    new ProjectPackageDocumentEntry(
                        Coordinate("Alpha", "1.0.0"),
                        "PROJECT.md",
                        Size: 1,
                        Content: "a"),
                ],
                [
                    ProjectDocumentFailure.Named(
                        Coordinate("Mike", "1.5.0"),
                        documentPath: null,
                        ProjectDocumentFailureReason.Unacquired),
                ]));

        Assert.Equal(
            ["Alpha", "Mike", "Zulu"],
            result.Documents.Select(document => document.Package.PackageId));
        Assert.Null(result.Documents[1].Content);
        Assert.Null(result.Documents[1].DocumentPath);
        Assert.Null(result.Documents[1].Size);

        ProjectDocumentFailure failure = Assert.Single(result.Failures);
        Assert.Equal(ProjectDocumentFailureReason.Unacquired, failure.Reason);
        Assert.Null(failure.DocumentPath);
        Assert.Equal("Mike", failure.Package?.PackageId);
    }

    [Fact]
    public void PackageDocumentsQuery_RejectsDuplicateRowIdentity()
    {
        var request = new ProjectPackageDocumentsRequest(
            [
                new ProjectPackageDocumentEntry(Coordinate("Alpha", "1.0.0"), "README.md"),
                new ProjectPackageDocumentEntry(Coordinate("ALPHA", "1.0.0"), "PROJECT.md"),
            ]);

        Assert.Throws<InspectionQueryException>(
            () => ProjectPackageDocumentsQuery.Execute(request));
    }

    [Fact]
    public void Failures_OrderNamedSubjectsBeforeRedactedFailures()
    {
        ProjectSkillsResult result = ProjectSkillsQuery.Execute(
            new ProjectSkillsRequest(
                [],
                [
                    ProjectDocumentFailure.Redacted(
                        ProjectDocumentFailureReason.InvalidMetadata),
                    ProjectDocumentFailure.Named(
                        Coordinate("Zulu", "2.0.0"),
                        "skills/z/SKILL.md",
                        ProjectDocumentFailureReason.Missing),
                    ProjectDocumentFailure.Named(
                        Coordinate("Alpha", "1.0.0"),
                        "skills/b/SKILL.md",
                        ProjectDocumentFailureReason.Unreadable),
                    ProjectDocumentFailure.Named(
                        Coordinate("Alpha", "1.0.0"),
                        "skills/a/SKILL.md",
                        ProjectDocumentFailureReason.Missing),
                ]));

        Assert.Equal(
            [
                ("Alpha", "skills/a/SKILL.md"),
                ("Alpha", "skills/b/SKILL.md"),
                ("Zulu", "skills/z/SKILL.md"),
                (null, null),
            ],
            result.Failures.Select(failure => (failure.Package?.PackageId, failure.DocumentPath)));
        Assert.Equal(
            ProjectDocumentSubjectDisposition.Redacted,
            result.Failures[^1].Disposition);
    }

    [Fact]
    public void RedactedFailure_CarriesNoPackageAuthoredIdentity()
    {
        ProjectDocumentFailure failure =
            ProjectDocumentFailure.Redacted(ProjectDocumentFailureReason.InvalidMetadata);

        Assert.Equal(ProjectDocumentSubjectDisposition.Redacted, failure.Disposition);
        Assert.Null(failure.Package);
        Assert.Null(failure.DocumentPath);
        Assert.Equal(
            "A project document declares metadata that does not satisfy its contract.",
            failure.Message);
    }

    /// <summary>
    /// The non-vacuity gate for the redaction invariant: a redacted failure carries no subject
    /// only because the two factories are the only way to build one.
    /// </summary>
    [Fact]
    public void Failure_HasNoConstructionPathBesideItsFactories()
    {
        Assert.Empty(
            typeof(ProjectDocumentFailure).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(
            ["Named", "Redacted"],
            typeof(ProjectDocumentFailure)
                .GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Order());
        Assert.DoesNotContain(
            typeof(ProjectDocumentFailure).GetProperties(
                BindingFlags.Public | BindingFlags.Instance),
            property => property.CanWrite);
    }

    [Fact]
    public void FailureMessage_IsStableForEveryReason()
    {
        Dictionary<ProjectDocumentFailureReason, string> expected = new()
        {
            [ProjectDocumentFailureReason.Missing] =
                "A document the restored project lists is missing from the package.",
            [ProjectDocumentFailureReason.Unreadable] =
                "A project document could not be read.",
            [ProjectDocumentFailureReason.Unacquired] =
                "A project document could not be acquired from the package that declares it.",
            [ProjectDocumentFailureReason.InvalidMetadata] =
                "A project document declares metadata that does not satisfy its contract.",
        };

        Assert.Equal(
            Enum.GetValues<ProjectDocumentFailureReason>().Order(),
            expected.Keys.Order());
        foreach ((ProjectDocumentFailureReason reason, string message) in expected)
        {
            Assert.Equal(message, ProjectDocumentFailure.Redacted(reason).Message);
            Assert.NotEqual(UnknownReasonMessage, message);
        }
    }

    [Fact]
    public void FailureMessage_IsSafeForUnknownFutureReason()
    {
        ProjectDocumentFailure failure =
            ProjectDocumentFailure.Redacted((ProjectDocumentFailureReason)int.MaxValue);

        Assert.Equal(UnknownReasonMessage, failure.Message);
    }

    [Fact]
    public void Results_DoNotDependOnHostEnumerationOrder()
    {
        ProjectSkillEntry[] entries =
        [
            Skill("Zulu", "2.0.0", "skills/z/SKILL.md"),
            Skill("Alpha", "1.0.0", "skills/b/SKILL.md"),
            Skill("Alpha", "1.0.0", "skills/a/SKILL.md"),
            Skill("Mike", "1.5.0", "skills/m/SKILL.md"),
        ];
        (string PackageId, string DocumentPath)[] expected =
        [
            .. ProjectSkillsQuery.Execute(new ProjectSkillsRequest(entries))
                .Skills
                .Select(entry => (entry.Package.PackageId, entry.DocumentPath)),
        ];

        foreach (ProjectSkillEntry[] permutation in Permutations(entries))
        {
            Assert.Equal(
                expected,
                ProjectSkillsQuery.Execute(new ProjectSkillsRequest(permutation))
                    .Skills
                    .Select(entry => (entry.Package.PackageId, entry.DocumentPath)));
        }
    }

    [Fact]
    public void Requests_MaterializeInputSoLaterMutationCannotChangeResults()
    {
        List<ProjectSkillEntry> skills = [Skill("Alpha", "1.0.0", "skills/a/SKILL.md")];
        List<ProjectDocumentFailure> failures =
            [ProjectDocumentFailure.Redacted(ProjectDocumentFailureReason.InvalidMetadata)];
        var request = new ProjectSkillsRequest(skills, failures);

        skills.Add(Skill("Zulu", "2.0.0", "skills/z/SKILL.md"));
        failures.Add(
            ProjectDocumentFailure.Redacted(ProjectDocumentFailureReason.Unreadable));
        ProjectSkillsResult result = ProjectSkillsQuery.Execute(request);

        Assert.Equal("skills/a/SKILL.md", Assert.Single(result.Skills).DocumentPath);
        Assert.Equal(
            ProjectDocumentFailureReason.InvalidMetadata,
            Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void Requests_RejectMissingRowCollections()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ProjectSkillsRequest(null!));
        Assert.Throws<ArgumentNullException>(
            () => new ProjectAgentGuidanceRequest(null!));
        Assert.Throws<ArgumentNullException>(
            () => new ProjectPackageDocumentsRequest(null!));
        Assert.Throws<ArgumentException>(
            () => new ProjectSkillsRequest([null!]));
    }

    [Fact]
    public void Definitions_DeclareTruthfulCostsUnderDemand()
    {
        InspectionQueryRegistry<ProjectDocumentQueryContext> registry = CreateRegistry();

        Assert.Equal(
            InspectionCost.NetworkFree,
            registry.CostOf(ProjectSkillsQuery.Definition));
        Assert.Equal(
            InspectionCost.NetworkFree,
            registry.CostOf(ProjectAgentGuidanceQuery.Definition));
        Assert.Equal(
            InspectionCost.Unbounded,
            registry.CostOf(ProjectPackageDocumentsQuery.Definition));
    }

    [Fact]
    public void RegistryRun_ExecutesOnlyDemandedProjectDocumentQueries()
    {
        InspectionQueryRegistry<ProjectDocumentQueryContext> registry = CreateRegistry();
        var context = new ProjectDocumentQueryContext();

        InspectionQueryResults results = registry.Run(
            [ProjectSkillsQuery.Definition, ProjectAgentGuidanceQuery.Definition],
            context);

        Assert.True(results.TryGet(ProjectSkillsQuery.Definition, out ProjectSkillsResult? skills));
        Assert.Equal("skills/a/SKILL.md", Assert.Single(skills!.Skills).DocumentPath);
        Assert.True(results.TryGet(ProjectAgentGuidanceQuery.Definition, out _));
        Assert.False(results.TryGet(ProjectPackageDocumentsQuery.Definition, out _));
        Assert.Equal(1, context.SkillReads);
        Assert.Equal(1, context.AgentGuidanceReads);
        Assert.Equal(0, context.PackageDocumentReads);
    }

    [Fact]
    public void ProjectDocumentContracts_AreTheDeclaredHostNeutralSurface()
    {
        Assert.Equal(
            ContractTypes.Select(type => type.FullName).Order(),
            typeof(ProjectSkillsQuery).Assembly
                .GetExportedTypes()
                .Where(type =>
                    !type.IsNested
                    && type.Namespace == "DotnetInspector.Queries"
                    && type.Name.StartsWith("Project", StringComparison.Ordinal))
                .Select(type => type.FullName)
                .Order());
    }

    /// <summary>
    /// The boundary gate: these contracts carry already-acquired typed input, so no host
    /// filesystem, package-client, Markout, or presentation type may appear on their surface.
    /// </summary>
    [Fact]
    public void ProjectDocumentContracts_ExposeNoHostOrAcquisitionTypes()
    {
        string[] allowedNamespaces =
        [
            "System",
            "System.Collections.Generic",
            "System.Collections.Immutable",
            "DotnetInspector.Queries",
        ];

        foreach (Type contract in ContractTypes)
        {
            foreach (Type used in SurfaceTypes(contract))
            {
                Assert.Contains(used.Namespace ?? "", allowedNamespaces);
            }
        }
    }

    private const string UnknownReasonMessage = "A project document could not be inspected.";

    private static IEnumerable<Type> SurfaceTypes(Type contract)
    {
        const BindingFlags Surface =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        IEnumerable<Type> declared =
        [
            .. contract.GetProperties(Surface).Select(property => property.PropertyType),
            .. contract.GetConstructors(Surface)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType),
            .. contract.GetMethods(Surface)
                .Where(method => !method.IsSpecialName)
                .SelectMany(method =>
                    method.GetParameters()
                        .Select(parameter => parameter.ParameterType)
                        .Append(method.ReturnType)),
        ];
        return declared.SelectMany(Flatten).Distinct();
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        Type element = type.IsByRef || type.IsArray ? type.GetElementType()! : type;
        yield return element;
        foreach (Type argument in element.GetGenericArguments())
        {
            foreach (Type flattened in Flatten(argument))
                yield return flattened;
        }
    }

    private static InspectionQueryRegistry<ProjectDocumentQueryContext> CreateRegistry()
        => new InspectionQueryRegistry<ProjectDocumentQueryContext>()
            .Add(ProjectSkillsQuery.Definition, static context => context.ReadSkills())
            .Add(
                ProjectAgentGuidanceQuery.Definition,
                static context => context.ReadAgentGuidance())
            .Add(
                ProjectPackageDocumentsQuery.Definition,
                static context => context.ReadPackageDocuments());

    private static ProjectDependencyCoordinate Coordinate(string packageId, string version)
        => new(packageId, version);

    private static ProjectSkillEntry Skill(string packageId, string version, string documentPath)
        => new(Coordinate(packageId, version), documentPath);

    private static IEnumerable<TItem[]> Permutations<TItem>(IReadOnlyList<TItem> items)
    {
        if (items.Count <= 1)
        {
            yield return [.. items];
            yield break;
        }

        for (int index = 0; index < items.Count; index++)
        {
            TItem head = items[index];
            TItem[] rest = [.. items.Where((_, position) => position != index)];
            foreach (TItem[] permutation in Permutations(rest))
                yield return [head, .. permutation];
        }
    }

    /// <summary>
    /// A host context that records which project document reads a demanded query performed.
    /// </summary>
    private sealed class ProjectDocumentQueryContext
    {
        public int SkillReads { get; private set; }

        public int AgentGuidanceReads { get; private set; }

        public int PackageDocumentReads { get; private set; }

        public ProjectSkillsResult ReadSkills()
        {
            SkillReads++;
            return ProjectSkillsQuery.Execute(
                new ProjectSkillsRequest([Skill("Alpha", "1.0.0", "skills/a/SKILL.md")]));
        }

        public ProjectAgentGuidanceResult ReadAgentGuidance()
        {
            AgentGuidanceReads++;
            return ProjectAgentGuidanceQuery.Execute(
                new ProjectAgentGuidanceRequest(
                    [new ProjectAgentGuidanceEntry(Coordinate("Alpha", "1.0.0"))]));
        }

        public ProjectPackageDocumentsResult ReadPackageDocuments()
        {
            PackageDocumentReads++;
            return ProjectPackageDocumentsQuery.Execute(
                new ProjectPackageDocumentsRequest(
                    [new ProjectPackageDocumentEntry(Coordinate("Alpha", "1.0.0"))]));
        }
    }
}
