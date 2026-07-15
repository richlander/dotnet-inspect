using System.Reflection;
using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

/// <summary>
/// Produces C#-flavored type <em>shapes</em> — the skeletal facts a consumer needs
/// to render a type declaration and its members without decompiling method bodies,
/// loading the inspected assembly, or using Roslyn. It is the C#-spelling companion
/// to the metadata-spelling producer (<c>System.Int32</c> vs <c>int</c>), and stays
/// SRM-only and NativeAOT-friendly so consumers that only want skeletons never take
/// the decompiler dependency. The decompiler layer coordinates with this producer to
/// fill selected members with full bodies.
///
/// These are its first capabilities; type/member/stubbed-shell shaping migrates here
/// as the shell composition currently trapped in the ReturnToSender harness folds in.
/// </summary>
public static class TypeShellProducer
{
    /// <summary>
    /// True when a C# type display name cannot be represented on a skeletal type
    /// surface (function pointers, compiler-generated <c>&lt;&gt;</c> names, or
    /// anonymous/tuple <c>{</c> shapes). Consumers drop such members.
    ///
    /// Callers must pass an already-normalized C# display name; this method is a pure
    /// substring heuristic and does not itself normalize.
    /// </summary>
    public static bool IsUnsupportedSurfaceSignature(string signature)
        => signature.Contains("delegate*", StringComparison.Ordinal)
            || signature.Contains("@delegate*", StringComparison.Ordinal)
            || signature.Contains("<>", StringComparison.Ordinal)
            || signature.Contains('{', StringComparison.Ordinal);

    /// <summary>
    /// The base type name a skeletal type shape should reconstruct for
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
        // No C# surface-representability gate here. That check belongs on the C#
        // display form (where `{`, `delegate*`, and generated `<>` are judged after
        // normalization), which is a caller concern: the harness applies it on the
        // Clean'd display name. Running the pure heuristic on the RAW metadata name
        // here would diverge from that normalized decision (e.g. wrongly dropping a
        // generated `<>`-named base). This method owns only the metadata-level gates.
        return baseType;
    }
}
