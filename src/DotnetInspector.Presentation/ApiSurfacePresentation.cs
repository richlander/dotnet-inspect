using System.Collections.Immutable;

using CSharpText;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

namespace DotnetInspector.Presentation;

public readonly record struct ApiTypeSurfacePresentation(
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

public readonly record struct ApiParameterSurfacePresentation(
    string Name,
    string Type,
    string? Modifier,
    bool HasDefault,
    string? DefaultValue,
    string? Description);

public readonly record struct ApiMemberBodySelectorPresentation(
    int Token,
    string MemberName,
    string SelectorKey);

public readonly record struct ApiMemberSurfacePresentation<
    TParameter,
    TBodySelector>(
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
    ImmutableArray<TParameter> Parameters,
    string? DocumentationId,
    string StableSelector,
    string AnchorDigest,
    string CanonicalSignature,
    string AnchorTypeFullName,
    string? DeclaringTypeDefinitionId,
    string GraphSelectorKey,
    ImmutableArray<TBodySelector> BodySelectors);

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

    public static ApiMemberSurfacePresentation<TParameter, TBodySelector>
        Member<TParameter, TBodySelector>(
        ApiType type,
        ApiMember member,
        Func<ApiParameterSurfacePresentation, TParameter>
            parameterProjection,
        Func<ApiMemberBodySelectorPresentation, TBodySelector>
            bodySelectorProjection)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(parameterProjection);
        ArgumentNullException.ThrowIfNull(bodySelectorProjection);

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
                    parameter =>
                        parameterProjection(
                            new ApiParameterSurfacePresentation(
                            parameter.Name,
                            parameter.Type,
                            parameter.Modifier,
                            parameter.HasDefault,
                            parameter.DefaultValueText,
                            Description: null))),
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
                    .Select(selector =>
                        bodySelectorProjection(
                            new ApiMemberBodySelectorPresentation(
                                selector.BodyToken,
                                selector.MemberName,
                                selector.SelectorKey))),
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
