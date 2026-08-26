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
        ResolvedAssemblyReference bodyAssembly)
        => Resolve(
            members
                .Where(member => member.MetadataToken is not null)
                .Select(member => member.MetadataToken!.Value),
            tokenOrigin,
            bodyAssembly);

    internal static IReadOnlyDictionary<int, int> Resolve(
        IEnumerable<int> sourceMethodTokens,
        ResolvedAssemblyReference tokenOrigin,
        ResolvedAssemblyReference bodyAssembly)
    {
        int[] sourceTokens = sourceMethodTokens
            .Distinct()
            .ToArray();

        if (tokenOrigin.Registration == bodyAssembly.Registration)
            return sourceTokens.ToDictionary(token => token);

        using var originImage = AssemblyInspectionSession.Open(tokenOrigin);
        using var bodyImage = AssemblyInspectionSession.Open(bodyAssembly);
        if (!originImage.HasMetadata || !bodyImage.HasMetadata)
        {
            throw new InvalidOperationException(
                "Cannot establish body-member correspondence because an acquisition has no metadata.");
        }

        var sourceAnchors = new Dictionary<int, MemberAnchor>();
        foreach (int sourceToken in sourceTokens)
        {
            MemberAnchor anchor =
                originImage.MethodBodies.ResolveMethodAnchor(sourceToken)
                ?? throw new InvalidOperationException(
                    $"MethodDef token 0x{sourceToken:X8} does not identify a method in the API acquisition.");
            sourceAnchors.Add(sourceToken, anchor);
        }

        IReadOnlyDictionary<string, MethodBodySelection> bodyMethods =
            bodyImage.MethodBodies.ResolveMethods(sourceAnchors.Values);
        var resolved = new Dictionary<int, int>();
        foreach ((int sourceToken, MemberAnchor anchor) in sourceAnchors)
        {
            if (!bodyMethods.TryGetValue(
                    anchor.CanonicalSignature,
                    out MethodBodySelection? selection))
            {
                throw new InvalidOperationException(
                    $"Cannot correspond '{anchor.CanonicalSignature}' from the API acquisition to '{bodyAssembly.Identity.Name}'.");
            }

            resolved.Add(sourceToken, selection.MetadataToken);
        }

        return resolved;
    }
}
