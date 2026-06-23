using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Sections;
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
        Option<bool> SelectOption,
        Option<string[]> KindOption,
        Option<string[]> BinOption,
        Option<string[]> ProjectOption,
        Option<string[]> CallerPackageOption);

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
        var sourceInputs = SharedParsers.ReadSourceSelectionInputs(
            parseResult, args.ArgsArg, args.PackageOption, args.AssemblyOption, args.PlatformOption);

        // Handle projection discovery or help
        if (sourceInputs.Args.Length == 0 && !sourceInputs.HasExplicitSource)
        {
            if (opts.IsDiscoveryMode(parseResult))
                return new Discovery(opts.ParseDiscover(parseResult), opts.ParseTree(parseResult));
            return new ShowHelp();
        }

        // Extract positional members
        List<string> positionalMembers = [];
        if (sourceInputs.HasExplicitSource && sourceInputs.Args.Length >= 2)
            positionalMembers.AddRange(sourceInputs.Args[1..]);
        else if (!sourceInputs.HasExplicitSource && sourceInputs.Args.Length >= 3)
            positionalMembers.AddRange(sourceInputs.Args[2..]);

        // Resolve source
        var sourceSelection = await SharedParsers.ResolveSourceSelectionAsync(
            sourceInputs, parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: false);
        var source = sourceSelection.Source;

        if (source.VersionError)
            return new VersionError(source.VersionErrorMessage!);

        var optionMembers = parseResult.GetValue(args.MemberOption) ?? [];

        // If the only positional value is a fully-qualified Type.Member, split the
        // member suffix first, then probe the type portion as the source/type name.
        // Handles: member System.Text.Json.JsonSerializer.SerializeToNode
        //   → source=System.Text.Json, type=JsonSerializer, member=SerializeToNode
        bool resolvedQualifiedTypeMember = false;
        if (sourceSelection.ExplicitPackage == null
            && source.TypeName == null
            && source.PackagePath != null
            && source.PlatformAssembly == null
            && source.AssemblyPath == null
            && positionalMembers.Count == 0
            && optionMembers.Length == 0)
        {
            var split = SharedParsers.TrySplitQualifiedTypeMember(
                source.PackagePath,
                allowPlatformPrefixFallback: true);
            if (split != null)
            {
                positionalMembers.Add(split.Value.MemberName);
                var probe = split.Value.Probe;
                source = probe.Kind == SourceResolver.LocalSourceKind.Platform
                    ? source with { PackagePath = null, PlatformAssembly = probe.SourceName, TypeName = probe.Remainder }
                    : source with { PackagePath = probe.SourceName, TypeName = probe.Remainder };
                resolvedQualifiedTypeMember = true;
            }
        }

        // If source resolution left us with an unresolved package that is actually
        // a qualified type name (e.g., "System.Text.Json.JsonDocument"), split it
        // so the user can write: member System.Text.Json.JsonDocument Parse
        if (!resolvedQualifiedTypeMember
            && sourceSelection.ExplicitPackage == null
            && source.PackagePath != null
            && source.PlatformAssembly == null
            && source.AssemblyPath == null)
        {
            var probe = SourceResolver.TryResolveQualifiedTypeName(
                source.PackagePath,
                allowPlatformPrefixFallback: true);
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
        //      and: member JsonSerializer.Serialize --package System.Text.Json
        //   → source=System.Text.Json, type=JsonDocument, member=Parse
        // Skip if the right part contains '<' — that's a generic type name (e.g., Generic.List<T>),
        // not a type.member pair.
        if (typeName != null
            && ((!sourceInputs.HasExplicitSource && sourceInputs.Args.Length > 1)
                || (sourceInputs.HasExplicitSource
                    && sourceInputs.Args.Length == 1
                    && IsLocalDottedMemberWithExplicitSource(typeName)))
            && typeName.Contains('.')
            && positionalMembers.Count == 0
            && optionMembers.Length == 0)
        {
            var (splitTypeName, splitMemberName) = SharedParsers.SplitTrailingMember(typeName);
            if (splitMemberName != null)
            {
                positionalMembers.Add(splitMemberName);
                typeName = splitTypeName;
            }
        }

        // Check for unrecognized options in positional args
        var badOption = positionalMembers.FirstOrDefault(m => m.StartsWith('-'));
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        // Combine -m option with positional members
        var allMembers = optionMembers.Concat(positionalMembers).ToArray();
        var ctorOnly = parseResult.GetValue(args.CtorOption);

        // Process dotted syntax and overload shorthand
        var (dottedTypeFilter, shorthandIndex, memberDigest, memberKindFilter) = SharedParsers.ProcessMemberArguments(allMembers);

        // Use extracted type name if no explicit type was provided
        if (dottedTypeFilter != null && string.IsNullOrEmpty(typeName))
            typeName = dottedTypeFilter;

        // Build member filter
        var (memberFilter, memberLimit) = BuildMemberFilter(allMembers, ctorOnly, out var clearShorthand);
        if (clearShorthand)
            shorthandIndex = null;

        var kindValues = parseResult.GetValue(args.KindOption) ?? [];
        var kindFilter = SharedParsers.ParseKindFilter(kindValues);
        kindFilter.UnionWith(memberKindFilter);

        var showMemberIndex = parseResult.GetValue(args.SelectOption);
        var select = opts.ParseSelect(parseResult);
        if (showMemberIndex)
            select = [.. select ?? [], SectionNames.MemberIndex];

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
            BrowsableUrls = parseResult.GetValue(opts.BrowsableUrls)
                && !parseResult.GetValue(opts.RawUrls),
            JsonOutput = parseResult.GetValue(opts.Json),
            CompactJson = parseResult.GetValue(args.CompactOption),
            OneLine = opts.ResolveOneLine(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            PlainText = parseResult.GetValue(opts.PlainText),
            Bare = parseResult.GetValue(opts.Bare),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            UnsafeOnly = parseResult.GetValue(args.UnsafeOption),
            CtorOnly = ctorOnly,
            OverloadIndex = parseResult.GetValue(args.IndexOption) ?? shorthandIndex,
            MemberDigest = memberDigest,
            ShowMemberIndex = showMemberIndex,
            CallerScopeDirectories = parseResult.GetValue(args.BinOption) ?? [],
            CallerScopeProjects = parseResult.GetValue(args.ProjectOption) ?? [],
            CallerScopePackages = parseResult.GetValue(args.CallerPackageOption) ?? [],
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = select,
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Rows = opts.ParseRows(parseResult),
            Schema = opts.ParseSchema(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
        };

        options = options with
        {
            TipLevel = options.FormatExplicitlySet || options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || memberLimit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        };

        return new Success(options);
    }

    private static bool IsLocalDottedMemberWithExplicitSource(string typeName)
    {
        var (splitTypeName, splitMemberName) = SharedParsers.SplitTrailingMember(typeName);
        return splitMemberName != null && splitTypeName != null && !splitTypeName.Contains('.');
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
