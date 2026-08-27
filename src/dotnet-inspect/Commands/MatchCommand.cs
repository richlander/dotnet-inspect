using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Analysis;
using ILInspector.Decompiler.Pipeline;
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

        // The Implementation Diff section renders one C#/IL side-by-side row set, the same
        // "one section at a time" restriction diff already enforces for tabular/TSV/JSONL output
        // (issue #4304 Slice 4).
        if (options.IncludeImplementation && (options.Tabular || options.Tsv || options.Jsonl))
        {
            CommandError.Write(
                "--implementation cannot be combined with --table, --tsv, or --jsonl; "
                    + "render Markdown (the default) or --json instead.");
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

            var left = ResolveSelector(loaded.Api, loaded.ApiDllPath, options.LeftSelector);
            if (left.Error is not null)
            {
                CommandError.Write(left.Error);
                return 1;
            }

            var right = ResolveSelector(loaded.Api, loaded.ApiDllPath, options.RightSelector);
            if (right.Error is not null)
            {
                CommandError.Write(right.Error);
                return 1;
            }

            // Both tokens must index the same physical module. A type resolved through a type
            // forwarder (e.g. a facade assembly forwarding to its implementation assembly) carries
            // metadata tokens that index that *target* assembly, not loaded.ApiDllPath — comparing
            // them against the wrong image would silently reinterpret an unrelated MethodDef row
            // (issue #4304 Slice 3 review). Require both selectors to originate from the same file.
            if (!string.Equals(left.OriginAssemblyPath, right.OriginAssemblyPath, StringComparison.Ordinal))
            {
                CommandError.Write(
                    $"'{options.LeftSelector}' and '{options.RightSelector}' resolve to different assemblies "
                        + $"({Path.GetFileName(left.OriginAssemblyPath)} vs {Path.GetFileName(right.OriginAssemblyPath)}); "
                        + "match compares two methods within one retained assembly.");
                return 1;
            }

            ResearchMatchResult result = ResearchMatch.Compare(
                left.OriginAssemblyPath!,
                MetadataTokens.MethodDefinitionHandle(left.Token!.Value),
                MetadataTokens.MethodDefinitionHandle(right.Token!.Value));

            ImplementationDiffView? implementationView = options.IncludeImplementation
                ? BuildImplementationDiffView(left, right)
                : null;

            if (options.JsonOutput)
            {
                if (implementationView is null)
                {
                    JsonOutputHelper.Write(
                        result.Document,
                        StructuralCloneComparisonDocumentJsonContext.Default.StructuralCloneComparisonDocument,
                        StructuralCloneComparisonDocumentCompactJsonContext.Default.StructuralCloneComparisonDocument,
                        options.CompactJson);
                }
                else
                {
                    var envelope = new MatchImplementationDocument(result.Document, implementationView);
                    JsonOutputHelper.Write(
                        envelope,
                        MatchImplementationDocumentJsonContext.Default.MatchImplementationDocument,
                        MatchImplementationDocumentCompactJsonContext.Default.MatchImplementationDocument,
                        options.CompactJson);
                }
            }
            else
            {
                WriteMarkoutOutput(left.Display!, right.Display!, result, implementationView, options);
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

    internal readonly record struct ResolvedSelector(int? Token, string? Display, string? OriginAssemblyPath, string? Error);

    /// <summary>
    /// Resolves a <c>Type.Member</c> selector to the unique <see cref="ApiMember.MetadataToken"/>
    /// it names. Reuses <see cref="ApiTypeLookupService"/>'s type-vs-member boundary detection
    /// rather than re-splitting the string, since that boundary is not knowable syntactically.
    /// </summary>
    internal static ResolvedSelector ResolveSelector(ApiSurface api, string apiDllPath, string selector)
    {
        var lookup = ApiTypeLookupService.LookupType(api, selector);
        if (!lookup.Found || lookup.ImpliedMember is null)
        {
            return new ResolvedSelector(
                null,
                null,
                null,
                $"'{selector}' must name a Type.Member selector (e.g. MyType.MyMethod).");
        }

        var apiType = lookup.Type!;
        var memberName = lookup.ImpliedMember;

        // A property/event member has no method-def token of its own; its addressable body
        // lives on an accessor. Only a member with exactly one addressable accessor (a
        // get-only property, or an add-only/remove-only event) resolves unambiguously here —
        // a member with both a getter and a setter (or an adder and a remover) would otherwise
        // silently prefer one accessor over the other and compare the wrong body without
        // telling the caller (issue #4304 Slice 3 review).
        static int?[] AccessorTokens(ILInspector.Metadata.ApiMember m)
            => [m.GetterToken, m.SetterToken, m.AdderToken, m.RemoverToken];

        static int? MethodToken(ILInspector.Metadata.ApiMember m)
        {
            if (m.MetadataToken.HasValue)
                return m.MetadataToken;

            var accessors = AccessorTokens(m).Where(t => t.HasValue).ToList();
            return accessors.Count == 1 ? accessors[0] : null;
        }

        var candidates = apiType.Members
            .Where(m => string.Equals(m.Name, memberName, StringComparison.Ordinal)
                && MethodToken(m).HasValue)
            .ToList();

        if (candidates.Count == 0)
        {
            var namedMember = apiType.Members.FirstOrDefault(
                m => string.Equals(m.Name, memberName, StringComparison.Ordinal));
            var reason = namedMember is not null && AccessorTokens(namedMember).Count(t => t.HasValue) > 1
                ? "names more than one addressable accessor (e.g. both a getter and a setter); "
                    + "select the accessor directly (e.g. Type.get_Member) instead"
                : "did not resolve to a member with an addressable metadata token";
            return new ResolvedSelector(
                null,
                null,
                null,
                $"'{selector}' {reason}.");
        }

        if (candidates.Count > 1)
        {
            return new ResolvedSelector(
                null,
                null,
                null,
                $"'{selector}' matches {candidates.Count} overloads; narrow the pattern or use a fully-qualified selector.");
        }

        // The candidate's token indexes the assembly it was extracted from —
        // apiType.SourceAssemblyPath for a type resolved through a type forwarder, otherwise the
        // extraction dll (apiDllPath). Comparing a forwarded type's token against apiDllPath would
        // silently reinterpret an unrelated MethodDef row in the wrong image.
        var originAssemblyPath = apiType.SourceAssemblyPath ?? apiDllPath;
        return new ResolvedSelector(MethodToken(candidates[0]), $"{apiType.FullName}.{memberName}", originAssemblyPath, null);
    }

    /// <summary>
    /// Independently decompiles both selectors' method bodies and projects a C#/IL side-by-side
    /// implementation-diff view, reusing <see cref="ImplementationDiff.CompareMembers"/> and
    /// <see cref="DiffOutputFormatter.BuildImplementationDiffView"/> exactly as <c>diff</c>'s
    /// Implementation Diff section does (issue #4304 Slice 4). <c>CompareMembers</c> makes no
    /// identity assumption about its two handles, so it applies unchanged to match's
    /// non-identity-matched pair.
    /// </summary>
    static ImplementationDiffView BuildImplementationDiffView(ResolvedSelector left, ResolvedSelector right)
    {
        using var source = MetadataSource.Open(left.OriginAssemblyPath!);
        var memberDiff = ImplementationDiff.CompareMembers(
            source,
            MetadataTokens.MethodDefinitionHandle(left.Token!.Value),
            source,
            MetadataTokens.MethodDefinitionHandle(right.Token!.Value));

        var member = new ImplementationDiffMember(memberDiff.Subject, memberDiff.Changes)
        {
            SourceComparison = memberDiff.SourceComparison,
        };
        // BuildImplementationDiffView only reads diff.Members; this ResearchComparison is a
        // type-satisfying formality, not a second, independently meaningful comparison result.
        var diff = new ImplementationDiffResult(
            [member],
            new ResearchComparison(memberDiff.Changes.ToImmutableArray()));

        return DiffOutputFormatter.BuildImplementationDiffView(
            $"{left.Display} vs {right.Display}",
            diff,
            left.Display!,
            right.Display!);
    }

    static void WriteMarkoutOutput(
        string leftDisplay,
        string rightDisplay,
        ResearchMatchResult result,
        ImplementationDiffView? implementationView,
        MatchOptions options)
    {
        var view = MatchOutputFormatter.BuildView(leftDisplay, rightDisplay, result);

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                options.Rows, options.HumanRowWindowNote);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(Console.Out, options.Rows,
                opts =>
                {
                    var text = MarkoutSerializer.Serialize(view, SearchViewContext.Default, opts);
                    if (implementationView is not null)
                    {
                        var implementationText = DiffOutputFormatter.RenderImplementationDiffView(implementationView, opts);
                        text = $"{text}\n\n{implementationText}";
                    }

                    return text;
                },
                humanRowWindowNote: options.HumanRowWindowNote);
        }
    }
}
