using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Services;
using DotnetInspector.Presentation;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.CommandLine;

internal static class TypeOverviewHierarchyRoute
{
    internal static TypeOverviewHierarchyPresentationFormat? Select(
        ParseResult result,
        SharedOptions opts,
        TypeOptionsParser.TypeCommandArgs args,
        Option<bool> match,
        out string? error)
    {
        error = null;
        bool mermaid = result.GetValue(opts.Mermaid);
        bool tree = result.GetValue(opts.Tree);
        bool competing = result.GetValue(match)
            || result.GetValue(opts.Json)
            || result.GetValue(opts.Markdown)
            || result.GetValue(opts.PlainText)
            || result.GetValue(opts.Table)
            || result.GetValue(opts.Tsv)
            || result.GetValue(opts.Jsonl)
            || result.GetValue(opts.Envelope)
            || result.GetValue(opts.Raw)
            || result.GetValue(opts.NoHeaders)
            || result.GetValue(opts.Count)
            || result.GetValue(opts.Print)
            || result.GetValue(opts.Value)
            || result.GetValue(opts.Urls)
            || result.GetValue(opts.Paths)
            || result.GetValue(opts.JsonArray)
            || result.GetValue(opts.Schema)
            || result.GetValue(opts.Details)
            || result.GetValue(opts.Effective)
            || result.GetValue(opts.Lines)
            || result.GetValue(opts.TailLines)
            || result.GetValue(opts.Head)
            || result.GetValue(opts.Tail)
            || result.GetValue(args.UnsafeOption)
            || result.GetValue(args.CompactOption)
            || result.GetValue(args.WorkspaceOption) is not null
            || result.GetValue(args.ShareOption) is not null
            || result.GetValue(args.TypeFilterOption) is not null
            || result.GetValue(args.MemberOption) is { Length: > 0 }
            || result.GetValue(args.KindOption) is { Length: > 0 }
            || result.GetValue(opts.Limit) is not null
            || result.GetValue(opts.Rows) is not null
            || result.GetResult(opts.Select) is { Implicit: false }
            || result.GetResult(opts.Discover) is { Implicit: false }
            || result.GetResult(opts.QueryHelp) is { Implicit: false }
            || result.GetResult(opts.Columns) is { Implicit: false }
            || result.GetResult(opts.Fields) is { Implicit: false }
            || result.GetResult(opts.Row) is { Implicit: false }
            || result.GetResult(opts.Verbosity) is { Implicit: false }
            || result.GetResult(opts.PerformanceTriageTop) is { Implicit: false }
            || result.GetResult(opts.RowWhere) is { Implicit: false }
            || result.GetResult(opts.RowOrderBy) is { Implicit: false }
            || result.GetValue(opts.PerformanceTriageLoop)
            || result.GetResult(opts.PerformanceTriageShape) is { Implicit: false }
            || result.GetResult(opts.PerformanceTriageMinConfidence) is { Implicit: false }
            || result.GetValue(opts.Taste)
            || result.GetValue(opts.ReadableNames);

        if (mermaid && (tree || competing))
        {
            error = "--mermaid requires a standalone exact Type hierarchy without another operation, filter, window, verbosity, or format.";
            return null;
        }

        if (!mermaid && (competing || opts.IsFormatExplicitlySet(result) && !tree))
            return null;

        SharedParsers.SourceSelectionInputs inputs =
            SharedParsers.ReadSourceSelectionInputs(
                result,
                args.ArgsArg,
                args.PackageOption,
                args.AssemblyOption,
                args.PlatformOption);
        bool explicitSource =
            inputs.HasExplicitSource
            || result.GetValue(args.ProjectOption) is not null;
        string? target = explicitSource
            ? inputs.Args.Length == 1 ? inputs.Args[0] : null
            : inputs.Args.Length == 2 ? inputs.Args[1]
            : inputs.Args.Length == 1 ? inputs.Args[0] : null;
        bool exact = !string.IsNullOrWhiteSpace(target)
            && !TypeMatcher.IsTypeGlobPattern(target);
        if (exact && !explicitSource && inputs.Args.Length == 1)
        {
            // An implicit platform namespace or library prefix is not a Type.
            // Only a locally resolved exact Type may select this route.
            exact = SourceResolver.TryResolveBareCoreLibTypeName(target!) is not null
                || SourceResolver.TryResolveQualifiedTypeName(
                    target!,
                    sourceOptions: null,
                    allowPlatformPrefixFallback: false)
                    is { Kind: SourceResolver.LocalSourceKind.Platform };
        }
        if (exact
            && mermaid
            && !result.GetValue(args.AllOption)
            && result.GetValue(args.PlatformOption) is { } platform)
        {
            var (assemblyPath, _, _, _) =
                PlatformResolver.ResolveAssembly(platform);
            if (assemblyPath is not null)
            {
                try
                {
                    exact = AssemblyReader.FindUniquePublicType(
                        assemblyPath,
                        target!) is not null;
                }
                catch (Exception failure)
                    when (failure is IOException
                        or UnauthorizedAccessException
                        or BadImageFormatException)
                {
                    exact = false;
                }
            }
        }

        if (!exact)
        {
            if (mermaid)
                error = "--mermaid requires one exact Type; listing, glob, and namespace/prefix targets are not supported.";
            return null;
        }

        return mermaid
            ? TypeOverviewHierarchyPresentationFormat.Mermaid
            : TypeOverviewHierarchyPresentationFormat.Tree;
    }
}
