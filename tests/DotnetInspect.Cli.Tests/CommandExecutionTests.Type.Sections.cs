using DotnetInspect.Cli.Sections;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task Type_SingleType_SelectSection_RendersSectionNotShape()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Selection produces a focused section view, not the tree shape.
        Assert.Contains("## Properties", output);
        Assert.DoesNotContain("├─", output);
    }

    /// <summary>
    /// <c>Type Info</c> is the type view's identity fact table, and the only section on the type
    /// pipeline that does not grow with the type under inspection.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_RendersIdentityFactsRatherThanMembers()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = [SectionNames.TypeInfo]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        AssertPlatformTypeInfo(output, "System.Text.Json");
        Assert.Contains("| Type | System.Text.Json.JsonSerializer |", output);
        Assert.Contains("| Kind | class |", output);
        // Identity, not inventory: the member sections stay out.
        Assert.DoesNotContain("## Methods", output);
        Assert.DoesNotContain("## Method Groups", output);
    }

    /// <summary>
    /// The section is <c>ExplicitOnly</c>, so it must not join the default markdown view, where
    /// the same facts already render as the inline identity line. This is the gate for the
    /// "new sections do not enter the default -v:m view" rule for this section.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_DoesNotEnterTheDefaultMarkdownView()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MarkdownExplicitlySet = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Type Info", output);
        // The identity facts are present, just inline rather than as a section.
        Assert.Contains("Kind: class", output);
    }

    /// <summary>
    /// The boundedness claim: the row set is a function of which facts apply to the type, never of
    /// how many members it has. A 200+ member type must not produce a materially larger section
    /// than an 8-member enum, and every label it emits must come from the same fixed vocabulary.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_DoesNotGrowWithTheType()
    {
        var large = await RenderTypeInfoLabelsAsync("System.Private.CoreLib", "String");
        var small = await RenderTypeInfoLabelsAsync("System.Private.CoreLib", "DayOfWeek");

        Assert.NotEmpty(large);
        Assert.NotEmpty(small);

        // The vocabulary is derived from TypeInfoSection's declaration rather than copied here, so
        // the declaration drives the gate: a row whose label is not a declared property fails, and
        // the bound tracks the property count instead of a hand-maintained literal that goes stale.
        var vocabulary = DeclaredTypeInfoLabels();

        // Deriving the vocabulary keeps it from going stale, but on its own it would absorb a new
        // property silently - including an unbounded one. Pinning the count makes any addition fail
        // here, so whoever adds a property has to state that it is a fixed fact about the type and
        // not a per-member row. Bump this only alongside that judgement.
        Assert.Equal(11, vocabulary.Count);

        Assert.All(large, label => Assert.Contains(label, vocabulary));
        Assert.All(small, label => Assert.Contains(label, vocabulary));

        // The bound that makes the section Fixed: one row per declared property, never one per
        // member. String has 250+ members and DayOfWeek has 7; both stay inside a constant.
        Assert.True(
            large.Count <= vocabulary.Count,
            $"Type Info grew past its declared vocabulary: {string.Join(", ", large)}");
    }

    /// <summary>
    /// Effective <c>-D</c> must list the fields <c>Type Info</c> actually renders. Two things can
    /// break this and both did: the section is a <c>Field</c>/<c>Value</c> fact table, so matching
    /// the schema against rendered *table columns* intersects nothing and reports the section as
    /// having no queryable fields at all; and the discovery render manifest is built without
    /// acquisition context, so the provenance rows are invisible to it unless that context is
    /// threaded in. Either failure is silent — exit 0 with fields missing.
    /// </summary>
    [Theory]
    [InlineData("System.String")]
    [InlineData("System.Collections.Generic.List`1")]
    // An enum is the case that catches a census computed off a different member list than the one
    // discovery sees: DayOfWeek's only field is the compiler-generated `value__`.
    [InlineData("System.DayOfWeek")]
    // A readonly ref struct: BuildFilteredTypeForSections did not copy IsReadOnly/IsByRefLike, so
    // discovery hid the Modifiers row that -S renders.
    [InlineData("System.Span`1")]
    [InlineData("System.DateTime")]
    // Filters narrow the type discovery builds its manifest from. Type Info reports identity, not
    // the filtered slice, so its field set must not move when a filter is active.
    [InlineData("System.String", "-m", "Contains")]
    [InlineData("System.String", "--all")]
    [InlineData("System.String", "-k", "property")]
    [InlineData("System.Span`1", "--unsafe")]
    public async Task Type_TypeInfoSection_EffectiveDiscovery_ListsTheFieldsItRenders(
        string typeName,
        params string[] extraArgs)
    {
        string[] discoverArgs = ["type", typeName, .. extraArgs, "-D", SectionNames.TypeInfo];
        string[] renderArgs = ["type", typeName, .. extraArgs, "-S", SectionNames.TypeInfo];

        var (discoverExit, discoverOutput, _) = await RunAppAsync(discoverArgs);
        var (renderExit, renderOutput, _) = await RunAppAsync(renderArgs);

        Assert.Equal(0, discoverExit);
        Assert.Equal(0, renderExit);

        var advertised = ParseFirstColumn(discoverOutput, "Name");
        var rendered = ParseFirstColumn(renderOutput, "Field");

        Assert.NotEmpty(advertised);
        Assert.NotEmpty(rendered);

        // Structural facts the manifest can see from the type itself.
        Assert.Contains("Kind", advertised);
        // Provenance facts that only exist if acquisition context reached the manifest.
        Assert.Contains("Library", advertised);
        Assert.Contains("Source", advertised);

        // Set equality, not containment: -D over-reporting a field -S never renders is the same
        // contract break as under-reporting one, and only equality catches both.
        Assert.Equal(
            rendered.OrderBy(f => f, StringComparer.Ordinal),
            advertised.OrderBy(f => f, StringComparer.Ordinal));
    }

    /// <summary>
    /// Type parameters are an identity fact, so an open generic must report them. The summary used
    /// to be computed only at quiet verbosity for the inline header, which left the section's
    /// declared field permanently empty and made <c>--fields "Type Parameters"</c> report no data.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_ReportsTypeParametersForOpenGenerics()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "System.Collections.Generic.List`1", "-S", SectionNames.TypeInfo);

        Assert.Equal(0, exit);
        Assert.Contains("| Type Parameters | T |", output);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task Type_TypeInfoSection_NonTabularValidEmptyFieldReportsNoData(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            SectionNames.TypeInfo,
            "--fields",
            "Type Parameters",
            format,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(output.Trim());
        Assert.Contains(
            "Note: 1 field has no data: Type Parameters",
            error);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task Type_FieldReplayDoesNotCreditProjectedAwayFieldTable(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            "Type Info,Methods",
            "--fields",
            "Interfaces",
            "--columns",
            "Signature",
            "--rows",
            "1",
            format,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("Methods", output);
        Assert.DoesNotContain("Type Info", output);
        Assert.Contains(
            "Note: 1 field has no data: Interfaces",
            error);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task Type_NonTabularUnknownFieldWithoutSectionFails(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "--fields",
            "NoSuchField",
            format,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "No fields matched projection: NoSuchField",
            error);
    }

    /// <summary>
    /// Bare <c>-S</c> on a single type renders the fixed overview: sections whose length does not
    /// depend on which type is being viewed. It used to render the Info set - the per-kind member
    /// tables - so its size tracked the type, from one section for an enum to seven for
    /// System.String. Type Info is the only Fixed, network-free section on this pipeline.
    /// </summary>
    [Theory]
    [InlineData("System.String")]
    [InlineData("System.DayOfWeek")]
    [InlineData("System.Int32")]
    [InlineData("System.Span`1")]
    [InlineData("System.Exception")]
    public async Task Type_BareSelect_RendersOnlyTheFixedOverview(string typeName)
    {
        var (exit, output, _) = await RunAppAsync("type", typeName, "-S", "--tips", "q");

        Assert.Equal(0, exit);

        var sections = SectionHeadings(output);
        Assert.Equal([SectionNames.TypeInfo], sections);
    }

    /// <summary>
    /// The point of the fixed overview is that its size is a property of the command, not of the
    /// target. A 250-member class and an 8-member enum must produce the same section set, and
    /// neither may run long. Before this, System.String rendered 125 lines and System.DayOfWeek 13.
    /// </summary>
    [Fact]
    public async Task Type_BareSelect_DoesNotGrowWithTheType()
    {
        var (largeExit, large, _) = await RunAppAsync("type", "System.String", "-S", "--tips", "q");
        var (smallExit, small, _) = await RunAppAsync("type", "System.DayOfWeek", "-S", "--tips", "q");

        Assert.Equal(0, largeExit);
        Assert.Equal(0, smallExit);
        Assert.Equal(SectionHeadings(large), SectionHeadings(small));

        // Bounded in rows, not merely in section count: Type Info emits at most one row per
        // declared property, so the whole overview stays within a small constant.
        int largeRows = large.Split('\n').Count(line => line.TrimStart().StartsWith('|'));
        Assert.InRange(largeRows, 1, DeclaredTypeInfoLabels().Count + 2);
    }

    /// <summary>
    /// Explicit selection still wins over the bare marker, and still reaches sections that are not
    /// in the fixed overview.
    /// </summary>
    [Fact]
    public async Task Type_ExplicitSelect_StillReachesGrowingSections()
    {
        var (exit, output, _) = await RunAppAsync("type", "System.String", "-S", "Fields", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal(["Fields"], SectionHeadings(output));
    }

    /// <summary>
    /// Bare <c>-S</c> on the type listing renders exactly the fixed, bounded overview and nothing
    /// else. Before this it resolved to an empty include set, which <c>IsRequested</c> read as "no
    /// filter" and fell through to the verbosity ladder -- so the one flag meant to apply
    /// backpressure printed all five per-kind tables, every one of which grows with the assembly.
    /// </summary>
    [Theory]
    [InlineData("System.Private.CoreLib")]
    [InlineData("System.Text.Json")]
    public async Task Type_Listing_BareSelect_RendersOnlyTheFixedOverview(string library)
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "--platform", library, "-S", "--tips", "q");

        Assert.Equal(0, exit);

        // Asserted against the pipeline rather than against a literal, so that a section added to
        // the fixed overview later is covered here without editing this test -- and so that a
        // section wrongly classified as Fixed shows up as a diff here rather than silently
        // enlarging what bare -S prints.
        var listPipeline = ApiTypeSectionDescriptors.CreatePipeline();
        Assert.Equal([SectionNames.ApiInfo], listPipeline.FixedOverviewSectionNames);
        Assert.Equal(listPipeline.FixedOverviewSectionNames, SectionHeadings(output));

        // The growing tables are the point: naming them individually is what makes this a gate
        // against the fall-through returning, rather than a restatement of the line above.
        foreach (var kind in new[] { "Classes", "Structs", "Interfaces", "Enums", "Delegates" })
            Assert.DoesNotContain(kind, SectionHeadings(output));
    }

    /// <summary>
    /// A dotted name that does not resolve to a type renders a listing, so a listing section name
    /// has to be selectable on it. The preamble picks its pipeline from the argument shape, long
    /// before the assembly is read, so it was answering for the single-type view: <c>-D</c>
    /// advertised <c>Classes</c> and <c>-S Classes</c> was rejected on the same command line,
    /// against a section list the user was never shown.
    /// </summary>
    [Theory]
    [InlineData("Classes")]
    [InlineData(SectionNames.ApiInfo)]
    public async Task Type_PrefixBrowse_ListingSectionName_IsSelectable(string section)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", section, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error, StringComparison.Ordinal);

        // Renders the named section and only it -- a fall-through to the verbosity ladder would
        // also pass a bare "contains Classes" check.
        Assert.Equal([section], SectionHeadings(output));
    }

    /// <summary>
    /// The deferral must not leak into the view it was deferred for. A type that resolves renders a
    /// single type, where a listing section name is exactly as wrong as it was before -- reported
    /// against the single-type pipeline, with that pipeline's sections offered.
    /// </summary>
    [Fact]
    public async Task Type_SingleType_ListingSectionName_IsStillRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests", "--library", TestAssemblyPath,
            "-S", "Classes", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Classes' not found", error, StringComparison.Ordinal);

        // Names the pipeline that rejected it, so the message is actionable rather than merely
        // negative -- and proves the single-type list is what was consulted.
        Assert.Contains(SectionNames.Baseclass, error, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no prefix matches there is no listing for a deferred select to belong to, so it is
    /// reported exactly as the preamble would have reported it. Holding the rejection must not turn
    /// into dropping it.
    /// </summary>
    [Fact]
    public async Task Type_UnresolvedTypeWithoutPrefixMatches_StillReportsTheDeferredSelect()
    {
        var (exit, _, error) = await RunAppAsync(
            "type", "Zqqxnomatch", "--library", TestAssemblyPath, "-S", "Classes", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Classes' not found", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name that is valid for neither pipeline is a plain typo and still fails in the preamble,
    /// keeping the fast rejection -- and the single-type suggestions -- for the case that cannot be
    /// a listing.
    /// </summary>
    /// <remarks>
    /// This is the gate for that claim, and for the guard that carries it: dropping the
    /// "resolves against the listing" test from <c>ShouldDeferSelectToListing</c> would defer every
    /// total failure, so a typo would announce a prefix browse it never performs and then offer the
    /// listing's sections. Asserting only the exit code and the "not found" text cannot see that --
    /// both survive the deferral, because a landing site rejects the typo either way. The two
    /// negative assertions below are what make the difference observable.
    /// </remarks>
    [Theory]
    [InlineData("Command")]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests")]
    public async Task Type_SelectValidForNeitherPipeline_FailsRegardlessOfWhatTheNameResolvesTo(string target)
    {
        var (exit, _, error) = await RunAppAsync(
            "type", target, "--library", TestAssemblyPath, "-S", "Zzznosuchsection", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Zzznosuchsection' not found", error, StringComparison.Ordinal);
        Assert.DoesNotContain("best-effort prefix matches", error, StringComparison.Ordinal);
        Assert.Contains("Baseclass", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every consumer of a deferred select has to resolve it, not just the one that renders the
    /// table. <c>--count</c> checks section arity in the preamble and discovery filters by the
    /// selected sections, so both would otherwise read the deferral's empty include set as "no
    /// sections" and answer about the wrong thing.
    /// </summary>
    [Fact]
    public async Task Type_PrefixBrowse_ListingSectionName_ReachesCountAndDiscovery()
    {
        var (countExit, countOutput, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "--count", "--tips", "q");

        Assert.Equal(0, countExit);

        // Agrees with the rows the same selection renders, so this cannot pass by counting a
        // different section or an unfiltered surface.
        var (rowsExit, rowsOutput, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "--tsv", "--tips", "q");
        Assert.Equal(0, rowsExit);
        var rowCount = rowsOutput.Split('\n').Count(l => l.Trim().Length > 0) - 1;
        Assert.Equal(rowCount, int.Parse(countOutput.Trim()));

        var (discoverExit, discoverOutput, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "-D", "--tips", "q");

        Assert.Equal(0, discoverExit);
        Assert.Contains("Classes", discoverOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Enums", discoverOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_PrefixBrowse_DiscoveryValidOnlyForListingIsDeferred()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "Command",
            "--library",
            TestAssemblyPath,
            "-D",
            "Classes",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("Kind", output, StringComparison.Ordinal);
        Assert.Contains("Type", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_ExactMatch_ListingOnlyDiscoveryIsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "type",
            "DotnetInspect.Cli.Tests.CommandExecutionTests",
            "--library",
            TestAssemblyPath,
            "-D",
            "Classes",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Contains(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            SectionNames.Baseclass,
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_NoPrefixMatch_DeferredDiscoveryIsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "type",
            "Zqqxnomatch",
            "--library",
            TestAssemblyPath,
            "-D",
            "Classes",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Contains(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_PrefixBrowse_DiscoveryUsesFilteredSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.IAsync",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            "Classes",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_PrefixBrowse_SharedDiscoveryUsesFilteredSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.Str",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            "Interfaces",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Section 'Interfaces' not found",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A deferred select must use the listing pipeline for both count-map ordering and reduction;
    /// otherwise it either retains the obsolete single-section rejection or emits one scalar total.
    /// </summary>
    [Fact]
    public async Task Type_PrefixBrowse_MultiSectionSelect_RendersCountMap()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes,Enums",
            "--count", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(["Classes", "Enums"], rows.Select(row => row.GetProperty("section").GetString()));
        Assert.All(rows, row => Assert.Equal(JsonValueKind.Number, row.GetProperty("count").ValueKind));
    }

    [Fact]
    public async Task Type_ExactMatch_MultiSectionSelect_RendersCountMap()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests", "--library", TestAssemblyPath,
            "-S", "Type Info,Methods", "--count", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(["Methods", "Type Info"], rows.Select(row => row.GetProperty("section").GetString()));
        Assert.All(rows, row => Assert.Equal(JsonValueKind.Number, row.GetProperty("count").ValueKind));
    }

    /// <summary>
    /// The preamble runs four selection checks -- --count arity, shape-projection arity, --print
    /// selection, and tabular arity -- and every one of them reads the include set. A deferred
    /// select leaves that set empty, so each check has to ask the listing pipeline what the select
    /// resolves to or it silently judges nothing. Only --count was made deferral-aware at first;
    /// the tabular check then let a two-section select through and rendered just the first table at
    /// exit 0, which the direct listing rejects. Pinned per flag so a regression names its own site.
    /// </summary>
    [Theory]
    [InlineData("--tsv")]
    [InlineData("--table")]
    [InlineData("--jsonl")]
    public async Task Type_PrefixBrowse_MultiSectionSelect_FailsTabularArityLikeTheDirectListing(string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes,API Info", format, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Selection matches 2 sections", error, StringComparison.Ordinal);
        Assert.DoesNotContain("kind\ttype", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A single-section select must still reach the renderer once the tabular check consults the
    /// listing pipeline, or the fix for the multi-section case would simply reject everything.
    /// </summary>
    [Fact]
    public async Task Type_PrefixBrowse_SingleSectionSelect_StillRendersTabular()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("kind\ttype", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The payload projections are not supported by the listing at all, so the deferred path must
    /// reach that diagnostic rather than the arity one. Judging the deferral's empty include set
    /// reported "requires -S/--select to match exactly one section" for a select that resolves to
    /// exactly one -- telling the user to narrow a selector that was never the problem.
    /// </summary>
    [Theory]
    [InlineData("--value")]
    [InlineData("--urls")]
    [InlineData("--paths")]
    [InlineData("--print")]
    public async Task Type_PrefixBrowse_PayloadProjection_ReportsTheListingReasonNotArity(string flag)
    {
        var (exit, _, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", flag, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("is not supported when listing types", error, StringComparison.Ordinal);
        Assert.DoesNotContain("exactly one section", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deferred select must actually narrow the rendered listing, not merely stop failing.
    /// A prefix whose matches are all one kind cannot show the difference -- selecting Classes and
    /// selecting nothing render the same single table -- so this uses a prefix that matches four
    /// kinds, where a dropped selector is visible as the other three sections surviving.
    /// </summary>
    [Theory]
    [InlineData("--all")]
    public async Task Type_PrefixBrowse_DeferredSelect_NarrowsAMultiKindListing(string flag)
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "Json", "--platform", "System.Text.Json", "-S", "Classes", flag, "--tips", "q");

        Assert.Equal(0, exit);

        var headings = SectionHeadings(output);
        Assert.Equal(["Classes"], headings);
    }

    /// <summary>
    /// The platform prefix browse renders a listing for what entered as a single-type request, so
    /// it needs the same re-resolution the local prefix browse does. It is reached by a different
    /// route -- the wide fallback, after the local lookup finds neither the type nor a prefix match
    /// -- so covering only the local browse would leave this one dropping the selector silently.
    /// </summary>
    [Fact]
    public async Task Type_PlatformPrefixBrowse_ListingSectionName_IsSelectable()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Immutabl", "-S", "Classes", "--tips", "q");

        Assert.Equal(0, exit);

        // Names the route, so that this silently becoming the local browse -- which has its own
        // coverage -- shows up as a failure rather than as duplicate coverage of one path.
        Assert.Contains("platform prefix matches", error, StringComparison.Ordinal);
        Assert.Equal(["Classes"], SectionHeadings(output));

        // A name valid for neither pipeline still fails on this route.
        var (bogusExit, _, bogusError) = await RunAppAsync(
            "type", "System.Collections.Immutabl", "-S", "Zzznosuchsection", "--tips", "q");
        Assert.Equal(1, bogusExit);
        Assert.Contains("Select value 'Zzznosuchsection' not found", bogusError, StringComparison.Ordinal);
    }
}
