using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InspectWeb.Engine;
using InspectWeb.Engine.MetadataFacade;

[SupportedOSPlatform("browser")]
public static partial class MetadataExports
{
    [JSExport]
    public static async Task<string> QueryPackageMetadata(
        string packageId,
        string version,
        string targetFramework,
        string assemblyFileName)
    {
        BrowserPackageMetadata metadata =
            await PackageMetadataAsync(
                packageId, version, targetFramework, assemblyFileName);
        return JsonSerializer.Serialize(
            metadata,
            BrowserMetadataJsonContext.Default.BrowserPackageMetadata);
    }

    static async Task<BrowserPackageMetadata> PackageMetadataAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyFileName)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserMetadataWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return new BrowserPackageMetadata(
                Assemblies: [],
                InspectionError: null,
                compileLibrary);
        }

        BrowserWorkspaceParticipant participant =
            scope.LibraryParticipant(coordinate, assemblyFileName);
        AssemblyContextEntry<MetadataImageResult> cli =
            scope.UseMetadataParticipant(
                participant,
                (group, selected) =>
                AssemblyContextMetadataImageQuery.ExecuteParticipant(
                    group,
                    selected,
                    MetadataRootKind.Cli));
        AssemblyContextEntry<MetadataImageResult> manifest =
            scope.UseMetadataParticipant(
                participant,
                (group, selected) =>
                AssemblyContextMetadataImageQuery.ExecuteParticipant(
                    group,
                    selected,
                    MetadataRootKind.ReadyToRunManifest));
        AssemblyContextEntry<ReadyToRunImageResult> readyToRun =
            scope.UseMetadataParticipant(
                participant,
                AssemblyContextReadyToRunImageQuery.ExecuteParticipant);
        if (cli
                is not AssemblyContextEntry<MetadataImageResult>.Available
            && manifest
                is not AssemblyContextEntry<MetadataImageResult>.Available
            && readyToRun
                is not AssemblyContextEntry<ReadyToRunImageResult>.Available)
        {
            return new BrowserPackageMetadata(
                [],
                MetadataFailure(cli),
                compileLibrary);
        }

        BrowserAssemblyMetadata? assembly = ProjectMetadataAssembly(
            participant.Asset.AssemblyName,
            cli,
            manifest,
            readyToRun);
        return new BrowserPackageMetadata(
            assembly is null ? [] : [assembly],
            null,
            compileLibrary);
    }

    [JSExport]
    public static async Task<string> QueryPackageMetadataTable(
        string packageId,
        string version,
        string targetFramework,
        string assemblyFileName,
        string metadataRoot,
        int tableIndex,
        int startRowId,
        int maxRows)
    {
        BrowserMetadataWindow window = await PackageMetadataTableAsync(
            packageId,
            version,
            targetFramework,
            assemblyFileName,
            metadataRoot,
            tableIndex,
            startRowId,
            maxRows);
        return JsonSerializer.Serialize(
            window,
            BrowserMetadataJsonContext.Default.BrowserMetadataWindow);
    }

    static async Task<BrowserMetadataWindow> PackageMetadataTableAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyFileName,
        string metadataRoot,
        int tableIndex,
        int startRowId,
        int maxRows)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserWorkspaceParticipant participant = MetadataParticipant(
            scope,
            scope.Coordinates[0],
            assemblyFileName);
        var request = new MetadataTableWindowRequest(
            (TableIndex)tableIndex,
            startRowId,
            maxRows);
        MetadataRootKind root = ParseMetadataRoot(metadataRoot);
        AssemblyContextEntry<MetadataTableWindow> result =
            scope.UseMetadataParticipant(
                participant,
                (group, selected) =>
                AssemblyContextMetadataTableQuery.ExecuteParticipant(
                    group,
                    selected,
                    request,
                    root));
        return ProjectMetadataWindow(assemblyFileName, tableIndex, result);
    }

    [JSExport]
    public static async Task<string> QueryPackageHeapEntries(
        string packageId,
        string version,
        string targetFramework,
        string assemblyFileName,
        string metadataRoot,
        string heap)
    {
        BrowserHeapListing listing = await PackageHeapEntriesAsync(
            packageId,
            version,
            targetFramework,
            assemblyFileName,
            metadataRoot,
            heap);
        return JsonSerializer.Serialize(
            listing,
            BrowserMetadataJsonContext.Default.BrowserHeapListing);
    }

    static async Task<BrowserHeapListing> PackageHeapEntriesAsync(
        string packageId,
        string version,
        string targetFramework,
        string assemblyFileName,
        string metadataRoot,
        string heap)
    {
        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserInspectionScope scope = scopeLease.Scope;
        BrowserWorkspaceParticipant participant = MetadataParticipant(
            scope,
            scope.Coordinates[0],
            assemblyFileName);
        MetadataRootKind root = ParseMetadataRoot(metadataRoot);
        HeapKind heapKind = ParseHeap(heap);
        AssemblyContextEntry<MetadataHeapEntrySet> result =
            scope.UseMetadataParticipant(
                participant,
                (group, selected) =>
                AssemblyContextMetadataHeapQuery.ExecuteParticipant(
                    group,
                    selected,
                    heapKind,
                    root));
        return ProjectHeapListing(assemblyFileName, heapKind, result);
    }

    [JSExport]
    public static async Task<string> QueryPlatformMetadata(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack)
    {
        BrowserPackageMetadata metadata;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            AssemblyContextEntry<MetadataImageResult> cli =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    (group, participant) =>
                        AssemblyContextMetadataImageQuery.ExecuteParticipant(
                            group,
                            participant,
                            MetadataRootKind.Cli));
            string assembly = PlatformAssemblyFileName(
                resolution.Participant.Participant.Assembly.Identity.Name);
            BrowserCompileLibraryAvailability compileLibrary =
                BrowserMetadataWireProjection.Project(
                    BrowserCompileLibraryProjection.Selected(
                        resolution.Scope.Framework));
            AssemblyContextEntry<MetadataImageResult> manifest =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    (group, participant) =>
                        AssemblyContextMetadataImageQuery.ExecuteParticipant(
                            group,
                            participant,
                            MetadataRootKind.ReadyToRunManifest));
            AssemblyContextEntry<ReadyToRunImageResult> readyToRun =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    AssemblyContextReadyToRunImageQuery.ExecuteParticipant);
            if (cli
                    is not AssemblyContextEntry<MetadataImageResult>.Available
                && manifest
                    is not AssemblyContextEntry<MetadataImageResult>.Available
                && readyToRun
                    is not AssemblyContextEntry<ReadyToRunImageResult>.Available)
            {
                metadata = new BrowserPackageMetadata(
                    [],
                    MetadataFailure(cli),
                    compileLibrary);
            }
            else
            {
                BrowserAssemblyMetadata? projected = ProjectMetadataAssembly(
                    assembly,
                    cli,
                    manifest,
                    readyToRun);
                metadata = new BrowserPackageMetadata(
                    projected is null ? [] : [projected],
                    null,
                    compileLibrary);
            }
        }

        return JsonSerializer.Serialize(
            metadata,
            BrowserMetadataJsonContext.Default.BrowserPackageMetadata);
    }

    [JSExport]
    public static async Task<string> QueryPlatformMetadataTable(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack,
        string metadataRoot,
        int tableIndex,
        int startRowId,
        int maxRows)
    {
        BrowserMetadataWindow window;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            var request = new MetadataTableWindowRequest(
                (TableIndex)tableIndex,
                startRowId,
                maxRows);
            MetadataRootKind root = ParseMetadataRoot(metadataRoot);
            AssemblyContextEntry<MetadataTableWindow> result =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    (group, participant) =>
                        AssemblyContextMetadataTableQuery.ExecuteParticipant(
                            group,
                            participant,
                            request,
                            root));
            window = ProjectMetadataWindow(
                PlatformAssemblyFileName(
                    resolution.Participant.Participant.Assembly.Identity.Name),
                tableIndex,
                result);
        }

        return JsonSerializer.Serialize(
            window,
            BrowserMetadataJsonContext.Default.BrowserMetadataWindow);
    }

    [JSExport]
    public static async Task<string> QueryPlatformHeapEntries(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack,
        string metadataRoot,
        string heap)
    {
        MetadataRootKind root = ParseMetadataRoot(metadataRoot);
        HeapKind heapKind = ParseHeap(heap);
        BrowserHeapListing listing;
        await using (BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack))
        {
            AssemblyContextEntry<MetadataHeapEntrySet> result =
                resolution.Scope.UseParticipant(
                    resolution.Participant,
                    (group, participant) =>
                        AssemblyContextMetadataHeapQuery.ExecuteParticipant(
                            group,
                            participant,
                            heapKind,
                            root));
            listing = ProjectHeapListing(
                PlatformAssemblyFileName(
                    resolution.Participant.Participant.Assembly.Identity.Name),
                heapKind,
                result);
        }

        return JsonSerializer.Serialize(
            listing,
            BrowserMetadataJsonContext.Default.BrowserHeapListing);
    }

    internal static BrowserAssemblyMetadata? ProjectMetadataAssembly(
        string assembly,
        AssemblyContextEntry<MetadataImageResult> cli,
        AssemblyContextEntry<MetadataImageResult> manifest,
        AssemblyContextEntry<ReadyToRunImageResult> readyToRun)
    {
        var roots = new List<BrowserMetadataImage>();
        string? cliError = ProjectMetadataRoot(
            MetadataRootKind.Cli,
            cli,
            roots);
        string? manifestError = ProjectMetadataRoot(
            MetadataRootKind.ReadyToRunManifest,
            manifest,
            roots);
        string? readyToRunError = ProjectReadyToRun(
            readyToRun,
            out BrowserReadyToRunImage? readyToRunImage);
        if (roots.Count == 0
            && cliError is null
            && manifestError is null
            && readyToRunImage is null
            && readyToRunError is null)
        {
            return null;
        }

        return new BrowserAssemblyMetadata(
            assembly,
            [.. roots],
            cliError,
            manifestError,
            readyToRunImage,
            readyToRunError);
    }

    static string? ProjectMetadataRoot(
        MetadataRootKind requestedRoot,
        AssemblyContextEntry<MetadataImageResult> entry,
        List<BrowserMetadataImage> roots)
    {
        if (entry
            is not AssemblyContextEntry<MetadataImageResult>.Available
                available)
        {
            return MetadataFailure(entry);
        }

        switch (available.Value)
        {
            case MetadataImageResult.Available result:
                roots.Add(ProjectMetadataImage(
                    requestedRoot,
                    result));
                return null;
            case MetadataImageResult.NoMetadata:
            case MetadataImageResult.MissingRoot:
                return null;
            case MetadataImageResult.Failed failed:
                return BrowserSurfaceProjection.FailedAssembly(
                    failed.Error);
            default:
                throw new InvalidOperationException(
                    "Unknown metadata image result.");
        }
    }

    static BrowserMetadataImage ProjectMetadataImage(
        MetadataRootKind requestedRoot,
        MetadataImageResult.Available result)
    {
        MetadataImageOverview overview = result.Overview;
        MetadataRootInspection? root = result.Root;
        if (root is not null && root.RequestedRoot != requestedRoot)
        {
            throw new InvalidOperationException(
                "The metadata query returned a different requested root.");
        }
        if (requestedRoot == MetadataRootKind.ReadyToRunManifest
            && root is null)
        {
            throw new InvalidOperationException(
                "A ReadyToRun manifest result has no root identity.");
        }

        MetadataCorHeaderSummary? cor = overview.Headers.Cor;
        return new BrowserMetadataImage(
            requestedRoot.ToString(),
            root?.Identity.Kind.ToString(),
            root?.Identity.RelativeVirtualAddress,
            root?.Identity.Size,
            requestedRoot == MetadataRootKind.ReadyToRunManifest
                && root?.Identity.Kind == MetadataRootKind.Cli,
            overview.MetadataVersion.ToString(),
            overview.MetadataVersion.IsTruncated,
            overview.Kind.ToString(),
            overview.IsAssembly,
            overview.MetadataSize,
            overview.Tables.Count(
                table => table.IsProjected && table.RowCount > 0),
            [
                .. overview.Heaps.Select(heap => new BrowserMetadataHeap(
                    heap.Heap.ToString(),
                    heap.SizeInBytes,
                    heap.MaxAddress,
                    heap.Addressing.ToString())),
            ],
            [
                .. overview.Tables
                    .Where(table => table.RowCount > 0)
                    .Select(table => new BrowserMetadataTable(
                        (int)table.Index,
                        table.Name,
                        table.RowCount,
                        table.IsProjected)),
            ],
            new BrowserMetadataHeaders(
                overview.Headers.Machine.ToString(),
                overview.Headers.IsPE32Plus,
                overview.Headers.Subsystem.ToString(),
                cor?.Flags.ToString(),
                cor?.MajorRuntimeVersion,
                cor?.MinorRuntimeVersion,
                cor?.EntryPointToken,
                cor?.ManagedNativeHeaderDirectory.RelativeVirtualAddress ?? 0,
                cor?.ManagedNativeHeaderDirectory.Size ?? 0));
    }

    static string? ProjectReadyToRun(
        AssemblyContextEntry<ReadyToRunImageResult> entry,
        out BrowserReadyToRunImage? result)
    {
        result = null;
        if (entry
            is not AssemblyContextEntry<ReadyToRunImageResult>.Available
                available)
        {
            return MetadataFailure(entry);
        }

        switch (available.Value)
        {
            case ReadyToRunImageResult.Available ready:
                result = ProjectReadyToRunImage(ready.Overview);
                return null;
            case ReadyToRunImageResult.NotReadyToRun:
                return null;
            case ReadyToRunImageResult.Failed failed:
                return BrowserSurfaceProjection.FailedAssembly(
                    failed.Error);
            default:
                throw new InvalidOperationException(
                    "Unknown ReadyToRun image result.");
        }
    }

    static BrowserReadyToRunImage ProjectReadyToRunImage(
        ReadyToRunImageOverview overview) =>
        new(
            overview.Role.ToString(),
            overview.Advertisements.ToString(),
            overview.MajorVersion,
            overview.MinorVersion,
            (uint)overview.Flags,
            overview.Flags.ToString(),
            overview.HeaderRelativeVirtualAddress,
            overview.HeaderEncodedSize,
            overview.ManagedNativeHeaderDirectory?
                .RelativeVirtualAddress,
            overview.ManagedNativeHeaderDirectory?.Size,
            overview.ExportHeaderRelativeVirtualAddress,
            overview.ManifestMetadata is { } manifest
                ? new BrowserReadyToRunManifest(
                    manifest.RelativeVirtualAddress,
                    manifest.Size,
                    manifest.AliasesCliMetadataDirectory)
                : null,
            [
                .. overview.Sections.Select(section =>
                    new BrowserReadyToRunSection(
                        Enum.IsDefined(section.Type)
                            ? section.Type.ToString()
                            : "Unknown",
                        (uint)section.Type,
                        section.RelativeVirtualAddress,
                        section.Size,
                        section.AliasesCliMetadataDirectory)),
            ]);

    internal static BrowserMetadataWindow ProjectMetadataWindow(
        string assembly,
        int requestedTableIndex,
        AssemblyContextEntry<MetadataTableWindow> result)
    {
        if (result
            is not AssemblyContextEntry<MetadataTableWindow>.Available
                available)
        {
            return EmptyMetadataWindow(
                assembly,
                requestedTableIndex,
                MetadataFailure(result));
        }

        MetadataTableWindow window = available.Value;
        MetadataTableView table = window.Table;
        return new BrowserMetadataWindow(
            assembly,
            (int)table.Index,
            table.Name,
            table.RowCount,
            window.StartRowId,
            [
                .. table.Columns.Select(column =>
                    new BrowserMetadataColumn(
                        column.Name,
                        column.Kind.ToString(),
                        [
                            .. column.CandidateTargets.Select(
                                candidate => (int)candidate),
                        ])),
            ],
            [
                .. table.Rows.Select(row => new BrowserMetadataRow(
                    row.RowId,
                    row.Token,
                    [.. row.Cells.Select(ProjectMetadataCell)])),
            ],
            table.Truncation is not null,
            null);
    }

    internal static BrowserHeapListing ProjectHeapListing(
        string assembly,
        HeapKind heap,
        AssemblyContextEntry<MetadataHeapEntrySet> result)
    {
        if (result
            is not AssemblyContextEntry<MetadataHeapEntrySet>.Available
                available)
        {
            return EmptyHeapListing(
                assembly,
                heap,
                MetadataFailure(result));
        }

        MetadataHeapEntrySet set = available.Value;
        return new BrowserHeapListing(
            assembly,
            set.Heap.ToString(),
            HeapStreamName(set.Heap),
            set.Coverage.ToString(),
            [
                .. set.Entries.Select(entry => new BrowserHeapEntry(
                    entry.Offset,
                    ProjectMetadataCell(entry.Value),
                    entry.ReferenceCount)),
            ],
            set.RowsTruncated,
            set.EntriesTruncated,
            null);
    }

    static BrowserMetadataCell ProjectMetadataCell(
        MetadataValue value) =>
        value switch
        {
            MetadataValue.Nil =>
                new BrowserMetadataCell("nil"),
            MetadataValue.Scalar scalar =>
                new BrowserMetadataCell(
                    "scalar",
                    Raw: scalar.Raw,
                    Display: scalar.Display),
            MetadataValue.Flags flags =>
                new BrowserMetadataCell(
                    "flags",
                    Raw: flags.Raw,
                    Decoded: flags.Decoded),
            MetadataValue.HeapReference heap =>
                new BrowserMetadataCell(
                    "heap",
                    Heap: heap.Heap.ToString(),
                    Text: heap.Text?.ToString(),
                    Preview: heap.Preview.ToString(),
                    Offset: heap.Offset,
                    Length: heap.Length,
                    Truncated: heap.Truncated),
            MetadataValue.Handle handle =>
                new BrowserMetadataCell(
                    "handle",
                    Display: handle.Reference.Display?.ToString(),
                    Truncated:
                        handle.Reference.Display?.IsTruncated ?? false,
                    TargetTable: (int)handle.Reference.TargetTable,
                    TargetRowId: handle.Reference.TargetRowId,
                    Token: handle.Reference.Token),
            MetadataValue.Range range =>
                new BrowserMetadataCell(
                    "range",
                    TargetTable: (int)range.Reference.TargetTable,
                    StartRowId: range.Reference.StartRowId,
                    EndRowId: range.Reference.EndRowId,
                    Count: range.Reference.Count),
            MetadataValue.Malformed malformed =>
                new BrowserMetadataCell(
                    "malformed",
                    Detail: malformed.Detail.ToString()),
            _ => throw new InvalidOperationException(
                "Unknown metadata cell value."),
        };

    static BrowserMetadataWindow EmptyMetadataWindow(
        string assembly,
        int tableIndex,
        string error) =>
        new(
            assembly,
            tableIndex,
            tableIndex is >= 0 and <= 63
                ? ((TableIndex)tableIndex).ToString()
                : $"#{tableIndex}",
            0,
            MetadataProjectionOptions.DefaultStartRowId,
            [],
            [],
            false,
            error);

    static BrowserHeapListing EmptyHeapListing(
        string assembly,
        HeapKind heap,
        string error) =>
        new(
            assembly,
            heap.ToString(),
            HeapStreamName(heap),
            MetadataHeapCoverage.NotEnumerable.ToString(),
            [],
            false,
            false,
            error);

    static string MetadataFailure<TValue>(
        AssemblyContextEntry<TValue> entry) =>
        BrowserSurfaceProjection.Failure(entry)
        ?? throw new ArgumentException(
            "An available assembly-context entry has no failure.",
            nameof(entry));

    static BrowserWorkspaceParticipant MetadataParticipant(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate,
        string assemblyFileName) =>
        scope.LibraryParticipant(coordinate, assemblyFileName);

    static HeapKind ParseHeap(string heap) =>
        Enum.TryParse(heap, ignoreCase: true, out HeapKind parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException(
                $"'{heap}' is not a metadata heap.",
                nameof(heap));

    static MetadataRootKind ParseMetadataRoot(string root) =>
        root switch
        {
            "cli" => MetadataRootKind.Cli,
            "r2r-manifest" => MetadataRootKind.ReadyToRunManifest,
            _ => throw new ArgumentException(
                $"'{root}' is not a metadata root.",
                nameof(root)),
        };

    static string HeapStreamName(HeapKind heap) =>
        heap switch
        {
            HeapKind.String => "#Strings",
            HeapKind.Blob => "#Blob",
            HeapKind.Guid => "#GUID",
            HeapKind.UserString => "#US",
            _ => throw new ArgumentOutOfRangeException(nameof(heap)),
        };

    static string PlatformAssemblyFileName(string assembly) =>
        assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? assembly
            : $"{assembly}.dll";
}
