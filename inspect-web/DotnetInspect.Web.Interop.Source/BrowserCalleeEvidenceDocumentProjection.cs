using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Decompiler;

namespace DotnetInspect.Web.Interop.Source;

internal sealed record BrowserCalleeEvidenceDocumentAdmission(
    int? DocumentId,
    string? UnavailableReason);

internal sealed record BrowserCalleeEvidenceDocumentProjectionResult(
    BrowserAnnotatedSourceFindingEvidenceDocument[] Documents,
    IReadOnlyDictionary<MethodIdentity, BrowserCalleeEvidenceDocumentAdmission>
        Admissions);

internal sealed record BrowserCalleeEvidenceDocumentReference(
    int? DocumentId,
    int[] NodeIds,
    string? UnavailableReason);

internal static class BrowserCalleeEvidenceDocumentProjection
{
    // The ordinary worker admits 16,777,216 JSON characters. Reserve half for
    // the caller document, Finding rows, targets, and envelope overhead.
    internal const int MaxDocumentJsonCharacters = 8_388_608;

    internal static BrowserCalleeEvidenceDocumentProjectionResult Project(
        IReadOnlyList<AssemblyMemberFindingEvidence> evidence,
        int maximumDocumentJsonCharacters = MaxDocumentJsonCharacters)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumDocumentJsonCharacters);

        var documents =
            new List<BrowserAnnotatedSourceFindingEvidenceDocument>();
        var memberOrder = new List<MethodIdentity>();
        var seenMembers = new HashSet<MethodIdentity>();
        var admissions =
            new Dictionary<
                MethodIdentity,
                BrowserCalleeEvidenceDocumentAdmission>();
        var serializedByMember =
            new Dictionary<
                MethodIdentity,
                (AnnotatedSourceDocument Source, JsonElement Serialized)>();
        long retainedCharacters = 0;

        foreach (AssemblyMemberFindingEvidence row in evidence)
        {
            if (seenMembers.Add(row.Member))
                memberOrder.Add(row.Member);

            if (row.SourceDocument is not { } sourceDocument)
                continue;

            if (serializedByMember.TryGetValue(
                row.Member,
                out (AnnotatedSourceDocument Source, JsonElement Serialized)
                    existing))
            {
                if (ReferenceEquals(existing.Source, sourceDocument))
                    continue;

                JsonElement repeated =
                    BrowserAnnotatedSource.SerializeDocument(sourceDocument)!.Value;
                if (!JsonElement.DeepEquals(existing.Serialized, repeated))
                {
                    throw new InvalidOperationException(
                        $"Callee evidence for '{row.Member}' produced inconsistent source documents.");
                }
                continue;
            }

            JsonElement serialized =
                BrowserAnnotatedSource.SerializeDocument(sourceDocument)!.Value;
            serializedByMember.Add(
                row.Member,
                (sourceDocument, serialized));
        }

        foreach (MethodIdentity member in memberOrder)
        {
            if (!serializedByMember.TryGetValue(member, out var document))
                continue;

            JsonElement serialized = document.Serialized;
            int documentCharacters = serialized.GetRawText().Length;
            if (retainedCharacters + documentCharacters
                > maximumDocumentJsonCharacters)
            {
                admissions.Add(
                    member,
                    new BrowserCalleeEvidenceDocumentAdmission(
                        DocumentId: null,
                        $"Callee evidence document omitted because its "
                            + $"{documentCharacters} JSON characters would exceed "
                            + $"the aggregate Browser/Wasm document limit of "
                            + $"{maximumDocumentJsonCharacters}."));
                continue;
            }

            int documentId = documents.Count;
            documents.Add(
                new BrowserAnnotatedSourceFindingEvidenceDocument(
                    documentId,
                    serialized));
            admissions.Add(
                member,
                new BrowserCalleeEvidenceDocumentAdmission(
                    documentId,
                    UnavailableReason: null));
            retainedCharacters += documentCharacters;
        }

        return new BrowserCalleeEvidenceDocumentProjectionResult(
            [.. documents],
            admissions);
    }

    internal static BrowserCalleeEvidenceDocumentReference Reference(
        AssemblyMemberFindingEvidence evidence,
        BrowserCalleeEvidenceDocumentProjectionResult projection)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(projection);
        if (evidence.SourceDocument is null)
        {
            return new BrowserCalleeEvidenceDocumentReference(
                DocumentId: null,
                [.. evidence.NodeIds],
                evidence.UnavailableReason);
        }
        if (!projection.Admissions.TryGetValue(
            evidence.Member,
            out BrowserCalleeEvidenceDocumentAdmission? admission))
        {
            throw new InvalidOperationException(
                $"Callee evidence for '{evidence.Member}' has no document admission.");
        }

        string? unavailableReason =
            admission.UnavailableReason is { } budgetReason
                ? evidence.UnavailableReason is { } originalReason
                    ? $"{budgetReason} {originalReason}"
                    : budgetReason
                : evidence.UnavailableReason;
        return new BrowserCalleeEvidenceDocumentReference(
            admission.DocumentId,
            admission.UnavailableReason is null
                ? [.. evidence.NodeIds]
                : [],
            unavailableReason);
    }
}
