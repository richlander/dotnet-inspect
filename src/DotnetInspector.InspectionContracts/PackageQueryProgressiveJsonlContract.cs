using DotnetInspector.Sections;

namespace DotnetInspector.InspectionContracts;

/// <summary>The exact Progressive JSONL profile for Package Query durable rows.</summary>
public static class PackageQueryProgressiveJsonlContract
{
    public const int FormatVersion = 1;
    public const string DescriptorIdentity =
        "sha256:2a5e969f2964da281f10259eb282b2bdc273ddf72519d6c0930eaf02cb7c15e4";
    public const int MaximumBatchJsonCodeUnits = 1_048_576;
    public const int MaximumRecordsPerBatch = 20;

    public static ProgressiveJsonlProfile Profile { get; } =
        new(
            FormatVersion,
            PackageQueryDurableRowContract.ContractIdentity,
            ProgressiveJsonlDirection.Serialize,
            DescriptorIdentity,
            MaximumBatchJsonCodeUnits,
            MaximumRecordsPerBatch,
            ProgressiveJsonlLineTerminator.Lf);

    public static InspectionEnvelope<ProgressiveJsonlProfile> Inspect() =>
        new(
            Profile,
            new InspectionShare.NonProjectable(
                "progressive-jsonl/package-query.durable-row/v1",
                "Progressive JSONL profiles describe Browser transport "
                    + "encoding and do not project a Workspace."));
}
