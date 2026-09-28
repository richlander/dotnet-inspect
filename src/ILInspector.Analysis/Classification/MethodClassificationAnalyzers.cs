using System.Reflection;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

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
/// The gate classification for method classification: public, non-accessor
/// methods on types whose name does not start with <c>&lt;</c>, as legacy
/// scopes them. An in-scope row is <see cref="PInvoke"/> when it carries
/// <see cref="MethodAttributes.PinvokeImpl"/>, and <see cref="Other"/> otherwise.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/method-classification-analyzers.md#the-analyzers</c>.
/// </remarks>
public sealed class MethodClassificationScope : MethodRowClassifier<MethodClassificationScope.Classification>
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

    /// <summary>The scope's tests, as a struct a kernel specializes to.</summary>
    public readonly struct Classification : IMethodRowClassification
    {
        public bool TypeInScope(scoped MethodRowTypeView type) =>
            !type.NameStartsWith("<");

        public int Classify(scoped MethodDefinitionView row)
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
    : MethodDefinitionQueryProducer<MethodClassificationScope.Classification, PInvokeTest, PInvokeRowProjection, ClassifiedMethodRow>
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

    internal override MethodRowClassifier<MethodClassificationScope.Classification> GateClassifier =>
        MethodClassificationScope.Instance;

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
    : MethodDefinitionQueryProducer<MethodClassificationScope.Classification, PointerSignatureTest, PointerSignatureRowProjection, ClassifiedMethodRow>
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

    internal override MethodRowClassifier<MethodClassificationScope.Classification> GateClassifier =>
        MethodClassificationScope.Instance;

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
    : MethodDefinitionQueryProducer<MethodClassificationScope.Classification, AsyncTest, AsyncRowProjection, ClassifiedMethodRow>
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

    internal override MethodRowClassifier<MethodClassificationScope.Classification> GateClassifier =>
        MethodClassificationScope.Instance;

    internal override SourceGateGuard? SourceGate { get; } =
        new(MethodClassificationScope.Instance, 1UL << MethodClassificationScope.Other);
}
