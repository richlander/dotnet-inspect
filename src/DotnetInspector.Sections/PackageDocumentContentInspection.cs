using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using InertText;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<PackageDocumentContentStatus>))]
public enum PackageDocumentContentStatus
{
    Completed,
    Unavailable,
    Failed,
}

public sealed record PackageDocumentContentDocument
{
    private PackageDocumentContentDocument(
        PackageDocumentContentStatus status,
        string? packageId,
        string? version,
        string? path,
        long size,
        ImmutableArray<byte> content,
        InertString? detail)
    {
        Status = status;
        PackageId = packageId;
        Version = version;
        Path = path;
        Size = size;
        Content = content.IsDefault ? [] : content;
        Detail = detail;
    }

    public PackageDocumentContentStatus Status { get; }

    public string? PackageId { get; }

    public string? Version { get; }

    public string? Path { get; }

    public long Size { get; }

    public ImmutableArray<byte> Content { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Detail { get; }

    public static PackageDocumentContentDocument Completed(
        string packageId,
        string version,
        string path,
        ImmutableArray<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (content.IsDefault)
            throw new ArgumentException(
                "Completed package document content must be initialized.",
                nameof(content));

        return new(
            PackageDocumentContentStatus.Completed,
            packageId,
            version,
            path,
            content.Length,
            content,
            detail: null);
    }

    public static PackageDocumentContentDocument Failed(
        PackageDocumentContentStatus status,
        string detail)
    {
        if (status == PackageDocumentContentStatus.Completed)
        {
            throw new ArgumentException(
                "A failed package document cannot have completed status.",
                nameof(status));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        return new(
            status,
            packageId: null,
            version: null,
            path: null,
            size: 0,
            content: [],
            new InertString(TextPolicy.Field, detail));
    }
}

public sealed record PackageDocumentContentInspectionRequest
{
    public PackageDocumentContentInspectionRequest(
        PackageHouseSettlement.Acquired settlement,
        string path)
    {
        Settlement = settlement
            ?? throw new ArgumentNullException(nameof(settlement));
        Path = PackageDocumentDemand.Create([path]).Entries.Single();
    }

    public PackageHouseSettlement.Acquired Settlement { get; }

    public string Path { get; }
}

public static class PackageDocumentContentInspection
{
    private const string SharePath = "package-document-content/share";

    public static async ValueTask<
        InspectionEnvelope<PackageDocumentContentDocument>>
        ExecuteAsync(
            PackageDocumentContentInspectionRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Settlement.Payload.Content
            is not IPackageContentEntryManifest manifest)
        {
            return Failed(
                PackageDocumentContentStatus.Unavailable,
                "package-document-content.manifest-unavailable",
                "The acquired package content does not expose declared entry lengths.");
        }

        PackageContentEntry[] matches =
        [
            .. manifest.EnumerateEntriesWithLengths()
                .Where(entry => entry.Path.Equals(
                    request.Path,
                    StringComparison.OrdinalIgnoreCase))
                .Take(2),
        ];
        if (matches.Length == 0)
        {
            return Failed(
                PackageDocumentContentStatus.Unavailable,
                "package-document-content.entry-missing",
                $"The package does not contain '{request.Path}'.");
        }
        if (matches.Length > 1)
        {
            return Failed(
                PackageDocumentContentStatus.Unavailable,
                "package-document-content.entry-ambiguous",
                $"The package contains more than one entry matching '{request.Path}'.");
        }

        PackageContentEntry entry = matches[0];
        if (entry.Length < 0 || entry.Length > Array.MaxLength)
        {
            return Failed(
                PackageDocumentContentStatus.Unavailable,
                "package-document-content.length-unavailable",
                $"Package entry '{entry.Path}' has an unsupported declared length.");
        }

        try
        {
            await using PackageHousePayloadRead input =
                request.Settlement.OpenPayloadRead(
                    entry.Path,
                    entry.Length);
            byte[] content = GC.AllocateUninitializedArray<byte>(
                checked((int)entry.Length));
            int offset = 0;
            while (offset < content.Length)
            {
                int read = await input.ReadAsync(
                        content.AsMemory(offset),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException(
                        $"Package entry '{entry.Path}' ended before its declared length.");
                offset += read;
            }

            byte[] extra = new byte[1];
            if (await input.ReadAsync(extra, cancellationToken)
                    .ConfigureAwait(false) != 0)
            {
                throw new InvalidDataException(
                    $"Package entry '{entry.Path}' exceeded its declared length.");
            }

            var coordinate = request.Settlement.Payload.Coordinate;
            var document = PackageDocumentContentDocument.Completed(
                coordinate.PackageId,
                coordinate.Version,
                entry.Path,
                ImmutableCollectionsMarshal.AsImmutableArray(content));
            return new(
                document,
                new InspectionShare.NonProjectable(
                    SharePath,
                    "Package document content sharing is not yet available."));
        }
        catch (Exception exception) when (
            exception is IOException
                or NotSupportedException
                or PackageEntryNotMaterializedException)
        {
            return Failed(
                PackageDocumentContentStatus.Failed,
                "package-document-content.read-failed",
                exception.Message);
        }
    }

    private static InspectionEnvelope<PackageDocumentContentDocument> Failed(
        PackageDocumentContentStatus status,
        string code,
        string detail) =>
        new(
            PackageDocumentContentDocument.Failed(status, detail),
            new InspectionShare.NonProjectable(
                SharePath,
                "Package document content sharing is not yet available."),
            [
                new InspectionDiagnostic(
                    code,
                    InspectionDiagnosticSeverity.Error,
                    detail),
            ]);
}
