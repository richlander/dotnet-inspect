using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using InertText;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public sealed record PackageFileInventoryEntry
{
    public PackageFileInventoryEntry(string path, long size)
        : this(new InertString(TextPolicy.Field, path), size)
    {
    }

    public PackageFileInventoryEntry(InertString path, long size)
    {
        if (path.IsEmpty)
            throw new ArgumentException("A package file path is required.", nameof(path));
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        Path = path;
        Size = size;
    }

    public InertString Path { get; }

    public long Size { get; }
}

internal readonly record struct PackageFileInventoryOperationPredicate;

internal sealed record PackageFileInventoryOperationPlan(
    PortableQueryIntent Intent);

internal sealed class PackageFileInventoryOperationVocabulary
    : PortableQueryVocabulary<
        PackageFileInventoryOperationPredicate,
        PackageFileInventoryOperationPlan>
{
    public override string Identity =>
        PackageFileInventoryQuery.OperationVocabularyIdentity;

    public override IReadOnlyList<string> RequiredDimensions => [];

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out PortableQueryKeyDeclaration<
            PackageFileInventoryOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)]
        out PortableQueryDimensionDeclaration<
            PackageFileInventoryOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool AdmitsStageKind(RowSelectionStageKind kind) => false;

    public override bool TryGetNamedOrder(
        string reference,
        out PortableQueryOrderPurpose purpose)
    {
        purpose = default;
        return false;
    }

    public override bool IsOrderable(string key) => false;

    public override bool CollapsesDuplicateBindings => true;

    public override bool AreTermsCompatible(
        PortableQueryResolvedTerm<
            PackageFileInventoryOperationPredicate> first,
        PortableQueryResolvedTerm<
            PackageFileInventoryOperationPredicate> second) =>
        true;

    public override PackageFileInventoryOperationPlan CreatePlan(
        PortableQueryResolvedIntent<
            PackageFileInventoryOperationPredicate> resolved) =>
        new(
            PortableQueryIntent.Create(
                [.. resolved.Terms.Select(term => term.Term)],
                [.. resolved.Bounds],
                [.. resolved.Stages],
                []));
}

public static class PackageFileInventoryQuery
{
    public const string OperationIdentity = "package-file-inventory";
    public const string OperationRouteIdentity =
        "package-file-inventory/default";
    public const string OperationSubjectRole = "acquired-package";
    public const string OperationResultGrain = "package-file";
    public const string OperationProfileIdentity = "default";
    public const string OperationVocabularyIdentity =
        "package-file-inventory/operation/v1";
    public const string QuerySpaceIdentity =
        "package-file-inventory/query-space/v1";
    public const string FileRowsScopeIdentity =
        "package-file-inventory/file-rows/v1";
    public const string FileRowsResultContract =
        "package-file-inventory/document/v1";
    public const string FilesRowSet = "package-files";

    private static readonly QueryOperationDefinition<
        PackageFileInventoryOperationPredicate,
        PackageFileInventoryOperationPlan> OperationDefinition =
        QueryOperationDefinition<
            PackageFileInventoryOperationPredicate,
            PackageFileInventoryOperationPlan>.Create(
                OperationIdentity,
                new PackageFileInventoryOperationVocabulary(),
                [OperationSubjectRole],
                [OperationResultGrain],
                [FilesRowSet],
                [],
                [],
                [
                    new(
                        OperationProfileIdentity,
                        [],
                        []),
                ]);

    private static readonly QueryOperationRoute<
        PackageFileInventoryOperationPredicate,
        PackageFileInventoryOperationPlan> Route =
        QueryOperationRoute<
            PackageFileInventoryOperationPredicate,
            PackageFileInventoryOperationPlan>.Create(
                OperationRouteIdentity,
                OperationDefinition,
                OperationSubjectRole,
                OperationResultGrain,
                [FilesRowSet],
                OperationProfileIdentity,
                [],
                []);

    public static QuerySpaceRowScopeBinding<PackageFileInventoryEntry>
        FileRowsScope { get; } =
        new(
            new QuerySpaceRowScopeDescriptor(
                FileRowsScopeIdentity,
                FileRowsResultContract,
                [FilesRowSet],
                [],
                [],
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Tail,
                    RowSelectionStageKind.Window,
                ]),
            RowQueryVocabulary<PackageFileInventoryEntry>.Create(
                RowQueryVocabularyIdentity.Create(),
                [],
                []));

    public static QuerySpaceBinding QuerySpace { get; } =
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            Route,
            [FileRowsScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    FileRowsResultContract),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    FileRowsResultContract),
            ]);

    public static bool IsPlumbingPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.StartsWith(
                "_rels/",
                StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(
                "[Content_Types]",
                StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(
                ".psmdcp",
                StringComparison.OrdinalIgnoreCase)
            || path.Equals(
                ".signature.p7s",
                StringComparison.OrdinalIgnoreCase)
            || path.Equals(
                ".nupkg.metadata",
                StringComparison.OrdinalIgnoreCase)
            || path.Equals(
                DotnetInspector.Packages.NuGetCache.CommitMarkerFileName,
                StringComparison.Ordinal)
            || path.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(
                ".nupkg.sha512",
                StringComparison.OrdinalIgnoreCase);
    }

    public static QuerySpaceRequest CreateRequest(
        RowSelectionIntent<string> rows,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentNullException.ThrowIfNull(rows);

        PortableQueryIntent intent = PortableQueryIntent.Create(
            [],
            [],
            PortableQueryRowSelection.ToStages(rows),
            []);
        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            PortableQueryIntent.Create([], [], [], []),
            [FilesRowSet],
            [
                new(
                    FileRowsScopeIdentity,
                    intent,
                    [FilesRowSet]),
            ],
            terminal);
    }
}
