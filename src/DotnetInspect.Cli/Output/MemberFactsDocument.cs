using System.Collections.Immutable;
using System.Text.Json.Serialization;

using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspect.Cli.Output;

internal sealed record MemberFactsDocument(
    string Member,
    ImmutableArray<MemberFactDocument> Facts)
{
    internal static MemberFactsDocument Create(
        string member,
        IReadOnlyList<ResearchViews.FactRow> facts)
        => new(
            member,
            [.. facts.Select(MemberFactDocument.Create)]);
}

internal sealed record MemberFactDocument(
    int? IlOffset,
    int? CSharpLine,
    string Anchor,
    string Category,
    string Id,
    string? Detail,
    string Conditionality,
    string? CensusReceipt,
    int? InstanceKey,
    MemberFactCalleeEvidenceDocument? CalleeEvidence)
{
    internal static MemberFactDocument Create(
        ResearchViews.FactRow fact)
        => new(
            fact.ILOffset,
            fact.CSharpLine,
            fact.Anchor,
            fact.Category,
            fact.Id,
            fact.Detail,
            fact.Conditionality,
            fact.CensusReceipt?.ToString(),
            fact.InstanceKey?.Value,
            fact.Evidence is null
                ? null
                : MemberFactCalleeEvidenceDocument.Create(
                    fact.Evidence));
}

internal sealed record MemberFactCalleeEvidenceDocument(
    MemberFactMethodDocument Subject,
    string State,
    ImmutableArray<MemberFactEvidenceLocationDocument> Locations)
{
    internal static MemberFactCalleeEvidenceDocument Create(
        ResearchFindingEvidence evidence)
        => new(
            MemberFactMethodDocument.Create(evidence.Subject),
            StateName(evidence.State),
            [.. evidence.Locations.Select(
                MemberFactEvidenceLocationDocument.Create)]);

    internal static string StateName(
        ResearchFindingEvidenceState state)
        => state switch
        {
            ResearchFindingEvidenceState.Instruction => "instruction",
            ResearchFindingEvidenceState.Method => "method",
            ResearchFindingEvidenceState.InstructionUnavailable =>
                "instruction-unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Unknown Research Finding evidence state."),
        };
}

internal sealed record MemberFactEvidenceLocationDocument(
    MemberFactMethodDocument Method,
    int? IlOffset)
{
    internal static MemberFactEvidenceLocationDocument Create(
        ResearchEvidenceLocation location)
        => new(
            MemberFactMethodDocument.Create(location.Method),
            location.ILOffset);
}

internal sealed record MemberFactMethodDocument(
    string Assembly,
    Guid ModuleVersionId,
    int MetadataToken,
    string DeclaringType,
    string Name,
    ImmutableArray<string> ParameterTypes,
    string ReturnType,
    int GenericArity,
    bool IsStatic)
{
    internal static MemberFactMethodDocument Create(
        MethodIdentity method)
        => new(
            method.AssemblyName,
            method.ModuleVersionId,
            method.MetadataToken,
            method.DeclaringType.ToQualifiedDisplayString(),
            method.Name,
            [.. method.ParameterTypes.Select(static parameter =>
                parameter.ToQualifiedDisplayString())],
            method.ReturnType.ToQualifiedDisplayString(),
            method.GenericArity,
            method.IsStatic);
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemberFactsDocument))]
internal partial class MemberFactsJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemberFactsDocument))]
internal partial class MemberFactsCompactJsonContext : JsonSerializerContext;
