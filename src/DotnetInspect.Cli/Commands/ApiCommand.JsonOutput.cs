using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Net;
using DotnetInspect.Cli.CommandLine;
using CSharpText.MemberSlicing;
using DotnetInspect.Cli.Inspectors;
using ILInspector.Metadata;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using Markout;
using Markout.Formatting;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;

using Decompiler = ILInspector.Decompiler;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Shared helpers for type and member commands.
/// </summary>
public partial class ApiCommand
{

    private static void WriteJsonTypeOutput(ApiType type, ApiOptions options)
    {
        var outputType = type;
        var members = type.Members;

        if (options.MemberFilter.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter)).ToList();

        if (options.KindFilter.Count > 0)
            members = members.Where(m => options.KindFilter.Contains(m.Kind)).ToList();

        if (options.UnsafeOnly)
            members = members.Where(m => m.IsUnsafe).ToList();

        if (options.Limit.HasValue && members.Count > options.Limit.Value)
            members = members
                .OrderBy(m => ApiOutputFormatter.GetMemberSortOrder(m.Kind))
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .ThenBy(ApiOutputFormatter.GetMemberSignatureSortKey, StringComparer.Ordinal)
                .Take(options.Limit.Value)
                .ToList();

        // -S/--select scopes JSON to the requested sections, mirroring the markdown view.
        if (options.IncludeSections is { } sections
            && (sections.Count > 0
                || options is MemberOptions { MemberSectionsPreResolved: true }))
        {
            outputType = ProjectTypeToSections(type, members, sections);
        }

        else if (members != type.Members)
        {
            outputType = new ApiType
            {
                Namespace = type.Namespace,
                Name = type.Name,
                MetadataName = type.MetadataName,
                DefinitionName = type.DefinitionName,
                IntroducedTypeParameterCounts =
                    type.IntroducedTypeParameterCounts,
                Kind = type.Kind,
                Layout = type.Layout,
                LayoutDetails = type.LayoutDetails,
                MemorySafety = type.MemorySafety,
                IsSealed = type.IsSealed,
                IsAbstract = type.IsAbstract,
                IsStatic = type.IsStatic,
                BaseType = type.BaseType,
                Interfaces = type.Interfaces,
                Members = members,
                SourceFilePath = type.SourceFilePath,
                SourceUrl = type.SourceUrl,
                GitHubBrowseUrl = type.GitHubBrowseUrl,
                SourceLineNumber = type.SourceLineNumber,
                SourceChecksum = type.SourceChecksum,
                SourceChecksumAlgorithm = type.SourceChecksumAlgorithm,
                AdditionalSourceFiles = type.AdditionalSourceFiles,
                Documentation = type.Documentation
            };
        }

        // Project the durable identity (Digest + Canonical Signature) onto each member so
        // JSON consumers get the same overload handle the Markdown Digest column exposes.
        // Computed against the resolved declaring type, matching the table's anchor.
        foreach (var member in outputType.Members)
        {
            var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
            member.Digest = anchor.Fingerprint;
            member.CanonicalSignature = anchor.CanonicalSignature;
        }

        if (options.CompactJson)
            Console.WriteLine(JsonSerializer.Serialize(outputType, ApiTypeCompactJsonContext.Default.ApiType));
        else
            Console.WriteLine(JsonSerializer.Serialize(outputType, ApiTypeJsonContext.Default.ApiType));
    }

    private static bool IsAnnotatedSourceDocumentJson(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && options.IncludeSections is { Count: 1 } sections
           && sections.Contains(SectionNames.AnnotatedSourceDocument)
           && HasOnlyExplicitAnnotatedSourceDocumentSelectors(options);

    private static bool IsInvalidAnnotatedSourceDocumentJsonSelection(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && options.IncludeSections is { Count: > 0 } sections
           && sections.Contains(SectionNames.AnnotatedSourceDocument)
           && HasExplicitAnnotatedSourceDocumentSelector(options)
           && (sections.Count != 1
               || !HasOnlyExplicitAnnotatedSourceDocumentSelectors(options));

    private static bool HasOnlyExplicitAnnotatedSourceDocumentSelectors(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections is { Count: 1 } exactSections
              && exactSections.Contains(SectionNames.AnnotatedSourceDocument)
            : options.Select is { Length: > 0 } selectors
              && selectors.All(IsExplicitAnnotatedSourceDocumentSelector);

    private static bool HasExplicitAnnotatedSourceDocumentSelector(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections?.Contains(
                SectionNames.AnnotatedSourceDocument) == true
            : options.Select?.Any(IsExplicitAnnotatedSourceDocumentSelector) == true;

    private static bool IsExplicitAnnotatedSourceDocumentSelector(string selector)
        => selector.Equals(
            SectionNames.AnnotatedSourceDocument,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsFindingCensusJson(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && !IsColumnProjectionRequested(options)
           && options.Limit is null
           && !IsLineLimitRequested()
           && options.Rows is null
           && options.IncludeSections is { Count: 1 } sections
           && sections.Contains(SectionNames.FindingCensus)
           && HasOnlyExplicitFindingCensusSelectors(options);

    private static bool IsFactsJson(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && !IsColumnProjectionRequested(options)
           && options.Limit is null
           && !IsMemberLineWindowRequested(options)
           && options.Rows is null
           && options.IncludeSections is { Count: 1 } sections
           && sections.Contains(SectionNames.Facts)
           && HasOnlyExplicitFactsSelectors(options);

    private static bool IsProjectedFactsJson(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && IsColumnProjectionRequested(options)
           && options.IncludeSections is { Count: 1 } sections
           && sections.Contains(SectionNames.Facts)
           && HasOnlyExplicitFactsSelectors(options);

    private static bool IsCallersJson(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && options.IncludeSections is { Count: 1 } sections
           && sections.Contains(SectionNames.Callers)
           && HasOnlyExplicitCallersSelectors(options);

    private static bool IsCallsJson(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && options.IncludeSections is { Count: 1 } sections
           && sections.Contains(SectionNames.Calls)
           && HasOnlyExplicitCallsSelectors(options);

    private static bool IsCallGraphTransport(MemberOptions options) =>
        RequestsCompleteCallGraphTransport(options)
        && HasExactCallGraphDocumentSelection(options);

    internal static bool HasExactCallGraphDocumentSelection(
        MemberOptions options) =>
        (options.EnvelopeOutput
            || options.JsonOutput && !options.Count)
        && options.IncludeSections is { Count: 1 } sections
        && sections.Contains(SectionNames.CallGraph)
        && HasOnlyExplicitCallGraphSelectors(options);

    private static bool ValidateCallGraphTransport(MemberOptions options)
    {
        bool hasExplicitCallGraph = HasExplicitCallGraphSelector(options);
        if (!options.EnvelopeOutput
        && !(options.JsonOutput
            && !options.Count
            && !IsProjectionRequested(options)
            && !IsColumnProjectionRequested(options)
            && hasExplicitCallGraph))
    {
        return true;
    }

        if (options.IncludeSections is not { Count: 1 } sections
            || !sections.Contains(SectionNames.CallGraph)
            || !HasOnlyExplicitCallGraphSelectors(options))
        {
            CommandError.Write(
                options.EnvelopeOutput
                    ? "--envelope on member requires exactly -S \"Call Graph\"."
                    : "Call Graph --json requires exactly -S \"Call Graph\".");
            return false;
        }

        if (options.Count
            || options.Tabular
            || options.Tsv
            || options.Jsonl
            || options.Tree
            || options.MermaidOutput
            || options.EmbeddedMermaid
            || options.PlainText
            || IsProjectionRequested(options)
            || IsColumnProjectionRequested(options)
            || options.Limit is not null
            || IsMemberLineWindowRequested(options)
            || options.Rows is not null)
        {
            CommandError.Write(
                "Complete Call Graph JSON does not support row, line, field, column, count, or presentation projections.");
            return false;
        }

        return true;
    }

    private static bool RequestsCompleteCallGraphTransport(
        MemberOptions options) =>
        options.EnvelopeOutput
        || options.JsonOutput
        && !options.Count
        && !IsProjectionRequested(options)
        && !IsColumnProjectionRequested(options);

    private static bool HasOnlyExplicitCallGraphSelectors(
        MemberOptions options) =>
        options.MemberSectionsPreResolved
            ? options.ExactIncludeSections is { Count: 1 } exactSections
              && exactSections.Contains(SectionNames.CallGraph)
            : options.Select is { Length: > 0 } selectors
              && selectors.All(IsExplicitCallGraphSelector);

    private static bool HasExplicitCallGraphSelector(
        MemberOptions options) =>
        options.MemberSectionsPreResolved
            ? options.ExactIncludeSections?.Contains(
                SectionNames.CallGraph) == true
            : options.Select?.Any(IsExplicitCallGraphSelector) == true;

    private static bool IsExplicitCallGraphSelector(string selector) =>
        selector.Equals(
            SectionNames.CallGraph,
            StringComparison.OrdinalIgnoreCase);

    private static bool HasOnlyExplicitCallsSelectors(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections is { Count: 1 } exactSections
              && exactSections.Contains(SectionNames.Calls)
            : options.Select is { Length: > 0 } selectors
              && selectors.All(IsExplicitCallsSelector);

    private static bool IsExplicitCallsSelector(string selector)
        => selector.Equals(
            SectionNames.Calls,
            StringComparison.OrdinalIgnoreCase);

    private static bool HasOnlyExplicitCallersSelectors(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections is { Count: 1 } exactSections
              && exactSections.Contains(SectionNames.Callers)
            : options.Select is { Length: > 0 } selectors
              && selectors.All(IsExplicitCallersSelector);

    private static bool IsExplicitCallersSelector(string selector)
        => selector.Equals(
            SectionNames.Callers,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsInvalidFactsJsonSelection(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && !IsColumnProjectionRequested(options)
           && options.IncludeSections is { Count: > 0 } sections
           && sections.Contains(SectionNames.Facts)
           && HasExplicitFactsSelector(options)
           && (sections.Count != 1
               || !HasOnlyExplicitFactsSelectors(options));

    private static bool IsInvalidFactsJsonWindow(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && !IsColumnProjectionRequested(options)
           && options.IncludeSections?.Contains(SectionNames.Facts) == true
           && HasExplicitFactsSelector(options)
           && (options.Limit is not null
               || IsMemberLineWindowRequested(options)
               || options.Rows is not null);

    private static bool IsMemberLineWindowRequested(ApiOptions options)
        => IsLineLimitRequested()
           || options is MemberOptions
           {
               LineWindowExplicitlySet: true,
           };

    private static bool HasOnlyExplicitFactsSelectors(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections is { Count: 1 } exactSections
              && exactSections.Contains(SectionNames.Facts)
            : options.Select is { Length: > 0 } selectors
              && selectors.All(IsExplicitFactsSelector);

    private static bool HasExplicitFactsSelector(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections?.Contains(
                SectionNames.Facts) == true
            : options.Select?.Any(IsExplicitFactsSelector) == true;

    private static bool IsExplicitFactsSelector(string selector)
        => selector.Equals(
            SectionNames.Facts,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsInvalidFindingCensusJsonSelection(ApiOptions options)
        => options.JsonOutput
           && options.IncludeSections is { Count: > 0 } sections
           && sections.Contains(SectionNames.FindingCensus)
           && HasExplicitFindingCensusSelector(options)
           && (sections.Count != 1
               || !HasOnlyExplicitFindingCensusSelectors(options));

    private static bool IsInvalidFindingCensusProjection(ApiOptions options)
        => options.IncludeSections?.Contains(SectionNames.FindingCensus) == true
           && HasExplicitFindingCensusSelector(options)
           && (options.Count
               || options.Tabular
               || options.Tsv
               || options.Jsonl
               || IsProjectionRequested(options)
               || IsColumnProjectionRequested(options)
               || options.Limit is not null
               || IsLineLimitRequested()
               || options.Rows is not null);

    private static bool IsLineLimitRequested()
        => ArgumentPreprocessor.HeadLines is not null
           || ArgumentPreprocessor.TailLines is not null;

    private static bool HasOnlyExplicitFindingCensusSelectors(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections is { Count: 1 } exactSections
              && exactSections.Contains(SectionNames.FindingCensus)
            : options.Select is { Length: > 0 } selectors
              && selectors.All(IsExplicitFindingCensusSelector);

    private static bool HasExplicitFindingCensusSelector(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections?.Contains(SectionNames.FindingCensus) == true
            : options.Select?.Any(IsExplicitFindingCensusSelector) == true;

    private static bool IsExplicitFindingCensusSelector(string selector)
        => selector.Equals(
            SectionNames.FindingCensus,
            StringComparison.OrdinalIgnoreCase);

    private static bool HasExplicitPerformanceTriageSelector(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections?.Contains(
                SectionNames.PerformanceTriage) == true
            : options.Select?.Any(static selector =>
                   selector.Equals(
                       SectionNames.PerformanceTriage,
                       StringComparison.OrdinalIgnoreCase)
                   || selector.Equals(
                       "Optimization Opportunities",
                       StringComparison.OrdinalIgnoreCase)) == true;

    private static bool ShouldRenderMemberIndex(ApiOptions options)
        => options.IncludeSections?.Contains(SectionNames.MemberIndex) == true;

    private static bool ShouldRenderSourceLocations(ApiOptions options)
        => options.IncludeSections?.Contains(SectionNames.SourceLocations) == true;

    private static bool TryWriteMemorySafetyModeUnavailable(
        Decompiler.DecompilerResult? result)
    {
        if (result is null
            || !result.Diagnostics.Any(
                static diagnostic => diagnostic.Id
                    == Decompiler.DiagnosticIds.MemorySafetyModeUnavailable))
        {
            return false;
        }

        CommandError.Write(string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(
                static diagnostic => diagnostic.ToString())));
        return true;
    }

    internal static string AnnotatedSourceDocumentError(MemberCodeView? memberCode)
        => memberCode?.AnnotatedSourceDocumentFailure is { } failure
            ? string.Join(
                "; ",
                failure.Diagnostics.Select(diagnostic => diagnostic.ToString()))
            : $"section '{SectionNames.AnnotatedSourceDocument}' produced no payload.";

    internal static string FindingCensusError(MemberCodeView? memberCode)
        => memberCode?.FindingCensusFailure
            ?? $"section '{SectionNames.FindingCensus}' produced no payload.";

    private static readonly HashSet<string> SemanticFactSections = new(StringComparer.OrdinalIgnoreCase)
    {
        SectionNames.AllocationFacts,
        SectionNames.SafetyFacts,
        SectionNames.CostFacts
    };

    /// <summary>
    /// Maps each member section name to the predicate that selects its members.
    /// </summary>
    private static readonly Dictionary<string, Func<ApiMember, bool>> MemberSectionPredicates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SectionNames.Values] = m => m.Kind == "field" && m.EnumValue.HasValue,
            [SectionNames.Fields] = m => m.Kind == "field" && !m.EnumValue.HasValue,
            [SectionNames.Properties] = m => m.Kind == "property",
            [SectionNames.MethodGroups] = m => m.Kind == "method",
            [SectionNames.Methods] = m => m.Kind == "method",
            [SectionNames.Operators] = m => m.Kind == "operator",
            [SectionNames.ExplicitInterfaceImplementations] = m => m.Kind == "explicit-interface-implementation",
            [SectionNames.ExtensionMethods] = m => m.Kind == "extension-method",
            [SectionNames.Constructors] = m => m.Kind == "constructor",
            [SectionNames.Finalizer] = m => m.Kind == "finalizer",
            [SectionNames.Events] = m => m.Kind == "event",
            [SectionNames.SourceLocations] = ApiMemberSectionDescriptors.IsMethodLike,
        };

    /// <summary>
    /// Builds a copy of <paramref name="type"/> scoped to the requested sections: members are
    /// restricted to the selected member sections, and the Baseclass / Interfaces / Type
    /// Parameters facets are retained only when their section is selected. Identity fields
    /// (namespace, name, kind) are always preserved.
    /// </summary>
    private static ApiType ProjectTypeToSections(ApiType type, IEnumerable<ApiMember> members, HashSet<string> sections)
    {
        var predicates = MemberSectionPredicates
            .Where(kv => sections.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToList();

        var scopedMembers = predicates.Count > 0
            ? members.Where(m => predicates.Any(p => p(m))).ToList()
            : [];

        return new ApiType
        {
            Namespace = type.Namespace,
            Name = type.Name,
            MetadataName = type.MetadataName,
            DefinitionName = type.DefinitionName,
            IntroducedTypeParameterCounts =
                type.IntroducedTypeParameterCounts,
            Kind = type.Kind,
            Layout = type.Layout,
            LayoutDetails = type.LayoutDetails,
            MemorySafety = type.MemorySafety,
            IsSealed = type.IsSealed,
            IsAbstract = type.IsAbstract,
            IsStatic = type.IsStatic,
            BaseType = sections.Contains(SectionNames.Baseclass) && IsRenderableBaseType(type.BaseType) ? type.BaseType : null,
            Interfaces = sections.Contains(SectionNames.TypeInterfaces) ? type.Interfaces : [],
            TypeParameters = sections.Contains(SectionNames.TypeParameters) ? type.TypeParameters : [],
            Members = scopedMembers,
            SourceFilePath = type.SourceFilePath,
            SourceUrl = type.SourceUrl,
            GitHubBrowseUrl = type.GitHubBrowseUrl,
            SourceLineNumber = type.SourceLineNumber,
            SourceChecksum = type.SourceChecksum,
            SourceChecksumAlgorithm = type.SourceChecksumAlgorithm,
            AdditionalSourceFiles = type.AdditionalSourceFiles,
            Documentation = type.Documentation
        };
    }

    /// <summary>
    /// Mirrors the Baseclass section's CanRender: a base type is meaningful only when it is
    /// present and not one of the implicit roots (Object/ValueType/Enum).
    /// </summary>
    private static bool IsRenderableBaseType(string? baseType)
        => !string.IsNullOrEmpty(baseType)
           && baseType is not ("System.Object" or "System.ValueType" or "System.Enum");

}
