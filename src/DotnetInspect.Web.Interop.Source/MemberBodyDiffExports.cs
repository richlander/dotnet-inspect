using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Presentation;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using DotnetInspect.Web;
using ILInspector.Research;
using Markout;
using DotnetInspector.Queries;
using TsJsExport;
using static DotnetInspect.Web.BrowserOrdinaryWorkerJsonBudget;

namespace DotnetInspect.Web.Interop.Source;

[SupportedOSPlatform("browser")]
[JsExportJsonInput(nameof(QueryMemberBodyDiff), "requestJson", typeof(BrowserMemberBodyDiffRequest))]
[JsExportJsonOutput(nameof(QueryMemberBodyDiff), typeof(BrowserMemberBodyDiffResult))]
public static partial class SourceExports
{
    static readonly BrowserManagedOperationBridge MemberBodyDiffOperations = new();
    static readonly Dictionary<string, RetainedMemberBodyInventory> MemberBodyInventories = new(StringComparer.Ordinal);
    const int MaximumMemberBodyMembers = 10_000;
    const int MaximumMemberBodyInventories = 4;

    sealed record RetainedMemberBodyInventory(BrowserMemberBodyDiffRequest Request,
        InspectionEnvelope<MemberBodyDiffInventory> Inspection);

    [JSExport]
    public static string CancelMemberBodyDiff(string operationId, string reason)
        => JsonSerializer.Serialize(BrowserTypeSourceCancellation.From(
            MemberBodyDiffOperations.RequestCancellation(BrowserManagedOperationId.From(operationId),
                BrowserManagedOperationCancelReasons.Parse(reason))),
            BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation);

    [JSExport]
    public static async Task<string> QueryMemberBodyDiff(string operationId, string requestJson)
    {
        var result = await MemberBodyDiffOperations.RunAsync<BrowserMemberBodyDiffResult, string, string, object>(
            BrowserManagedOperationId.From(operationId), null, async (token, _) =>
            {
                BrowserSourceDiffProjection.AdmitRequest(requestJson);
                var request = JsonSerializer.Deserialize(requestJson, BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffRequest)
                    ?? throw new ArgumentException("A Member Body request is required.");
                ValidateMemberBodyRequest(request);
                return new BrowserManagedOperationBodyResult<BrowserMemberBodyDiffResult, string, string>.Succeeded(
                    await QueryMemberBodyDiffCore(request, token));
            }, error => new(error.Message, error.ToString()));
        BrowserMemberBodyDiffResult wire = result switch
        {
            BrowserManagedOperationResult<BrowserMemberBodyDiffResult, string, string>.Succeeded success => success.Value,
            BrowserManagedOperationResult<BrowserMemberBodyDiffResult, string, string>.Canceled canceled =>
                new("Canceled", null, null, BrowserManagedOperationCancelReasons.Format(canceled.Reason)),
            BrowserManagedOperationResult<BrowserMemberBodyDiffResult, string, string>.Failed failed =>
                new("Failed", null, null, failed.Error),
            _ => throw new InvalidOperationException("Unknown Member Body operation outcome."),
        };
        string json = JsonSerializer.Serialize(wire, BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffResult);
        using var parsed = JsonDocument.Parse(json);
        long characters = JsonStringifyCharacters(parsed.RootElement) + 2;
        long entries = CollectionEntries(parsed.RootElement) + OrdinaryWorkerResultTupleOverhead;
        if (characters > MaxOrdinaryWorkerTransportJsonCharacters || entries > MaxOrdinaryWorkerTransportCollectionEntries)
        {
            if (wire.Inventory is { } inventory) MemberBodyInventories.Remove(inventory.Id);
            return JsonSerializer.Serialize(new BrowserMemberBodyDiffResult("TooComplex", null, null,
                $"The complete Member Body result exceeds the Worker transport limits "
                + $"({characters} characters and {entries} collection entries; "
                + $"limits {MaxOrdinaryWorkerTransportJsonCharacters} and {MaxOrdinaryWorkerTransportCollectionEntries})."),
                BrowserMemberBodyDiffJsonContext.Default.BrowserMemberBodyDiffResult);
        }
        return json;
    }

    static void ValidateMemberBodyRequest(BrowserMemberBodyDiffRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CompileAssetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Generation);
        _ = BrowserFrameworkText.Require(request.Framework);
        if (!NuGet.Versioning.NuGetVersion.TryParse(request.BeforeVersion, out _)
            || !NuGet.Versioning.NuGetVersion.TryParse(request.AfterVersion, out _))
            throw new ArgumentException("Member Body requires two exact package versions.");
        if ((request.InventoryId is null) != (request.MemberId is null))
            throw new ArgumentException("An exact Member requires its inventory and Member identity.");
    }

    internal static async Task<BrowserMemberBodyDiffResult> QueryMemberBodyDiffCore(
        BrowserMemberBodyDiffRequest request, CancellationToken token)
    {
        RetainedMemberBodyInventory? retained = null;
        if (request.InventoryId is { } inventoryId)
        {
            if (!MemberBodyInventories.TryGetValue(inventoryId, out retained)
                || retained.Request != request with { InventoryId = null, MemberId = null })
                return new("Unavailable", null, null, "The exact Library comparison is no longer retained. Retry comparison.");
        }
        await using var beforeLease = await BrowserPackageWorkspace.OpenScopeAsync(
            request.PackageId, request.BeforeVersion, request.Framework, token);
        await using var afterLease = await BrowserPackageWorkspace.OpenScopeAsync(
            request.PackageId, request.AfterVersion, request.Framework, token);
        var beforeScope = beforeLease.Scope;
        var afterScope = afterLease.Scope;
        var oldCoordinate = beforeScope.Coordinates[0];
        var newCoordinate = afterScope.Coordinates[0];
        var oldAsset = oldCoordinate.Selection.FindAsset(request.CompileAssetId)
            ?? throw new ArgumentException("The exact Before compile asset is unavailable.");
        var newAsset = newCoordinate.Selection.FindAsset(request.CompileAssetId)
            ?? throw new ArgumentException("The exact After compile asset is unavailable.");
        var oldSurface = beforeScope.SurfaceParticipant(oldCoordinate, oldAsset);
        var newSurface = afterScope.SurfaceParticipant(newCoordinate, newAsset);
        var oldImplementation = beforeScope.ImplementationParticipant(oldSurface);
        var newImplementation = afterScope.ImplementationParticipant(newSurface);
        if (retained is not null)
        {
            var member = retained.Inspection.Content.Destinations.SingleOrDefault(member => member.Subject.Id == request.MemberId);
            if (member?.After is null) return new("Unavailable", null, null, "This Member has no current destination.");
            var inspection = beforeScope.UseImplementationParticipant(oldImplementation, (oldGroup, oldRoot) =>
                afterScope.UseImplementationParticipant(newImplementation, (newGroup, newRoot) =>
                    MemberBodyDiffInspection.ExecuteMember(oldGroup, oldRoot, newGroup, newRoot, member, token)));
            token.ThrowIfCancellationRequested();
            var document = inspection.Content.Document;
            using var owned = JsonDocument.Parse(MemberBodyDiffJson.Serialize(inspection.Content));
            return new("Available", null, ProjectMemberBodyDocument(document), null,
                new(owned.RootElement.Clone(), inspection.Share, inspection.Diagnostics));
        }
        var api = beforeScope.UseSurfaceParticipant(oldSurface, (oldGroup, oldRoot) =>
            afterScope.UseSurfaceParticipant(newSurface, (newGroup, newRoot) =>
                AssemblyContextApiComparisonQuery.Execute(oldGroup, oldRoot, newGroup, newRoot,
                    ApiSurfaceScope.Public, BrowserApiSurfacePolicy.Limits)));
        if (!api.IsComplete)
            return new("Unavailable", null, null, "The public API endpoints are unavailable or incomplete.");
        var inventoryInspection = beforeScope.UseImplementationParticipant(oldImplementation, (oldGroup, oldRoot) =>
            afterScope.UseImplementationParticipant(newImplementation, (newGroup, newRoot) =>
                MemberBodyDiffInspection.Execute(oldGroup, oldRoot, newGroup, newRoot, BrowserApiSurfacePolicy.Limits, token, api)));
        if (inventoryInspection.Content.Members.Count > MaximumMemberBodyMembers)
            return new("TooComplex", null, null, "The comparison exceeds 10,000 changed Member subjects.");
        token.ThrowIfCancellationRequested();
        string id = Guid.NewGuid().ToString("N");
        var inventory = ProjectMemberBodyInventory(id, inventoryInspection.Content);
        while (MemberBodyInventories.Count >= MaximumMemberBodyInventories)
            MemberBodyInventories.Remove(MemberBodyInventories.Keys.First());
        MemberBodyInventories.Add(id, new(request, inventoryInspection));
        using var content = JsonDocument.Parse(MemberBodyDiffJson.Serialize(inventoryInspection.Content));
        return new("Available", inventory, null, null,
            new(content.RootElement.Clone(), inventoryInspection.Share, inventoryInspection.Diagnostics));
    }

    internal static BrowserMemberBodyDiffInventory ProjectMemberBodyInventory(string id, MemberBodyDiffInventory inventory)
    {
        var types = inventory.Members.GroupBy(member => (member.After ?? member.Before)?.Type.ToEscapedFullName()
                ?? member.Subject.Id, StringComparer.Ordinal)
            .Select(group => new BrowserMemberBodyType(group.Key, group.First().Subject.TypeName ?? group.Key,
                group.Any(member => member.After is not null)
                    || inventory.Api.Comparison.Subjects.Any(type => type.Comparison.After?.Identifier == group.Key),
                [.. group.Select(member =>
                    new BrowserMemberBodyMember(member.Subject.Id, member.Subject.Display, member.Outcome,
                        [.. member.Implementation.SelectMany(item => item.Evidence).Select(evidence => evidence.Mechanism.ToString()).Distinct()],
                        member.After?.ProjectedAnchor.Fingerprint, member.After?.Selector.NormalizedSelector,
                        member.After?.MethodToken, member.IdentityFailure, member.After?.Type.ToEscapedFullName()))])).ToList();
        foreach (var removed in inventory.Api.Comparison.Subjects.Where(type => type.Comparison.After is null))
        {
            types.RemoveAll(type => type.Identity == removed.Comparison.Before!.Identifier);
            types.Add(new(removed.Comparison.Before!.Identifier, removed.Display, false,
                [.. removed.Comparison.Members.Select(member => new BrowserMemberBodyMember(
                    member.Relation.Identifier, member.Relation.Before!.Display, "Removed", [], null, null, null, null, null))]));
        }
        return new(id, inventory.Implementation.Coverage.IsComplete,
            [.. inventory.Implementation.Coverage.Mechanisms.Where(coverage => coverage.Requested).Select(coverage =>
                new BrowserMemberBodyCoverage(coverage.Mechanism.ToString(), coverage.EvaluatedSubjectCount,
                    coverage.ExactSubjectCount, coverage.ChangedSubjectCount, coverage.UnavailableSubjectCount,
                    coverage.IncompleteSubjectCount, coverage.FailedSubjectCount))], [.. types],
            [.. inventory.Destinations.Where(member => member.After is not null).Select(member =>
                new BrowserMemberBodyMember(member.Subject.Id, member.Subject.Display, member.Outcome, [],
                    member.After!.ProjectedAnchor.Fingerprint, member.After.Selector.NormalizedSelector,
                    member.After.MethodToken, member.IdentityFailure, member.After.Type.ToEscapedFullName()))]);
    }

    internal static BrowserMemberBodyDiffDocument ProjectMemberBodyDocument(AnnotatedSourceDiffDocument document)
        => new(document.Subject.Selector, [.. document.Media.Select(medium =>
        {
            BrowserSourceDiff? diff = null;
            string? capacity = null;
            try
            {
                if (medium.Comparison is { } comparison)
                {
                    var mapped = TextAnalysisDiffPresentation.CreateLabeledMappedTextDiff(comparison.Analysis,
                    comparison.Characterization, "Before", document.Before.Outcome == AnnotatedSourceDiffSideOutcomeKind.Absent ? TextDiffLineTerminator.Unknown : TextDiffLineTerminator.Absent,
                    "After", document.After.Outcome == AnnotatedSourceDiffSideOutcomeKind.Absent ? TextDiffLineTerminator.Unknown : TextDiffLineTerminator.Absent);
                    diff = BrowserSourceDiffProjection.Project(comparison.Analysis, mapped,
                        MemberSourceDiffStatistics.Create(comparison.Analysis));
                }
            }
            catch (BrowserSourceDiffCapacityException failure) { capacity = failure.Message; }
            return new BrowserMemberBodyMedium(medium.Medium.ToString(), diff,
                capacity ?? (medium.TooComplex is { } limit ? $"Too complex: {limit.Dimension} {limit.Actual} exceeds {limit.Maximum}." : null),
                document.Before.Document is { } oldDocument
                    ? string.Join("\n", medium.BeforeLines.Select(line => oldDocument.Text.Substring(line.Start, line.Length))) : null,
                document.After.Document is { } newDocument
                    ? string.Join("\n", medium.AfterLines.Select(line => newDocument.Text.Substring(line.Start, line.Length))) : null);
        })], document.Before.Outcome.ToString(), document.After.Outcome.ToString(),
            document.Before.Detail, document.After.Detail);

}
