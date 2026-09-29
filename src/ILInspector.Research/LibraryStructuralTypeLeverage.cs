using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Graph;

namespace ILInspector.Research;

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

public sealed record LibraryStructuralBodyUseQualification(
    AnalysisLibraryBodyUseReceipt Receipt,
    AnalysisLibraryBodyUseDisposition Disposition,
    AnalysisLibraryBodyUseCoverage Coverage,
    int OccurrenceCount,
    ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics);

public sealed record LibraryStructuralTypeLeverageRow(
    MetadataTypeDefinitionAddress Type,
    MetadataTypeDefinitionName Name,
    MetadataLibraryTypeClassification Classification,
    bool RankingEligible,
    int SignatureIncomingDegree,
    int BodyOutgoingDegree,
    int CombinedIncomingDegree,
    int CombinedOutgoingDegree,
    LibraryStructuralTypeRole Role);

public sealed record LibraryStructuralTypeLeverageOrder(
    LibraryStructuralEvidenceDisposition Disposition,
    ImmutableArray<MetadataTypeDefinitionAddress> Types);

public sealed record LibraryStructuralTypeLeverageGraphWork(
    GraphExecutionWorkReceipt SignatureIncomingDegree,
    GraphExecutionWorkReceipt BodyOutgoingDegree,
    GraphExecutionWorkReceipt CombinedIncomingDegree,
    GraphExecutionWorkReceipt CombinedOutgoingDegree);

public sealed record LibraryStructuralTypeLeverageDocument(
    ImmutableArray<LibraryStructuralTypeLeverageRow> Rows,
    LibraryStructuralTypeLeverageOrder SeaLevel,
    LibraryStructuralTypeLeverageOrder MountainPeak,
    LibraryStructuralEvidenceDisposition RoleDisposition,
    LibraryStructuralSignatureUseQualification SignatureUse,
    LibraryStructuralBodyUseQualification BodyUse,
    LibraryStructuralTypeLeverageGraphWork GraphWork);
