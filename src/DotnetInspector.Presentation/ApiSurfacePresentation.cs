using System.Collections.Immutable;

using CSharpText;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

namespace DotnetInspector.Presentation;

public sealed record ApiTypeSurfacePresentation(
    string DefinitionId,
    string QueryId,
    string MetadataId,
    string Name,
    string DisplayName,
    string Namespace,
    string Kind,
    string KindFacetId,
    ImmutableArray<string> TraitFacetIds,
    string Accessibility,
    string AccessibilityId,
    string Signature);

public sealed record ApiParameterSurfacePresentation(
    string Name,
    string Type,
    string? Modifier,
    bool HasDefault,
    string? DefaultValue);

public sealed record ApiMemberBodySelectorPresentation(
    int Token,
    string MemberName,
    string SelectorKey);

public sealed record ApiMemberSurfacePresentation(
    string Name,
    string Kind,
    string Signature,
    string Accessibility,
    bool IsStatic,
    bool IsUnsafe,
    bool IsVirtual,
    bool IsAbstract,
    bool IsOverride,
    bool IsExtension,
    bool IsObsolete,
    int GenericArity,
    int? MetadataToken,
    int? DeclarationMetadataToken,
    string? ReturnType,
    ImmutableArray<ApiParameterSurfacePresentation> Parameters,
    string? DocumentationId,
    string StableSelector,
    string AnchorDigest,
    string CanonicalSignature,
    string AnchorTypeFullName,
    string? DeclaringTypeDefinitionId,
    string GraphSelectorKey,
    ImmutableArray<ApiMemberBodySelectorPresentation> BodySelectors);

/// <summary>
/// Projects Metadata-owned API declarations into detached presentation facts
/// shared by product hosts.
/// </summary>
public static class ApiSurfacePresentation
{
    public static ApiTypeSurfacePresentation Type(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        string displayName = MetadataTypeNameFormatter.FormatGenericTypeName(
            type.Name,
            type.TypeParameters);
        ApiAccessibilityBucket bucket =
            ApiAccessibility.Classify(type.Accessibility);
        string accessibility = string.IsNullOrWhiteSpace(type.Accessibility)
            ? bucket.Label
            : type.Accessibility;
        IReadOnlyList<string> typeModifiers =
            ResearchViews.TypeModifiers(type);
        string kind = string.Join(
            ' ',
            typeModifiers.Append(type.Kind));
        string signature = string.Join(
            ' ',
            new[] { accessibility }
                .Concat(typeModifiers)
                .Append(type.Kind)
                .Append(displayName));
        string metadataId = MetadataId(type);

        return new(
            type.DefinitionName?.ToEscapedFullName() ?? metadataId,
            type.FullName,
            metadataId,
            type.Name,
            displayName,
            type.Namespace ?? "",
            kind,
            ApiInventoryQuery.TypeKindFacetId(type),
            [.. ApiInventoryQuery.TypeTraitFacetIds(type)],
            accessibility,
            bucket.Id,
            signature);
    }

    public static ApiMemberSurfacePresentation Member(
        ApiType type,
        ApiMember member)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);

        MemberAnchor anchor =
            ApiMemberIdentity.GetMemberAnchor(type, member);
        CallGraphMemberSelector graphSelector =
            CallGraphMemberResolver.CreateSelector(type, member);

        return new(
            member.Name,
            member.Kind,
            member.Signature ?? member.Name,
            member.Accessibility ?? "public",
            member.IsStatic,
            member.IsUnsafe,
            member.IsVirtual,
            member.IsAbstract,
            member.IsOverride,
            member.IsExtension,
            member.IsObsolete,
            member.SignatureModel?.TypeParameters.Count ?? 0,
            member.MetadataToken,
            member.DeclarationMetadataToken,
            member.SignatureModel?.ReturnType ?? member.ReturnType,
            [
                .. (member.SignatureModel?.Parameters ?? []).Select(
                    static parameter =>
                        new ApiParameterSurfacePresentation(
                            parameter.Name,
                            parameter.Type,
                            parameter.Modifier,
                            parameter.HasDefault,
                            parameter.DefaultValueText)),
            ],
            DocumentationId(type, member),
            anchor.StableSelector,
            anchor.Fingerprint,
            anchor.CanonicalSignature,
            anchor.TypeFullName,
            member.DeclaringTypeDefinitionName?.ToEscapedFullName(),
            graphSelector.Key,
            [
                .. CallGraphMemberResolver
                    .CreateBodySelectors(type, member)
                    .Select(static selector =>
                        new ApiMemberBodySelectorPresentation(
                            selector.BodyToken,
                            selector.MemberName,
                            selector.SelectorKey)),
            ]);
    }

    static string MetadataId(ApiType type)
    {
        string name = type.MetadataName ?? type.Name;
        return string.IsNullOrEmpty(type.Namespace)
            ? name
            : $"{type.Namespace}.{name}";
    }

    static string? DocumentationId(ApiType type, ApiMember member) =>
        ApiMemberIdentity.TryGetXmlDocMemberIdentity(
            type,
            member,
            out XmlDocMemberIdentity identity)
            ? identity.Value
            : null;
}
