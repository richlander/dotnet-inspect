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
        Option<string?> TypeFilterOption,
        Option<bool> VerifyOption,
        Option<bool> BrowsableUrlsOption,
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
        var argsValue = parseResult.GetValue(args.ArgsArg) ?? [];
        var explicitPackage = parseResult.GetValue(args.PackageOption);
        var explicitAssembly = parseResult.GetValue(args.AssemblyOption);
        var explicitPlatform = parseResult.GetValue(args.PlatformOption);
        bool isLibrarySelector = SourceResolver.IsLibrarySelector(explicitAssembly, explicitPackage);
        bool hasExplicitSource = SourceResolver.HasExplicitSource(explicitPackage, explicitAssembly, explicitPlatform, isLibrarySelector);

        // Handle projection discovery or help
        if (argsValue.Length == 0 && !hasExplicitSource)
        {
            if (opts.IsDiscoveryMode(parseResult))
                return new Discovery(opts.ParseDiscover(parseResult), opts.ParseTree(parseResult));
            return new ShowHelp();
        }

        // Check for unrecognized options in positional args
        var badOption = argsValue.FirstOrDefault(a => a.StartsWith('-'));
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        // Resolve source
        var source = await SourceResolver.ResolveAsync(
            argsValue, explicitPackage, explicitAssembly, explicitPlatform,
            parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: true);

        if (source.VersionError)
            return new VersionError(source.VersionErrorMessage!);

        // Capture member name from extra positional args (after package + type)
        // Pattern: source <package> <type> <member>  or  source <type> --package <pkg> <member>
        string? memberName = null;
        int? overloadIndex = null;
        {
            var positionalMembers = new List<string>();
            if (hasExplicitSource && argsValue.Length >= 2)
                positionalMembers.AddRange(argsValue[1..]);
            else if (!hasExplicitSource && argsValue.Length >= 3)
                positionalMembers.AddRange(argsValue[2..]);

            // Handle Type.Member dotted syntax
            var typeName = source.TypeName;
            if (typeName != null && typeName.Contains('.') && positionalMembers.Count == 0)
            {
                var lastDot = typeName.LastIndexOf('.');
                var rightPart = typeName[(lastDot + 1)..];
                if (!rightPart.Contains('<'))
                {
                    positionalMembers.Add(rightPart);
                    source = source with { TypeName = typeName[..lastDot] };
                }
            }

            if (positionalMembers.Count > 0)
            {
                memberName = positionalMembers[0];
                // Parse overload index shorthand: GetValue:2
                if (memberName.Contains(':'))
                {
                    var colonIdx = memberName.LastIndexOf(':');
                    if (int.TryParse(memberName[(colonIdx + 1)..], out var idx))
                    {
                        overloadIndex = idx;
                        memberName = memberName[..colonIdx];
                    }
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
            JsonOutput = parseResult.GetValue(opts.Json),
            CompactJson = parseResult.GetValue(args.CompactOption),
            OneLine = opts.ResolveOneLine(parseResult, args.OneLineOption),
            OneLineExplicitlySet = parseResult.GetResult(args.OneLineOption) is { Implicit: false },
            PlainText = parseResult.GetValue(opts.PlainText),
            NoHeader = parseResult.GetValue(args.NoHeaderOption),
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            NuGetOptions = opts.ParseNuGetSourceOptions(parseResult)
        };

        options = options with
        {
            TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || typeLimit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        };

        return new Success(options);
    }
}
