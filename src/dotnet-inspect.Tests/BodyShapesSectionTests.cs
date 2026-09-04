using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Markout;
using System.Reflection;
using System.Text.Json;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class BodyShapesSectionTests
{
    static string FixturePath => typeof(BodyShapeFixture).Assembly.Location;

    [Fact]
    public async Task LibraryKindPredicate_AutoSelectsBodyShapesSection()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        Assert.Contains("## Body Shapes", result.Output, StringComparison.Ordinal);
        Assert.Contains("ObjectCreationExpression", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            nameof(BodyShapeFixture.PublicCreation),
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberBodyShapeGlobExpansion_RequiresKindPredicate()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(CommandLineBuilder.PreprocessArgs(
                [
                    "member",
                    typeof(BodyShapeFixture).FullName!,
                    $"{nameof(BodyShapeFixture.PublicCreation)}:1",
                    "--library",
                    FixturePath,
                    "-S",
                    "Body*,Signature",
                ]))
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --where \"Kind=<C# Body Kinds ID>\"",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryKindPredicate_UsesOrdinaryJsonlProjection()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--columns",
                    "Kind;Token",
                    "--rows",
                    "1",
                    "--jsonl",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        using var row = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "ObjectCreationExpression",
            row.RootElement.GetProperty("kind").GetString());
        Assert.StartsWith(
            "0x06",
            row.RootElement.GetProperty("token").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(2, row.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task LibraryKindPredicate_CountAppliesTheRenderedRowWindow()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--rows",
                    "2..3",
                    "--count",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2", result.Output.Trim());
    }

    [Fact]
    public async Task LibraryKindPredicate_CountValidatesTheColumnProjection()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--columns",
                    "NoSuchColumn",
                    "--count",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "No columns matched projection: NoSuchColumn",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryKindPredicate_IncludesMatchesInStructuredJson()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--json",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(result.Output);
        var matches = document.RootElement.GetProperty("body_shapes");
        Assert.NotEmpty(matches.EnumerateArray());
        Assert.All(
            matches.EnumerateArray(),
            match => Assert.Equal(
                "ObjectCreationExpression",
                match.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task EffectiveDiscovery_RequiresKindBeforeRunningBodyShapes()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(CommandLineBuilder.PreprocessArgs(
                [
                    "library",
                    FixturePath,
                    "-D",
                    SectionNames.BodyShapes,
                    "--effective",
                ]))
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --where \"Kind=<C# Body Kinds ID>\"",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Could not read library",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheapDiscovery_DoesNotRunComposedPerformanceQuery()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "-D",
                    "--where",
                    "Kind=ArrayCreationExpression",
                    "--where",
                    "Shape=small-array",
                    "--trace",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(
            OptimizationOpportunitiesQuery.Definition.Name,
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Inspection Failures",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberEffectiveDiscovery_BodyShapeGlobRequiresKind()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeof(BodyShapeFixture).FullName!,
                    $"{nameof(BodyShapeFixture.PublicCreation)}:1",
                    "--library",
                    FixturePath,
                    "-D",
                    "Body*",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --where \"Kind=<C# Body Kinds ID>\"",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyBodyShapesResult_RendersExplicitEmptyState()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Fixture.dll",
            BodyShapeSearchResult = new BodyShapeSearchResult([], [], 0),
        };

        string output = MarkoutSerializer.Serialize(
            new LibraryInspectionView(inspection),
            InspectionContext.Default,
            new MarkoutWriterOptions
            {
                IncludeSections = [SectionNames.BodyShapes],
            });

        Assert.Contains("## Body Shapes", output, StringComparison.Ordinal);
        Assert.Contains(
            "No matching body shapes found.",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BodyShapesSelection_RequiresKindPredicate()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(["library", FixturePath, "-S", "Body Shapes"]).InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --where \"Kind=<C# Body Kinds ID>\"",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BodyKindPredicate_DoesNotLeakIntoAnotherSelectedSection()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "-S",
                    "Library Info",
                    "--where",
                    "Kind=LiteralExpression",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "targets section 'Body Shapes'",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryKindPredicate_ComposesWithPerformancePredicates()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ArrayCreationExpression",
                    "--where",
                    "Finding=analysis.allocation",
                    "--where",
                    "Shape=small-array",
                    "--where",
                    "Confidence>=low",
                    "--jsonl",
                    "--trace",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            nameof(BodyShapeFixture.PublicSmallArray),
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Performance:", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            OptimizationOpportunitiesQuery.Definition.Name,
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            BodyShapesQuery.Definition.Name,
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryKindPredicate_DoesNotRunOptionalPerformanceQuery()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--trace",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            BodyShapesQuery.Definition.Name,
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            OptimizationOpportunitiesQuery.Definition.Name,
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposedBodyShapesJson_OmitsUnselectedPerformanceProjection()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "-S",
                    SectionNames.BodyShapes,
                    "--where",
                    "Kind=ArrayCreationExpression",
                    "--where",
                    "Shape=small-array",
                    "--json",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.NotEmpty(
            document.RootElement.GetProperty("body_shapes").EnumerateArray());
        Assert.False(
            document.RootElement.TryGetProperty("performance", out _));
    }

    [Fact]
    public async Task ComposedBodyShapesJson_PreservesSelectedPerformanceProjection()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(CommandLineBuilder.PreprocessArgs(
                [
                    "library",
                    FixturePath,
                    "-S",
                    SectionNames.BodyShapes,
                    "-S",
                    SectionNames.PerformanceArrays,
                    "--where",
                    "Kind=ArrayCreationExpression",
                    "--where",
                    "Shape=small-array",
                    "--json",
                ]))
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.NotEmpty(
            document.RootElement.GetProperty("body_shapes").EnumerateArray());
        Assert.NotEmpty(
            document.RootElement
                .GetProperty("performance")
                .GetProperty("arrays")
                .EnumerateArray());
    }

    [Fact]
    public async Task LibraryKindPredicate_IntersectsAllPerformancePredicates()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ArrayCreationExpression",
                    "--where",
                    "Finding=analysis.call-site",
                    "--where",
                    "Shape=small-array",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "No matching body shapes found.",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(BodyShapeFixture.PublicSmallArray),
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryKindPredicate_MapsLiftedBodyToSourceOwner()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=InvocationExpression",
                    "--where",
                    "Shape=generic-parameter-object-box",
                    "--jsonl",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            nameof(BodyShapeFixture.PublicLocalFunctionBox),
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<PublicLocalFunctionBox>",
            result.Output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--top", "1")]
    [InlineData("--order-by", "Confidence desc")]
    public async Task LibraryKindPredicate_RejectsPerformanceRankingControls(
        string option,
        string value)
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ArrayCreationExpression",
                    "--where",
                    "Shape=small-array",
                    option,
                    value,
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "not --top or --order-by",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            "Use --rows to limit rendered matches.",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceSourceMethods_UsesSourceOwnerIdentity()
    {
        var declaringType = ILInspector.Analysis.TypeRef.Definition(
            "Fixture",
            "DotnetInspector.Fixtures",
            nameof(BodyShapeFixture));
        var synthesized = new ILInspector.Analysis.MethodIdentity(
            "Fixture",
            Guid.Empty,
            declaringType,
            "<PublicSmallArray>g__Create|0_0",
            [],
            ILInspector.Analysis.TypeRef.CoreLib("System", "Object"),
            0x06000002,
            IsStatic: true);
        var sourceOwner = synthesized with
        {
            Name = nameof(BodyShapeFixture.PublicSmallArray),
            MetadataToken = 0x06000001,
        };
        var opportunity = new ILInspector.Analysis.OptimizationOpportunity(
            synthesized,
            "small-array",
            "newarr",
            "Use stackalloc when the array does not escape.",
            "high",
            InLoop: false,
            ILOffset: 0,
            Caveat: null)
        {
            SourceOwner = sourceOwner,
        };

        var methods = LibraryMetadataService.PerformanceSourceMethods(
            [opportunity]);

        Assert.Equal([sourceOwner], methods);
    }

    [Fact]
    public async Task TypeKindPredicate_AutoSelectsBodyShapesForOnlyTheSelectedType()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(BodyShapeFixture).FullName!,
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--jsonl",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        Assert.Contains(
            nameof(BodyShapeFixture.PublicCreation),
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(MemberAccessorModifierFixture).FullName!,
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateCreation", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(BodyShapeFixtureExtensions.ProjectedCreation),
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeKindPredicate_AutoSelectsBodyShapesInDefaultOutput()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(BodyShapeFixture).FullName!,
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        Assert.Contains("Body Shapes", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            nameof(BodyShapeFixture.PublicCreation),
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "ObjectCreationExpression",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("├─", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeKindPredicate_PlainTextHonorsRowWindow()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(BodyShapeFixture).FullName!,
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--plaintext",
                    "--columns",
                    "Kind;Member",
                    "--rows",
                    "2",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            2,
            result.Output
                .Split('\n')
                .Count(line => line.Contains(
                    "ObjectCreationExpression",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TypeKindPredicate_ExplicitShapeWarnsThatSelectionIsIgnored()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(BodyShapeFixture).FullName!,
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--shape",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "--where Kind=...",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains("├─", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Body Shapes", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeKindPredicate_QuietVerbosityFailsVisibly()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(BodyShapeFixture).FullName!,
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "-v:q",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "-v:q is not supported by Body Shapes queries",
            result.Error,
            StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    [InlineData("--no-header")]
    public async Task TypeKindPredicate_QuietVerbosityWithOutputModifierFailsVisibly(
        string outputOption)
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(BodyShapeFixture).FullName!,
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "-v:q",
                    outputOption,
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "-v:q is not supported by Body Shapes queries",
            result.Error,
            StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    [Fact]
    public void TypeBodyShapeTokens_ExcludeProjectedExtensionMethods()
    {
        using var source = MetadataSource.Open(FixturePath);
        var surface = source.ExtractApiSurface(includeAll: false);
        var type = Assert.Single(surface.Types, candidate =>
            candidate.FullName == typeof(BodyShapeFixture).FullName);
        var projectedExtension = Assert.Single(type.Members, member =>
            member.Kind == "extension-method"
            && member.Name == nameof(BodyShapeFixtureExtensions.ProjectedCreation));

        var tokens = ApiOutputFormatter.ResolveTypeBodyShapeMethodTokens(type);

        Assert.DoesNotContain(projectedExtension.MetadataToken!.Value, tokens);
        Assert.Contains(
            typeof(BodyShapeFixture)
                .GetMethod(nameof(BodyShapeFixture.PublicCreation))!
                .MetadataToken,
            tokens);
    }

    [Fact]
    public async Task TypeKindPredicate_IncludesAccessorMethodTokens()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(MemberAccessorModifierFixture).FullName!,
                    "--library",
                    typeof(MemberAccessorModifierFixture).Assembly.Location,
                    "--where",
                    "Kind=AssignmentStatement",
                    "--jsonl",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        Assert.Contains(".State~", result.Output, StringComparison.Ordinal);
        Assert.Contains(":2", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeKindPredicate_AllIncludesNonPublicBodiesInTheSelectedType()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(BodyShapeFixture).FullName!,
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--all",
                    "--jsonl",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("PrivateCreation", result.Output, StringComparison.Ordinal);
        Assert.Contains("new Version(1, 2)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeKindPredicate_RequiresOneExactType()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires one exact type name",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--jsonl")]
    [InlineData("--count")]
    public async Task TypeKindPredicate_UnresolvedTypeDoesNotFallBackToPrefixBrowse(
        string outputOption)
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    "DotnetInspector.Fixtures.BodyShape",
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    outputOption,
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prefix matches", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task TypeBodyShapesSelection_RequiresKindPredicate()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(BodyShapeFixture).FullName!,
                    "--library",
                    FixturePath,
                    "-S",
                    "Body Shapes",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --where \"Kind=<C# Body Kinds ID>\"",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeKindPredicate_RejectsPerformancePredicatesInSameQuery()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(BodyShapeFixture).FullName!,
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--where",
                    "Confidence=high",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "cannot yet be combined with Performance Triage",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains("one type query", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeBareDiscovery_DoesNotAdvertiseBodyShapesWithoutKind()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "type",
                    typeof(BodyShapeFixture).FullName!,
                    "--library",
                    FixturePath,
                    "-D",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Body Shapes", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PdbAcquisition_PropagatesCallerCancellation()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"body-shape-pdb-cancellation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string runtimeAssembly = typeof(object).Assembly.Location;
        string assemblyPath = Path.Combine(directory, Path.GetFileName(runtimeAssembly));
        File.Copy(runtimeAssembly, assemblyPath);
        try
        {
            using var httpClient = new HttpClient();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ApiCommand.TryAcquirePdbPathAsync(
                    assemblyPath,
                    new ApiOptions { AssemblyPath = assemblyPath },
                    new VerboseLogger(enabled: false),
                    httpClient,
                    cancellation.Token));
        }
        finally
        {
            File.Delete(assemblyPath);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task MemberKindPredicate_AutoSelectsBodyShapesForOnlyTheSelectedMethod()
    {
        var result = await RunMemberAsync(
            nameof(BodyShapeFixture.PublicCreation),
            "ObjectCreationExpression");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        Assert.Contains("## Body Shapes", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            nameof(BodyShapeFixture.PublicCreation),
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateCreation", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberKindPredicate_QualifiedTypeMemberComposesWithCount()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    $"{typeof(BodyShapeFixture).FullName}."
                        + nameof(BodyShapeFixture.PublicCreation),
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--count",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.Output.Trim());
    }

    [Fact]
    public async Task MemberKindPredicate_AutoSelectsTheUniqueOverload()
    {
        var result = await RunMemberAsync(
            nameof(BodyShapeFixture.Branch),
            "IfStatement",
            includeSelector: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Body Shapes", result.Output, StringComparison.Ordinal);
        Assert.Contains("if (value)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberKindPredicate_AmbiguousNameRequiresAnOverloadSelector()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeof(MemberCallsFixture).FullName!,
                    nameof(MemberCallsFixture.Overloaded),
                    "--library",
                    typeof(MemberCallsFixture).Assembly.Location,
                    "--where",
                    "Kind=InvocationExpression",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires a single selected overload",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            $"{nameof(MemberCallsFixture.Overloaded)}:1",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberEffectiveDiscovery_DescribesBodyShapesWhenKindIsPresent()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(CommandLineBuilder.PreprocessArgs(
                [
                    "member",
                    typeof(BodyShapeFixture).FullName!,
                    nameof(BodyShapeFixture.PublicCreation),
                    "--library",
                    FixturePath,
                    "-D",
                    "Body Shapes",
                    "--where",
                    "Kind=ObjectCreationExpression",
                ]))
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        Assert.Contains("| Kind | column |", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberEffectiveDiscovery_RequiresKindBeforeRunningBodyShapes()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(CommandLineBuilder.PreprocessArgs(
                [
                    "member",
                    typeof(BodyShapeFixture).FullName!,
                    $"{nameof(BodyShapeFixture.PublicCreation)}:1",
                    "--library",
                    FixturePath,
                    "-D",
                    "Body Shapes",
                ]))
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --where \"Kind=<C# Body Kinds ID>\"",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberBareDiscovery_DoesNotAdvertiseBodyShapesWithoutKind()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeof(BodyShapeFixture).FullName!,
                    nameof(BodyShapeFixture.PublicCreation),
                    "--library",
                    FixturePath,
                    "-D",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Body Shapes", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberBodyShapesSelection_RequiresKindPredicate()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeof(BodyShapeFixture).FullName!,
                    $"{nameof(BodyShapeFixture.PublicCreation)}:1",
                    "--library",
                    FixturePath,
                    "-S",
                    "Body Shapes",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --where \"Kind=<C# Body Kinds ID>\"",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberBroadSelection_OmitsBodyShapesWithoutKind()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeof(BodyShapeFixture).FullName!,
                    $"{nameof(BodyShapeFixture.PublicCreation)}:1",
                    "--library",
                    FixturePath,
                    "-S",
                    "*",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Signature", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Body Shapes", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberKindPredicate_RejectsAnotherSelectedSection()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeof(BodyShapeFixture).FullName!,
                    $"{nameof(BodyShapeFixture.PublicCreation)}:1",
                    "--library",
                    FixturePath,
                    "-S",
                    "Decompiled Source",
                    "--where",
                    "Kind=ObjectCreationExpression",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "targets section 'Body Shapes'",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberKindPredicate_RejectsPerformancePredicatesInSameQuery()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeof(BodyShapeFixture).FullName!,
                    $"{nameof(BodyShapeFixture.PublicCreation)}:1",
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--where",
                    "Confidence=high",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "cannot yet be combined with Performance Triage",
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Public*")]
    public async Task MemberKindPredicate_RequiresOneExactMember(string? memberFilter)
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var args = new List<string>
        {
            "member",
            typeof(BodyShapeFixture).FullName!,
            "--library",
            FixturePath,
            "--where",
            "Kind=ObjectCreationExpression",
        };
        if (memberFilter is not null)
        {
            args.Add("-m");
            args.Add(memberFilter);
        }

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(args).InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires one exact member name or selector",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberKindPredicate_UsesOrdinaryJsonlProjection()
    {
        var result = await RunMemberAsync(
            nameof(BodyShapeFixture.PublicCreation),
            "ObjectCreationExpression",
            "--columns",
            "Kind;Token",
            "--jsonl");

        Assert.Equal(0, result.ExitCode);
        using var row = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "ObjectCreationExpression",
            row.RootElement.GetProperty("kind").GetString());
        Assert.StartsWith(
            "0x06",
            row.RootElement.GetProperty("token").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(2, row.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task MemberKindPredicate_CountUsesTheScopedMatches()
    {
        var result = await RunMemberAsync(
            nameof(BodyShapeFixture.PublicCreation),
            "ObjectCreationExpression",
            "--count");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.Output.Trim());
    }

    [Fact]
    public async Task MemberKindPredicate_DocumentJsonFailsClosed()
    {
        var result = await RunMemberAsync(
            nameof(BodyShapeFixture.PublicCreation),
            "ObjectCreationExpression",
            "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "Document --json cannot represent Body Shapes analysis.",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberKindPredicate_RendersExplicitEmptyState()
    {
        var result = await RunMemberAsync(
            nameof(BodyShapeFixture.PublicCreation),
            "FixedStatement");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "No matching body shapes found.",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberKindPredicate_AllCanSelectANonPublicBody()
    {
        var result = await RunMemberAsync(
            "PrivateCreation",
            "ObjectCreationExpression",
            "--all");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("new Version(1, 2)", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(BodyShapeFixture.PublicCreation),
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberKindPredicate_UsesOnlyTheSelectedAccessorBody()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string assemblyPath = typeof(MemberAccessorModifierFixture).Assembly.Location;
        string typeName = typeof(MemberAccessorModifierFixture).FullName!;

        var getter = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeName,
                    "State:1",
                    "--library",
                    assemblyPath,
                    "--where",
                    "Kind=ReturnStatement",
                    "--count",
                ])
                .InvokeAsync());
        var setter = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeName,
                    "State:2",
                    "--library",
                    assemblyPath,
                    "--where",
                    "Kind=ReturnStatement",
                    "--count",
                ])
                .InvokeAsync());

        Assert.Equal(0, getter.ExitCode);
        Assert.Equal("1", getter.Output.Trim());
        Assert.Equal(0, setter.ExitCode);
        Assert.Equal("0", setter.Output.Trim());
    }

    [Fact]
    public async Task MemberKindPredicate_MultipleAccessorsRequireASelector()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeof(MemberAccessorModifierFixture).FullName!,
                    "State",
                    "--library",
                    typeof(MemberAccessorModifierFixture).Assembly.Location,
                    "--where",
                    "Kind=AssignmentStatement",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("has 2 body accessors", result.Error, StringComparison.Ordinal);
        Assert.Contains(":1 through", result.Error, StringComparison.Ordinal);
        Assert.Contains(":2", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberKindPredicate_EmitsAnAccessorSelectorThatRoundTrips()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string assemblyPath = typeof(MemberAccessorModifierFixture).Assembly.Location;
        string typeName = typeof(MemberAccessorModifierFixture).FullName!;

        var first = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeName,
                    "State:2",
                    "--library",
                    assemblyPath,
                    "--where",
                    "Kind=AssignmentStatement",
                    "--jsonl",
                ])
                .InvokeAsync());

        Assert.Equal(0, first.ExitCode);
        using var row = JsonDocument.Parse(first.Output);
        string selector = row.RootElement.GetProperty("member").GetString()!;
        string token = row.RootElement.GetProperty("token").GetString()!;
        Assert.StartsWith($"{typeName}.State~", selector, StringComparison.Ordinal);
        Assert.EndsWith(":2", selector, StringComparison.Ordinal);

        var replay = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    selector,
                    "--library",
                    assemblyPath,
                    "--where",
                    "Kind=AssignmentStatement",
                    "--jsonl",
                ])
                .InvokeAsync());

        Assert.Equal(0, replay.ExitCode);
        using var replayRow = JsonDocument.Parse(replay.Output);
        Assert.Equal(
            token,
            replayRow.RootElement.GetProperty("token").GetString());
        Assert.Equal(
            selector,
            replayRow.RootElement.GetProperty("member").GetString());
    }

    [Fact]
    public async Task MemberKindPredicate_OverloadedAccessorSelectorRoundTrips()
    {
        using var source = MetadataSource.Open(FixturePath);
        var surface = source.ExtractApiSurface(includeAll: false);
        var type = Assert.Single(surface.Types, candidate =>
            candidate.FullName
                == typeof(OverloadedIndexerBodyShapeFixture).FullName);
        var stringIndexer = Assert.Single(type.Members, member =>
            member.Kind == "property"
            && member.Signature?.Contains(
                "string key",
                StringComparison.Ordinal) == true);
        string stable = ApiMemberIdentity
            .GetMemberAnchor(type, stringIndexer)
            .StableSelector;
        string selector = $"{stable}:2";
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeof(OverloadedIndexerBodyShapeFixture).FullName!,
                    selector,
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=InvocationExpression",
                    "--jsonl",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        using var row = JsonDocument.Parse(result.Output);
        string emitted = row.RootElement.GetProperty("member").GetString()!;
        string token = row.RootElement.GetProperty("token").GetString()!;
        Assert.Equal(
            $"{typeof(OverloadedIndexerBodyShapeFixture).FullName}.{selector}",
            emitted);

        var replay = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    emitted,
                    "--library",
                    FixturePath,
                    "--where",
                    "Kind=InvocationExpression",
                    "--jsonl",
                ])
                .InvokeAsync());

        Assert.Equal(0, replay.ExitCode);
        using var replayRow = JsonDocument.Parse(replay.Output);
        Assert.Equal(
            token,
            replayRow.RootElement.GetProperty("token").GetString());
        Assert.Equal(
            emitted,
            replayRow.RootElement.GetProperty("member").GetString());
    }

    [Fact]
    public void MemberBodyResolution_UsesPropertyAndEventAccessorTokens()
    {
        using var source = MetadataSource.Open(FixturePath);
        var surface = source.ExtractApiSurface(includeAll: true);
        var type = Assert.Single(surface.Types, candidate =>
            candidate.FullName == typeof(BodyShapeFixture).FullName);
        var property = Assert.Single(type.Members, member =>
            member.Kind == "property"
            && member.Name
                == $"{typeof(IBodyShapeValue).FullName}.{nameof(IBodyShapeValue.Value)}");
        var @event = Assert.Single(type.Members, member =>
            member.Kind == "event"
            && member.Name
                == $"{typeof(IBodyShapeValue).FullName}.{nameof(IBodyShapeValue.Changed)}");

        type.Members = [property];
        var propertyMethods = ApiOutputFormatter.ResolveBodyMethods(
            type,
            new HashSet<string> { SectionNames.BodyShapes });
        type.Members = [@event];
        var eventMethods = ApiOutputFormatter.ResolveBodyMethods(
            type,
            new HashSet<string> { SectionNames.BodyShapes });

        var reflectionProperty = typeof(BodyShapeFixture).GetProperty(
            $"{typeof(IBodyShapeValue).FullName}.{nameof(IBodyShapeValue.Value)}",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var reflectionEvent = typeof(BodyShapeFixture).GetEvent(
            $"{typeof(IBodyShapeValue).FullName}.{nameof(IBodyShapeValue.Changed)}",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(
            [reflectionProperty.GetMethod!.MetadataToken],
            propertyMethods.Select(method => method.MetadataToken));
        Assert.Equal(
            [
                reflectionEvent.AddMethod!.MetadataToken,
                reflectionEvent.RemoveMethod!.MetadataToken,
            ],
            eventMethods.Select(method => method.MetadataToken));
        Assert.Equal(
            reflectionEvent.RemoveMethod.MetadataToken,
            Assert.Single(
                ApiOutputFormatter.ResolveBodyShapeMethods(
                    eventMethods,
                    overloadIndex: 2)).MetadataToken);
    }

    static Task<(int ExitCode, string Output, string Error)> RunMemberAsync(
        string member,
        string kind,
        params string[] extraArguments)
        => RunMemberAsync(member, kind, includeSelector: true, extraArguments);

    static Task<(int ExitCode, string Output, string Error)> RunMemberAsync(
        string member,
        string kind,
        bool includeSelector,
        params string[] extraArguments)
    {
        var root = CommandLineBuilder.CreateRootCommand();
        return ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "member",
                    typeof(BodyShapeFixture).FullName!,
                    includeSelector ? $"{member}:1" : member,
                    "--library",
                    FixturePath,
                    "--where",
                    $"Kind={kind}",
                    .. extraArguments,
                ])
                .InvokeAsync());
    }
}
