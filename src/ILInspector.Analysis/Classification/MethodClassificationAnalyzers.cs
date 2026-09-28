using System.Reflection;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;
using QuerySpace.Rows;

namespace ILInspector.Analysis.Classification;

/// <summary>
/// One classified method row: the fields <c>MethodClassificationScanner.Scan</c>
/// published, read through the method-row gate's identity decoder.
/// </summary>
public sealed record ClassifiedMethodRow(
    int Token,
    int Ordinal,
    InertString MethodName,
    InertString DeclaringType,
    InertString Namespace,
    InertString Signature,
    MethodClassification Classification,
    InertString? ModuleName,
    MethodRowAnchor? Anchor,
    InertString? ReturnType);

/// <summary>
/// The orders a classified row can be read in: exactly the orders today's
/// outputs use, declared beside the row type as QuerySpace named orders and
/// applied by <see cref="RowQueryExecutor"/>, which sorts stably. Every order
/// breaks ties by traversal order.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/method-classification-analyzers.md#queries-and-demand</c>.
/// </remarks>
public static class ClassifiedMethodRowOrders
{
    public const string Metadata = "Metadata";
    public const string Model = "Model";
    public const string AsyncModel = "AsyncModel";
    public const string Display = "Display";
    public const string AsyncDisplay = "AsyncDisplay";
    public const string PInvokeDisplay = "PInvokeDisplay";

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

/// <summary>
/// The gate classification for method classification: public, non-accessor
/// methods on types whose name does not start with <c>&lt;</c>, as legacy
/// scopes them. An in-scope row is <see cref="PInvoke"/> when it carries
/// <see cref="MethodAttributes.PinvokeImpl"/>, and <see cref="Other"/> otherwise.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/method-classification-analyzers.md#the-analyzers</c>.
/// </remarks>
public sealed class MethodClassificationScope : MethodRowClassifier
{
    public const int PInvoke = 0;
    public const int Other = 1;

    MethodClassificationScope()
        : base(
            "MethodClassification.Scope",
            MethodDefinitionLayers.Flags | MethodDefinitionLayers.NameComparison)
    {
    }

    public static MethodClassificationScope Instance { get; } = new();

    internal override bool TypeInScope(scoped MethodRowTypeView type) =>
        !type.NameStartsWith("<");

    internal override int Classify(scoped MethodDefinitionView row)
    {
        MethodAttributes attributes = row.Attributes;
        if ((attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            return -1;
        if (row.NameStartsWith("get_")
            || row.NameStartsWith("set_")
            || row.NameStartsWith("add_")
            || row.NameStartsWith("remove_"))
        {
            return -1;
        }

        return (attributes & MethodAttributes.PinvokeImpl) != 0 ? PInvoke : Other;
    }
}

/// <summary>Projects a classified row through the gate's identity decoder.</summary>
static class ClassifiedRows
{
    internal static ClassifiedMethodRow Project(
        scoped MethodDefinitionView view,
        MethodClassification classification,
        bool withModule)
    {
        MethodRowIdentity identity = view.Identity;
        return new ClassifiedMethodRow(
            view.Token,
            view.Ordinal,
            identity.MethodName,
            identity.DeclaringType,
            identity.Namespace,
            identity.Signature,
            classification,
            withModule ? view.PInvokeModuleName : null,
            identity.Anchor,
            identity.ReturnType);
    }
}

// ---- P/Invoke ----

/// <summary>The gate's <c>PInvoke</c> class is the whole test.</summary>
public struct PInvokeTest : IMethodDefinitionPredicate
{
    public readonly bool Test(scoped MethodDefinitionView view) => true;
}

public struct PInvokeRowProjection : IMethodDefinitionProjection<ClassifiedMethodRow>
{
    public readonly ClassifiedMethodRow Project(scoped MethodDefinitionView view) =>
        ClassifiedRows.Project(view, MethodClassification.PInvoke, withModule: true);
}

/// <summary>The P/Invoke analyzer: rows the gate classifies <c>PInvoke</c>.</summary>
public sealed class PInvokeAnalyzer
    : MethodDefinitionQueryProducer<PInvokeTest, PInvokeRowProjection, ClassifiedMethodRow>
{
    PInvokeAnalyzer()
        : base(
            "MethodClassification.PInvoke",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Flags,
            MethodDefinitionLayers.IdentityText)
    {
    }

    public static PInvokeAnalyzer Instance { get; } = new();

    internal override SourceGateGuard? SourceGate { get; } =
        new(MethodClassificationScope.Instance, 1UL << MethodClassificationScope.PInvoke);
}

// ---- Pointer signature ----

/// <summary>A pointer in the return type or a parameter type.</summary>
public struct PointerSignatureTest : IMethodDefinitionPredicate
{
    public readonly bool Test(scoped MethodDefinitionView view) => view.SignatureHasPointer;
}

public struct PointerSignatureRowProjection : IMethodDefinitionProjection<ClassifiedMethodRow>
{
    public readonly ClassifiedMethodRow Project(scoped MethodDefinitionView view) =>
        ClassifiedRows.Project(view, MethodClassification.Unsafe, withModule: false);
}

/// <summary>The pointer-signature analyzer: <c>Other</c> rows whose signature has a pointer.</summary>
public sealed class PointerSignatureAnalyzer
    : MethodDefinitionQueryProducer<PointerSignatureTest, PointerSignatureRowProjection, ClassifiedMethodRow>
{
    PointerSignatureAnalyzer()
        : base(
            "MethodClassification.PointerSignature",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.SignatureShape,
            MethodDefinitionLayers.IdentityText)
    {
    }

    public static PointerSignatureAnalyzer Instance { get; } = new();

    internal override SourceGateGuard? SourceGate { get; } =
        new(MethodClassificationScope.Instance, 1UL << MethodClassificationScope.Other);
}

// ---- Async ----

/// <summary>
/// Runtime async (the <c>MethodImplAttributes.Async</c> flag), or classic
/// async: a <c>StateMachineRelationshipIndex</c> kickoff relationship resolved
/// as <c>ClassicAsync</c> or <c>AsyncIterator</c>. An iterator claim or no
/// relationship is not async. A rejected relationship fails the analyzer at
/// that method.
/// </summary>
public struct AsyncTest : IMethodDefinitionPredicate
{
    public readonly bool Test(scoped MethodDefinitionView view) =>
        AsyncClassification.Classify(view) is not null;
}

static class AsyncClassification
{
    internal const MethodImplAttributes RuntimeAsyncFlag = (MethodImplAttributes)0x2000;

    internal static MethodClassification? Classify(scoped MethodDefinitionView view)
    {
        if ((view.ImplAttributes & RuntimeAsyncFlag) != 0)
            return MethodClassification.RuntimeAsync;

        return view.StateMachineByKickoff switch
        {
            StateMachineRelationshipResult.Resolved
            {
                Relationship.Kind: StateMachineClaimKind.ClassicAsync or StateMachineClaimKind.AsyncIterator,
            } => MethodClassification.StateMachineAsync,
            StateMachineRelationshipResult.Rejected rejected => throw new BadImageFormatException(
                $"The method's state-machine relationship was rejected ({rejected.Failure.Kind}): "
                + rejected.Failure.Detail),
            _ => null,
        };
    }
}

public struct AsyncRowProjection : IMethodDefinitionProjection<ClassifiedMethodRow>
{
    public readonly ClassifiedMethodRow Project(scoped MethodDefinitionView view) =>
        ClassifiedRows.Project(
            view,
            AsyncClassification.Classify(view)!.Value,
            withModule: false);
}

/// <summary>The async analyzer: <c>Other</c> rows that are runtime or classic async.</summary>
public sealed class AsyncAnalyzer
    : MethodDefinitionQueryProducer<AsyncTest, AsyncRowProjection, ClassifiedMethodRow>
{
    AsyncAnalyzer()
        : base(
            "MethodClassification.Async",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Flags | MethodDefinitionLayers.StateMachineRelationship,
            MethodDefinitionLayers.IdentityText)
    {
    }

    public static AsyncAnalyzer Instance { get; } = new();

    internal override SourceGateGuard? SourceGate { get; } =
        new(MethodClassificationScope.Instance, 1UL << MethodClassificationScope.Other);
}
