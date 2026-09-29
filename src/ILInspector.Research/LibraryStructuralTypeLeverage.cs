using System.Collections.Immutable;

using ILInspector.Metadata;
using Inspector.Graph;

namespace ILInspector.Research;

public static class LibraryStructuralTypeLeverage
{
    public const string CurrentMethodologyVersion = "type-leverage.v2";
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

public sealed record LibraryStructuralTypeLeverageRow(
    MetadataTypeDefinitionAddress Type,
    MetadataTypeDefinitionName Name,
    MetadataLibraryTypeClassification Classification,
    bool RankingEligible,
    int SignatureIncomingDegree,
    int SignatureOutgoingDegree,
    LibraryStructuralTypeRole Role);

public sealed record LibraryStructuralTypeLeverageOrder(
    LibraryStructuralEvidenceDisposition Disposition,
    ImmutableArray<MetadataTypeDefinitionAddress> Types);

public sealed record LibraryStructuralTypeLeverageGraphWork(
    GraphExecutionWorkReceipt SignatureIncomingDegree,
    GraphExecutionWorkReceipt SignatureOutgoingDegree);

public sealed record LibraryStructuralTypeLeverageDocument(
    ImmutableArray<LibraryStructuralTypeLeverageRow> Rows,
    LibraryStructuralTypeLeverageOrder SeaLevel,
    LibraryStructuralTypeLeverageOrder MountainPeak,
    LibraryStructuralEvidenceDisposition RoleDisposition,
    LibraryStructuralSignatureUseQualification SignatureUse,
    LibraryStructuralTypeLeverageGraphWork GraphWork);
