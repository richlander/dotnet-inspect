using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text.Json.Serialization;

namespace ILInspector.Metadata;

[JsonConverter(typeof(JsonStringEnumConverter<ApiTypeLayout>))]
public enum ApiTypeLayout
{
    Auto = 0,
    Sequential = 8,
    Explicit = 16,
    Extended = 24,
}

public sealed record ApiModuleMemorySafetyFacts(
    Guid ModuleVersionId,
    MemorySafetyRulesResult Rules);

public sealed record ApiMemberMemorySafetyFacts(
    Guid ModuleVersionId,
    MemorySafetyMemberContractResult CallerContract,
    MemorySafetyPointerEvidence SignaturePointer);

[JsonConverter(typeof(JsonStringEnumConverter<ApiBackingStorageState>))]
public enum ApiBackingStorageState
{
    Unknown,
    Associated,
    Ambiguous,
}

[JsonConverter(typeof(JsonStringEnumConverter<ApiBackingStorageConvention>))]
public enum ApiBackingStorageConvention
{
    AutoProperty,
    FieldLikeEvent,
}

public sealed record ApiBackingFieldEvidence(
    int FieldToken,
    string MatchedName,
    bool IsStatic);

/// <summary>
/// Conventional backing-field matches, not proof of the original source
/// construct. Unknown is not evidence that the declaration has no storage.
/// </summary>
public sealed record ApiBackingStorageAssociation(
    Guid ModuleVersionId,
    ApiBackingStorageConvention Convention,
    ApiBackingStorageState State,
    ImmutableArray<ApiBackingFieldEvidence> Candidates);

internal static class ApiMemorySafetyFacts
{
    public static ApiMemberMemorySafetyFacts Read(
        MetadataReader reader,
        MemorySafetyMetadataIndex index,
        Guid moduleVersionId,
        EntityHandle member)
    {
        var contract = index.GetMemberContract(member);
        return new(
            moduleVersionId,
            contract,
            contract.Evidence.Pointer == MemorySafetyPointerEvidence.NotExamined
                ? PointerDetector.ReadMember(reader, member)
                : contract.Evidence.Pointer);
    }
}
