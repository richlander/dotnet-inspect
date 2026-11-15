using System.Text.Json;

namespace DotnetInspector.Sections.Tests;

public class ApiFindInspectionTests
{
    [Fact]
    public void RepresentationsRetainFindingOrderAndKeepPatternSettlementOutOfRows()
    {
        var packageType = new TypeFindResult
        {
            Type = "JsonSerializer", Source = "System.Text.Json",
            SourceVersion = "10.0.0", Match = TypeFindMatchKind.Direct,
        };
        var platformType = packageType with { Source = "runtime", SourceVersion = null };
        var member = new MemberFindResult
        {
            DeclaringType = "System.Text.Json.JsonSerializer",
            Member = "Serialize", Match = MemberFindMatchKind.Direct,
        };
        TypeFindResult[] types =
        [
            packageType,
            new() { Pattern = "Missing", Match = TypeFindMatchKind.NotFound },
            platformType,
        ];

        var table = ApiFindInspection.Table(types, [member], false);
        var document = ApiFindInspection.Document(types, [member], ["Missing"], false);

        Assert.Equal<ApiFindRow>([member, packageType, platformType], table.Content.Rows);
        Assert.Equal(table.Content.Rows, document.Content.Rows);
        Assert.Same(member, document.Content.Rows[0]);
        Assert.Equal(["Missing"], document.Content.UnmatchedPatterns);
        Assert.Equal(table.Share, document.Share);
        Assert.Empty(table.Diagnostics);

        string json = JsonSerializer.Serialize(
            document.Content, ApiFindDocumentJsonContext.Default.ApiFindDocument);
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement rows = parsed.RootElement.GetProperty("rows");
        Assert.Equal("member", rows[0].GetProperty("finding_kind").GetString());
        Assert.Equal("type", rows[1].GetProperty("finding_kind").GetString());
        Assert.Equal("Direct", rows[0].GetProperty("match").GetString());
        Assert.False(parsed.RootElement.GetProperty("is_incomplete").GetBoolean());
    }

    [Fact]
    public void EmptyPartialAnswerRetainsIncompletenessAcrossRepresentations()
    {
        InspectionDiagnostic failure = new(
            "locator/source", InspectionDiagnosticSeverity.Warning,
            "A source could not be inspected.");
        var table = ApiFindInspection.Table([], [], true, [failure]);
        var document = ApiFindInspection.Document([], [], [], true, [failure]);

        Assert.Empty(table.Content.Rows);
        Assert.Empty(document.Content.Rows);
        Assert.True(table.Content.IsIncomplete);
        Assert.True(document.Content.IsIncomplete);
        Assert.Equal(table.Diagnostics, document.Diagnostics);
        Assert.Same(failure, table.Diagnostics[0]);
        Assert.Equal("api-find/incomplete", table.Diagnostics[1].Code);
        Assert.IsType<InspectionShare.NonProjectable>(document.Share);
    }
}
