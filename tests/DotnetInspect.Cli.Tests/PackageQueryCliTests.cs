using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.PackageQueries;
using QuerySpace;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class PackageQueryCliTests
{
    internal static PackageQueryMatch ContainmentMatch(string text)
    {
        using var source = Source(out _);
        return new(
            new PackageQueryPackage(text, text, [], null, null, source.Source),
            PackageQueryAcquisitionTier.Nuspec,
            [new(PackageQuery.DependsTermKey, new InertString(TextPolicy.Field, text))],
            [
                new(PackageQuery.DependsTermKey)
                {
                    Properties =
                    [
                        new(
                            "value",
                            new InertString(TextPolicy.Field, text)),
                    ],
                },
            ]);
    }

    [Fact]
    public void DiscoveryValues_ExposeTheProductTermVocabulary()
    {
        PackageQueryRegisteredTerm[] registeredInspectionTerms =
        [
            .. PackageQuery.RegisteredTerms.Where(term =>
                term.Descriptor.Role
                    == PackageQueryTermRole.Inspection),
        ];
        Assert.Equal(
            registeredInspectionTerms.Select(term =>
                term.Descriptor.Key),
            PackageQueryOptions.QueryKeys.Select(key => key.Name));
        Assert.Equal(
            registeredInspectionTerms.Length,
            PackageQueryOptions.QueryKeys.Length);
        for (int index = 0;
             index < registeredInspectionTerms.Length;
             index++)
        {
            Assert.Equal(
                registeredInspectionTerms[index].Operators.Select(
                    @operator =>
                        @operator switch
                        {
                            PortableQueryOperator.Equal => "=",
                            PortableQueryOperator.StartsWith =>
                                "starts-with",
                            _ => throw new InvalidOperationException(),
                        }),
                PackageQueryOptions.QueryKeys[index].Comparisons);
            Assert.Equal(
                PackageQuery.ExecutionClassIdentity(
                    registeredInspectionTerms[index]
                        .Descriptor.ExecutionClass),
                PackageQueryOptions.QueryKeys[index].ExecutionClass);
        }
        Assert.Equal(
            ["v1", "v2"],
            PackageQueryOptions.QueryKeys.Single(key =>
                key.Name == PackageQuery.ToolFormatTermKey).Values);
        Assert.Equal(
            ["none", "cross-prefix"],
            PackageQueryOptions.QueryKeys.Single(key =>
                key.Name == PackageQuery.DependenciesTermKey).Values);
        Assert.Equal(
            ["2", "3", "4"],
            PackageQueryOptions.QueryKeys.Single(key =>
                key.Name == PackageQuery.DependencyDepthTermKey).Values);
        Assert.Equal(
            ["any", "MIT", "OSMF"],
            PackageQueryOptions.QueryKeys.Single(key =>
                key.Name == PackageQuery.LicenseTermKey).Values);
        Assert.Equal(
            "metadata",
            PackageQueryOptions.QueryKeys.Single(key =>
                key.Name == PackageQuery.ReferencesTermKey).ExecutionClass);
        Assert.Equal(
            PackageQueryCapabilityResourcePaths
                .QueryFacet(PackageQuery.LibraryLiteralTermKey)
                .Value,
            PackageQueryOptions.QueryKeys.Single(key =>
                key.Name == PackageQuery.LibraryLiteralTermKey)
                .ResourcePath);
    }

    [Fact]
    public void ProductionBindingUsesTheRegisteredPackageQueryRoute()
    {
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageQueryCapability.ProductModule,
                    PackageQueryCommandCapability.Module,
                ]);

        Assert.Same(
            PackageQueryCapability.Route,
            PackageQueryCommandCapability.Binding.Route);
        Assert.Same(
            PackageQueryCommandCapability.Binding,
            Assert.Single(catalog.Bindings));
        Assert.Equal(
            InspectionConsumerKind.Browser,
            Assert.Single(catalog.AdoptionGaps).ConsumerKind);
        Assert.Contains(
            PackageQueryCommandCapability.Binding.ExposedQueryTerms,
            identity =>
                identity
                == "package-query.term.library-literal");
    }

    [Fact]
    public void DependsTerm_LowersToTheProductPlan()
    {
        Assert.Equal(
            PackageQuery.DependsTermKey,
            PackageQueryOptions.QueryKeys.Single(key =>
                key.Name == PackageQuery.DependsTermKey).Name);
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Microsoft.Extensions.*",
                ["depends=Microsoft.Extensions.DependencyInjection"],
                nuspecOnly: false,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        PortableQueryTerm term = Assert.Single(options!.Plan.Terms);
        Assert.Equal(PackageQuery.DependsTermKey, term.Key);
        Assert.Equal(PortableQueryOperator.Equal, term.Operator);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection",
            term.Value);
        Assert.Equal(
            PackageQuery.DefaultMaximumCandidates,
            options.Plan.MaximumCandidates);
    }

    [Fact]
    public void DependsStartsWithTerm_LowersToTheProductPlan()
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Microsoft.Extensions.*",
                ["depends starts-with Microsoft.Extensions."],
                nuspecOnly: true,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        BoundPackageQueryTerm term = Assert.Single(options!.Plan.BoundTerms);
        Assert.Equal(PackageQuery.DependsTermKey, term.Term.Key);
        Assert.Equal(
            PortableQueryOperator.StartsWith,
            term.Term.Operator);
        Assert.Equal(
            "Microsoft.Extensions.",
            term.Predicate.PackagePrefix!.Prefix);
        Assert.True(options.Plan.RequiresManifest);
        Assert.False(options.Plan.RequiresPackageContent);
    }

    [Fact]
    public void TransitiveDependencyTerms_LowerToTheProductPlan()
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Microsoft.Extensions.*",
                [
                    "depends-transitive=Microsoft.Extensions.Primitives",
                    "dependency-target=net10.0",
                    "dependency-depth=2",
                ],
                nuspecOnly: true,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        Assert.True(options!.Plan.RequiresDependencyTraversal);
        Assert.Equal(2, options.Plan.DependencyDepth);
        Assert.Equal(
            "net10.0",
            options.Plan.DependencyTarget.RequestedTargetFramework);
        Assert.Equal(
            PackageQuery.MaximumNuspecExpensiveCandidates,
            options.Plan.MaximumCandidates);
        Assert.Equal(
            PackageQueryExecutionClass.NuspecExpensive,
            PackageQuery.Terms.Single(term =>
                term.Key == PackageQuery.DependsTransitiveTermKey)
                .ExecutionClass);
    }

    [Fact]
    public void CrossPrefixDependenciesTerm_LowersToTheProductPlan()
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Azure.*",
                ["dependencies=cross-prefix"],
                nuspecOnly: true,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        PortableQueryTerm term = Assert.Single(options!.Plan.Terms);
        Assert.Equal(PackageQuery.DependenciesTermKey, term.Key);
        Assert.Equal(PortableQueryOperator.Equal, term.Operator);
        Assert.Equal("cross-prefix", term.Value);
        Assert.True(options.Plan.RequiresManifest);
        Assert.False(options.Plan.RequiresPackageContent);
    }

    [Fact]
    public void ReferencesTerm_LowersToTheProductPackageContentPlan()
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Microsoft.Extensions.*",
                [
                    "references="
                    + "Microsoft.Extensions.DependencyInjection.Abstractions",
                ],
                nuspecOnly: false,
                take: PackageQuery.MaximumPackageContentCandidates,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        PortableQueryTerm term = Assert.Single(options!.Plan.Terms);
        Assert.Equal(PackageQuery.ReferencesTermKey, term.Key);
        Assert.Equal(PortableQueryOperator.Equal, term.Operator);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            term.Value);
        Assert.True(options.Plan.RequiresPackageContent);
        Assert.True(options.Plan.RequiresManifest);
    }

    [Fact]
    public void CliLowering_ProducesTheRegisteredCanonicalIntent()
    {
        RowSelectionIntent<string> selection =
            RowSelectionIntent<string>.Create(
            [
                RowSelectionIntentOperation<string>.Head(3),
            ]);

        Assert.True(
            PackageQueryOptions.TryCreate(
                "Contoso.*",
                ["license=MIT"],
                nuspecOnly: false,
                take: null,
                rowSelection: selection,
                includePrerelease: true,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        PortableQueryIntent expected = PortableQueryIntent.Create(
            [
                new(
                    PackageQuery.LicenseTermKey,
                    PortableQueryOperator.Equal,
                    "MIT"),
                new(
                    PackageQuery.PrefixTermKey,
                    PortableQueryOperator.Equal,
                    "Contoso."),
                new(
                    PackageQuery.PrereleaseTermKey,
                    PortableQueryOperator.Equal,
                    "include"),
            ],
            [
                new("candidates", 200),
                new("matches", 3),
            ],
            [PortableQueryStage.Head(3)],
            []);
        Assert.Equal(
            PortableQueryPayloadCodec.Encode(
                expected,
                TestContext.Current.CancellationToken),
            PortableQueryPayloadCodec.Encode(
                options!.Plan.Intent,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DependsEcosystemTerm_LowersToTheProductPlan()
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Aspire.Hosting.PostgreSQL",
                ["depends-ecosystem=ecosystem.aspire"],
                nuspecOnly: false,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        PortableQueryTerm term = Assert.Single(options!.Plan.Terms);
        Assert.Equal(PackageQuery.DependsEcosystemTermKey, term.Key);
        Assert.Equal("ecosystem.aspire", term.Value);
    }

    [Theory]
    [InlineData("all", PackageQueryDependencyTargetKind.All, null)]
    [InlineData(
        "NET8.0",
        PackageQueryDependencyTargetKind.TargetFramework,
        "net8.0")]
    [InlineData(
        "any",
        PackageQueryDependencyTargetKind.TargetFramework,
        "any")]
    public void DependencyTarget_LowersToTheProductPlan(
        string value,
        PackageQueryDependencyTargetKind expectedKind,
        string? expectedFramework)
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Microsoft.Extensions.*",
                [
                    "depends=Microsoft.Extensions.DependencyInjection",
                    $"dependency-target={value}",
                ],
                nuspecOnly: false,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        Assert.Equal(expectedKind, options!.Plan.DependencyTarget.Kind);
        Assert.Equal(
            expectedFramework,
            options.Plan.DependencyTarget.RequestedTargetFramework);
        Assert.Contains(
            options.Plan.Terms,
            term => term.Key == PackageQuery.DependencyTargetTermKey
                && term.Value == value);
    }

    [Fact]
    public void LicenseTerm_LowersToTheProductPlan()
    {
        Assert.Equal(
            PackageQuery.LicenseTermKey,
            PackageQueryOptions.QueryKeys.Single(key =>
                key.Name == PackageQuery.LicenseTermKey).Name);
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Contoso.*",
                ["license=MIT"],
                nuspecOnly: true,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        PortableQueryTerm term = Assert.Single(options!.Plan.Terms);
        Assert.Equal(PackageQuery.LicenseTermKey, term.Key);
        Assert.Equal("MIT", term.Value);
        Assert.Equal(
            PackageQuery.DefaultMaximumCandidates,
            options.Plan.MaximumCandidates);
    }

    [Fact]
    public void LibraryLiteral_LowersToPackageGrainSemanticPlan()
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Newtonsoft.Json",
                ["library-literal=Unexpected end when reading JSON"],
                nuspecOnly: false,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                targetFramework: "net6.0",
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        Assert.True(options!.Plan.RequiresLibraryLiteralEvaluation);
        Assert.Equal(
            "Unexpected end when reading JSON",
            options.Plan.LibraryLiteral);
        Assert.Equal("net6.0", options.Plan.LibraryTargetFramework);
        Assert.Contains(
            options.Plan.Terms,
            term => term.Key == PackageQuery.LibraryTargetTermKey
                && term.Value == "net6.0");
        Assert.Null(options.Plan.MaximumMatches);
        Assert.Equal(1, options.Plan.MaximumCandidates);
        Assert.False(options.SemanticHeadPushedDown);
    }

    [Fact]
    public void LibraryLiteral_PreservesExactMultilineOperand()
    {
        const string literal = " \r\nmarker\\suffix ";
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Newtonsoft.Json",
                [$"library-literal={literal}"],
                nuspecOnly: false,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                targetFramework: "net6.0",
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        Assert.Equal(literal, options!.Plan.LibraryLiteral);
        PortableQueryTerm term = Assert.Single(
            options.Plan.Terms,
            term => term.Key == PackageQuery.LibraryLiteralTermKey);
        Assert.Equal(PortableQueryOperator.Equal, term.Operator);
        Assert.Equal(literal, term.Value);
    }

    [Fact]
    public void LibraryLiteral_AcceptsTfmAfterQuerySubcommand()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] args =
        [
            "package",
            "query",
            "Newtonsoft.Json",
            "--where",
            "library-literal=marker",
            "--tfm",
            "net6.0",
        ];
        string[] processed = CommandLineBuilder.PreprocessArgs(args, root);

        Assert.Empty(root.Parse(processed).Errors);
    }

    [Theory]
    [InlineData("Contoso.*", null, 5)]
    [InlineData("Contoso.*", 3, 3)]
    public void LibraryLiteral_PrefixUsesBoundedCandidatePopulation(
        string input,
        int? take,
        int expectedCandidates)
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                input,
                ["library-literal=marker"],
                nuspecOnly: false,
                take,
                rowSelection: Head(1),
                includePrerelease: false,
                targetFramework: "net10.0",
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        Assert.Equal(expectedCandidates, options!.Plan.MaximumCandidates);
        Assert.True(options.Plan.RequiresLibraryLiteralEvaluation);
        Assert.Equal(take is null ? 1 : null, options.Plan.MaximumMatches);
        Assert.Equal(take is null, options.SemanticHeadPushedDown);
    }

    [Fact]
    public void LibraryLiteral_ExactPackageUsesOneCandidate()
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Newtonsoft.Json",
                ["library-literal=marker"],
                nuspecOnly: false,
                take: 2,
                rowSelection: null,
                includePrerelease: false,
                targetFramework: "net6.0",
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        Assert.Equal(1, options!.Plan.MaximumCandidates);
    }

    [Theory]
    [InlineData(true, null, null, null, "require package archive content")]
    [InlineData(false, null, null, null, "requires one exact target framework")]
    [InlineData(false, "depends=Dependency.One", "net10.0", null, "")]
    [InlineData(false, null, "net10.0", 6, "metadata-expensive terms admit at most 5")]
    public void LibraryLiteral_RejectsIncompatiblePlanning(
        bool nuspecOnly,
        string? additionalTerm,
        string? targetFramework,
        int? take,
        string expected)
    {
        string[] terms = additionalTerm is null
            ? ["library-literal=marker"]
            : ["library-literal=marker", additionalTerm];
        bool accepted = PackageQueryOptions.TryCreate(
            "Contoso.*",
            terms,
            nuspecOnly,
            take,
            rowSelection: null,
            includePrerelease: false,
            targetFramework,
            out PackageQueryOptions? options,
            out OptionError error);
        if (additionalTerm is not null)
        {
            Assert.True(accepted, error.ToString());
            Assert.NotNull(options);
            Assert.Contains(
                options.Plan.Terms,
                term => term.Key == PackageQuery.DependsTermKey);
            return;
        }

        Assert.False(accepted);
        Assert.Contains(expected, error.ToString());
    }

    [Theory]
    [InlineData("facet!=package.query.dotnet-tool", "does not define term")]
    [InlineData("downloads>=1000000", "does not support '>='")]
    [InlineData("facet=package.query.unknown", "does not define term")]
    [InlineData(
        "depends starts-with Microsoft.*",
        "term value is invalid")]
    [InlineData("depends=not/a/package", "term value is invalid")]
    [InlineData("dependencies=other", "term value is invalid")]
    [InlineData(
        "references=System.Runtime, Version=10.0.0.0",
        "term value is invalid")]
    [InlineData("depends-ecosystem=Aspire", "term value is invalid")]
    [InlineData("depends-ecosystem=ecosystem.unknown", "Unknown ecosystem")]
    [InlineData(
        "depends-ecosystem=ecosystem.platform",
        "Unknown ecosystem")]
    [InlineData("license=Apache-2.0", "term value is invalid")]
    [InlineData("dependency-target=not/a/tfm", "term value is invalid")]
    [InlineData(
        "dependency-target=net8.0",
        "requires a depends")]
    [InlineData("library-literal=", "Missing value")]
    [InlineData(
        "library-literal starts-with marker",
        "does not support 'starts-with'")]
    [InlineData("", "Empty")]
    public void InvalidSelections_FailBeforeExecution(string expression, string message)
    {
        Assert.False(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [expression],
            nuspecOnly: false,
            take: null,
            rowSelection: null,
            includePrerelease: false,
            out PackageQueryOptions? options,
            out OptionError error));
        Assert.Null(options);
        Assert.Contains(message, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InspectionTermCount_UsesProductPortableBoundary()
    {
        string[] maximum = InspectionExpressions(
            PackageQuery.MaximumInspectionTerms);
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Contoso.*",
                maximum,
                nuspecOnly: false,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? accepted,
                out OptionError acceptedError),
            acceptedError.ToString());
        Assert.NotNull(accepted);

        Assert.False(
            PackageQueryOptions.TryCreate(
                "Contoso.*",
                InspectionExpressions(PackageQuery.MaximumInspectionTerms + 1),
                nuspecOnly: false,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? rejected,
                out OptionError rejectedError));
        Assert.Null(rejected);
        Assert.Contains(
            $"at most {PackageQuery.MaximumInspectionTerms} inspection terms",
            rejectedError.ToString());
    }

    [Fact]
    public void NuspecOnly_RejectsPackageContentTerms()
    {
        Assert.False(PackageQueryOptions.TryCreate(
            "Contoso.*",
            ["skill=true"],
            nuspecOnly: true,
            take: null,
            rowSelection: null,
            includePrerelease: false,
            out PackageQueryOptions? options,
            out OptionError error));
        Assert.Null(options);
        Assert.Contains("cannot be combined with --nuspec-only", error.ToString());
    }

    [Fact]
    public void NuspecOnly_AllowsMetadataOnlyQuery()
    {
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [],
            nuspecOnly: true,
            take: null,
            rowSelection: null,
            includePrerelease: false,
            out PackageQueryOptions? options,
            out OptionError error),
            error.ToString());
        Assert.Empty(options!.Plan.Terms);
        Assert.Equal(
            PackageQuery.DefaultMaximumCandidates,
            options.Plan.MaximumCandidates);
    }

    [Fact]
    public void ProductPlanner_OwnsCompatibilityAndDuplicateCollapse()
    {
        Assert.False(PackageQueryOptions.TryCreate("Contoso.*",
            ["tool=true", "tool-format=v1"],
            false, null, null, false, out _, out _));
        Assert.True(PackageQueryOptions.TryCreate("Contoso.*",
            ["tool-format=v1", "tool-format=v2"],
            false, null, null, false, out var options, out var error), error.ToString());
        Assert.Equal(2, options!.Plan.Terms.Length);
        Assert.True(PackageQueryOptions.TryCreate("Contoso.*",
            ["skill=true", "skill=true"],
            false, null, null, false, out options, out error), error.ToString());
        Assert.Single(options!.Plan.Terms);

        Assert.True(
            PackageQueryOptions.TryCreate(
                "Contoso.Package",
                [
                    "library-literal=shared-literal-use-marker",
                    "library-literal=shared-literal-use-marker",
                ],
                nuspecOnly: false,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                targetFramework: "net10.0",
                out options,
                out error),
            error.ToString());
        Assert.Single(
            options!.Plan.Terms,
            term => term.Key == PackageQuery.LibraryLiteralTermKey);

        Assert.False(
            PackageQueryOptions.TryCreate(
                "Contoso.Package",
                [
                    "library-literal=shared-literal-use-marker",
                    "library-literal=different-literal",
                ],
                nuspecOnly: false,
                take: null,
                rowSelection: null,
                includePrerelease: false,
                targetFramework: "net10.0",
                out _,
                out error));
        Assert.Contains("cannot be combined", error.Message);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1001, false)]
    [InlineData(21, true)]
    public void InvalidCandidateBudgets_AreRejected(int take, bool packageContentTerm)
    {
        string[] terms = packageContentTerm
            ? ["skill=true"]
            : [];
        Assert.False(PackageQueryOptions.TryCreate(
            "Contoso.*",
            terms,
            false,
            take,
            null,
            false,
            out _,
            out _));
    }

    [Fact]
    public void CliPlan_PreservesAbsentMatchBudget()
    {
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [],
            false,
            300,
            null,
            false,
            out var options,
            out var error),
            error.ToString());
        Assert.Equal(300, options!.Plan.MaximumCandidates);
        Assert.Null(options.Plan.MaximumMatches);
    }

    [Fact]
    public void SemanticHeadWithoutTake_BoundsDirectRowsAndMatches()
    {
        RowSelectionIntent<string> head = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(2)]);
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [],
            nuspecOnly: false,
            take: null,
            rowSelection: head,
            includePrerelease: false,
            out var options,
            out var error),
            error.ToString());
        Assert.Equal(2, options!.Plan.MaximumCandidates);
        Assert.Equal(2, options.Plan.MaximumMatches);
        Assert.True(options.SemanticHeadPushedDown);
    }

    [Fact]
    public void SemanticHeadWithContentTerm_BoundsMatchesWithinContentCandidateCeiling()
    {
        RowSelectionIntent<string> head = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(2)]);
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            ["skill=true"],
            nuspecOnly: false,
            take: null,
            rowSelection: head,
            includePrerelease: false,
            out var options,
            out var error),
            error.ToString());
        Assert.Equal(
            PackageQuery.MaximumPackageContentCandidates,
            options!.Plan.MaximumCandidates);
        Assert.Equal(2, options.Plan.MaximumMatches);
        Assert.True(options.SemanticHeadPushedDown);
    }

    [Fact]
    public void ExplicitTake_PreventsSemanticHeadPushdown()
    {
        RowSelectionIntent<string> head = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(2)]);
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [],
            nuspecOnly: false,
            take: 100,
            rowSelection: head,
            includePrerelease: false,
            out var options,
            out var error),
            error.ToString());
        Assert.Equal(100, options!.Plan.MaximumCandidates);
        Assert.Null(options.Plan.MaximumMatches);
        Assert.False(options.SemanticHeadPushedDown);
    }

    [Theory]
    [InlineData("Contoso.*", PackageQueryOptions.MaximumCandidates)]
    [InlineData("Contoso.First", 1)]
    public void SemanticHeadAboveWorkLimit_RemainsSemanticOnly(
        string input,
        int expectedCandidateLimit)
    {
        RowSelectionIntent<string> head = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(1_001)]);
        Assert.True(PackageQueryOptions.TryCreate(
            input,
            [],
            nuspecOnly: false,
            take: null,
            rowSelection: head,
            includePrerelease: false,
            out var options,
            out var error),
            error.ToString());
        Assert.Equal(
            expectedCandidateLimit,
            options!.Plan.MaximumCandidates);
        Assert.Null(options.Plan.MaximumMatches);
        Assert.False(options.SemanticHeadPushedDown);
    }

    [Fact]
    public async Task SemanticHeadPushdown_StopsAtRequestedRowsWithoutWarning()
    {
        RowSelectionIntent<string> head = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(2)]);
        Assert.True(PackageQueryOptions.TryCreate(
            "Contoso.*",
            [],
            nuspecOnly: false,
            take: null,
            rowSelection: head,
            includePrerelease: false,
            out var options,
            out var error),
            error.ToString());

        using var source = Source(out _);
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                options! with
                {
                    Tabular = true,
                    Tsv = true,
                },
                source,
                null));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Contoso.First", result.Output);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.DoesNotContain("Contoso.Third", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task DependsTerms_AndAcrossManifestDependenciesWithoutPackageContent()
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Contoso.*",
                [
                    "depends=Dependency.One",
                    "depends=Dependency.Two",
                ],
                nuspecOnly: false,
                take: 3,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        using var source = Source(out var fixture);
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                options! with
                {
                    JsonOutput = true,
                    CompactJson = true,
                },
                source,
                null));

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement match = Assert.Single(
            document.RootElement.GetProperty("results").EnumerateArray());
        Assert.Equal(
            "Contoso.Second",
            match.GetProperty("package").GetProperty("packageId").GetString());
        JsonElement[] evidence =
        [
            .. match.GetProperty("evidence").EnumerateArray(),
        ];
        string[] dependencyPreviews =
        [
            .. evidence
                .Where(item =>
                    item.GetProperty("id").GetString()
                        == PackageQuery.DependsTermKey)
                .SelectMany(item =>
                    item.GetProperty("summary")
                        .GetProperty("preview")
                        .EnumerateArray())
                .Select(item => item.GetString()!),
        ];
        Assert.Contains(
            dependencyPreviews,
            item => item
                .Contains("Dependency.One 1.0.0", StringComparison.Ordinal));
        Assert.Contains(
            dependencyPreviews,
            item => item
                .Contains("Dependency.Two 2.0.0", StringComparison.Ordinal));
        Assert.Equal(3, fixture.ManifestRequests);
        Assert.Equal(0, fixture.PackageRequests);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task CrossPrefixDependenciesTerm_UsesManifestEvidenceWithoutPackageContent()
    {
        using var source = Source(out var fixture);
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                Options("dependencies=cross-prefix"),
                source,
                null));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.Contains("Contoso.Third", result.Output);
        Assert.DoesNotContain("Contoso.First", result.Output);
        Assert.Contains("cross-prefix", result.Output);
        Assert.Equal(3, fixture.ManifestRequests);
        Assert.Equal(0, fixture.PackageRequests);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task DependsStartsWithTerm_UsesManifestEvidenceWithoutPackageContent()
    {
        using var source = Source(out var fixture);
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                Options("depends starts-with Dependency."),
                source,
                null));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.Contains("Contoso.Third", result.Output);
        Assert.DoesNotContain("Contoso.First", result.Output);
        Assert.Contains("Dependency.", result.Output);
        Assert.Equal(3, fixture.ManifestRequests);
        Assert.Equal(0, fixture.PackageRequests);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task LicenseTerm_MatchesManifestWithoutPackageContent()
    {
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Contoso.*",
                ["license=OSMF"],
                nuspecOnly: true,
                take: 3,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? options,
                out OptionError error),
            error.ToString());

        using var source = Source(out var fixture);
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                options! with
                {
                    Tabular = true,
                    Tsv = true,
                },
                source,
                null));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.DoesNotContain("Contoso.First", result.Output);
        Assert.DoesNotContain("Contoso.Third", result.Output);
        Assert.Contains(
            "\tOSMF\n",
            result.Output.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("Nuspec license", result.Output);
        Assert.Equal(3, fixture.ManifestRequests);
        Assert.Equal(0, fixture.PackageRequests);
        Assert.Empty(result.Error);

        using var jsonSource = Source(out var jsonFixture);
        var jsonResult = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                options! with
                {
                    JsonOutput = true,
                    Tabular = false,
                    Tsv = false,
                },
                jsonSource,
                null));

        Assert.Equal(0, jsonResult.ExitCode);
        using var json = JsonDocument.Parse(jsonResult.Output);
        JsonElement row = Assert.Single(
            json.RootElement.GetProperty("results").EnumerateArray());
        JsonElement answer = Assert.Single(
            row.GetProperty("answers").EnumerateArray());
        Assert.Equal("OSMF", answer.GetProperty("value").GetString());
        JsonElement licenseEvidence = Assert.Single(
            row.GetProperty("evidence").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "license");
        JsonElement declarationValue = Assert.Single(
            licenseEvidence.GetProperty("properties").EnumerateArray(),
            item =>
                item.GetProperty("name").GetString() == "declaration-value");
        Assert.Equal(
            "OSMFEULA.txt",
            declarationValue.GetProperty("value").GetString());
        Assert.Equal(3, jsonFixture.ManifestRequests);
        Assert.Equal(0, jsonFixture.PackageRequests);
        Assert.Empty(jsonResult.Error);
    }

    [Fact]
    public async Task DependsTerm_HeadStopsAfterItsWitnessAndCountEvaluatesThePopulation()
    {
        RowSelectionIntent<string> head = Head(1);
        Assert.True(
            PackageQueryOptions.TryCreate(
                "Contoso.*",
                ["depends=Dependency.One"],
                nuspecOnly: false,
                take: null,
                rowSelection: head,
                includePrerelease: false,
                out PackageQueryOptions? headOptions,
                out OptionError headError),
            headError.ToString());

        using (var headSource = Source(out var headFixture))
        {
            var headResult = await ConsoleCapture.RunAsync(() =>
                PackageQueryCommand.ExecuteAsync(
                    headOptions! with
                    {
                        Tabular = true,
                        Tsv = true,
                    },
                    headSource,
                    null));
            Assert.Equal(0, headResult.ExitCode);
            Assert.Contains("Contoso.Second", headResult.Output);
            Assert.DoesNotContain("Contoso.Third", headResult.Output);
            Assert.Equal(2, headFixture.ManifestRequests);
            Assert.Empty(headResult.Error);
        }

        using (var headCountSource = Source(out var headCountFixture))
        {
            var headCountResult = await ConsoleCapture.RunAsync(() =>
                PackageQueryCommand.ExecuteAsync(
                    headOptions! with
                    {
                        Count = true,
                    },
                    headCountSource,
                    null));
            Assert.Equal(0, headCountResult.ExitCode);
            Assert.Equal("1", headCountResult.Output.Trim());
            Assert.Equal(2, headCountFixture.ManifestRequests);
            Assert.Empty(headCountResult.Error);
        }

        Assert.True(
            PackageQueryOptions.TryCreate(
                "Contoso.*",
                ["depends=Dependency.One"],
                nuspecOnly: false,
                take: 2,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? boundedCountOptions,
                out OptionError boundedCountError),
            boundedCountError.ToString());

        using (var boundedCountSource = Source(out var boundedCountFixture))
        {
            var boundedCountResult = await ConsoleCapture.RunAsync(() =>
                PackageQueryCommand.ExecuteAsync(
                    boundedCountOptions! with { Count = true },
                    boundedCountSource,
                    null));
            Assert.Equal(1, boundedCountResult.ExitCode);
            Assert.Empty(boundedCountResult.Output);
            Assert.Equal(2, boundedCountFixture.ManifestRequests);
            Assert.Contains(
                "Cannot count Package Query rows",
                boundedCountResult.Error);
            Assert.Contains(
                "CandidateLimitReached",
                boundedCountResult.Error);
        }

        Assert.True(
            PackageQueryOptions.TryCreate(
                "Contoso.*",
                ["depends=Dependency.One"],
                nuspecOnly: false,
                take: 3,
                rowSelection: null,
                includePrerelease: false,
                out PackageQueryOptions? countOptions,
                out OptionError countError),
            countError.ToString());

        using var countSource = Source(out var countFixture);
        var countResult = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                countOptions! with { Count = true },
                countSource,
                null));
        Assert.Equal(0, countResult.ExitCode);
        Assert.Equal("2", countResult.Output.Trim());
        Assert.Equal(3, countFixture.ManifestRequests);
        Assert.Equal(0, countFixture.PackageRequests);
        Assert.Empty(countResult.Error);
    }

    [Fact]
    public async Task NuspecOnly_RejectsContentTermBeforeAcquisition()
    {
        var result = await Run(
            "package",
            "query",
            "Contoso.*",
            "--nuspec-only",
            "--where",
            "skill=true");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("cannot be combined with --nuspec-only", result.Error);
    }

    [Theory]
    [InlineData("--where", "tool=true")]
    [InlineData("--take", "20")]
    [InlineData("--nuspec-only", null)]
    public async Task QueryDiscovery_RejectsExecutionGestures(string flag, string? value)
    {
        var result = await Run(
            ["package", "query", "-Q", "Packages", flag,
                .. value is null ? [] : new[] { value }]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("does not execute", result.Error);
    }

    [Fact]
    public async Task QueryDiscovery_RejectsInheritedParentExecutionGesture()
    {
        var result = await Run(
            "package",
            "--depth",
            "2",
            "query",
            "-Q",
            "Packages");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--depth cannot be combined with query discovery",
            result.Error);
    }

    [Fact]
    public async Task PatternlessFindPrefix_UsesPackageQueryGuidance()
    {
        var result = await Run("find", "--package-prefix", "Contoso.");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("requires a type or member pattern", result.Error);
        Assert.Contains("package query", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task RemovedFindLiteral_UsesPackageQueryGuidance()
    {
        var result = await Run(
            "find",
            "--literal",
            "marker",
            "--package",
            "Contoso@1.0.0",
            "--tfm",
            "net10.0");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("'find --literal' is no longer valid", result.Error);
        Assert.Contains(
            "library-literal=TEXT",
            result.Error);
        Assert.Contains("not an equivalent replacement", result.Error);
    }

    [Theory]
    [InlineData("--where", "tool=true")]
    [InlineData("-S", "Packages")]
    public async Task ApiFindRejectsPackageQuerySelectors(
        string option,
        string value)
    {
        var result = await Run(
            "find",
            "JsonDocument",
            "--platform",
            option,
            value);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Unrecognized command or argument", result.Error);
        Assert.Contains(option, result.Error);
    }
    [Fact]
    public async Task RemovedPackageSearch_UsesPackageQueryGuidance()
    {
        var result = await Run("package", "search", "Contoso");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("'package search' has been removed", result.Error);
        Assert.Contains("package query", result.Error);
    }

    [Theory]
    [InlineData("--lines")]
    [InlineData("--tail-lines")]
    public async Task PackageQueryAcceptsLineUnitsBeforeSourceValidation(
        string lineUnit)
    {
        var result = await Run(
            "package",
            "query",
            "Contoso.*",
            "-n",
            "1",
            lineUnit,
            "--source",
            "https://example.invalid/index.json");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("NuGet.org", result.Error);
        Assert.DoesNotContain("not available with package query", result.Error);
    }

    [Fact]
    public async Task DataDiscovery_UsesUnifiedPackageQuerySchemaWithoutAcquisition()
    {
        var query = await Run(
            "package",
            "query",
            "-D",
            "Packages",
            "--json");
        Assert.Equal(0, query.ExitCode);
        Assert.Contains("Source", query.Output);
        Assert.Contains("Answer", query.Output);
        Assert.Contains("Library", query.Output);
        Assert.Contains("Assembly", query.Output);
        Assert.Contains("Target Framework", query.Output);
        Assert.Contains("Unevaluated Siblings", query.Output);
        Assert.Contains("Occurrences", query.Output);
        Assert.Contains("Evidence", query.Output);
        Assert.Contains("Root", query.Output);

        var summary = await Run(
            "package",
            "query",
            "-D",
            "Query Summary",
            "--json");
        Assert.Equal(0, summary.ExitCode);
        Assert.Contains("Evaluation Failures", summary.Output);
    }

    [Fact]
    public void EnvelopeAdmissionRejectsPostServiceShaping()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] accepted =
            CommandLineBuilder.PreprocessArgs(
                [
                    "package",
                    "query",
                    "Contoso.*",
                    "--envelope",
                    "--compact",
                ],
                root);
        Assert.Empty(root.Parse(accepted).Errors);

        foreach (string[] arguments in new[]
        {
            new[] { "-n", "1" },
            new[] { "-n", "1", "--lines" },
            new[] { "-S", "Packages" },
            new[] { "--count" },
        })
        {
            string[] rejected =
                CommandLineBuilder.PreprocessArgs(
                    [
                        "package",
                        "query",
                        "Contoso.*",
                        "--envelope",
                        .. arguments,
                    ],
                    root);
            Assert.Contains(
                root.Parse(rejected).Errors,
                error => error.Message.Contains(
                    "--envelope cannot be combined with",
                    StringComparison.Ordinal));
        }

        string[] rejectedTree =
            CommandLineBuilder.PreprocessArgs(
                [
                    "package",
                    "query",
                    "Contoso.*",
                    "--json",
                    "--tree",
                ],
                root);
        Assert.Contains(
            root.Parse(rejectedTree).Errors,
            error => error.Message.Contains(
                "--tree with package query --json requires schema discovery",
                StringComparison.Ordinal));

        string[] discoveryTree =
            CommandLineBuilder.PreprocessArgs(
                [
                    "package",
                    "query",
                    "-D",
                    "Packages",
                    "--json",
                    "--tree",
                ],
                root);
        Assert.Empty(root.Parse(discoveryTree).Errors);
    }

    [Fact]
    public async Task DataDiscovery_UsesAuthoredCategoryBeforeAlphabeticalSections()
    {
        var catalog = await Run(
            "package",
            "query",
            "-D",
            "--table");
        var category = await Run(
            "package",
            "query",
            "-D",
            SectionCategoryNames.Query,
            "--table");

        Assert.Equal(0, catalog.ExitCode);
        Assert.Empty(catalog.Error);
        int categoryIndex = catalog.Output.IndexOf(
            SectionCategoryNames.Query,
            StringComparison.Ordinal);
        int literalsIndex = catalog.Output.IndexOf(
            PackageQuerySections.LiteralStringsName,
            StringComparison.Ordinal);
        int packagesIndex = catalog.Output.IndexOf(
            PackageProfileSections.Packages,
            StringComparison.Ordinal);
        int summaryIndex = catalog.Output.IndexOf(
            PackageQuerySections.QuerySummaryName,
            StringComparison.Ordinal);
        Assert.True(categoryIndex >= 0);
        Assert.True(categoryIndex < literalsIndex);
        Assert.True(literalsIndex < packagesIndex);
        Assert.True(packagesIndex < summaryIndex);
        Assert.DoesNotContain("@All", catalog.Output);
        Assert.DoesNotContain("@Default", catalog.Output);
        Assert.DoesNotContain("@Hidden", catalog.Output);

        Assert.Equal(0, category.ExitCode);
        Assert.Empty(category.Error);
        Assert.Contains(PackageProfileSections.Packages, category.Output);
        Assert.Contains(PackageQuerySections.LiteralStringsName, category.Output);
        Assert.Contains(PackageQuerySections.QuerySummaryName, category.Output);
    }

    [Fact]
    public async Task LiteralStringsSectionRequiresLibraryLiteralQuery()
    {
        var result = await Run(
            "package",
            "query",
            "Microsoft.Identity.Client",
            "-S",
            PackageQuerySections.LiteralStringsName);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires --where \"library-literal=TEXT\" and --tfm TFM",
            result.Error);
    }

    [Fact]
    public async Task SemanticHeadRunsAfterAllCandidatesAndKeepsOnePackagePerRow()
    {
        using var source = Source(out var fixture);
        var result = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(
            Options("depends=Dependency.One", rowSelection: Head(1)),
            source,
            null));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.DoesNotContain("Contoso.First", result.Output);
        Assert.DoesNotContain("Contoso.Third", result.Output);
        Assert.Equal(3, fixture.ManifestRequests);
        Assert.Equal(0, fixture.PackageRequests);
        Assert.Empty(result.Error);
        Assert.Equal(2, result.Output.TrimEnd().Split('\n').Length);
    }

    [Fact]
    public async Task SemanticHead_PreservesALaterCandidateFailure()
    {
        using var source = Source(out var fixture);
        fixture.MissingManifest = "contoso.third";

        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                Options("depends=Dependency.One", rowSelection: Head(1)),
                source,
                null));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(3, fixture.ManifestRequests);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.DoesNotContain("Contoso.Third", result.Output);
        Assert.Contains("ManifestAcquisition", result.Error);
    }

    [Theory]
    [InlineData("markdown")]
    [InlineData("tsv")]
    [InlineData("jsonl")]
    [InlineData("json")]
    [InlineData("count")]
    public async Task OutputModes_UseTheSameWindowedMatches(string format)
    {
        using var source = Source(out _);
        var options = Options(
            "depends=Dependency.One",
            rowSelection: Head(1)) with
        {
            Count = format == "count",
            Tabular = format is "tsv" or "jsonl",
            Tsv = format == "tsv",
            Jsonl = format == "jsonl",
            JsonOutput = format == "json",
        };
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(options, source, null));
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        if (format == "count")
        {
            Assert.Equal("1", result.Output.Trim());
            return;
        }
        Assert.Contains("Contoso.Second", result.Output);
        Assert.DoesNotContain("Contoso.Third", result.Output);
        if (format == "json")
        {
            using var json = JsonDocument.Parse(result.Output);
            Assert.Single(json.RootElement.GetProperty("packages").EnumerateArray());
        }
        if (format == "jsonl")
        {
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal("1.0.0", json.RootElement.GetProperty("version").GetString());
            Assert.Equal(
                "Dependency.One",
                json.RootElement.GetProperty("answer").GetString());
            Assert.False(json.RootElement.TryGetProperty("evidence", out _));
        }
    }

    [Theory]
    [InlineData("markdown", "## Packages", "## Query Summary")]
    [InlineData("table", "Package", "Candidates  Matches")]
    [InlineData("tsv", "package\tversion", "candidates\tmatches")]
    [InlineData("jsonl", "\"package\"", "\"candidates\"")]
    public async Task DefaultOutput_AdaptsToPackageOrSummaryShape(
        string format,
        string packageMarker,
        string summaryMarker)
    {
        using var source = Source(out _);
        var matched = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                WithFormat(
                    OptionsForInput(
                        "Contoso.Second*",
                        ["depends=Dependency.One"]),
                    format),
                source,
                null));
        var empty = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                WithFormat(
                    OptionsForInput(
                        "Contoso.First*",
                        ["depends=Dependency.One"]),
                    format),
                source,
                null));

        Assert.Equal(0, matched.ExitCode);
        Assert.Equal(0, empty.ExitCode);
        Assert.Empty(matched.Error);
        Assert.Empty(empty.Error);
        Assert.Contains("Contoso.Second", matched.Output);
        Assert.Contains(packageMarker, matched.Output);
        Assert.DoesNotContain(summaryMarker, matched.Output);
        Assert.Contains(summaryMarker, empty.Output);
        Assert.DoesNotContain(packageMarker, empty.Output);
        Assert.Contains("1", empty.Output);
        Assert.Contains("0", empty.Output);
    }

    [Theory]
    [InlineData("markdown", "| Package | Version | Tier | Source | Answer |")]
    [InlineData("table", "Package  Version  Tier  Source  Answer")]
    [InlineData("tsv", "package\tversion\ttier\tsource\tanswer")]
    [InlineData("jsonl", null)]
    [InlineData("json", "\"packages\": []")]
    public async Task ExplicitPackages_PreservesEmptyPackageShape(
        string format,
        string? expectedOutput)
    {
        using var source = Source(out _);
        var options = WithFormat(
            OptionsForInput(
                "Contoso.First*",
                ["depends=Dependency.One"]),
            format) with
        {
            IncludeSections =
            [
                PackageProfileSections.Packages,
            ],
        };
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(options, source, null));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        if (expectedOutput is null)
            Assert.Empty(result.Output);
        else
            Assert.Contains(expectedOutput, result.Output);
        Assert.DoesNotContain("Query Summary", result.Output);
        Assert.DoesNotContain("query_summary", result.Output);
    }

    [Fact]
    public async Task PackagesSelection_PreservesEmptyPackageShape()
    {
        using var source = Source(out _);
        var options = OptionsForInput(
            "Contoso.First*",
            ["depends=Dependency.One"]) with
        {
            JsonOutput = true,
            Tabular = false,
            IncludeSections =
            [
                PackageProfileSections.Packages,
            ],
        };
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(options, source, null));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Empty(json.RootElement.GetProperty("packages").EnumerateArray());
        Assert.False(json.RootElement.TryGetProperty("query_summary", out _));
    }

    [Fact]
    public async Task ExplicitQuerySummary_RemainsSummaryWhenPackagesMatch()
    {
        using var source = Source(out _);
        var options = OptionsForInput(
            "Contoso.Second*",
            ["depends=Dependency.One"]) with
        {
            JsonOutput = true,
            Tabular = false,
            IncludeSections =
            [
                PackageQuerySections.QuerySummaryName,
            ],
        };
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(options, source, null));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.TryGetProperty("packages", out _));
        JsonElement summary = Assert.Single(
            json.RootElement
                .GetProperty("query_summary")
                .EnumerateArray());
        Assert.Equal("1", summary.GetProperty("matches").GetString());
    }

    [Fact]
    public async Task ExplicitQueryCategory_ComposesSectionsAlphabetically()
    {
        using var source = Source(out _);
        var options = OptionsForInput(
            "Contoso.Second*",
            ["depends=Dependency.One"]) with
        {
            JsonOutput = true,
            Tabular = false,
            IncludeSections =
            [
                PackageQuerySections.QuerySummaryName,
                PackageProfileSections.Packages,
            ],
        };
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(options, source, null));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        int packagesIndex = result.Output.IndexOf(
            "\"packages\"",
            StringComparison.Ordinal);
        int summaryIndex = result.Output.IndexOf(
            "\"query_summary\"",
            StringComparison.Ordinal);
        Assert.True(packagesIndex >= 0);
        Assert.True(packagesIndex < summaryIndex);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Single(json.RootElement.GetProperty("packages").EnumerateArray());
        Assert.Single(
            json.RootElement.GetProperty("query_summary").EnumerateArray());
    }

    [Fact]
    public async Task QuerySummary_DistinguishesMissingAndFilteredPackages()
    {
        using var source = Source(out _);
        PackageQueryOptions missingOptions = OptionsForInput(
            "Missing.Package*",
            ["depends=Dependency.One"]) with
        {
            JsonOutput = true,
            Tabular = false,
            IncludeSections =
            [
                PackageQuerySections.QuerySummaryName,
            ],
        };
        PackageQueryOptions filteredOptions = OptionsForInput(
            "Contoso.First*",
            ["depends=Dependency.One"]) with
        {
            JsonOutput = true,
            Tabular = false,
            IncludeSections =
            [
                PackageQuerySections.QuerySummaryName,
            ],
        };

        var missing = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                missingOptions,
                source,
                null));
        var filtered = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                filteredOptions,
                source,
                null));

        Assert.Equal(0, missing.ExitCode);
        Assert.Equal(0, filtered.ExitCode);
        using var missingJson = JsonDocument.Parse(missing.Output);
        using var filteredJson = JsonDocument.Parse(filtered.Output);
        JsonElement missingSummary = Assert.Single(
            missingJson.RootElement
                .GetProperty("query_summary")
                .EnumerateArray());
        JsonElement filteredSummary = Assert.Single(
            filteredJson.RootElement
                .GetProperty("query_summary")
                .EnumerateArray());
        Assert.Equal(
            "0",
            missingSummary.GetProperty("candidates").GetString());
        Assert.Equal(
            "1",
            filteredSummary.GetProperty("candidates").GetString());
        Assert.Equal(
            "0",
            missingSummary.GetProperty("matches").GetString());
        Assert.Equal(
            "0",
            filteredSummary.GetProperty("matches").GetString());
    }

    [Theory]
    [InlineData(PackageQuerySections.QuerySummaryName)]
    [InlineData(SectionCategoryNames.Query)]
    public async Task CountRejectsNonPackageSelectionBeforeAcquisition(
        string selection)
    {
        var result = await Run(
            "package",
            "query",
            "Contoso.*",
            "--count",
            "-S",
            selection);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--count supports the Packages section only",
            result.Error);
    }

    [Fact]
    public async Task TabularOutputRejectsMultipleExplicitSectionsBeforeAcquisition()
    {
        var result = await Run(
            "package",
            "query",
            "Contoso.*",
            "--tsv",
            "-S",
            "Packages",
            "-S",
            "Query Summary");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--table, --tsv, and --jsonl display one section at a time",
            result.Error);
    }

    [Fact]
    public async Task EnvelopeContentMatchesUnprojectedJson()
    {
        using var source = Source(out _);
        PackageQueryOptions baseline =
            Options("depends=Dependency.One") with
            {
                Tabular = false,
                Tsv = false,
                JsonOutput = true,
                CompactJson = true,
            };
        var json = await ConsoleCapture.RunAsync(
            () => PackageQueryCommand.ExecuteAsync(
                baseline,
                source,
                null));
        var envelope = await ConsoleCapture.RunAsync(
            () => PackageQueryCommand.ExecuteAsync(
                baseline with
                {
                    JsonOutput = false,
                    EnvelopeOutput = true,
                },
                source,
                null));

        Assert.Equal(0, json.ExitCode);
        Assert.Equal(0, envelope.ExitCode);
        using JsonDocument contentDocument = JsonDocument.Parse(json.Output);
        using JsonDocument envelopeDocument =
            JsonDocument.Parse(envelope.Output);
        JsonElement root = envelopeDocument.RootElement;
        Assert.Equal(
            "package-query",
            root.GetProperty("result_kind").GetString());
        Assert.NotEmpty(
            contentDocument.RootElement
                .GetProperty("results")[0]
                .GetProperty("evidence")
                .EnumerateArray());
        Assert.True(
            JsonElement.DeepEquals(
                contentDocument.RootElement,
                root.GetProperty("content")));
        Assert.Equal(
            "nonProjectable",
            root.GetProperty("share").GetProperty("kind").GetString());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CliPackageQueryContentAndEnvelopeAreIdentical()
    {
        var json = await Run(
            "package",
            "query",
            "System.Text.Json",
            "--json",
            "--compact");
        var envelope = await Run(
            "package",
            "query",
            "System.Text.Json",
            "--envelope",
            "--compact");

        Assert.Equal(0, json.ExitCode);
        Assert.Equal(0, envelope.ExitCode);
        using JsonDocument contentDocument = JsonDocument.Parse(json.Output);
        using JsonDocument envelopeDocument =
            JsonDocument.Parse(envelope.Output);
        Assert.True(
            JsonElement.DeepEquals(
                contentDocument.RootElement,
                envelopeDocument.RootElement.GetProperty("content")));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CliAssemblySemanticContentAndEnvelopeAreIdentical()
    {
        string[] arguments =
        [
            "package",
            "query",
            "Newtonsoft.Json",
            "--where",
            "library-literal=Unexpected end when reading JSON",
            "--tfm",
            "net6.0",
        ];
        var json = await Run([.. arguments, "--json", "--compact"]);
        var envelope =
            await Run([.. arguments, "--envelope", "--compact"]);
        var markdown = await Run(arguments);

        Assert.Equal(0, json.ExitCode);
        Assert.Equal(0, envelope.ExitCode);
        Assert.Equal(0, markdown.ExitCode);
        using JsonDocument contentDocument = JsonDocument.Parse(json.Output);
        using JsonDocument envelopeDocument =
            JsonDocument.Parse(envelope.Output);
        Assert.True(
            JsonElement.DeepEquals(
                contentDocument.RootElement,
                envelopeDocument.RootElement.GetProperty("content")));
        JsonElement literal = contentDocument.RootElement
            .GetProperty("results")[0]
            .GetProperty("libraryLiteral");
        string rootToken = literal
            .GetProperty("rootRequest")
            .GetString()
            ?? throw new InvalidOperationException(
                "Expected an encoded Package Root reopening token.");
        string libraryPath = literal
            .GetProperty("selectedAsset")
            .GetProperty("path")
            .GetString()
            ?? throw new InvalidOperationException(
                "Expected a selected implementation library path.");
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                rootToken,
                out PackageRootReacquisitionRequest? rootRequest));
        Assert.Equal(
            "Newtonsoft.Json",
            rootRequest.Coordinate.PackageId,
            ignoreCase: true);
        Assert.Contains(libraryPath, markdown.Output);
        Assert.Contains("/IL_", markdown.Output);
        Assert.Contains(rootToken, markdown.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CliLiteralStringQueryItemizesPhysicalUrlOccurrences()
    {
        string[] arguments =
        [
            "package",
            "query",
            "Microsoft.Identity.Client",
            "--where",
            "library-literal=https://",
            "--tfm",
            "net8.0",
        ];
        var content = await Run([.. arguments, "--json", "--compact"]);
        var projected = await Run(
            [
                .. arguments,
                "-S",
                PackageQuerySections.LiteralStringsName,
                "--json",
                "--compact",
            ]);

        Assert.Equal(0, content.ExitCode);
        Assert.Equal(0, projected.ExitCode);
        Assert.Empty(content.Error);
        Assert.Empty(projected.Error);

        using JsonDocument contentDocument = JsonDocument.Parse(content.Output);
        JsonElement result = contentDocument.RootElement
            .GetProperty("results")[0];
        string package = result
            .GetProperty("package")
            .GetProperty("packageId")
            .GetString()
            ?? throw new InvalidOperationException(
                "Expected a package identity.");
        JsonElement libraryLiteral = result.GetProperty("libraryLiteral");
        JsonElement[] occurrences =
        [
            .. libraryLiteral
                .GetProperty("occurrences")
                .EnumerateArray()
        ];
        JsonElement[] libraryOccurrences =
        [
            .. libraryLiteral
                .GetProperty("libraryOccurrences")
                .EnumerateArray()
        ];
        Assert.Equal(occurrences.Length, libraryOccurrences.Length);

        using JsonDocument projectedDocument =
            JsonDocument.Parse(projected.Output);
        JsonElement[] rows =
        [
            .. projectedDocument.RootElement
                .GetProperty("literal_strings")
                .EnumerateArray(),
        ];
        Assert.Equal(occurrences.Length, rows.Length);
        for (int index = 0; index < rows.Length; index++)
        {
            JsonElement libraryOccurrence = libraryOccurrences[index];
            JsonElement occurrence =
                libraryOccurrence.GetProperty("evidence");
            JsonElement address = occurrence.GetProperty("address");
            JsonElement row = rows[index];
            string library = libraryOccurrence
                .GetProperty("selectedAsset")
                .GetProperty("path")
                .GetString()
                ?? throw new InvalidOperationException(
                    "Expected an occurrence implementation library.");

            Assert.Equal(package, row.GetProperty("package").GetString());
            Assert.Equal(library, row.GetProperty("library").GetString());
            Assert.Equal(
                $"0x{address.GetProperty("methodDefinitionToken").GetInt32():X8}",
                row.GetProperty("method_token").GetString());
            Assert.Equal(
                $"IL_{address.GetProperty("ilOffset").GetInt32():X4}",
                row.GetProperty("il_offset").GetString());
            Assert.Equal(
                occurrence.GetProperty("literalText").GetString(),
                row.GetProperty("literal").GetString());
            Assert.Equal(5, row.EnumerateObject().Count());
        }

        string[] literals =
        [
            .. rows.Select(row =>
                row.GetProperty("literal").GetString()
                ?? throw new InvalidOperationException(
                    "Expected a projected literal string.")),
        ];
        Assert.Contains(
            literals,
            literal =>
                literal.Split(
                    "https://",
                    StringSplitOptions.None).Length > 2);
        Assert.Contains(
            literals.GroupBy(
                literal => literal,
                StringComparer.Ordinal),
            group => group.Count() > 1);
        Assert.Contains(
            literals,
            literal => literal.StartsWith(
                "https://",
                StringComparison.Ordinal));
        Assert.Contains(
            literals,
            literal =>
                !literal.StartsWith("https://", StringComparison.Ordinal)
                && literal.Contains("https://", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CliLiteralStringQueryFindsCompanionLibrary()
    {
        var search = await Run(
            ["explain", "literal", "--json"]);
        Assert.Equal(0, search.ExitCode);
        Assert.Empty(search.Error);
        using JsonDocument searchDocument =
            JsonDocument.Parse(search.Output);
        string explanationPath =
            searchDocument.RootElement
                .GetProperty("results")[0]
                .GetProperty("resource_path")
                .GetString()!;
        Assert.Equal(
            "package-query/query/facets/library-literal",
            explanationPath);

        var explanation = await Run(
            ["explain", explanationPath, "--json"]);
        Assert.Equal(0, explanation.ExitCode);
        Assert.Empty(explanation.Error);
        using JsonDocument explanationDocument =
            JsonDocument.Parse(explanation.Output);
        Assert.Equal(
            PackageQuery.LibraryLiteralTermKey,
            explanationDocument.RootElement
                .GetProperty("resources")[0]
                .GetProperty("details")
                .GetProperty("key")
                .GetString());

        string[] arguments =
        [
            "package",
            "query",
            "Microsoft.Azure.SignalR",
            "--where",
            "library-literal=https://",
            "--tfm",
            "net8.0",
        ];
        var content = await Run([.. arguments, "--json", "--compact"]);
        var projected = await Run(
            [
                .. arguments,
                "-S",
                PackageQuerySections.LiteralStringsName,
                "--json",
                "--compact",
            ]);

        Assert.Equal(0, content.ExitCode);
        Assert.Equal(0, projected.ExitCode);
        Assert.Empty(content.Error);
        Assert.Empty(projected.Error);

        using JsonDocument contentDocument = JsonDocument.Parse(content.Output);
        JsonElement root = contentDocument.RootElement;
        JsonElement result = Assert.Single(
            root.GetProperty("results").EnumerateArray());
        Assert.Equal(
            "1.33.1",
            result.GetProperty("package").GetProperty("version").GetString());
        JsonElement[] libraries =
        [
            .. Assert.Single(
                    root.GetProperty("libraryLiteralAssessments")
                        .EnumerateArray())
                .GetProperty("libraries")
                .EnumerateArray(),
        ];
        JsonElement matched = Assert.Single(
            libraries,
            library => library.GetProperty("kind").GetInt32() == 0);
        JsonElement noMatch = Assert.Single(
            libraries,
            library => library.GetProperty("kind").GetInt32() == 1);
        Assert.Equal(
            "lib/net8.0/Microsoft.Azure.SignalR.Common.dll",
            matched
                .GetProperty("selectedAsset")
                .GetProperty("path")
                .GetString());
        Assert.Equal(
            "lib/net8.0/Microsoft.Azure.SignalR.dll",
            noMatch
                .GetProperty("selectedAsset")
                .GetProperty("path")
                .GetString());
        Assert.True(matched.GetProperty("occurrences").GetInt32() > 0);
        Assert.Equal(0, noMatch.GetProperty("occurrences").GetInt32());

        using JsonDocument projectedDocument =
            JsonDocument.Parse(projected.Output);
        JsonElement[] rows =
        [
            .. projectedDocument.RootElement
                .GetProperty("literal_strings")
                .EnumerateArray(),
        ];
        Assert.NotEmpty(rows);
        Assert.All(
            rows,
            row => Assert.Equal(
                "lib/net8.0/Microsoft.Azure.SignalR.Common.dll",
                row.GetProperty("library").GetString()));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CliSelectedJsonRetainsPresentationContract()
    {
        var result = await Run(
            [
                "package",
                "query",
                "Contoso.Package.That.Does.Not.Exist.7357",
                "-S",
                "Packages",
                "--json",
                "--compact",
            ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("{\"packages\":[]}", result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CliEnvelopeIgnoresImplicitRenderingFormat()
    {
        string? previous =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "table");
            var result = await Run(
                "package",
                "query",
                "System.Text.Json",
                "--envelope",
                "--compact");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument envelope = JsonDocument.Parse(result.Output);
            Assert.Equal(
                "package-query",
                envelope.RootElement.GetProperty("result_kind").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                previous);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CliImplicitJsonRejectsTreeOutsideDiscovery()
    {
        string? previous =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "json");
            var result = await Run(
                "package",
                "query",
                "System.Text.Json",
                "--tree");

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(
                "--tree with package query --json requires schema discovery",
                result.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                previous);
        }
    }

    [Fact]
    public async Task EnvelopeRetainsFailedPackageQueryContent()
    {
        using var source = Source(out var fixture);
        fixture.SearchFails = true;
        PackageQueryOptions options =
            Options("depends=Dependency.One") with
            {
                Tabular = false,
                Tsv = false,
                EnvelopeOutput = true,
            };

        var result = await ConsoleCapture.RunAsync(
            () => PackageQueryCommand.ExecuteAsync(options, source, null));

        Assert.Equal(1, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement content = document.RootElement.GetProperty("content");
        Assert.Equal(
            (int)PackageQueryCompletionKind.Failed,
            content.GetProperty("summary")
                .GetProperty("completion")
                .GetInt32());
        Assert.Single(content.GetProperty("failures").EnumerateArray());
        Assert.Contains("Package Query completion", result.Error);
    }

    [Fact]
    public async Task SelectedJsonRetainsPresentationContractOnFailure()
    {
        using var source = Source(out var fixture);
        fixture.SearchFails = true;
        PackageQueryOptions options =
            Options("depends=Dependency.One") with
            {
                Tabular = false,
                Tsv = false,
                JsonOutput = true,
                CompactJson = true,
                IncludeSections = [PackageProfileSections.Packages],
            };

        var result = await ConsoleCapture.RunAsync(
            () => PackageQueryCommand.ExecuteAsync(options, source, null));

        Assert.Equal(1, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Empty(
            document.RootElement
                .GetProperty("packages")
                .EnumerateArray());
        Assert.Contains("Package Query completion", result.Error);
    }

    [Fact]
    public async Task TabularOutputRejectsQueryCategoryBeforeAcquisition()
    {
        var result = await Run(
            "package",
            "query",
            "Contoso.*",
            "--tsv",
            "-S",
            SectionCategoryNames.Query);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--table, --tsv, and --jsonl display one section at a time",
            result.Error);
    }

    [Fact]
    public async Task CandidateBudget_StopsBeforeAFilteredMatchAndDisclosesTheBoundary()
    {
        using var source = Source(out var fixture);
        PackageQueryOptions query = Options(
            "depends=Dependency.One",
            maximumCandidates: 1);
        var result = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(
            query with { Count = true },
            source, null));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(1, fixture.ManifestRequests);
        Assert.Contains("Cannot count Package Query rows", result.Error);
        Assert.Contains("CandidateLimitReached", result.Error);
    }

    [Fact]
    public async Task StrictWindowFailure_RetainsCandidateBoundDisclosure()
    {
        using var source = Source(out var fixture);
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(
                Options(
                    "depends=Dependency.One",
                    maximumCandidates: 1,
                    rowSelection: RowSelectionIntent<string>.Create(
                        [RowSelectionIntentOperation<string>.Window(1, 2)])),
                source,
                null));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(1, fixture.ManifestRequests);
        Assert.Contains(
            "Package Query row selection stage 1",
            result.Error);
        Assert.Contains("CandidateLimitReached", result.Error);
    }

    [Fact]
    public async Task PartialManifestFailure_RetainsMatchesAndNonzeroExit()
    {
        using var source = Source(out var fixture);
        fixture.MissingManifest = "contoso.third";
        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(Options("depends=Dependency.One"), source, null));
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Contoso.Second", result.Output);
        Assert.Contains("ManifestAcquisition", result.Error);
        Assert.DoesNotContain("Contoso.Third", result.Output);
    }

    [Fact]
    public async Task EmptySuccessAndSearchFailureRemainDistinct()
    {
        using var source = Source(out var fixture);
        var options = Options("downloads=1m") with { Count = true };
        var empty = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(options, source, null));
        Assert.Equal(0, empty.ExitCode);
        Assert.Equal("0", empty.Output.Trim());
        Assert.Empty(empty.Error);
        fixture.SearchFails = true;
        var failed = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(options, source, null));
        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("Cannot count Package Query rows", failed.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContentProvider_UsesAdmittedArchiveAndDisposesTransport(bool invalidArchive)
    {
        using var source = Source(out var fixture);
        fixture.InvalidArchive = invalidArchive;
        using var operation = new NuGetOperationContext();
        await using var provider = ContentProvider(fixture, operation);
        PackageQueryOptions query = Options(
            "dependencies=none",
            "skill=true");
        var result = await ConsoleCapture.RunAsync(() => PackageQueryCommand.ExecuteAsync(
            query, source, provider));
        Assert.Equal(invalidArchive ? 1 : 0, result.ExitCode);
        Assert.Equal(1, fixture.PackageRequests);
        Assert.True(fixture.Payload!.Disposed);
        if (invalidArchive)
            Assert.Contains("PackageContentAcquisition", result.Error);
        else
        {
            Assert.Contains("Contoso.First", result.Output);
            Assert.Contains("PackageContent", result.Output);
            Assert.DoesNotContain("Contoso.Second", result.Output);
        }
    }

    [Fact]
    public async Task ReferencesTerm_ExecutesThroughTheCliContentProvider()
    {
        using var source = Source(out var fixture);
        using var operation = new NuGetOperationContext();
        await using var provider = ContentProvider(fixture, operation);
        PackageQueryOptions query = OptionsForInput(
            "Contoso.First",
            ["references=DotnetInspector.Queries"],
            maximumCandidates: 1);

        var result = await ConsoleCapture.RunAsync(() =>
            PackageQueryCommand.ExecuteAsync(query, source, provider));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Contoso.First", result.Output);
        Assert.Contains("DotnetInspector.Queries", result.Output);
        Assert.Equal(1, fixture.PackageRequests);
        Assert.True(fixture.Payload!.Disposed);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task ContentProvider_RetainsAuthorityStorageThroughUseAndThenCleansIt()
    {
        using var source = Source(out var fixture);
        using var operation = new NuGetOperationContext();
        string root;
        await using (var provider = ContentProvider(fixture, operation))
        {
            var package = new PackageQueryPackage("Contoso.First", "1.0.0", [], null, null, source.Source);
            var result = Assert.IsType<PackageQueryContentResult.Available>(
                await provider.GetContentAsync(package, CancellationToken.None));
            root = Assert.IsType<string>(result.Content.RootPath);
            Assert.True(Directory.Exists(root));
        }
        Assert.False(Directory.Exists(root));
        Assert.True(fixture.Payload!.Disposed);
    }

    [Fact]
    public async Task CancellationDoesNotBecomeAnEmptySuccess()
    {
        using var source = Source(out _);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PackageQueryCommand.ExecuteAsync(Options("depends=Dependency.One"),
                source, null, cancellation.Token));
    }

    private static PackageQueryOptions Options(
        string expression,
        int? maximumCandidates = null,
        RowSelectionIntent<string>? rowSelection = null) =>
        Options([expression], maximumCandidates, rowSelection);

    private static PackageQueryOptions Options(
        string firstExpression,
        string secondExpression,
        int? maximumCandidates = null,
        RowSelectionIntent<string>? rowSelection = null) =>
        Options(
            [firstExpression, secondExpression],
            maximumCandidates,
            rowSelection);

    private static PackageQueryOptions Options(
        IReadOnlyCollection<string> expressions,
        int? maximumCandidates,
        RowSelectionIntent<string>? rowSelection = null)
        => OptionsForInput(
            "Contoso.*",
            expressions,
            maximumCandidates,
            rowSelection);

    private static PackageQueryOptions OptionsForInput(
        string input,
        IReadOnlyCollection<string> expressions,
        int? maximumCandidates = null,
        RowSelectionIntent<string>? rowSelection = null)
    {
        PortableQueryTerm[] terms =
        [
            .. expressions.Select(expression =>
            {
                const string StartsWith = " starts-with ";
                int startsWith = expression.IndexOf(
                    StartsWith,
                    StringComparison.Ordinal);
                if (startsWith > 0)
                {
                    return new PortableQueryTerm(
                        expression[..startsWith],
                        PortableQueryOperator.StartsWith,
                        expression[(startsWith + StartsWith.Length)..]);
                }

                string[] parts = expression.Split('=', 2);
                return new PortableQueryTerm(
                    parts[0],
                    PortableQueryOperator.Equal,
                    parts[1]);
            }),
        ];
        bool requiresContent = PackageQuery.Terms.Any(descriptor =>
            terms.Any(term => term.Key == descriptor.Key)
            && descriptor.Tier == PackageQueryAcquisitionTier.PackageContent);
        int candidateLimit = maximumCandidates
            ?? (requiresContent
                ? PackageQuery.MaximumPackageContentCandidates
                : PackageQuery.DefaultMaximumCandidates);
        PackageQueryPlanResult result = PackageQuery.PlanInput(
            input,
            terms,
            candidateLimit,
            maximumMatches: null,
            rowSelection: rowSelection);
        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(result);
        return new()
        {
            Plan = accepted.Plan,
            Tabular = true,
            Tsv = true,
        };
    }

    private static PackageQueryOptions WithFormat(
        PackageQueryOptions options,
        string format) =>
        options with
        {
            JsonOutput = format == "json",
            Tabular = format is "table" or "tsv" or "jsonl",
            Tsv = format == "tsv",
            Jsonl = format == "jsonl",
        };

    private static RowSelectionIntent<string> Head(int count) =>
        RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Head(count)]);

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    private static string[] InspectionExpressions(int count) =>
    [
        .. Enumerable.Range(0, count).Select(index =>
            $"depends=Contoso.Dependency.{index:D2}"),
    ];

    private static IPackageSourceClient Source(out FakeSource fixture)
    {
        FakeSource? created = null;
        var source = PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetGallery, PackageSourceAssociation.Create(),
            factory => created = new FakeSource(factory));
        fixture = created!;
        return source;
    }

    private static PackageQueryCommand.ContentProvider ContentProvider(
        FakeSource fixture, NuGetOperationContext operation) =>
        new(new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(10), new UnavailableCredentials(),
            (_, _) => new PayloadHandler(fixture)), operation);

    private sealed class UnavailableCredentials : ICredentialSource
    {
        public bool HasCredentialSources => false;
        public Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri uri, bool isRetry, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The public Gallery fixture does not require credentials.");
    }

    private sealed class PayloadHandler(FakeSource fixture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("globalcdn.nuget.org", request.RequestUri!.Host);
            Assert.Equal("/packages/contoso.first.1.0.0.nupkg", request.RequestUri.AbsolutePath);
            var result = await fixture.GetPackageAsync("Contoso.First", "1.0.0", cancellationToken);
            return new(System.Net.HttpStatusCode.OK) { Content = new StreamContent(result.Value!.Content) };
        }
    }

    private sealed class FakeSource(PackageSourceResultFactory results) : IPackageSourceClient
    {
        public int ManifestRequests { get; private set; }
        public int PackageRequests { get; private set; }
        public string? MissingManifest { get; set; }
        public bool SearchFails { get; set; }
        public bool InvalidArchive { get; set; }
        public TrackedStream? Payload { get; private set; }
        public PackageSourceResultIdentity Source => results.Source;
        public PackageSourceCapabilities Capabilities => PackageSourceCapabilities.Search
            | PackageSourceCapabilities.Manifest
            | PackageSourceCapabilities.PackagePayload
            | PackageSourceCapabilities.VersionEnumeration;

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
            string prefix, int take = 100, bool prerelease = false,
            CancellationToken cancellationToken = default, NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchResult[] rows =
            [
                .. new SearchResult[]
                {
                    new("Contoso.First", "1.0.0"),
                    new("Contoso.Second", "1.0.0"),
                    new("Contoso.Third", "1.0.0"),
                }.Where(row => row.Id.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase)),
            ];
            return Task.FromResult(SearchFails ? results.FailedSearch(PackageSourceFailureKind.Transport)
                : results.SucceededSearch(results.Search([.. rows.Take(take)],
                    take < rows.Length ? PackageSearchTruncationReason.RequestedLimit : PackageSearchTruncationReason.None)));
        }

        public Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
            string packageId, string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManifestRequests++;
            var coordinate = PackageSourceCoordinate.Create(packageId, version);
            return Task.FromResult(coordinate.PackageId == MissingManifest
                ? results.FailedManifest(coordinate, PackageSourceFailureKind.NotFound)
                : results.SucceededManifest(coordinate, results.Manifest(coordinate, Manifest(packageId))));
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
            string packageId, string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageRequests++;
            using var bytes = new MemoryStream();
            if (!InvalidArchive)
            {
                using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using (var manifest = zip.CreateEntry($"{packageId}.nuspec").Open())
                        manifest.Write(Manifest(packageId));
                    using (var skill = new StreamWriter(
                        zip.CreateEntry("skills/example/SKILL.md").Open()))
                    {
                        skill.Write("A package skill.");
                    }
                    using Stream assemblyEntry = zip.CreateEntry(
                        "lib/net11.0/DotnetInspect.Cli.Tests.dll").Open();
                    using Stream assembly = File.OpenRead(
                        typeof(PackageQueryCliTests).Assembly.Location);
                    assembly.CopyTo(assemblyEntry);
                }
            }
            Payload = new TrackedStream(bytes.ToArray());
            var coordinate = PackageSourceCoordinate.Create(packageId, version);
            return Task.FromResult(results.SucceededPackage(coordinate,
                results.Payload(coordinate, PackageSourcePayloadKind.Package, Payload, Payload.Length)));
        }

        private static byte[] Manifest(string id)
        {
            string dependencies = id.Equals(
                    "Contoso.First",
                    StringComparison.OrdinalIgnoreCase)
                ? ""
                : id.Equals(
                    "Contoso.Third",
                    StringComparison.OrdinalIgnoreCase)
                    ? "<dependency id=\"Dependency.One\" version=\"1.0.0\"/>"
                    : "<dependency id=\"Dependency.One\" version=\"1.0.0\"/><dependency id=\"Dependency.Two\" version=\"2.0.0\"/>";
            string license = id.Equals(
                    "Contoso.First",
                    StringComparison.OrdinalIgnoreCase)
                ? "<license type=\"expression\">MIT</license>"
                : id.Equals(
                    "Contoso.Second",
                    StringComparison.OrdinalIgnoreCase)
                    ? "<license type=\"file\">OSMFEULA.txt</license>"
                    : "";
            return Encoding.UTF8.GetBytes($"""
                <package><metadata><id>{id}</id><version>1.0.0</version><authors>Contoso</authors>
                <description>CLI query fixture</description>{license}<dependencies>{dependencies}</dependencies>
                </metadata></package>
                """);
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(string query, int take = 20,
            bool prerelease = false, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) => throw new NotSupportedException();
        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, "1.0.0");
            PackageCandidateObservation candidate = results.Candidate(
                coordinate,
                PackageDiscoveryContract.CompleteVersionEnumeration,
                PackageListingState.Listed);
            return Task.FromResult(results.SucceededVersions(
                results.Versions(
                    [candidate],
                    hasAuthoritativeListingState: true)));
        }
        public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(string packageId,
            string version, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
