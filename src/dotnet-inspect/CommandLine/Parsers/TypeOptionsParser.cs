using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the type command options.
/// Extracts options and builds ApiOptions for type discovery.
/// </summary>
public static class TypeOptionsParser
{
    /// <summary>
    /// Arguments container for type command options.
    /// </summary>
    public record TypeCommandArgs(
        Argument<string[]> ArgsArg,
        Option<string?> PackageOption,
        Option<string?> AssemblyOption,
        Option<string?> PlatformOption,
        Option<string?> FrameworkOption,
        Option<string?> TfmOption,
        Option<bool> AllOption,
        Option<string?> TypeFilterOption,
        Option<bool> CompactOption,
        Option<bool> OneLineOption,
        Option<bool> NoHeaderOption,
        Option<bool> ShapeOption,
        Option<bool> UnsafeOption,
        Option<string[]> MemberOption,
        Option<string[]> KindOption);

    /// <summary>
    /// Result of parsing type command options.
    /// </summary>
    public abstract record TypeParseResult;

    /// <summary>
    /// Indicates sections should be listed.
    /// </summary>
    public record ListSections : TypeParseResult;

    /// <summary>
    /// Indicates projection discovery (--select or --columns bare).
    /// </summary>
    public record Discovery(string[]? Select, string[]? Columns, string[]? Fields) : TypeParseResult;

    /// <summary>
    /// Indicates help should be shown.
    /// </summary>
    public record ShowHelp : TypeParseResult;

    /// <summary>
    /// Indicates a version error occurred.
    /// </summary>
    public record VersionError(string Message) : TypeParseResult;

    /// <summary>
    /// Indicates an unrecognized option was found.
    /// </summary>
    public record UnrecognizedOption(string Option) : TypeParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(TypeOptions Options) : TypeParseResult;

    /// <summary>
    /// Parses type command options asynchronously (due to source resolution).
    /// </summary>
    public static async Task<TypeParseResult> ParseAsync(
        ParseResult parseResult,
        SharedOptions opts,
        TypeCommandArgs args)
    {
        var argsValue = parseResult.GetValue(args.ArgsArg) ?? [];
        var explicitPackage = parseResult.GetValue(args.PackageOption);
        var explicitAssembly = parseResult.GetValue(args.AssemblyOption);
        var explicitPlatform = parseResult.GetValue(args.PlatformOption);
        bool isLibrarySelector = SourceResolver.IsLibrarySelector(explicitAssembly, explicitPackage);
        bool hasExplicitSource = SourceResolver.HasExplicitSource(explicitPackage, explicitAssembly, explicitPlatform, isLibrarySelector);

        // Handle section listing, projection discovery, or help
        if (argsValue.Length == 0 && !hasExplicitSource)
        {
            if (parseResult.GetResult(opts.IncludeSections) != null && parseResult.GetValue(opts.IncludeSections) == null)
                return new ListSections();
            if (opts.IsDiscoveryMode(parseResult))
                return new Discovery(opts.ParseSelect(parseResult), opts.ParseColumns(parseResult), opts.ParseFields(parseResult));
            return new ShowHelp();
        }

        // Check for unrecognized options in positional args
        var badOption = argsValue.FirstOrDefault(a => a.StartsWith("--"));
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        // Resolve source
        var source = await SourceResolver.ResolveAsync(
            argsValue, explicitPackage, explicitAssembly, explicitPlatform,
            parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: true);

        if (source.VersionError)
            return new VersionError(source.VersionErrorMessage!);

        // Parse type filter (number = limit, string = glob)
        var (typeFilter, typeLimit) = SharedParsers.ParseTypeFilter(parseResult.GetValue(args.TypeFilterOption));

        // Parse member filter
        var memberValues = parseResult.GetValue(args.MemberOption) ?? [];
        var (memberFilter, memberLimit) = SharedParsers.ParseMemberFilter(memberValues);

        var kindValues = parseResult.GetValue(args.KindOption) ?? [];
        var kindFilter = SharedParsers.ParseKindFilter(kindValues);

        var options = new TypeOptions
        {
            TypeName = source.TypeName,
            PackagePath = source.PackagePath,
            AssemblyPath = source.AssemblyPath,
            PlatformAssembly = source.PlatformAssembly,
            PlatformFramework = source.FrameworkOverride ?? parseResult.GetValue(args.FrameworkOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            TypeFilter = typeFilter,
            MemberFilter = memberFilter,
            KindFilter = kindFilter,
            Limit = memberLimit ?? typeLimit,
            ShowDocs = false,  // Type command: docs off by default
            DocsExplicitlySet = false,
            JsonOutput = parseResult.GetValue(opts.Json),
            CompactJson = parseResult.GetValue(args.CompactOption),
            OneLine = opts.ResolveOneLine(parseResult, args.OneLineOption),
            OneLineExplicitlySet = parseResult.GetResult(args.OneLineOption) is { Implicit: false },
            NoHeader = parseResult.GetValue(args.NoHeaderOption),
            ShapeOutput = parseResult.GetValue(args.ShapeOption),
            ShapeExplicitlySet = parseResult.GetResult(args.ShapeOption) is { Implicit: false },
            UnsafeOnly = parseResult.GetValue(args.UnsafeOption),
            IncludeSections = opts.ParseIncludeSections(parseResult),
            ExcludeSections = opts.ParseExcludeSections(parseResult),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
        };

        options = options with
        {
            TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || typeLimit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        };

        return new Success(options);
    }
}
