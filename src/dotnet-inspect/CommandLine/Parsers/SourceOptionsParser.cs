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

        string? preResolvedMemberName = null;
        int? preResolvedOverloadIndex = null;
        if (!sourceInputs.HasExplicitSource && sourceInputs.Args.Length == 1)
        {
            var split = TrySplitImplicitPlatformMember(sourceInputs.Args[0]);
            if (split != null)
            {
                preResolvedMemberName = split.Value.MemberName;
                preResolvedOverloadIndex = split.Value.OverloadIndex;
                sourceInputs = sourceInputs with
                {
                    Args = split.Value.Args,
                    ExplicitPlatform = split.Value.ExplicitPlatform,
                    HasExplicitSource = split.Value.ExplicitPlatform != null
                };
            }
        }

        // Resolve source
        var sourceSelection = await SharedParsers.ResolveSourceSelectionAsync(
            sourceInputs, parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: true);
        var source = sourceSelection.Source;

        if (source.VersionError)
            return new VersionError(source.VersionErrorMessage!);

        // Capture member name: -m/--member option, positional args, or Type.Member dot syntax
        string? memberName = parseResult.GetValue(args.MemberOption) ?? preResolvedMemberName;
        int? overloadIndex = preResolvedOverloadIndex;
        {
            // Fall back to positional or dot syntax if -m not specified
            if (memberName == null)
            {
                var positionalMembers = new List<string>();
                if (!sourceSelection.HasExplicitSource && sourceSelection.Args.Length == 1)
                {
                    var split = SharedParsers.TrySplitQualifiedTypeMember(
                        sourceSelection.Args[0],
                        allowPlatformPrefixFallback: true);
                    if (split != null)
                    {
                        positionalMembers.Add(split.Value.MemberName);
                        var probe = split.Value.Probe;
                        source = probe.Kind == SourceResolver.LocalSourceKind.Platform
                            ? source with { PackagePath = null, PlatformAssembly = probe.SourceName, TypeName = probe.Remainder }
                            : source with { PackagePath = probe.SourceName, TypeName = probe.Remainder };
                    }
                }

                if (sourceSelection.HasExplicitSource && sourceSelection.Args.Length >= 2)
                    positionalMembers.AddRange(sourceSelection.Args[1..]);
                else if (!sourceSelection.HasExplicitSource && sourceSelection.Args.Length >= 3)
                    positionalMembers.AddRange(sourceSelection.Args[2..]);

                // Handle source-explicit Type.Member shorthand.
                var typeName = source.TypeName;
                if (typeName != null && typeName.Contains('.') && positionalMembers.Count == 0)
                {
                    var exactImplicitQualifiedType = !sourceSelection.HasExplicitSource
                        && sourceSelection.Args.Length == 1
                        && source.PackagePath == null
                        && source.PlatformAssembly != null
                        && (string.Equals(
                                ILInspector.Metadata.TypeMatcher.Normalize(sourceSelection.Args[0]),
                                typeName,
                                StringComparison.OrdinalIgnoreCase)
                            || SourceResolver.TryResolveQualifiedTypeName(sourceSelection.Args[0], allowPlatformPrefixFallback: false) != null);

                    var qualifiedSplit = sourceSelection.HasExplicitSource
                        ? SharedParsers.TrySplitQualifiedTypeMember(typeName, allowPlatformPrefixFallback: true)
                        : null;
                    if (qualifiedSplit != null && ProbeMatchesExplicitSource(sourceSelection, qualifiedSplit.Value.Probe))
                    {
                        positionalMembers.Add(qualifiedSplit.Value.MemberName);
                        source = source with { TypeName = qualifiedSplit.Value.Probe.Remainder };
                    }
                    else if (!exactImplicitQualifiedType && !IsExplicitSourceQualifiedTypeName(sourceSelection, typeName))
                    {
                        var (splitTypeName, splitMemberName) = SharedParsers.SplitTrailingMember(typeName);
                        if (splitMemberName != null)
                        {
                            positionalMembers.Add(splitMemberName);
                            source = source with { TypeName = splitTypeName };
                        }
                    }
                }

                if (positionalMembers.Count > 0)
                    memberName = positionalMembers[0];
            }

            // Parse overload index shorthand and normalize generic method selectors.
            if (memberName != null)
            {
                var (name, idx) = SharedParsers.ParseOverloadShorthand(memberName);
                if (idx != null)
                    overloadIndex = idx;
                memberName = name;
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
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
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
            TipLevel = options.FormatExplicitlySet || options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || typeLimit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        };

        return new Success(options);
    }

    private static (string[] Args, string? ExplicitPlatform, string MemberName, int? OverloadIndex)? TrySplitImplicitPlatformMember(string value)
    {
        for (var i = value.Length - 1; i > 0; i--)
        {
            if (value[i] != '.')
                continue;

            var typeCandidate = value[..i];
            var memberSelector = value[(i + 1)..];
            if (string.IsNullOrWhiteSpace(memberSelector))
                continue;

            var probe = SourceResolver.TryResolveQualifiedTypeName(typeCandidate, allowPlatformPrefixFallback: true);
            if (probe == null)
            {
                if (!typeCandidate.Contains('<') && !memberSelector.Contains('<'))
                    continue;

                var (genericMemberName, genericOverloadIndex) = SharedParsers.ParseOverloadShorthand(memberSelector);
                return ([typeCandidate], null, genericMemberName, genericOverloadIndex);
            }

            var (memberName, overloadIndex) = SharedParsers.ParseOverloadShorthand(memberSelector);
            return ([probe.Remainder], probe.SourceName, memberName, overloadIndex);
        }

        return null;
    }

    private static bool ProbeMatchesExplicitSource(
        SharedParsers.SourceSelection sourceSelection,
        SourceResolver.LocalProbeResult probe)
    {
        var explicitName = GetExplicitSourceName(sourceSelection);
        return explicitName != null
            && string.Equals(explicitName, NormalizeSourceName(probe.SourceName), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplicitSourceQualifiedTypeName(
        SharedParsers.SourceSelection sourceSelection,
        string typeName)
    {
        var explicitName = GetExplicitSourceName(sourceSelection);
        return explicitName != null
            && typeName.StartsWith($"{explicitName}.", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetExplicitSourceName(SharedParsers.SourceSelection sourceSelection) =>
        NormalizeSourceName(sourceSelection.ExplicitPlatform)
        ?? NormalizeSourceName(sourceSelection.ExplicitPackage)
        ?? NormalizeSourceName(sourceSelection.ExplicitAssembly);

    private static string? NormalizeSourceName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var name = value;
        var versionIndex = name.IndexOf('@');
        if (versionIndex >= 0)
            name = name[..versionIndex];

        name = Path.GetFileName(name);
        return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }
}
