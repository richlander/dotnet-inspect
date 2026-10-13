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
        foreach (var handle in parameter.GetCustomAttributes())
        {
            string? name = AttributeDecoder.GetAttributeTypeName(
                reader,
                reader.GetCustomAttribute(handle).Constructor,
                beforeMaterialize);
            if (name == IsReadOnlyAttribute)
                return ParameterByRefKind.In;
            if (name == RequiresLocationAttribute)
                return ParameterByRefKind.RefReadOnly;
        }

        var attributes = parameter.Attributes;
        return (attributes & ParameterAttributes.Out) != 0
            && (attributes & ParameterAttributes.In) == 0
                ? ParameterByRefKind.Out
                : ParameterByRefKind.Ref;
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
