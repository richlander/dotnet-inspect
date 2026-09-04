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
        string targetFramework)
    {
        BrowserInspectionScope scope =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserPackageCoordinate coordinate = scope.Coordinates[0];
        BrowserCompileLibraryAvailability compileLibrary =
            BrowserMetadataWireProjection.Project(
                BrowserCompileLibraryProjection.Project(coordinate.Selection));
        if (!coordinate.Selection.IsSelected)
        {
            return JsonSerializer.Serialize(
                new BrowserPackageMetadata(
                    Assemblies: [],
                    InspectionError: null,
                    compileLibrary),
                BrowserMetadataJsonContext.Default.BrowserPackageMetadata);
        }

        var assemblies = new List<BrowserAssemblyMetadata>();
        var failures = new List<string>();
        foreach (BrowserWorkspaceParticipant participant
            in MetadataParticipants(scope, coordinate))
        {
            AssemblyContextEntry<MetadataImageOverview> result =
                scope.UseMetadataParticipant(
                    participant,
                    (group, selected) =>
                    AssemblyContextMetadataImageQuery.ExecuteParticipant(
                        group,
                        selected));
            if (result
                is AssemblyContextEntry<MetadataImageOverview>.Available
                    available)
            {
                assemblies.Add(
                    ProjectMetadataAssembly(
                        participant.Asset.AssemblyName,
                        available.Value));
            }
            else
            {
                failures.Add(MetadataFailure(result));
            }
        }

        return JsonSerializer.Serialize(
            new BrowserPackageMetadata(
                [.. assemblies],
                failures.Count == 0 ? null : string.Join("; ", failures),
                compileLibrary),
            BrowserMetadataJsonContext.Default.BrowserPackageMetadata);
    }

    [JSExport]
    public static async Task<string> QueryPackageMetadataTable(
        string packageId,
        string version,
        string targetFramework,
        string assemblyFileName,
        int tableIndex,
        int startRowId,
        int maxRows)
    {
        BrowserInspectionScope scope =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserWorkspaceParticipant participant = MetadataParticipant(
            scope,
            scope.Coordinates[0],
            assemblyFileName);
        var request = new MetadataTableWindowRequest(
            (TableIndex)tableIndex,
            startRowId,
            maxRows);
        AssemblyContextEntry<MetadataTableWindow> result =
            scope.UseMetadataParticipant(
                participant,
                (group, selected) =>
                AssemblyContextMetadataTableQuery.ExecuteParticipant(
                    group,
                    selected,
                    request));
        return JsonSerializer.Serialize(
            ProjectMetadataWindow(assemblyFileName, tableIndex, result),
            BrowserMetadataJsonContext.Default.BrowserMetadataWindow);
    }

    [JSExport]
    public static async Task<string> QueryPackageHeapEntries(
        string packageId,
        string version,
        string targetFramework,
        string assemblyFileName,
        string heap)
    {
        BrowserInspectionScope scope =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                targetFramework);
        BrowserWorkspaceParticipant participant = MetadataParticipant(
            scope,
            scope.Coordinates[0],
            assemblyFileName);
        HeapKind heapKind = ParseHeap(heap);
        AssemblyContextEntry<MetadataHeapEntrySet> result =
            scope.UseMetadataParticipant(
                participant,
                (group, selected) =>
                AssemblyContextMetadataHeapQuery.ExecuteParticipant(
                    group,
                    selected,
                    heapKind));
        return JsonSerializer.Serialize(
            ProjectHeapListing(assemblyFileName, heapKind, result),
            BrowserMetadataJsonContext.Default.BrowserHeapListing);
    }

    [JSExport]
    public static async Task<string> QueryPlatformMetadata(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack);
        AssemblyContextEntry<MetadataImageOverview> result =
            resolution.Scope.UseParticipant(
                resolution.Participant,
                AssemblyContextMetadataImageQuery.ExecuteParticipant);
        string assembly = PlatformAssemblyFileName(
            resolution.Participant.Participant.Assembly.Identity.Name);
        BrowserPackageMetadata metadata = result switch
        {
            AssemblyContextEntry<MetadataImageOverview>.Available available =>
                new BrowserPackageMetadata(
                    [ProjectMetadataAssembly(assembly, available.Value)],
                    null,
                    BrowserMetadataWireProjection.Project(
                        BrowserCompileLibraryProjection.Selected(
                            resolution.Scope.Framework))),
            _ => new BrowserPackageMetadata(
                [],
                MetadataFailure(result),
                BrowserMetadataWireProjection.Project(
                        BrowserCompileLibraryProjection.Selected(
                            resolution.Scope.Framework))),
        };
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
        int tableIndex,
        int startRowId,
        int maxRows)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack);
        var request = new MetadataTableWindowRequest(
            (TableIndex)tableIndex,
            startRowId,
            maxRows);
        AssemblyContextEntry<MetadataTableWindow> result =
            resolution.Scope.UseParticipant(
                resolution.Participant,
                (group, participant) =>
                    AssemblyContextMetadataTableQuery.ExecuteParticipant(
                        group,
                        participant,
                        request));
        return JsonSerializer.Serialize(
            ProjectMetadataWindow(
                PlatformAssemblyFileName(
                    resolution.Participant.Participant.Assembly.Identity.Name),
                tableIndex,
                result),
            BrowserMetadataJsonContext.Default.BrowserMetadataWindow);
    }

    [JSExport]
    public static async Task<string> QueryPlatformHeapEntries(
        string targetFramework,
        string platformVersion,
        string assemblyFileName,
        string pack,
        string heap)
    {
        await using BrowserPlatformScopeResolution resolution =
            await BrowserPlatformWorkspace.OpenAssemblyAsync(
                targetFramework,
                platformVersion,
                assemblyFileName,
                pack);
        HeapKind heapKind = ParseHeap(heap);
        AssemblyContextEntry<MetadataHeapEntrySet> result =
            resolution.Scope.UseParticipant(
                resolution.Participant,
                (group, participant) =>
                    AssemblyContextMetadataHeapQuery.ExecuteParticipant(
                        group,
                        participant,
                        heapKind));
        return JsonSerializer.Serialize(
            ProjectHeapListing(
                PlatformAssemblyFileName(
                    resolution.Participant.Participant.Assembly.Identity.Name),
                heapKind,
                result),
            BrowserMetadataJsonContext.Default.BrowserHeapListing);
    }

    internal static BrowserAssemblyMetadata ProjectMetadataAssembly(
        string assembly,
        MetadataImageOverview overview)
    {
        MetadataCorHeaderSummary? cor = overview.Headers.Cor;
        return new BrowserAssemblyMetadata(
            assembly,
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
                cor?.EntryPointToken));
    }

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

    static BrowserWorkspaceParticipant[] MetadataParticipants(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate) =>
    [
        .. scope.ImplementationParticipants
            .Concat(scope.ReferenceOnlySurfaceParticipants)
            .Where(participant => ReferenceEquals(
                participant.Coordinate.Root.Identity,
                coordinate.Root.Identity)),
    ];

    static BrowserWorkspaceParticipant MetadataParticipant(
        BrowserInspectionScope scope,
        BrowserPackageCoordinate coordinate,
        string assemblyFileName)
    {
        if (!coordinate.Selection.IsSelected)
        {
            throw new InvalidOperationException(
                $"{coordinate.PackageId} {coordinate.Version} has no selected "
                + $"compile library ({coordinate.Selection.Status}).");
        }

        BrowserWorkspaceParticipant[] matches =
        [
            .. MetadataParticipants(scope, coordinate).Where(participant =>
                participant.Asset.AssemblyName.Equals(
                    assemblyFileName,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Assembly '{assemblyFileName}' is not part of the selected metadata workspace."),
            _ => throw new InvalidOperationException(
                $"Assembly '{assemblyFileName}' is ambiguous in the selected metadata workspace."),
        };
    }

    static HeapKind ParseHeap(string heap) =>
        Enum.TryParse(heap, ignoreCase: true, out HeapKind parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException(
                $"'{heap}' is not a metadata heap.",
                nameof(heap));

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
