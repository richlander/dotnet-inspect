using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;

namespace ILInspector.Metadata;

/// <summary>The image-level provenance that names an ECMA-335 metadata root.</summary>
public enum MetadataRootSource
{
    /// <summary>The metadata directory advertised by the CLI header.</summary>
    Cli,

    /// <summary>The metadata root carried by ReadyToRun section 112.</summary>
    ReadyToRunManifest,
}

/// <summary>
/// The identity and provenance of one declared metadata-root extent.
/// </summary>
public sealed record MetadataRootInfo
{
    public MetadataRootInfo(
        int RelativeVirtualAddress,
        int Size,
        ImmutableArray<MetadataRootSource> Sources)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(RelativeVirtualAddress);
        ArgumentOutOfRangeException.ThrowIfNegative(Size);
        if (Sources.IsDefaultOrEmpty)
            throw new ArgumentException("At least one metadata-root source is required.", nameof(Sources));
        if (Sources.Distinct().Count() != Sources.Length)
            throw new ArgumentException("Metadata-root sources must be unique.", nameof(Sources));

        this.RelativeVirtualAddress = RelativeVirtualAddress;
        this.Size = Size;
        this.Sources = Sources;
    }

    /// <summary>The root's RVA in the containing PE image.</summary>
    public int RelativeVirtualAddress { get; }

    /// <summary>The root's declared byte length.</summary>
    public int Size { get; }

    /// <summary>
    /// Every image-level provenance that names this exact extent, in stable
    /// CLI-then-ReadyToRun order.
    /// </summary>
    public ImmutableArray<MetadataRootSource> Sources { get; }

    /// <summary>Whether more than one provenance names this exact extent.</summary>
    public bool IsAliased => Sources.Length > 1;
}

/// <summary>
/// One root-bound metadata result whose physical identity and provenance
/// travel with the projected value.
/// </summary>
public sealed record MetadataRootValue<T>(MetadataRootInfo Root, T Value);

internal sealed class MetadataRootCatalog : IDisposable
{
    readonly PEReader _peReader;
    readonly Action _ensureAlive;
    readonly Lazy<MetadataRootReader?> _cliReader;
    readonly Lazy<ReadyToRunDiscovery> _readyToRun;
    readonly Lazy<MetadataRootBinding?> _cli;
    readonly Lazy<MetadataRootBinding?> _aliased;
    readonly Lazy<MetadataRootBinding?> _manifest;
    MetadataRootReader? _createdCliReader;
    MetadataRootReader? _createdManifestReader;

    internal MetadataRootCatalog(PEReader peReader, Action ensureAlive)
    {
        _peReader = peReader;
        _ensureAlive = ensureAlive;
        _cliReader = new Lazy<MetadataRootReader?>(
            CreateCliReader,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _readyToRun = new Lazy<ReadyToRunDiscovery>(
            DiscoverReadyToRun,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _cli = new Lazy<MetadataRootBinding?>(
            CreateCliBinding,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _aliased = new Lazy<MetadataRootBinding?>(
            CreateAliasedBinding,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _manifest = new Lazy<MetadataRootBinding?>(
            CreateManifestBinding,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal ImmutableArray<MetadataRootInfo> Roots
    {
        get
        {
            _ensureAlive();
            MetadataRootBinding? cli = Resolve(MetadataRootSource.Cli);
            MetadataRootBinding? manifest = Resolve(
                MetadataRootSource.ReadyToRunManifest);

            if (cli is null)
                return manifest is null ? [] : [manifest.Info];
            if (manifest is null || ReferenceEquals(cli, manifest))
                return [cli.Info];
            return [cli.Info, manifest.Info];
        }
    }

    internal MetadataRootBinding? Resolve(MetadataRootSource source)
    {
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown metadata-root source.");

        _ensureAlive();
        return source switch
        {
            MetadataRootSource.Cli => ResolveCli(),
            MetadataRootSource.ReadyToRunManifest => ResolveManifest(),
            _ => throw new InvalidOperationException("Unknown metadata-root source."),
        };
    }

    MetadataRootBinding? ResolveCli()
    {
        MetadataRootReader? reader = _cliReader.Value;
        if (reader is null)
            return null;

        ReadyToRunDiscovery discovery = _readyToRun.Value;
        return discovery.Error is null
            && discovery.Overview?.ManifestMetadata?.AliasesCliMetadataDirectory == true
                ? _aliased.Value
                : _cli.Value;
    }

    MetadataRootBinding? ResolveManifest()
    {
        ReadyToRunDiscovery discovery = _readyToRun.Value;
        discovery.Error?.Throw();
        ReadyToRunSectionSummary? manifest = discovery.Overview?.ManifestMetadata;
        if (manifest is null)
            return null;
        return manifest.AliasesCliMetadataDirectory
            ? _aliased.Value
            : _manifest.Value;
    }

    MetadataRootReader? CreateCliReader()
    {
        _ensureAlive();
        if (!_peReader.HasMetadata)
            return null;

        DirectoryEntry directory = _peReader.PEHeaders.CorHeader!.MetadataDirectory;
        var info = new MetadataRootInfo(
            directory.RelativeVirtualAddress,
            directory.Size,
            [MetadataRootSource.Cli]);
        MetadataRootReader reader = MetadataRootReader.ForCli(
            _peReader,
            _ensureAlive,
            info);
        _createdCliReader = reader;
        return reader;
    }

    ReadyToRunDiscovery DiscoverReadyToRun()
    {
        _ensureAlive();
        try
        {
            return new ReadyToRunDiscovery(
                ReadyToRunImageInspector.Describe(_peReader),
                Error: null);
        }
        catch (BadImageFormatException ex)
        {
            return new ReadyToRunDiscovery(
                Overview: null,
                ExceptionDispatchInfo.Capture(ex));
        }
    }

    MetadataRootBinding? CreateCliBinding()
    {
        MetadataRootReader? reader = _cliReader.Value;
        return reader is null
            ? null
            : new MetadataRootBinding(reader.Info, reader);
    }

    MetadataRootBinding? CreateAliasedBinding()
    {
        MetadataRootReader? reader = _cliReader.Value;
        if (reader is null)
        {
            throw new BadImageFormatException(
                "The ReadyToRun manifest metadata aliases a CLI metadata directory that is not available.");
        }

        var info = new MetadataRootInfo(
            reader.Info.RelativeVirtualAddress,
            reader.Info.Size,
            [MetadataRootSource.Cli, MetadataRootSource.ReadyToRunManifest]);
        return new MetadataRootBinding(info, reader);
    }

    MetadataRootBinding? CreateManifestBinding()
    {
        ReadyToRunDiscovery discovery = _readyToRun.Value;
        discovery.Error?.Throw();
        ReadyToRunSectionSummary? manifest = discovery.Overview?.ManifestMetadata;
        if (manifest is null || manifest.AliasesCliMetadataDirectory)
            return null;

        var info = new MetadataRootInfo(
            manifest.RelativeVirtualAddress,
            manifest.Size,
            [MetadataRootSource.ReadyToRunManifest]);
        var reader = MetadataRootReader.ForManifest(
            _peReader,
            _ensureAlive,
            info);
        _createdManifestReader = reader;
        return new MetadataRootBinding(info, reader);
    }

    public void Dispose()
    {
        _createdManifestReader?.Dispose();
        _createdCliReader?.Dispose();
    }

    sealed record ReadyToRunDiscovery(
        ReadyToRunImageOverview? Overview,
        ExceptionDispatchInfo? Error);
}

internal sealed record MetadataRootBinding(
    MetadataRootInfo Info,
    MetadataRootReader ReaderOwner)
{
    internal MetadataReader Reader => ReaderOwner.Reader;
    internal int ImageOffset => ReaderOwner.ImageOffset;
}

internal sealed class MetadataRootReader : IDisposable
{
    readonly PEReader _peReader;
    readonly Action _ensureAlive;
    readonly bool _isCli;
    readonly Lazy<MetadataReader> _reader;
    MetadataReaderProvider? _provider;

    MetadataRootReader(
        PEReader peReader,
        Action ensureAlive,
        MetadataRootInfo info,
        bool isCli)
    {
        _peReader = peReader;
        _ensureAlive = ensureAlive;
        Info = info;
        _isCli = isCli;
        _reader = new Lazy<MetadataReader>(
            CreateReader,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal MetadataRootInfo Info { get; }

    internal MetadataReader Reader
    {
        get
        {
            _ensureAlive();
            return _reader.Value;
        }
    }

    internal int ImageOffset
    {
        get
        {
            _ensureAlive();
            if (_isCli)
                return _peReader.PEHeaders.MetadataStartOffset;

            int sectionIndex = _peReader.PEHeaders.GetContainingSectionIndex(
                Info.RelativeVirtualAddress);
            if (sectionIndex < 0)
            {
                throw new MalformedMetadataRootException(
                    MetadataRootMalformedReason.UnmappableMetadataExtent,
                    MetadataRootSource.ReadyToRunManifest);
            }

            SectionHeader section = _peReader.PEHeaders.SectionHeaders[sectionIndex];
            return checked(
                section.PointerToRawData
                + Info.RelativeVirtualAddress
                - section.VirtualAddress);
        }
    }

    internal static MetadataRootReader ForCli(
        PEReader peReader,
        Action ensureAlive,
        MetadataRootInfo info) =>
        new(peReader, ensureAlive, info, isCli: true);

    internal static MetadataRootReader ForManifest(
        PEReader peReader,
        Action ensureAlive,
        MetadataRootInfo info) =>
        new(peReader, ensureAlive, info, isCli: false);

    MetadataReader CreateReader()
    {
        _ensureAlive();
        if (_isCli)
        {
            return MetadataFormatAdmission.GetMetadataReader(
                _peReader,
                MetadataReaderOptions.None,
                MetadataRootSource.Cli);
        }

        PEMemoryBlock block;
        try
        {
            block = _peReader.GetSectionData(Info.RelativeVirtualAddress);
        }
        catch (BadImageFormatException)
        {
            throw new MalformedMetadataRootException(
                MetadataRootMalformedReason.UnmappableMetadataExtent,
                MetadataRootSource.ReadyToRunManifest);
        }

        if (block.Length < Info.Size)
        {
            throw new MalformedMetadataRootException(
                MetadataRootMalformedReason.UnmappableMetadataExtent,
                MetadataRootSource.ReadyToRunManifest);
        }

        MetadataFormatAdmission.AdmitRoot(
            block,
            Info.Size,
            MetadataRootSource.ReadyToRunManifest);

        ImmutableArray<byte> image = block.GetContent(0, Info.Size);
        _provider = MetadataReaderProvider.FromMetadataImage(image);
        return _provider.GetMetadataReader(MetadataReaderOptions.None);
    }

    public void Dispose() => _provider?.Dispose();
}
