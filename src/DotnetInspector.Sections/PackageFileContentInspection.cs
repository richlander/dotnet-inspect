using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<PackageFileContentStatus>))]
public enum PackageFileContentStatus
{
    Completed,
    Unavailable,
    Failed,
}

public sealed record PackageFileContentDocument
{
    private PackageFileContentDocument(
        PackageFileContentStatus status,
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

    public PackageFileContentStatus Status { get; }

    public string? PackageId { get; }

    public string? Version { get; }

    public string? Path { get; }

    public long Size { get; }

    public ImmutableArray<byte> Content { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Detail { get; }

    public static PackageFileContentDocument Completed(
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
                "Completed package file content must be initialized.",
                nameof(content));

        return new(
            PackageFileContentStatus.Completed,
            packageId,
            version,
            path,
            content.Length,
            content,
            detail: null);
    }

    public static PackageFileContentDocument Failed(
        PackageFileContentStatus status,
        string detail)
    {
        if (status == PackageFileContentStatus.Completed)
        {
            throw new ArgumentException(
                "A failed package file cannot have completed status.",
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

public sealed record PackageFileContentInspectionRequest
{
    public PackageFileContentInspectionRequest(
        PackageFileAcquisitionResult.Acquired file,
        long maxBytes)
    {
        File = file
            ?? throw new ArgumentNullException(nameof(file));
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maxBytes,
            int.MaxValue);
        MaxBytes = maxBytes;
    }

    public PackageFileAcquisitionResult.Acquired File { get; }

    public long MaxBytes { get; }
}

public static class PackageFileContentInspection
{
    private const string SharePath = "package-file-content/share";

    public static async ValueTask<
        InspectionEnvelope<PackageFileContentDocument>>
        ExecuteAsync(
            PackageFileContentInspectionRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        PackageContentEntry entry = request.File.Entry;
        if (entry.Length < 0
            || entry.Length > request.MaxBytes)
        {
            return Failed(
                PackageFileContentStatus.Unavailable,
                "package-file-content.length-unavailable",
                $"Package entry '{entry.Path}' exceeds the detached content "
                    + $"limit of {request.MaxBytes} bytes.");
        }

        try
        {
            await using PackageHousePayloadRead input =
                request.File.OpenRead();
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

            PackageSourceCoordinate coordinate =
                request.File.Settlement.Payload.Coordinate;
            var file = PackageFileContentDocument.Completed(
                coordinate.PackageId,
                coordinate.Version,
                entry.Path,
                ImmutableCollectionsMarshal.AsImmutableArray(content));
            return new(
                file,
                new InspectionShare.NonProjectable(
                    SharePath,
                    "Package file content sharing is not yet available."));
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or NotSupportedException
                or PackageEntryNotMaterializedException)
        {
            return Failed(
                PackageFileContentStatus.Failed,
                "package-file-content.read-failed",
                exception.Message);
        }
    }

    private static InspectionEnvelope<PackageFileContentDocument> Failed(
        PackageFileContentStatus status,
        string code,
        string detail) =>
        new(
            PackageFileContentDocument.Failed(status, detail),
            new InspectionShare.NonProjectable(
                SharePath,
                "Package file content sharing is not yet available."),
            [
                new InspectionDiagnostic(
                    code,
                    InspectionDiagnosticSeverity.Error,
                    detail),
            ]);
}
