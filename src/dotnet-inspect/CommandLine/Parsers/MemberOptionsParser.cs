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
        Option<bool> CompactOption,
        Option<bool> OneLineOption,
        Option<bool> NoHeaderOption,
        Option<bool> UnsafeOption,
        Option<int?> IndexOption,
        Option<string> ParamsOption,
        Option<string> OfOption,
        Option<bool> SelectOption,
        Option<string[]> KindOption);

    /// <summary>
    /// Result of parsing member command options.
    /// </summary>
    public abstract record MemberParseResult;

    /// <summary>
    /// Indicates sections should be listed.
    /// </summary>
    public record ListSections : MemberParseResult;

    /// <summary>
    /// Indicates schema discovery (-D/--discover).
    /// </summary>
    public record Discovery(string[]? Discover, bool Tree) : MemberParseResult;

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
    public record Success(MemberOptions Options) : MemberParseResult;

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

        // Handle projection discovery or help
        if (argsValue.Length == 0 && !hasExplicitSource)
        {
            if (opts.IsDiscoveryMode(parseResult))
                return new Discovery(opts.ParseDiscover(parseResult), opts.ParseTree(parseResult));
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

        // If source resolution left us with an unresolved package that is actually
        // a qualified type name (e.g., "System.Text.Json.JsonDocument"), split it
        // so the user can write: member System.Text.Json.JsonDocument Parse
        if (explicitPackage == null && source.PackagePath != null && source.PlatformAssembly == null && source.AssemblyPath == null)
        {
            var probe = SourceResolver.TryProbeLocalQualifiedName(source.PackagePath);
            if (probe != null)
            {
                if (source.TypeName != null)
                    positionalMembers.Insert(0, source.TypeName);
                if (probe.Kind == SourceResolver.LocalSourceKind.Platform)
                    source = source with { PackagePath = null, PlatformAssembly = probe.SourceName, TypeName = probe.Remainder };
                else
                    source = source with { PackagePath = probe.SourceName, TypeName = probe.Remainder };
            }
        }

        var typeName = source.TypeName;

        // If the type name contains a dot and no member filters were provided,
        // split at the last dot: the right part is a member filter.
        // Handles: member System.Text.Json.JsonDocument.Parse
        //   → source=System.Text.Json, type=JsonDocument, member=Parse
        // Skip if the right part contains '<' — that's a generic type name (e.g., Generic.List<T>),
        // not a type.member pair.
        if (typeName != null && typeName.Contains('.') && positionalMembers.Count == 0)
        {
            var lastDot = typeName.LastIndexOf('.');
            var rightPart = typeName[(lastDot + 1)..];
            if (!rightPart.Contains('<'))
            {
                positionalMembers.Add(rightPart);
                typeName = typeName[..lastDot];
            }
        }

        // Check for unrecognized options in positional args
        var badOption = positionalMembers.FirstOrDefault(m => m.StartsWith('-'));
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

        var kindValues = parseResult.GetValue(args.KindOption) ?? [];
        var kindFilter = SharedParsers.ParseKindFilter(kindValues);

        var options = new MemberOptions
        {
            TypeName = typeName,
            PackagePath = source.PackagePath,
            AssemblyPath = source.AssemblyPath,
            PlatformAssembly = source.PlatformAssembly,
            PlatformFramework = source.FrameworkOverride ?? parseResult.GetValue(args.FrameworkOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            MemberFilter = memberFilter,
            KindFilter = kindFilter,
            Limit = memberLimit,
            ShowDocs = true,  // Docs always on (local XML); use source command for SourceLink
            DocsExplicitlySet = false,
            JsonOutput = parseResult.GetValue(opts.Json),
            CompactJson = parseResult.GetValue(args.CompactOption),
            OneLine = opts.ResolveOneLine(parseResult, args.OneLineOption),
            OneLineExplicitlySet = parseResult.GetResult(args.OneLineOption) is { Implicit: false },
            PlainText = parseResult.GetValue(opts.PlainText),
            NoHeader = parseResult.GetValue(args.NoHeaderOption),
            UnsafeOnly = parseResult.GetValue(args.UnsafeOption),
            CtorOnly = ctorOnly,
            OverloadIndex = parseResult.GetValue(args.IndexOption) ?? shorthandIndex,
            ParamTypes = SharedParsers.ParseParamTypes(parseResult.GetValue(args.ParamsOption)),
            FirstParamType = parseResult.GetValue(args.OfOption),
            ShowSelect = parseResult.GetValue(args.SelectOption),
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
        };

        options = options with
        {
            TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || memberLimit != null
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

}
