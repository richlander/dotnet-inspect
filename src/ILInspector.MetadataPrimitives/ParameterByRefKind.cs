using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>The C# passing mode of a parameter whose signature type is by-reference.</summary>
public enum ParameterByRefKind
{
    /// <summary><c>ref</c>: a mutable reference, including interop <c>[In] ref</c> and <c>[In, Out] ref</c>.</summary>
    Ref,

    /// <summary><c>out</c>: the <c>Out</c> flag without <c>In</c>.</summary>
    Out,

    /// <summary><c>in</c>: marked with <c>IsReadOnlyAttribute</c>.</summary>
    In,

    /// <summary><c>ref readonly</c>: marked with <c>RequiresLocationAttribute</c>.</summary>
    RefReadOnly,
}

/// <summary>
/// The one classification of a by-reference parameter's C# passing mode from metadata.
/// The raw <c>In</c> and <c>Out</c> parameter flags are interop marshalling directions,
/// not C# modifiers: <c>[In] ref</c> and <c>[In, Out] ref</c> are mutable <c>ref</c>
/// parameters. C# <c>in</c> and <c>ref readonly</c> are identified only by their
/// compiler attributes (both compilers also set the <c>In</c> flag).
/// </summary>
/// <remarks>
/// Precedence follows Roslyn's metadata reader: <c>Out</c> without <c>In</c> is
/// <c>out</c> before either attribute is consulted, then <c>IsReadOnlyAttribute</c>
/// is <c>in</c>, then <c>RequiresLocationAttribute</c> is <c>ref readonly</c>, and
/// everything else is <c>ref</c>.
/// </remarks>
public static class ParameterByRefKinds
{
    const string IsReadOnlyAttribute = "System.Runtime.CompilerServices.IsReadOnlyAttribute";
    const string RequiresLocationAttribute = "System.Runtime.CompilerServices.RequiresLocationAttribute";

    /// <summary>
    /// Classifies <paramref name="parameter"/>, whose signature type the caller has
    /// already established is by-reference.
    /// </summary>
    public static ParameterByRefKind Classify(
        MetadataReader reader,
        Parameter parameter,
        Action<int>? beforeMaterialize = null)
    {
        var flags = parameter.Attributes & (ParameterAttributes.In | ParameterAttributes.Out);
        if (flags == ParameterAttributes.Out)
            return ParameterByRefKind.Out;

        bool requiresLocation = false;
        foreach (var handle in parameter.GetCustomAttributes())
        {
            string? name = AttributeDecoder.GetAttributeTypeName(
                reader,
                reader.GetCustomAttribute(handle).Constructor,
                beforeMaterialize);
            if (name == IsReadOnlyAttribute)
                return ParameterByRefKind.In;
            requiresLocation |= name == RequiresLocationAttribute;
        }

        return requiresLocation ? ParameterByRefKind.RefReadOnly : ParameterByRefKind.Ref;
    }

    /// <summary>The C# declaration modifier for <paramref name="kind"/>.</summary>
    public static string CSharpModifier(this ParameterByRefKind kind)
        => kind switch
        {
            ParameterByRefKind.Out => "out",
            ParameterByRefKind.In => "in",
            ParameterByRefKind.RefReadOnly => "ref readonly",
            _ => "ref",
        };
}
