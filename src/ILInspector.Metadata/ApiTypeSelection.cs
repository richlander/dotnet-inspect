using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>
/// Narrows an API surface projection to the Types whose full names a host
/// selection admits.
/// </summary>
/// <remarks>
/// Extension/instance member pairing can relate a selected Type to a Type
/// that declares an extension method, on either endpoint of a comparison. A
/// comparison therefore pairs the selection with both endpoints'
/// extension-declaring Types (<see cref="PairWith"/>) before it projects
/// either side: those Types and their nested Types stay in both projections,
/// and a selection that admits one of them keeps both projections complete.
/// A Type whose name traversal is rejected also stays, so its failure is
/// reported as in the complete projection.
/// </remarks>
public sealed class ApiTypeSelection
{
    readonly Func<string, bool> _matchesFullName;
    readonly IReadOnlySet<string> _extensionTypes;

    public ApiTypeSelection(Func<string, bool> matchesFullName)
        : this(matchesFullName, new HashSet<string>(StringComparer.Ordinal))
    {
    }

    ApiTypeSelection(Func<string, bool> matchesFullName, IReadOnlySet<string> extensionTypes)
    {
        ArgumentNullException.ThrowIfNull(matchesFullName);
        _matchesFullName = matchesFullName;
        _extensionTypes = extensionTypes;
    }

    /// <summary>
    /// Returns the selection to apply to both endpoints, or null when either
    /// projection must stay complete.
    /// </summary>
    /// <param name="before">
    /// The full names of the before endpoint's extension-declaring Types, or
    /// null when they could not be read.
    /// </param>
    /// <param name="after">The same names for the after endpoint.</param>
    public ApiTypeSelection? PairWith(
        IReadOnlySet<string>? before,
        IReadOnlySet<string>? after)
    {
        if (before is null || after is null)
            return null;

        var extensionTypes = new HashSet<string>(before, StringComparer.Ordinal);
        extensionTypes.UnionWith(after);
        return extensionTypes.Any(_matchesFullName)
            ? null
            : new ApiTypeSelection(_matchesFullName, extensionTypes);
    }

    /// <summary>
    /// Returns the full names of the top-level Types that declare an extension
    /// method, or null when the image's method or Type rows cannot be read.
    /// </summary>
    internal static IReadOnlySet<string>? ExtensionDeclaringTypeNames(MetadataReader reader)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
            {
                MethodDefinition method = reader.GetMethodDefinition(handle);
                if ((method.Attributes & MethodAttributes.Static) == 0
                    || !AttributeReader.HasExtensionAttribute(
                        reader, method.GetCustomAttributes()))
                {
                    continue;
                }

                if (reader.ResolveFullTypeName(Outermost(reader, method.GetDeclaringType()))
                    is not RelationshipTraversalResult<string>.Completed { Value: var name })
                {
                    return null;
                }
                names.Add(name);
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            return null;
        }
        return names;
    }

    /// <summary>
    /// Returns the TypeDef predicate for one endpoint image.
    /// </summary>
    internal Func<TypeDefinitionHandle, bool> Bind(MetadataReader reader)
    {
        var include = new bool[reader.TypeDefinitions.Count + 1];
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            include[MetadataTokens.GetRowNumber(handle)] =
                reader.ResolveFullTypeName(handle)
                    is not RelationshipTraversalResult<string>.Completed { Value: var name }
                || _matchesFullName(name)
                || IsExtensionTypeOrNested(name);
        }
        return handle => include[MetadataTokens.GetRowNumber(handle)];
    }

    bool IsExtensionTypeOrNested(string name)
    {
        if (_extensionTypes.Contains(name))
            return true;
        for (int dot = name.IndexOf('.'); dot > 0; dot = name.IndexOf('.', dot + 1))
        {
            if (_extensionTypes.Contains(name[..dot]))
                return true;
        }
        return false;
    }

    static TypeDefinitionHandle Outermost(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinitionHandle current = handle;
        for (int depth = 0; depth <= reader.TypeDefinitions.Count; depth++)
        {
            TypeDefinitionHandle parent = reader.GetTypeDefinition(current).GetDeclaringType();
            if (parent.IsNil)
                return current;
            current = parent;
        }
        throw new BadImageFormatException("Nested Type chain does not terminate.");
    }
}
