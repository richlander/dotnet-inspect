using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

public enum HandlerKind { Catch, Filter, Finally, Fault }

/// <summary>An exception region, fully materialized (no metadata handles).</summary>
public sealed record HandlerRegion(
    HandlerKind Kind,
    int TryOffset,
    int TryLength,
    int HandlerOffset,
    int HandlerLength,
    int FilterOffset,
    TypeRef? CatchType);

/// <summary>
/// The IL range of one local slot's declaration scope, as recorded by a portable
/// PDB's <c>LocalScope</c> table. <see cref="EndOffset"/> is exclusive. This is the
/// evidence that separates a local the source declared at method scope from one it
/// declared inside a nested block; IL alone carries only a flat slot list.
/// </summary>
public readonly record struct LocalSlotScope(int StartOffset, int EndOffset)
{
    public int Length => EndOffset - StartOffset;

    /// <summary>
    /// True when the scope spans the whole method body, i.e. the source declared the
    /// local at method scope. Anything narrower means the source declared it inside a
    /// nested block.
    /// </summary>
    public bool CoversMethodBody(int ilLength) => StartOffset <= 0 && EndOffset >= ilLength;
}

/// <summary>
/// A method body as plain data: no metadata handles, no lifetime — safe to
/// hold after its <see cref="MetadataSource"/> is disposed.
/// </summary>
public sealed record MethodBody(
    ImmutableArray<byte> IL,
    int MaxStack,
    ImmutableArray<TypeRef> Locals,
    ImmutableArray<string?> LocalNames,
    ImmutableArray<HandlerRegion> Handlers,
    bool SkipLocalsInit = false)
{
    /// <summary>
    /// Per local slot, whether the portable PDB scoped the local to something
    /// narrower than the whole method body — that is, whether the source declared it
    /// inside a nested block. Empty when no PDB was available, and false for a slot
    /// with no scope entry (a compiler temp the source never declared). Length-aligned
    /// with <see cref="Locals"/> when non-empty.
    /// </summary>
    public ImmutableArray<bool> LocalDeclaredInNestedScope { get; init; } = [];
}

/// <summary>
/// A parameter: name, symbolic type, whether metadata declares an optional
/// default, and whether the top-level type was authored as <c>dynamic</c>
/// (a <c>System.Object</c> position carrying <c>[DynamicAttribute]</c>). The
/// dynamic view lets the printer drop a redundant <c>(dynamic)</c> cast on a
/// raised dynamic member access whose receiver is this parameter.
/// </summary>
public sealed record Parameter(string Name, TypeRef Type, bool HasDefault = false, bool IsDynamic = false);

/// <summary>A method signature with symbolic types throughout.</summary>
public sealed record MethodSignature(
    TypeRef ReturnType,
    ImmutableArray<Parameter> Parameters,
    bool HasThis,
    int GenericParameterCount)
{
    /// <summary>
    /// The method's own generic parameter names (e.g. <c>T</c>), when known; empty
    /// otherwise. A method type parameter shares the declaration space with the
    /// declaring type's members, so it shadows a same-named static member — an
    /// unqualified call to that member would bind to the type parameter (CS0119).
    /// </summary>
    public ImmutableArray<string> GenericParameterNames { get; init; } = [];
}

/// <summary>
/// The importer's output: declaring type, name, signature, and body — all
/// materialized, valid after the source is disposed.
/// </summary>
public sealed record ImportedMethod(
    TypeRef DeclaringType,
    string Name,
    MethodSignature Signature,
    MethodBody Body,
    MetadataFactState CompilerGenerated = MetadataFactState.Unknown,
    MetadataFactState DeclaringTypeCompilerGenerated = MetadataFactState.Unknown,
    MetadataFactState IsRuntimeAsync = MetadataFactState.Unknown,
    int MetadataToken = 0);
