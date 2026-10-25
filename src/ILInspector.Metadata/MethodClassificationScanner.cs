using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Information about a method with a notable classification (unsafe, P/Invoke, etc.).
/// </summary>
public record ClassifiedMethodInfo(
    string MethodName,
    string DeclaringType,
    string? Namespace,
    string Signature,
    MethodClassification Classification,
    string? ModuleName = null)
{
    public MemberAnchor? Anchor { get; init; }
    public string? ReturnType { get; init; }

    // Preserve the original six-field record contract. Anchor and ReturnType are
    // derived structured data and intentionally do not participate in equality.
    public virtual bool Equals(ClassifiedMethodInfo? other)
        => ReferenceEquals(this, other)
        || other is not null
        && EqualityContract == other.EqualityContract
        && string.Equals(MethodName, other.MethodName, StringComparison.Ordinal)
        && string.Equals(DeclaringType, other.DeclaringType, StringComparison.Ordinal)
        && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
        && string.Equals(Signature, other.Signature, StringComparison.Ordinal)
        && Classification == other.Classification
        && string.Equals(ModuleName, other.ModuleName, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EqualityContract);
        hash.Add(MethodName, StringComparer.Ordinal);
        hash.Add(DeclaringType, StringComparer.Ordinal);
        hash.Add(Namespace, StringComparer.Ordinal);
        hash.Add(Signature, StringComparer.Ordinal);
        hash.Add(Classification);
        hash.Add(ModuleName, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Classification of a method based on its metadata characteristics.
/// </summary>
public enum MethodClassification
{
    Unsafe,
    PInvoke,

    /// <summary>
    /// Runtime async method (.NET 11+): MethodImplAttributes.Async (0x2000) flag set,
    /// suspension handled by the runtime with no compiler-generated state machine.
    /// </summary>
    RuntimeAsync,

    /// <summary>
    /// Classic compiler state-machine async method: carries
    /// AsyncStateMachineAttribute or AsyncIteratorStateMachineAttribute.
    /// </summary>
    StateMachineAsync,

    /// <summary>
    /// A public static extension method declared by a static extension type,
    /// neither hidden, as <c>ExtensionMethodScanner.FindAllExtensions</c>
    /// selects it without <c>includeAll</c>.
    /// </summary>
    Extension,
}

/// <summary>
/// Method classification helpers shared by metadata consumers.
/// </summary>
public static class MethodClassificationScanner
{
    /// <summary>
    /// Classifies a method as runtime async or classic state-machine async, or null
    /// if it is not an async method. Runtime async (.NET 11+) is identified by the
    /// MethodImplAttributes.Async (0x2000) flag; classic async by the compiler-emitted
    /// AsyncStateMachineAttribute / AsyncIteratorStateMachineAttribute.
    /// </summary>
    public static MethodClassification? ClassifyAsyncMethod(MetadataReader reader, MethodDefinition method)
    {
        const MethodImplAttributes AsyncImplFlag = (MethodImplAttributes)0x2000;
        if ((method.ImplAttributes & AsyncImplFlag) != 0)
            return MethodClassification.RuntimeAsync;

        var attributes = method.GetCustomAttributes();
        if (AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.AsyncStateMachineAttribute)
            || AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.AsyncIteratorStateMachineAttribute))
            return MethodClassification.StateMachineAsync;

        return null;
    }

    static readonly MetadataTypeNameTarget AsyncStateMachine =
        new(KnownAttributeNames.AsyncStateMachineAttribute);

    static readonly MetadataTypeNameTarget AsyncIteratorStateMachine =
        new(KnownAttributeNames.AsyncIteratorStateMachineAttribute);

    static readonly MetadataTypeNameTarget IteratorStateMachine =
        new(KnownAttributeNames.IteratorStateMachineAttribute);

    /// <summary>
    /// The state-machine kind a method's own attributes claim, matched in
    /// place as the classification analyzers match them, or null when the
    /// method claims none. It reads only the state-machine attributes, so a
    /// runtime-async method claims none and a caller that distinguishes
    /// runtime async tests the implementation flag first.
    /// <see cref="ClassifyAsyncMethod"/> keeps its materialized comparison as
    /// the legacy oracle's reference.
    /// </summary>
    public static StateMachineClaimKind? ClassifyStateMachine(MetadataReader reader, MethodDefinition method)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var attributes = method.GetCustomAttributes();
        if (MetadataTypeNameMatch.AnyAttributeHasType(reader, attributes, AsyncStateMachine))
            return StateMachineClaimKind.ClassicAsync;
        if (MetadataTypeNameMatch.AnyAttributeHasType(reader, attributes, AsyncIteratorStateMachine))
            return StateMachineClaimKind.AsyncIterator;
        if (MetadataTypeNameMatch.AnyAttributeHasType(reader, attributes, IteratorStateMachine))
            return StateMachineClaimKind.Iterator;
        return null;
    }
}
