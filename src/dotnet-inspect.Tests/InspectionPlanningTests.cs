using System.Collections.Immutable;
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Planning;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class InspectionPlanningTests
{
    [Fact]
    public void StructuralRegistry_DeclaresTheClosedRouteSet()
    {
        StructuralViewDescriptor[] views =
            [.. StructuralViewRegistry.All];

        Assert.Equal(
            Enum.GetValues<StructuralViewIdentity>(),
            views.Select(view => view.Identity));
        Assert.Equal(
            views.Length,
            views.Select(view => view.Precedence).Distinct().Count());
        Assert.Equal(
            [
                (
                    StructuralViewIdentity.Package,
                    PackageCommand.Name,
                    "package",
                    ImmutableArray.Create(
                        InspectionCatalogIdentity.Package)),
                (
                    StructuralViewIdentity.PackageSingleLibrary,
                    PackageCommand.Name,
                    "single-library",
                    ImmutableArray.Create(
                        InspectionCatalogIdentity.Library)),
                (
                    StructuralViewIdentity.PackageAllLibraries,
                    PackageCommand.Name,
                    "all-libraries",
                    ImmutableArray.Create(
                        InspectionCatalogIdentity.LibraryAggregate)),
                (
                    StructuralViewIdentity.DirectLibrary,
                    "library",
                    "library",
                    ImmutableArray.Create(
                        InspectionCatalogIdentity.Library)),
                (
                    StructuralViewIdentity.Type,
                    TypeCommand.Name,
                    "type",
                    ImmutableArray.Create(
                        InspectionCatalogIdentity.ApiType,
                        InspectionCatalogIdentity.ApiMember)),
                (
                    StructuralViewIdentity.MemberType,
                    MemberCommand.Name,
                    "type-view",
                    ImmutableArray.Create(
                        InspectionCatalogIdentity.ApiMember)),
                (
                    StructuralViewIdentity.MemberTarget,
                    MemberCommand.Name,
                    "member-target",
                    ImmutableArray.Create(
                        InspectionCatalogIdentity.ApiMemberOverload,
                        InspectionCatalogIdentity.ApiMemberDetail)),
            ],
            views.Select(view =>
                (
                    view.Identity,
                    view.DestinationCommand,
                    view.ViewMode,
                    view.Catalogs)));
    }

    [Fact]
    public void PackageLibrarySchema_IsDerivedFromAvailableRouteInputs()
    {
        StructuralSchemaProjection packageLibrary =
            StructuralViewRegistry.Project(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.PackageSingleLibrary,
                    InspectionCatalogIdentity.Library));
        StructuralSchemaProjection directLibrary =
            StructuralViewRegistry.Project(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.DirectLibrary,
                    InspectionCatalogIdentity.Library));

        Assert.Contains(
            SectionNames.PerformanceBoxing,
            packageLibrary.Schema.SectionNames);
        Assert.Contains(
            SectionNames.ILOffset,
            directLibrary.Schema.SectionNames);
        Assert.Contains(
            SectionNames.BodyShapes,
            directLibrary.Schema.SectionNames);
        Assert.DoesNotContain(
            SectionNames.ILOffset,
            packageLibrary.Schema.SectionNames);
        Assert.DoesNotContain(
            MetadataSectionNames.Heap,
            packageLibrary.Schema.SectionNames);
        Assert.DoesNotContain(
            SectionNames.BodyShapes,
            packageLibrary.Schema.SectionNames);
        Assert.All(
            packageLibrary.SectionInputs,
            pair => Assert.Equal(
                StructuralSectionInput.None,
                pair.Value));
    }

    [Fact]
    public void PackageAllLibrariesRowSchema_MatchesRendererDeclarations()
    {
        StructuralSchemaProjection projection =
            StructuralViewRegistry.Project(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.PackageAllLibraries,
                    InspectionCatalogIdentity.LibraryAggregate),
                StructuralOutputShape.Rows);

        Assert.Equal(
            PackageCommand.AllLibrariesRowSchemas.Select(
                row => row.Section),
            projection.Schema.SectionNames);
        foreach (PackageCommand.AllLibrariesRowSchema rowSchema in
                 PackageCommand.AllLibrariesRowSchemas)
        {
            Assert.Equal(
                ["Package", "Version", "Library", "TFM"],
                rowSchema.Headers[..4]);
            Assert.Equal(
                rowSchema.Headers
                    .Concat(rowSchema.AlternateHeaders ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase),
                projection.Schema
                    .GetSection(rowSchema.Section)!
                    .Items
                    .Select(item => item.Name));
        }
    }

    [Theory]
    [InlineData("Signature")]
    [InlineData("Original Source")]
    [InlineData("*Source*")]
    [InlineData("@Source")]
    public void SectionDemandIndex_PromotesExactMemberSelectors(
        string selector)
    {
        SectionDemandClassification result =
            ApiSectionDemandIndex.Classify(
                InspectionSurface.Member,
                [selector],
                selectDefault: false,
                InspectionTargetRequirement.MemberSet);

        Assert.Equal(
            InspectionTargetRequirement.ExactMember,
            result.RequiredTarget);
        Assert.Empty(result.UnresolvedSelectors);
    }

    [Fact]
    public void SectionDemandIndex_AllSelectorDoesNotPromoteTarget()
    {
        SectionDemandClassification result =
            ApiSectionDemandIndex.Classify(
                InspectionSurface.Member,
                [SelectResolver.AllSelector],
                selectDefault: false,
                InspectionTargetRequirement.MemberSet);

        Assert.Equal(
            InspectionTargetRequirement.MemberSet,
            result.RequiredTarget);
        Assert.Empty(result.MatchedSections);
    }

    [Fact]
    public void SectionDemandIndex_RejectsConflictingDeclarations()
    {
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() =>
                ApiSectionDemandIndex.CreateRequirementsForTest(
                    ("Signature",
                        InspectionTargetRequirement.MemberSet),
                    ("signature",
                        InspectionTargetRequirement.ExactMember)));

        Assert.Contains(
            "declares both",
            error.Message);
    }

    [Fact]
    public void ResolvedPlan_UsesEffectiveDiscoverySelectors()
    {
        var options = new MemberOptions
        {
            TypeName = "Example.Type",
            MemberFilter = ["Run"],
            Discover = ["Original Source"],
        };

        ResolvedMemberInspectionPlan plan =
            ResolvedMemberInspectionPlan.FromCompatibilityOptions(
                options,
                selectCatalogFromDemand: true);

        Assert.Equal(
            InspectionCatalogIdentity.ApiMemberDetail,
            plan.Selection.Catalog);
        Assert.Contains(
            SectionNames.PdbSource,
            plan.Selection.ResolvedSections);
    }

    [Theory]
    [InlineData("--library")]
    [InlineData("--all-libraries")]
    public async Task ExplicitPackageStructuralSchema_DoesNotAcquireTarget(
        string viewOption)
    {
        string missing =
            $"Missing.Package.{Guid.NewGuid():N}";
        string[] args = viewOption == "--library"
            ? ["package", missing, viewOption, "", "-D", "--schema"]
            : ["package", missing, viewOption, "-D", "--schema"];

        var result = await RunAppAsync(args);

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            SectionNames.LibraryInfo,
            result.Output);
        Assert.DoesNotContain(
            "not found",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandlessStructuralSchema_UsesLabeledAlternativesWithoutResolution()
    {
        string target =
            $"Missing.Type.{Guid.NewGuid():N}.Run";

        var result = await RunAppAsync(
            target,
            "--library",
            "missing.dll",
            "-D",
            "Signature",
            "--schema");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[package/single-library/Library]",
            result.Output);
        Assert.Contains(
            "[member/member-target/ApiMemberDetail] Signature",
            result.Output);
        Assert.DoesNotContain(
            "not found",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "resolution",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StructuralAlternatives_CannotSatisfyFinalShapeValidation()
    {
        var result = await RunAppAsync(
            "Example.Type.Run",
            "-D",
            "Signature",
            "--schema",
            "--value");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--value is not available with -D/--discover",
            result.Error);
    }

    [Fact]
    public void CommandlessNupkgLibraryGesture_HasOneDeterministicRoute()
    {
        bool classified =
            StructuralViewRegistry.TryClassifyCommandless(
                ["missing.nupkg", "--library", "lib/a.dll"],
                structuralDiscovery: true,
                out CommandlessStructuralRoute? route);

        Assert.True(classified);
        Assert.NotNull(route);
        Assert.Equal(
            StructuralViewIdentity.PackageSingleLibrary,
            route.Route.View.Identity);
        Assert.Equal(
            InspectionCatalogIdentity.Library,
            route.Route.Catalog);
        Assert.Equal(
            PackageCommand.Name,
            route.RewrittenTokens[0]);
    }

    [Fact]
    public void CommandlessGenericBodyQuery_SelectsTheTypeView()
    {
        bool classified =
            StructuralViewRegistry.TryClassifyCommandless(
                [
                    "System.Collections.Generic.List<string>",
                    "--where",
                    "Kind=ObjectCreationExpression",
                ],
                structuralDiscovery: true,
                out CommandlessStructuralRoute? route);

        Assert.True(classified);
        Assert.NotNull(route);
        Assert.Equal(
            StructuralViewIdentity.Type,
            route.Route.View.Identity);
        Assert.Equal(
            InspectionCatalogIdentity.ApiMember,
            route.Route.Catalog);
    }

    [Fact]
    public void CommandlessExplicitIndex_SelectsMemberDetail()
    {
        bool classified =
            StructuralViewRegistry.TryClassifyCommandless(
                [
                    "System.String",
                    "-m",
                    "Contains",
                    "--index",
                    "1",
                ],
                structuralDiscovery: true,
                out CommandlessStructuralRoute? route);

        Assert.True(classified);
        Assert.NotNull(route);
        Assert.Equal(
            StructuralViewIdentity.MemberTarget,
            route.Route.View.Identity);
        Assert.Equal(
            InspectionCatalogIdentity.ApiMemberDetail,
            route.Route.Catalog);
    }

    [Fact]
    public async Task ExplicitGenericMemberSchema_IsOneTypeView()
    {
        var result = await RunAppAsync(
            "member",
            "System.Collections.Generic.List<string>",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.DoesNotContain(
            "[member/",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            SectionNames.Methods,
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandlessGenericBodySchema_IsOneTypeView()
    {
        var result = await RunAppAsync(
            "Missing.Namespace.Generic<Type>",
            "--where",
            "Kind=ObjectCreationExpression",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(SectionNames.TypeInfo, result.Output);
        Assert.Contains(SectionNames.BodyShapes, result.Output);
        Assert.DoesNotContain(
            "[member/",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task CommandlessIndexSchema_IsMemberDetail()
    {
        var result = await RunAppAsync(
            "Missing.Type",
            "--platform",
            "Missing.Platform.For.Schema",
            "-m",
            "Run",
            "--index",
            "1",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(SectionNames.Signature, result.Output);
        Assert.DoesNotContain(SectionNames.TypeInfo, result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task MultiArgumentDottedMemberSchema_MatchesExplicitMember()
    {
        string[] common =
        [
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var dotted = await RunAppAsync(
            [
                "member",
                "System.Text.Json",
                "JsonSerializer.Serialize",
                .. common,
            ]);
        var explicitMember = await RunAppAsync(
            [
                "member",
                "System.Text.Json",
                "JsonSerializer",
                "-m",
                "Serialize",
                .. common,
            ]);

        Assert.Equal(explicitMember, dotted);
        Assert.Equal(0, dotted.Exit);
        Assert.DoesNotContain(
            "[member/",
            dotted.Output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("member-shape", "--shape is only valid for type targets.")]
    [InlineData("member-arity", "cannot combine different generic arities")]
    [InlineData("member-kind", "Unknown C# body kind 'loop'.")]
    [InlineData("member-mermaid", "--mermaid is standalone")]
    [InlineData("type-order", "Field 'bogus' is not sortable")]
    [InlineData("package-multi", "Multiple package inspection cannot be combined")]
    public async Task StaticSchema_PreservesTargetIndependentValidation(
        string scenario,
        string expectedError)
    {
        string[] args = scenario switch
        {
            "member-shape" =>
                ["member", "System.String", "--shape", "record", "-D", "--schema"],
            "member-arity" =>
                ["member", "System.String", "-m", "Foo`1", "-m", "Bar`2", "-D", "--schema"],
            "member-kind" =>
                ["member", "System.String", "-m", "Substring", "--where", "Kind=loop", "-D", "--schema"],
            "member-mermaid" =>
                ["member", "System.String", "-m", "Substring", "--mermaid", "--json", "-D", "--schema"],
            "type-order" =>
                ["type", "System.String", "--order-by", "bogus", "-D", "--schema"],
            "package-multi" =>
                ["package", "Newtonsoft.Json", "Serilog", "-D", "--schema"],
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                "Unknown validation scenario."),
        };

        var result = await RunAppAsync(args);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            expectedError,
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("package", "Summary")]
    [InlineData("library", "DefinitelyNotASection")]
    public async Task StaticSchema_RejectsNonSelectableRouteSections(
        string command,
        string selector)
    {
        string target = command == "package"
            ? "Missing.Package.For.Schema"
            : "missing-library.dll";

        var result = await RunAppAsync(
            command,
            target,
            "-D",
            "--schema",
            "-S",
            selector,
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"Select value '{selector}' not found.",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            target,
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiStaticSchema_PreservesCategoryDoorsAndCostAnnotations()
    {
        var result = await RunAppAsync(
            "type",
            "System.String",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains("@All", result.Output);
        Assert.Contains("@Audit", result.Output);
        Assert.Contains("(verbose)", result.Output);
        Assert.Empty(result.Error);
    }

    private static Task<(int Exit, string Output, string Error)>
        RunAppAsync(params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            args = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(
                root.Parse(args),
                args);
        });
}
