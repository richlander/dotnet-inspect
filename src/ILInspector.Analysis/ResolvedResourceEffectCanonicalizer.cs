using System.Globalization;
using System.Text;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal static class ResolvedResourceEffectCanonicalizer
{
    internal static string Type(ResolvedResourceEffectType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var value = new StringBuilder();
        AppendType(value, type);
        return value.ToString();
    }

    static void AppendType(
        StringBuilder value,
        ResolvedResourceEffectType type)
    {
        Append(value, (int)type.Type.Kind);
        Append(value, type.Type.RawTypeKind);
        Append(value, type.Type.Rank);
        Append(value, type.Type.ArraySizes.Length);
        foreach (int size in type.Type.ArraySizes)
            Append(value, size);
        Append(value, type.Type.ArrayLowerBounds.Length);
        foreach (int lowerBound in type.Type.ArrayLowerBounds)
            Append(value, lowerBound);
        if (type.Definition is { } definition)
        {
            Append(value, "definition");
            Append(value, MetadataReceiptEvidence.For(definition));
            Append(value, (int)definition.Kind);
        }
        else
        {
            Append(value, "exact-signature");
            AppendTypeRef(value, type.Type);
            if (type.DefiningAssembly is { } assembly)
                AppendAssembly(value, assembly);
            else
                Append(value, "no-defining-assembly");
        }
        if (type.GenericScope is { } scope)
        {
            Append(value, (int)scope.Kind);
            Append(value, scope.Owner.SourceReceiptEvidence);
            Append(value, scope.Owner.ModuleVersionId);
            Append(value, scope.Owner.MethodToken);
        }
        else
        {
            Append(value, "no-generic-scope");
        }
        if (type.Element is null)
        {
            Append(value, "no-element");
        }
        else
        {
            Append(value, "element");
            AppendType(value, type.Element);
        }
        Append(value, type.Arguments.Length);
        foreach (ResolvedResourceEffectType argument in type.Arguments)
            AppendType(value, argument);
    }

    static void AppendTypeRef(StringBuilder value, TypeRef type)
    {
        Append(value, (int)type.Kind);
        Append(value, type.Assembly);
        Append(value, type.Namespace);
        Append(value, type.Name);
        Append(value, type.Rank);
        Append(value, type.GenericParameterIndex);
        Append(value, type.RawTypeKind);
        Append(value, type.ArraySizes.Length);
        foreach (int size in type.ArraySizes)
            Append(value, size);
        Append(value, type.ArrayLowerBounds.Length);
        foreach (int lowerBound in type.ArrayLowerBounds)
            Append(value, lowerBound);
        if (type.ElementType is null)
        {
            Append(value, "no-element");
        }
        else
        {
            Append(value, "element");
            AppendTypeRef(value, type.ElementType);
        }
        Append(value, type.TypeArguments.Length);
        foreach (TypeRef argument in type.TypeArguments)
            AppendTypeRef(value, argument);
        if (type.ModifierType is null)
        {
            Append(value, "no-modifier");
        }
        else
        {
            Append(value, "modifier");
            AppendTypeRef(value, type.ModifierType);
        }
        if (type.UnmodifiedType is null)
        {
            Append(value, "no-unmodified");
        }
        else
        {
            Append(value, "unmodified");
            AppendTypeRef(value, type.UnmodifiedType);
        }
    }

    static void AppendAssembly(
        StringBuilder value,
        AssemblyReferenceIdentity assembly)
    {
        Append(value, assembly.Name);
        Append(value, assembly.Version?.ToString() ?? "");
        Append(value, assembly.Culture ?? "");
        Append(value, assembly.PublicKeyToken ?? "");
    }

    static void Append(StringBuilder value, string text)
    {
        value.Append(text.Length.ToString(CultureInfo.InvariantCulture));
        value.Append(':');
        value.Append(text);
        value.Append(';');
    }

    static void Append(StringBuilder value, Guid item) =>
        Append(value, item.ToString("D"));

    static void Append(StringBuilder value, int item) =>
        Append(value, item.ToString(CultureInfo.InvariantCulture));
}
