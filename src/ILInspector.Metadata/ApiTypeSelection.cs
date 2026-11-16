using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Narrows an API surface projection by structured Type identity or full-name
/// selection.
/// </summary>
/// <remarks>
/// Extension/instance member pairing can relate a selected Type to a Type
/// that declares an extension method, on either endpoint of a comparison. A
/// comparison therefore pairs the selection with both endpoints'
/// extension-declaring Types (<see cref="PairWith"/>) before it projects
/// either side: those Types and their nested Types stay in both projections,
/// and a selection that admits one of them keeps both projections complete.
/// A Type whose name traversal is rejected also stays, so its failure is
/// reported as in the complete projection. A selection of exact full names
/// (<see cref="ForFullNames"/>) pairs instead with only the Types that declare
/// an extension for a selected Type.
/// </remarks>
public sealed class ApiTypeSelection
{
    readonly Func<string, bool> _matchesFullName;
    readonly IReadOnlySet<string> _extensionTypes;
    readonly MetadataTypeDefinitionName? _definition;
    readonly IReadOnlySet<string>? _fullNames;

    public bool MatchesFullName(string fullName) => _matchesFullName(fullName);

    public ApiTypeSelection(Func<string, bool> matchesFullName)
        : this(matchesFullName, new HashSet<string>(StringComparer.Ordinal))
    {
    }

    ApiTypeSelection(
        Func<string, bool> matchesFullName,
        IReadOnlySet<string> extensionTypes,
        MetadataTypeDefinitionName? definition = null,
        IReadOnlySet<string>? fullNames = null)
    {
        ArgumentNullException.ThrowIfNull(matchesFullName);
        _matchesFullName = matchesFullName;
        _extensionTypes = extensionTypes;
        _definition = definition;
        _fullNames = fullNames;
    }

    /// <summary>
    /// Selects the Types whose full names, as <see cref="ApiType.FullName"/>
    /// spells them, equal one of <paramref name="fullNames"/> exactly. A
    /// comparison pairs it with only the Types that declare an extension for
    /// a selected Type (<see cref="ExtensionDeclaringTypeNamesFor"/>), not
    /// with every extension-declaring Type.
    /// </summary>
    public static ApiTypeSelection ForFullNames(IEnumerable<string> fullNames)
    {
        ArgumentNullException.ThrowIfNull(fullNames);
        var names = new HashSet<string>(fullNames, StringComparer.Ordinal);
        if (names.Count == 0)
            throw new ArgumentException("An exact Type selection names at least one Type.", nameof(fullNames));
        return new(
            names.Contains,
            new HashSet<string>(StringComparer.Ordinal),
            definition: null,
            names);
    }

    /// <summary>
    /// The exact full names a <see cref="ForFullNames"/> selection names, or
    /// null for any other selection.
    /// </summary>
    public IReadOnlySet<string>? FullNames => _fullNames;

    /// <summary>
    /// Selects one exact Type definition without conflating namespace and
    /// nesting separators in its display name.
    /// </summary>
    public static ApiTypeSelection ForDefinition(MetadataTypeDefinitionName definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new(
            name => StringComparer.Ordinal.Equals(name, definition.ToMetadataFullName()),
            new HashSet<string>(StringComparer.Ordinal),
            definition);
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
            : new ApiTypeSelection(_matchesFullName, extensionTypes, _definition, _fullNames);
    }

    /// <summary>
    /// Returns the full names of the top-level Types that declare an
    /// extension, in this image, whose receiver is a Type named by
    /// <paramref name="receivers"/>, hidden, obsolete, and non-public
    /// extensions included, or null when a name, a receiver's extension
    /// relations, or a declaring Type cannot be read completely; with null,
    /// <see cref="PairWith"/> keeps both projections complete.
    /// When a named Type itself declares an extension, returns every
    /// extension-declaring Type instead, so <see cref="PairWith"/> keeps both
    /// projections complete as it does for any selection of such a Type: its
    /// extensions pair with members of the Types they extend.
    /// </summary>
    /// <remarks>
    /// Otherwise reads the canonical same-image extension relation population
    /// of each receiver Type, which holds extension methods and properties,
    /// so no Type is projected for pairing unless it declares an extension
    /// for a selected Type.
    /// </remarks>
    internal static IReadOnlySet<string>? ExtensionDeclaringTypeNamesFor(
        PEReader image,
        MetadataReader reader,
        IReadOnlySet<string> receivers)
    {
        IReadOnlySet<string>? everyDeclaring = ExtensionDeclaringTypeNames(reader);
        if (everyDeclaring is null || everyDeclaring.Overlaps(receivers))
            return everyDeclaring;

        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            AssemblyReferenceIdentity? identity = null;
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                if (reader.ResolveFullTypeName(handle)
                    is not RelationshipTraversalResult<string>.Completed { Value: var name })
                {
                    return null;
                }
                if (!receivers.Contains(name))
                    continue;
                if (MetadataTypeDefinitionName.Read(reader, handle)
                    is not MetadataTypeDefinitionNameReadResult.Read { Name: var typeName })
                {
                    return null;
                }

                identity ??= AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
                MetadataExtensionRelationPopulationOutcome outcome =
                    MetadataRelationInspection.ExecuteExtensionPopulation(
                        image,
                        reader,
                        new MetadataExtensionRelationPopulationRequest(
                            new MetadataExtensionReceiverSelection(
                                identity,
                                typeName,
                                MetadataTypeDefinitionAddress.FromHandle(reader, handle)),
                            new MetadataOperationPolicy(int.MaxValue),
                            rows: new MetadataExtensionRelationPopulationRowsRequest(0, int.MaxValue),
                            // The complete comparison pairs with every
                            // extension it projects, hidden, obsolete, and
                            // non-public ones included.
                            includeNonPublic: true),
                        CancellationToken.None);
                if (outcome is not MetadataExtensionRelationPopulationOutcome.Available
                    {
                        Result:
                        {
                            Disposition: MetadataRelationFamilyDisposition.Complete,
                            Rows: MetadataExtensionRelationPopulationRowsOutcome.Read
                            {
                                NextOrdinal: null,
                            } rows,
                        },
                    })
                {
                    return null;
                }

                foreach (MetadataExtensionRelationPopulationRow row in rows.Items)
                {
                    foreach (MetadataExtensionRelationEvidence occurrence in row.Occurrences)
                    {
                        if (!occurrence.DeclaringType.TryResolve(reader, out TypeDefinitionHandle declaring)
                            || reader.ResolveFullTypeName(Outermost(reader, declaring))
                                is not RelationshipTraversalResult<string>.Completed { Value: var declaringName })
                        {
                            return null;
                        }
                        names.Add(declaringName);
                    }
                }
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
            if (_definition is { } definition)
            {
                if (MetadataTypeDefinitionName.Matches(reader, handle, definition, out _)
                    != MetadataTypeDefinitionNameMatchResult.NoMatch)
                {
                    include[MetadataTokens.GetRowNumber(handle)] = true;
                    continue;
                }
                if (_extensionTypes.Count == 0)
                    continue;
            }
            include[MetadataTokens.GetRowNumber(handle)] =
                reader.ResolveFullTypeName(handle)
                    is not RelationshipTraversalResult<string>.Completed { Value: var name }
                || _definition is null && _matchesFullName(name)
                || IsExtensionTypeOrNested(name);
        }
        return handle => include[MetadataTokens.GetRowNumber(handle)];
    }

    internal static Func<TypeDefinitionHandle, bool>? BindDefinition(
        MetadataReader reader,
        TypeDeclarationResult declaration)
    {
        if (declaration is not TypeDeclarationResult.Defined defined
            || ExtensionDeclaringTypeNames(reader) is not { } extensionTypes)
        {
            return null;
        }

        Func<TypeDefinitionHandle, bool> includeExtensions =
            new ApiTypeSelection(static _ => false, extensionTypes).Bind(reader);
        return handle =>
            MetadataTokens.GetToken(handle) == defined.Definition.Value
            || includeExtensions(handle);
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
