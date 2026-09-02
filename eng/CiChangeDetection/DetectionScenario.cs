namespace CiChangeDetection;

internal sealed record DetectionScenario(
    string EventName,
    string Files,
    string PreviousFiles = "",
    string? ReportedChangedFileCount = null,
    bool ChangedFileCountIsString = false,
    bool ResolutionSucceeds = true,
    string MalformedFileRecordJson = "",
    bool ObjectShapedFilePage = false,
    bool NulFileRecord = false,
    bool NulPreviousFileRecord = false,
    string FileStatus = "modified",
    int FailDecodeAt = 0,
    bool TruncateRecordStream = false,
    bool TruncatePushStream = false,
    bool EmptyPushRecord = false,
    string? TlaCandidateFiles = null,
    bool TlaCandidateResolutionSucceeds = true);
