using System.Collections.Immutable;
using System.CommandLine;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.MetadataRendering;
using ILInspector.Metadata;

namespace Mdi;

/// <summary>
/// The <c>mdi</c> command: inspect the ECMA-335 metadata tables of a .NET
/// assembly and render them as Markdown, TSV, or JSONL. The front-end maps its
/// flags onto <see cref="MetadataProjectionOptions"/> and delegates rendering to
/// <see cref="MetadataProjectionRenderer"/>; the projection and rendering logic
/// live in reusable libraries so this tool stays a thin shell.
/// </summary>
public static class MdiCommand
{
    /// <summary>Builds and runs the command over <paramref name="args"/>.</summary>
    public static int Invoke(string[] args) => CreateRootCommand().Parse(args).Invoke();

    /// <summary>Builds the System.CommandLine surface (also used by tests).</summary>
    public static RootCommand CreateRootCommand()
    {
        var assemblyArgument = new Argument<string>("assembly")
        {
            Description = "Path to a .NET assembly (.dll or .exe).",
        };

        var tableOption = new Option<string?>("--table")
        {
            Description = "Comma-separated ECMA-335 table names to include (for example TypeDef,MethodDef). Default: every supported table.",
        };
        tableOption.Aliases.Add("-t");

        var formatOption = new Option<string>("--format")
        {
            Description = "Table format: md (default), tsv, or jsonl.",
            DefaultValueFactory = _ => "md",
        };
        formatOption.Aliases.Add("-f");

        var maxRowsOption = new Option<int>("--max-rows")
        {
            Description = $"Maximum rows projected per table before explicit truncation (default {MetadataProjectionOptions.DefaultMaxRowsPerTable}).",
            DefaultValueFactory = _ => MetadataProjectionOptions.DefaultMaxRowsPerTable,
        };
        maxRowsOption.Aliases.Add("-n");

        var startRowOption = new Option<int>("--start-row")
        {
            Description = $"1-based row id each table starts at, forming a row window with --max-rows (default {MetadataProjectionOptions.DefaultStartRowId}).",
            DefaultValueFactory = _ => MetadataProjectionOptions.DefaultStartRowId,
        };
        startRowOption.Aliases.Add("-s");

        var referencesOption = new Option<string?>("--references")
        {
            Description = "Instead of dumping tables, list the rows pointing at one row, given as Table:RowId (for example TypeDef:5). Includes list-column ownership, so Field:1 names its declaring type.",
        };
        referencesOption.Aliases.Add("-r");

        var maxReferencesOption = new Option<int>("--max-references")
        {
            Description = $"Maximum references collected by --references before the scan stops (default {MetadataRowReferenceSet.DefaultMaxReferences}).",
            DefaultValueFactory = _ => MetadataRowReferenceSet.DefaultMaxReferences,
        };

        var maxBytesOption = new Option<int>("--max-bytes")
        {
            Description = $"Maximum blob-preview bytes per cell (default {MetadataProjectionOptions.DefaultMaxPreviewBytes}).",
        };
        maxBytesOption.DefaultValueFactory = _ => MetadataProjectionOptions.DefaultMaxPreviewBytes;

        var maxCharsOption = new Option<int>("--max-chars")
        {
            Description = $"Maximum decoded string characters per cell (default {MetadataProjectionOptions.DefaultMaxStringChars}).",
        };
        maxCharsOption.DefaultValueFactory = _ => MetadataProjectionOptions.DefaultMaxStringChars;

        var root = new RootCommand("mdi \u2014 inspect the ECMA-335 metadata tables of a .NET assembly.");
        root.Arguments.Add(assemblyArgument);
        root.Options.Add(tableOption);
        root.Options.Add(formatOption);
        root.Options.Add(maxRowsOption);
        root.Options.Add(startRowOption);
        root.Options.Add(referencesOption);
        root.Options.Add(maxReferencesOption);
        root.Options.Add(maxBytesOption);
        root.Options.Add(maxCharsOption);

        root.SetAction(parseResult =>
        {
            string assembly = parseResult.GetValue(assemblyArgument)!;
            string? tableSpec = parseResult.GetValue(tableOption);
            string formatText = parseResult.GetValue(formatOption)!;
            int maxRows = parseResult.GetValue(maxRowsOption);
            int startRow = parseResult.GetValue(startRowOption);
            string? referenceSpec = parseResult.GetValue(referencesOption);
            int maxReferences = parseResult.GetValue(maxReferencesOption);
            int maxBytes = parseResult.GetValue(maxBytesOption);
            int maxChars = parseResult.GetValue(maxCharsOption);

            if (!TryParseFormat(formatText, out var format))
            {
                Console.Error.WriteLine($"Error: unknown format '{formatText}'. Use md, tsv, or jsonl.");
                return 1;
            }

            if (maxRows < 0 || maxBytes < 0 || maxChars < 0)
            {
                Console.Error.WriteLine("Error: --max-rows, --max-bytes, and --max-chars must be non-negative.");
                return 1;
            }

            if (startRow < 1)
            {
                Console.Error.WriteLine("Error: --start-row must be 1 or greater (row ids are 1-based).");
                return 1;
            }

            if (!TryParseTables(tableSpec, out var tables, out var badName))
            {
                Console.Error.WriteLine(
                    $"Error: unknown table '{badName}'. Table names are members of System.Reflection.Metadata.Ecma335.TableIndex (for example TypeDef, MethodDef, Field).");
                return 1;
            }

            if (referenceSpec is not null)
            {
                if (maxReferences < 0)
                {
                    Console.Error.WriteLine("Error: --max-references must be non-negative.");
                    return 1;
                }

                if (!TryParseRowLocation(referenceSpec, out var targetTable, out int targetRowId, out string? specError))
                {
                    Console.Error.WriteLine($"Error: {specError}");
                    return 1;
                }

                return ExecuteReferences(
                    assembly, targetTable, targetRowId, maxReferences, format, Console.Out, Console.Error);
            }

            var options = new MetadataProjectionOptions
            {
                MaxRowsPerTable = maxRows,
                StartRowId = startRow,
                MaxPreviewBytes = maxBytes,
                MaxStringChars = maxChars,
                Tables = tables,
            };

            return Execute(assembly, options, format, Console.Out, Console.Error);
        });

        return root;
    }

    /// <summary>
    /// Opens <paramref name="assemblyPath"/>, projects its metadata tables, and
    /// renders them. Returns a process exit code; all diagnostics are surfaced on
    /// <paramref name="error"/> and never collapsed into success-shaped output.
    /// </summary>
    public static int Execute(
        string assemblyPath,
        MetadataProjectionOptions options,
        MetadataTableFormat format,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!File.Exists(assemblyPath))
        {
            error.WriteLine($"Error: file not found: {assemblyPath}");
            return 1;
        }

        MetadataTableProjection projection;
        try
        {
            using var session = AssemblyInspectionSession.Open(assemblyPath);
            if (!session.HasMetadata)
            {
                error.WriteLine($"Error: '{assemblyPath}' contains no .NET metadata (not a managed assembly).");
                return 1;
            }

            projection = session.MetadataTables(options);
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            error.WriteLine($"Error: cannot read metadata from '{assemblyPath}': {ex.Message}");
            return 1;
        }

        if (projection.Tables.IsEmpty)
        {
            error.WriteLine("No metadata tables matched the current selection.");
            return 0;
        }

        MetadataProjectionRenderer.Render(projection, output, format);

        // Markdown announces truncation inline in each table heading. The machine
        // formats carry no heading, so a bounded table would otherwise be
        // indistinguishable from a complete one (and a fully-clipped request could
        // even emit nothing at all). Report truncation on the error writer so it
        // stays visible without polluting the row stream on stdout.
        if (format != MetadataTableFormat.Markdown)
        {
            foreach (var table in projection.Tables)
            {
                if (table.Truncation is not { } truncation)
                    continue;

                // Name the window, not just its size: "4 of 100 rows" would read as
                // the first four rows even when --start-row moved the window.
                string window = table.Rows.IsEmpty
                    ? "no rows"
                    : $"rows {table.Rows[0].RowId} to {table.Rows[^1].RowId}";

                error.WriteLine(
                    $"Note: table {table.Name} shows {window} of {truncation.RowCount}; raise --max-rows or move --start-row to include more.");
            }
        }

        return 0;
    }

    /// <summary>
    /// Opens <paramref name="assemblyPath"/> and renders every row pointing at
    /// <paramref name="targetTable"/>[<paramref name="targetRowId"/>]. Returns a
    /// process exit code.
    ///
    /// A row id that does not exist is not an error: the answer is an empty set,
    /// which is what the projection itself reports, since a handle pointing past
    /// the end of a table is malformed rather than a reference.
    /// </summary>
    public static int ExecuteReferences(
        string assemblyPath,
        TableIndex targetTable,
        int targetRowId,
        int maxReferences,
        MetadataTableFormat format,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (targetRowId < 1)
        {
            error.WriteLine("Error: the row id in --references must be 1 or greater (row ids are 1-based).");
            return 1;
        }

        if (!File.Exists(assemblyPath))
        {
            error.WriteLine($"Error: file not found: {assemblyPath}");
            return 1;
        }

        MetadataRowReferenceSet references;
        try
        {
            using var session = AssemblyInspectionSession.Open(assemblyPath);
            if (!session.HasMetadata)
            {
                error.WriteLine($"Error: '{assemblyPath}' contains no .NET metadata (not a managed assembly).");
                return 1;
            }

            references = session.MetadataReferences(targetTable, targetRowId, maxReferences);
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            error.WriteLine($"Error: cannot read metadata from '{assemblyPath}': {ex.Message}");
            return 1;
        }

        MetadataProjectionRenderer.Render(references, output, format);

        // Markdown renders the search's blind spots inline. The machine formats
        // are pure row streams, so an incomplete scan would look identical to a
        // complete one; surface the same caveats on the error writer instead.
        if (format != MetadataTableFormat.Markdown)
        {
            foreach (string caveat in MetadataProjectionRenderer.Caveats(references))
                error.WriteLine($"Note: {caveat}");
        }

        return 0;
    }

    /// <summary>
    /// Whether <paramref name="ex"/> is an expected failure of opening or reading
    /// an assembly file — a bad image, an unreadable or inaccessible file, or an
    /// invalid path — as opposed to an unexpected programming error. These are
    /// turned into a clean diagnostic and a non-zero exit; anything else is left
    /// to propagate.
    /// </summary>
    internal static bool IsExpectedReadFailure(Exception ex) =>
        ex is BadImageFormatException
           or InvalidOperationException
           or IOException
           or UnauthorizedAccessException
           or ArgumentException
           or NotSupportedException;

    static bool TryParseFormat(string text, out MetadataTableFormat format)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "md":
            case "markdown":
                format = MetadataTableFormat.Markdown;
                return true;
            case "tsv":
                format = MetadataTableFormat.Tsv;
                return true;
            case "jsonl":
                format = MetadataTableFormat.Jsonl;
                return true;
            default:
                format = MetadataTableFormat.Markdown;
                return false;
        }
    }

    /// <summary>
    /// Parses a <c>Table:RowId</c> row reference (for example <c>TypeDef:5</c>).
    /// The two halves are validated separately so the diagnostic names the half
    /// that is wrong.
    /// </summary>
    static bool TryParseRowLocation(string spec, out TableIndex table, out int rowId, out string? error)
    {
        table = default;
        rowId = 0;

        int separator = spec.LastIndexOf(':');
        if (separator < 0)
        {
            error = $"'{spec}' is not a row reference. Use Table:RowId, for example TypeDef:5.";
            return false;
        }

        string tableName = spec[..separator].Trim();
        string rowText = spec[(separator + 1)..].Trim();

        if (!Enum.TryParse(tableName, ignoreCase: true, out table) || !Enum.IsDefined(table))
        {
            error = $"unknown table '{tableName}'. Table names are members of System.Reflection.Metadata.Ecma335.TableIndex (for example TypeDef, MethodDef, Field).";
            return false;
        }

        if (!int.TryParse(rowText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out rowId) || rowId < 1)
        {
            error = $"'{rowText}' is not a row id. Row ids are 1-based positive integers.";
            return false;
        }

        error = null;
        return true;
    }

    static bool TryParseTables(string? spec, out ImmutableArray<TableIndex> tables, out string? badName)    {
        badName = null;

        // A default (unset) array is the projector's signal to include every
        // supported table; an empty spec keeps that default.
        if (string.IsNullOrWhiteSpace(spec))
        {
            tables = default;
            return true;
        }

        var builder = ImmutableArray.CreateBuilder<TableIndex>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<TableIndex>(part, ignoreCase: true, out var index) || !Enum.IsDefined(index))
            {
                badName = part;
                tables = default;
                return false;
            }

            builder.Add(index);
        }

        tables = builder.ToImmutable();
        return true;
    }
}
