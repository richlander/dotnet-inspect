using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.CommandLine;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Container for shared CLI options that are reused across multiple commands.
/// Created once in CreateRootCommand and passed to command builders.
/// </summary>
public class SharedOptions
{
    // Output format options
    public Option<bool> Json { get; } = new("--json") { Description = "Output as JSON" };
    public Option<bool> Markdown { get; } = new("--markdown") { Description = "Output as markdown" };
    public Option<bool> PlainText { get; } = new("--plaintext") { Description = "Output as plain text" };
    public Option<bool> Bare { get; } = new("--bare") { Description = "Render the selected payload without document decoration; does not change the selected shape" };
    public Option<bool> RawUrls { get; } = new("--raw") { Description = "Emit GitHub URLs as raw/fetchable URLs (default; URL-shape modifier, not an output-shape modifier)" };
    public Option<bool> BrowsableUrls { get; } = new("--blob") { Description = "Emit GitHub URLs as browser-friendly /blob/ URLs (URL-shape modifier, not an output-shape modifier)" };
    public Option<bool> Mermaid { get; } = new("--mermaid") { Description = "Output as mermaid diagram (standalone or with --markdown for embedded)" };
    public Option<bool> Taste { get; } = new("--taste") { Description = "Render source with the full oracle-endorsed style set (includes byte-divergent lenses); Annotated Source names the applied knobs on the signature" };
    public Option<bool> ReadableNames { get; } = new("--readable-names") { Description = "Synthesize readable names (from a local's type/role) for locals that have no usable PDB source name, instead of the V_index fallback; byte-preserving (names do not affect IL)" };
    public Option<string?> Focus { get; } = new("--focus") { Description = "Report a fact family with the caret gesture (underlined beneath the statement) instead of a trailing comment: a category (allocation), a descriptor id (alloc.box), or an id prefix (alloc). Promotes, never filters: unmatched facts keep their trailing comment", Arity = ArgumentArity.ExactlyOne };
    public Option<bool> Table { get; } = new("--table") { Description = "Output as a pretty table (space-padded columns)" };
    public Option<bool> Tsv { get; } = new("--tsv") { Description = "Output as normalized tab-separated values" };
    public Option<bool> Jsonl { get; } = new("--jsonl") { Description = "Output as JSON Lines (one object per row)" };
    public Option<bool> NoHeaders { get; } = new("--no-headers") { Description = "Suppress table/TSV column headers" };

    // Verbosity options
    public Option<bool> Verbose { get; } = new("--verbose") { Description = "Show progress messages on stderr" };
    public Option<string?> Verbosity { get; } = new("-v") { Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)", Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => null };

    // Output control options
    public Option<int?> Limit { get; }
    public Option<string?> Rows { get; } = new("--rows")
    {
        Description = "Select data rows per rendered table: a count (6), an inclusive range (2..10), a start plus count (2+10), or an open range (10..)",
        Arity = ArgumentArity.ExactlyOne
    };
    public Option<bool> Head { get; } = new("--head") { Description = "Take the count from the start (the default direction)" };
    public Option<bool> Tail { get; } = new("--tail") { Description = "Take the count from the end instead of the start" };
    public Option<bool> Count { get; } = new("--count") { Description = "Reduce a selected table/vector to a single row count" };
    public Option<bool> Print { get; } = new("--print") { Description = "Print one document behind a selected section row; use --row N|first|last to choose a row when multiple rows are printable" };
    public Option<string?> Row { get; } = new("--row") { Description = "With --print or a shape projection, select a printable row: a 1-based index, first, or last" };
    public Option<bool> Value { get; } = new("--value") { Description = "Print one scalar value from a selected section; use --row N|first|last when multiple rows exist" };
    public Option<bool> Urls { get; } = new("--urls") { Description = "Project URL-bearing selected section rows to a URL list or JSONL rows" };
    public Option<bool> Paths { get; } = new("--paths") { Description = "Project path-bearing selected section rows to a path list or JSONL rows" };
    public Option<bool> JsonArray { get; } = new("--json-array") { Description = "With a shape projection, emit projected rows as one JSON array" };
    public Option<bool> Info { get; } = new("--info") { Description = "Show operational metrics (output, time, HTTP, cache) on stderr" };
    public Option<string?> Tips { get; }

    // Discovery option
    public Option<string?> Discover { get; }

    // Projection options
    public Option<string?> Select { get; }
    public Option<string?> Columns { get; }
    public Option<string?> Fields { get; }
    public Option<bool> Schema { get; } = new("--schema") { Description = "With -D: show the full static schema without resolving/loading source (offline)" };
    public Option<bool> Tree { get; } = new("--tree") { Description = "Show discovery as a tree (sections → items)" };

    // Performance Triage row predicates
    public Option<bool> PerformanceTriageLoop { get; } = new("--loop") { Description = "Performance Triage: show only opportunities inside loops" };
    public Option<string?> PerformanceTriageMinConfidence { get; } = new("--min-confidence") { Description = "Performance Triage: minimum confidence (low, medium, high)" };
    public Option<string[]> PerformanceTriageShape { get; } = new("--triage-shape")
    {
        Description = "Performance Triage: include only shape(s), comma-separated or repeated; run -S \"Performance Triage\" to see shapes",
        AllowMultipleArgumentsPerToken = false
    };
    public Option<int?> PerformanceTriageTop { get; } = new("--top") { Description = "Performance Triage: show the top N ranked rows" };
    public Option<string[]> RowWhere { get; } = new("--where")
    {
        Description = "Filter selected section rows with a field predicate, e.g. --where \"Allocation=boxed *\" or --where \"RootReach>=10\"",
        AllowMultipleArgumentsPerToken = false
    };
    public Option<string?> RowOrderBy { get; } = new("--order-by")
    {
        Description = "Order selected section rows by field(s), e.g. --order-by \"RootReach desc,Confidence desc\""
    };

    // NuGet source options
    public Option<string[]> Source { get; } = new("--source")
    {
        Description = "NuGet source URL (replaces defaults, can repeat)",
        AllowMultipleArgumentsPerToken = false
    };
    public Option<string[]> AddSource { get; } = new("--add-source")
    {
        Description = "NuGet source URL to add (can repeat)",
        AllowMultipleArgumentsPerToken = false
    };
    public Option<string?> NuGetConfig { get; } = new("--nugetconfig")
    {
        Description = "Path to nuget.config file"
    };

    public SharedOptions()
    {
        Verbosity.AcceptOnlyFromAmong(StringComparer.OrdinalIgnoreCase, OptionParsers.ValidVerbosityValues);
        PerformanceTriageMinConfidence.AcceptOnlyFromAmong(StringComparer.OrdinalIgnoreCase, "low", "medium", "high");

        Limit = new Option<int?>("-n") { Description = "Count of output lines to keep (like head -n); pair with --tail to take them from the end" };

        Tips = new Option<string?>("--tips")
        {
            Description = "Tip verbosity: q(uiet), m(inimal), d(etailed)",
            Arity = ArgumentArity.ZeroOrOne
        };
        Tips.Aliases.Add("-T");

        Discover = new Option<string?>("-D")
        {
            Description = "Discover schema: sections, or items within a section",
            Arity = ArgumentArity.ZeroOrOne
        };
        Discover.Aliases.Add("--discover");

        Select = new Option<string?>("-S")
        {
            Description = "Select sections/categories by name, wildcard, or @All (comma/semicolon-separated)",
            Arity = ArgumentArity.ZeroOrOne
        };
        Select.Aliases.Add("--select");
        Select.Aliases.Add("-s");
        Select.Aliases.Add("--section");

        Columns = new Option<string?>("--columns")
        {
            Description = "Filter columns by name (comma/semicolon-separated)",
            Arity = ArgumentArity.ZeroOrOne
        };

        Fields = new Option<string?>("--fields")
        {
            Description = "Filter fields by name (comma/semicolon-separated)",
            Arity = ArgumentArity.ZeroOrOne
        };

        NoHeaders.Aliases.Add("--no-header");
        NoHeaders.Aliases.Add("--nh");

        Row.Validators.Add(result =>
        {
            var token = result.Tokens.Count > 0 ? result.Tokens[^1].Value : null;
            if (token is not null && !RowSelector.TryParse(token, out _))
                result.AddError($"--row must be a 1-based row number, 'first', or 'last' (got '{token}').");
        });

        // A config the user names explicitly must be usable. Reporting it here gives every
        // command that takes --nugetconfig the same clean parse-time error, instead of an
        // unhandled exception from whichever service happens to resolve sources first.
        NuGetConfig.Validators.Add(result =>
        {
            var token = result.Tokens.Count > 0 ? result.Tokens[^1].Value : null;
            if (token is not null && NuGetSourceResolver.DescribeConfigProblem(token) is string problem)
                result.AddError(problem);
        });

        // Credentials in the URL authenticate against no feed, so left alone they surface as a
        // bare 401 that looks like the credential was wrong rather than never sent.
        Source.Validators.Add(ValidateSourceUrls);
        AddSource.Validators.Add(ValidateSourceUrls);
    }

    private static void ValidateSourceUrls(System.CommandLine.Parsing.OptionResult result)
    {
        foreach (var token in result.Tokens)
        {
            if (NuGetSourceResolver.DescribeSourceProblem(token.Value) is string problem)
                result.AddError(problem);
        }
    }

    /// <summary>
    /// Adds core output options to a command (verbose, verbosity, tips, limit).
    /// </summary>
    public void AddOutputOptionsTo(Command command, bool supportsRowWindows = true)
    {
        command.Options.Add(Verbose);
        command.Options.Add(Verbosity);
        command.Options.Add(Tips);
        command.Options.Add(Info);
        command.Options.Add(Limit);
        command.Options.Add(Rows);
        command.Options.Add(Head);
        command.Options.Add(Tail);

        // --head and --tail name a direction, so asking for both is not a narrower
        // window but a contradiction. This applies with or without --rows.
        command.Validators.Add(result =>
        {
            if (result.GetValue(Head) && result.GetValue(Tail))
                result.AddError("--head and --tail select opposite ends; choose one.");
        });

        if (!supportsRowWindows)
        {
            command.Validators.Add(result =>
            {
                if (result.GetResult(Rows) is not null)
                    result.AddError($"--rows is not supported by the '{command.Name}' command.");
            });
            return;
        }

        // Validate the --rows spec at parse time so an invalid selection surfaces as a
        // clean System.CommandLine error (one line on stderr, exit 1) rather than a
        // RowWindowValidationException thrown inside the invocation pipeline, which SCL
        // prints as an unhandled-exception stack trace. Reading the parse results here
        // (not the arg-preprocessor token scan) covers =-syntax and concatenated forms
        // the scanner misses.
        //
        // This reads the raw token rather than calling GetValue, because GetValue on a
        // required-argument option with no value throws out of the validator itself --
        // which surfaced as a stack trace *and* exit code 0, hiding the failure from any
        // caller checking the exit code. Leaving a valueless --rows alone lets
        // System.CommandLine report the missing argument the way it reports every other.
        command.Validators.Add(result =>
        {
            if (result.GetResult(Rows) is not { } rowsResult || rowsResult.Tokens.Count == 0)
                return;

            var token = rowsResult.Tokens[^1].Value;

            // System.CommandLine will hand a required-argument option the next token
            // even when it is plainly another option, so `--rows --tsv` arrives here as
            // a row selection of "--tsv". Blaming the spelling of --tsv would send a
            // reader to fix the wrong thing; the actual mistake is the missing value.
            if (token.StartsWith('-'))
            {
                result.AddError($"--rows requires a row selection, but '{token}' is another option. Give --rows a count (6), a range (2..10), a start plus count (2+10), or an open range (10..).");
                return;
            }

            if (!RowSpec.TryParse(token, out var spec, out var error))
            {
                result.AddError($"--rows {error}");
                return;
            }

            // A range names the rows to keep, so it already answers the question a
            // direction would answer. Taking "the last of rows 2..10" is not a
            // narrower request, it is two different answers to the same question.
            if (spec.IsRange && (result.GetValue(Head) || result.GetValue(Tail)))
                result.AddError($"--rows {token} already names which rows to keep, so it cannot combine with --head or --tail; use a count such as --rows {spec.RowCount ?? 10} --tail to take rows from one end.");

            // -n counts output lines. With --rows the count comes from the spec, so a
            // second count is ambiguous rather than redundant.
            if (result.GetValue(Limit) is not null)
                result.AddError($"--rows {token} already carries the count, so it cannot combine with -n; drop one.");
        });
    }

    /// <summary>
    /// Adds JSON output option to a command.
    /// </summary>
    public void AddJsonOptionTo(Command command)
    {
        command.Options.Add(Json);
    }

    public void AddTableOptionsTo(Command command)
    {
        command.Options.Add(Table);
        command.Options.Add(Tsv);
        command.Options.Add(Jsonl);
        command.Options.Add(NoHeaders);
    }

    /// <summary>
    /// Adds section filtering and projection options to a command.
    /// </summary>
    public void AddSectionOptionsTo(Command command)
    {
        command.Options.Add(Discover);
        command.Options.Add(Select);
        command.Options.Add(Columns);
        command.Options.Add(Fields);
        command.Options.Add(Schema);
        command.Options.Add(Tree);
    }

    /// <summary>
    /// Adds the count output option to commands that support sectioned tables.
    /// </summary>
    public void AddCountOptionTo(Command command)
    {
        command.Options.Add(Count);
    }

    /// <summary>
    /// Adds Performance Triage row predicates to commands that can render that section.
    /// </summary>
    public void AddPerformanceTriageOptionsTo(Command command)
    {
        command.Options.Add(PerformanceTriageLoop);
        command.Options.Add(PerformanceTriageMinConfidence);
        command.Options.Add(PerformanceTriageShape);
        command.Options.Add(PerformanceTriageTop);
        command.Options.Add(RowWhere);
        command.Options.Add(RowOrderBy);
    }

    /// <summary>
    /// Adds the print output option to commands that can render a printable document section.
    /// </summary>
    public void AddPrintOptionTo(Command command)
    {
        command.Options.Add(Print);
        command.Options.Add(Row);
    }

    public void AddShapeProjectionOptionsTo(Command command)
    {
        command.Options.Add(Value);
        command.Options.Add(Urls);
        command.Options.Add(Paths);
        command.Options.Add(JsonArray);
        if (!command.Options.Contains(Row))
            command.Options.Add(Row);
    }

    public RowWindow? ParseRows(ParseResult parseResult)
        => BuildRowWindow(parseResult.GetValue(Rows), parseResult.GetValue(Tail));

    /// <summary>
    /// Resolves the <c>--rows</c> data-row window from the parsed spec and direction.
    /// The primary user-facing validation is the parse-time command validator in
    /// <see cref="AddOutputOptionsTo"/>, which fails cleanly during parsing before
    /// this runs. The throws here are a defensive invariant guard for direct callers
    /// (and are unit-tested); in the CLI path the invalid combinations are already
    /// rejected, so they are not expected to fire.
    /// </summary>
    public static RowWindow? BuildRowWindow(string? rows, bool fromEnd)
    {
        if (rows is null)
            return null;
        if (!RowSpec.TryParse(rows, out var spec, out var error))
            throw new RowWindowValidationException($"--rows {error}");
        return BuildRowWindow(spec, fromEnd);
    }

    /// <inheritdoc cref="BuildRowWindow(string?, bool)"/>
    public static RowWindow BuildRowWindow(RowSpec spec, bool fromEnd)
    {
        if (spec.IsRange)
        {
            if (fromEnd)
                throw new RowWindowValidationException($"--rows {spec} already names which rows to keep, so it cannot combine with --head or --tail.");
            return RowWindow.Range(spec.Start, spec.IsOpenEnded ? null : spec.End);
        }

        return fromEnd ? RowWindow.Tail(spec.Count) : RowWindow.Head(spec.Count);
    }

    public RowSelector? ParsePrintRow(ParseResult parseResult)
        => RowSelector.TryParse(parseResult.GetValue(Row), out var selector) ? selector : null;

    public PerformanceTriageOptions ParsePerformanceTriageOptions(ParseResult parseResult)
    {
        var shapes = (parseResult.GetValue(PerformanceTriageShape) ?? [])
            .SelectMany(value => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var top = parseResult.GetValue(Count) ? null : parseResult.GetValue(PerformanceTriageTop);
        return new PerformanceTriageOptions
        {
            LoopOnly = parseResult.GetValue(PerformanceTriageLoop),
            MinConfidence = parseResult.GetValue(PerformanceTriageMinConfidence),
            Shapes = shapes,
            Top = top is > 0 ? top : null,
            Where = parseResult.GetValue(RowWhere) ?? [],
            OrderBy = parseResult.GetValue(RowOrderBy)
        };
    }

    /// <summary>
    /// Adds NuGet source options to a command.
    /// </summary>
    public void AddNuGetOptionsTo(Command command)
    {
        command.Options.Add(Source);
        command.Options.Add(AddSource);
        command.Options.Add(NuGetConfig);
    }

    /// <summary>
    /// Adds all common options for a full inspection command (JSON, markdown, verbose, sections, NuGet).
    /// </summary>
    public void AddAllOptionsTo(Command command)
    {
        command.Options.Add(Json);
        command.Options.Add(Markdown);
        command.Options.Add(PlainText);
        command.Options.Add(Mermaid);
        AddTableOptionsTo(command);
        AddOutputOptionsTo(command);
        AddSectionOptionsTo(command);
        AddNuGetOptionsTo(command);
    }

    // Parsing helpers that use this instance's options

    /// <summary>
    /// Parses NuGet source options from parse result.
    /// </summary>
    public NuGetSourceOptions ParseNuGetSourceOptions(ParseResult parseResult)
    {
        var sources = parseResult.GetValue(Source) ?? [];
        var addSources = parseResult.GetValue(AddSource) ?? [];
        var configFile = parseResult.GetValue(NuGetConfig);

        if (sources.Length == 0 && addSources.Length == 0 && configFile == null)
            return NuGetSourceOptions.Default;

        return new NuGetSourceOptions
        {
            Sources = sources,
            AdditionalSources = addSources,
            ConfigFile = configFile
        };
    }

    /// <summary>
    /// Parses verbosity from parse result.
    /// </summary>
    public Verbosity ParseVerbosity(ParseResult parseResult)
        => OptionParsers.ParseVerbosity(parseResult.GetValue(Verbosity));

    /// <summary>
    /// Parses tip level from parse result.
    /// </summary>
    public TipLevel ParseTipLevel(ParseResult parseResult)
        => OptionParsers.ParseTipLevel(parseResult.GetValue(Tips), parseResult.GetResult(Tips) != null);

    /// <summary>
    /// Resolves the output format from parse result.
    /// Precedence: explicit CLI flags (--json, --markdown, -v:*) → DOTNET_INSPECT_FORMAT env → <paramref name="defaultFormat"/>.
    /// </summary>
    public OutputFormat ResolveFormat(ParseResult parseResult, OutputFormat defaultFormat = OutputFormat.Markdown)
    {
        bool jsonFlag = parseResult.GetValue(Json);
        bool markdownFlag = parseResult.GetValue(Markdown);
        bool plainTextFlag = parseResult.GetValue(PlainText);
        bool mermaidFlag = parseResult.GetValue(Mermaid);
        bool tableFlag = IsExplicitTrue(parseResult, Table);
        bool tsvFlag = IsExplicitTrue(parseResult, Tsv);
        bool jsonlFlag = IsExplicitTrue(parseResult, Jsonl);
        bool hasVerbosity = parseResult.GetResult(Verbosity) is { Implicit: false };
        Verbosity? verbosity = hasVerbosity ? ParseVerbosity(parseResult) : null;
        ValidateRendererFlags(jsonFlag, markdownFlag, plainTextFlag, mermaidFlag, tableFlag || tsvFlag || jsonlFlag, hasVerbosity);
        if (ShouldSuppressEnvironmentTabularFormat(
            parseResult,
            tableFlag || tsvFlag || jsonlFlag,
            jsonFlag || markdownFlag || plainTextFlag || mermaidFlag || hasVerbosity))
        {
            return defaultFormat;
        }

        return OutputFormatResolver.Resolve(jsonFlag, markdownFlag, verbosity, plainTextFlag, mermaidFlag, tableFlag, tsvFlag, jsonlFlag, defaultFormat);
    }

    /// <summary>
    /// Returns true when --mermaid is combined with --markdown (embedded mermaid in markdown).
    /// </summary>
    public bool IsEmbeddedMermaid(ParseResult parseResult)
        => OutputFormatResolver.IsEmbeddedMermaid(parseResult.GetValue(Markdown), parseResult.GetValue(Mermaid));

    /// <summary>
    /// Resolves whether tabular output should be used, considering --table, --tsv, and --jsonl.
    /// Throws if a tabular flag is combined with -v (contradictory: -v implies markdown).
    /// </summary>
    public bool ResolveTabular(ParseResult parseResult, OutputFormat defaultFormat = OutputFormat.Markdown)
    {
        var format = ResolveFormat(parseResult, defaultFormat);
        return format is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl;
    }

    public bool ResolveTsv(ParseResult parseResult, OutputFormat defaultFormat = OutputFormat.Markdown) =>
        ResolveFormat(parseResult, defaultFormat) == OutputFormat.Tsv;

    public bool ResolveJsonl(ParseResult parseResult, OutputFormat defaultFormat = OutputFormat.Markdown) =>
        ResolveFormat(parseResult, defaultFormat) == OutputFormat.Jsonl;

    /// <summary>
    /// Returns true when the user explicitly chose an output format via CLI flags
    /// (--json, --markdown, --plain-text, --table, --tsv, --jsonl, or -v)
    /// or DOTNET_INSPECT_FORMAT.
    /// When false, commands are free to apply their own default format.
    /// </summary>
    public bool IsFormatExplicitlySet(ParseResult parseResult)
    {
        if (IsTableExplicitlySet(parseResult)) return true;
        if (parseResult.GetResult(Json) is { Implicit: false }) return true;
        if (parseResult.GetResult(Markdown) is { Implicit: false }) return true;
        if (parseResult.GetResult(PlainText) is { Implicit: false }) return true;
        if (parseResult.GetResult(Mermaid) is { Implicit: false }) return true;
        if (parseResult.GetResult(Bare) is { Implicit: false }) return true;
        if (parseResult.GetResult(Verbosity) is { Implicit: false }) return true;
        return OutputFormatResolver.GetEnvironmentOverride() != null;
    }

    public bool IsTableExplicitlySet(ParseResult parseResult) =>
        IsTableFlagExplicitlySet(parseResult)
        || (!IsNonTabularFormatExplicitlySet(parseResult)
            && OutputFormatResolver.GetEnvironmentOverride() is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl);

    public bool IsTableFlagExplicitlySet(ParseResult parseResult) =>
        IsExplicit(parseResult, Table) || IsExplicit(parseResult, Tsv) || IsExplicit(parseResult, Jsonl);

    /// <summary>
    /// Parses select list from parse result.
    /// Returns null if not specified, @Default for bare -S, or populated array with section/category names.
    /// </summary>
    public string[]? ParseSelect(ParseResult parseResult)
        => IsBareFlag(parseResult, Select)
            ? [SelectResolver.InfoSelector]
            : ParseCommaSeparatedList(parseResult.GetValue(Select));

    /// <summary>
    /// Parses discover flag from parse result.
    /// Returns null if not specified, empty array for bare -D, or populated array with section name.
    /// </summary>
    public string[]? ParseDiscover(ParseResult parseResult)
        => ParseProjectionList(parseResult, Discover);

    /// <summary>
    /// Parses columns list from parse result.
    /// Returns null if not specified, or populated array with column names.
    /// </summary>
    public string[]? ParseColumns(ParseResult parseResult)
        => ParseCommaSeparatedList(parseResult.GetValue(Columns));

    /// <summary>
    /// Parses fields list from parse result.
    /// Returns null if not specified, or populated array with field names.
    /// </summary>
    public string[]? ParseFields(ParseResult parseResult)
        => ParseCommaSeparatedList(parseResult.GetValue(Fields));

    /// <summary>
    /// Returns true if -D/--discover flag is present.
    /// </summary>
    public bool IsDiscoveryMode(ParseResult parseResult)
        => parseResult.GetResult(Discover) is { Implicit: false };

    public bool ParseTree(ParseResult parseResult) => parseResult.GetValue(Tree);

    /// <summary>
    /// Resolves static discovery. <c>--schema</c> opts out of effective discovery.
    /// </summary>
    public bool ParseSchema(ParseResult parseResult)
        => parseResult.GetValue(Schema);

    private static string[]? ParseProjectionList(ParseResult parseResult, Option<string?> option)
    {
        var values = ParseCommaSeparatedList(parseResult.GetValue(option));
        // Bare flag with no value: return empty array (signals "discover")
        if (values == null && parseResult.GetResult(option) != null)
            return [];
        return values;
    }

    private static bool IsBareFlag(ParseResult parseResult, Option<string?> option)
    {
        return parseResult.GetResult(option) is { Implicit: false } &&
               string.IsNullOrWhiteSpace(parseResult.GetValue(option));
    }

    private static void ValidateRendererFlags(
        bool jsonFlag,
        bool markdownFlag,
        bool plainTextFlag,
        bool mermaidFlag,
        bool tabularFlag,
        bool hasVerbosity)
    {
        if (!tabularFlag)
            return;

        if (jsonFlag)
        {
            Console.Error.WriteLine("--json cannot be combined with --table, --tsv, or --jsonl.");
            throw new OperationCanceledException();
        }

        if (markdownFlag || plainTextFlag || mermaidFlag || hasVerbosity)
        {
            Console.Error.WriteLine("--table/--tsv/--jsonl cannot be combined with --markdown, --plaintext, --mermaid, or -v.");
            throw new OperationCanceledException();
        }
    }

    private static bool IsExplicit(ParseResult parseResult, Option<bool> option) =>
        parseResult.GetResult(option) is { Implicit: false };

    private static bool IsExplicitTrue(ParseResult parseResult, Option<bool> option) =>
        IsExplicit(parseResult, option) && parseResult.GetValue(option);

    private bool IsNonTabularFormatExplicitlySet(ParseResult parseResult) =>
        IsExplicit(parseResult, Json)
        || IsExplicit(parseResult, Markdown)
        || IsExplicit(parseResult, PlainText)
        || IsExplicit(parseResult, Mermaid)
        || IsExplicit(parseResult, Bare)
        || parseResult.GetResult(Verbosity) is { Implicit: false };

    private bool ShouldSuppressEnvironmentTabularFormat(
        ParseResult parseResult,
        bool tabularFlag,
        bool explicitNonTabularFormat) =>
        !tabularFlag
        && !explicitNonTabularFormat
        && IsExplicit(parseResult, Bare)
        && OutputFormatResolver.GetEnvironmentOverride() is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl;

    private static readonly char[] ListSeparators = [',', ';'];

    private static string[]? ParseCommaSeparatedList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value
            .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(UnescapeAtCategory)
            .ToArray();
    }

    private static string UnescapeAtCategory(string value)
        => ArgumentPreprocessor.UnescapeAtCategoryValue(value);
}
