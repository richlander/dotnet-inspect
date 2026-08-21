using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Compares two methods from one retained assembly by structural clone equivalence, projecting
/// the result onto the discrete <see cref="ResearchMatchOutcome"/> model (issue #4304).
/// </summary>
/// <remarks>
/// This is deliberately A-vs-A: both selectors resolve against the same opened assembly. Matching
/// the same declared identity across two different assemblies (A-vs-A', e.g. a version bump) is a
/// distinct, not-yet-built cross-module capability; use <c>diff</c> for identity-driven A-vs-B
/// comparisons instead.
/// </remarks>
public static class MatchCommand
{
    public const string Name = "match";

    public static async Task<int> ExecuteAsync(MatchOptions options)
    {
        if (string.IsNullOrEmpty(options.LeftSelector) || string.IsNullOrEmpty(options.RightSelector))
        {
            CommandError.Write("match requires two method selectors (Type.Member).");
            CommandError.WriteLine("Usage: dotnet-inspect match <Type.MemberA> <Type.MemberB> --package <pkg>");
            return 1;
        }

        var (source, sourceError) = await ApiSourceResolver.ResolveAsync(options with { TypeName = null });
        if (sourceError.HasValue)
            return sourceError.Value;

        var logger = source.Context.Logger;

        try
        {
            var loaded = ApiServices.LoadFullApi(
                source.SearchPath, source.RuntimeAssemblyPath, options.PackagePath, source.PackageName,
                source.ApiSource, source.ApiVersion, source.SelectedTfm, logger, options);
            if (loaded == null)
            {
                CommandError.Write("Could not extract API from library.");
                return 1;
            }

            var left = ResolveSelector(loaded.Api, options.LeftSelector);
            if (left.Error is not null)
            {
                CommandError.Write(left.Error);
                return 1;
            }

            var right = ResolveSelector(loaded.Api, options.RightSelector);
            if (right.Error is not null)
            {
                CommandError.Write(right.Error);
                return 1;
            }

            ResearchMatchResult result = ResearchMatch.Compare(
                loaded.ApiDllPath,
                MetadataTokens.MethodDefinitionHandle(left.Token!.Value),
                MetadataTokens.MethodDefinitionHandle(right.Token!.Value));

            if (options.JsonOutput)
            {
                JsonOutputHelper.Write(
                    result.Document,
                    StructuralCloneComparisonDocumentJsonContext.Default.StructuralCloneComparisonDocument,
                    StructuralCloneComparisonDocumentCompactJsonContext.Default.StructuralCloneComparisonDocument,
                    options.CompactJson);
            }
            else
            {
                WriteMarkoutOutput(left.Display!, right.Display!, result, options);
            }

            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        finally
        {
            if (source.TempDir is not null)
                TryDeleteTempDir(source.TempDir);
        }
    }

    static void TryDeleteTempDir(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal readonly record struct ResolvedSelector(int? Token, string? Display, string? Error);

    /// <summary>
    /// Resolves a <c>Type.Member</c> selector to the unique <see cref="ApiMember.MetadataToken"/>
    /// it names. Reuses <see cref="ApiTypeLookupService"/>'s type-vs-member boundary detection
    /// rather than re-splitting the string, since that boundary is not knowable syntactically.
    /// </summary>
    internal static ResolvedSelector ResolveSelector(ApiSurface api, string selector)
    {
        var lookup = ApiTypeLookupService.LookupType(api, selector);
        if (!lookup.Found || lookup.ImpliedMember is null)
        {
            return new ResolvedSelector(
                null,
                null,
                $"'{selector}' must name a Type.Member selector (e.g. MyType.MyMethod).");
        }

        var apiType = lookup.Type!;
        var memberName = lookup.ImpliedMember;

        // A property/event member has no method-def token of its own; its addressable
        // body lives on the getter (or adder) accessor, so fall back to that when the
        // member itself carries no MetadataToken.
        static int? MethodToken(ILInspector.Metadata.ApiMember m)
            => m.MetadataToken ?? m.GetterToken ?? m.AdderToken;

        var candidates = apiType.Members
            .Where(m => string.Equals(m.Name, memberName, StringComparison.Ordinal)
                && MethodToken(m).HasValue)
            .ToList();

        if (candidates.Count == 0)
        {
            return new ResolvedSelector(
                null,
                null,
                $"'{selector}' did not resolve to a member with an addressable metadata token.");
        }

        if (candidates.Count > 1)
        {
            return new ResolvedSelector(
                null,
                null,
                $"'{selector}' matches {candidates.Count} overloads; disambiguate with Type.Member:N.");
        }

        return new ResolvedSelector(MethodToken(candidates[0]), $"{apiType.FullName}.{memberName}", null);
    }

    static void WriteMarkoutOutput(string leftDisplay, string rightDisplay, ResearchMatchResult result, MatchOptions options)
    {
        var view = MatchOutputFormatter.BuildView(leftDisplay, rightDisplay, result);

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                options.Rows);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(Console.Out, options.Rows,
                opts => MarkoutSerializer.Serialize(view, SearchViewContext.Default, opts));
        }
    }
}
