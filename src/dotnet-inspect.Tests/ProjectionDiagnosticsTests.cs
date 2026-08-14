using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Tests;

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

    [Fact]
    public async Task DiagnoseProjectedJson_MachineColumnSatisfiesDisplayName()
    {
        var schema = new DocumentSchema().Add("Methods", "column", "Return Type");

        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            ProjectionDiagnostics.DiagnoseProjectedJson(
                fields: null,
                columns: ["Return Type"],
                emittedFields: [],
                emittedColumns: ["return_type"],
                schema);
            return Task.FromResult(0);
        });

        Assert.Empty(error);
    }

    [Fact]
    public async Task DiagnoseProjectedJson_WildcardAndValueOnlyFieldUseEvidence()
    {
        var schema = new DocumentSchema().Add("Type Info", "field", "Kind", "Version");

        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            ProjectionDiagnostics.DiagnoseProjectedJson(
                fields: ["Ki*"],
                columns: ["Value"],
                emittedFields: ["Kind"],
                emittedColumns: ["value"],
                schema);
            return Task.FromResult(0);
        });

        Assert.Empty(error);
    }

    [Fact]
    public async Task DiagnoseProjectedJson_ReportsMissingFieldAndColumn()
    {
        var schema = new DocumentSchema()
            .Add("API Info", "field", "Types", "Version")
            .Add("Classes", "column", "Field", "Bogus");

        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            ProjectionDiagnostics.DiagnoseProjectedJson(
                fields: ["Version"],
                columns: ["Field", "Bogus"],
                emittedFields: [],
                emittedColumns: ["field"],
                schema);
            return Task.FromResult(0);
        });

        Assert.Contains("1 field has no data: Version", error, StringComparison.Ordinal);
        Assert.Contains("1 column has no data: Bogus", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Field", error, StringComparison.Ordinal);
    }
}
