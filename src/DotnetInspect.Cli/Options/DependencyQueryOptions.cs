using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Parsing;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;
using QuerySpace;
using QuerySpace.Operations;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using ILInspector.CSharp;

namespace DotnetInspect.Cli.Options;

internal static class DependencyQueryOptions
{
    internal const string TypeSummary =
        "Filter type dependency relationships with --where; order them with "
        + "--order-by; use --top N with a field order; bound traversal with "
        + "--depth. Traversal is a sequence order and cannot rank --top.";

    internal const string HierarchySummary =
        "Bound dependency traversal with --depth. Row selection is applied "
        + "after traversal and does not reduce the traversal work bound.";

    internal static ImmutableArray<SectionQueryKey> QueryKeys(
        DependencyQueryRouteKind kind)
    {
        IQueryOperationRoute route = DependencyQuery.Route(kind);
        var keys = ImmutableArray.CreateBuilder<SectionQueryKey>();
        foreach (QueryOperationTermCapability term in
                 route.Capabilities.Terms)
        {
            QueryOperationOrderCapability? order =
                route.Capabilities.Orders.SingleOrDefault(candidate =>
                    candidate.Binding.Kind
                        is QueryOperationOrderKind.Field
                    && candidate.Binding.Reference.Equals(
                        term.Binding.Key,
                        StringComparison.Ordinal));
            var operators = ImmutableArray.CreateBuilder<string>();
            operators.Add("--where");
            if (order is not null)
            {
                operators.Add("--order-by");
                if (route.Capabilities.Stages.Contains(
                        RowSelectionStageKind.Top)
                    && order.Roles.Contains(
                        QueryOperationOrderRole.Ranking))
                {
                    operators.Add("--top");
                }
            }

            keys.Add(
                new(
                    term.Binding.Key,
                    [.. operators],
                    [
                        .. term.Operators.Select(
                            RowPredicateSyntaxParser.Comparison),
                    ],
                    term.Binding.Description.ValueKind,
                    [.. term.Binding.Description.Values],
                    Example(term.Binding.Key)));
        }

        foreach (QueryOperationOrderCapability order in
                 route.Capabilities.Orders.Where(capability =>
                     capability.Binding.Kind
                        is QueryOperationOrderKind.Named))
        {
            keys.Add(
                new(
                    order.Binding.Reference,
                    ["--order-by"],
                    [],
                    "sequence order",
                    ["asc", "desc"],
                    $"--order-by \"{order.Binding.Reference} asc\""));
        }

        if (route.Capabilities.Dimensions.Contains(
                DependencyQuery.DepthDimension,
                StringComparer.Ordinal))
        {
            keys.Add(
                new(
                    "Depth",
                    ["--depth"],
                    [],
                    "positive traversal depth",
                    [],
                    "--depth 2"));
        }

        return [.. keys];
    }

    internal static bool TryResolve(
        DependencyQueryRouteKind kind,
        IReadOnlyList<string> expressions,
        string? orderBy,
        RowSelectionIntent<string>? rowSelection,
        int? depth,
        out DependencyQueryPlan plan,
        out OptionError error)
    {
        IQueryOperationRoute route = DependencyQuery.Route(kind);
        var terms = ImmutableArray.CreateBuilder<PortableQueryTerm>();
        foreach (string expression in expressions)
        {
            if (!RowPredicateSyntaxParser.TryParse(
                    expression,
                    out RowPredicateSyntax syntax,
                    out error))
            {
                plan = null!;
                return false;
            }

            QueryOperationTermCapability? capability =
                route.Capabilities.Terms.SingleOrDefault(candidate =>
                    candidate.Binding.Key.Equals(
                        RowPredicateSyntaxParser.NormalizeFieldName(
                            syntax.Field),
                        StringComparison.OrdinalIgnoreCase));
            if (capability is null)
            {
                plan = null!;
                string field =
                    CSharpIdentifier.ContainRenderedText(syntax.Field);
                error =
                    $"Field '{field}' is not queryable by this Dependency route.";
                return false;
            }

            PortableQueryOperator @operator =
                RowPredicateSyntaxParser.PortableOperator(
                    syntax.Operator);
            if (!capability.Operators.Contains(@operator))
            {
                plan = null!;
                error =
                    $"Field '{capability.Binding.Key}' does not support "
                    + $"'{RowPredicateSyntaxParser.Comparison(@operator)}' "
                    + "predicates.";
                return false;
            }

            terms.Add(
                new(
                    capability.Binding.Key,
                    @operator,
                    syntax.Value));
        }

        PortableQueryStage[] stages = rowSelection is null
            ? []
            : [.. rowSelection.Operations.Select(ToPortableStage)];
        var orders = ImmutableArray.CreateBuilder<
            PortableQueryOrderOperation>();
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            int topIndex = Array.FindIndex(
                stages,
                stage => stage.Kind is RowSelectionStageKind.Top);
            PortableQueryOrderRole role = topIndex >= 0
                ? PortableQueryOrderRole.ForStage(topIndex)
                : PortableQueryOrderRole.Baseline;
            if (!TryParseOrder(
                    route,
                    orderBy,
                    role,
                    out PortableQueryOrderOperation? order,
                    out error))
            {
                plan = null!;
                return false;
            }

            orders.Add(order);
        }

        PortableQueryBound[] bounds = depth is int maximumDepth
            ? [new(DependencyQuery.DepthDimension, maximumDepth)]
            : [];
        DependencyQueryPlanResult result =
            DependencyQuery.ResolveIntent(
                kind,
                PortableQueryIntent.Create(
                    [.. terms],
                    bounds,
                    stages,
                    [.. orders]));
        if (result is DependencyQueryPlanResult.Rejected rejected)
        {
            plan = null!;
            error = Error(rejected.Failure);
            return false;
        }

        plan = ((DependencyQueryPlanResult.Accepted)result).Plan;
        error = "";
        return true;
    }

    internal static RowSelectionIntent<string>? AppendLegacyRows(
        ParseResult parseResult,
        SharedOptions options,
        RowSelectionIntent<string>? selection,
        out int? legacyWindowStageIndex)
    {
        legacyWindowStageIndex = null;
        string? rows = parseResult.GetValue(options.Rows);
        if (rows is null)
            return selection;

        if (!RowSpec.TryParse(
                rows,
                out RowSpec spec,
                out string? error))
        {
            throw new RowWindowValidationException(
                $"--rows {error}");
        }

        RowSelectionIntentOperation<string> operation =
            spec.Kind switch
            {
                RowSpecKind.Count
                    when parseResult.GetValue(options.Tail) =>
                    RowSelectionIntentOperation<string>.Tail(
                        spec.Count),
                RowSpecKind.Count =>
                    RowSelectionIntentOperation<string>.Head(
                        spec.Count),
                RowSpecKind.Range =>
                    RowSelectionIntentOperation<string>.Window(
                        spec.Start,
                        spec.End),
                _ => throw new InvalidOperationException(
                    "Unsupported Dependency row selection."),
            };
        var operations =
            (selection ?? RowSelectionIntent<string>.Empty)
                .Operations
                .ToList();
        int rowsPosition =
            CliRowSelectionCommandRegistry.GetPreparedOptionPosition(
                parseResult,
                options.Rows)
            ?? int.MaxValue;
        int insertionIndex =
            CliRowSelectionCommandRegistry
                .GetPreparedSemanticOperationPositions(parseResult)
                .Count(position => position < rowsPosition);
        int operationIndex =
            Math.Min(insertionIndex, operations.Count);
        operations.Insert(
            operationIndex,
            operation);
        if (spec.Kind == RowSpecKind.Range)
            legacyWindowStageIndex = operationIndex;
        return RowSelectionIntent<string>.Create(operations);
    }

    private static bool TryParseOrder(
        IQueryOperationRoute route,
        string expression,
        PortableQueryOrderRole role,
        out PortableQueryOrderOperation order,
        out OptionError error)
    {
        string[] rawTerms = expression.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        if (rawTerms.Length == 0)
        {
            order = null!;
            error = "--order-by requires at least one field.";
            return false;
        }

        var fields = ImmutableArray.CreateBuilder<PortableQueryOrderTerm>();
        foreach (string rawTerm in rawTerms)
        {
            (string reference, PortableQueryDirection direction)
                = ParseOrderTerm(rawTerm, out error);
            if (!string.IsNullOrEmpty(error.Message))
            {
                order = null!;
                return false;
            }

            QueryOperationOrderCapability? capability =
                route.Capabilities.Orders.SingleOrDefault(candidate =>
                    candidate.Binding.Reference.Equals(
                        reference,
                        StringComparison.OrdinalIgnoreCase));
            if (capability is null)
            {
                order = null!;
                string field =
                    CSharpIdentifier.ContainRenderedText(reference);
                error =
                    $"Field or order '{field}' is not sortable by this "
                    + "Dependency route.";
                return false;
            }

            if (capability.Binding.Kind is QueryOperationOrderKind.Named)
            {
                if (rawTerms.Length != 1)
                {
                    order = null!;
                    error =
                        $"Named order '{capability.Binding.Reference}' "
                        + "must be used alone.";
                    return false;
                }

                order = PortableQueryOrderOperation.Named(
                    role,
                    capability.Binding.Reference,
                    direction);
                error = "";
                return true;
            }

            fields.Add(
                new(
                    capability.Binding.Reference,
                    direction));
        }

        order = PortableQueryOrderOperation.Fields(role, [.. fields]);
        error = "";
        return true;
    }

    private static (string Reference, PortableQueryDirection Direction)
        ParseOrderTerm(
        string raw,
        out OptionError error)
    {
        string value = raw.Trim();
        int lastSpace = value.LastIndexOf(' ');
        string reference = lastSpace < 0
            ? value
            : value[..lastSpace].Trim();
        string? directionText = lastSpace < 0
            ? null
            : value[(lastSpace + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(reference))
        {
            error = "--order-by requires a field before its direction.";
            return default;
        }

        if (string.IsNullOrEmpty(directionText)
            || directionText.Equals(
                "asc",
                StringComparison.OrdinalIgnoreCase)
            || directionText.Equals(
                "ascending",
                StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return (
                RowPredicateSyntaxParser.NormalizeFieldName(reference),
                PortableQueryDirection.Ascending);
        }

        if (directionText.Equals(
                "desc",
                StringComparison.OrdinalIgnoreCase)
            || directionText.Equals(
                "descending",
                StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return (
                RowPredicateSyntaxParser.NormalizeFieldName(reference),
                PortableQueryDirection.Descending);
        }

        error =
            $"Invalid --order-by direction "
            + $"'{CSharpIdentifier.ContainRenderedText(directionText)}'. "
            + "Valid directions: asc, desc.";
        return default;
    }

    private static PortableQueryStage ToPortableStage(
        RowSelectionIntentOperation<string> operation) =>
        operation.Kind switch
        {
            RowSelectionStageKind.Head =>
                PortableQueryStage.Head(operation.Count),
            RowSelectionStageKind.Tail =>
                PortableQueryStage.Tail(operation.Count),
            RowSelectionStageKind.Window =>
                PortableQueryStage.Window(
                    operation.Start,
                    operation.End),
            RowSelectionStageKind.Top =>
                PortableQueryStage.Top(operation.Count),
            _ => throw new InvalidOperationException(
                "Dependency Query received an unsupported row stage."),
        };

    private static string Example(string key) =>
        key switch
        {
            TypeDependencyVocabulary.SourceKey =>
                "--where \"Source=System.Collections.*\"",
            TypeDependencyVocabulary.TargetKey =>
                "--where \"Target=System.Text.Json.*\"",
            TypeDependencyVocabulary.KindKey =>
                "--where \"Kind=Interface\"",
            _ => $"--where \"{key}=...\"",
        };

    private static OptionError Error(PortableQueryFailure failure) =>
        failure.Reason switch
        {
            PortableQueryFailureReason.ValueRejected =>
                $"Dependency query value for '{failure.Offender}' is invalid.",
            PortableQueryFailureReason.OperatorNotAdmitted =>
                $"Dependency query field '{failure.Offender}' does not support that comparison.",
            PortableQueryFailureReason.UnknownKey =>
                $"Dependency query field '{failure.Offender}' is not registered.",
            PortableQueryFailureReason.MaximumOutsideRange =>
                $"Dependency work bound '{failure.Offender}' requires a positive maximum.",
            PortableQueryFailureReason.UnknownOrderReference
                or PortableQueryFailureReason.OrderReferenceNotOrderable =>
                $"Dependency order '{failure.Offender}' is not registered.",
            PortableQueryFailureReason.OrderNotARanking =>
                $"Dependency order '{failure.Offender}' cannot rank --top.",
            PortableQueryFailureReason.RankingMissing =>
                "--top requires --order-by with Source, Target, or Kind.",
            _ => "The Dependency query could not be resolved.",
        };
}
