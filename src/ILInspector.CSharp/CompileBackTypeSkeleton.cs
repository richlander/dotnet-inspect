using System.Reflection;
using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

/// <summary>
/// Product-owned composition of compile-back type skeletons: the minimal,
/// compilable type-shell facts a consumer needs to reconstruct a member's
/// referenced types without loading the inspected assembly or using Roslyn.
///
/// This is the first seam extracted from the ReturnToSender harness so the
/// knowledge of how to shape a compile-back shell lives in the product (and is
/// validated by the harness compile-back oracle) rather than being re-derived by
/// every consumer. It stays SRM-only and NativeAOT-friendly.
/// </summary>
public static class CompileBackTypeSkeleton
{
    /// <summary>
    /// True when a C# type display name cannot be represented on a compile-back
    /// shell surface (function pointers, compiler-generated <c>&lt;&gt;</c> names, or
    /// anonymous/tuple <c>{</c> shapes). Consumers drop such members.
    ///
    /// Callers must pass an already-normalized C# display name (e.g. the harness
    /// applies its <c>CompileBackTypeSignature.Display</c>/<c>Clean</c> pass first);
    /// this method is a pure substring heuristic and does not itself normalize.
    /// </summary>
    public static bool IsUnsupportedSurfaceSignature(string signature)
        => signature.Contains("delegate*", StringComparison.Ordinal)
            || signature.Contains("@delegate*", StringComparison.Ordinal)
            || signature.Contains("<>", StringComparison.Ordinal)
            || signature.Contains('{', StringComparison.Ordinal);

    /// <summary>
    /// The base type name a compile-back shell should reconstruct for
    /// <paramref name="typeDef"/>, or <see langword="null"/> when the base should
    /// be dropped (left to its compiler-implied default).
    ///
    /// Attributes always keep their <c>System.Attribute</c> base. Otherwise only a
    /// same-assembly (<see cref="HandleKind.TypeDefinition"/>) non-generic plain
    /// class base is reconstructed: external (TypeReference) and generic
    /// instantiation (TypeSpecification) bases are dropped because the shell cannot
    /// own their construction, and object-family / value-type / delegate bases are
    /// left implicit to avoid conflicts. <paramref name="isClass"/> is the caller's
    /// resolved kind (records/structs/enums/delegates pass <see langword="false"/>).
    /// </summary>
    public static string? ReconstructedBaseTypeName(MetadataReader reader, TypeDefinition typeDef, bool isClass)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return null;
        if (typeDef.BaseType.IsNil)
            return null;

        string? baseType;
        try
        {
            baseType = TypeResolver.GetTypeName(reader, typeDef.BaseType, GenericContext.ForType(reader, typeDef));
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }

        if (baseType is null)
            return null;
        // Attributes must derive from System.Attribute (existing behavior).
        if (baseType is "System.Attribute")
            return baseType;
        // Only reconstruct same-assembly base classes. External (TypeReference) and
        // generic-instantiation (TypeSpecification) bases are dropped: the shell
        // cannot own their construction, so an external base whose only constructor
        // is parameterized would make the derived stub's implicit `: base()` fail
        // (CS7036) where the baseline compiled without a base.
        if (typeDef.BaseType.Kind != HandleKind.TypeDefinition)
            return null;
        // Only plain classes reconstruct a real base class; records/structs/enums/
        // delegates keep their compiler-implied base to avoid primary-constructor
        // and value-type conflicts. A generic type's base can reference its own type
        // parameters, which the flat shell does not carry, so skip it.
        if (!isClass || typeDef.GetGenericParameters().Count != 0)
            return null;
        if (baseType is "System.Object" or "System.ValueType" or "System.Enum"
            or "System.Delegate" or "System.MulticastDelegate")
            return null;
        // No surface-representability gate here. origin/main applied it to the C#
        // display form (CompileBackCSharpNames.Clean) of the base, and Clean strips
        // the compiler-generated `<>` segments and modreq/modopt that the heuristic
        // looks for, so for a same-assembly TypeDefinition name the check could never
        // fire. Running the pure heuristic on the RAW metadata name instead would
        // wrongly drop a generated `<>`-named base that origin/main kept, so the gate
        // is omitted to stay byte-identical. The downstream C# printer sanitizes the
        // emitted base name.
        return baseType;
    }
}
