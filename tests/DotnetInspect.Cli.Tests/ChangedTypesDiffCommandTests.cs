using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Diff;
using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// The exact-name <c>diff -S "Changed Types"</c> section: the CLI consumer of
/// Finding Diff Any's Library route (finding-diff-any.md#adoption, step 9).
/// </summary>
[Collection("Console")]
public sealed class ChangedTypesDiffCommandTests
{
    const string Section = "Changed Types";

    [Fact]
    public async Task Tsv_IsOneRowPerReportedTypeInProducerOrder()
    {
        var (exitCode, output, error) = await Run("-S", Section);

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("type\tapi\tbody\tidentifier", lines[0]);
        Assert.Equal(
            [
                .. ReportedTypes().Select(static type => string.Join(
                    '\t',
                    type.FullName,
                    type.Api.ToString(),
                    type.Body.ToString(),
                    type.Identifier)),
            ],
            lines[1..]);
        Assert.Contains(
            "FindingDiffAnyFixture.BodyOnly\tUnchanged\tChanged\tFindingDiffAnyFixture.BodyOnly",
            lines);
        Assert.Contains(
            "FindingDiffAnyFixture.Outer.Inner\tUnchanged\tChanged\tFindingDiffAnyFixture.Outer+Inner",
            lines);
        Assert.Contains(
            "FindingDiffAnyFixture.AbstractMemberAdded\tChanged\tUnchanged\tFindingDiffAnyFixture.AbstractMemberAdded",
            lines);
        Assert.DoesNotContain(lines, static line =>
            line.StartsWith("FindingDiffAnyFixture.Unchanged\t", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Json_IsTheRouteDocumentWithItsReceipt()
    {
        var (exitCode, output, error) = await Run("-S", Section, "--json");

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement root = json.RootElement;
        Assert.Equal("Compared", root.GetProperty("outcome").GetString());
        JsonElement comparison = root.GetProperty("comparison");
        Assert.Equal("ILInspector.Metadata.FindingDiffAny", comparison.GetProperty("name").GetString());
        Assert.Equal("ApiAndBody", comparison.GetProperty("axes").GetString());
        FindingDiffAnyTypeState[] reported = ReportedTypes();
        JsonElement[] rows = [.. root.GetProperty("rows").EnumerateArray()];
        Assert.Equal(
            reported.Select(static type => type.Identifier),
            rows.Select(static row => row.GetProperty("identifier").GetString()));
        JsonElement bodyOnly = Assert.Single(rows, static row =>
            row.GetProperty("type").GetString() == "FindingDiffAnyFixture.BodyOnly");
        Assert.Equal("Unchanged", bodyOnly.GetProperty("api").GetString());
        Assert.Equal("Changed", bodyOnly.GetProperty("body").GetString());
        JsonElement counts = root.GetProperty("counts");
        Assert.Equal(reported.Length, counts.GetProperty("reportedTypes").GetInt32());
        Assert.Equal(
            reported.Count(static type => type.Api == FindingDiffAnyState.Changed),
            counts.GetProperty("apiChanged").GetInt32());
        Assert.Equal(
            reported.Count(static type => type.Body == FindingDiffAnyState.Changed),
            counts.GetProperty("bodyChanged").GetInt32());
        Assert.Equal(
            Compare().Types.Length,
            root.GetProperty("receipt").GetProperty("comparedTypes").GetInt32());
    }

    [Fact]
    public async Task Markdown_StatesWhatWasComparedAndListsTheRows()
    {
        var (exitCode, output, error) = await Run("-S", Section, "--markdown");

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        int reported = ReportedTypes().Length;
        int compared = Compare().Types.Length;
        Assert.StartsWith("# Changed Types: ILInspector.Metadata.FindingDiffAny", output);
        Assert.Contains("| Axes | API and Body |", output);
        Assert.Contains($"| Compared | {compared} |", output);
        Assert.Contains($"{reported} of {compared} compared Types differ.", output);
        Assert.Contains("## Changed Types", output);
        Assert.Contains("| Type | API | Body | Identifier |", output);
        Assert.Contains(
            "| FindingDiffAnyFixture.BodyOnly | Unchanged | Changed | FindingDiffAnyFixture.BodyOnly |",
            output);
    }

    [Theory]
    [InlineData("-S", "Changes")]
    [InlineData("-S", "@Diff")]
    public async Task OtherSections_AreRejected(string option, string value)
    {
        var (exitCode, output, error) = await Run("-S", Section, option, value);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("Changed Types must be selected by itself", error);
    }

    [Theory]
    [InlineData("--all")]
    [InlineData("--breaking")]
    [InlineData("--type", "BodyOnly")]
    [InlineData("--analysis", "api")]
    public async Task FiltersAndAnalyses_AreRejected(params string[] options)
    {
        var (exitCode, output, error) = await Run(["-S", Section, .. options]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("Changed Types compares every Type of one Library pair", error);
    }

    [Fact]
    public async Task Tree_IsRejected()
    {
        var (exitCode, output, error) = await Run("-S", Section, "--tree");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("Changed Types does not offer a Hierarchy representation.", error);
    }

    [Fact]
    public void Section_IsExactNameOnlyAndOutsideTheDiffCategory()
    {
        SectionCatalog<DiffDiscoveryModel> catalog = DiffSections.SectionCatalog;

        Assert.Contains(Section, DiffSections.ExactOnlySections);
        Assert.Contains(Section, catalog.SelectableSectionNames);
        Assert.DoesNotContain(Section, catalog.SelectionCategoryMap["@Diff"]);
        Assert.Equal(SectionShape.Table, catalog.SectionShapes[Section]);
        Assert.Same(DiffSectionDescriptions.ChangedTypes, catalog.SectionDescriptions[Section]);
    }

    [Fact]
    public void CliBinding_ConsumesTheFindingDiffAnyRoute()
    {
        InspectionCapabilityCatalog catalog = FindingDiffAnyCommandCapability.Catalog;

        Assert.Same(
            FindingDiffAnyLibraryCapability.Route,
            FindingDiffAnyCommandCapability.Binding.Route);
        Assert.Equal(InspectionConsumerKind.Cli, FindingDiffAnyCommandCapability.Binding.Descriptor.Kind);
        Assert.Equal("diff -S \"Changed Types\"", FindingDiffAnyCommandCapability.Binding.Descriptor.Gesture);
        Assert.Same(FindingDiffAnyCommandCapability.Binding, Assert.Single(catalog.Bindings));
        // The CLI binding closes the CLI gap; the Browser gap is closed by the
        // Browser's own composition.
        Assert.Equal(
            InspectionConsumerKind.Browser,
            Assert.Single(catalog.AdoptionGaps).ConsumerKind);
    }

    static FindingDiffAnyResult Compare()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        return FindingDiffAny.Compare(before, after);
    }

    static FindingDiffAnyTypeState[] ReportedTypes()
        => [.. Compare().Types.Where(FindingDiffAnyLibraryInspection.IsReported)];

    static Task<(int ExitCode, string Output, string Error)> Run(params string[] options)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        string[] args = CommandLineBuilder.PreprocessArgs(
            ["diff", "--library", $"{pair.OldAssemblyPath()}..{pair.NewAssemblyPath()}", .. options]);
        return ConsoleCapture.RunAsync(async () =>
            await CommandLineBuilder.CreateRootCommand().Parse(args).InvokeAsync());
    }
}
