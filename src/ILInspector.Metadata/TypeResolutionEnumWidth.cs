using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Plans TypeResolution requests from custom-attribute serialized enum names
/// and materializes a frozen width table from a generation that already
/// retained the defining images.
///
/// This is a definition locator adapter, not a width oracle: local TypeDefs
/// still win inside <see cref="AttributeDecoder"/> /
/// <see cref="CustomAttributeValueGuard"/>. A missing, unplanned, or
/// unopened definition stays
/// <see cref="PrimitiveTypeCode.Int32"/> so guard skip and SRM consume the
/// same four bytes.
/// Gated by <c>TypeResolutionEnumWidthTests</c>.
/// </summary>
public static class TypeResolutionEnumWidth
{
    /// <summary>
    /// Builds one structured request from a blob-authored serialized name.
    /// Assembly-qualified names become <see cref="TypeResolutionRequest.FromReference"/>;
    /// simple names start at the requesting assembly so a local TypeDef or
    /// ExportedType hop can locate the definition.
    /// </summary>
    public static bool TryCreateRequest(
        string serializedName,
        ResolvedAssemblyReference requesting,
        AssemblyResolutionScope scope,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out TypeResolutionRequest? request)
    {
        ArgumentNullException.ThrowIfNull(serializedName);
        ArgumentNullException.ThrowIfNull(requesting);
        request = null;
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
        if (serializedName.Length > MetadataSafetyPolicy.MaxTypeNameCharacters)
            return false;
        if (!TryParseDefinitionName(
                serializedName,
                out MetadataTypeDefinitionName? type,
                out AssemblyReferenceIdentity? assembly))
        {
            return false;
        }

        request = assembly is null
            ? TypeResolutionRequest.FromAssembly(requesting, scope, type)
            : TypeResolutionRequest.FromReference(
                assembly,
                AssemblyBindingOrigin.FromAssembly(requesting),
                scope,
                type);
        return true;
    }

    /// <summary>
    /// Resolves each planned request against the frozen generation and
    /// returns a decoder callback keyed by
    /// <see cref="MetadataTypeDefinitionName.ToMetadataFullName"/>. Names the
    /// generation did not resolve stay
    /// <see cref="PrimitiveTypeCode.Int32"/>.
    /// </summary>
    public static Func<string, PrimitiveTypeCode> CreateResolver(
        TypeResolutionContext context,
        IEnumerable<TypeResolutionRequest> planned)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(planned);

        var widths = new Dictionary<string, PrimitiveTypeCode>(
            StringComparer.Ordinal);
        foreach (TypeResolutionRequest request in planned)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (context.Resolve(request)
                    is not TypeResolutionOutcome.Resolved resolved
                || !context.TryGetEnumUnderlyingType(
                    resolved.Definition,
                    out PrimitiveTypeCode code))
            {
                continue;
            }

            widths.TryAdd(request.Type.ToMetadataFullName(), code);
        }

        return name =>
            widths.TryGetValue(name, out PrimitiveTypeCode code)
                ? code
                : PrimitiveTypeCode.Int32;
    }

    static bool TryParseDefinitionName(
        string serializedName,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out MetadataTypeDefinitionName? type,
        out AssemblyReferenceIdentity? assembly)
    {
        type = null;
        assembly = null;
        var options = new TypeNameParseOptions
        {
            MaxNodes = MetadataSafetyPolicy.MaxRelationshipNodes,
        };
        if (!TypeName.TryParse(serializedName, out TypeName? parsed, options)
            || !parsed.IsSimple)
        {
            return false;
        }

        if (parsed.AssemblyName is { } assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName.Name))
                return false;
            assembly = new AssemblyReferenceIdentity(
                assemblyName.Name,
                assemblyName.Version,
                assemblyName.CultureName,
                PublicKeyToken(assemblyName));
        }

        var segments = ImmutableArray.CreateBuilder<string>();
        TypeName current = parsed;
        while (true)
        {
            if ((current != parsed && current.AssemblyName is not null)
                || !current.IsSimple)
            {
                return false;
            }

            segments.Add(current.Name);
            if (!current.IsNested)
                break;
            current = current.DeclaringType;
        }

        var rootToLeaf = ImmutableArray.CreateBuilder<string>(segments.Count);
        for (int i = segments.Count - 1; i >= 0; i--)
            rootToLeaf.Add(segments[i]);

        if (MetadataTypeDefinitionName.Create(
                current.Namespace,
                rootToLeaf.MoveToImmutable())
            is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            return false;
        }

        type = valid.Name;
        return true;
    }

    static string? PublicKeyToken(AssemblyNameInfo assemblyName)
    {
        ImmutableArray<byte> token = assemblyName.PublicKeyOrToken;
        return token.IsDefaultOrEmpty
            ? null
            : Convert.ToHexString(token.AsSpan()).ToLowerInvariant();
    }
}
