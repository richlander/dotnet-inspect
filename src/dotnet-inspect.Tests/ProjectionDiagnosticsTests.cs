using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Tests;

public class ProjectionDiagnosticsTests
{
    // Mirrors the member detail schema: a graph section that carries the projected fields
    // alongside a companion table section that does not (e.g. Caller Graph + Callers, where
    // a scope flag such as --bin implies -S Callers).
    private static DocumentSchema GraphAndTableSchema() =>
        new DocumentSchema()
            .Add("Caller Graph", "field", "Fanin", "Depth", "Loop", "Root", "EvidenceIL")
            .Add("Callers", "column", "Caller", "Kind", "IL", "Token");

    [Fact]
    public async Task ValidateProjection_FieldResolvingInOneSection_SucceedsWithoutWarning()
    {
        var schema = GraphAndTableSchema();
        bool result = false;

        var (_, _, error) = await ConsoleCapture.RunAsync(() =>
        {
            result = ProjectionDiagnostics.ValidateProjection(
                schema, ["Caller Graph", "Callers"], fields: ["Fanin", "Depth"], columns: null);
            return Task.FromResult(0);
        });

        // Graph fields resolve in Caller Graph, so the projection is valid even though the
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
                schema, ["Caller Graph", "Callers"], fields: ["Bogus"], columns: null);
            return Task.FromResult(0);
        });

        Assert.False(result);
        Assert.Contains("warning: field 'Bogus' not found in section 'Caller Graph'", error);
        Assert.Contains("Run -D \"Caller Graph\" to list available fields.", error);
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
                schema, ["Caller Graph", "Callers"], fields: ["Fanin", "Bogus"], columns: null);
            return Task.FromResult(0);
        });

        // One valid graph field is enough to proceed; only the genuine typo is reported.
        Assert.True(result);
        Assert.Contains("warning: field 'Bogus' not found in section", error);
        Assert.DoesNotContain("'Fanin'", error);
        Assert.DoesNotContain("No fields matched projection", error);
    }
}
