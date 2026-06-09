using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the source command options.
/// Extracts options and builds SourceOptions for source file discovery.
/// </summary>
public static class SourceOptionsParser
{
    /// <summary>
    /// Arguments container for source command options.
    /// </summary>
    public record SourceCommandArgs(
        Argument<string[]> ArgsArg,
        Option<string?> PackageOption,
        Option<string?> AssemblyOption,
        Option<string?> PlatformOption,
        Option<string?> FrameworkOption,
        Option<string?> TfmOption,
        Option<bool> AllOption,
        Option<string?> MemberOption,
        Option<string?> TypeFilterOption,
        Option<bool> VerifyOption,
        Option<bool> BrowsableUrlsOption,
        Option<bool> CatOption,
        Option<string?> ILOffsetOption,
        Option<bool> CompactOption,
        Option<bool> OneLineOption,
        Option<bool> NoHeaderOption);

    /// <summary>
    /// Result of parsing source command options.
    /// </summary>
    public abstract record SourceParseResult;

    /// <summary>
    /// Indicates schema discovery (-D/--discover).
    /// </summary>
    public record Discovery(string[]? Discover, bool Tree) : SourceParseResult;

    /// <summary>
    /// Indicates help should be shown.
    /// </summary>
    public record ShowHelp : SourceParseResult;

    /// <summary>
    /// Indicates a version error occurred.
    /// </summary>
    public record VersionError(string Message) : SourceParseResult;

    /// <summary>
    /// Indicates an unrecognized option was found.
    /// </summary>
    public record UnrecognizedOption(string Option) : SourceParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(SourceOptions Options) : SourceParseResult;

    /// <summary>
    /// Parses source command options asynchronously (due to source resolution).
    /// </summary>
    public static async Task<SourceParseResult> ParseAsync(
        ParseResult parseResult,
        SharedOptions opts,
        SourceCommandArgs args)
    {
        var ilOffset = parseResult.GetValue(args.ILOffsetOption);
        var sourceInputs = SharedParsers.ReadSourceSelectionInputs(
            parseResult, args.ArgsArg, args.PackageOption, args.AssemblyOption, args.PlatformOption);

        // Handle projection discovery or help
        if (sourceInputs.Args.Length == 0 && !sourceInputs.HasExplicitSource && ilOffset == null)
        {
            if (opts.IsDiscoveryMode(parseResult))
                return new Discovery(opts.ParseDiscover(parseResult), opts.ParseTree(parseResult));
            return new ShowHelp();
        }

        // Check for unrecognized options in positional args
        var badOption = sourceInputs.Args.FirstOrDefault(a => a.StartsWith('-'));
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        // Resolve source
        var sourceSelection = await SharedParsers.ResolveSourceSelectionAsync(
            sourceInputs, parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: true);
        var source = sourceSelection.Source;

        if (source.VersionError)
            return new VersionError(source.VersionErrorMessage!);

        // Capture member name: -m/--member option, positional args, or Type.Member dot syntax
        string? memberName = parseResult.GetValue(args.MemberOption);
        int? overloadIndex = null;
        {
            // Fall back to positional or dot syntax if -m not specified
            if (memberName == null)
            {
                var positionalMembers = new List<string>();
                if (sourceSelection.HasExplicitSource && sourceSelection.Args.Length >= 2)
                    positionalMembers.AddRange(sourceSelection.Args[1..]);
                else if (!sourceSelection.HasExplicitSource && sourceSelection.Args.Length >= 3)
                    positionalMembers.AddRange(sourceSelection.Args[2..]);

                // Handle Type.Member dotted syntax
                var typeName = source.TypeName;
                if (typeName != null && typeName.Contains('.') && positionalMembers.Count == 0)
                {
                    var (splitTypeName, splitMemberName) = SharedParsers.SplitTrailingMember(typeName);
                    if (splitMemberName != null)
                    {
                        positionalMembers.Add(splitMemberName);
                        source = source with { TypeName = splitTypeName };
                    }
                }

                if (positionalMembers.Count > 0)
                    memberName = positionalMembers[0];
            }

            // Parse overload index shorthand: GetValue:2
            if (memberName != null && memberName.Contains(':'))
            {
                var colonIdx = memberName.LastIndexOf(':');
                if (int.TryParse(memberName[(colonIdx + 1)..], out var idx))
                {
                    overloadIndex = idx;
                    memberName = memberName[..colonIdx];
                }
            }
        }

        // Parse type filter
        var (typeFilter, typeLimit) = SharedParsers.ParseTypeFilter(parseResult.GetValue(args.TypeFilterOption));

        var options = new SourceOptions
        {
            TypeName = source.TypeName,
            MemberName = memberName,
            OverloadIndex = overloadIndex,
            ILOffset = ilOffset,
            PackagePath = source.PackagePath,
            AssemblyPath = source.AssemblyPath,
            PlatformAssembly = source.PlatformAssembly,
            PlatformFramework = source.FrameworkOverride ?? parseResult.GetValue(args.FrameworkOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            TypeFilter = typeFilter,
            Limit = typeLimit,
            Verify = parseResult.GetValue(args.VerifyOption),
            BrowsableUrls = parseResult.GetValue(args.BrowsableUrlsOption),
            Cat = parseResult.GetValue(args.CatOption),
            JsonOutput = parseResult.GetValue(opts.Json),
            CompactJson = parseResult.GetValue(args.CompactOption),
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            PlainText = parseResult.GetValue(opts.PlainText),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Rows = opts.ParseRows(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            NuGetOptions = opts.ParseNuGetSourceOptions(parseResult)
        };

        options = options with
        {
            TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || typeLimit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        };

        return new Success(options);
    }
}
