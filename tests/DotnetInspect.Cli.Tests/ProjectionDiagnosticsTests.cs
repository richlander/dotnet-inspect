using DotnetInspect.Cli.Output;
using Markout;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class ProjectionDiagnosticsTests
{
    // Mirrors the member detail schema: a graph section that carries the projected fields
    // alongside a companion table section that does not (e.g. Call Graph + Callers, where
    // a scope flag such as --bin implies -S Callers).
    private static DocumentSchema GraphAndTableSchema() =>
        new DocumentSchema()
            .Add("Call Graph", "field", "Fanin", "Depth", "Loop", "Root", "EvidenceIL")
            .Add("Callers", "column", "Caller", "Kind", "IL", "Token");

    [Fact]
    public async Task ValidateProjection_FieldResolvingInOneSection_SucceedsWithoutWarning()
    {
        var schema = GraphAndTableSchema();
        bool result = false;

        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            result = ProjectionDiagnostics.ValidateProjection(
                schema, ["Call Graph", "Callers"], fields: ["Fanin", "Depth"], columns: null);
            return Task.FromResult(0);
        });

        // Graph fields resolve in Call Graph, so the projection is valid even though the
        // companion Callers table lacks them — no error, no spurious warning.
        Assert.True(result);
        Assert.DoesNotContain("not found", error);
        Assert.DoesNotContain("No fields matched", error);
    }

    [Fact]
    public async Task ValidateProjection_FieldResolvingInNoSection_FailsWithError()
    {
        var schema = GraphAndTableSchema();
        bool result = true;

        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            result = ProjectionDiagnostics.ValidateProjection(
                schema, ["Call Graph", "Callers"], fields: ["Bogus"], columns: null);
            return Task.FromResult(0);
        });

        Assert.False(result);
        Assert.Contains("Warning: field 'Bogus' not found in section 'Call Graph'", error);
        Assert.Contains("Run -D \"Call Graph\" to list available fields.", error);
        Assert.Contains("No fields matched projection", error);
    }

    [Fact]
    public async Task ValidateProjection_MixedValidAndUnknown_WarnsOnUnknownButSucceeds()
    {
        var schema = GraphAndTableSchema();
        bool result = false;

        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            result = ProjectionDiagnostics.ValidateProjection(
                schema, ["Call Graph", "Callers"], fields: ["Fanin", "Bogus"], columns: null);
            return Task.FromResult(0);
        });

        // One valid graph field is enough to proceed; only the genuine typo is reported.
        Assert.True(result);
        Assert.Contains("Warning: field 'Bogus' not found in section", error);
        Assert.DoesNotContain("'Fanin'", error);
        Assert.DoesNotContain("No fields matched projection", error);
    }

    [Theory]
    [InlineData("Signed", false)]
    [InlineData(null, true)]
    public async Task DiagnoseProjected_WildcardUsesResolvedNames(
        string? presentName,
        bool expectsMissingNote)
    {
        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            ProjectionDiagnostics.DiagnoseProjected(
                ["Sign*"],
                presentName is null ? [] : [presentName],
                ["Signed", "Status"]);
            return Task.FromResult(0);
        });

        Assert.Equal(
            expectsMissingNote,
            error.Contains("field has no data: Sign*", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiagnoseProjected_OverlappingPatternsUseResolvedNames()
    {
        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            ProjectionDiagnostics.DiagnoseProjected(
                ["Field", "*"],
                ["Field", "Value"],
                ["Field", "Value"]);
            return Task.FromResult(0);
        });

        Assert.Empty(error);
    }

    [Fact]
    public async Task DiagnoseProjected_DoesNotUseAnotherSectionsEvidence()
    {
        var schema = new DocumentSchema()
            .Add("Expected", "field", "Status")
            .Add("Other", "field", "Status");
        var formatter = new RenderManifestFormatter(schema);
        formatter.BeginDocument(MarkoutWriterOptions.Default);
        formatter.FormatHeading(
            TextWriter.Null,
            2,
            "Other",
            context: null);
        formatter.FormatFields(
            TextWriter.Null,
            [new MarkoutField("Status", "available")],
            bold: false);

        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            ProjectionDiagnostics.DiagnoseProjected(
                ["Status"],
                formatter.Manifest,
                schema,
                "field",
                ["Expected"],
                fieldSectionsAsColumns: false);
            return Task.FromResult(0);
        });

        Assert.Contains(
            "Note: 1 field has no data: Status",
            error,
            StringComparison.Ordinal);
    }
}
