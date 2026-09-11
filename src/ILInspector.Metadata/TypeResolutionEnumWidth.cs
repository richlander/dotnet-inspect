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
/// still win inside <see cref="AttributeDecoder"/>. A missing, unplanned, or
/// unopened definition leaves the adapter's default width at
/// <see cref="PrimitiveTypeCode.Int32"/>.
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
    /// returns a decoder callback keyed by SRM's normalized serialized-name
    /// projection. Names the generation did not resolve, and names shared by
    /// distinct structured requests that the callback cannot distinguish, stay
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
        var requestsByName = new Dictionary<string, TypeResolutionRequest>(
            StringComparer.Ordinal);
        var ambiguousNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypeResolutionRequest request in planned)
        {
            ArgumentNullException.ThrowIfNull(request);
            string name = request.Type.ToMetadataFullName();
            if (requestsByName.TryGetValue(
                    name,
                    out TypeResolutionRequest? existing))
            {
                if (!TypeResolutionRequestComparer.Instance.Equals(
                        existing,
                        request))
                {
                    ambiguousNames.Add(name);
                    widths.Remove(name);
                }
            }
            else
            {
                requestsByName.Add(name, request);
            }

            if (ambiguousNames.Contains(name))
                continue;

            if (context.Resolve(request)
                    is not TypeResolutionOutcome.Resolved resolved
                || !SatisfiesExplicitUnsignedRequest(request, resolved)
                || !context.TryGetEnumUnderlyingType(
                    resolved.Definition,
                    out PrimitiveTypeCode code))
            {
                continue;
            }

            widths.TryAdd(name, code);
        }

        return name =>
            widths.TryGetValue(name, out PrimitiveTypeCode code)
                ? code
                : PrimitiveTypeCode.Int32;
    }

    /// <summary>
    /// Enforces an explicit <c>PublicKeyToken=null</c> after binding.
    /// <see cref="AssemblyReferenceIdentity.MatchesCandidate"/> reads an empty
    /// token as a wildcard, so a request that names an unsigned assembly can
    /// still bind a signed candidate of the same name. Narrowing here keeps
    /// the qualifier a constraint without changing that identity contract,
    /// which <c>AssemblyDependencyResolver</c> and <c>MetadataSource</c> also
    /// consume.
    ///
    /// The qualifier constrains the assembly the reference bound to, not the
    /// assembly that ultimately defines the type. When forwarding hops were
    /// followed the bound assembly is the first hop's source, so a signed
    /// facade cannot satisfy the qualifier by forwarding to an unsigned
    /// implementation, and an unsigned facade is not rejected for forwarding
    /// to a signed one. Gated by
    /// <c>ExplicitNullPublicKeyToken_RejectsSignedFacadeForwardingToUnsigned</c>
    /// and
    /// <c>ExplicitNullPublicKeyToken_AcceptsUnsignedFacadeForwardingToSigned</c>.
    /// </summary>
    static bool SatisfiesExplicitUnsignedRequest(
        TypeResolutionRequest request,
        TypeResolutionOutcome.Resolved resolved)
    {
        if (request.Start is not TypeResolutionStart.Reference reference
            || reference.Value.PublicKeyToken is not { Length: 0 })
        {
            return true;
        }

        ResolvedAssemblyCandidate bound =
            resolved.Hops.IsDefaultOrEmpty
                ? resolved.Definition.Assembly
                : resolved.Hops[0].SourceAssembly;
        return string.IsNullOrEmpty(bound.Assembly.Identity.PublicKeyToken);
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
            if (string.IsNullOrEmpty(assemblyName.Name)
                || !TryGetPublicKeyToken(
                    assemblyName,
                    out string? publicKeyToken))
            {
                return false;
            }

            assembly = new AssemblyReferenceIdentity(
                assemblyName.Name,
                assemblyName.Version,
                ExplicitCultureOrNull(assemblyName.CultureName),
                publicKeyToken);
        }

        if (MetadataTypeDefinitionName.FromParsedSerializedName(parsed)
            is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            return false;
        }

        type = valid.Name;
        return true;
    }

    /// <summary>
    /// Distinguishes an omitted culture qualifier from an explicit
    /// <c>Culture=neutral</c>. <see cref="AssemblyNameInfo"/> reports the
    /// former as <see langword="null"/> and the latter as empty, but
    /// <see cref="AssemblyReferenceIdentity.MatchesCandidate"/> treats an
    /// empty culture as a wildcard. Spelling the explicit form as
    /// <c>neutral</c> keeps it a constraint, so a request for the neutral
    /// culture cannot bind a culture-specific candidate.
    /// </summary>
    static string? ExplicitCultureOrNull(string? cultureName)
        => cultureName is null ? null
            : cultureName.Length == 0 ? "neutral"
            : cultureName;

    static bool TryGetPublicKeyToken(
        AssemblyNameInfo assemblyName,
        out string? publicKeyToken)
    {
        ImmutableArray<byte> token = assemblyName.PublicKeyOrToken;
        if (token.IsDefault)
        {
            // The qualifier was omitted, so any candidate may satisfy it.
            publicKeyToken = null;
            return true;
        }

        if (token.IsEmpty)
        {
            // An explicit `PublicKeyToken=null` names an unsigned assembly.
            // Keep it as a recorded constraint rather than refusing the name:
            // refusing would leave every unsigned cross-assembly enum on the
            // Int32 default. AssemblyReferenceIdentity reads an empty token as
            // a wildcard during binding, so CreateResolver narrows the bound
            // candidate afterwards and drops a signed one.
            publicKeyToken = "";
            return true;
        }

        if ((assemblyName.Flags & AssemblyNameFlags.PublicKey) != 0)
        {
            publicKeyToken =
                AssemblyReferenceIdentity.ComputePublicKeyToken(
                    token.ToArray());
            return true;
        }

        if (token.Length != 8)
        {
            publicKeyToken = null;
            return false;
        }

        publicKeyToken =
            Convert.ToHexString(token.AsSpan()).ToLowerInvariant();
        return true;
    }
}
