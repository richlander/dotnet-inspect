using ILInspector.Analysis.Classification;
using ILInspector.Metadata;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

/// <summary>
/// The orders a classified row can be read in: exactly the orders today's
/// outputs use, declared beside the row type as QuerySpace named orders and
/// applied by <see cref="RowQueryExecutor"/>, which sorts stably. Every order
/// breaks ties by traversal order.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/method-classification-analyzers.md#queries-and-demand</c>.
/// The analyzers return rows in traversal order; the query applies an order.
/// </remarks>
public static class ClassifiedMethodRowOrders
{
    public const string Metadata = "Metadata";
    public const string Model = "Model";
    public const string AsyncModel = "AsyncModel";
    public const string Display = "Display";
    public const string AsyncDisplay = "AsyncDisplay";
    public const string PInvokeDisplay = "PInvokeDisplay";
    public const string Legacy = "Legacy";

    /// <summary>The model's async kind text, as <c>LibraryInspection</c> sorts it.</summary>
    public static string AsyncKind(MethodClassification classification) =>
        classification == MethodClassification.RuntimeAsync ? "runtime" : "state-machine";

    // Model order (LibraryMetadataService): declaring type, then method name,
    // with the default comparer; async rows first by kind, ordinally.
    static int CompareModel(ClassifiedMethodRow left, ClassifiedMethodRow right)
    {
        int result = Comparer<string>.Default.Compare(left.DeclaringType.ToString(), right.DeclaringType.ToString());
        return result != 0
            ? result
            : Comparer<string>.Default.Compare(left.MethodName.ToString(), right.MethodName.ToString());
    }

    static int CompareAsyncModel(ClassifiedMethodRow left, ClassifiedMethodRow right)
    {
        int result = string.CompareOrdinal(AsyncKind(left.Classification), AsyncKind(right.Classification));
        return result != 0 ? result : CompareModel(left, right);
    }

    // Display order (LibraryInspectionView): a stable re-sort of the model
    // order, so model order breaks its ties.
    static int CompareDisplay(ClassifiedMethodRow left, ClassifiedMethodRow right, bool withModule, Comparison<ClassifiedMethodRow> model)
    {
        StringComparer ignoreCase = StringComparer.OrdinalIgnoreCase;
        int result = ignoreCase.Compare(left.DeclaringType.ToString(), right.DeclaringType.ToString());
        if (result == 0)
            result = ignoreCase.Compare(left.MethodName.ToString(), right.MethodName.ToString());
        if (result == 0 && withModule)
            result = ignoreCase.Compare(left.ModuleName?.ToString(), right.ModuleName?.ToString());
        if (result == 0)
            result = ignoreCase.Compare(left.Signature.ToString(), right.Signature.ToString());
        return result != 0 ? result : model(left, right);
    }

    // Legacy order (MethodClassificationScanner.Scan): traversal order, then,
    // within one method, P/Invoke, async, then pointer signature.
    static int CompareLegacy(ClassifiedMethodRow left, ClassifiedMethodRow right)
    {
        int result = left.Ordinal.CompareTo(right.Ordinal);
        return result != 0 ? result : LegacyRank(left.Classification).CompareTo(LegacyRank(right.Classification));
    }

    static int LegacyRank(MethodClassification classification) => classification switch
    {
        MethodClassification.PInvoke => 0,
        MethodClassification.Unsafe => 2,
        _ => 1,
    };

    static RowQueryNamedOrder<ClassifiedMethodRow> Order(string key, Comparison<ClassifiedMethodRow> ascending) =>
        new(
            RowQueryNamedOrderIdentity.Create(),
            key,
            RowQueryOrderPurpose.Sequence,
            direction =>
            {
                Comparison<ClassifiedMethodRow> directed = direction is RowQueryOrderDirection.Ascending
                    ? ascending
                    : (left, right) => ascending(right, left);
                return Comparer<ClassifiedMethodRow>.Create(
                    (left, right) =>
                    {
                        int result = directed(left, right);
                        return result != 0 ? result : left.Ordinal.CompareTo(right.Ordinal);
                    });
            });

    public static RowQueryVocabulary<ClassifiedMethodRow> Vocabulary { get; } =
        RowQueryVocabulary<ClassifiedMethodRow>.Create(
            RowQueryVocabularyIdentity.Create(),
            [],
            [
                Order(Metadata, static (left, right) => left.Ordinal.CompareTo(right.Ordinal)),
                Order(Model, CompareModel),
                Order(AsyncModel, CompareAsyncModel),
                Order(Display, static (left, right) => CompareDisplay(left, right, withModule: false, CompareModel)),
                Order(AsyncDisplay, static (left, right) => CompareDisplay(left, right, withModule: false, CompareAsyncModel)),
                Order(PInvokeDisplay, static (left, right) => CompareDisplay(left, right, withModule: true, CompareModel)),
                Order(Legacy, CompareLegacy),
            ]);

    /// <summary>Reads <paramref name="rows"/> in the named order through the QuerySpace executor.</summary>
    public static IReadOnlyList<ClassifiedMethodRow> Apply(
        IReadOnlyList<ClassifiedMethodRow> rows,
        string orderKey)
    {
        RowQueryResolutionResult<ClassifiedMethodRow> resolution = RowQueryResolver.Resolve(
            Vocabulary,
            RowQueryIntent.Create(
                [],
                RowQueryOrderIntent.Named(orderKey, RowQueryOrderDirection.Ascending),
                RowSelectionIntent<RowQueryOrderIntent>.Empty));
        ResolvedRowQueryPlan<ClassifiedMethodRow> plan = resolution.Plan
            ?? throw new InvalidOperationException($"The classified row order '{orderKey}' did not resolve.");
        return RowQueryExecutor.Apply(rows, plan).Values;
    }
}
