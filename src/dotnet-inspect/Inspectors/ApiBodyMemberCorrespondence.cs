using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Establishes MethodDef-token correspondence between the API surface
/// acquisition and the assembly acquisition that owns the analyzed bodies.
/// </summary>
internal static class ApiBodyMemberCorrespondence
{
    internal static IReadOnlyDictionary<int, int> Resolve(
        IReadOnlyList<ApiMember> members,
        ResolvedAssemblyReference tokenOrigin,
        ResolvedAssemblyReference bodyAssembly,
        string? projectAssetsPath,
        string? targetFramework,
        string? platformFramework)
        => Resolve(
            members
                .Where(member => member.MetadataToken is not null)
                .Select(member => member.MetadataToken!.Value),
            tokenOrigin,
            bodyAssembly,
            projectAssetsPath,
            targetFramework,
            platformFramework);

    internal static IReadOnlyDictionary<int, int> Resolve(
        IEnumerable<int> sourceMethodTokens,
        ResolvedAssemblyReference tokenOrigin,
        ResolvedAssemblyReference bodyAssembly,
        string? projectAssetsPath,
        string? targetFramework,
        string? platformFramework)
    {
        int[] sourceTokens = sourceMethodTokens
            .Distinct()
            .ToArray();

        if (ReferenceEquals(tokenOrigin, bodyAssembly))
        {
            using AssemblyInspectionSession image =
                AssemblyInspectionSession.Open(tokenOrigin);
            return sourceTokens.ToDictionary(token => token);
        }

        using var originImage = AssemblyInspectionSession.Open(tokenOrigin);
        using var bodyImage = AssemblyInspectionSession.Open(bodyAssembly);
        if (!originImage.HasMetadata || !bodyImage.HasMetadata)
        {
            throw new InvalidOperationException(
                "Cannot establish body-member correspondence because an acquisition has no metadata.");
        }
        if (tokenOrigin.Registration == bodyAssembly.Registration)
            return sourceTokens.ToDictionary(token => token);

        var sourceAnchors = new Dictionary<int, MemberAnchor>();
        foreach (int sourceToken in sourceTokens)
        {
            MemberAnchor anchor =
                originImage.MethodBodies.ResolveMethodAnchor(sourceToken)
                ?? throw new InvalidOperationException(
                    $"MethodDef token 0x{sourceToken:X8} does not identify a method in the API acquisition.");
            sourceAnchors.Add(sourceToken, anchor);
        }

        using var nominalTypes = new NominalTypeIdentityResolver(
            tokenOrigin,
            tokenOrigin.ContentModuleVersionId
                ?? throw new InvalidOperationException(
                    "The token-origin acquisition has no bound module generation."),
            bodyAssembly,
            bodyAssembly.ContentModuleVersionId
                ?? throw new InvalidOperationException(
                    "The body acquisition has no bound module generation."),
            projectAssetsPath,
            targetFramework,
            platformFramework);
        IReadOnlyDictionary<int, MethodBodySelection> bodyMethods =
            originImage.MethodBodies.ResolveCorrespondingMethods(
                sourceTokens,
                bodyImage.MethodBodies,
                nominalTypes.SourceIdentity,
                nominalTypes.TargetIdentity);
        var resolved = new Dictionary<int, int>();
        foreach ((int sourceToken, MemberAnchor anchor) in sourceAnchors)
        {
            if (!bodyMethods.TryGetValue(
                    sourceToken,
                    out MethodBodySelection? selection))
            {
                throw new InvalidOperationException(
                    $"Cannot correspond '{anchor.CanonicalSignature}' from the API acquisition to '{bodyAssembly.Identity.Name}'.");
            }

            resolved.Add(sourceToken, selection.MetadataToken);
        }

        return resolved;
    }

    sealed class NominalTypeIdentityResolver : IDisposable
    {
        readonly ResolvedAssemblyReference _sourceRoot;
        readonly ResolvedAssemblyReference _targetRoot;
        readonly Guid _sourceRootModuleVersionId;
        readonly Guid _targetRootModuleVersionId;
        readonly string? _projectAssetsPath;
        readonly string? _targetFramework;
        readonly string? _platformFramework;
        readonly Dictionary<MetadataNamedTypeReference, string>
            _sourceIdentities = [];
        readonly Dictionary<MetadataNamedTypeReference, string>
            _targetIdentities = [];
        TypeDefinitionResolutionSession? _sourceResolution;
        TypeDefinitionResolutionSession? _targetResolution;

        internal NominalTypeIdentityResolver(
            ResolvedAssemblyReference source,
            Guid sourceRootModuleVersionId,
            ResolvedAssemblyReference target,
            Guid targetRootModuleVersionId,
            string? projectAssetsPath,
            string? targetFramework,
            string? platformFramework)
        {
            _sourceRoot = source;
            _targetRoot = target;
            _sourceRootModuleVersionId = sourceRootModuleVersionId;
            _targetRootModuleVersionId = targetRootModuleVersionId;
            _projectAssetsPath = projectAssetsPath;
            _targetFramework = targetFramework;
            _platformFramework = platformFramework;
        }

        internal string SourceIdentity(
            MetadataNamedTypeReference reference) =>
            Identity(
                reference,
                source: true,
                _sourceIdentities);

        internal string TargetIdentity(
            MetadataNamedTypeReference reference) =>
            Identity(
                reference,
                source: false,
                _targetIdentities);

        string Identity(
            MetadataNamedTypeReference reference,
            bool source,
            Dictionary<MetadataNamedTypeReference, string> cache)
        {
            if (cache.TryGetValue(reference, out string? identity))
                return identity;

            if (reference.Scope
                is MetadataTypeReferenceScope.CurrentAssembly)
            {
                identity = RootIdentity(reference.Type);
            }
            else
            {
                TypeResolutionOutcome outcome =
                    Resolve(reference, source);
                if (outcome
                    is not TypeResolutionOutcome.Resolved resolved)
                {
                    throw new InvalidOperationException(
                        "Cannot resolve signature type "
                        + $"'{reference.Type.ToEscapedFullName()}' "
                        + "for cross-acquisition member correspondence.");
                }

                MetadataTypeDefinitionAddress address =
                    resolved.Definition.Address;
                identity =
                    address.ModuleVersionId
                        == _sourceRootModuleVersionId
                    || address.ModuleVersionId
                        == _targetRootModuleVersionId
                        ? RootIdentity(resolved.Definition.Type)
                        : $"definition:{address.ModuleVersionId:N}:"
                            + $"{address.Definition.Value:X8}";
            }

            cache.Add(reference, identity);
            return identity;
        }

        TypeResolutionOutcome Resolve(
            MetadataNamedTypeReference reference,
            bool source)
        {
            try
            {
                return Resolution(source).Resolve(reference);
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException
                    or ArgumentException)
            {
                throw new InvalidOperationException(
                    "Cannot resolve signature type "
                    + $"'{reference.Type.ToEscapedFullName()}' from the "
                    + $"{(source ? "source" : "target")} acquisition.",
                    ex);
            }
        }

        TypeDefinitionResolutionSession Resolution(bool source)
        {
            if (source)
            {
                return _sourceResolution ??=
                    new TypeDefinitionResolutionSession(
                        _sourceRoot,
                        _projectAssetsPath,
                        _targetFramework,
                        _platformFramework);
            }

            return _targetResolution ??=
                new TypeDefinitionResolutionSession(
                    _targetRoot,
                    _projectAssetsPath,
                    _targetFramework,
                    _platformFramework);
        }

        static string RootIdentity(
            MetadataTypeDefinitionName type) =>
            $"root:{type.ToEscapedFullName()}";

        public void Dispose()
        {
            _targetResolution?.Dispose();
            _sourceResolution?.Dispose();
        }
    }
}
