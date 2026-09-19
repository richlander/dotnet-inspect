using System.Collections.Immutable;
using DotnetInspector.Services;
using NuGetFetch;

namespace DotnetInspector.Queries;

public enum PackageLicenseIdentityKind
{
    None,
    Expression,
    RecognizedFile,
    Unknown,
}

public sealed record PackageLicenseIdentity(
    PackageLicenseIdentityKind Kind,
    string Value);

public static class PackageLicenseIdentityQuery
{
    public static PackageLicenseIdentity Execute(
        PackageLicenseDeclaration? declaration)
    {
        if (declaration is null)
            return new(PackageLicenseIdentityKind.None, "none");
        if (declaration.Kind == PackageLicenseDeclarationKind.Expression)
        {
            return new(
                PackageLicenseIdentityKind.Expression,
                declaration.Value);
        }
        if (declaration.Kind == PackageLicenseDeclarationKind.File
            && IsOsmfFile(declaration.Value))
        {
            return new(
                PackageLicenseIdentityKind.RecognizedFile,
                "OSMF");
        }
        return new(PackageLicenseIdentityKind.Unknown, "unknown");
    }

    public static bool Matches(
        PackageLicenseDeclaration? declaration,
        string requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (requested.Equals("any", StringComparison.Ordinal))
            return declaration is not null;
        if (requested.Equals("MIT", StringComparison.Ordinal))
        {
            return declaration?.Kind
                    == PackageLicenseDeclarationKind.Expression
                && declaration.Value.Equals(
                    "MIT",
                    StringComparison.OrdinalIgnoreCase);
        }
        if (requested.Equals("OSMF", StringComparison.Ordinal))
        {
            return declaration?.Kind == PackageLicenseDeclarationKind.File
                && IsOsmfFile(declaration.Value);
        }
        throw new InvalidOperationException(
            "Unknown bound Package Query license identity.");
    }

    private static bool IsOsmfFile(string path)
    {
        string normalized = path.Replace('\\', '/');
        string fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        return fileName.StartsWith(
            "OSMFEULA.",
            StringComparison.OrdinalIgnoreCase);
    }
}

public enum PackageLicenseManifestFailureReason
{
    AuthorizationDenied,
    AuthorizationIncomplete,
    AcquisitionFailed,
    AcquisitionIncomplete,
}

public abstract record PackageLicenseManifestResult
{
    private PackageLicenseManifestResult()
    {
    }

    public sealed record Acquired(ReadOnlyMemory<byte> Content) :
        PackageLicenseManifestResult;

    public sealed record Unavailable(
        PackageLicenseManifestFailureReason Reason) :
        PackageLicenseManifestResult;
}

public interface IPackageLicenseManifestSource
{
    Task<PackageLicenseManifestResult> AcquireAsync(
        PackageSourceCoordinate coordinate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);
}

public enum PackageLicenseInventoryFailureReason
{
    AuthorizationDenied,
    AuthorizationIncomplete,
    ManifestAcquisitionFailed,
    ManifestAcquisitionIncomplete,
    InvalidManifest,
}

public sealed record PackageLicenseInventoryFailure(
    PackageLicenseInventoryFailureReason Reason,
    string Message);

public sealed record PackageLicenseInventoryItem
{
    private PackageLicenseInventoryItem(
        PackageSourceCoordinate coordinate,
        PackageLicenseIdentity? license,
        PackageLicenseDeclaration? declaration,
        PackageLicenseInventoryFailure? failure)
    {
        if ((license is null) == (failure is null))
        {
            throw new ArgumentException(
                "A license inventory item requires either license facts or one failure.");
        }

        Coordinate = coordinate
            ?? throw new ArgumentNullException(nameof(coordinate));
        License = license;
        Declaration = declaration;
        Failure = failure;
    }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageLicenseIdentity? License { get; }

    public PackageLicenseDeclaration? Declaration { get; }

    public PackageLicenseInventoryFailure? Failure { get; }

    public static PackageLicenseInventoryItem Available(
        PackageSourceCoordinate coordinate,
        PackageLicenseDeclaration? declaration) =>
        new(
            coordinate,
            PackageLicenseIdentityQuery.Execute(declaration),
            declaration,
            failure: null);

    public static PackageLicenseInventoryItem Unavailable(
        PackageSourceCoordinate coordinate,
        PackageLicenseInventoryFailure failure) =>
        new(
            coordinate,
            license: null,
            declaration: null,
            failure);
}

public enum PackageLicenseInventoryCompletion
{
    Complete,
    Partial,
}

public sealed record PackageLicenseInventoryResult(
    ImmutableArray<PackageLicenseInventoryItem> Items,
    PackageLicenseInventoryCompletion Completion)
{
    public ImmutableArray<PackageLicenseInventoryItem> Items { get; init; } =
        Items.IsDefault ? [] : Items;
}

public static class PackageLicenseInventoryQuery
{
    private const int AcquisitionBatchSize = 8;

    public static async Task<PackageLicenseInventoryResult> ExecuteAsync(
        IEnumerable<PackageSourceCoordinate> coordinates,
        IPackageLicenseManifestSource source,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(source);

        PackageSourceCoordinate[] ordered =
        [
            .. coordinates
                .Distinct()
                .OrderBy(
                    static coordinate => coordinate.PackageId,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static coordinate => coordinate.Version,
                    StringComparer.Ordinal),
        ];
        var items = new PackageLicenseInventoryItem[ordered.Length];
        for (int offset = 0;
             offset < ordered.Length;
             offset += AcquisitionBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(
                AcquisitionBatchSize,
                ordered.Length - offset);
            Task<PackageLicenseInventoryItem>[] batch = new Task<
                PackageLicenseInventoryItem>[count];
            for (int index = 0; index < count; index++)
            {
                PackageSourceCoordinate coordinate = ordered[offset + index];
                batch[index] = EvaluateAsync(
                    coordinate,
                    source,
                    cancellationToken,
                    operationContext);
            }
            PackageLicenseInventoryItem[] completed =
                await Task.WhenAll(batch).ConfigureAwait(false);
            completed.CopyTo(items, offset);
        }

        return new PackageLicenseInventoryResult(
            [.. items],
            items.Any(static item => item.Failure is not null)
                ? PackageLicenseInventoryCompletion.Partial
                : PackageLicenseInventoryCompletion.Complete);
    }

    private static async Task<PackageLicenseInventoryItem> EvaluateAsync(
        PackageSourceCoordinate coordinate,
        IPackageLicenseManifestSource source,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext)
    {
        PackageLicenseManifestResult result =
            await source.AcquireAsync(
                coordinate,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        if (result is PackageLicenseManifestResult.Unavailable unavailable)
        {
            return PackageLicenseInventoryItem.Unavailable(
                coordinate,
                Failure(unavailable.Reason));
        }

        ReadOnlyMemory<byte> content =
            ((PackageLicenseManifestResult.Acquired)result).Content;
        PackageManifestFactsResult facts =
            PackageManifestFactsQuery.Execute(content, coordinate);
        return facts switch
        {
            PackageManifestFactsResult.Available available =>
                PackageLicenseInventoryItem.Available(
                    coordinate,
                    available.Value.LicenseDeclaration),
            PackageManifestFactsResult.Failed failed =>
                PackageLicenseInventoryItem.Unavailable(
                    coordinate,
                    new PackageLicenseInventoryFailure(
                        PackageLicenseInventoryFailureReason.InvalidManifest,
                        failed.Failure.Message)),
            _ => throw new InvalidOperationException(
                "Unknown package manifest facts result."),
        };
    }

    private static PackageLicenseInventoryFailure Failure(
        PackageLicenseManifestFailureReason reason) =>
        reason switch
        {
            PackageLicenseManifestFailureReason.AuthorizationDenied =>
                new(
                    PackageLicenseInventoryFailureReason.AuthorizationDenied,
                    "No configured package source authorized this exact package coordinate."),
            PackageLicenseManifestFailureReason.AuthorizationIncomplete =>
                new(
                    PackageLicenseInventoryFailureReason.AuthorizationIncomplete,
                    "Package-source authorization did not complete."),
            PackageLicenseManifestFailureReason.AcquisitionFailed =>
                new(
                    PackageLicenseInventoryFailureReason
                        .ManifestAcquisitionFailed,
                    "The exact package manifest could not be acquired."),
            PackageLicenseManifestFailureReason.AcquisitionIncomplete =>
                new(
                    PackageLicenseInventoryFailureReason
                        .ManifestAcquisitionIncomplete,
                    "Exact package manifest acquisition did not complete."),
            _ => throw new InvalidOperationException(
                "Unknown package license manifest failure reason."),
        };
}
