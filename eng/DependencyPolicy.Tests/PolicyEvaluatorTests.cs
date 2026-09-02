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
}
