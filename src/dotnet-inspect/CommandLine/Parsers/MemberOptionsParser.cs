using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the member command options.
/// Extracts options and builds ApiOptions for member inspection.
/// </summary>
public static class MemberOptionsParser
{
    /// <summary>
    /// Arguments container for member command options.
    /// </summary>
    public record MemberCommandArgs(
        Argument<string[]> ArgsArg,
        Option<string?> PackageOption,
        Option<string?> AssemblyOption,
        Option<string?> PlatformOption,
        Option<string?> FrameworkOption,
        Option<string?> TfmOption,
        Option<bool> AllOption,
        Option<string[]> MemberOption,
        Option<bool> CtorOption,
        Option<bool> DocsOption,
        Option<bool> NoDocsOption,
        Option<bool> UseLocalDocsOption,
        Option<bool> SamplesOption,
        Option<bool> BrowsableUrlsOption,
        Option<bool> CompactOption,
        Option<bool> OneLineOption,
        Option<bool> NoHeaderOption,
        Option<bool> UnsafeOption,
        Option<int?> IndexOption,
        Option<string> ParamsOption,
        Option<string> OfOption,
        Option<bool> SelectOption);

    /// <summary>
    /// Result of parsing member command options.
    /// </summary>
    public abstract record MemberParseResult;

    /// <summary>
    /// Indicates sections should be listed.
    /// </summary>
    public record ListSections : MemberParseResult;

    /// <summary>
    /// Indicates selectable names should be listed.
    /// </summary>
    public record ListSelect : MemberParseResult;

    /// <summary>
    /// Indicates help should be shown.
    /// </summary>
    public record ShowHelp : MemberParseResult;

    /// <summary>
    /// Indicates a version error occurred.
    /// </summary>
    public record VersionError(string Message) : MemberParseResult;

    /// <summary>
    /// Indicates an unrecognized option was found.
    /// </summary>
    public record UnrecognizedOption(string Option) : MemberParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(ApiOptions Options) : MemberParseResult;

    /// <summary>
    /// Parses member command options asynchronously (due to source resolution).
    /// </summary>
    public static async Task<MemberParseResult> ParseAsync(
        ParseResult parseResult,
        SharedOptions opts,
        MemberCommandArgs args)
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

        // Extract positional members
        List<string> positionalMembers = [];
        if (hasExplicitSource && argsValue.Length >= 2)
            positionalMembers.AddRange(argsValue[1..]);
        else if (!hasExplicitSource && argsValue.Length >= 3)
            positionalMembers.AddRange(argsValue[2..]);

        // Resolve source
        var source = await SourceResolver.ResolveAsync(
            argsValue, explicitPackage, explicitAssembly, explicitPlatform,
            parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: false);

        if (source.VersionError)
            return new VersionError(source.VersionErrorMessage!);

        var typeName = source.TypeName;

        // Check for unrecognized options in positional args
        var badOption = positionalMembers.FirstOrDefault(m => m.StartsWith("--"));
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        // Combine -m option with positional members
        var members = parseResult.GetValue(args.MemberOption) ?? [];
        var allMembers = members.Concat(positionalMembers).ToArray();
        var ctorOnly = parseResult.GetValue(args.CtorOption);

        // Process dotted syntax and overload shorthand
        var (dottedTypeFilter, shorthandIndex) = SharedParsers.ProcessMemberArguments(allMembers);

        // Use extracted type name if no explicit type was provided
        if (dottedTypeFilter != null && string.IsNullOrEmpty(typeName))
            typeName = dottedTypeFilter;

        // Build member filter
        var (memberFilter, memberLimit) = BuildMemberFilter(allMembers, ctorOnly, out var clearShorthand);
        if (clearShorthand)
            shorthandIndex = null;

        // Determine docs behavior
        var (showDocs, docsExplicitlySet) = ParseDocsBehavior(parseResult, args);
        var (select, preferFields) = opts.ResolveSelectAndField(parseResult);

        var options = new ApiOptions
        {
            TypeName = typeName,
            PackagePath = source.PackagePath,
            AssemblyPath = source.AssemblyPath,
            PlatformAssembly = source.PlatformAssembly,
            PlatformFramework = source.FrameworkOverride ?? parseResult.GetValue(args.FrameworkOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            MemberFilter = memberFilter,
            Limit = memberLimit,
            ShowDocs = showDocs || parseResult.GetValue(args.UseLocalDocsOption),
            DocsExplicitlySet = docsExplicitlySet,
            UseLocalDocs = parseResult.GetValue(args.UseLocalDocsOption),
            ShowSamples = parseResult.GetValue(args.SamplesOption),
            BrowsableUrls = parseResult.GetValue(args.BrowsableUrlsOption),
            JsonOutput = parseResult.GetValue(opts.Json),
            CompactJson = parseResult.GetValue(args.CompactOption),
            OneLine = parseResult.GetValue(args.OneLineOption),
            Markdown = opts.ParseMarkdown(parseResult),
            NoHeader = parseResult.GetValue(args.NoHeaderOption),
            UnsafeOnly = parseResult.GetValue(args.UnsafeOption),
            CtorOnly = ctorOnly,
            OverloadIndex = parseResult.GetValue(args.IndexOption) ?? shorthandIndex,
            ParamTypes = SharedParsers.ParseParamTypes(parseResult.GetValue(args.ParamsOption)),
            FirstParamType = parseResult.GetValue(args.OfOption),
            ShowSelect = parseResult.GetValue(args.SelectOption),
            IncludeSections = null,
            ExcludeSections = null,
            Select = select,
            PreferFields = preferFields,
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            IsMemberCommand = true
        };

        options = options with
        {
            TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || memberLimit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        };

        return new Success(options);
    }

    private static (HashSet<string> Filter, int? Limit) BuildMemberFilter(string[] allMembers, bool ctorOnly, out bool clearShorthand)
    {
        clearShorthand = false;

        if (ctorOnly)
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" }, null);

        if (allMembers.Length == 1 && int.TryParse(allMembers[0], out var mNum))
        {
            clearShorthand = true;
            return ([], mNum);
        }

        if (allMembers.Length > 0)
            return (new HashSet<string>(allMembers, StringComparer.OrdinalIgnoreCase), null);

        return ([], null);
    }

    private static (bool ShowDocs, bool DocsExplicitlySet) ParseDocsBehavior(
        ParseResult parseResult,
        MemberCommandArgs args)
    {
        bool showDocs = !parseResult.GetValue(args.NoDocsOption);
        bool docsExplicitlySet = parseResult.GetResult(args.DocsOption) is { Implicit: false }
            || parseResult.GetResult(args.NoDocsOption) is { Implicit: false }
            || parseResult.GetResult(args.UseLocalDocsOption) is { Implicit: false };

        // If --docs is explicitly set, honor it (overrides --no-docs precedence)
        if (parseResult.GetResult(args.DocsOption) is { Implicit: false })
            showDocs = true;

        return (showDocs, docsExplicitlySet);
    }
}
