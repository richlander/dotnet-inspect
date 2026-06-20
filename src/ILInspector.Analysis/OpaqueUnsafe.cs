using System.Collections.Immutable;
using System.Linq;

namespace ILInspector.Analysis;

/// <summary>
/// A requires-unsafe method whose signature carries no pointer — the unsafety is
/// hidden from the caller's signature view. The only structural signal is the
/// attribute (<c>RequiresUnsafeAttribute</c> or the <c>unsafe</c> modifier), so a
/// caller reading the parameter and return types alone sees nothing unsafe.
/// </summary>
public sealed record OpaqueUnsafeMethod(
    MethodIdentity Method,
    CallerUnsafeMode Mode);

public static class OpaqueUnsafe
{
    /// <summary>
    /// Whether <paramref name="method"/> hides its requires-unsafe obligation from
    /// its signature: it requires unsafe (<see cref="CallerUnsafeMode"/> is not
    /// <see cref="CallerUnsafeMode.None"/>) yet no parameter or return type
    /// contains a pointer. The obligation therefore comes from the attribute or
    /// <c>unsafe</c> modifier, not a visible pointer.
    /// </summary>
    /// <remarks>
    /// This is a positive, sound structural claim — it never asserts the body is
    /// safe, only that the requires-unsafe contract is invisible in the signature.
    /// </remarks>
    public static bool IsOpaque(MethodIdentity method)
        => method.CallerUnsafeMode != CallerUnsafeMode.None
            && !method.ParameterTypes.Any(type => type.ContainsPointer())
            && !method.ReturnType.ContainsPointer();

    /// <summary>
    /// Every requires-unsafe method whose signature hides the obligation, ordered
    /// by metadata token. Pure over the index's array so it is testable without a
    /// real assembly.
    /// </summary>
    public static ImmutableArray<OpaqueUnsafeMethod> Collect(ImmutableArray<MethodIdentity> methods)
        => methods
            .Where(IsOpaque)
            .OrderBy(method => method.MetadataToken)
            .Select(method => new OpaqueUnsafeMethod(method, method.CallerUnsafeMode))
            .ToImmutableArray();
}
