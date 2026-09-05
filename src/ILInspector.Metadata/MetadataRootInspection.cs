using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using InertText;

namespace ILInspector.Metadata;

/// <summary>The metadata root requested within one PE image.</summary>
public enum MetadataRootKind
{
    Cli,
    ReadyToRunManifest,
}

/// <summary>
/// A canonical root coordinate within its containing image, not an artifact
/// identity or a cross-image cache key. A manifest that aliases CLI metadata
/// has the same identity as the CLI root.
/// </summary>
public readonly record struct MetadataRootIdentity(
    MetadataRootKind Kind,
    int RelativeVirtualAddress,
    int Size);

/// <summary>
/// Root-scoped access to the existing metadata projection. Retains only the
/// selected root's bytes and header facts, independently of the source reader.
/// Tokens and heap addresses returned by its operations are local to this root.
/// </summary>
public sealed class MetadataRootInspection
{
    readonly ImmutableArray<byte> _metadata;
    readonly MetadataImageHeaders _headers;
    readonly int _metadataOffset;

    MetadataRootInspection(
        ImmutableArray<byte> metadata,
        MetadataImageHeaders headers,
        int metadataOffset,
        MetadataRootKind requestedRoot,
        MetadataRootIdentity identity)
    {
        _metadata = metadata;
        _headers = headers;
        _metadataOffset = metadataOffset;
        RequestedRoot = requestedRoot;
        Identity = identity;
    }

    /// <summary>The caller's selection, including manifest provenance for a CLI alias.</summary>
    public MetadataRootKind RequestedRoot { get; }

    /// <summary>The selected root's canonical coordinate in the source image.</summary>
    public MetadataRootIdentity Identity { get; }

    /// <summary>
    /// Captures the selected root. Returns null only when that root is absent.
    /// Format rejection and R2R discovery failures propagate; SRM stream/table
    /// validation occurs when an operation reads the captured root.
    /// </summary>
    public static MetadataRootInspection? Open(
        PEReader peReader,
        MetadataRootKind root = MetadataRootKind.Cli)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        if (root is not (MetadataRootKind.Cli or MetadataRootKind.ReadyToRunManifest))
            throw new ArgumentOutOfRangeException(nameof(root));

        if (root == MetadataRootKind.Cli)
            return OpenCli(peReader, root);

        var manifest = ReadyToRunImageInspector.Describe(peReader)?.ManifestMetadata;
        if (manifest is null)
            return null;

        // Zero-size sections have no producer-validated payload mapping. A
        // declared empty metadata root is malformed, not an absent manifest.
        if (manifest.Size == 0)
            throw new MalformedMetadataRootException(MetadataRootMalformedReason.TruncatedFixedPrefix);

        if (manifest.AliasesCliMetadataDirectory)
            return OpenCli(peReader, root);

        var directory = new DirectoryEntry(manifest.RelativeVirtualAddress, manifest.Size);
        if (!peReader.PEHeaders.TryGetDirectoryOffset(directory, out int metadataOffset))
            throw new BadImageFormatException("The ReadyToRun manifest metadata directory cannot be mapped.");

        var block = peReader.GetSectionData(manifest.RelativeVirtualAddress);
        MetadataFormatAdmission.AdmitRoot(block.GetReader(0, manifest.Size));
        return new MetadataRootInspection(
            block.GetContent(0, manifest.Size),
            MetadataImageInspector.DescribeHeaders(peReader.PEHeaders),
            metadataOffset,
            root,
            new MetadataRootIdentity(root, manifest.RelativeVirtualAddress, manifest.Size));
    }

    static MetadataRootInspection? OpenCli(PEReader peReader, MetadataRootKind requestedRoot)
    {
        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return null;

        var headers = peReader.PEHeaders;
        var directory = headers.CorHeader!.MetadataDirectory;
        return new MetadataRootInspection(
            peReader.GetMetadata().GetContent(),
            MetadataImageInspector.DescribeHeaders(headers),
            headers.MetadataStartOffset,
            requestedRoot,
            new MetadataRootIdentity(
                MetadataRootKind.Cli,
                directory.RelativeVirtualAddress,
                directory.Size));
    }

    /// <summary>Describes this root's tables, heaps, and containing-image headers.</summary>
    public MetadataImageOverview Image(UntrustedTextMode untrustedText = UntrustedTextMode.Contain) =>
        Read(reader => MetadataImageInspector.Describe(
            reader, _headers, _metadataOffset, Identity.Size, untrustedText));

    /// <summary>Projects tables using the existing selection and cell budgets.</summary>
    public MetadataTableProjection Tables(MetadataProjectionOptions? options = null) =>
        Read(reader => MetadataTableProjectionEngine.Project(reader, options ?? new()));

    /// <summary>Reads a root-local row independently of the projection's table selection/window.</summary>
    public MetadataTableView? Row(
        TableIndex table,
        int rowId,
        MetadataProjectionOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rowId, 1);
        if (!MetadataTableProjectionEngine.TryGetTableSpec(table, out var spec))
            return null;

        return Read(reader => MetadataTableProjectionEngine.ProjectRow(
            reader, spec, rowId, options ?? new()));
    }

    /// <summary>Finds root-local handle and range references with the existing coverage reporting.</summary>
    public MetadataRowReferenceSet References(
        TableIndex table,
        int rowId,
        int maxReferences = MetadataRowReferenceSet.DefaultMaxReferences)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rowId, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReferences);
        return Read(reader => MetadataRowReferenceFinder.FindReferences(
            reader, new MetadataRowLocation(table, rowId), maxReferences));
    }

    /// <summary>Reads a heap address in this root, not in the containing image's CLI root.</summary>
    public MetadataValue HeapValue(
        HeapKind heap,
        int address,
        MetadataProjectionOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(address);
        return Read(reader => MetadataHeapProjector.ReadHeapValue(reader, heap, address, options ?? new()));
    }

    /// <summary>Lists this root's heap entries with the existing enumeration limits.</summary>
    public MetadataHeapEntrySet HeapEntries(HeapKind heap, MetadataProjectionOptions? options = null) =>
        Read(reader => MetadataHeapProjector.ReadHeapEntries(reader, heap, options ?? new()));

    TResult Read<TResult>(Func<MetadataReader, TResult> inspect)
    {
        using var provider = MetadataReaderProvider.FromMetadataImage(_metadata);
        return inspect(provider.GetMetadataReader(MetadataReaderOptions.None));
    }
}
