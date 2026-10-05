using InertText;

namespace DotnetInspector.Packages;

public sealed partial class PackageHouse
{
    private sealed record PackageHouseLibraryInventoryRowResolution(
        PackageCompileAsset CompileAsset,
        PackageContentEntry CompileEntry,
        PackageCompileAsset? ImplementationAsset,
        PackageContentEntry? ImplementationEntry,
        PackageHousePortablePdbEvidenceKind PortablePdbEvidence,
        PackageContentEntry? PortablePdbEntry);

    private sealed record PackageHouseLibraryInventoryResolution(
        IReadOnlyList<PackageHouseLibraryInventoryRowResolution> Rows,
        IReadOnlyList<string> MaterializedEntryPaths,
        int SelectedRowIndex,
        InertString? SelectionFailure,
        PackageHouseContentNarrowingCompletion Completion,
        InertString? Reason);

    private static PackageHouseLibraryInventoryResolution
        ResolveLibraryInventory(
            PackageHouseContentNarrowingResolution narrowing,
            IReadOnlyList<PackageContentEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(narrowing);
        ArgumentNullException.ThrowIfNull(entries);
        if (narrowing.TargetSelection is not { } targetSelection)
        {
            return new(
                [],
                [],
                0,
                SelectionFailure: null,
                narrowing.Completion,
                narrowing.Reason);
        }

        PackageCompileAssetSelection selection =
            targetSelection.Selection;
        if (!selection.IsSelected)
        {
            bool emptyCompileGroup =
                selection.Status
                    == PackageCompileAssetSelectionStatus
                        .EmptyCompileGroup;
            return new(
                [],
                [],
                0,
                SelectionFailure: null,
                emptyCompileGroup
                    ? PackageHouseContentNarrowingCompletion.NoMatch
                    : narrowing.Completion,
                emptyCompileGroup
                    ? Reason(
                        "The selected package target contains an explicit empty compile group and no compatible Libraries.")
                    : narrowing.Reason);
        }

        var rows =
            new List<PackageHouseLibraryInventoryRowResolution>(
                selection.Assets.Count);
        foreach (PackageCompileAsset compileAsset in selection.Assets)
        {
            if (!TryFindExactEntry(
                    entries,
                    compileAsset.Path,
                    out PackageContentEntry compileEntry,
                    out InertString? entryFailure))
            {
                return new(
                    [],
                    [],
                    0,
                    SelectionFailure: null,
                    PackageHouseContentNarrowingCompletion.Rejected,
                    entryFailure);
            }

            PackageCompileAsset? implementationAsset =
                selection.FindImplementationAsset(compileAsset);
            PackageContentEntry? implementationEntry = null;
            PackageContentEntry? portablePdbEntry = null;
            PackageHousePortablePdbEvidenceKind portablePdbEvidence;
            if (implementationAsset is null)
            {
                portablePdbEvidence =
                    PackageHousePortablePdbEvidenceKind.NotApplicable;
            }
            else
            {
                if (!TryFindExactEntry(
                        entries,
                        implementationAsset.Path,
                        out PackageContentEntry resolvedImplementation,
                        out entryFailure))
                {
                    return new(
                        [],
                        [],
                        0,
                        SelectionFailure: null,
                        PackageHouseContentNarrowingCompletion.Rejected,
                        entryFailure);
                }
                implementationEntry = resolvedImplementation;
                string? portablePdbPath =
                    implementationAsset.PortablePdbCompanionPath;
                if (portablePdbPath is null)
                {
                    portablePdbEvidence =
                        PackageHousePortablePdbEvidenceKind.Absent;
                }
                else
                {
                    PackageContentEntry[] matches =
                    [
                        .. entries.Where(entry =>
                            entry.Path.Equals(
                                portablePdbPath,
                                StringComparison.OrdinalIgnoreCase)),
                    ];
                    if (matches.Length > 1)
                    {
                        return new(
                            [],
                            [],
                            0,
                            SelectionFailure: null,
                            PackageHouseContentNarrowingCompletion.Rejected,
                            Reason(
                                $"The selected target contains more than one entry matching '{portablePdbPath}'."));
                    }
                    if (matches.Length == 1)
                    {
                        portablePdbEntry = matches[0];
                        portablePdbEvidence =
                            PackageHousePortablePdbEvidenceKind.Listed;
                    }
                    else
                    {
                        portablePdbEvidence =
                            PackageHousePortablePdbEvidenceKind.Absent;
                    }
                }
            }

            rows.Add(
                new(
                    compileAsset,
                    compileEntry,
                    implementationAsset,
                    implementationEntry,
                    portablePdbEvidence,
                    portablePdbEntry));
        }

        PackageHouseLibraryInventoryRowResolution[] ordered =
        [
            .. rows
                .OrderBy(
                    static row => Path.GetFileNameWithoutExtension(
                        row.CompileEntry.Path),
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static row => row.CompileEntry.Path,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static row => row.CompileEntry.Path,
                    StringComparer.Ordinal),
        ];
        if (ordered.Length == 0)
        {
            return new(
                [],
                [],
                0,
                SelectionFailure: null,
                PackageHouseContentNarrowingCompletion.NoMatch,
                Reason(
                    "The selected package target contains no compatible Libraries."));
        }

        return new(
            ordered,
            [ordered[0].CompileEntry.Path],
            SelectedRowIndex: 0,
            SelectionFailure: null,
            PackageHouseContentNarrowingCompletion.Settled,
            Reason: null);
    }

    private static bool TryFindExactEntry(
        IReadOnlyList<PackageContentEntry> entries,
        string path,
        out PackageContentEntry entry,
        out InertString? failure)
    {
        PackageContentEntry[] exact =
        [
            .. entries.Where(candidate =>
                candidate.Path.Equals(path, StringComparison.Ordinal)),
        ];
        if (exact.Length == 1)
        {
            entry = exact[0];
            failure = null;
            return true;
        }

        PackageContentEntry[] matches =
        [
            .. entries.Where(candidate =>
                candidate.Path.Equals(
                    path,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (matches.Length == 1)
        {
            entry = matches[0];
            failure = null;
            return true;
        }

        entry = default;
        failure = Reason(
            matches.Length == 0
                ? $"The selected target does not contain '{path}'."
                : $"The selected target contains more than one entry matching '{path}'.");
        return false;
    }

    private static PackageHouseLibraryAndInventory
        CreateLibraryAndInventory(
            PackageHouseAcquisitionReceipt acquisition,
            PackageHouseContentNarrowingReceipt narrowing,
            PackageHouseLibraryInventoryResolution resolution,
            string packageId)
    {
        PackageCompileAssetSelectionReceipt targetSelection =
            narrowing.TargetSelection
            ?? throw new InvalidOperationException(
                "Library inventory requires target-selection evidence.");
        PackageHouseLibraryInventoryRow[] rows =
        [
            .. resolution.Rows.Select(row =>
                new PackageHouseLibraryInventoryRow(
                    narrowing,
                    targetSelection,
                    row.CompileAsset,
                    row.CompileEntry,
                    row.ImplementationAsset,
                    row.ImplementationEntry,
                    row.PortablePdbEvidence,
                    row.PortablePdbEntry)),
        ];
        var inventory = new PackageHouseLibraryInventory(
            narrowing,
            rows);
        PackageHouseLibraryInventoryRow selectedRow =
            rows[resolution.SelectedRowIndex];
        var handoff = new PackageHouseLibraryHandoff.Compile(
            acquisition,
            targetSelection,
            selectedRow.CompileAsset,
            selectedRow.ImplementationAsset);
        var receipt =
            new PackageHouseLibraryInventorySelectionReceipt(
                inventory,
                selectedRow,
                packageId);
        return new(inventory, handoff, receipt);
    }
}
