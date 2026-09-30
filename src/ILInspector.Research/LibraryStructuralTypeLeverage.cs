using System.Collections.Immutable;

using ILInspector.Metadata;
using Inspector.Graph;

namespace ILInspector.Research;

public static class LibraryStructuralSalience
{
    public const string CurrentMethodologyVersion =
        "structural-salience.v1";
    public const int MinimumDesignationDegree = 3;
}

public enum LibraryStructuralSalienceEvidenceMode
{
    Signature,
}

public enum LibraryStructuralTypeRole
{
    Foundation,
    Hub,
    Orchestrator,
}

public enum LibraryStructuralEvidenceDisposition
{
    Complete,
    Qualified,
}

public sealed record LibraryStructuralSignatureUseQualification(
    MetadataLibrarySignatureUseReceipt Receipt,
    MetadataLibrarySignatureUseDisposition Disposition,
    MetadataLibrarySignatureUseCoverage Coverage,
    int OccurrenceCount,
    ImmutableArray<MetadataLibrarySignatureUseDiagnostic> Diagnostics);

public sealed record LibraryStructuralNamespaceLeverageRow(
    string Namespace,
    int TypeCount,
    int ExternalIncomingSourceTypeCount,
    bool TopLeverage);

public sealed record LibraryStructuralNamespaceLeverageIndex(
    string MethodologyVersion,
    LibraryStructuralSalienceEvidenceMode EvidenceMode,
    LibraryStructuralEvidenceDisposition Disposition,
    ImmutableArray<LibraryStructuralNamespaceLeverageRow> Rows,
    LibraryStructuralSignatureUseQualification SignatureUse);

public sealed record LibraryStructuralTypeLeverageRow(
    MetadataTypeDefinitionAddress Type,
    MetadataTypeDefinitionName Name,
    MetadataLibraryTypeClassification Classification,
    bool DesignationEligible,
    int SignatureIncomingDegree,
    int SignatureOutgoingDegree,
    LibraryStructuralTypeRole Role,
    bool SeaLevel,
    bool MountainPeak);

public sealed record LibraryStructuralTypeLeverageOrder(
    LibraryStructuralEvidenceDisposition Disposition,
    ImmutableArray<MetadataTypeDefinitionAddress> Types);

public sealed record LibraryStructuralTypeLeverageGraphWork(
    GraphExecutionWorkReceipt SignatureIncomingDegree,
    GraphExecutionWorkReceipt SignatureOutgoingDegree);

public sealed record LibraryStructuralTypeLeverageShard(
    string MethodologyVersion,
    LibraryStructuralSalienceEvidenceMode EvidenceMode,
    string Namespace,
    ImmutableArray<LibraryStructuralTypeLeverageRow> Rows,
    LibraryStructuralTypeLeverageOrder SeaLevel,
    LibraryStructuralTypeLeverageOrder MountainPeak,
    LibraryStructuralEvidenceDisposition RoleDisposition,
    LibraryStructuralSignatureUseQualification SignatureUse,
    LibraryStructuralTypeLeverageGraphWork GraphWork);

public sealed record LibraryStructuralSalienceDocument(
    string MethodologyVersion,
    LibraryStructuralSalienceEvidenceMode EvidenceMode,
    LibraryStructuralNamespaceLeverageIndex NamespaceIndex,
    ImmutableArray<LibraryStructuralTypeLeverageShard> TypeLeverageShards);
