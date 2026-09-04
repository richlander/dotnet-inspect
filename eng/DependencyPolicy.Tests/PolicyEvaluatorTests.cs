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
    public void Graph_RejectsRepositoryAndPlatformAssemblyNameCollision()
    {
        DependencyPolicyException exception = Assert.Throws<
            DependencyPolicyException>(
                () => RepositoryDependencyGraph.Create(
                    [Node("System.Runtime")],
                    ["System.Runtime"]));

        Assert.Contains(
            "collide with platform assemblies: [System.Runtime]",
            exception.Message);
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
    public void App_ReportsEmptyEnvironmentDotnetHostAsConfigurationError()
    {
        const string variable = "DOTNET_HOST_PATH";
        string? originalHost = Environment.GetEnvironmentVariable(variable);
        TextWriter originalError = Console.Error;
        using var error = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable(variable, "");
            Console.SetError(error);

            int exitCode = DependencyPolicyApp.Run(
                ["--repository", FindRepositoryRoot()]);

            Assert.Equal(2, exitCode);
            Assert.Equal(
                $"error DP0002: The dotnet host path must be non-empty."
                + Environment.NewLine,
                error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Environment.SetEnvironmentVariable(variable, originalHost);
        }
    }

    [Fact]
    public void App_ReportsMalformedSolutionPathAsConfigurationError()
    {
        string rulesPath = Path.Combine(
            Path.GetTempPath(),
            $"dependency-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            rulesPath,
            """
            {
              "schemaVersion": 1,
              "solution": "dotnet\u0000-inspect.slnx",
              "configuration": "Release",
              "rules": [
                {
                  "id": "leaf",
                  "source": "docs/dependency-policy.md",
                  "graphs": [ "project" ],
                  "targets": [ "DependencyPolicy" ],
                  "allowOnly": []
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
                [
                    "--repository",
                    FindRepositoryRoot(),
                    "--rules",
                    rulesPath,
                ]);

            Assert.Equal(2, exitCode);
            Assert.Equal(
                "error DP0002: Dependency policy solution path is invalid."
                + Environment.NewLine,
                error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            File.Delete(rulesPath);
        }
    }

    [Fact]
    public void App_ReportsViolationsWithDeterministicDiagnosticAndExitCode()
    {
        ImmutableArray<DependencyViolation> violations =
        [
            new(
                "leaf",
                "docs/overview.md",
                DependencyGraphKind.Project,
                "Leaf",
                "Repository.Dependency"),
        ];
        using var error = new StringWriter();

        int exitCode = DependencyPolicyApp.ReportViolations(
            violations,
            error);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "error DP0001: leaf [project] Leaf -> Repository.Dependency "
            + "is not permitted (docs/overview.md)"
            + Environment.NewLine
            + "Dependency policy failed with 1 violation(s)."
            + Environment.NewLine,
            error.ToString());
    }

    [Fact]
    public void Reader_IncludesBuildOnlyProjectReferenceAndAssemblyClosure()
    {
        string repository = FindRepositoryRoot();
        DependencyPolicyDocument policy = Policy(
            new DependencyRule
            {
                Id = "build-only",
                Source = "docs/dependency-policy.md",
                Graphs = [DependencyGraphKind.Project],
                Targets = ["DotnetInspector.Queries.Tests"],
                Deny = ["ILInspector.Analysis.CallerGraphTarget"],
            },
            new DependencyRule
            {
                Id = "assembly-closure",
                Source = "docs/dependency-policy.md",
                Graphs = [DependencyGraphKind.Assembly],
                Targets = ["DependencyPolicy"],
                AllowOnly = ["$platform", "$repository"],
            });
        RepositoryDependencyGraph graph = RepositoryGraphReader.Read(
            repository,
            Path.Combine(repository, "dotnet-inspect.slnx"),
            "Release",
            FindDotnetHost(),
            policy);

        DependencyViolation violation = Assert.Single(
            PolicyEvaluator.Evaluate(policy, graph));

        Assert.Equal("DotnetInspector.Queries.Tests", violation.Target);
        Assert.Equal(
            "ILInspector.Analysis.CallerGraphTarget",
            violation.Dependency);
        Assert.Equal(DependencyGraphKind.Project, violation.Graph);
        Assert.Equal(
            "DependencyPolicy",
            graph.Projects["DependencyPolicy"].AssemblyName);
        Assert.Equal(
            "ILInspector.Metadata",
            graph.Projects["ILInspector.Metadata"].AssemblyName);
    }

    [Fact]
    public void Reader_RejectsEmptyTargetPath()
    {
        DependencyPolicyException exception = Assert.Throws<
            DependencyPolicyException>(
                () => RepositoryGraphReader.NormalizeTargetPath(
                    Path.Combine("repo", "Library.csproj"),
                    ""));

        Assert.Contains("invalid TargetPath", exception.Message);
    }

    [Fact]
    public void Reader_RejectsMissingOutput()
    {
        string candidate = Path.Combine(
            Path.GetTempPath(),
            $"missing-output-{Guid.NewGuid():N}.dll");

        DependencyPolicyException exception = Assert.Throws<
            DependencyPolicyException>(
                () => RepositoryGraphReader.ReadAssemblyOutput(
                    "Missing",
                    candidate));

        Assert.Contains(
            "Release target output for 'Missing' does not exist",
            exception.Message);
    }

    [Fact]
    public void Reader_RejectsIncompleteAssemblyReferences()
    {
        DependencyPolicyException exception = Assert.Throws<
            DependencyPolicyException>(
                () => RepositoryGraphReader.ValidateAssemblyIdentity(
                    "Library.dll",
                    new(
                        "Library",
                        ["System.Runtime"],
                        ReferencesComplete: false)));

        Assert.Contains(
            "Could not decode every assembly reference",
            exception.Message);
    }

    [Fact]
    public void Reader_RejectsNativeOutput()
    {
        DependencyPolicyException exception = Assert.Throws<
            DependencyPolicyException>(
                () => RepositoryGraphReader.ReadAssemblyOutput(
                    "NativeHost",
                    FindDotnetHost()));

        Assert.True(
            exception.Message.Contains(
                "has no managed metadata",
                StringComparison.Ordinal)
            || exception.Message.Contains(
                "Could not inspect built output",
                StringComparison.Ordinal),
            exception.Message);
    }

    [Fact]
    public void CheckedInPolicyTreatsTsJsExportContractsAsDependencyFree()
    {
        string repository = FindRepositoryRoot();
        DependencyPolicyDocument policy = PolicyLoader.Load(
            Path.Combine(repository, "eng", "dependency-policy.json"));
        DependencyRule rule = Assert.Single(
            policy.Rules,
            candidate => candidate.Id == "dependency-free-contract-floors");
        Assert.Contains("TsJsExport.Contracts", rule.Targets);
        RepositoryDependencyGraph graph = RepositoryDependencyGraph.Create(
            [
                Node(
                    "TsJsExport.Contracts",
                    projectReferences: ["Repository.Dependency"],
                    assemblyReferences: ["Repository.Dependency"]),
                Node("Repository.Dependency"),
            ]);

        DependencyViolation[] violations = PolicyEvaluator
            .Evaluate(
                new DependencyPolicyDocument
                {
                    SchemaVersion = policy.SchemaVersion,
                    Solution = policy.Solution,
                    Configuration = policy.Configuration,
                    Rules =
                    [
                        new DependencyRule
                        {
                            Id = rule.Id,
                            Source = rule.Source,
                            Graphs = rule.Graphs,
                            Targets = ["TsJsExport.Contracts"],
                            AllowOnly = rule.AllowOnly,
                        },
                    ],
                },
                graph)
            .ToArray();

        Assert.Equal(2, violations.Length);
        Assert.Contains(
            violations,
            violation => violation.Graph == DependencyGraphKind.Project);
        Assert.Contains(
            violations,
            violation => violation.Graph == DependencyGraphKind.Assembly);
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

    [Fact]
    public void CheckedInEcosystemRuleCoversEverySourceProductExceptTheCli()
    {
        string repository = FindRepositoryRoot();
        DependencyPolicyDocument policy = PolicyLoader.Load(
            Path.Combine(repository, "eng", "dependency-policy.json"));
        DependencyRule rule = Assert.Single(
            policy.Rules,
            candidate => candidate.Id
                == "ecosystem-catalog-stays-in-approved-hosts");
        string[] productProjects = Directory
            .EnumerateFiles(
                Path.Combine(repository, "src"),
                "*.csproj",
                SearchOption.AllDirectories)
            .Where(path =>
                Path.GetDirectoryName(Path.GetDirectoryName(path))
                    == Path.Combine(repository, "src"))
            .Where(path =>
            {
                string name = Path.GetFileNameWithoutExtension(path);
                return !name.EndsWith(".Tests", StringComparison.Ordinal)
                    && !name.Contains("Fixture", StringComparison.Ordinal);
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(productProjects);
        Assert.All(
            productProjects.Where(path =>
                Path.GetFileNameWithoutExtension(path) != "dotnet-inspect"),
            path =>
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string relativePath = Path.GetRelativePath(repository, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                Assert.True(
                    DependencyPattern.Selects(rule, name, relativePath),
                    $"{rule.Id} does not select {relativePath}.");
            });
        Assert.False(
            DependencyPattern.Selects(
                rule,
                "dotnet-inspect",
                "src/dotnet-inspect/dotnet-inspect.csproj"));
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

    private static string FindDotnetHost()
    {
        string? host = Environment.GetEnvironmentVariable(
            "DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(host))
        {
            return host;
        }

        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            string candidate = Path.Combine(
                root,
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "dotnet";
    }
}
