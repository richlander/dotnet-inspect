using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Packages;
using QuerySpace;
using DotnetInspector.QueryOperations;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public partial class PackageQueryTests
{
    [Fact]
    public async Task ExecuteAsync_DependsTermsUseNuGetIdentityAndRetainRanges()
    {
        SearchResult[] candidates =
        [
            Match("Microsoft.Extensions.Hosting"),
            Match("Microsoft.Extensions.Logging"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["microsoft.extensions.hosting@1.0.0"] = Manifest(
                    "Microsoft.Extensions.Hosting",
                    dependencies:
                    """
                    <group targetFramework="net10.0">
                      <dependency id="Microsoft.Extensions.DependencyInjection" version="[10.0.0, 11.0.0)" />
                      <dependency id="Microsoft.Extensions.Configuration" version="[10.0.0, 11.0.0)" />
                    </group>
                    <group targetFramework="net9.0">
                      <dependency id="microsoft.extensions.configuration" version="[10.0.0, 11.0.0)" />
                    </group>
                    <group targetFramework="net8.0">
                      <dependency id="Microsoft.Extensions.Configuration" version="10.0.0" />
                    </group>
                    <group targetFramework="net7.0">
                      <dependency id="Microsoft.Extensions.Configuration" version="[10.0.0, 11.0.0)" />
                    </group>
                    """),
                ["microsoft.extensions.logging@1.0.0"] = Manifest(
                    "Microsoft.Extensions.Logging",
                    dependencies:
                    """
                    <group targetFramework="net10.0">
                      <dependency id="Microsoft.Extensions.DependencyInjection" version="[10.0.0, 11.0.0)" />
                    </group>
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.Extensions.*",
                terms:
                [
                    new(
                        PackageQuery.DependsTermKey,
                        PortableQueryOperator.Equal,
                        "microsoft.extensions.configuration"),
                    new(
                        PackageQuery.DependsTermKey,
                        PortableQueryOperator.Equal,
                        "Microsoft.Extensions.DependencyInjection"),
                ],
                maximumCandidates: 2,
                maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Microsoft.Extensions.Hosting", match.Package.PackageId);
        Assert.Equal(PackageQueryAcquisitionTier.Nuspec, match.Tier);
        PackageQueryEvidence[] termEvidence =
        [
            .. match.Evidence.Where(evidence => evidence.Term is not null),
        ];
        Assert.Equal(2, termEvidence.Length);
        Assert.Contains(
            termEvidence,
            evidence => evidence.Term!.Value
                == "microsoft.extensions.configuration");
        Assert.Contains(
            termEvidence,
            evidence => evidence.Term!.Value
                == "Microsoft.Extensions.DependencyInjection");
        Assert.Equal(
            [
                "microsoft.extensions.configuration",
                "Microsoft.Extensions.DependencyInjection",
            ],
            match.Answers.Select(answer => answer.Value));
        PackageQueryEvidence configurationEvidence =
            termEvidence.Single(evidence =>
                evidence.Term!.Value
                    == "microsoft.extensions.configuration");
        Assert.Equal(4, configurationEvidence.Summary!.Count);
        Assert.Equal(
            [
                "net10.0: Microsoft.Extensions.Configuration [10.0.0, 11.0.0)",
                "net7.0: Microsoft.Extensions.Configuration [10.0.0, 11.0.0)",
                "net8.0: Microsoft.Extensions.Configuration 10.0.0",
            ],
            configurationEvidence.Summary.Preview.Select(value =>
                value.ToString()));
        Assert.Equal(2, source.ManifestRequests.Count);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_DependsPrefixTermsAndWithinSelectedScope()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.Complete"),
            Match("Contoso.Partial"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["contoso.complete@1.0.0"] = Manifest(
                    "Contoso.Complete",
                    dependencies:
                    """
                    <group targetFramework="net10.0">
                      <dependency id="Microsoft.Extensions.Configuration" version="[10.0.0, 11.0.0)" />
                      <dependency id="microsoft.extensions.logging" version="10.0.0" />
                      <dependency id="System.Text.Json" version="[10.0.0]" />
                    </group>
                    <group targetFramework="net8.0">
                      <dependency id="Legacy.Dependency" version="1.0.0" />
                    </group>
                    """),
                ["contoso.partial@1.0.0"] = Manifest(
                    "Contoso.Partial",
                    dependencies:
                    """
                    <group targetFramework="net10.0">
                      <dependency id="Microsoft.Extensions.Options" version="10.0.0" />
                    </group>
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "microsoft.extensions.",
                        PortableQueryOperator.StartsWith),
                    Term(
                        PackageQuery.DependsTermKey,
                        "System.",
                        PortableQueryOperator.StartsWith),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net10.0"),
                ],
                maximumCandidates: 2,
                maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Contoso.Complete", match.Package.PackageId);
        Assert.Equal(
            ["microsoft.extensions.", "net10.0", "System."],
            match.Answers.Select(answer => answer.Value)
                .Order(StringComparer.OrdinalIgnoreCase));
        PackageQueryEvidence[] prefixEvidence =
        [
            .. match.Evidence.Where(evidence =>
                evidence.Id == PackageQuery.DependsTermKey
                && evidence.Term!.Operator
                    == PortableQueryOperator.StartsWith),
        ];
        Assert.Equal(2, prefixEvidence.Length);
        PackageQueryEvidence extensions = prefixEvidence.Single(evidence =>
            evidence.Term!.Value == "microsoft.extensions.");
        Assert.Equal(2, extensions.Summary!.Count);
        Assert.Equal(
            [
                "net10.0: Microsoft.Extensions.Configuration [10.0.0, 11.0.0)",
                "net10.0: microsoft.extensions.logging 10.0.0",
            ],
            extensions.Summary.Preview.Select(value => value.ToString()));
        PackageQueryEvidence system = prefixEvidence.Single(evidence =>
            evidence.Term!.Value == "System.");
        Assert.Equal(1, system.Summary!.Count);
        Assert.Equal(
            ["net10.0: System.Text.Json [10.0.0]"],
            system.Summary.Preview.Select(value => value.ToString()));
        Assert.Equal(2, source.ManifestRequests.Count);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_DependsPrefixUsesLiteralPrefixSemantics()
    {
        var source = SourceFor(
            Manifest(
                "Contoso.Package",
                dependencies:
                """
                <group targetFramework="net10.0">
                  <dependency id="Microsoft.ExtensionsX" version="1.0.0" />
                </group>
                """),
            "Contoso.Package");
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "Microsoft.Extensions",
                        PortableQueryOperator.StartsWith),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;

        Assert.Equal(
            "Microsoft.Extensions",
            Assert.Single(match.Answers).Value);
        Assert.Equal(
            "net10.0: Microsoft.ExtensionsX 1.0.0",
            Assert.Single(
                Assert.Single(match.Evidence.Where(evidence =>
                    evidence.Id == PackageQuery.DependsTermKey
                    && evidence.Term!.Operator
                        == PortableQueryOperator.StartsWith))
                .Summary!.Preview).ToString());
    }

    [Fact]
    public async Task ExecuteAsync_TransitiveTermsShareOneBoundedTraversalAndRetainPaths()
    {
        var source = SourceFor(
            Manifest(
                "Contoso.Root",
                dependencies:
                """
                <group targetFramework="net10.0">
                  <dependency id="Contoso.Bridge" version="[1.0.0]" />
                </group>
                """),
            "Contoso.Root");
        var traversal = new FakePackageQueryTraversal(
            ("Contoso.Bridge", "1.0.0",
                """
                <group targetFramework="net10.0">
                  <dependency id="Contoso.Target.One" version="[2.0.0]" />
                  <dependency id="Contoso.Target.Two" version="[3.0.0]" />
                </group>
                """),
            ("Contoso.Target.One", "2.0.0", ""),
            ("Contoso.Target.Two", "3.0.0", ""));
        PackageQueryPlan plan = Accepted(PackageQuery.PlanInput(
            "Contoso.*",
            terms:
            [
                Term(
                    PackageQuery.DependsTransitiveTermKey,
                    "Contoso.Target.One"),
                Term(
                    PackageQuery.DependsTransitiveTermKey,
                    "contoso.target.two"),
                Term(PackageQuery.DependencyTargetTermKey, "net10.0"),
                Term(PackageQuery.DependencyDepthTermKey, "2"),
            ],
            maximumCandidates: 1,
            maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                traversal.Services,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(
            [
                "net10.0",
                "Contoso.Target.One",
                "contoso.target.two",
                "2",
            ],
            match.Answers.Select(answer => answer.Value));
        PackageQueryEvidence[] transitiveEvidence =
        [
            .. match.Evidence.Where(item =>
                item.Id == PackageQuery.DependsTransitiveTermKey),
        ];
        Assert.Equal(2, transitiveEvidence.Length);
        Assert.All(
            transitiveEvidence,
            item =>
            {
                Assert.Equal(1, item.Summary!.Count);
                string preview = Assert.Single(item.Summary.Preview).ToString();
                Assert.Contains(
                    "Contoso.Root@1.0.0",
                    preview,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Contains("[1.0.0, 1.0.0]", preview);
                Assert.Contains(
                    "Contoso.Bridge@1.0.0",
                    preview,
                    StringComparison.OrdinalIgnoreCase);
            });
        Assert.Contains(
            "Contoso.Target.One@2.0.0",
            transitiveEvidence[0].Summary!.Preview[0].ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, traversal.ResolverCallCount);
        Assert.Equal(1, traversal.ManifestCallCount);
        Assert.Contains(
            events.OfType<PackageQueryEvent.Progress>(),
            item => item.Value.Phase
                == PackageQueryProgressPhase.DependencyTraversal);
    }

    [Fact]
    public async Task ExecuteAsync_TransitiveTermExcludesDirectOnlyReachability()
    {
        var source = SourceFor(
            Manifest(
                "Contoso.Root",
                dependencies:
                """
                <group targetFramework="net10.0">
                  <dependency id="Contoso.Target" version="[2.0.0]" />
                </group>
                """),
            "Contoso.Root");
        var traversal = new FakePackageQueryTraversal(
            ("Contoso.Target", "2.0.0", ""));
        PackageQueryPlan plan = Accepted(PackageQuery.PlanInput(
            "Contoso.*",
            terms:
            [
                Term(
                    PackageQuery.DependsTransitiveTermKey,
                    "Contoso.Target"),
                Term(PackageQuery.DependencyTargetTermKey, "net10.0"),
                Term(PackageQuery.DependencyDepthTermKey, "2"),
            ],
            maximumCandidates: 1,
            maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                traversal.Services,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Empty(events.OfType<PackageQueryEvent.Failure>());
    }

    [Fact]
    public async Task ExecuteAsync_IncompleteTransitiveTraversalIsVisibleFailure()
    {
        var source = SourceFor(
            Manifest(
                "Contoso.Root",
                dependencies:
                """
                <group targetFramework="net10.0">
                  <dependency id="Contoso.Bridge" version="[1.0.0]" />
                </group>
                """),
            "Contoso.Root");
        var traversal = new FakePackageQueryTraversal();
        PackageQueryPlan plan = Accepted(PackageQuery.PlanInput(
            "Contoso.*",
            terms:
            [
                Term(
                    PackageQuery.DependsTransitiveTermKey,
                    "Contoso.Target"),
                Term(PackageQuery.DependencyTargetTermKey, "net10.0"),
                Term(PackageQuery.DependencyDepthTermKey, "2"),
            ],
            maximumCandidates: 1,
            maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                traversal.Services,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        PackageQueryFailure failure =
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value;
        Assert.Equal(
            PackageQueryFailureKind.DependencyTraversal,
            failure.Kind);
        Assert.Contains("work bounds", failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CrossPrefixDependenciesUseFirstIdSegmentAndRetainWitnesses()
    {
        SearchResult[] candidates =
        [
            Match("Microsoft.Extensions.Hosting"),
            Match("Microsoft.Extensions.Logging"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["microsoft.extensions.hosting@1.0.0"] = Manifest(
                    "Microsoft.Extensions.Hosting",
                    dependencies:
                    """
                    <group targetFramework="net10.0">
                      <dependency id="microsoft.extensions.configuration" version="10.0.0" />
                      <dependency id="Newtonsoft.Json" version="13.0.3" />
                      <dependency id="Newtonsoft.Json" version="13.0.3" />
                    </group>
                    <group targetFramework="net8.0">
                      <dependency id="System.Text.Json" version="10.0.0" />
                    </group>
                    """),
                ["microsoft.extensions.logging@1.0.0"] = Manifest(
                    "Microsoft.Extensions.Logging",
                    dependencies:
                    """
                    <group targetFramework="net10.0">
                      <dependency id="Microsoft.Extensions.Primitives" version="10.0.0" />
                    </group>
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Microsoft.*",
                terms:
                [
                    Term(PackageQuery.DependenciesTermKey, "cross-prefix"),
                ],
                maximumCandidates: 2,
                maximumMatches: null));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken));

        PackageQueryMatch match =
            Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("Microsoft.Extensions.Hosting", match.Package.PackageId);
        PackageQueryEvidence evidence = Assert.Single(
            match.Evidence,
            candidate =>
                candidate.Id == PackageQuery.DependenciesTermKey);
        Assert.Equal(3, evidence.Summary!.Count);
        Assert.Equal(
            [
                "net10.0: Newtonsoft.Json 13.0.3",
                "net8.0: System.Text.Json 10.0.0",
            ],
            evidence.Summary.Preview.Select(value => value.ToString()));
        Assert.Equal(
            "Microsoft",
            EvidenceProperty(evidence, "package-prefix"));
        Assert.Equal(1, evidence.Summary.Count - evidence.Summary.Preview.Length);
        Assert.Equal(2, source.ManifestRequests.Count);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_CrossPrefixDependenciesUseWholeIdWhenNoDotExists()
    {
        var source = SourceFor(
            Manifest(
                "Polly",
                dependencies:
                """
                <group targetFramework="net10.0">
                  <dependency id="Polly.Core" version="8.6.4" />
                  <dependency id="System.Threading.Tasks.Extensions" version="4.5.4" />
                </group>
                """),
            "Polly");
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Polly*",
                terms:
                [
                    Term(PackageQuery.DependenciesTermKey, "cross-prefix"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence evidence = Assert.Single(
            match.Evidence,
            candidate =>
                candidate.Id == PackageQuery.DependenciesTermKey);

        Assert.Equal(1, evidence.Summary!.Count);
        Assert.Equal(
            ["net10.0: System.Threading.Tasks.Extensions 4.5.4"],
            evidence.Summary.Preview.Select(value => value.ToString()));
    }

    [Fact]
    public async Task ExecuteAsync_CrossPrefixDependenciesUseSelectedDependencyTarget()
    {
        var source = SourceFor(
            Manifest(
                "Azure.Identity",
                dependencies:
                """
                <group targetFramework="net10.0">
                  <dependency id="Azure.Core" version="1.50.0" />
                </group>
                <group targetFramework="net8.0">
                  <dependency id="Microsoft.Identity.Client" version="4.77.0" />
                </group>
                """),
            "Azure.Identity");
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Azure.Identity*",
                terms:
                [
                    Term(PackageQuery.DependenciesTermKey, "cross-prefix"),
                    Term(PackageQuery.DependencyTargetTermKey, "net10.0"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));

        Assert.Empty(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>());

        PackageQueryPlan net8Plan = Accepted(
            PackageQuery.PlanInput(
                "Azure.Identity*",
                terms:
                [
                    Term(PackageQuery.DependenciesTermKey, "cross-prefix"),
                    Term(PackageQuery.DependencyTargetTermKey, "net8.0"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch net8Match = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                net8Plan,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Contains(
            "net8.0: Microsoft.Identity.Client 4.77.0",
            net8Match.Evidence.Single(evidence =>
                    evidence.Id == PackageQuery.DependenciesTermKey)
                .Summary!.Preview.Select(value => value.ToString()));
    }

    [Fact]
    public async Task ExecuteAsync_DependencyTargetSelectsOneCompatibleGroup()
    {
        var source = SourceFor(
            Manifest(
                "Polly.Core",
                dependencies:
                """
                <group targetFramework="netstandard2.0">
                  <dependency id="Microsoft.Bcl.AsyncInterfaces" version="8.0.0" />
                  <dependency id="Microsoft.Bcl.TimeProvider" version="8.0.0" />
                  <dependency id="System.ComponentModel.Annotations" version="5.0.0" />
                  <dependency id="System.Threading.Tasks.Extensions" version="4.5.4" />
                </group>
                <group targetFramework="net8.0" />
                """),
            "Polly.Core");

        PackageQueryPlan all = Accepted(
            PackageQuery.PlanInput(
                "Polly.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "System.Threading.Tasks.Extensions"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch allMatch = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                all,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(
            PackageQueryDependencyTargetKind.All,
            all.DependencyTarget.Kind);
        Assert.Equal(
            "netstandard2.0: System.Threading.Tasks.Extensions 4.5.4",
            Assert.Single(allMatch.Evidence.Single(evidence =>
                evidence.Id == PackageQuery.DependsTermKey).Summary!.Preview)
                .ToString());

        PackageQueryPlan net12Dependency = Accepted(
            PackageQuery.PlanInput(
                "Polly.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "System.Threading.Tasks.Extensions"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net12.0"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        Assert.Empty(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                net12Dependency,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>());

        PackageQueryPlan net12Empty = Accepted(
            PackageQuery.PlanInput(
                "Polly.*",
                terms:
                [
                    Term(PackageQuery.DependenciesTermKey, "none"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net12.0"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch emptyMatch = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                net12Empty,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(
            "net8.0",
            EvidenceProperty(
                emptyMatch,
                PackageQuery.DependencyTargetTermKey,
                "selected-group"));

        PackageQueryPlan netStandardDependency = Accepted(
            PackageQuery.PlanInput(
                "Polly.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "System.Threading.Tasks.Extensions"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "netstandard2.0"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch selectedMatch = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                netStandardDependency,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal(
            [
                PackageQuery.PrefixEvidenceId,
                PackageQuery.DependencyTargetTermKey,
                PackageQuery.DependsTermKey,
            ],
            selectedMatch.Evidence.Select(evidence => evidence.Id));
        Assert.Equal(
            "netstandard2.0",
            EvidenceProperty(
                selectedMatch.Evidence[1],
                "selected-group"));
        Assert.Equal(
            PackageQueryEvidenceScope.Package,
            selectedMatch.Evidence[1].Scope);
    }

    [Fact]
    public async Task ExecuteAsync_DependsEcosystemMatchesExactAndPrefixMembership()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.Exact"),
            Match("Contoso.Prefix"),
            Match("Contoso.Similar"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["contoso.exact@1.0.0"] = Manifest(
                    "Contoso.Exact",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Aspire.Hosting" version="[9.0.0, 10.0.0)" />
                    </group>
                    """),
                ["contoso.prefix@1.0.0"] = Manifest(
                    "Contoso.Prefix",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Aspire.Future.Integration" version="9.1.0" />
                    </group>
                    """),
                ["contoso.similar@1.0.0"] = Manifest(
                    "Contoso.Similar",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Contoso.Aspire.Hosting" version="1.0.0" />
                    </group>
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                EcosystemCatalog(
                    Ecosystem(
                        "ecosystem.aspire",
                        exactPackages: ["Aspire.Hosting"],
                        packagePrefixes: ["Aspire."])),
                terms:
                [
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.aspire"),
                ],
                maximumCandidates: 3,
                maximumMatches: null));

        PackageQueryMatch[] matches =
        [
            .. (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
                .OfType<PackageQueryEvent.Match>()
                .Select(queryEvent => queryEvent.Value),
        ];

        Assert.Equal(
            ["Contoso.Exact", "Contoso.Prefix"],
            matches.Select(match => match.Package.PackageId));
        Assert.All(matches, match => Assert.Equal(
            "ecosystem.aspire",
            Assert.Single(match.Answers).Value));
        Assert.Equal(
            "net8.0: Aspire.Hosting [9.0.0, 10.0.0)"
                + " -> ecosystem.aspire (exact package Aspire.Hosting)",
            Assert.Single(matches[0].Evidence.Single(evidence =>
                evidence.Id == PackageQuery.DependsEcosystemTermKey)
                .Summary!.Preview).ToString());
        Assert.Equal(
            "net8.0: Aspire.Future.Integration 9.1.0"
                + " -> ecosystem.aspire (package prefix Aspire.)",
            Assert.Single(matches[1].Evidence.Single(evidence =>
                evidence.Id == PackageQuery.DependsEcosystemTermKey)
                .Summary!.Preview).ToString());
        Assert.Equal(3, source.ManifestRequests.Count);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_DependsEcosystemTermsAndWithinSelectedGroup()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.Both"),
            Match("Contoso.Split"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["contoso.both@1.0.0"] = Manifest(
                    "Contoso.Both",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Aspire.Hosting" version="9.0.0" />
                      <dependency id="Microsoft.Extensions.AI" version="9.0.0" />
                    </group>
                    """),
                ["contoso.split@1.0.0"] = Manifest(
                    "Contoso.Split",
                    dependencies:
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Aspire.Hosting" version="9.0.0" />
                    </group>
                    <group targetFramework="net9.0">
                      <dependency id="Microsoft.Extensions.AI" version="9.0.0" />
                    </group>
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                EcosystemCatalog(
                    Ecosystem(
                        "ecosystem.aspire",
                        packagePrefixes: ["Aspire."]),
                    Ecosystem(
                        "ecosystem.ai",
                        packagePrefixes: ["Microsoft.Extensions.AI"])),
                terms:
                [
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.aspire"),
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.ai"),
                    Term(PackageQuery.DependencyTargetTermKey, "net8.0"),
                ],
                maximumCandidates: 2,
                maximumMatches: null));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;

        Assert.Equal("Contoso.Both", match.Package.PackageId);
        Assert.Equal(
            2,
            match.Evidence.Count(evidence =>
                evidence.Id == PackageQuery.DependsEcosystemTermKey));
        Assert.Equal(
            ["ecosystem.ai", "ecosystem.aspire", "net8.0"],
            match.Answers
                .Select(answer => answer.Value)
                .Order(StringComparer.Ordinal));
        Assert.All(
            match.Evidence.Where(evidence =>
                evidence.Id == PackageQuery.DependsEcosystemTermKey),
            evidence => Assert.StartsWith(
                "net8.0:",
                Assert.Single(evidence.Summary!.Preview).ToString(),
                StringComparison.Ordinal));
    }

    [Fact]
    public void DependsEcosystemRejectsMalformedUnknownAndUnboundIdentities()
    {
        PackageQueryEcosystemMembershipCatalog catalog = EcosystemCatalog(
            Ecosystem("ecosystem.platform"));

        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidTermValue,
            Rejected(PackageQuery.PlanInput(
                "Contoso.*",
                catalog,
                [Term(PackageQuery.DependsEcosystemTermKey, "Aspire")]))
                .Reason);

        PackageQueryRequestFailure unknown = Rejected(
            PackageQuery.PlanInput(
                "Contoso.*",
                catalog,
                [
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.unknown"),
                ]));
        Assert.Equal(
            PackageQueryRequestFailureReason.UnknownEcosystem,
            unknown.Reason);
        Assert.Equal("ecosystem.unknown", unknown.EcosystemId);

        PackageQueryRequestFailure unbound = Rejected(
            PackageQuery.PlanInput(
                "Contoso.*",
                catalog,
                [
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.platform"),
                ]));
        Assert.Equal(
            PackageQueryRequestFailureReason
                .EcosystemPackagePopulationUnavailable,
            unbound.Reason);
        Assert.Equal("ecosystem.platform", unbound.EcosystemId);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsPortableUnboundEcosystemBeforeSourceWork()
    {
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan portablePlan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsEcosystemTermKey,
                        "ecosystem.aspire"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CollectAsync(PackageQuery.ExecuteAsync(
                    source,
                    portablePlan,
                    TestContext.Current.CancellationToken)));

        Assert.Contains(
            "ecosystem-membership binding",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, source.LastSearchTake);
        Assert.Empty(source.ManifestRequests);
    }

    [Fact]
    public async Task ExecuteAsync_DependencyTargetAllAndAnyRemainDistinct()
    {
        var source = SourceFor(Manifest(
            "Contoso.Package",
            dependencies:
            """
            <group targetFramework="any">
              <dependency id="Universal.Dependency" version="1.0.0" />
            </group>
            <group targetFramework="net8.0">
              <dependency id="Net8.Dependency" version="8.0.0" />
            </group>
            """));

        PackageQueryPlan all = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Net8.Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "all"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch allMatch = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                all,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence allTarget = Assert.Single(
            allMatch.Evidence,
            evidence =>
                evidence.Id == PackageQuery.DependencyTargetTermKey);
        Assert.Equal(PackageQueryEvidenceScope.Query, allTarget.Scope);
        Assert.Equal("all", EvidenceProperty(allTarget, "target"));

        PackageQueryPlan anyNet8 = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Net8.Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "any"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        Assert.Empty(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                anyNet8,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>());

        PackageQueryPlan anyUniversal = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(
                        PackageQuery.DependsTermKey,
                        "Universal.Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "any"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));
        PackageQueryMatch anyMatch = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                anyUniversal,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence anyTarget = Assert.Single(
            anyMatch.Evidence,
            evidence =>
                evidence.Id == PackageQuery.DependencyTargetTermKey);
        Assert.Equal(PackageQueryEvidenceScope.Package, anyTarget.Scope);
        Assert.Equal("any", EvidenceProperty(anyTarget, "selected-group"));
        Assert.Equal(
            "any: Universal.Dependency 1.0.0",
            Assert.Single(anyMatch.Evidence.Single(evidence =>
                evidence.Id == PackageQuery.DependsTermKey).Summary!.Preview)
                .ToString());
    }

    [Fact]
    public async Task ExecuteAsync_DependencyTargetAnyUsesAllImplicitRuns()
    {
        var source = SourceFor(Manifest(
            "Contoso.Package",
            dependencies:
            """
            <dependency id="Early.Dependency" version="1.0.0" />
            <group targetFramework="net8.0" />
            <dependency id="Late.Dependency" version="2.0.0" />
            """));
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependsTermKey, "Late.Dependency"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "any"),
                ],
                maximumCandidates: 1,
                maximumMatches: 1));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;

        Assert.Equal(
            "any: Late.Dependency 2.0.0",
            Assert.Single(match.Evidence.Single(evidence =>
                evidence.Id == PackageQuery.DependsTermKey).Summary!.Preview)
                .ToString());
    }

    [Fact]
    public async Task ExecuteAsync_DependencyTargetDistinguishesNoGroupsFromNoMatch()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.NoGroups"),
            Match("Contoso.Net8Only"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["contoso.nogroups@1.0.0"] =
                    Manifest("Contoso.NoGroups"),
                ["contoso.net8only@1.0.0"] = Manifest(
                    "Contoso.Net8Only",
                    dependencies:
                    """
                    <group targetFramework="net8.0" />
                    """),
            });
        PackageQueryPlan plan = Accepted(
            PackageQuery.PlanInput(
                "Contoso.*",
                terms:
                [
                    Term(PackageQuery.DependenciesTermKey, "none"),
                    Term(
                        PackageQuery.DependencyTargetTermKey,
                        "net6.0"),
                ],
                maximumCandidates: 2,
                maximumMatches: null));

        PackageQueryMatch match = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                plan,
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;

        Assert.Equal("Contoso.NoGroups", match.Package.PackageId);
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.NoDependencyGroups.ToString(),
            EvidenceProperty(
                match,
                PackageQuery.DependencyTargetTermKey,
                "selection-status"));
    }

    [Fact]
    public async Task ExecuteAsync_LicenseAnswersAreSemanticAndNuspecOnly()
    {
        SearchResult[] candidates =
        [
            Match("Contoso.Mit"),
            Match("Contoso.Osmf"),
            Match("Contoso.Generic"),
        ];
        var source = new FakePackageSource(
            candidates,
            new Dictionary<string, byte[]>
            {
                ["contoso.mit@1.0.0"] = Manifest(
                    "Contoso.Mit",
                    license: """<license type="expression">MIT</license>"""),
                ["contoso.osmf@1.0.0"] = Manifest(
                    "Contoso.Osmf",
                    license: """<license type="file">licenses/OSMFEULA.txt</license>"""),
                ["contoso.generic@1.0.0"] = Manifest(
                    "Contoso.Generic",
                    license: """<license type="file">LICENSE.txt</license>"""),
            });

        PackageQueryMatch mit = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                Accepted(PackageQuery.PlanInput(
                    "Contoso.*",
                    terms: [Term(PackageQuery.LicenseTermKey, "MIT")],
                    maximumCandidates: 3,
                    maximumMatches: null)),
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("MIT", Assert.Single(mit.Answers).Value);
        PackageQueryEvidence mitEvidence = Assert.Single(
            mit.Evidence,
            evidence => evidence.Id == PackageQuery.LicenseTermKey);
        Assert.Equal(
            PackageLicenseDeclarationKind.Expression.ToString(),
            EvidenceProperty(mitEvidence, "declaration-kind"));
        Assert.Equal(
            "MIT",
            EvidenceProperty(mitEvidence, "declaration-value"));

        PackageQueryMatch osmf = Assert.Single(
            (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                Accepted(PackageQuery.PlanInput(
                    "Contoso.*",
                    terms: [Term(PackageQuery.LicenseTermKey, "OSMF")],
                    maximumCandidates: 3,
                    maximumMatches: null)),
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()).Value;
        Assert.Equal("OSMF", Assert.Single(osmf.Answers).Value);
        PackageQueryEvidence osmfEvidence = Assert.Single(
            osmf.Evidence,
            evidence => evidence.Id == PackageQuery.LicenseTermKey);
        Assert.Equal(
            PackageLicenseDeclarationKind.File.ToString(),
            EvidenceProperty(osmfEvidence, "declaration-kind"));
        Assert.Equal(
            "licenses/OSMFEULA.txt",
            EvidenceProperty(osmfEvidence, "declaration-value"));

        PackageQueryMatch[] any =
        [
            .. (await CollectAsync(PackageQuery.ExecuteAsync(
                source,
                Accepted(PackageQuery.PlanInput(
                    "Contoso.*",
                    terms: [Term(PackageQuery.LicenseTermKey, "any")],
                    maximumCandidates: 3,
                    maximumMatches: null)),
                TestContext.Current.CancellationToken)))
            .OfType<PackageQueryEvent.Match>()
            .Select(item => item.Value),
        ];
        Assert.Equal(3, any.Length);
        Assert.All(any, match =>
            Assert.Equal("true", Assert.Single(match.Answers).Value));
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_DependsEvidenceCountsDistinctDeclarationsAcrossGroups()
    {
        var source = SourceFor(Manifest(
            "Contoso.Package",
            dependencies:
            """
            <group targetFramework="net8.0">
              <dependency id="Gamma" version="[1.0.0]" />
              <dependency id="beta" version="[1.0.0]" />
              <dependency id="Alpha" version="[1.0.0]" />
            </group>
            <group targetFramework="net9.0">
              <dependency id="BETA" version="[2.0.0]" />
              <dependency id="Alpha" version="[2.0.0]" />
              <dependency id="Zeta" version="[1.0.0]" />
            </group>
            """));
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*", [Term(PackageQuery.DependsTermKey, "Alpha")],
                MaximumCandidates: 1, MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(PackageQuery.ExecuteAsync(
            source, plan, TestContext.Current.CancellationToken));

        PackageQueryMatch match = Assert.Single(events.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence evidence = Assert.Single(match.Evidence,
            item => item.Id == PackageQuery.DependsTermKey);
        PackageQueryEvidenceSummary summary =
            Assert.IsType<PackageQueryEvidenceSummary>(evidence.Summary);
        Assert.Equal(2, summary.Count);
        Assert.Equal(
            ["net8.0: Alpha [1.0.0]", "net9.0: Alpha [2.0.0]"],
            summary.Preview.Select(item => item.ToString()));
        Assert.Equal(PackageQueryEvidenceScope.Package, evidence.Scope);
        Assert.Single(source.ManifestRequests);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_AllEmptyDependencyGroupsMatchNoDependencies()
    {
        byte[] manifest = Manifest(
            "Contoso.EmptyGroups",
            dependencies:
            """
            <group targetFramework="net8.0"></group>
            <group targetFramework="net9.0"></group>
            """);

        var noDependenciesSource = SourceFor(
            manifest,
            "Contoso.EmptyGroups");
        PackageQueryPlan noDependencies = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [Term(PackageQuery.DependenciesTermKey, "none")],
                    MaximumCandidates: 2,
                    MaximumMatches: 1)));
        List<PackageQueryEvent> noDependencyEvents = await CollectAsync(
            PackageQuery.ExecuteAsync(
                noDependenciesSource,
                noDependencies,
                TestContext.Current.CancellationToken));
        PackageQueryMatch emptyMatch =
            Assert.Single(noDependencyEvents.OfType<PackageQueryEvent.Match>()).Value;
        PackageQueryEvidence emptyEvidence = Assert.Single(emptyMatch.Evidence,
            evidence => evidence.Id == PackageQuery.DependenciesTermKey);
        PackageQueryEvidenceSummary emptySummary =
            Assert.IsType<PackageQueryEvidenceSummary>(emptyEvidence.Summary);
        Assert.Equal(0, emptySummary.Count);
        Assert.Empty(emptySummary.Preview);
        Assert.Equal(
            "none",
            Assert.Single(emptyMatch.Answers).Value);
    }
}
