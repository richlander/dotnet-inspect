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
        root.Options.Add(maxBytesOption);
        root.Options.Add(maxCharsOption);

        root.SetAction(parseResult =>
        {
            string assembly = parseResult.GetValue(assemblyArgument)!;
            string? tableSpec = parseResult.GetValue(tableOption);
            string formatText = parseResult.GetValue(formatOption)!;
            int maxRows = parseResult.GetValue(maxRowsOption);
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

            if (!TryParseTables(tableSpec, out var tables, out var badName))
            {
                Console.Error.WriteLine(
                    $"Error: unknown table '{badName}'. Table names are members of System.Reflection.Metadata.Ecma335.TableIndex (for example TypeDef, MethodDef, Field).");
                return 1;
            }

            var options = new MetadataProjectionOptions
            {
                MaxRowsPerTable = maxRows,
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
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException)
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
        return 0;
    }

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

    static bool TryParseTables(string? spec, out ImmutableArray<TableIndex> tables, out string? badName)
    {
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
