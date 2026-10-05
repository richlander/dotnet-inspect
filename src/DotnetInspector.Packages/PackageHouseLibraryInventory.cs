using System.Collections.Immutable;

namespace DotnetInspector.Packages;

public enum PackageHousePortablePdbEvidenceKind
{
    Listed,
    Absent,
    NotApplicable,
}

/// <summary>
/// One resource-free logical Library row issued from one validated package
/// directory snapshot.
/// </summary>
public sealed class PackageHouseLibraryInventoryRow
{
    internal PackageHouseLibraryInventoryRow(
        PackageHouseContentNarrowingReceipt narrowing,
        PackageCompileAssetSelectionReceipt targetSelection,
        PackageCompileAsset compileAsset,
        PackageContentEntry compileEntry,
        PackageCompileAsset? implementationAsset,
        PackageContentEntry? implementationEntry,
        PackageHousePortablePdbEvidenceKind portablePdbEvidence,
        PackageContentEntry? portablePdbEntry)
    {
        Narrowing = narrowing;
        TargetSelection = targetSelection;
        CompileAsset = compileAsset;
        CompileEntry = compileEntry;
        ImplementationAsset = implementationAsset;
        ImplementationEntry = implementationEntry;
        PortablePdbEvidence = portablePdbEvidence;
        PortablePdbEntry = portablePdbEntry;
    }

    public PackageHouseContentNarrowingReceipt Narrowing { get; }

    public PackageCompileAssetSelectionReceipt TargetSelection { get; }

    public PackageCompileAsset CompileAsset { get; }

    public PackageContentEntry CompileEntry { get; }

    public PackageCompileAsset? ImplementationAsset { get; }

    public PackageContentEntry? ImplementationEntry { get; }

    public PackageHousePortablePdbEvidenceKind PortablePdbEvidence { get; }

    public PackageContentEntry? PortablePdbEntry { get; }
}

/// <summary>
/// The complete logical Library inventory for one owner-issued package target.
/// </summary>
public sealed class PackageHouseLibraryInventory
{
    internal PackageHouseLibraryInventory(
        PackageHouseContentNarrowingReceipt narrowing,
        IEnumerable<PackageHouseLibraryInventoryRow> rows)
    {
        Narrowing = narrowing;
        Rows = [.. rows];
        if (Rows.Any(row => !ReferenceEquals(row.Narrowing, narrowing)))
        {
            throw new ArgumentException(
                "Every Library row must belong to this inventory's narrowing.",
                nameof(rows));
        }
    }

    public PackageHouseContentNarrowingReceipt Narrowing { get; }

    public ImmutableArray<PackageHouseLibraryInventoryRow> Rows { get; }

    /// <summary>
    /// Creates a later exact Files query from package-entry references issued
    /// by this inventory.
    /// </summary>
    public PackageHouseContentQuery CreateFilesQuery(
        IEnumerable<PackageContentEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        PackageContentEntry[] selected = [.. entries];
        HashSet<PackageContentEntry> issued =
        [
            .. Rows.SelectMany(static row =>
                new PackageContentEntry?[]
                {
                    row.CompileEntry,
                    row.ImplementationEntry,
                    row.PortablePdbEntry,
                })
                .OfType<PackageContentEntry>(),
        ];
        if (selected.Length == 0 || selected.Any(entry => !issued.Contains(entry)))
        {
            throw new ArgumentException(
                "Every exact file reference must be issued by this Library inventory.",
                nameof(entries));
        }

        return new PackageHouseContentQuery(
            Narrowing.Narrowing,
            [
                new PackageHouseContentTerminal.Files(
                    selected.Select(static entry => entry.Path)),
            ],
            retainedFileList: null,
            this);
    }
}

/// <summary>
/// Resource-free evidence linking the selected Library to its exact inventory
/// row and policy outcome.
/// </summary>
public sealed class PackageHouseLibraryInventorySelectionReceipt
{
    internal PackageHouseLibraryInventorySelectionReceipt(
        PackageHouseLibraryInventory inventory,
        PackageHouseLibraryInventoryRow selectedRow,
        string packageId)
    {
        if (!inventory.Rows.Contains(selectedRow))
        {
            throw new ArgumentException(
                "The selected Library row must belong to this inventory.",
                nameof(selectedRow));
        }

        Inventory = inventory;
        SelectedRow = selectedRow;
        IsPackageNamesake =
            Path.GetFileNameWithoutExtension(
                    selectedRow.CompileEntry.Path)
                .Equals(
                    packageId,
                    StringComparison.OrdinalIgnoreCase);
    }

    public PackageHouseLibraryInventory Inventory { get; }

    public PackageHouseLibraryInventoryRow SelectedRow { get; }

    public bool IsPackageNamesake { get; }
}

/// <summary>
/// The selected Library handoff, complete target inventory, and receipt
/// produced by the composite content terminal.
/// </summary>
public sealed class PackageHouseLibraryAndInventory
{
    internal PackageHouseLibraryAndInventory(
        PackageHouseLibraryInventory inventory,
        PackageHouseLibraryHandoff.Compile selectedLibrary,
        PackageHouseLibraryInventorySelectionReceipt selection)
    {
        Inventory = inventory;
        SelectedLibrary = selectedLibrary;
        Selection = selection;
    }

    public PackageHouseLibraryInventory Inventory { get; }

    public PackageHouseLibraryHandoff.Compile SelectedLibrary { get; }

    public PackageHouseLibraryInventorySelectionReceipt Selection { get; }
}
