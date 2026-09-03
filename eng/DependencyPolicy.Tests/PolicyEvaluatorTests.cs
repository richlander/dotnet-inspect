using System.Collections.Immutable;
using System.Text.Json;

namespace DependencyPolicy.Tests;

public sealed class PolicyEvaluatorTests
{
    [Fact]
    public void AllowOnly_DistinguishesPlatformRepositoryAndExternalAssemblies()
    {
        RepositoryDependencyGraph graph = RepositoryDependencyGraph.Create(
            [
                Node(
                    "Engine",
                    assemblyReferences:
                    [
                        "System.Runtime",
                        "Repo.Dependency",
                        "Approved.External",
                        "Unexpected.External",
                    ]),
                Node("Repo.Dependency"),
            ],
            ["System.Runtime"]);
        DependencyPolicyDocument policy = Policy(
            new DependencyRule
            {
                Id = "external",
                Source = "docs/dependency-policy.md",
                Graphs = [DependencyGraphKind.Assembly],
                Targets = ["Engine"],
                AllowOnly =
                [
                    "$platform",
                    "$repository",
                    "Approved.External",
                ],
            });

        DependencyViolation violation = Assert.Single(
            PolicyEvaluator.Evaluate(policy, graph));

        Assert.Equal("Engine", violation.Target);
        Assert.Equal("Unexpected.External", violation.Dependency);
        Assert.Equal(DependencyGraphKind.Assembly, violation.Graph);
    }

    [Fact]
    public void Deny_AppliesTargetExclusionsAndDependencyExceptions()
    {
        RepositoryDependencyGraph graph = RepositoryDependencyGraph.Create(
            [
                Node(
                    "ILInspector.Metadata",
                    projectReferences:
                    [
                        "DotnetInspector.Artifacts",
                        "DotnetInspector.Services",
                    ]),
                Node(
                    "ILInspector.Metadata.Tests",
                    projectReferences: ["DotnetInspector.Services"]),
                Node("DotnetInspector.Artifacts"),
                Node("DotnetInspector.Services"),
            ]);
        DependencyPolicyDocument policy = Policy(
            new DependencyRule
            {
                Id = "engine-below-tool",
                Source = "docs/design/inspection-layers.md#seam-rules",
                Graphs = [DependencyGraphKind.Project],
                Targets = ["ILInspector.*"],
                ExcludeTargets = ["*.Tests"],
                Deny = ["DotnetInspector.*"],
                Except = ["DotnetInspector.Artifacts"],
            });

        DependencyViolation violation = Assert.Single(
            PolicyEvaluator.Evaluate(policy, graph));

        Assert.Equal("ILInspector.Metadata", violation.Target);
        Assert.Equal("DotnetInspector.Services", violation.Dependency);
    }

    [Fact]
    public void AllowOnly_EmptyProjectSetAndPlatformAssemblyKeepLeafClean()
    {
        RepositoryDependencyGraph graph = RepositoryDependencyGraph.Create(
            [
                Node(
                    "Leaf",
                    assemblyReferences: ["System.Runtime"]),
            ],
            ["System.Runtime"]);
        DependencyPolicyDocument policy = Policy(
            new DependencyRule
            {
                Id = "leaf",
                Source = "docs/overview.md",
                Graphs =
                [
                    DependencyGraphKind.Project,
                    DependencyGraphKind.Assembly,
                ],
                Targets = ["Leaf"],
                AllowOnly = ["$platform"],
            });

        Assert.Empty(PolicyEvaluator.Evaluate(policy, graph));
    }

    [Fact]
    public void AllowOnly_RepositoryTokenDoesNotPermitProjectReference()
    {
        RepositoryDependencyGraph graph = RepositoryDependencyGraph.Create(
            [
                Node(
                    "Engine",
                    projectReferences: ["Repo.Dependency"],
                    assemblyReferences: ["Repo.Dependency"]),
                Node("Repo.Dependency"),
            ]);
        DependencyPolicyDocument policy = Policy(
            new DependencyRule
            {
                Id = "combined",
                Source = "docs/dependency-policy.md",
                Graphs =
                [
                    DependencyGraphKind.Project,
                    DependencyGraphKind.Assembly,
                ],
                Targets = ["Engine"],
                AllowOnly = ["$repository"],
            });

        DependencyViolation violation = Assert.Single(
            PolicyEvaluator.Evaluate(policy, graph));

        Assert.Equal(DependencyGraphKind.Project, violation.Graph);
        Assert.Equal("Repo.Dependency", violation.Dependency);
    }

    [Fact]
    public void Evaluate_RejectsVacuousTargetPattern()
    {
        RepositoryDependencyGraph graph = RepositoryDependencyGraph.Create(
            [Node("Present")]);
        DependencyPolicyDocument policy = Policy(
            new DependencyRule
            {
                Id = "missing",
                Source = "docs/overview.md",
                Graphs = [DependencyGraphKind.Project],
                Targets = ["Absent"],
                AllowOnly = [],
            });

        DependencyPolicyException exception = Assert.Throws<
            DependencyPolicyException>(
                () => PolicyEvaluator.Evaluate(policy, graph));

        Assert.Contains("selects no projects", exception.Message);
    }

    [Fact]
    public void Evaluate_RequiresEveryTargetPatternToMatch()
    {
        RepositoryDependencyGraph graph = RepositoryDependencyGraph.Create(
            [Node("Present")]);
        DependencyPolicyDocument policy = Policy(
            new DependencyRule
            {
                Id = "partly-missing",
                Source = "docs/overview.md",
                Graphs = [DependencyGraphKind.Project],
                Targets = ["Present", "Absent"],
                AllowOnly = [],
            });

        DependencyPolicyException exception = Assert.Throws<
            DependencyPolicyException>(
                () => PolicyEvaluator.Evaluate(policy, graph));

        Assert.Contains("'Absent' selects no projects", exception.Message);
    }

    [Fact]
    public void ProjectPathFilter_ExcludesLookalikeFixture()
    {
        RepositoryDependencyGraph graph = RepositoryDependencyGraph.Create(
            [
                Node(
                    "ILInspector.Product",
                    "src/ILInspector.Product/ILInspector.Product.csproj",
                    projectReferences: ["DotnetInspector.Services"]),
                Node(
                    "ILInspector.ProductFixture",
                    "tests/Fixtures/ILInspector.ProductFixture.csproj",
                    projectReferences: ["DotnetInspector.Services"]),
                Node("DotnetInspector.Services"),
            ]);
        DependencyPolicyDocument policy = Policy(
            new DependencyRule
            {
                Id = "product-only",
                Source = "docs/overview.md",
                Graphs = [DependencyGraphKind.Project],
                Targets = ["ILInspector.*"],
                ProjectPaths = ["src/*/*.csproj"],
                Deny = ["DotnetInspector.*"],
            });

        DependencyViolation violation = Assert.Single(
            PolicyEvaluator.Evaluate(policy, graph));

        Assert.Equal("ILInspector.Product", violation.Target);
    }

    [Fact]
    public void AssemblyRule_RequiresBuiltTargetEvidence()
    {
        RepositoryDependencyGraph graph = RepositoryDependencyGraph.Create(
            [
                new(
                    "Missing",
                    "Missing.csproj",
                    [],
                    null,
                    []),
            ]);
        DependencyPolicyDocument policy = Policy(
            new DependencyRule
            {
                Id = "assembly",
                Source = "docs/overview.md",
                Graphs = [DependencyGraphKind.Assembly],
                Targets = ["Missing"],
                AllowOnly = ["$platform"],
            });

        DependencyPolicyException exception = Assert.Throws<
            DependencyPolicyException>(
                () => PolicyEvaluator.Evaluate(policy, graph));

        Assert.Contains("Build the solution", exception.Message);
    }

    [Fact]
    public void Loader_RejectsUnknownJsonMember()
    {
        string json =
            """
            {
              "schemaVersion": 1,
              "solution": "dotnet-inspect.slnx",
              "configuration": "Release",
              "rules": [],
              "unexpected": true
            }
            """;

        Assert.Throws<JsonException>(() => PolicyLoader.Deserialize(json));
    }

    [Fact]
    public void Loader_AcceptsCanonicalCamelCaseDocument()
    {
        string json =
            """
            {
              "schemaVersion": 1,
              "solution": "dotnet-inspect.slnx",
              "configuration": "Release",
              "rules": [
                {
                  "id": "leaf",
                  "source": "docs/overview.md",
                  "graphs": [ "project" ],
                  "targets": [ "Leaf" ],
                  "allowOnly": []
                }
              ]
            }
            """;

        DependencyPolicyDocument document = PolicyLoader.Deserialize(json);

        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal("leaf", Assert.Single(document.Rules).Id);
    }

    [Fact]
    public void Loader_RejectsTokensInDenyPatterns()
    {
        string json =
            """
            {
              "schemaVersion": 1,
              "solution": "dotnet-inspect.slnx",
              "configuration": "Release",
              "rules": [
                {
                  "id": "invalid",
                  "source": "docs/overview.md",
                  "graphs": [ "assembly" ],
                  "targets": [ "Target" ],
                  "deny": [ "$platform" ]
                }
              ]
            }
            """;

        DependencyPolicyException exception = Assert.Throws<
            DependencyPolicyException>(
                () => PolicyLoader.Deserialize(json));

        Assert.Contains("may not contain token", exception.Message);
    }

    [Fact]
    public void App_ReportsNullExceptAsConfigurationError()
    {
        string rulesPath = Path.Combine(
            Path.GetTempPath(),
            $"dependency-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            rulesPath,
            """
            {
              "schemaVersion": 1,
              "solution": "dotnet-inspect.slnx",
              "configuration": "Release",
              "rules": [
                {
                  "id": "invalid",
                  "source": "docs/overview.md",
                  "graphs": [ "assembly" ],
                  "targets": [ "Target" ],
                  "allowOnly": [ "$platform" ],
                  "except": null
                }
              ]
            }
            """);
        TextWriter originalError = Console.Error;
        using var error = new StringWriter();

        try
        {
            Console.SetError(error);

            int exitCode = DependencyPolicyApp.Run(
                ["--rules", rulesPath]);

            Assert.Equal(2, exitCode);
            Assert.StartsWith("error DP0002:", error.ToString());
            Assert.Contains("dependency exceptions", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            File.Delete(rulesPath);
        }
    }

    [Fact]
    public void App_ReportsUnstartableDotnetHostAsConfigurationError()
    {
        string dotnetHost = Path.Combine(
            Path.GetTempPath(),
            $"missing-dotnet-{Guid.NewGuid():N}");
        TextWriter originalError = Console.Error;
        using var error = new StringWriter();

        try
        {
            Console.SetError(error);

            int exitCode = DependencyPolicyApp.Run(
                [
                    "--repository",
                    FindRepositoryRoot(),
                    "--dotnet",
                    dotnetHost,
                ]);

            Assert.Equal(2, exitCode);
            Assert.StartsWith("error DP0002:", error.ToString());
            Assert.Contains(
                $"Could not start '{dotnetHost}'",
                error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void CheckedInBroadProductRulesExcludeCallerGraphFixtures()
    {
        string repository = FindRepositoryRoot();
        DependencyPolicyDocument policy = PolicyLoader.Load(
            Path.Combine(repository, "eng", "dependency-policy.json"));
        string[] fixtureNames = Directory
            .EnumerateDirectories(
                Path.Combine(repository, "src"),
                "ILInspector.Analysis.CallerGraph*")
            .Select(path => new DirectoryInfo(path).Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(fixtureNames);

        foreach (string ruleId in new[]
        {
            "engine-libraries-stay-below-tool-libraries",
            "product-libraries-use-repository-and-platform-assemblies",
        })
        {
            DependencyRule rule = Assert.Single(
                policy.Rules,
                candidate => candidate.Id == ruleId);
            foreach (string fixtureName in fixtureNames)
            {
                Assert.False(
                    DependencyPattern.Selects(
                        rule,
                        fixtureName,
                        $"src/{fixtureName}/{fixtureName}.csproj"),
                    $"{ruleId} selects caller-graph fixture {fixtureName}.");
            }
        }
    }

    private static ProjectDependencyNode Node(
        string name,
        string? projectPath = null,
        string[]? projectReferences = null,
        string[]? assemblyReferences = null) =>
        new(
            name,
            projectPath ?? $"{name}.csproj",
            (projectReferences ?? []).ToImmutableArray(),
            name,
            (assemblyReferences ?? []).ToImmutableArray());

    private static DependencyPolicyDocument Policy(
        params DependencyRule[] rules) =>
        new()
        {
            SchemaVersion = 1,
            Solution = "dotnet-inspect.slnx",
            Configuration = "Release",
            Rules = rules,
        };

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }
}
