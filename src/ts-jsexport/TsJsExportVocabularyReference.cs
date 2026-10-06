using DotnetInspector.InspectionContracts;
using QuerySpace.Vocabulary;

namespace TsJsExport;

internal static class TsJsExportVocabularyReference
{
    internal static VocabularySnapshotReference Reference { get; } =
        PackageQueryDurableRowContract.CreateVocabularySnapshotReference();
}
