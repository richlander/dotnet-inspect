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

static class AsyncClassification
{
    internal const MethodImplAttributes RuntimeAsyncFlag = (MethodImplAttributes)0x2000;

    internal static readonly MetadataTypeNameTarget AsyncStateMachine =
        new(KnownAttributeNames.AsyncStateMachineAttribute);

    internal static readonly MetadataTypeNameTarget AsyncIteratorStateMachine =
        new(KnownAttributeNames.AsyncIteratorStateMachineAttribute);

    internal static bool IsRuntimeAsync(scoped MethodDefinitionView view) =>
        (view.ImplAttributes & RuntimeAsyncFlag) != 0;
}

/// <summary>Runtime async: the <c>MethodImplAttributes.Async</c> (0x2000) flag.</summary>
public struct RuntimeAsyncTest : IMethodDefinitionPredicate
{
    public readonly bool Test(scoped MethodDefinitionView view) =>
        AsyncClassification.IsRuntimeAsync(view);
}

public struct RuntimeAsyncRowProjection : IMethodDefinitionProjection<ClassifiedMethodRow>
{
    public readonly ClassifiedMethodRow Project(scoped MethodDefinitionView view) =>
        ClassifiedRows.Project(view, MethodClassification.RuntimeAsync, withModule: false);
}

/// <summary>The runtime-async analyzer: <c>Other</c> rows carrying the runtime async flag.</summary>
public sealed class RuntimeAsyncAnalyzer
    : MethodDefinitionQueryProducer<RuntimeAsyncTest, RuntimeAsyncRowProjection, ClassifiedMethodRow>
{
    RuntimeAsyncAnalyzer()
        : base(
            "MethodClassification.RuntimeAsync",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Flags,
            MethodDefinitionLayers.IdentityText)
    {
    }

    public static RuntimeAsyncAnalyzer Instance { get; } = new();

    internal override SourceGateGuard? SourceGate { get; } =
        new(MethodClassificationScope.Instance, 1UL << MethodClassificationScope.Other);
}

/// <summary>
/// Compiler async: no runtime async flag, and an
/// <c>AsyncStateMachineAttribute</c> or <c>AsyncIteratorStateMachineAttribute</c>
/// matched by name in place, as the Roslyn compiler emits them. It excludes
/// runtime async, so the two analyzers' rows are disjoint.
/// </summary>
public struct CompilerAsyncTest : IMethodDefinitionPredicate
{
    public readonly bool Test(scoped MethodDefinitionView view) =>
        !AsyncClassification.IsRuntimeAsync(view)
        && (view.HasAttributeOfType(AsyncClassification.AsyncStateMachine)
            || view.HasAttributeOfType(AsyncClassification.AsyncIteratorStateMachine));
}

public struct CompilerAsyncRowProjection : IMethodDefinitionProjection<ClassifiedMethodRow>
{
    public readonly ClassifiedMethodRow Project(scoped MethodDefinitionView view) =>
        ClassifiedRows.Project(view, MethodClassification.StateMachineAsync, withModule: false);
}

/// <summary>The compiler-async analyzer: <c>Other</c> rows with a compiler async state-machine attribute.</summary>
public sealed class CompilerAsyncAnalyzer
    : MethodDefinitionQueryProducer<CompilerAsyncTest, CompilerAsyncRowProjection, ClassifiedMethodRow>
{
    CompilerAsyncAnalyzer()
        : base(
            "MethodClassification.CompilerAsync",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Flags | MethodDefinitionLayers.AttributeTypeMatch,
            MethodDefinitionLayers.IdentityText)
    {
    }

    public static CompilerAsyncAnalyzer Instance { get; } = new();

    internal override SourceGateGuard? SourceGate { get; } =
        new(MethodClassificationScope.Instance, 1UL << MethodClassificationScope.Other);
}

/// <summary>
/// Async, one pass: each row tests the runtime flag first, then the compiler
/// attribute, through the runtime-async and compiler-async analyzers' own
/// tests. Its rows are their union, which is disjoint.
/// </summary>
public struct AsyncTest : IMethodDefinitionPredicate
{
    public readonly bool Test(scoped MethodDefinitionView view) =>
        default(RuntimeAsyncTest).Test(view) || default(CompilerAsyncTest).Test(view);
}

/// <summary>Carries the kind of async the test found, re-read from the runtime flag.</summary>
public struct AsyncRowProjection : IMethodDefinitionProjection<ClassifiedMethodRow>
{
    public readonly ClassifiedMethodRow Project(scoped MethodDefinitionView view) =>
        default(RuntimeAsyncTest).Test(view)
            ? default(RuntimeAsyncRowProjection).Project(view)
            : default(CompilerAsyncRowProjection).Project(view);
}

/// <summary>
/// The async analyzer: <c>Other</c> rows that are runtime or compiler async,
/// in one pass. It declares the union of the two analyzers' fields.
/// </summary>
public sealed class AsyncAnalyzer
    : MethodDefinitionQueryProducer<AsyncTest, AsyncRowProjection, ClassifiedMethodRow>
{
    AsyncAnalyzer()
        : base(
            "MethodClassification.Async",
            version: 2,
            tier: 0,
            MethodDefinitionLayers.Flags | MethodDefinitionLayers.AttributeTypeMatch,
            MethodDefinitionLayers.IdentityText)
    {
    }

    public static AsyncAnalyzer Instance { get; } = new();

    internal override SourceGateGuard? SourceGate { get; } =
        new(MethodClassificationScope.Instance, 1UL << MethodClassificationScope.Other);
}
