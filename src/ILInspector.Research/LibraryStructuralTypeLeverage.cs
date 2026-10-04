using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Graph;

namespace ILInspector.Research;

public static class LibraryStructuralSalience
{
    public const string CurrentMethodologyVersion =
        "structural-salience.v3";
    public const int MinimumDesignationDegree = 3;
    public const int MinimumCohortMaximumDegree = 10;
    public const int CohortMinimumPercentage = 90;
}

public enum LibraryStructuralSalienceEvidenceMode
{
    Signature,
    BodyUse,
}

public enum LibraryStructuralTypeRole
{
    Foundation,
    Hub,
    Orchestrator,
}

public enum LibraryStructuralTypePole
{
    SeaLevel,
    MountainPeak,
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
    LibraryStructuralTypePole? Pole);

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

public sealed record LibraryStructuralBodyUseQualification(
    AnalysisLibraryBodyUseReceipt Receipt,
    AnalysisLibraryBodyUseDisposition Disposition,
    AnalysisLibraryBodyUseCoverage Coverage,
    int OccurrenceCount,
    ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics);

public sealed record LibraryStructuralBodyTypeLeverageRow(
    MetadataTypeDefinitionAddress Type,
    MetadataTypeDefinitionName Name,
    MetadataLibraryTypeClassification Classification,
    bool DesignationEligible,
    int BodyIncomingDegree,
    int BodyOutgoingDegree,
    LibraryStructuralTypeRole Role,
    LibraryStructuralTypePole? Pole);

public sealed record LibraryStructuralBodyTypeLeverageGraphWork(
    GraphExecutionWorkReceipt BodyIncomingDegree,
    GraphExecutionWorkReceipt BodyOutgoingDegree);

public sealed record LibraryStructuralBodyTypeLeverageShard(
    string MethodologyVersion,
    LibraryStructuralSalienceEvidenceMode EvidenceMode,
    string Namespace,
    ImmutableArray<LibraryStructuralBodyTypeLeverageRow> Rows,
    LibraryStructuralTypeLeverageOrder SeaLevel,
    LibraryStructuralTypeLeverageOrder MountainPeak,
    LibraryStructuralEvidenceDisposition RoleDisposition,
    LibraryStructuralSignatureUseQualification TypeInventory,
    LibraryStructuralBodyUseQualification BodyUse,
    LibraryStructuralBodyTypeLeverageGraphWork GraphWork);

public sealed record LibraryStructuralSalienceDocument(
    string MethodologyVersion,
    LibraryStructuralSalienceEvidenceMode EvidenceMode,
    LibraryStructuralNamespaceLeverageIndex NamespaceIndex,
    ImmutableArray<LibraryStructuralTypeLeverageShard> TypeLeverageShards);
