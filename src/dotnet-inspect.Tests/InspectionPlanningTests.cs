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
    public void SectionDemandIndex_AllWithExactCompanionPromotesExactMember()
    {
        SectionDemandClassification result =
            ApiSectionDemandIndex.Classify(
                InspectionSurface.Member,
                [SelectResolver.AllSelector, SectionNames.Signature],
                selectDefault: false,
                InspectionTargetRequirement.MemberSet);

        Assert.Equal(
            InspectionTargetRequirement.ExactMember,
            result.RequiredTarget);
        Assert.Equal(
            [SectionNames.Signature],
            result.MatchedSections);
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

    [Theory]
    [InlineData("--all-libraries=false")]
    [InlineData("--all-libraries:false")]
    public async Task CommandlessDisabledAllLibraries_MatchesOmission(
        string disabledOption)
    {
        string[] projection =
        [
            "-D",
            SectionNames.TypeInfo,
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var disabled = await RunAppAsync(
            ["System.String", disabledOption, .. projection]);
        var omitted = await RunAppAsync(
            ["System.String", .. projection]);

        Assert.Equal(omitted, disabled);
        Assert.Equal(0, disabled.Exit);
    }

    [Fact]
    public async Task CommandlessLeadingSchema_PreservesPackageTypePrecedence()
    {
        string[] trailing =
        [
            "Missing.Package",
            "Missing.Type",
            "-D",
            "Package Info",
            "--table",
            "--tips",
            "q",
        ];
        var leadingSchema = await RunAppAsync(
            ["--schema", .. trailing]);
        var trailingSchema = await RunAppAsync(
            [.. trailing, "--schema"]);

        Assert.Equal(trailingSchema, leadingSchema);
        Assert.Equal(1, leadingSchema.Exit);
        Assert.Contains(
            "Section 'Package Info' not found.",
            leadingSchema.Error);
    }

    [Fact]
    public async Task StaticPackageLibrarySchema_RejectsProjectedOutCategory()
    {
        var result = await RunAppAsync(
            "package",
            "Missing.Package",
            "--library",
            "ref/net8.0/Missing.dll",
            "-S",
            SectionCategoryNames.Context,
            "-D",
            "--schema",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"Select value '{SectionCategoryNames.Context}' not found.",
            result.Error);
        Assert.DoesNotContain(
            "Package 'missing.package' not found.",
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
    public async Task CommandlessTypeFilter_MatchesExplicitTypeListingCatalog()
    {
        string[] projection =
        [
            "-t",
            "*",
            "-D",
            SectionNames.TypeInfo,
            "--schema",
            "--count",
            "--tips",
            "q",
        ];
        var commandless =
            await RunAppAsync(
                ["Missing.Type.Run", .. projection]);
        var explicitType =
            await RunAppAsync(
                ["type", "Missing.Type.Run", .. projection]);

        Assert.Equal(explicitType, commandless);
        Assert.Equal(1, commandless.Exit);
        Assert.Contains(
            $"Section '{SectionNames.TypeInfo}' not found.",
            commandless.Error);
    }

    [Fact]
    public async Task EffectiveCommandlessTypeFilter_UsesListingCatalog()
    {
        var result = await RunAppAsync(
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-t",
            "*String*",
            "-D",
            SectionNames.ApiInfo,
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains("Library", result.Output);
        Assert.Contains("Types", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task TypeFilterMatchingTarget_RetainsSingleTypeCatalog()
    {
        string[] common =
        [
            "type",
            "JsonSerializer",
            "--platform",
            "System.Text.Json",
            "-D",
            SectionNames.TypeInfo,
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var baseline = await RunAppAsync(common);
        var matchingFilter = await RunAppAsync(
            [
                .. common[..4],
                "-t",
                "JsonSerializer",
                .. common[4..],
            ]);

        Assert.Equal(baseline, matchingFilter);
        Assert.Equal(0, matchingFilter.Exit);
        Assert.Contains("Type", matchingFilter.Output);
    }

    [Fact]
    public async Task CommandlessNumericTypeLimit_RetainsTypeMemberAlternatives()
    {
        var result = await RunAppAsync(
            "Missing.Type.Run",
            "-t",
            "5",
            "-D",
            SectionNames.TypeInfo,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[type/type/ApiMember]",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "[type/type/ApiType]",
            result.Output,
            StringComparison.Ordinal);
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
    public async Task ExplicitGenericMemberSchema_ReturnsCompleteTypeAndPeeledMemberAlternatives()
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
        Assert.Contains(
            "[member/member-target/ApiMemberOverload]",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "[member/type-view/ApiMember]",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task CommandlessDottedIndexSchema_MatchesOrdinalShorthand()
    {
        string[] projection =
        [
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var explicitIndex = await RunAppAsync(
            [
                "Missing.Type.Run",
                "--index",
                "1",
                .. projection,
            ]);
        var shorthand = await RunAppAsync(
            [
                "Missing.Type.Run:1",
                .. projection,
            ]);

        Assert.Equal(shorthand, explicitIndex);
        Assert.Equal(0, explicitIndex.Exit);
        Assert.DoesNotContain(
            "[member/type-view/",
            explicitIndex.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DottedOrdinalWithDistinctExplicitMember_IsRejectedConsistently()
    {
        string[] common =
        [
            "member",
            "System.String.Contains:1",
            "--platform",
            "System.Private.CoreLib",
            "-m",
            "StartsWith",
            "-D",
            SectionNames.Signature,
            "--table",
            "--tips",
            "q",
        ];
        var structural = await RunAppAsync(
            [.. common, "--schema"]);
        var effective = await RunAppAsync(common);

        Assert.Equal(1, structural.Exit);
        Assert.Equal(1, effective.Exit);
        Assert.Contains(
            "exactly one member name.",
            structural.Error);
        Assert.Contains(
            "exactly one member name.",
            effective.Error);
    }

    [Fact]
    public async Task DottedOrdinalWithMatchingExplicitMember_RemainsValid()
    {
        var result = await RunAppAsync(
            "member",
            "System.String.Contains:1",
            "--platform",
            "System.Private.CoreLib",
            "-m",
            "Contains",
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains("Canonical Signature", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task PositionalLibraryConflictingDottedOrdinals_AreRejectedStatically()
    {
        string fixture =
            typeof(Fixtures.BodyShapeFixture).Assembly.Location;
        string target =
            $"{typeof(Fixtures.BodyShapeFixture).FullName}."
            + $"{nameof(Fixtures.BodyShapeFixture.Classify)}:1";

        var result = await RunAppAsync(
            "member",
            fixture,
            target,
            "-m",
            $"{nameof(Fixtures.BodyShapeFixture.Classify)}:2",
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "cannot combine different overload selectors",
            result.Error);
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
    [InlineData("System.String..CTOR")]
    [InlineData("System.Type..CCTOR")]
    [InlineData("System.Decimal.OP_Addition")]
    public async Task CommandlessSpecialMemberSchema_IsCaseInsensitive(
        string target)
    {
        var result = await RunAppAsync(
            target,
            "-D",
            SectionNames.Signature,
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
    [InlineData("member-multi-index", "--index/Name:N requires exactly one member name.")]
    [InlineData("member-multi-digest", "Name~digest requires exactly one member name.")]
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
            "member-multi-index" =>
                ["member", "Missing.Type", "-m", "Run", "-m", "Stop", "--index", "1", "-D", "--schema"],
            "member-multi-digest" =>
                ["member", "Missing.Type", "-m", "Run", "-m", "Stop~abcd", "-D", "--schema"],
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
            "--platform",
            "System.Private.CoreLib",
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
            "MissingGeneric<T>",
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
                "MissingGeneric<T>",
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
    public async Task EffectiveTypeFallback_ReresolvesSuccessfulRawSelection()
    {
        var result = await RunAppAsync(
            "type",
            "Command",
            "--library",
            typeof(InspectionPlanningTests).Assembly.Location,
            "-S",
            SelectResolver.AllSelector,
            "-D",
            SectionNames.Classes,
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains("Members", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task EffectiveDiscovery_AllSelectionRetainsExactDemand()
    {
        var result = await RunAppAsync(
            "member",
            "System.String",
            "-m",
            "Contains",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            SelectResolver.AllSelector,
            "-D",
            SectionNames.Signature,
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "require a single selected overload for member 'Contains'",
            result.Error);
    }

    [Fact]
    public async Task EffectiveMemberTypeView_RejectsExactSectionBeforeAcquisition()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var result = await RunAppAsync(
            "member",
            "MissingType",
            "--library",
            missing,
            "-D",
            SectionNames.Signature,
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Exact-member section selection requires exactly one member name.",
            result.Error);
        Assert.DoesNotContain("File not found", result.Error);
    }

    [Fact]
    public async Task EffectiveExactDiscovery_MultipleMembersRejectsBeforeAcquisition()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var result = await RunAppAsync(
            "member",
            "MissingType",
            "-m",
            "Run",
            "-m",
            "Stop",
            "--library",
            missing,
            "-D",
            SectionNames.Signature,
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Exact-member section selection requires exactly one member name.",
            result.Error);
        Assert.DoesNotContain("File not found", result.Error);
    }

    [Theory]
    [InlineData(
        "System.Private.CoreLib",
        "--platform",
        "System.String.Contains")]
    [InlineData(
        "Missing.Package",
        "--package",
        "Missing.Type.Run")]
    public async Task ExplicitSourceIdentitySchema_RetainsDottedAlternatives(
        string source,
        string sourceOption,
        string target)
    {
        var result = await RunAppAsync(
            source,
            sourceOption,
            source,
            target,
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[member/member-target/ApiMemberDetail]",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "[member/type-view/ApiMember]",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
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

    [Fact]
    public async Task CommandlessStructuralMode_UsesParsedAttachedValues()
    {
        string[] common =
        [
            "Missing.Type.Run",
            "--schema",
            "--count",
            "--tips",
            "q",
        ];
        var equals = await RunAppAsync(
            [.. common, "-D=Signature"]);
        var colon = await RunAppAsync(
            [.. common, "-D:Signature"]);

        Assert.Equal(equals, colon);
        Assert.Equal(0, colon.Exit);

        string missing =
            Path.Combine(
                Path.GetTempPath(),
                $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        var disabled = await RunAppAsync(
            "Missing.Type.Run",
            "--library",
            missing,
            "-D",
            "Signature",
            "--schema=false",
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, disabled.Exit);
        Assert.Contains("File not found", disabled.Error);
    }

    [Fact]
    public async Task DirectLibraryStructuralSchema_DoesNotResolvePlatformTarget()
    {
        var result = await RunAppAsync(
            "library",
            "System.Text.Json",
            "-D",
            "--schema",
            "--count",
            "--verbose",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.DoesNotContain(
            "Resolved from installed packs",
            result.Output + result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("format", "--mermaid is standalone")]
    [InlineData("package-version", "--library cannot be combined with --versions")]
    [InlineData("package-dependencies", "--library cannot be combined with --dependencies")]
    [InlineData("package-layout", "--library cannot be combined with --layout")]
    [InlineData("all-libraries-layout", "--all-libraries cannot be combined with --layout")]
    public async Task StaticSchema_PreservesRouteValidationAddedByReplacement(
        string scenario,
        string expectedError)
    {
        string[] args = scenario switch
        {
            "format" =>
            [
                "Missing.Type.Run",
                "-D",
                "Signature",
                "--schema",
                "--mermaid",
                "--json",
            ],
            "package-version" =>
            [
                "package",
                "Missing.Package",
                "--library",
                "--versions",
                "-D",
                "--schema",
            ],
            "package-dependencies" =>
            [
                "package",
                "Missing.Package",
                "--library",
                "ref/net8.0/Missing.dll",
                "--dependencies",
                "-D",
                "--schema",
            ],
            "package-layout" =>
            [
                "package",
                "Missing.Package",
                "--library",
                "ref/net8.0/Missing.dll",
                "--layout",
                "-D",
                "--schema",
            ],
            "all-libraries-layout" =>
            [
                "package",
                "Missing.Package",
                "--all-libraries",
                "--layout",
                "-D",
                "--schema",
            ],
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                "Unknown validation scenario."),
        };

        var result = await RunAppAsync(args);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(expectedError, result.Error);
    }

    [Fact]
    public async Task CommandlessAlternatives_DoNotDiscardSelectDuringDiscovery()
    {
        var result = await RunAppAsync(
            "Missing.Type.Run",
            "-D",
            "Signature",
            "--schema",
            "-S",
            "DefinitelyNotASection",
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Select value 'DefinitelyNotASection' not found.",
            result.Error);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("commandless")]
    public async Task StaticSchema_SelectionDeterminesDemandBeforeDiscovery(
        string route)
    {
        string[] args = route == "member"
            ?
            [
                "member",
                "Missing.Type",
                "-m",
                "Run",
                "-S",
                SectionNames.Signature,
                "-D",
                SelectResolver.AllSelector,
                "--schema",
                "--table",
                "--tips",
                "q",
            ]
            :
            [
                "Missing.Type",
                "-m",
                "Run",
                "-S",
                SectionNames.Signature,
                "-D",
                SelectResolver.AllSelector,
                "--schema",
                "--table",
                "--tips",
                "q",
            ];

        var result = await RunAppAsync(args);

        Assert.Equal(0, result.Exit);
        Assert.Contains(SectionNames.Signature, result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task EffectiveDiscovery_RejectsSelectionDisjointSectionBeforeAcquisition()
    {
        string missing =
            Path.Combine(
                Path.GetTempPath(),
                $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var result = await RunAppAsync(
            "member",
            "Missing.Type",
            "-m",
            "Run",
            "--library",
            missing,
            "-S",
            SectionNames.Signature,
            "-D",
            SectionNames.Methods,
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Contains(
            $"Select value '{SectionNames.Methods}' not found.",
            result.Error);
        Assert.DoesNotContain("File not found", result.Error);
    }

    [Theory]
    [InlineData("library", "--layout=false")]
    [InlineData("library", "--layout:false")]
    [InlineData("all", "--layout=false")]
    [InlineData("all", "--layout:false")]
    public async Task StaticPackageLibrarySchema_ExplicitFalseLayoutIsDisabled(
        string route,
        string layout)
    {
        string[] scope =
            route == "library"
                ? ["--library", "ref/net8.0/Missing.dll"]
                : ["--all-libraries"];

        var result = await RunAppAsync(
            [
                "package",
                "Missing.Package",
                .. scope,
                layout,
                "-D",
                "--schema",
                "--count",
                "--tips",
                "q",
            ]);

        Assert.Equal(0, result.Exit);
        Assert.DoesNotContain(
            "cannot be combined with --layout",
            result.Error);
    }

    [Theory]
    [InlineData("-D")]
    [InlineData("-S")]
    public async Task Member_RejectsInferredUniversalMissBeforeAcquisition(
        string projection)
    {
        var result = await RunAppAsync(
            "member",
            "System.String",
            "-m",
            "Contains",
            projection,
            "DefinitelyNotASection",
            "--verbose",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Contains(
            "Select value 'DefinitelyNotASection' not found.",
            result.Error);
        Assert.DoesNotContain(
            "Resolved from installed packs",
            result.Output + result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Extracting API",
            result.Output + result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("cost")]
    [InlineData("semantics")]
    public async Task EffectiveDiscovery_ExactSectionAutoSelectsUniqueOverload(
        string scenario)
    {
        string fixture =
            (scenario == "signature"
                ? typeof(Fixtures.BodyShapeFixture)
                : typeof(CostOverlayFixture))
                .Assembly.Location;
        string typeName =
            scenario == "signature"
                ? typeof(Fixtures.BodyShapeFixture).FullName!
                : typeof(CostOverlayFixture).FullName!;
        string memberName = scenario switch
        {
            "signature" =>
                nameof(Fixtures.BodyShapeFixture.PublicCreation),
            "cost" =>
                nameof(CostOverlayFixture.Caller),
            "semantics" =>
                nameof(CostOverlayFixture.CallsExceptionOnly),
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                "Unknown exact discovery scenario."),
        };
        string section = scenario switch
        {
            "signature" => SectionNames.Signature,
            "cost" => SectionNames.CostOverlay,
            "semantics" => SectionNames.SemanticsOverlay,
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                "Unknown exact discovery scenario."),
        };
        string[] common =
        [
            "member",
            typeName,
            "-m",
            memberName,
            "--library",
            fixture,
            "-D",
            section,
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

        Assert.True(
            implicitOverload.Exit == 0,
            implicitOverload.Output + implicitOverload.Error);
        Assert.Equal(explicitOverload, implicitOverload);
        if (scenario == "signature")
            Assert.Contains(section, implicitOverload.Output);
    }

    [Theory]
    [InlineData(SectionNames.CostOverlay)]
    [InlineData(SectionNames.SemanticsOverlay)]
    public async Task EffectiveDiscovery_ExactOverlayRequiresOneOfMultipleOverloads(
        string section)
    {
        var result = await RunAppAsync(
            "member",
            "System.String",
            "-m",
            "Contains",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            section,
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Contains(
            $"section '{section}' requires a single selected overload",
            result.Error);
    }

    [Theory]
    [InlineData("select-discover")]
    [InlineData("positional-member")]
    public async Task Member_PreflightPreservesCompleteAcquisitionFreeIntent(
        string scenario)
    {
        string[] args = scenario switch
        {
            "select-discover" =>
            [
                "member",
                "System.String",
                "-m",
                "Contains",
                "--platform",
                "System.Private.CoreLib",
                "-S",
                SectionNames.TypeInfo,
                "-D",
                SectionNames.Signature,
                "--verbose",
                "--tips",
                "q",
            ],
            "positional-member" =>
            [
                "member",
                "System.Text.Json",
                "System.Text.Json.JsonSerializer",
                "Serialize",
                "-S",
                SectionNames.TypeInfo,
                "--verbose",
                "--tips",
                "q",
            ],
            _ => throw new ArgumentOutOfRangeException(
                nameof(scenario),
                scenario,
                "Unknown preflight scenario."),
        };

        var result = await RunAppAsync(args);

        Assert.Equal(1, result.Exit);
        Assert.Contains(
            $"Select value '{SectionNames.TypeInfo}' not found.",
            result.Error);
        Assert.DoesNotContain(
            "Resolved from installed packs",
            result.Output + result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Extracting API",
            result.Output + result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task StaticSchema_NumericFilterUsesNormalizedLimitIntent(
        string command)
    {
        string filter = command == "type" ? "-t" : "-m";
        string[] common =
        [
            command,
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            SectionNames.TypeInfo,
            "--schema",
            "--count",
            "--tips",
            "q",
        ];

        var baseline = await RunAppAsync(common);
        var limited = await RunAppAsync(
            [
                .. common[..4],
                filter,
                "5",
                .. common[4..],
            ]);

        Assert.Equal(baseline, limited);
        Assert.Equal(0, limited.Exit);
    }

    [Fact]
    public async Task StaticMemberSchema_IndexWithoutMemberIsDiagnostic()
    {
        var result = await RunAppAsync(
            "member",
            "String",
            "--platform",
            "System.Private.CoreLib",
            "--index",
            "1",
            "-D",
            "--schema",
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--index/Name:N requires exactly one member name.",
            result.Error);
    }

    [Fact]
    public async Task CommandlessTypeGlobStructuralSchemaMatchesExplicitType()
    {
        string[] projection =
        [
            "-D",
            SectionNames.ApiInfo,
            "--schema",
            "--count",
            "--tips",
            "q",
        ];
        var commandless =
            await RunAppAsync(
                ["System.*", .. projection]);
        var explicitType =
            await RunAppAsync(
                ["type", "System.*", .. projection]);

        Assert.Equal(explicitType, commandless);
        Assert.Equal(0, commandless.Exit);
    }

    [Fact]
    public async Task CommandlessExplicitLibraryPathHasNoPackageAlternative()
    {
        string missing =
            Path.Combine(
                Path.GetTempPath(),
                $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        var result = await RunAppAsync(
            "Missing.Type.Run",
            "--library",
            missing,
            "-D",
            "Signature",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.DoesNotContain(
            "[package/",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("[type/type/", result.Output);
        Assert.Contains("[member/member-target/", result.Output);
    }

    [Fact]
    public async Task CommandlessConstructorStructuralSchemaMatchesMemberSet()
    {
        string[] projection =
        [
            "-D",
            "--schema",
            "--count",
            "--tips",
            "q",
        ];
        var commandless =
            await RunAppAsync(
                ["System.String..ctor", .. projection]);
        var explicitMember =
            await RunAppAsync(
                [
                    "member",
                    "System.String",
                    "-m",
                    ".ctor",
                    .. projection,
                ]);

        Assert.Equal(explicitMember, commandless);
        Assert.Equal(0, commandless.Exit);
    }

    [Fact]
    public async Task CommandlessGenericMemberOptionMatchesExplicitMember()
    {
        string[] projection =
        [
            "-D",
            SectionNames.Signature,
            "--schema",
            "--count",
            "--tips",
            "q",
        ];
        var commandless =
            await RunAppAsync(
                [
                    "System.Collections.Generic.List<string>",
                    "-m",
                    "Add",
                    .. projection,
                ]);
        var explicitMember =
            await RunAppAsync(
                [
                    "member",
                    "System.Collections.Generic.List<string>",
                    "-m",
                    "Add",
                    .. projection,
                ]);

        Assert.Equal(explicitMember, commandless);
        Assert.Equal(0, commandless.Exit);
    }

    [Fact]
    public async Task CommandlessSourceIdentityExactMemberMatchesExplicitMember()
    {
        string[] projection =
        [
            "-D",
            SectionNames.Signature,
            "--schema",
            "--count",
            "--tips",
            "q",
        ];
        var commandless =
            await RunAppAsync(
                [
                    "System.Private.CoreLib",
                    "--platform",
                    "System.Private.CoreLib",
                    "System.String.Contains:1",
                    .. projection,
                ]);
        var explicitMember =
            await RunAppAsync(
                [
                    "member",
                    "System.String",
                    "-m",
                    "Contains:1",
                    "--platform",
                    "System.Private.CoreLib",
                    .. projection,
                ]);

        Assert.Equal(explicitMember, commandless);
        Assert.Equal(0, commandless.Exit);
    }

    [Fact]
    public async Task CommandlessSourceIdentityTypeFilterMatchesExplicitType()
    {
        string[] projection =
        [
            "-t",
            "*String*",
            "-D",
            SectionNames.Classes,
            "--table",
            "--tips",
            "q",
        ];
        var commandless = await RunAppAsync(
            [
                "System.Private.CoreLib",
                "--platform",
                "System.Private.CoreLib",
                "System.String",
                .. projection,
            ]);
        var explicitType = await RunAppAsync(
            [
                "type",
                "System.String",
                "--platform",
                "System.Private.CoreLib",
                .. projection,
            ]);

        Assert.Equal(explicitType, commandless);
        Assert.Equal(0, commandless.Exit);
    }

    [Fact]
    public async Task CommandlessSourceIdentityExactTypeFilterMatchesExplicitType()
    {
        string[] projection =
        [
            "-t",
            "System.String",
            "-S",
            SectionNames.TypeInfo,
            "--table",
            "--tips",
            "q",
        ];
        var commandless = await RunAppAsync(
            [
                "System.Private.CoreLib",
                "--platform",
                "System.Private.CoreLib",
                "System.String",
                .. projection,
            ]);
        var explicitType = await RunAppAsync(
            [
                "type",
                "System.String",
                "--platform",
                "System.Private.CoreLib",
                .. projection,
            ]);

        Assert.Equal(explicitType, commandless);
        Assert.Equal(0, commandless.Exit);
    }

    [Fact]
    public async Task CommandlessDisabledAllLibrariesDoesNotBecomeTarget()
    {
        var result = await RunAppAsync(
            "Missing.Package",
            "--all-libraries",
            "false",
            "-D",
            "Package Info",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[package/package/Package] Package Info",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task CommandlessLeadingDisabledAllLibrariesDoesNotBecomeTarget()
    {
        var result = await RunAppAsync(
            "--all-libraries",
            "false",
            "Missing.Package",
            "-D",
            "Package Info",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[package/package/Package] Package Info",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task StaticGenericMemberTailKeepsPeeledInterpretation()
    {
        string fixture =
            typeof(MemberGenericSelectorFixture)
                .Assembly.Location;
        var result = await RunAppAsync(
            "member",
            $"{typeof(MemberGenericSelectorFixture).FullName}.GenericChoice<T>",
            "--library",
            fixture,
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[member/member-target/ApiMemberDetail] Signature",
            result.Output);
        Assert.Contains(
            "[member/type-view/ApiMember] unresolved 'Signature'",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task CommandlessSourceFreeGenericMemberTailKeepsPeeledInterpretation()
    {
        var result = await RunAppAsync(
            "System.Linq.Enumerable.Empty<T>",
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[member/member-target/ApiMemberDetail] Signature",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task CommandlessGenericMemberCategoryKeepsPeeledInterpretation()
    {
        string fixture =
            typeof(MemberGenericSelectorFixture)
                .Assembly.Location;
        string target =
            $"{typeof(MemberGenericSelectorFixture).FullName}.GenericChoice<T>";

        var result = await RunAppAsync(
            target,
            "--library",
            fixture,
            "-D",
            "@Source",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[member/member-target/ApiMemberDetail] Annotated Source",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("platform")]
    public async Task CommandlessGenericMethodTailKeepsTypeAndMemberInterpretations(
        string source)
    {
        string[] scope = source == "library"
            ?
            [
                "--library",
                Path.Combine(
                    Path.GetTempPath(),
                    "missing-generic-member.dll"),
            ]
            :
            [
                "--platform",
                "Missing.Generic.Member.Platform",
            ];

        var result = await RunAppAsync(
            [
                "Missing.Type<T>.Run<U>",
                .. scope,
                "-D",
                SectionNames.Signature,
                "--schema",
                "--table",
                "--tips",
                "q",
            ]);

        Assert.True(
            result.Exit == 0,
            result.Output + result.Error);
        Assert.Contains(
            "[member/member-target/ApiMemberDetail] Signature",
            result.Output);
        Assert.Contains(
            "ApiMember] unresolved 'Signature'",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task EffectiveExactDiscovery_WildcardRequiresUniqueResolvedMember()
    {
        var result = await RunAppAsync(
            "member",
            "System.String",
            "-m",
            "Contains*",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            SectionNames.Signature,
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "requires a single selected overload",
            result.Error);
    }

    [Fact]
    public async Task EffectiveExactDiscovery_ReplansAfterImpliedMemberMerge()
    {
        var result = await RunAppAsync(
            "member",
            "System.String.Contains",
            "-m",
            "StartsWith",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            SectionNames.Signature,
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Exact-member section selection requires exactly one member name.",
            result.Error);
    }

    [Fact]
    public async Task EffectiveExactSelection_RevalidatesAfterCatalogTransition()
    {
        var result = await RunAppAsync(
            "member",
            "System.String.Contains:1",
            "--platform",
            "System.Private.CoreLib",
            "-m",
            "Contains",
            "-S",
            SectionNames.Methods,
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"Select value '{SectionNames.Methods}' not found.",
            result.Error);
    }

    [Fact]
    public async Task EffectiveTypeListing_SelectionConstrainsDiscovery()
    {
        var result = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-t",
            "*String*",
            "-S",
            "Classes",
            "-D",
            "Structs",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Section 'Structs' not found.",
            result.Error);
    }

    [Fact]
    public async Task EffectiveTypeDiscovery_RejectsUniversalMissBeforeAcquisition()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var result = await RunAppAsync(
            "type",
            "Missing.Type",
            "--library",
            missing,
            "-D",
            "DefinitelyNotASection",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "DefinitelyNotASection",
            result.Error);
        Assert.DoesNotContain(
            "File not found",
            result.Error);
    }

    [Fact]
    public async Task StaticBodyPredicate_MultipleMembersRejectBeforeAcquisition()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var result = await RunAppAsync(
            "member",
            "Missing.Type",
            "-m",
            "Run",
            "-m",
            "Stop",
            "--library",
            missing,
            "--where",
            "Kind=InvocationExpression",
            "-D",
            "--schema",
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--where Kind=... requires one exact member name or selector.",
            result.Error);
        Assert.DoesNotContain(
            "File not found",
            result.Error);
    }

    [Fact]
    public async Task CommandlessStaticAll_RetainsExactDiscoveryDemand()
    {
        var result = await RunAppAsync(
            "Missing.Type.Run",
            "-S",
            SelectResolver.AllSelector,
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[member/member-target/ApiMemberDetail] Signature",
            result.Output);
        Assert.DoesNotContain(
            "[member/member-target/ApiMemberOverload] Signature",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("-S")]
    [InlineData("-D")]
    public async Task CommandlessUniversalMiss_RejectsBeforeTargetResolution(
        string selectorOption)
    {
        var result = await RunAppAsync(
            "Timer.Start",
            selectorOption,
            "DefinitelyNotASection",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "DefinitelyNotASection",
            result.Error);
        Assert.DoesNotContain(
            "matched multiple platform types",
            result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactDiscoveryWithoutMember_RejectsBeforeAcquisition(
        bool schema)
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        var args = new List<string>
        {
            "member",
            "MissingType",
            "--library",
            missing,
            "-D",
            SectionNames.IL,
        };
        if (schema)
            args.Add("--schema");
        args.AddRange(["--table", "--tips", "q"]);

        var result = await RunAppAsync([.. args]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Exact-member section selection requires exactly one member name.",
            result.Error);
        Assert.DoesNotContain(
            "File not found",
            result.Error);
    }

    [Fact]
    public async Task StaticConflictingDottedOrdinals_AreRejected()
    {
        var result = await RunAppAsync(
            "member",
            "System.String.Contains:1",
            "--platform",
            "System.Private.CoreLib",
            "-m",
            "Contains:2",
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "cannot combine different overload selectors",
            result.Error);
    }

    [Fact]
    public async Task CommandlessNonGenericTypeGenericMethodTail_KeepsAlternatives()
    {
        string fixture =
            typeof(MemberGenericSelectorFixture)
                .Assembly.Location;

        var result = await RunAppAsync(
            $"{typeof(MemberGenericSelectorFixture).FullName}.GenericChoice<T>",
            "--library",
            fixture,
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[member/member-target/ApiMemberDetail] Signature",
            result.Output);
        Assert.Contains(
            "[type/type/ApiMember] unresolved 'Signature'",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("-D", "Signature")]
    [InlineData("-D", "Signature,IL")]
    [InlineData("--section", "Signature")]
    public void GenericMethodTailExactDemand_RecognizesSectionSpellings(
        string sectionOption,
        string sections)
    {
        string target =
            $"{typeof(MemberGenericSelectorFixture).FullName}.GenericChoice<T>";

        Assert.True(
            StructuralViewRegistry
                .RequiresGenericTailMemberAlternative(
                    target,
                    [target, sectionOption, sections]));
    }

    [Fact]
    public async Task PositionalPackageStaticDottedTarget_MatchesPackageOption()
    {
        string[] projection =
        [
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var positional = await RunAppAsync(
            [
                "member",
                "missing-for-schema.nupkg",
                "Missing.Type",
                .. projection,
            ]);
        var option = await RunAppAsync(
            [
                "member",
                "Missing.Type",
                "--package",
                "missing-for-schema.nupkg",
                .. projection,
            ]);

        Assert.Equal(option, positional);
        Assert.Equal(0, positional.Exit);
    }

    [Fact]
    public async Task PositionalLibraryStaticDottedTarget_MatchesLibraryOption()
    {
        string[] projection =
        [
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var positional = await RunAppAsync(
            [
                "member",
                "missing-for-schema.dll",
                "Missing.Type",
                .. projection,
            ]);
        var option = await RunAppAsync(
            [
                "member",
                "Missing.Type",
                "--library",
                "./missing-for-schema.dll",
                .. projection,
            ]);

        Assert.Equal(option, positional);
        Assert.Equal(0, positional.Exit);
    }

    [Fact]
    public async Task PositionalLibraryEffectiveDottedTarget_MatchesLibraryOption()
    {
        string fixture =
            typeof(MemberGenericSelectorFixture)
                .Assembly.Location;
        string target =
            $"{typeof(MemberGenericSelectorFixture).FullName}.GenericChoice<T>";
        string[] projection =
        [
            "-D",
            SectionNames.Signature,
            "--table",
            "--tips",
            "q",
        ];
        var positional = await RunAppAsync(
            [
                "member",
                fixture,
                target,
                .. projection,
            ]);
        var option = await RunAppAsync(
            [
                "member",
                target,
                "--library",
                fixture,
                .. projection,
            ]);

        Assert.Equal(option, positional);
        Assert.Equal(0, positional.Exit);
    }

    [Fact]
    public async Task AutoSelectedOverload_RevalidatesFinalCatalog()
    {
        var result = await RunAppAsync(
            "member",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-m",
            "Clone",
            "-S",
            SectionNames.Signature,
            "-S",
            SectionNames.Methods,
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(SectionNames.Signature, result.Output);
        Assert.Contains(
            $"Select value '{SectionNames.Methods}' not found.",
            result.Error);
    }

    [Fact]
    public async Task AutoSelectedOverload_DoesNotRepeatProvisionalDiagnostics()
    {
        var result = await RunAppAsync(
            "member",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-m",
            "Clone",
            "-S",
            $"{SectionNames.Signature},Not A Section",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Equal(
            1,
            result.Error.Split(
                "Select value 'Not A Section' not found.",
                StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("Missing.Type.operator+")]
    [InlineData("Missing.Type.op_Addition")]
    public async Task StaticOperatorWithDistinctExplicitMember_IsRejected(
        string target)
    {
        var result = await RunAppAsync(
            "member",
            target,
            "--platform",
            "Missing.Platform",
            "-m",
            "Other",
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "exactly one member name",
            result.Error);
    }

    [Theory]
    [InlineData("--latest-version=false")]
    [InlineData("--latest-version:false")]
    public async Task CommandlessDisabledLatestVersionRetainsAlternatives(
        string option)
    {
        var result = await RunAppAsync(
            "Missing.Type.Run",
            option,
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains("[package/", result.Output);
        Assert.Contains("[type/", result.Output);
        Assert.Contains("[member/", result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task TargetFreeStaticSchemaRetainsTableDefault(
        string command)
    {
        var defaultResult = await RunAppAsync(
            command,
            "-D",
            "--schema",
            "--tips",
            "q");
        var explicitTableResult = await RunAppAsync(
            command,
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(explicitTableResult, defaultResult);
        Assert.Equal(0, defaultResult.Exit);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task SourceTargetedStaticSchemaRetainsMarkdownDefault(
        string command)
    {
        var defaultResult = await RunAppAsync(
            command,
            "--package",
            "Missing.Package.For.Schema",
            "-D",
            "--schema",
            "--tips",
            "q");
        var explicitMarkdownResult = await RunAppAsync(
            command,
            "--package",
            "Missing.Package.For.Schema",
            "-D",
            "--schema",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(explicitMarkdownResult, defaultResult);
        Assert.Equal(0, defaultResult.Exit);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task StaticSchemaRejectsUnrecognizedOptions(
        string command)
    {
        var result = await RunAppAsync(
            command,
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "--bogus",
            "-D",
            "--schema",
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Unrecognized option '--bogus'",
            result.Error);
    }

    [Fact]
    public async Task CommandlessStaticSchemaRejectsUnrecognizedOptions()
    {
        var result = await RunAppAsync(
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "--bogus",
            "-D",
            "--schema",
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Unrecognized option '--bogus'",
            result.Error);
    }

    [Fact]
    public async Task PackageRelativeLibraryPrecedesCommandlessTypeFilter()
    {
        string[] tail =
        [
            "--library",
            "lib/net8.0/Missing.dll",
            "-t",
            "Missing.Type",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var commandless = await RunAppAsync(
            ["Missing.Package", .. tail]);
        var explicitPackage = await RunAppAsync(
            ["package", "Missing.Package", .. tail]);

        Assert.Equal(explicitPackage, commandless);
        Assert.Equal(0, commandless.Exit);
        Assert.Contains(
            "SourceLink: Files",
            commandless.Output);
    }

    [Fact]
    public async Task ExplicitSourceOrdinaryOpPrefixRetainsAlternatives()
    {
        var result = await RunAppAsync(
            "System.String.op_Helpers",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[member/type-view/",
            result.Output);
        Assert.Contains(
            "[member/member-target/",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task PositionalSourceOnlySchemaMatchesExplicitPackage(
        string command)
    {
        string[] tail =
        [
            "-D",
            "--schema",
            "--tips",
            "q",
        ];
        var positional = await RunAppAsync(
            [command, "Missing.Package.For.Schema", .. tail]);
        var explicitPackage = await RunAppAsync(
            [
                command,
                "--package",
                "Missing.Package.For.Schema",
                .. tail,
            ]);

        Assert.Equal(explicitPackage, positional);
        Assert.Equal(0, positional.Exit);
        Assert.Contains("|", positional.Output);
    }

    [Fact]
    public async Task TypeSelectorDoesNotReinterpretPositionalSource()
    {
        string[] tail =
        [
            "-m",
            "Run",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var positional = await RunAppAsync(
            ["type", "Missing.Package.For.Schema", .. tail]);
        var explicitPackage = await RunAppAsync(
            [
                "type",
                "--package",
                "Missing.Package.For.Schema",
                .. tail,
            ]);

        Assert.Equal(explicitPackage, positional);
        Assert.Equal(0, positional.Exit);
    }

    [Fact]
    public async Task MemberIndexDoesNotReinterpretPositionalSource()
    {
        string[] tail =
        [
            "--index",
            "1",
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var positional = await RunAppAsync(
            ["member", "Missing.Package.For.Schema", .. tail]);
        var explicitPackage = await RunAppAsync(
            [
                "member",
                "--package",
                "Missing.Package.For.Schema",
                .. tail,
            ]);

        Assert.Equal(explicitPackage, positional);
        Assert.Equal(1, positional.Exit);
        Assert.Contains(
            "--index/Name:N requires exactly one member name",
            positional.Error);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task SyntaxProvenBareTypeRetainsTypeCatalog(
        string command)
    {
        string[] tail =
        [
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var bareAlias = await RunAppAsync(
            [command, "string", .. tail]);
        var explicitPlatform = await RunAppAsync(
            [
                command,
                "string",
                "--platform",
                "System.Private.CoreLib",
                .. tail,
            ]);

        Assert.Equal(explicitPlatform, bareAlias);
        Assert.Equal(0, bareAlias.Exit);
    }

    [Fact]
    public async Task OrdinaryOpPrefixRetainsCommandlessAlternatives()
    {
        var result = await RunAppAsync(
            "Missing.Op_Helpers",
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains("[type/", result.Output);
        Assert.Contains("[member/", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task CommandlessSourceIdentityNumericTypeLimitRetainsListing()
    {
        string[] projection =
        [
            "-t",
            "5",
            "-D",
            "API Info",
            "--tips",
            "q",
        ];
        var commandless = await RunAppAsync(
            [
                "System.Private.CoreLib",
                "--platform",
                "System.Private.CoreLib",
                .. projection,
            ]);
        var explicitType = await RunAppAsync(
            [
                "type",
                "--platform",
                "System.Private.CoreLib",
                .. projection,
            ]);

        Assert.Equal(explicitType, commandless);
        Assert.Equal(0, commandless.Exit);
    }

    [Fact]
    public async Task CommandlessNumericMemberLimitRetainsTypeView()
    {
        string[] projection =
        [
            "--platform",
            "System.Private.CoreLib",
            "-m",
            "5",
            "-S",
            "-D",
            "Method Groups",
            "--markdown",
            "--tips",
            "q",
        ];
        var commandless = await RunAppAsync(
            ["String", .. projection]);
        var explicitMember = await RunAppAsync(
            ["member", "String", .. projection]);

        Assert.Equal(explicitMember, commandless);
        Assert.Equal(0, commandless.Exit);
    }

    [Fact]
    public async Task CommandlessMalformedDiscoveryUsesParserDiagnostic()
    {
        var result = await RunAppAsync(
            "Missing.Helpers",
            "-D",
            "Signature",
            "-D",
            "IL",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "expects a single argument",
            result.Error);
        Assert.DoesNotContain(
            "System.InvalidOperationException",
            result.Error);
        Assert.DoesNotContain(
            "RouterCommandDefinition",
            result.Error);
    }

    [Fact]
    public async Task AlternativeDiscoveryTotalMissIsRejected()
    {
        var result = await RunAppAsync(
            "member",
            "Missing.Type.Run",
            "-D",
            "DefinitelyNotASection",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "DefinitelyNotASection",
            result.Error);
    }

    [Fact]
    public async Task SeparatedSignedTypeLimitMatchesAttachedSpelling()
    {
        string[] suffix =
        [
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var attached = await RunAppAsync(
            ["Missing.Type", "-t=-1", .. suffix]);
        var separated = await RunAppAsync(
            ["Missing.Type", "-t", "-1", .. suffix]);

        Assert.Equal(attached, separated);
        Assert.Equal(0, separated.Exit);
        Assert.Contains(
            "[type/type/ApiType]",
            separated.Output);
    }

    [Fact]
    public async Task GenericRoutingUsesSelectionConstrainedDemand()
    {
        string[] suffix =
        [
            "System.Collections.Generic.List<string>",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            SectionNames.Methods,
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var commandless = await RunAppAsync(suffix);
        var explicitType = await RunAppAsync(
            ["type", .. suffix]);

        Assert.Equal(explicitType, commandless);
        Assert.Equal(1, commandless.Exit);
        Assert.Contains(
            SectionNames.Signature,
            commandless.Error);
    }

    [Fact]
    public async Task PackageLibraryPlainTextSchemaMatchesDirectLibrary()
    {
        string[] suffix =
        [
            "-D",
            "--schema",
            "--plaintext",
            "--tips",
            "q",
        ];
        var packageLibrary = await RunAppAsync(
            [
                "package",
                "Missing.Package",
                "--library",
                "Missing.dll",
                .. suffix,
            ]);
        var directLibrary = await RunAppAsync(
            ["library", "Missing.dll", .. suffix]);

        Assert.Equal(0, packageLibrary.Exit);
        Assert.Equal(0, directLibrary.Exit);
        Assert.DoesNotContain("|", packageLibrary.Output);
        Assert.DoesNotContain("|", directLibrary.Output);
        Assert.Empty(packageLibrary.Error);
        Assert.Empty(directLibrary.Error);
    }

    [Fact]
    public async Task FullyQualifiedTypeWithExplicitMemberRetainsDetailSchema()
    {
        string[] suffix =
        [
            "--platform",
            "System.Private.CoreLib",
            "-D",
            SectionNames.Signature,
            "--schema",
            "--table",
            "--tips",
            "q",
        ];
        var optionMember = await RunAppAsync(
            [
                "member",
                "System.Text.StringBuilder",
                "-m",
                "Append",
                .. suffix,
            ]);
        var positionalMember = await RunAppAsync(
            [
                "member",
                "System.Text.StringBuilder",
                "Append",
                .. suffix,
            ]);

        Assert.Equal(optionMember, positionalMember);
        Assert.Equal(0, optionMember.Exit);
        Assert.Contains(
            "Canonical Signature",
            optionMember.Output);
    }

    [Fact]
    public async Task BareGenericMethodTailRetainsStructuralAlternatives()
    {
        var result = await RunAppAsync(
            "member",
            "System.Linq.Enumerable.Where<int>",
            "--platform",
            "Missing.Platform",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "[member/member-target/ApiMemberOverload]",
            result.Output);
        Assert.Contains(
            "[member/type-view/ApiMember]",
            result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("all")]
    public async Task ProgrammaticPackageLibraryModeRejectsLayout(
        string route)
    {
        var options = new InspectionOptions
        {
            PackageArgs = ["Missing.Package"],
            PackageLibrary =
                route == "library"
                    ? "ref/net8.0/Missing.dll"
                    : null,
            AllLibraries = route == "all",
            ListLayout = true,
        };

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(options));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            route == "library"
                ? "--library cannot be combined with --layout"
                : "--all-libraries cannot be combined with --layout",
            result.Error);
        Assert.DoesNotContain(
            "Package 'Missing.Package' not found",
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
