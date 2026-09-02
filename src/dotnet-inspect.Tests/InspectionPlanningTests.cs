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
    public void PackageAllLibraries_DoesNotDeclareFieldOrColumnProjection()
    {
        StructuralViewDescriptor view =
            StructuralViewRegistry.Get(
                StructuralViewIdentity.PackageAllLibraries);

        Assert.False(
            view.ParserCapabilities.HasFlag(
                StructuralParserCapabilities.Fields));
        Assert.False(
            view.ParserCapabilities.HasFlag(
                StructuralParserCapabilities.Columns));
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

    [Theory]
    [InlineData("package-type")]
    [InlineData("member-option")]
    [InlineData("source-identity")]
    [InlineData("package-library")]
    public async Task CommandlessStructuralSchema_MatchesNormalSyntaxPrecedence(
        string scenario)
    {
        string[] commandless = scenario switch
        {
            "package-type" =>
                ["Missing.Package", "Missing.Type"],
            "member-option" =>
                ["Missing.Type", "-m", "Run"],
            "source-identity" =>
                [
                    "Missing.Package",
                    "--package",
                    "Missing.Package",
                    "Missing.Type",
                ],
            "package-library" =>
                [
                    "Missing.Package",
                    "--library",
                    "ref/net8.0/Missing.dll",
                ],
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                "Unknown precedence scenario."),
        };
        string[] explicitCommand = scenario switch
        {
            "package-type" =>
                ["type", "Missing.Type", "--package", "Missing.Package"],
            "member-option" =>
                ["member", "Missing.Type", "-m", "Run"],
            "source-identity" =>
                ["type", "Missing.Type", "--package", "Missing.Package"],
            "package-library" =>
                [
                    "package",
                    "Missing.Package",
                    "--library",
                    "ref/net8.0/Missing.dll",
                ],
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                "Unknown precedence scenario."),
        };
        string[] projection =
            ["-D", "--schema", "--count", "--tips", "q"];

        var routed = await RunAppAsync(
            [.. commandless, .. projection]);
        var direct = await RunAppAsync(
            [.. explicitCommand, .. projection]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
    }

    [Fact]
    public async Task StaticTypeSchema_PositionalNupkgTypeFilterUsesListingCatalog()
    {
        var result = await RunAppAsync(
            "type",
            "missing-for-schema.nupkg",
            "-t",
            "*",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(SectionNames.ApiInfo, result.Output);
        Assert.DoesNotContain(SectionNames.TypeInfo, result.Output);
        Assert.DoesNotContain(
            "not found",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StaticMemberSchema_PositionalNupkgMatchesExplicitSource()
    {
        string[] projection =
            ["-D", "--schema", "--count", "--tips", "q"];
        var positional = await RunAppAsync(
            [
                "member",
                "missing-for-schema.nupkg",
                "Missing.Type",
                "Run",
                .. projection,
            ]);
        var explicitSource = await RunAppAsync(
            [
                "member",
                "Missing.Type",
                "Run",
                "--package",
                "missing-for-schema.nupkg",
                .. projection,
            ]);

        Assert.Equal(explicitSource, positional);
        Assert.Equal(0, positional.Exit);
        Assert.DoesNotContain(
            "not found",
            positional.Error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandlessTypeFilterWithSource_RecordsListingCatalog()
    {
        bool classified =
            StructuralViewRegistry.TryClassifyCommandless(
                [
                    "Missing.Type",
                    "--package",
                    "Missing.Package",
                    "-t",
                    "*",
                ],
                structuralDiscovery: true,
                out CommandlessStructuralRoute? route);

        Assert.True(classified);
        Assert.NotNull(route);
        Assert.Equal(
            InspectionCatalogIdentity.ApiType,
            route.Route.Catalog);
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

    [Theory]
    [InlineData("System.String..ctor")]
    [InlineData("System.Type..cctor")]
    [InlineData("System.Type.operator:op_Equality")]
    [InlineData("System.Decimal.operator<")]
    [InlineData("System.Decimal.operator>")]
    [InlineData("System.Decimal.operator+")]
    [InlineData("System.Decimal.op_Addition")]
    [InlineData("System.Type.explicit:System.IConvertible.ToType")]
    [InlineData("System.Type.extension:AsType")]
    public async Task CommandlessSpecialMemberSchema_IsMemberDetail(
        string target)
    {
        var result = await RunAppAsync(
            target,
            "-D",
            "Signature",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(SectionNames.Signature, result.Output);
        Assert.DoesNotContain(
            "[type/",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData(
        "Microsoft.VisualBasic.CompilerServices.Operators.AddObject",
        "Microsoft.VisualBasic.CompilerServices.Operators",
        "AddObject")]
    [InlineData(
        "Newtonsoft.Json.Linq.Op_Helpers.JValue",
        "Newtonsoft.Json.Linq.Op_Helpers",
        "JValue")]
    public void OperatorLikeIdentifiers_UseTheOrdinaryMemberBoundary(
        string target,
        string expectedType,
        string expectedMember)
    {
        var (typeName, memberName) =
            SharedParsers.SplitTrailingMember(target);

        Assert.Equal(expectedType, typeName);
        Assert.Equal(expectedMember, memberName);
        Assert.False(
            StructuralViewRegistry
                .HasUnambiguousMemberTail(target));
    }

    [Fact]
    public async Task OperatorLikeTypeName_RemainsStructurallyAmbiguous()
    {
        var result = await RunAppAsync(
            "Microsoft.CodeAnalysis.CSharp.Syntax.OperatorDeclarationSyntax",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[type/type/ApiMember]",
            result.Output);
        Assert.Contains(
            "[member/member-target/ApiMemberOverload]",
            result.Output);
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
    [InlineData("commandless-member-kind", "Unknown C# body kind 'loop'.")]
    [InlineData("commandless-order", "Field 'bogus' is not sortable")]
    [InlineData("type-version", "Use 'Newtonsoft.Json@13.0.3' to specify a version.")]
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
            "commandless-member-kind" =>
                ["Missing.Type.Run", "--where", "Kind=loop", "-D", "--schema"],
            "commandless-order" =>
                ["Missing.Type.Run", "--order-by", "bogus", "-D", "--schema"],
            "type-version" =>
                ["type", "Newtonsoft.Json", "13.0.3", "-D", "--schema"],
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

    [Fact]
    public async Task CommandlessStaticSchema_PreservesAlternativeMetadata()
    {
        var result = await RunAppAsync(
            "Missing.Type.Run",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[member/member-target/ApiMemberOverload] @All",
            result.Output);
        Assert.Contains(
            "section (verbose)",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task CommandlessStaticSchema_BareSelectNarrowsEveryAlternative()
    {
        var complete = await RunAppAsync(
            "Missing.Type.Run",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");
        var selected = await RunAppAsync(
            "Missing.Type.Run",
            "-D",
            "--schema",
            "-S",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, complete.Exit);
        Assert.Equal(0, selected.Exit);
        Assert.True(
            selected.Output.Split('\n').Length
            < complete.Output.Split('\n').Length);
        Assert.DoesNotContain(
            "section (verbose)",
            selected.Output);
        Assert.Contains(
            "[type/type/ApiMember]",
            selected.Output);
        Assert.Empty(complete.Error);
        Assert.Empty(selected.Error);
    }

    [Fact]
    public async Task StaticSchema_BareSelectUsesRouteSpecificApiMemberDefaults()
    {
        string[] common =
        [
            "Missing.Generic<T>",
            "--package",
            "Missing.Package",
            "-D",
            "--schema",
            "-S",
            "--table",
            "--tips",
            "q",
        ];

        var type = await RunAppAsync(["type", .. common]);
        var member = await RunAppAsync(["member", .. common]);
        var commandless = await RunAppAsync(
            [
                "Missing.Generic<T>",
                "-D",
                "--schema",
                "-S",
                "--table",
                "--tips",
                "q",
            ]);

        Assert.Equal(0, type.Exit);
        Assert.Equal(0, member.Exit);
        Assert.Equal(type, commandless);
        Assert.Contains(SectionNames.TypeInfo, type.Output);
        Assert.DoesNotContain(SectionNames.MethodGroups, type.Output);
        Assert.Contains(SectionNames.MethodGroups, member.Output);
        Assert.DoesNotContain(SectionNames.TypeInfo, member.Output);
        Assert.Empty(type.Error);
        Assert.Empty(member.Error);
    }

    [Fact]
    public async Task StaticMemberBodyShapes_RequiresExactMemberCatalog()
    {
        var result = await RunAppAsync(
            "member",
            "Missing.Type",
            "-m",
            "Run",
            "--where",
            "Kind=InvocationExpression",
            "-D",
            SectionNames.BodyShapes,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains("Start Line", result.Output);
        Assert.DoesNotContain(
            "[member/",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task EffectiveBodyShapes_AutoSelectedOverloadUsesDetailCatalog()
    {
        string fixture =
            typeof(Fixtures.BodyShapeFixture)
                .Assembly.Location;
        string[] common =
        [
            "member",
            typeof(Fixtures.BodyShapeFixture).FullName!,
            "-m",
            nameof(Fixtures.BodyShapeFixture.PublicCreation),
            "--library",
            fixture,
            "--where",
            "Kind=ObjectCreationExpression",
            "-D",
            "--table",
            "--tips",
            "q",
        ];

        var implicitOverload =
            await RunAppAsync(common);
        var explicitOverload =
            await RunAppAsync(
                [
                    .. common[..4],
                    "--index",
                    "1",
                    .. common[4..],
                ]);

        Assert.Equal(explicitOverload, implicitOverload);
        Assert.Equal(0, implicitOverload.Exit);
        Assert.Contains(
            SectionNames.BodyShapes,
            implicitOverload.Output);
        Assert.DoesNotContain(
            SectionNames.Methods,
            implicitOverload.Output);
    }

    [Fact]
    public async Task EffectiveDiscovery_RejectsUniversalMemberMissBeforeAcquisition()
    {
        string missing =
            Path.Combine(
                Path.GetTempPath(),
                $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var result = await RunAppAsync(
            "member",
            "Missing.Type.Member",
            "--library",
            missing,
            "-D",
            "DefinitelyNotASection",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Select value 'DefinitelyNotASection' not found.",
            result.Error);
        Assert.DoesNotContain(
            "File not found",
            result.Error);
    }

    [Theory]
    [InlineData("--fields")]
    [InlineData("--columns")]
    public async Task PackageAllLibraries_StaticSchemaRejectsUnsupportedProjection(
        string projection)
    {
        string target =
            $"Missing.Package.{Guid.NewGuid():N}";

        var result = await RunAppAsync(
            "package",
            target,
            "--all-libraries",
            "-D",
            "Library Info",
            "--schema",
            projection,
            "NoSuchValue",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"--all-libraries cannot be combined with {projection}",
            result.Error);
        Assert.DoesNotContain(
            target,
            result.Error);
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
