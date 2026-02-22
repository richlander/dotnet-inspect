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
        Option<bool> SourcelinkOnlyOption,
        Option<bool> CompactOption,
        Option<bool> OneLineOption,
        Option<bool> NoHeaderOption,
        Option<bool> ShapeOption,
        Option<bool> UnsafeOption,
        Option<string[]> MemberOption);

    /// <summary>
    /// Result of parsing type command options.
    /// </summary>
    public abstract record TypeParseResult;

    /// <summary>
    /// Indicates sections should be listed.
    /// </summary>
    public record ListSections : TypeParseResult;

    /// <summary>
    /// Indicates selectable names should be listed.
    /// </summary>
    public record ListSelect : TypeParseResult;

    /// <summary>
    /// Indicates help should be shown.
    /// </summary>
    public record ShowHelp : TypeParseResult;

    /// <summary>
    /// Indicates a version error occurred.
    /// </summary>
    public record VersionError(string Message) : TypeParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(ApiOptions Options) : TypeParseResult;

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
            if (parseResult.GetResult(opts.Select) != null && parseResult.GetValue(opts.Select) == null)
                return new ListSelect();
            if (parseResult.GetResult(opts.Field) != null && parseResult.GetValue(opts.Field) == null)
                return new ListSelect();
            return new ShowHelp();
        }

        // Resolve source
        var source = await SourceResolver.ResolveAsync(
            argsValue, explicitPackage, explicitAssembly, explicitPlatform,
            parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: true);

        if (source.VersionError)
            return new VersionError(source.VersionErrorMessage!);

        // Parse type filter (number = limit, string = glob)
        var (typeFilter, typeLimit) = SharedParsers.ParseTypeFilter(parseResult.GetValue(args.TypeFilterOption));
        var (select, preferFields) = opts.ResolveSelectAndField(parseResult);

        // Parse member filter
        var memberValues = parseResult.GetValue(args.MemberOption) ?? [];
        var (memberFilter, memberLimit) = SharedParsers.ParseMemberFilter(memberValues);

        var options = new ApiOptions
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
            Limit = memberLimit ?? typeLimit,
            ShowDocs = false,  // Type command: docs off by default
            DocsExplicitlySet = false,
            SourceLinkOnly = parseResult.GetValue(args.SourcelinkOnlyOption),
            JsonOutput = parseResult.GetValue(opts.Json),
            CompactJson = parseResult.GetValue(args.CompactOption),
            OneLine = parseResult.GetValue(args.OneLineOption),
            Markdown = parseResult.GetValue(opts.Markdown),
            NoHeader = parseResult.GetValue(args.NoHeaderOption),
            ShapeOutput = parseResult.GetValue(args.ShapeOption),
            UnsafeOnly = parseResult.GetValue(args.UnsafeOption),
            IncludeSections = null,
            ExcludeSections = null,
            Select = select,
            PreferFields = preferFields,
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
