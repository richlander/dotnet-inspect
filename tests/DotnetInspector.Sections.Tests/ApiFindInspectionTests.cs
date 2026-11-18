using System.Text.Json;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using NuGetFetch;

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

    [Fact]
    public void SemanticRepresentationsRetainOwnerOrderAndSettlement()
    {
        FindSemanticResult result = SemanticResult(
            FindSemanticResultCompletion.Partial,
            failures: [new("runtime", "Runtime source failed.")],
            unsearched: ["remaining prefixes"]);
        InspectionDiagnostic diagnostic = new(
            "api-find/source",
            InspectionDiagnosticSeverity.Warning,
            "A source was unavailable.");

        InspectionEnvelope<ApiFindSemanticTable> table =
            ApiFindSemanticInspection.Table(result, [diagnostic]);
        InspectionEnvelope<ApiFindSemanticDocument> document =
            ApiFindSemanticInspection.Document(result, [diagnostic]);

        Assert.Equal(result.Findings, table.Content.Rows);
        Assert.Equal(result.Findings, document.Content.Findings);
        Assert.Same(result.Findings[0], table.Content.Rows[0]);
        Assert.Same(result.Findings[1], document.Content.Findings[1]);
        Assert.Equal(result.Completion, table.Content.Completion);
        Assert.Equal(result.Failures, document.Content.Failures);
        Assert.Equal(result.Unsearched, table.Content.Unsearched);
        Assert.True(table.Content.IsIncomplete);
        Assert.True(document.Content.IsIncomplete);
        Assert.Equal(table.Share, document.Share);
        Assert.Equal(table.Diagnostics, document.Diagnostics);
        Assert.Same(diagnostic, table.Diagnostics[0]);
    }

    [Fact]
    public void SemanticDocumentSerializationRetainsDistinctExactSources()
    {
        FindSemanticResult result = SemanticResult(
            FindSemanticResultCompletion.RowLimitReached,
            failures: [new("prefix", "A prefix source failed.")],
            unsearched: ["remaining prefix"]);
        InspectionDiagnostic diagnostic = new(
            "api-find/semantic/source",
            InspectionDiagnosticSeverity.Warning,
            "A source was unavailable.");
        InspectionEnvelope<ApiFindSemanticDocument> inspection =
            ApiFindSemanticInspection.Document(result, [diagnostic]);
        InspectionEnvelope<ApiFindSemanticTable> table =
            ApiFindSemanticInspection.Table(result, [diagnostic]);

        string json = JsonSerializer.Serialize(
            inspection,
            ApiFindSemanticInspectionJsonContext.Default
                .InspectionEnvelopeApiFindSemanticDocument);
        InspectionEnvelope<ApiFindSemanticDocument> roundTrip =
            Assert.IsType<InspectionEnvelope<ApiFindSemanticDocument>>(
                JsonSerializer.Deserialize(
                    json,
                    ApiFindSemanticInspectionJsonContext.Default
                        .InspectionEnvelopeApiFindSemanticDocument));
        string tableJson = JsonSerializer.Serialize(
            table,
            ApiFindSemanticInspectionJsonContext.Default
                .InspectionEnvelopeApiFindSemanticTable);
        InspectionEnvelope<ApiFindSemanticTable> roundTripTable =
            Assert.IsType<InspectionEnvelope<ApiFindSemanticTable>>(
                JsonSerializer.Deserialize(
                    tableJson,
                    ApiFindSemanticInspectionJsonContext.Default
                        .InspectionEnvelopeApiFindSemanticTable));

        Assert.Equal(
            FindSemanticResultCompletion.RowLimitReached,
            roundTrip.Content.Completion);
        Assert.Equal<FindSemanticFinding>(
            roundTrip.Content.Findings,
            roundTripTable.Content.Rows);
        Assert.True(roundTrip.Content.IsIncomplete);
        Assert.Equal<FindSemanticResultFailure>(
            result.Failures,
            roundTrip.Content.Failures);
        Assert.Equal<string>(
            result.Unsearched,
            roundTrip.Content.Unsearched);
        Assert.Equal("api-find/semantic/source",
            Assert.Single(roundTrip.Diagnostics).Code);
        Assert.Equal<InspectionDiagnostic>(
            roundTrip.Diagnostics,
            roundTripTable.Diagnostics);
        Assert.IsType<InspectionShare.NonProjectable>(roundTrip.Share);
        Assert.Collection(
            roundTrip.Content.Findings,
            finding =>
            {
                var type = Assert.IsType<FindSemanticFinding.Type>(finding);
                var package = Assert.IsType<
                    ExactLibrarySourceCoordinate.Package>(
                        type.Match.Declaration.Source.Coordinate);
                Assert.Equal(
                    Assert.IsType<FindSemanticFinding.Type>(
                            result.Findings[0])
                        .Match.Declaration.Source.Coordinate,
                    package);
                Assert.Equal("10.0.0", package.PackageCoordinate.Version);
                Assert.Equal(
                    "System.Text.Json.JsonSerializer",
                    type.Match.Declaration.Name.ToMetadataFullName());
                Assert.Equal(type.Match.Declaration.Source.Coordinate,
                    Assert.IsType<FindSemanticFindingIdentity.Type>(
                        type.Identity).Source);
            },
            finding =>
            {
                var member = Assert.IsType<FindSemanticFinding.Member>(finding);
                var package = Assert.IsType<
                    ExactLibrarySourceCoordinate.Package>(
                        member.Match.Declaration.Source.Coordinate);
                Assert.Equal(
                    "M:System.Text.Json.JsonSerializer.Serialize()",
                    member.Match.Declaration.Member.CanonicalSignature);
                Assert.Equal(
                    member.Match.Declaration.Member,
                    Assert.IsType<FindSemanticFindingIdentity.Member>(
                        member.Identity).Declaration);
                Assert.Equal(
                    Assert.IsType<FindSemanticFinding.Member>(
                            result.Findings[1])
                        .Match.Declaration.Source.Coordinate,
                    package);
            },
            finding =>
            {
                var type = Assert.IsType<FindSemanticFinding.Type>(finding);
                var platform = Assert.IsType<
                    ExactLibrarySourceCoordinate.Platform>(
                        type.Match.Declaration.Source.Coordinate);
                Assert.Equal(
                    Assert.IsType<FindSemanticFinding.Type>(
                            result.Findings[2])
                        .Match.Declaration.Source.Coordinate,
                    platform);
                Assert.Equal(PlatformFamily.DotNetRuntime, platform.Population.Family);
                Assert.Equal(
                    "System.Text.Json.JsonSerializer",
                    type.Match.Declaration.Name.ToMetadataFullName());
            });
    }

    [Fact]
    public void SemanticCompleteEmptyAndFiniteLimitAreNotExecutionDiagnostics()
    {
        var empty = new FindSemanticResultSession().Complete(
            FindSemanticResultCompletion.Exhausted);
        var limited = new FindSemanticResultSession().Complete(
            FindSemanticResultCompletion.CandidateLimitReached);

        InspectionEnvelope<ApiFindSemanticTable> emptyTable =
            ApiFindSemanticInspection.Table(empty);
        InspectionEnvelope<ApiFindSemanticTable> limitedTable =
            ApiFindSemanticInspection.Table(limited);

        Assert.Empty(emptyTable.Content.Rows);
        Assert.False(emptyTable.Content.IsIncomplete);
        Assert.Empty(emptyTable.Diagnostics);
        Assert.Empty(limitedTable.Content.Rows);
        Assert.True(limitedTable.Content.IsIncomplete);
        Assert.Empty(limitedTable.Diagnostics);
    }

    private static FindSemanticResult SemanticResult(
        FindSemanticResultCompletion completion,
        IEnumerable<FindSemanticResultFailure>? failures = null,
        IEnumerable<string>? unsearched = null)
    {
        var session = new FindSemanticResultSession();
        Assert.Equal(
            FindSemanticFindingPublication.Published,
            session.Publish(TypeMatch(package: true, memberOrder: 0)));
        Assert.Equal(
            FindSemanticFindingPublication.Published,
            session.Publish(MemberMatch()));
        Assert.Equal(
            FindSemanticFindingPublication.Published,
            session.Publish(TypeMatch(package: false, memberOrder: 1)));
        return session.Complete(completion, failures, unsearched);
    }

    private static MemberFindSemanticMatch MemberMatch()
    {
        FindSourceIdentity source = SourceIdentity(
            package: true,
            memberOrder: 0);
        MetadataTypeDefinitionName declaringType = TypeName();
        const string canonicalSignature =
            "M:System.Text.Json.JsonSerializer.Serialize()";
        string fingerprint =
            MemberAnchor.ComputeFingerprint(canonicalSignature);
        var anchor = new MemberAnchor(
            $"Serialize~{fingerprint}",
            canonicalSignature,
            fingerprint,
            declaringType.ToMetadataFullName(),
            "Serialize");
        MemberFindQuestion question = MemberFindQuestion.Create(
            ["Serialize"],
            FindVisibility.Public);
        return new(
            question.Patterns[0],
            MemberFindSemanticMatchKind.Direct,
            new(
                source,
                declaringType,
                anchor,
                declarationOrder: 0,
                memberOrder: 0),
            MemberName: "Serialize",
            DeclaringType: declaringType.ToMetadataFullName(),
            DeclaringNamespace: declaringType.Namespace,
            Kind: "method",
            Signature: "Serialize()",
            ReturnType: "string");
    }

    private static TypeFindSemanticMatch TypeMatch(
        bool package,
        int memberOrder)
    {
        FindSourceIdentity source = SourceIdentity(package, memberOrder);
        MetadataTypeDefinitionName declaration = TypeName();
        return new(
            TypeFindPattern.Create(
                0,
                "System.Text.Json.JsonSerializer"),
            "System.Text.Json.JsonSerializer",
            TypeFindSemanticMatchKind.Exact,
            Similarity: 1,
            new(
                source,
                declaration,
                new Guid(
                    package
                        ? "11111111-1111-1111-1111-111111111111"
                        : "22222222-2222-2222-2222-222222222222"),
                declarationOrder: 0),
            AssemblyTypeDeclarationKind.Definition,
            AssemblyTypeDefinitionKind.Class,
            DiscoveryAttributes: null);
    }

    private static FindSourceIdentity SourceIdentity(
        bool package,
        int memberOrder)
    {
        var assemblyIdentity = new AssemblyReferenceIdentity(
            "System.Text.Json",
            new Version(10, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        var libraryIdentity =
            new ManagedMetadataIdentity.Assembly(assemblyIdentity);
        ExactLibrarySourceCoordinate coordinate = package
            ? new ExactLibrarySourceCoordinate.Package(
                PackageSourceCoordinate.Create(
                    "System.Text.Json",
                    "10.0.0"),
                libraryIdentity)
            : new ExactLibrarySourceCoordinate.Platform(
                new(PlatformFamily.DotNetRuntime),
                libraryIdentity);
        return new FindSourceIdentity(
            coordinate,
            package
                ? AssemblyResolutionProvenance.Package(
                    "System.Text.Json",
                    "10.0.0",
                    "net10.0",
                    rid: null,
                    assetPath:
                        "lib/net10.0/System.Text.Json.dll")
                : AssemblyResolutionProvenance.Platform(
                    "Microsoft.NETCore.App",
                    "10.0.0",
                    "runtime"),
            package
                ? new FindPackageSourceRequest("net10.0", null)
                : null,
            contextOrder: memberOrder,
            memberOrder,
            assemblyIdentity);
    }

    private static MetadataTypeDefinitionName TypeName() =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "System.Text.Json",
                ["JsonSerializer"]))
            .Name;
}
