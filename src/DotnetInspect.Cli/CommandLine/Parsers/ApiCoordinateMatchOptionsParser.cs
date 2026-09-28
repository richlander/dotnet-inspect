using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Services;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.CommandLine;

internal static class ApiCoordinateMatchOptionsParser
{
    internal abstract record Result;

    internal sealed record Success(
        ApiCoordinateMatchRequest Request,
        NuGetSourceOptions SourceOptions,
        OutputFormat Format,
        bool Envelope,
        bool CompactJson,
        WorkspaceShareFormat? ShareFormat = null) : Result;

    internal sealed record Failure(OptionError Error) : Result;

    internal static Result ParseType(
        ParseResult parseResult,
        SharedOptions options,
        TypeOptionsParser.TypeCommandArgs args,
        Option<bool> matchOption)
    {
        if (SharedParsers.GetStructuralParseError(parseResult) is { } parseError)
            return new Failure(parseError);

        if (GetUnsupportedOption(
                parseResult,
                [
                    args.PackageOption,
                    args.AssemblyOption,
                    args.TfmOption,
                    args.AllOption,
                    matchOption,
                    args.CompactOption,
                    options.Json,
                    options.Envelope,
                    options.Markdown,
                    options.PlainText,
                    options.Verbose,
                    options.Tips,
                    options.Source,
                    options.AddSource,
                    options.NuGetConfig,
                ]) is { } unsupported)
        {
            return new Failure(
                $"--match cannot be combined with {unsupported}.");
        }

        string[] positional = parseResult.GetValue(args.ArgsArg) ?? [];
        if (positional.Length != 1)
        {
            return new Failure(
                "type --match requires exactly one source Type query.");
        }

        return CreateSuccess(
            parseResult,
            options,
            parseResult.GetValue(args.PackageOption),
            positional[0],
            member: null,
            parseResult.GetValue(args.TfmOption),
            parseResult.GetValue(args.AssemblyOption),
            parseResult.GetValue(args.CompactOption),
            includeAll: parseResult.GetValue(args.AllOption));
    }

    internal static Result ParseMember(
        ParseResult parseResult,
        SharedOptions options,
        MemberOptionsParser.MemberCommandArgs args,
        Option<bool> matchOption)
    {
        if (SharedParsers.GetStructuralParseError(parseResult) is { } parseError)
            return new Failure(parseError);

        if (GetUnsupportedOption(
                parseResult,
                [
                    args.PackageOption,
                    args.AssemblyOption,
                    args.TfmOption,
                    args.AllOption,
                    matchOption,
                    args.MemberOption,
                    args.IndexOption,
                    args.ShareOption,
                    args.CompactOption,
                    options.Json,
                    options.Envelope,
                    options.Markdown,
                    options.PlainText,
                    options.Verbose,
                    options.Tips,
                    options.Source,
                    options.AddSource,
                    options.NuGetConfig,
                ]) is { } unsupported)
        {
            return new Failure(
                $"--match cannot be combined with {unsupported}.");
        }

        string[] positional = parseResult.GetValue(args.ArgsArg) ?? [];
        string[] optionMembers =
            parseResult.GetValue(args.MemberOption) ?? [];
        int? index = parseResult.GetValue(args.IndexOption);
        if (index is <= 0)
        {
            return new Failure(
                "--index requires a positive one-based overload ordinal.");
        }

        string? type;
        string? member;
        if (positional.Length == 2 && optionMembers.Length == 0)
        {
            type = positional[0];
            member = positional[1];
        }
        else if (positional.Length == 1 && optionMembers.Length == 1)
        {
            type = positional[0];
            member = optionMembers[0];
        }
        else if (positional.Length == 1 && optionMembers.Length == 0)
        {
            if (!TrySplitUnambiguousMemberTarget(
                    positional[0],
                    index,
                    out type,
                    out member))
            {
                return new Failure(
                    "member --match requires one source Type query and one member selector.");
            }
        }
        else
        {
            return new Failure(
                "member --match requires one source Type query and one member selector.");
        }

        MemberTargetSelector selector =
            MemberTargetSelector.Parse(member!);
        if (string.IsNullOrWhiteSpace(selector.Name)
            || HasWildcard(selector.Name)
            || int.TryParse(selector.Name, out _))
        {
            return new Failure(
                "member --match requires one exact member name or selector.");
        }
        if (selector.OverloadIndex is <= 0)
        {
            return new Failure(
                "A Name:N selector requires a positive one-based overload ordinal.");
        }

        string requestedSelector = selector.RequestedText;
        if (index is { } overloadIndex)
        {
            if (selector.OverloadIndex is not null)
            {
                return new Failure(
                    "--index cannot be combined with a Name:N selector.");
            }

            requestedSelector = $"{requestedSelector}:{overloadIndex}";
        }

        return CreateSuccess(
            parseResult,
            options,
            parseResult.GetValue(args.PackageOption),
            type!,
            requestedSelector,
            parseResult.GetValue(args.TfmOption),
            parseResult.GetValue(args.AssemblyOption),
            parseResult.GetValue(args.CompactOption),
            parseResult.GetValue(args.AllOption),
            WorkspaceShareOption.Parse(
                parseResult,
                args.ShareOption));
    }

    internal static bool TryCreateRequest(
        string packageRange,
        string type,
        out ApiCoordinateMatchRequest? request,
        out OptionError? error,
        string? member = null,
        string? targetFramework = null,
        string? library = null,
        bool includeAll = false)
    {
        request = null;
        error = null;
        if (!PackageVersionRange.TryParse(
                packageRange,
                out PackageVersionRange? range,
                out string? rangeError))
        {
            error =
                rangeError
                ?? "--match requires --package Package@source..destination with two literal version endpoints.";
            return false;
        }

        string typeQuery = type.Trim();
        if (typeQuery.Length == 0
            || TypeMatcher.IsTypeGlobPattern(typeQuery)
            || CommandLineHelpers.TryClassifyAsFilePath(
                typeQuery,
                out _,
                out _))
        {
            error =
                "--match requires one exact source Type query; Type globs and listings are not supported.";
            return false;
        }
        if (!PackageExtractor.IsValidPackageId(range!.PackageId))
        {
            error =
                "--match requires a Package id, not a local package or Library path.";
            return false;
        }

        try
        {
            request = new ApiCoordinateMatchRequest(
                range.PackageId,
                range.Start.ToNormalizedString(),
                range.End.ToNormalizedString(),
                typeQuery,
                member,
                targetFramework,
                library,
                includeAll);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static Result CreateSuccess(
        ParseResult parseResult,
        SharedOptions options,
        string? packageRange,
        string type,
        string? member,
        string? targetFramework,
        string? library,
        bool compactJson,
        bool includeAll = false,
        WorkspaceShareFormat? shareFormat = null)
    {
        if (string.IsNullOrWhiteSpace(packageRange))
        {
            return new Failure(
                "--match requires --package Package@source..destination with two literal version endpoints.");
        }

        if (!TryCreateRequest(
                packageRange,
                type,
                out ApiCoordinateMatchRequest? request,
                out OptionError? requestError,
                member,
                targetFramework,
                library,
                includeAll))
        {
            return new Failure(requestError!.Value);
        }

        bool envelope = parseResult.GetValue(options.Envelope);
        int explicitFormatCount =
            (parseResult.GetResult(options.Json) is { Implicit: false } ? 1 : 0)
            + (parseResult.GetResult(options.Markdown) is { Implicit: false } ? 1 : 0)
            + (parseResult.GetResult(options.PlainText) is { Implicit: false } ? 1 : 0)
            + (parseResult.GetResult(options.Envelope) is { Implicit: false } ? 1 : 0);
        if (explicitFormatCount > 1)
        {
            return new Failure(
                "--match accepts only one of --markdown, --plaintext, --json, or --envelope.");
        }

        OutputFormat format =
            envelope
                ? OutputFormat.Json
                : options.ResolveFormat(parseResult);
        if (format is not (
                OutputFormat.Markdown
                or OutputFormat.PlainText
                or OutputFormat.Json))
        {
            return new Failure(
                "--match supports Markdown, plain text, --json, or --envelope output.");
        }

        if (compactJson
            && !envelope
            && format != OutputFormat.Json)
        {
            return new Failure(
                "--compact requires --json or --envelope with --match.");
        }

        return new Success(
            request!,
            options.ParseNuGetSourceOptions(parseResult),
            format,
            envelope,
            compactJson,
            shareFormat);
    }

    private static string? GetUnsupportedOption(
        ParseResult parseResult,
        IReadOnlyCollection<Option> allowed)
    {
        HashSet<Option> allowSet = [.. allowed];
        return parseResult.CommandResult.Children
            .OfType<OptionResult>()
            .Where(result => !result.Implicit
                && !allowSet.Contains(result.Option))
            .Select(result => result.Option.Name)
            .FirstOrDefault();
    }

    private static bool TrySplitUnambiguousMemberTarget(
        string value,
        int? index,
        out string? type,
        out string? member)
    {
        type = null;
        member = null;
        var split = SharedParsers.SplitTrailingMember(value);
        if (split.MemberName is null)
            return false;

        MemberTargetSelector selector =
            MemberTargetSelector.Parse(split.MemberName);
        if (index is null
            && selector.OverloadIndex is null
            && string.IsNullOrWhiteSpace(selector.DigestPrefix))
        {
            return false;
        }

        type = split.TypeName;
        member = split.MemberName;
        return !string.IsNullOrWhiteSpace(type);
    }

    private static bool HasWildcard(string value) =>
        value.Contains('*', StringComparison.Ordinal)
        || value.Contains('?', StringComparison.Ordinal);

}
