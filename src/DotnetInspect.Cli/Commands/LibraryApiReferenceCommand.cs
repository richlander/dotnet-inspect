using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Where the CLI resolved the Library an API Reference describes. The
/// package fields are empty outside a package; <see cref="Package"/> carries
/// the package acquisition when PackageHouse can serve a package-local PDB.
/// </summary>
internal sealed record LibraryApiReferenceTarget(
    string AssemblyPath,
    AssemblyResolutionProvenance Provenance,
    LibraryApiReferenceSource Source,
    PackageExtractionResult? Package = null);

/// <summary>
/// Writes the Library API Reference (docs/design/library-api-reference.md)
/// for the exact <c>library -S "API Reference"</c> selection. The producer
/// streams the document; this host owns the destination: standard output by
/// default, or the <c>-o</c> file, published by renaming a sibling temporary
/// file so an aborted write leaves an earlier file unchanged.
/// </summary>
internal static class LibraryApiReferenceCommand
{
    private const int MaxCompiledDocumentationBytes = 64 * 1024 * 1024;
    private const int DestinationBufferBytes = 64 * 1024;

    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 100_000,
            maxMembers: 1_000_000,
            maxInspectionFailures: 10_000,
            maxTypeForwarders: 100_000,
            maxMetadataRows: 20_000_000,
            maxRetainedTextCharacters: 200_000_000);

    private static readonly DocumentationHouseLimits s_documentationLimits =
        new(
            maximumCompiledXmlContributions: 1,
            MaxCompiledDocumentationBytes,
            XmlDocumentationReadLimits.Default);

    private static readonly SourceLinkReadLimits s_sourceLinkLimits =
        new(
            maxEmbeddedPdbBytes: int.MaxValue,
            maxMapBytes: 4 * 1024 * 1024,
            maxMappings: 100_000);

    /// <summary>
    /// The document is one Text section with no field items: discovery and
    /// <c>explain</c> name it, and nothing projects into it.
    /// </summary>
    internal static void AddStructuralSchema(DocumentSchema schema) =>
        schema.AddSection(SectionNames.ApiReference);

    internal static bool IsSelected(LibraryOptions options)
    {
        if (options.SelectDefault)
            return false;

        if (options.IncludeSections is { } included)
        {
            return included.Count == 1
                && included.Contains(SectionNames.ApiReference)
                && options.ExactIncludeSections is { Count: 1 } exact
                && exact.Contains(SectionNames.ApiReference);
        }

        return (options.Select ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            is [var selector]
            && selector.Equals(
                SectionNames.ApiReference,
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the request names the API Reference by its exact name, alone
    /// or among other sections. As with an exact-only section, a pattern
    /// such as <c>*</c> that merely reaches it does not request it.
    /// </summary>
    private static bool IsNamed(LibraryOptions options) =>
        !options.SelectDefault
        && (options.Select is { } select
            ? select.Any(selector =>
                selector.Equals(
                    SectionNames.ApiReference,
                    StringComparison.OrdinalIgnoreCase))
            : options.ExactIncludeSections?.Contains(
                SectionNames.ApiReference) == true);

    /// <summary>
    /// Refuses what does not apply to a document: it is written whole, so
    /// it is selected alone, and row, field, column, count, presentation,
    /// and other-format gestures have nothing to project.
    /// </summary>
    internal static bool Validate(LibraryOptions options)
    {
        if (options.Discover is not null)
            return true;

        if (!IsSelected(options))
        {
            if (!IsNamed(options))
                return true;

            CommandError.Write(
                "API Reference is one complete JSON document; select it "
                    + "alone with -S \"API Reference\".");
            return false;
        }

        // A rendered-line window (-n, --lines, --tail) would bound the
        // standard-output writer, which the document bypasses.
        if (options.Count
            || options.Rows is not null
            || CommandLineBuilder.HeadLines is not null
            || CommandLineBuilder.TailLines is not null
            || options.Fields is { Length: > 0 }
            || options.Columns is { Length: > 0 }
            || options.Print
            || options.PrintRow is not null
            || options.ProjectionRow is not null
            || options.Value
            || options.Urls
            || options.Paths
            || options.JsonArray
            || options.Tree
            || options.NoHeader
            || options.Effective
            || options.EnvelopeOutput
            || options.ExtractResources is not null
            || options.IncludeReferences
            || options.ReferenceHierarchyDepth is not null
            || options.TypeNamespace is not null
            || options.AddressRequest is not null)
        {
            CommandError.Write(
                "API Reference is one complete JSON document; it does not "
                    + "support row, line, field, column, count, envelope, or "
                    + "presentation projections.");
            return false;
        }

        if (options.FormatFlagExplicitlySet && !options.JsonOutput)
        {
            CommandError.Write(
                "API Reference is JSON; it cannot be written in another "
                    + "output format.");
            return false;
        }

        if (string.Equals(
                options.Tfm,
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            CommandError.Write(
                "API Reference describes one Library; --tfm all is not "
                    + "supported.");
            return false;
        }

        return true;
    }

    internal static async Task<int> ExecuteAsync(
        LibraryApiReferenceTarget target,
        LibraryOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        await using SettledSourceLink sourceLink =
            await SettleAsync(target, options, context, cancellationToken)
                .ConfigureAwait(false);
        var generator = new LibraryApiReferenceGenerator(
            "dotnet-inspect",
            VersionInfo.Version);
        MetadataLibrarySymbolAdmission admission =
            options.AllDeclarations
                ? MetadataLibrarySymbolAdmission.All
                : MetadataLibrarySymbolAdmission.PublicApi;

        using Destination destination =
            Destination.Open(options.OutputPath);
        LibraryApiReferenceOutcome? outcome;
        string? documentationFailure = null;
        try
        {
            outcome =
                await ExactLibraryInspectionExecutor.ExecuteComposedAsync(
                        target.AssemblyPath,
                        "library API Reference",
                        session => ValueTask.FromResult(
                            session.ExecuteApiReference(
                                contributions => new LibraryApiReferenceRequest(
                                    admission,
                                    generator,
                                    target.Source,
                                    s_bounds,
                                    sections: null,
                                    new LibraryApiReferenceDocumentation(
                                        contributions,
                                        s_documentationLimits,
                                        TimeSpan.FromMinutes(2))
                                    {
                                        CompanionFailure = documentationFailure,
                                    },
                                    sourceLink.Evidence),
                                destination.Stream,
                                cancellationToken)),
                        includeCompiledDocumentation: true,
                        cancellationToken,
                        maxCompiledDocumentationBytes:
                            MaxCompiledDocumentationBytes,
                        // An unreadable companion is section evidence, not
                        // an abort: documentation is failed and the rest of
                        // the document is still written.
                        compiledDocumentationUnreadable: failure =>
                            documentationFailure = FailureKind(failure))
                    .ConfigureAwait(false);
        }
        catch (CompiledDocumentationLimitExceededException failure)
        {
            // The compiled XML companion is read before the producer runs;
            // one beyond the documentation byte budget aborts the write.
            CommandError.Write(
                "API Reference aborted (DocumentationBudget): "
                    + failure.Message);
            return 1;
        }

        switch (outcome)
        {
            case LibraryApiReferenceOutcome.Written written:
                destination.Publish();
                return Report(written);

            case LibraryApiReferenceOutcome.Aborted aborted:
                CommandError.Write(
                    $"API Reference aborted ({aborted.Reason}): "
                        + aborted.Detail);
                if (!destination.IsFile)
                {
                    CommandError.WriteNote(
                        "Standard output holds an incomplete prefix that is "
                            + "not a JSON document.");
                }
                return 1;

            case null:
                // The executor already reported why the Library could not
                // be read.
                return 1;

            default:
                throw new InvalidOperationException(
                    "Unknown API Reference outcome.");
        }
    }

    /// <summary>
    /// The stable name a companion or settlement failure is recorded under in
    /// the file, which carries no path (docs/design/library-api-reference.md#layout).
    /// The exception's own message, which may name local paths, goes to
    /// stderr only.
    /// </summary>
    private static string FailureKind(Exception failure)
    {
        CommandError.WriteNote(failure.Message);
        return failure.GetType().Name;
    }

    private static int Report(LibraryApiReferenceOutcome.Written written)
    {
        foreach (LibraryApiReferenceSectionOutcome section
            in written.Sections)
        {
            switch (section.Status)
            {
                case LibraryApiReferenceSectionStatus.Partial:
                case LibraryApiReferenceSectionStatus.Failed:
                    CommandError.WriteWarning(
                        $"API Reference section {section.Name} is "
                            + $"{Spell(section.Status)}"
                            + (section.Detail.Length == 0
                                ? "."
                                : $": {section.Detail}"));
                    break;
                case LibraryApiReferenceSectionStatus.Offline:
                    CommandError.WriteNote(
                        $"API Reference section {section.Name} is offline; "
                            + "run without --offline to settle it.");
                    break;
            }
        }

        if (written.IsComplete)
            return 0;

        CommandError.Write(
            "The API Reference was written whole but is partial; its "
                + "outcome and section_outcomes say which sections did not "
                + "settle.");
        return 1;
    }

    private static string Spell(LibraryApiReferenceSectionStatus status) =>
        status switch
        {
            LibraryApiReferenceSectionStatus.Partial => "partial",
            LibraryApiReferenceSectionStatus.Failed => "failed",
            _ => status.ToString(),
        };

    /// <summary>
    /// Settles the Library's Portable PDB under the CLI's PDB policy
    /// (docs/pdb-acquisition.md#host-neutral-portable-pdb-settlement). A
    /// local file's sibling PDB is the caller's explicit local companion; a
    /// package's PDB comes only from settlement, including the package-local
    /// candidate PackageHouse issues. <c>--offline</c> keeps settlement to
    /// network-free candidates and reports <c>offline</c> when none holds a
    /// PDB.
    /// </summary>
    private static async Task<SettledSourceLink> SettleAsync(
        LibraryApiReferenceTarget target,
        LibraryOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        bool offline =
            DotnetInspector.Networking.HttpClientFactory.IsOffline;
        if (target.Provenance is AssemblyResolutionProvenance.LocalAsset
            && Path.ChangeExtension(target.AssemblyPath, ".pdb")
                is var siblingPath
            && File.Exists(siblingPath))
        {
            context.Logger.Log(
                "Using the local Library's sibling Portable PDB: "
                + siblingPath);
            try
            {
                return SettledSourceLink.Stream(
                    File.OpenRead(siblingPath),
                    scope: null);
            }
            catch (Exception failure)
                when (failure is IOException
                    or UnauthorizedAccessException)
            {
                return SettledSourceLink.Of(
                    new LibraryApiReferenceSourceLink.Failed(
                        "The local Library's sibling Portable PDB could not "
                            + $"be read ({FailureKind(failure)})."));
            }
        }

        AssemblyDescriptorSelectionResult selection =
            ResolvedAssemblyReference.SelectFromPath(
                target.AssemblyPath,
                target.Provenance);
        if (selection
            is not AssemblyDescriptorSelectionResult.Ready ready)
        {
            // The producer reports the unreadable Library; the section
            // has no image to settle against.
            return SettledSourceLink.Of(
                new LibraryApiReferenceSourceLink.Failed(
                    "The Library image could not be selected for Portable "
                        + "PDB settlement."));
        }

        PackageSettlementScope? scope =
            PackageSettlementScope.Create(target, options, context);
        try
        {
            using SourceLinkService settlementSource =
                SourceLinkService.OpenEmbeddedPdbOnly(
                    ready.Reference,
                    context.Logger.Log);
            var request =
                new PortablePdbSettlementRequest(
                    settlementSource.Context,
                    ready.Reference,
                    context.HttpClient,
                    FileSystemPdbStore.CreateDefault(),
                    new SourcePolicyPackageSourceAuthorization(
                        options.SourceOptions))
                {
                    CacheOnly = offline,
                    PackageProducer = scope?.Producer,
                    MicrosoftSymbolServerFallback = true,
                    PackagePreparation = scope?.Preparation(ready.Reference),
                    NuGetSourceOptions = options.SourceOptions,
                    Timeout = TimeSpan.FromMinutes(5),
                    Log = context.Logger.Log,
                };
            PortablePdbSettlementResult settlement =
                await PortablePdbSettlement.SettleAsync(
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);
            SettledSourceLink settled = settlement switch
            {
                PortablePdbSettlementResult.Acquired acquired =>
                    SettledSourceLink.Stream(
                        await acquired.Content.OpenReadAsync(
                                cancellationToken)
                            .ConfigureAwait(false),
                        scope),
                PortablePdbSettlementResult.Unavailable when offline =>
                    SettledSourceLink.Of(
                        new LibraryApiReferenceSourceLink.Offline(),
                        scope),
                PortablePdbSettlementResult.Unavailable =>
                    SettledSourceLink.Of(
                        new LibraryApiReferenceSourceLink.Unavailable(),
                        scope),
                PortablePdbSettlementResult.Incomplete =>
                    SettledSourceLink.Of(
                        new LibraryApiReferenceSourceLink.Failed(
                            "settlement was incomplete"),
                        scope),
                PortablePdbSettlementResult.Failed failed =>
                    SettledSourceLink.Of(
                        new LibraryApiReferenceSourceLink.Failed(
                            failed.Failure.ToString()),
                        scope),
                PortablePdbSettlementResult.Canceled =>
                    throw new OperationCanceledException(
                        cancellationToken),
                _ => throw new InvalidOperationException(
                    "Portable PDB settlement returned an unknown outcome."),
            };
            scope = null;
            return settled;
        }
        catch (Exception failure)
            when (failure is IOException
                or InvalidDataException
                or BadImageFormatException
                or UnauthorizedAccessException)
        {
            return SettledSourceLink.Of(
                new LibraryApiReferenceSourceLink.Failed(
                    $"Portable PDB settlement failed ({FailureKind(failure)})."));
        }
        finally
        {
            if (scope is not null)
                await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The source_link evidence and whatever must outlive the producer's read
    /// of it: the settled content stream and the package composition that
    /// served it.
    /// </summary>
    private sealed class SettledSourceLink(
        LibraryApiReferenceSourceLink evidence,
        Stream? content,
        PackageSettlementScope? scope)
        : IAsyncDisposable
    {
        internal LibraryApiReferenceSourceLink Evidence { get; } = evidence;

        internal static SettledSourceLink Of(
            LibraryApiReferenceSourceLink evidence,
            PackageSettlementScope? scope = null) =>
            new(evidence, content: null, scope);

        internal static SettledSourceLink Stream(
            Stream content,
            PackageSettlementScope? scope) =>
            new(
                new LibraryApiReferenceSourceLink.Settled(
                    () => content,
                    s_sourceLinkLimits),
                content,
                scope);

        public async ValueTask DisposeAsync()
        {
            if (content is not null)
                await content.DisposeAsync().ConfigureAwait(false);
            if (scope is not null)
                await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The PackageHouse composition that issues a package Library's
    /// package-local PDB candidate, from the producer that served the
    /// package, as member Source Locations does.
    /// </summary>
    private sealed class PackageSettlementScope : IAsyncDisposable
    {
        private readonly DesktopPackageSourceComposition _composition;
        private readonly TemporaryPackageStoreScope _stores;
        private readonly MemberSourceLocationCollector
            .PortablePdbPackageContentSource _content;

        private PackageSettlementScope(
            DesktopPackageSourceComposition composition,
            TemporaryPackageStoreScope stores,
            MemberSourceLocationCollector.PortablePdbPackageContentSource content,
            PackageProducerIdentity producer)
        {
            _composition = composition;
            _stores = stores;
            _content = content;
            Producer = producer;
        }

        internal PackageProducerIdentity Producer { get; }

        internal static PackageSettlementScope? Create(
            LibraryApiReferenceTarget target,
            LibraryOptions options,
            CommandContext context)
        {
            if (target.Provenance
                    is not AssemblyResolutionProvenance.PackageAsset
                    {
                        Tfm: not null,
                        AssetPath: not null,
                    }
                || target.Package is not
                {
                    ProducerKey: { } producerKey,
                    AcquiredPayload.Producer: { } producer,
                })
            {
                return null;
            }

            DesktopPackageSourceComposition composition =
                context.CreatePackageSourceComposition();
            var stores = new TemporaryPackageStoreScope(
                "inspect-cli-api-reference-pdb");
            return new(
                composition,
                stores,
                new(
                    composition,
                    stores,
                    options.SourceOptions,
                    producerKey,
                    context.Logger.Log),
                producer);
        }

        internal PortablePdbPackagePreparation Preparation(
            ResolvedAssemblyReference assembly) =>
            PortablePdbPackageComposition.DeferForAssembly(
                assembly,
                _content,
                Producer);

        public async ValueTask DisposeAsync()
        {
            await _composition.DisposeAsync().ConfigureAwait(false);
            _stores.Dispose();
        }
    }

    /// <summary>
    /// Standard output, or a sibling temporary file that becomes the
    /// <c>-o</c> file only when the producer writes the whole document.
    /// </summary>
    private sealed class Destination : IDisposable
    {
        private readonly string? _path;
        private readonly string? _temporaryPath;
        private bool _published;

        private Destination(
            Stream stream,
            string? path,
            string? temporaryPath)
        {
            Stream = stream;
            _path = path;
            _temporaryPath = temporaryPath;
        }

        internal Stream Stream { get; }

        internal bool IsFile => _path is not null;

        internal static Destination Open(string? outputPath)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                return new(
                    new BufferedStream(
                        Console.OpenStandardOutput(),
                        DestinationBufferBytes),
                    path: null,
                    temporaryPath: null);
            }

            string path = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException(
                    "The API Reference output path has no parent directory.");
            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            return new(
                new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    DestinationBufferBytes),
                path,
                temporaryPath);
        }

        internal void Publish()
        {
            Stream.Flush();
            if (_path is null)
                return;

            Stream.Dispose();
            File.Move(_temporaryPath!, _path, overwrite: true);
            _published = true;
        }

        public void Dispose()
        {
            if (_path is null)
            {
                // Standard output stays open for the process; flush what the
                // producer wrote, including an aborted prefix.
                Stream.Flush();
                return;
            }

            Stream.Dispose();
            if (!_published && File.Exists(_temporaryPath))
                File.Delete(_temporaryPath!);
        }
    }
}
