using System.Collections.Immutable;
using System.Runtime.Versioning;
using CSharpText;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Analysis = ILInspector.Analysis;

namespace InspectWeb.Engine;

/// <summary>
/// Adapts the typed models a product query returned into the browser's transport records. This is
/// pure projection: it classifies nothing, orders no label, derives no identity, and reads no
/// artifact. Every identity it carries — the member anchor, the canonical signature, the
/// call-graph selector, the accessibility bucket — was produced by the product.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserSurfaceProjection
{
    internal static BrowserAccessibilityDescriptor Descriptor(ApiAccessibilityBucket bucket) =>
        new(bucket.Id, bucket.Label, bucket.Order, bucket.IsDefault, bucket.Count);

    internal static BrowserTypeSurface Type(
        ApiType type,
        string assembly,
        string assemblyId,
        string assemblyName,
        BrowserSurfaceTextBudget? textBudget = null)
    {
        textBudget?.EnsureCanProject(type);
        // C#-spelled name for display (List<T>, Dictionary<TKey, TValue>) using the real generic
        // parameter names the surface carries. Identity stays the metadata form so deep links,
        // search, and tab matching remain stable.
        string displayName = MetadataTypeNameFormatter.FormatGenericTypeName(
            type.Name,
            type.TypeParameters);
        ApiAccessibilityBucket bucket = ApiAccessibility.Classify(type.Accessibility);
        string accessibility = string.IsNullOrWhiteSpace(type.Accessibility)
            ? bucket.Label
            : type.Accessibility;

        var modifiers = new List<string> { accessibility };
        modifiers.AddRange(ILInspector.Research.ResearchViews.TypeModifiers(type));
        modifiers.Add(type.Kind);
        modifiers.Add(displayName);

        BrowserMemberSurface[] members =
        [
            .. type.Members.Select(member => Member(type, member, textBudget)),
        ];
        string metadataId = MetadataId(type);
        string definitionId = type.DefinitionName?.ToEscapedFullName() ?? metadataId;
        var projected = new BrowserTypeSurface(
            definitionId,
            definitionId,
            type.FullName,
            metadataId,
            type.Name,
            displayName,
            type.Namespace ?? "",
            string.Join(' ', modifiers.Skip(1).SkipLast(1)),
            accessibility,
            bucket.Id,
            assembly,
            assemblyId,
            assemblyName,
            members.Length,
            string.Join(' ', modifiers),
            members);
        textBudget?.Retain(projected);
        return projected;
    }

    internal static BrowserMemberSurface Member(
        ApiType type,
        ApiMember member,
        BrowserSurfaceTextBudget? textBudget = null)
    {
        textBudget?.EnsureCanProject(type, member);
        MemberAnchor anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
        var projected = new BrowserMemberSurface(
            member.Name,
            member.Kind,
            member.Signature ?? member.Name,
            member.SignatureModel?.TypeParameters.Count ?? 0,
            member.MetadataToken,
            member.SignatureModel?.ReturnType ?? member.ReturnType,
            [
                .. (member.SignatureModel?.Parameters ?? []).Select(
                    parameter => new BrowserParameterSurface(
                        parameter.Name,
                        parameter.Type,
                        parameter.Modifier,
                        parameter.HasDefault,
                        parameter.DefaultValueText,
                        null)),
            ],
            DocumentationId(type, member),
            null,
            null,
            [],
            anchor.StableSelector,
            anchor.Fingerprint,
            anchor.CanonicalSignature,
            Analysis.CallGraphMemberResolver.CreateSelector(type, member).Key,
            [
                .. Analysis.CallGraphMemberResolver.CreateBodySelectors(type, member)
                    .Select(selector => new BrowserMemberBodySelector(
                        selector.BodyToken,
                        selector.MemberName,
                        selector.SelectorKey)),
            ]);
        textBudget?.Retain(projected);
        return projected;
    }

    /// <summary>
    /// The Browser transport's shared text budget. A participant spends pending text while its
    /// transport records are built and commits only after every type succeeds, so an over-budget
    /// assembly is omitted whole.
    /// </summary>
    [SupportedOSPlatform("browser")]
    internal sealed class BrowserSurfaceTextBudget(int maxCharacters)
    {
        int _committed;
        int _pending;

        internal int MaxCharacters { get; } = maxCharacters > 0
            ? maxCharacters
            : throw new ArgumentOutOfRangeException(nameof(maxCharacters));

        internal int CommittedCharacters => _committed;

        internal void BeginParticipant() => _pending = 0;

        internal void CommitParticipant()
        {
            _committed += _pending;
            _pending = 0;
        }

        internal void AbandonParticipant() => _pending = 0;

        internal void EnsureCanProject(ApiType type)
        {
            long identity = TextLength(type.Namespace)
                + TextLength(type.Name)
                + TextLength(type.MetadataName)
                + DefinitionNameLength(type.DefinitionName);
            foreach (TypeParameter parameter in type.TypeParameters)
                identity += TextLength(parameter.Name);
            EnsureCanMaterialize(identity * 12 + 256);
        }

        internal void EnsureCanProject(ApiType type, ApiMember member)
        {
            long identity = TextLength(type.Namespace)
                + TextLength(type.Name)
                + TextLength(type.MetadataName)
                + DefinitionNameLength(type.DefinitionName);
            long memberText = TextLength(member.Name)
                + TextLength(member.Signature)
                + TextLength(member.ReturnType);
            if (member.SignatureModel is { } signature)
            {
                memberText += TextLength(signature.ReturnType)
                    + TextLength(signature.CanonicalReturnType)
                    + TextLength(signature.MemberName);
                foreach (ApiParameter parameter in signature.Parameters)
                {
                    memberText += TextLength(parameter.Name)
                        + TextLength(parameter.Type)
                        + TextLength(parameter.CanonicalType)
                        + TextLength(parameter.Modifier)
                        + TextLength(parameter.DefaultValueText);
                }
            }
            EnsureCanMaterialize(identity * 16 + memberText * 8 + 512);
        }

        internal void Retain(BrowserTypeSurface type)
        {
            Retain(type.Id);
            Retain(type.DefinitionId);
            Retain(type.QueryId);
            Retain(type.MetadataId);
            Retain(type.Name);
            Retain(type.DisplayName);
            Retain(type.Namespace);
            Retain(type.Kind);
            Retain(type.Accessibility);
            Retain(type.AccessibilityId);
            Retain(type.Assembly);
            Retain(type.AssemblyId);
            Retain(type.AssemblyName);
            Retain(type.Signature);
        }

        internal void Retain(BrowserMemberSurface member)
        {
            Retain(member.Name);
            Retain(member.Kind);
            Retain(member.Signature);
            Retain(member.ReturnType);
            Retain(member.DocumentationId);
            Retain(member.Summary);
            Retain(member.Returns);
            Retain(member.StableSelector);
            Retain(member.AnchorDigest);
            Retain(member.CanonicalSignature);
            Retain(member.GraphSelectorKey);
            foreach (BrowserParameterSurface parameter in member.Parameters)
            {
                Retain(parameter.Name);
                Retain(parameter.Type);
                Retain(parameter.Modifier);
                Retain(parameter.DefaultValue);
                Retain(parameter.Description);
            }
            foreach (BrowserExceptionSurface exception in member.Exceptions)
            {
                Retain(exception.Type);
                Retain(exception.Description);
            }
            foreach (BrowserMemberBodySelector selector in member.BodySelectors)
            {
                Retain(selector.MemberName);
                Retain(selector.SelectorKey);
            }
        }

        void Retain(string? text)
        {
            if (text is null)
                return;
            if (text.Length > MaxCharacters - _committed - _pending)
                throw new BrowserSurfaceTextBoundExceededException();
            _pending += text.Length;
        }

        void EnsureCanMaterialize(long estimatedCharacters)
        {
            if (estimatedCharacters > MaxCharacters)
                throw new BrowserSurfaceTextBoundExceededException();
        }

        static int TextLength(string? text) => text?.Length ?? 0;

        static long DefinitionNameLength(MetadataTypeDefinitionName? name)
        {
            if (name is null)
                return 0;
            long length = TextLength(name.Namespace);
            foreach (string segment in name.Segments)
                length += segment.Length;
            return length;
        }
    }

    internal sealed class BrowserSurfaceTextBoundExceededException()
        : Exception("The Browser API-surface transport exceeded its text budget.")
    {
    }

    /// <summary>
    /// The exact metadata lookup name: nested types keep the <c>+</c> delimiter, which the
    /// display <c>FullName</c> does not.
    /// </summary>
    internal static string MetadataId(ApiType type)
    {
        string name = type.MetadataName ?? type.Name;
        return string.IsNullOrEmpty(type.Namespace) ? name : $"{type.Namespace}.{name}";
    }

    internal static string? DocumentationId(ApiType type, ApiMember member)
    {
        if (!ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out XmlDocMemberIdentity identity))
            return null;

        string key = identity.LookupKey;
        if (identity.NormalizedParameters.Count > 0)
            key += $"({string.Join(",", identity.NormalizedParameters)})";
        if (identity.NormalizedReturnType is { Length: > 0 } returnType)
            key += $"~{returnType}";
        return key;
    }

    /// <summary>
    /// The failure text for one participant outcome, or null when the participant projected. A
    /// rejected or failed participant is named beside the results rather than dropped.
    /// </summary>
    internal static string? Failure<TValue>(AssemblyContextEntry<TValue> entry) => entry switch
    {
        AssemblyContextEntry<TValue>.Rejected rejected =>
            $"{rejected.Subject.Identity.Name}: {rejected.Failure.Kind} ({rejected.Failure.Detail})",
        AssemblyContextEntry<TValue>.Failed failed =>
            $"{failed.Subject.Identity.Name}: {failed.Error.Message}",
        _ => null,
    };

    /// <summary>
    /// Participant failures plus partial API extraction from otherwise available participants.
    /// The latter is summarized without echoing artifact-authored metadata into the notice.
    /// </summary>
    internal static string? ApiSurfaceFailures(
        ImmutableArray<AssemblyContextEntry<AssemblyApiSurface>> entries)
    {
        var failures = new List<string>();
        foreach (AssemblyContextEntry<AssemblyApiSurface> entry in entries)
        {
            if (Failure(entry) is { } failure)
                failures.Add(failure);
            if (entry is AssemblyContextEntry<AssemblyApiSurface>.Available available
                && available.Value.InspectionFailures.Length > 0)
            {
                failures.Add(
                    $"{available.Subject.Identity.Name}: API surface omitted "
                    + $"{available.Value.InspectionFailures.Length} metadata row(s).");
            }
        }

        return failures.Count == 0 ? null : string.Join("; ", failures);
    }

    /// <summary>
    /// The response's visible notice: participant failures, partial extraction, and an explicit
    /// bounded-projection truncation, or null when there is nothing to report. A truncation is
    /// carried beside the failures rather than instead of them, so a bounded response never reads
    /// as a complete one.
    /// </summary>
    internal static string? Notice(
        ImmutableArray<AssemblyContextEntry<AssemblyApiSurface>> entries,
        string? truncation)
    {
        string? failures = ApiSurfaceFailures(entries);
        return (failures, truncation) switch
        {
            (null, null) => null,
            (null, { } only) => only,
            ({ } only, null) => only,
            var (left, right) => $"{left}; {right}",
        };
    }

    /// <summary>
    /// The value an available entry produced, or a visible failure naming why the participant
    /// could not answer. A participant-scoped export has exactly one answer, so a rejection is an
    /// error rather than a row beside other rows.
    /// </summary>
    internal static TValue Require<TValue>(AssemblyContextEntry<TValue> entry, string operation)
        => entry is AssemblyContextEntry<TValue>.Available available
            ? available.Value
            : throw new InvalidOperationException(
                $"{operation} failed: {Failure(entry)}");
}
