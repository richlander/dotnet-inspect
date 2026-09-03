using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Research;

namespace DotnetInspector.Output;

internal sealed record MemberFindingCensusEnvelope(
    string FactCensusReceipt,
    MemberFindingCensusFact[] Facts,
    JsonElement AnnotatedSourceDocument,
    MemberSourceFactInstance[] SourceFactInstances);

internal sealed record MemberFindingCensusFact(
    string Member,
    int? ILOffset,
    int? CSharpLine,
    string Anchor,
    string Category,
    string Id,
    string? Detail,
    string Conditionality,
    int? InstanceKey);

internal sealed record MemberSourceFactInstance(
    int FactId,
    int InstanceKey);

internal static class MemberFindingCensus
{
    public static MemberFindingCensusEnvelope Create(
        FindingCensusReceipt? receipt,
        IReadOnlyList<ResearchViews.FactRow>? facts,
        AnnotatedSourceDocument document,
        IReadOnlyList<ResearchViews.AnnotatedSourceFactIdentity>? sourceFactIdentities)
    {
        if (receipt is not { IsDefault: false } censusReceipt)
            throw new InvalidOperationException("Finding Census produced no non-default receipt.");
        if (facts is null)
            throw new InvalidOperationException("Finding Census produced no Facts projection.");
        if (sourceFactIdentities is null)
            throw new InvalidOperationException(
                "Finding Census produced no Annotated Source identity sidecar.");

        var factKeys = new HashSet<int>();
        var projectedFacts = new MemberFindingCensusFact[facts.Count];
        for (int index = 0; index < facts.Count; index++)
        {
            ResearchViews.FactRow fact = facts[index];
            bool hasReceipt = fact.CensusReceipt is not null;
            bool hasKey = fact.InstanceKey is not null;
            if (hasReceipt != hasKey)
            {
                throw new InvalidOperationException(
                    $"Finding Census Facts row {index} carries an incomplete identity.");
            }

            int? keyValue = null;
            if (fact.CensusReceipt is { } factReceipt
                && fact.InstanceKey is { } factKey)
            {
                if (factReceipt != censusReceipt)
                {
                    throw new InvalidOperationException(
                        $"Finding Census Facts row {index} carries a different receipt.");
                }
                if (factKey.IsDefault || !factKeys.Add(factKey.Value))
                {
                    throw new InvalidOperationException(
                        $"Finding Census Facts row {index} carries an invalid or duplicate instance key.");
                }
                keyValue = factKey.Value;
            }

            projectedFacts[index] = new MemberFindingCensusFact(
                fact.Member,
                fact.ILOffset,
                fact.CSharpLine,
                fact.Anchor,
                fact.Category,
                fact.Id,
                fact.Detail,
                fact.Conditionality,
                keyValue);
        }

        var bodyFactIds = document.Facts
            .Where(static fact => fact.Origin == AnnotatedSourceFactOrigin.Body)
            .Select(static fact => fact.Id)
            .ToHashSet();
        var sourceFactIds = new HashSet<int>();
        var sourceKeys = new HashSet<int>();
        var projectedIdentities =
            new MemberSourceFactInstance[sourceFactIdentities.Count];
        for (int index = 0; index < sourceFactIdentities.Count; index++)
        {
            ResearchViews.AnnotatedSourceFactIdentity identity =
                sourceFactIdentities[index];
            if (identity.CensusReceipt != censusReceipt)
            {
                throw new InvalidOperationException(
                    $"Finding Census source identity {index} carries a different receipt.");
            }
            if (identity.InstanceKey.IsDefault
                || !sourceKeys.Add(identity.InstanceKey.Value))
            {
                throw new InvalidOperationException(
                    $"Finding Census source identity {index} carries an invalid or duplicate instance key.");
            }
            if (!sourceFactIds.Add(identity.FactId)
                || !bodyFactIds.Contains(identity.FactId))
            {
                throw new InvalidOperationException(
                    $"Finding Census source identity {index} carries an invalid or duplicate fact id.");
            }

            projectedIdentities[index] = new MemberSourceFactInstance(
                identity.FactId,
                identity.InstanceKey.Value);
        }

        if (!bodyFactIds.SetEquals(sourceFactIds))
        {
            throw new InvalidOperationException(
                "Finding Census source identities do not cover the document body facts.");
        }
        if (!factKeys.SetEquals(sourceKeys))
        {
            throw new InvalidOperationException(
                "Finding Census Facts and Annotated Source identities do not describe the same instances.");
        }

        JsonElement serializedDocument = JsonSerializer.SerializeToElement(
            document,
            AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument);
        return new MemberFindingCensusEnvelope(
            censusReceipt.ToString(),
            projectedFacts,
            serializedDocument,
            projectedIdentities);
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemberFindingCensusEnvelope))]
internal partial class MemberFindingCensusJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemberFindingCensusEnvelope))]
internal partial class MemberFindingCensusCompactJsonContext : JsonSerializerContext;
