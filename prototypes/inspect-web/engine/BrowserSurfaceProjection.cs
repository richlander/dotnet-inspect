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

    internal sealed record Participant(
        AssemblyContextParticipant Context,
        string Assembly,
        string Id,
        string Asset);

    internal sealed record Surface(
        BrowserAssemblySurface[] Assemblies,
        BrowserTypeSurface[] Types,
        BrowserAccessibilityDescriptor[] Accessibility,
        int TotalMembers,
        string[] InspectionErrors,
        string? InspectionError,
        bool IsTruncated);

    internal static Surface Project(
        AssemblyContextApiSurfaceResult surfaces,
        IReadOnlyList<Participant> requested,
        bool qualifyTypeIds = false,
        string? platformPack = null)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(requested);
        if (surfaces.Assemblies.Assemblies.Length > requested.Count)
        {
            throw new InvalidOperationException(
                "The API surface query returned more entries than the workspace selected "
                + "participants, so per-assembly attribution cannot be trusted.");
        }

        var assemblies = new List<BrowserAssemblySurface>();
        var types = new List<BrowserTypeSurface>();
        HashSet<TypeCollisionKey> duplicateTypeKeys =
        [
            .. surfaces.Assemblies.Assemblies
                .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
                .SelectMany(entry => entry.Value.Surface.Types)
                .GroupBy(TypeCollisionKey.Create)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
        ];
        var transportTextBudget =
            new BrowserSurfaceTextBudget(
                BrowserApiSurfacePolicy.MaxRetainedTextCharacters);
        string? transportTruncation = null;
        int noticeEntryCount = surfaces.Assemblies.Assemblies.Length;
        for (int index = 0;
            index < surfaces.Assemblies.Assemblies.Length;
            index++)
        {
            if (surfaces.Assemblies.Assemblies[index]
                is not AssemblyContextEntry<AssemblyApiSurface>.Available available)
            {
                continue;
            }

            Participant participant = requested[index];
            if (!ReferenceEquals(
                    available.Subject.Registration,
                    participant.Context.Assembly.Registration))
            {
                throw new InvalidOperationException(
                    "The API surface query's entry order does not match the workspace's "
                    + "participant order, so per-assembly attribution cannot be trusted.");
            }

            BrowserTypeSurface[] assemblyTypes;
            transportTextBudget.BeginParticipant();
            try
            {
                assemblyTypes =
                [
                    .. available.Value.Surface.Types
                        .Select(type => Type(
                            type,
                            participant.Assembly,
                            participant.Id,
                            participant.Context.Assembly.Identity.Name,
                            transportTextBudget,
                            qualifyTypeIds
                            || duplicateTypeKeys.Contains(
                                TypeCollisionKey.Create(type)),
                            platformPack)),
                ];
                transportTextBudget.CommitParticipant();
            }
            catch (BrowserSurfaceTextBoundExceededException)
            {
                transportTextBudget.AbandonParticipant();
                transportTruncation =
                    BrowserApiSurfacePolicy.TransportTruncationNotice(
                        assemblies.Count,
                        requested.Count - index,
                        transportTextBudget.CommittedCharacters);
                noticeEntryCount = index;
                break;
            }

            BrowserTypeSurface[] publicTypes =
            [
                .. assemblyTypes.Where(type =>
                    IsDefaultBucket(surfaces, type)),
            ];
            AssemblyReferenceIdentity identity =
                participant.Context.Assembly.Identity;
            assemblies.Add(new BrowserAssemblySurface(
                participant.Id,
                identity.Name,
                identity.Version?.ToString() ?? "",
                identity.Culture,
                identity.PublicKeyToken,
                participant.Asset,
                publicTypes.Length,
                publicTypes.Sum(type => type.Members),
                platformPack));
            types.AddRange(assemblyTypes);
        }

        string? extractionTruncation =
            BrowserApiSurfacePolicy.TruncationNotice(surfaces.Truncation);
        string? truncation = (extractionTruncation, transportTruncation) switch
        {
            (null, null) => null,
            ({ } only, null) => only,
            (null, { } only) => only,
            var (left, right) => $"{left}; {right}",
        };
        string[] noticeEntries = NoticeEntries(
            [.. surfaces.Assemblies.Assemblies.Take(noticeEntryCount)],
            truncation);
        string? notice = Notice(noticeEntries);
        BrowserTypeSurface[] identified =
        [
            .. types
                .OrderBy(type => type.Namespace, StringComparer.Ordinal)
                .ThenBy(type => type.Name, StringComparer.Ordinal),
        ];
        return new Surface(
            [.. assemblies],
            identified,
            [.. surfaces.Accessibility.Select(Descriptor)],
            identified
                .Where(type => IsDefaultBucket(surfaces, type))
                .Sum(type => type.Members),
            noticeEntries,
            notice,
            truncation is not null);
    }

    internal static BrowserTypeSurface Type(
        ApiType type,
        string assembly,
        string assemblyId,
        string assemblyName,
        BrowserSurfaceTextBudget? textBudget = null,
        bool qualifyId = false,
        string? platformPack = null,
        IEnumerable<ApiMember>? selectedMembers = null)
    {
        textBudget?.EnsureCanProject(
            type,
            qualifyId ? assembly.Length + 1 : 0);
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
            .. (selectedMembers ?? type.Members)
                .Select(member => Member(type, member, textBudget)),
        ];
        string metadataId = MetadataId(type);
        string definitionId = type.DefinitionName?.ToEscapedFullName() ?? metadataId;
        string id = qualifyId ? $"{assembly}:{definitionId}" : definitionId;
        var projected = new BrowserTypeSurface(
            id,
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
            members,
            platformPack);
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
            member.Kind switch
            {
                "explicit-interface-implementation" => "private",
                "finalizer" => "protected",
                _ => member.Accessibility ?? "public",
            },
            member.IsStatic,
            member.IsUnsafe,
            member.IsVirtual,
            member.IsAbstract,
            member.IsOverride,
            member.IsExtension,
            member.IsObsolete,
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

    static bool IsDefaultBucket(
        AssemblyContextApiSurfaceResult surfaces,
        BrowserTypeSurface type) =>
        surfaces.Accessibility.Any(
            bucket => bucket.IsDefault
                && bucket.Id.Equals(
                    type.AccessibilityId,
                    StringComparison.Ordinal));

    readonly record struct TypeCollisionKey(
        MetadataTypeDefinitionName? DefinitionName,
        string Namespace,
        string MetadataName)
    {
        internal static TypeCollisionKey Create(ApiType type) =>
            type.DefinitionName is { } definitionName
                ? new(definitionName, "", "")
                : new(
                    null,
                    type.Namespace ?? "",
                    type.MetadataName ?? type.Name);
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

        internal void EnsureCanProject(ApiType type, int qualifiedIdPrefixLength = 0)
        {
            long identity = TextLength(type.Namespace)
                + TextLength(type.Name)
                + TextLength(type.MetadataName)
                + DefinitionNameLength(type.DefinitionName)
                + qualifiedIdPrefixLength;
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
            Retain(member.Accessibility);
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
            if (estimatedCharacters > MaxCharacters - (long)_committed - _pending)
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
    /// rejected or failed participant remains visible without carrying artifact-authored fields.
    /// </summary>
    internal static string? Failure<TValue>(AssemblyContextEntry<TValue> entry) => entry switch
    {
        AssemblyContextEntry<TValue>.Rejected rejected =>
            RejectedAssembly(rejected.Failure),
        AssemblyContextEntry<TValue>.Failed failed =>
            FailedAssembly(failed.Error),
        _ => null,
    };

    /// <summary>
    /// Participant failures plus partial API extraction from otherwise available participants.
    /// The latter is summarized without echoing artifact-authored metadata into the notice.
    /// </summary>
    internal static string[] ApiSurfaceFailureEntries(
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
                    PartialApiSurface(
                        available.Value.InspectionFailures.Length));
            }
        }

        return [.. failures];
    }

    internal static string RejectedAssembly(CandidateOpenFailure failure) =>
        $"Assembly unavailable: {failure.Kind}.";

    internal static string FailedAssembly(Exception error) =>
        $"Assembly inspection failed ({error.GetType().Name}).";

    internal static string PartialApiSurface(int omittedRows) =>
        $"An assembly API surface omitted {omittedRows} metadata row(s).";

    /// <summary>
    /// The response's structured notice entries: participant failures, partial extraction, and an
    /// explicit bounded-projection truncation. Keeping these boundaries beside the rendered
    /// notice lets cumulative consumers deduplicate whole entries without parsing their text.
    /// <c>BrowserEngineBoundaryTests.QueryPackage_FirstTransportTruncationReturnsTypedNotice</c>
    /// verifies that the transport emits both forms consistently.
    /// </summary>
    internal static string[] NoticeEntries(
        ImmutableArray<AssemblyContextEntry<AssemblyApiSurface>> entries,
        string? truncation)
    {
        string[] failures = ApiSurfaceFailureEntries(entries);
        return truncation is null ? failures : [.. failures, truncation];
    }

    internal static string? Notice(IEnumerable<string> entries)
    {
        string[] notices = [.. entries];
        return notices.Length == 0 ? null : string.Join("; ", notices);
    }

    internal static string? Notice(
        ImmutableArray<AssemblyContextEntry<AssemblyApiSurface>> entries,
        string? truncation) =>
        Notice(NoticeEntries(entries, truncation));

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
