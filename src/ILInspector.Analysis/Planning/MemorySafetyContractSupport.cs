using System.Reflection.Metadata;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>One MethodDef's caller contract and accessor association.</summary>
public sealed record MemorySafetyMethodContract(
    MemorySafetyMemberContractResult Contract,
    MemorySafetyAccessorAssociationResult Association);

/// <summary>
/// Exact memory-safety contract-support work in one source receipt, as
/// <c>docs/design/method-query-source.md#memory-safety-contract-support</c>
/// defines it.
/// </summary>
public sealed record MemorySafetyContractCoverage(
    bool Constructed,
    MemorySafetyMetadataWork ConstructionWork,
    MemorySafetyMetadataWork QueryWork,
    int Queries,
    MethodDefinitionHandleCoverage QueryingMethods)
{
    public static MemorySafetyContractCoverage Empty { get; } =
        new(
            Constructed: false,
            MemorySafetyMetadataWork.Empty,
            MemorySafetyMetadataWork.Empty,
            Queries: 0,
            MethodDefinitionHandleCoverage.Empty);
}

/// <summary>
/// The memory-safety contracts of same-image members, projected from one
/// receipted <see cref="MemorySafetyMetadataIndex"/>. It adds no correctness
/// logic: rules, contracts, associations, integrity validation, and budgets
/// are the index's.
/// </summary>
internal sealed class MemorySafetyContractLookup
{
    readonly MemorySafetyMetadataIndex _index;

    internal MemorySafetyContractLookup(MetadataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _index = MemorySafetyMetadataIndex.CreateReceipted(reader);
    }

    public MemorySafetyRulesResult Rules => _index.Rules;

    public MemorySafetyMetadataWork ConstructionWork =>
        _index.ConstructionWork!;

    public MemorySafetyMetadataWork RecordedWork => _index.RecordedWork!;

    /// <summary>The direct or associated contract of a same-image definition.</summary>
    public MemorySafetyMemberContractResult GetMemberContract(
        EntityHandle member) =>
        _index.GetMemberContract(member);

    /// <summary>A MethodDef's contract and the association it may inherit through.</summary>
    public MemorySafetyMethodContract GetMethodContract(
        MethodDefinitionHandle method) =>
        new(
            _index.GetMemberContract(method),
            _index.GetAccessorAssociation(method));
}

/// <summary>
/// Declared execution-scoped support that constructs its lookup at the first
/// query, so a declaring execution that never queries builds nothing.
/// </summary>
internal sealed class MemorySafetyContractSupport(MetadataReader reader)
{
    MemorySafetyContractLookup? _lookup;

    /// <summary>
    /// The lookup, constructed by this call when <paramref name="constructed"/>
    /// is true.
    /// </summary>
    public MemorySafetyContractLookup Acquire(out bool constructed)
    {
        constructed = _lookup is null;
        return _lookup ??= new MemorySafetyContractLookup(reader);
    }
}
