using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// A single member match produced by <see cref="MemberSearch"/>. The shape is
/// presentation-independent and free of provenance beyond the assembly file name:
/// callers attach package/source/version from their own resolution layer (e.g. the
/// <c>AssemblySet</c> entry that supplied the path), mirroring how type search leaves
/// <c>Source</c>/<c>SourceVersion</c> for the orchestrator to fill in.
/// </summary>
public sealed record MemberSearchResult
{
    /// <summary>The input pattern that matched this member.</summary>
    public required string Pattern { get; init; }

    /// <summary>The input ordinal of <see cref="Pattern"/>.</summary>
    public int PatternOrdinal { get; init; }

    /// <summary>The member's metadata name (e.g. <c>Parse</c>, <c>op_Addition</c>, <c>Item</c>).</summary>
    public required string MemberName { get; init; }

    /// <summary>The exact structured identity of the declaring Type.</summary>
    public required MetadataTypeDefinitionName DeclaringTypeName { get; init; }

    /// <summary>The producer-issued durable identity of this member.</summary>
    public required MemberAnchor Anchor { get; init; }

    /// <summary>The declaring Type's producer order within the assembly surface.</summary>
    public int DeclarationOrder { get; init; }

    /// <summary>The member's producer order within its declaring Type.</summary>
    public int MemberOrder { get; init; }

    /// <summary>Full name of the declaring type (<c>Namespace.Type</c>, or <c>Type</c> when global).</summary>
    public required string DeclaringType { get; init; }

    /// <summary>Namespace of the declaring type, when it has one.</summary>
    public string? DeclaringNamespace { get; init; }

    /// <summary>Member kind: method, property, field, event, constructor, operator, etc.</summary>
    public required string Kind { get; init; }

    /// <summary>Display signature of the member, when the API surface captured one.</summary>
    public string? Signature { get; init; }

    /// <summary>Return/field/property type, when applicable.</summary>
    public string? ReturnType { get; init; }

    /// <summary>Durable 10-char overload digest, when the surface projected member identity.</summary>
    public string? Digest { get; init; }

    /// <summary>
    /// The declaring assembly name supplied by the caller. Path-based searches
    /// use the file name without extension; content-backed callers may use the
    /// assembly identity.
    /// </summary>
    public required string Assembly { get; init; }

    /// <summary>True when <see cref="Pattern"/> was a glob (contained <c>*</c> or <c>?</c>).</summary>
    public bool IsGlob { get; init; }
}

/// <summary>
/// Result of a <see cref="MemberSearch.Search(System.Collections.Generic.IEnumerable{string},
/// System.Collections.Generic.IReadOnlyList{string}, bool, int?)"/> call: the matches and the
/// assembly paths from which no API surface could be read. Skipped paths are reported rather than
/// silently dropped so an all-unreadable set cannot masquerade as a clean "no matches" success.
/// </summary>
public sealed record MemberSearchOutcome(
    IReadOnlyList<MemberSearchResult> Results,
    IReadOnlyList<string> SkippedAssemblies);

/// <summary>
/// One-based inclusive accepted-match positions retained by Member search.
/// </summary>
public sealed record MemberSearchWindow
{
    public MemberSearchWindow(
        int start,
        int? end,
        bool materializeRows = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(start);
        if (end is int finiteEnd && finiteEnd < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                finiteEnd,
                "The member-search Window end must not precede its start.");
        }
        if (materializeRows && end is null)
        {
            throw new ArgumentException(
                "A row-materializing Member search requires a finite end.",
                nameof(end));
        }

        Start = start;
        End = end;
        MaterializeRows = materializeRows;
    }

    public int Start { get; }
    public int? End { get; }
    public bool MaterializeRows { get; }
}

/// <summary>Evidence for work performed by one metadata-native search.</summary>
public sealed record MemberSearchReceipt(
    int TypeDefinitionsVisited,
    int MemberCandidatesVisited,
    int MemberNamesDecoded,
    int AcceptedMatches,
    int ProjectedRows);

/// <summary>
/// Retained Member projections, accepted-row position, and scan evidence.
/// </summary>
public sealed record MemberSearchWindowResult(
    ImmutableArray<MemberSearchResult> Results,
    int AcceptedCount,
    bool EndReached,
    ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures,
    MemberSearchReceipt Receipt);

/// <summary>
/// Closed-set member search: given a finite set of already-resolved assembly paths, find members
/// whose name matches one or more patterns. This is the metadata-layer, offline "operate within a
/// set" counterpart to type search — it reads local assemblies via
/// <see cref="AssemblyReader.ExtractApiSurface(string, bool, bool)"/> and matches with the shared
/// <see cref="TypeMatcher"/> name semantics (direct/case-insensitive or glob). It performs no
/// package resolution or network access; populating the set is a separate, higher-layer concern.
/// </summary>
public static class MemberSearch
{
    /// <summary>
    /// Searches the members of every assembly in <paramref name="assemblyPaths"/> for names matching
    /// any pattern in <paramref name="patterns"/>. A member is emitted once per pattern it matches.
    /// </summary>
    /// <param name="assemblyPaths">The closed set of assembly file paths to search.</param>
    /// <param name="patterns">Member-name patterns. Direct names match case-insensitively and
    /// <c>this[]</c> matches indexer metadata names; patterns containing <c>*</c> or <c>?</c> are
    /// treated as globs.</param>
    /// <param name="includeAll">When true, non-public members are included; otherwise public only.</param>
    /// <param name="limit">Optional cap on the number of results collected across the whole set.</param>
    /// <remarks>
    /// An assembly with no managed metadata is recorded in
    /// <see cref="MemberSearchOutcome.SkippedAssemblies"/>. A file the admission contract refuses
    /// is not skipped: the typed mechanism propagates, because a rejected input is not the same
    /// answer as one that simply has no matching members.
    /// </remarks>
    public static MemberSearchOutcome Search(
        IEnumerable<string> assemblyPaths,
        IReadOnlyList<string> patterns,
        bool includeAll = false,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        ArgumentNullException.ThrowIfNull(patterns);

        var results = new List<MemberSearchResult>();
        var skipped = new List<string>();

        if (patterns.Count == 0)
            return new MemberSearchOutcome(results, skipped);

        foreach (var path in assemblyPaths)
        {
            if (limit is int cap && results.Count >= cap)
                break;

            var surface = AssemblyReader.ExtractApiSurface(path, includeAll, typesOnly: false);
            if (surface is null)
            {
                skipped.Add(path);
                continue;
            }

            CollectFromSurface(
                surface,
                Path.GetFileNameWithoutExtension(path),
                patterns,
                limit,
                results);
        }

        return new MemberSearchOutcome(results, skipped);
    }

    /// <summary>
    /// Searches a single assembly's members. Returns an empty list when the assembly has no managed
    /// metadata, which is not a failure. A file the admission contract refuses is a different case:
    /// <see cref="UnsupportedMetadataFormatException"/> and
    /// <see cref="MalformedMetadataRootException"/> propagate rather than being reported as an
    /// empty result, so a rejected input cannot be mistaken for one with no matches.
    /// </summary>
    public static List<MemberSearchResult> SearchAssembly(
        string assemblyPath,
        IReadOnlyList<string> patterns,
        bool includeAll = false)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var results = new List<MemberSearchResult>();
        if (patterns.Count == 0)
            return results;

        var surface = AssemblyReader.ExtractApiSurface(assemblyPath, includeAll, typesOnly: false);
        if (surface is not null)
            CollectFromSurface(
                surface,
                Path.GetFileNameWithoutExtension(assemblyPath),
                patterns,
                limit: null,
                results);

        return results;
    }

    /// <summary>
    /// Searches an already-produced API surface without reopening its assembly.
    /// </summary>
    public static IReadOnlyList<MemberSearchResult> Search(
        ApiSurface surface,
        string assemblyName,
        IReadOnlyList<string> patterns,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(patterns);

        var results = new List<MemberSearchResult>();
        CollectFromSurface(surface, assemblyName, patterns, limit, results);
        return results;
    }

    /// <summary>
    /// Scans admitted metadata declarations in the established Type/member
    /// order, retaining only requested accepted positions. Before
    /// <see cref="MemberSearchWindow.Start"/>, only visibility, hidden-state,
    /// accessor folding, name matching, and the optional declaring-Type
    /// predicate are evaluated.
    /// </summary>
    internal static MemberSearchWindowResult SearchWindow(
        PEReader peReader,
        string assemblyName,
        IReadOnlyList<string> patterns,
        bool includeAll,
        MemberSearchWindow window,
        Func<MetadataTypeDefinitionName, bool>? declaringTypeMatches = null)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(window);

        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        AttachedExtensionMap attachedExtensions =
            AttachedExtensionMap.Build(
                reader,
                includeAll);
        var results =
            ImmutableArray.CreateBuilder<MemberSearchResult>();
        var failures =
            ImmutableArray.CreateBuilder<ApiSurfaceInspectionFailure>();
        int acceptedCount = 0;
        int declarationOrder = 0;
        int typeDefinitionsVisited = 0;
        int memberCandidatesVisited = 0;
        int memberNamesDecoded = 0;
        int projectedRows = 0;
        bool endReached = false;

        foreach (TypeDefinitionHandle typeHandle
            in reader.TypeDefinitions)
        {
            if (endReached)
                break;

            typeDefinitionsVisited =
                checked(typeDefinitionsVisited + 1);
            try
            {
                TypeDefinition type =
                    reader.GetTypeDefinition(typeHandle);
                string metadataName =
                    reader.GetString(type.Name);
                if (TypeFilters.IsCompilerGenerated(metadataName)
                    || !includeAll && !type.IsPublic
                    || !includeAll
                        && AttributeReader.HasHiddenAttribute(
                            reader,
                            type.GetCustomAttributes()))
                {
                    continue;
                }

                MetadataTypeDefinitionNameReadResult typeName =
                    MetadataTypeDefinitionNameReader.Read(
                        reader,
                        typeHandle);
                if (typeName is
                    MetadataTypeDefinitionNameReadResult.Rejected
                        rejected)
                {
                    failures.Add(
                        InspectionFailure(
                            "type identity",
                            typeHandle,
                            rejected.Failure));
                    continue;
                }
                MetadataTypeDefinitionName declaringType =
                    typeName is
                        MetadataTypeDefinitionNameReadResult.Read read
                        ? read.Name
                        : throw new InvalidOperationException(
                            "Unknown Type-definition name result.");
                bool admitsDeclaringType =
                    declaringTypeMatches?.Invoke(declaringType)
                    ?? true;
                bool extensionContainer =
                    (type.Attributes
                        & (TypeAttributes.Sealed
                            | TypeAttributes.Abstract))
                    == (TypeAttributes.Sealed
                        | TypeAttributes.Abstract)
                    && AttributeReader.HasExtensionAttribute(
                        reader,
                        type.GetCustomAttributes());
                var sink = new SearchSink(
                    reader,
                    typeHandle,
                    type,
                    declaringType,
                    assemblyName,
                    patterns,
                    includeAll,
                    admitsDeclaringType,
                    window,
                    declarationOrder,
                    acceptedCount,
                    results,
                    memberCandidatesVisited,
                    memberNamesDecoded,
                    projectedRows);
                ApiSurfaceExtractor.ClassifyDeclaredMembers(
                    reader,
                    typeHandle,
                    type,
                    MetadataMemberSpelling.CSharp,
                    publicOnly: !includeAll,
                    extensionContainer,
                    classifyLogicalMethodKinds: true,
                    ref sink);
                if (!sink.EndReached
                    && attachedExtensions.TryGet(
                        typeHandle,
                        out IReadOnlyList<AttachedExtensionCandidate>
                            attached))
                {
                    foreach (AttachedExtensionCandidate candidate
                        in attached)
                    {
                        TypeDefinition extensionDeclaringType =
                            reader.GetTypeDefinition(
                                candidate.DeclaringType);
                        sink.AddAttached(
                            candidate.Member,
                            candidate.DeclaringType,
                            extensionDeclaringType);
                        if (sink.EndReached)
                            break;
                    }
                }
                acceptedCount = sink.AcceptedCount;
                memberCandidatesVisited =
                    sink.MemberCandidatesVisited;
                memberNamesDecoded = sink.MemberNamesDecoded;
                projectedRows = sink.ProjectedRows;
                endReached = sink.EndReached;
                declarationOrder =
                    checked(declarationOrder + 1);
            }
            catch (Exception exception) when (
                exception is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                failures.Add(
                    InspectionFailure(
                        "type row",
                        typeHandle,
                        MetadataTypeNameFailure.Malformed(
                            typeHandle,
                            exception.Message)));
            }
        }

        return new(
            results.DrainToImmutable(),
            acceptedCount,
            endReached,
            failures.DrainToImmutable(),
            new(
                typeDefinitionsVisited,
                memberCandidatesVisited,
                memberNamesDecoded,
                acceptedCount,
                projectedRows));
    }

    private static void CollectFromSurface(
        ApiSurface surface,
        string assemblyName,
        IReadOnlyList<string> patterns,
        int? limit,
        List<MemberSearchResult> results)
    {
        for (int declarationOrder = 0;
            declarationOrder < surface.Types.Count;
            declarationOrder++)
        {
            ApiType type = surface.Types[declarationOrder];
            MetadataTypeDefinitionName declaringTypeName =
                type.DefinitionName
                ?? throw new InvalidOperationException(
                    $"Member search cannot identify declaring Type "
                    + $"'{type.FullName}'.");
            for (int memberOrder = 0;
                memberOrder < type.Members.Count;
                memberOrder++)
            {
                ApiMember member = type.Members[memberOrder];
                MemberAnchor? anchor = null;
                for (int patternOrdinal = 0;
                    patternOrdinal < patterns.Count;
                    patternOrdinal++)
                {
                    if (limit is int cap && results.Count >= cap)
                        return;

                    string pattern = patterns[patternOrdinal];
                    var isGlob = pattern.Contains('*') || pattern.Contains('?');
                    var matched = isGlob
                        ? TypeMatcher.MatchesGlob(member.Name, pattern)
                        : TypeMatcher.MatchesMemberName(member.Name, pattern);

                    if (!matched)
                        continue;

                    results.Add(new MemberSearchResult
                    {
                        Pattern = pattern,
                        PatternOrdinal = patternOrdinal,
                        MemberName = member.Name,
                        DeclaringTypeName = declaringTypeName,
                        Anchor = anchor ??=
                            ApiMemberIdentity.GetMemberAnchor(
                                type,
                                member),
                        DeclarationOrder = declarationOrder,
                        MemberOrder = memberOrder,
                        DeclaringType = type.FullName,
                        DeclaringNamespace = type.Namespace,
                        Kind = member.Kind,
                        Signature = member.Signature,
                        ReturnType = member.ReturnType,
                        Digest = member.Digest,
                        Assembly = assemblyName,
                        IsGlob = isGlob,
                    });
                }
            }
        }
    }

    private static ApiSurfaceInspectionFailure InspectionFailure(
                    string operation,
                    EntityHandle subject,
                    MetadataTypeNameFailure failure) =>
                    new(
                        operation,
                        failure.SubjectToken
                            ?? MetadataTokens.GetToken(subject),
                        failure.Mechanism,
                        failure.Kind,
                        failure.Detail);

    private sealed class AttachedExtensionMap
    {
        private readonly Dictionary<
            TypeDefinitionHandle,
            List<AttachedExtensionCandidate>> _byReceiver;

        private AttachedExtensionMap(
            Dictionary<
                TypeDefinitionHandle,
                List<AttachedExtensionCandidate>> byReceiver)
        {
            _byReceiver = byReceiver;
        }

        internal static AttachedExtensionMap Build(
            MetadataReader reader,
            bool includeAll)
        {
            var targets = new Dictionary<
                MetadataTypeDefinitionName,
                TypeDefinitionHandle>();
            var ambiguous =
                new HashSet<MetadataTypeDefinitionName>();
            foreach (TypeDefinitionHandle handle
                in reader.TypeDefinitions)
            {
                TypeDefinition type =
                    reader.GetTypeDefinition(handle);
                string metadataName =
                    reader.GetString(type.Name);
                if (TypeFilters.IsCompilerGenerated(
                        metadataName)
                    || !includeAll && !type.IsPublic
                    || !includeAll
                        && AttributeReader.HasHiddenAttribute(
                            reader,
                            type.GetCustomAttributes()))
                {
                    continue;
                }

                if (MetadataTypeDefinitionNameReader.Read(
                        reader,
                        handle)
                    is not
                        MetadataTypeDefinitionNameReadResult.Read
                            read
                    || ambiguous.Contains(read.Name))
                {
                    continue;
                }
                if (!targets.TryAdd(read.Name, handle))
                {
                    targets.Remove(read.Name);
                    ambiguous.Add(read.Name);
                }
            }

            var byReceiver = new Dictionary<
                TypeDefinitionHandle,
                List<AttachedExtensionCandidate>>();
            var sink = new AttachedExtensionSink(
                targets,
                byReceiver);
            ApiSurfaceExtractor.ClassifyAttachedExtensions(
                reader,
                publicOnly: !includeAll,
                ref sink);
            return new(byReceiver);
        }

        internal bool TryGet(
            TypeDefinitionHandle receiver,
            out IReadOnlyList<AttachedExtensionCandidate>
                candidates)
        {
            bool found =
                _byReceiver.TryGetValue(
                    receiver,
                    out List<AttachedExtensionCandidate>? rows);
            candidates = rows ?? [];
            return found;
        }
    }

    private readonly record struct AttachedExtensionCandidate(
        TypeDefinitionHandle DeclaringType,
        ClassifiedMember Member);

    private struct AttachedExtensionSink(
        IReadOnlyDictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle> targets,
        Dictionary<
            TypeDefinitionHandle,
            List<AttachedExtensionCandidate>> byReceiver)
        : IAttachedExtensionSink
    {
        private TypeDefinitionHandle _receiver;
        private TypeDefinitionHandle _declaringType;

        public bool Wants(
            in ExtensionReceiver receiver,
            TypeDefinitionHandle declaringType)
        {
            if (receiver.ReadName() is not { } name
                || !targets.TryGetValue(
                    name,
                    out _receiver)
                || _receiver == declaringType)
            {
                return false;
            }

            _declaringType = declaringType;
            return true;
        }

        public void Add(
            in ExtensionReceiver receiver,
            in ClassifiedMember member)
        {
            if (!byReceiver.TryGetValue(
                    _receiver,
                    out List<AttachedExtensionCandidate>?
                        candidates))
            {
                candidates = [];
                byReceiver.Add(_receiver, candidates);
            }
            candidates.Add(
                new(
                    _declaringType,
                    member));
        }
    }

    private struct SearchSink : IClassifiedMemberSink
    {
        private readonly MetadataReader _reader;
        private readonly TypeDefinitionHandle _typeHandle;
        private readonly TypeDefinition _type;
        private readonly MetadataTypeDefinitionName _declaringType;
        private readonly string _assemblyName;
        private readonly IReadOnlyList<string> _patterns;
        private readonly bool _includeAll;
        private readonly bool _admitsDeclaringType;
        private readonly MemberSearchWindow _window;
        private readonly int _declarationOrder;
        private readonly ImmutableArray<MemberSearchResult>.Builder
            _results;
        private int _memberOrder;
        private int _anchorWorkRemaining;

        internal SearchSink(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            TypeDefinition type,
            MetadataTypeDefinitionName declaringType,
            string assemblyName,
            IReadOnlyList<string> patterns,
            bool includeAll,
            bool admitsDeclaringType,
            MemberSearchWindow window,
            int declarationOrder,
            int acceptedCount,
            ImmutableArray<MemberSearchResult>.Builder results,
            int memberCandidatesVisited,
            int memberNamesDecoded,
            int projectedRows)
        {
            _reader = reader;
            _typeHandle = typeHandle;
            _type = type;
            _declaringType = declaringType;
            _assemblyName = assemblyName;
            _patterns = patterns;
            _includeAll = includeAll;
            _admitsDeclaringType = admitsDeclaringType;
            _window = window;
            _declarationOrder = declarationOrder;
            _results = results;
            _anchorWorkRemaining =
                MetadataSafetyPolicy.MaxClassificationScanWorkChars;
            AcceptedCount = acceptedCount;
            MemberCandidatesVisited = memberCandidatesVisited;
            MemberNamesDecoded = memberNamesDecoded;
            ProjectedRows = projectedRows;
        }

        internal int AcceptedCount { get; private set; }
        internal int MemberCandidatesVisited { get; private set; }
        internal int MemberNamesDecoded { get; private set; }
        internal int ProjectedRows { get; private set; }
        internal bool EndReached { get; private set; }

        public void Add(in ClassifiedMember member)
            => AddCore(
                member,
                _typeHandle,
                _type);

        internal void AddAttached(
            in ClassifiedMember member,
            TypeDefinitionHandle declaringTypeHandle,
            TypeDefinition declaringType)
            => AddCore(
                member,
                declaringTypeHandle,
                declaringType);

        private void AddCore(
            in ClassifiedMember member,
            TypeDefinitionHandle projectionTypeHandle,
            TypeDefinition projectionType)
        {
            if (EndReached)
                return;
            MemberCandidatesVisited =
                checked(MemberCandidatesVisited + 1);
            if (member.IsHidden && !_includeAll)
                return;

            int memberOrder = _memberOrder++;
            string memberName =
                ReadName(member);
            MemberNamesDecoded =
                checked(MemberNamesDecoded + 1);
            if (!_admitsDeclaringType)
                return;

            for (int patternOrdinal = 0;
                patternOrdinal < _patterns.Count;
                patternOrdinal++)
            {
                string pattern = _patterns[patternOrdinal];
                bool isGlob =
                    pattern.Contains('*')
                    || pattern.Contains('?');
                bool matched =
                    isGlob
                        ? TypeMatcher.MatchesGlob(
                            memberName,
                            pattern)
                        : TypeMatcher.MatchesMemberName(
                            memberName,
                            pattern);
                if (!matched)
                    continue;

                AcceptedCount = checked(AcceptedCount + 1);
                if (_window.MaterializeRows
                    && AcceptedCount >= _window.Start)
                {
                    _results.Add(
                        Project(
                            member,
                            memberName,
                            pattern,
                            patternOrdinal,
                            isGlob,
                            memberOrder,
                            projectionTypeHandle,
                            projectionType));
                    ProjectedRows =
                        checked(ProjectedRows + 1);
                }
                if (_window.End is int end
                    && AcceptedCount >= end)
                {
                    EndReached = true;
                    break;
                }
            }
        }

        private string ReadName(
            in ClassifiedMember member) =>
            member.Kind switch
            {
                ClassifiedMemberKind.Method
                    or ClassifiedMemberKind.Constructor
                    or ClassifiedMemberKind.Operator
                    or ClassifiedMemberKind.Finalizer
                    or ClassifiedMemberKind
                        .ExplicitInterfaceImplementation
                    or ClassifiedMemberKind.ExtensionMethod =>
                    _reader.GetString(
                        _reader.GetMethodDefinition(
                            (MethodDefinitionHandle)
                                member.Handle).Name),
                ClassifiedMemberKind.Property =>
                    _reader.GetString(
                        _reader.GetPropertyDefinition(
                            (PropertyDefinitionHandle)
                                member.Handle).Name),
                ClassifiedMemberKind.Field =>
                    _reader.GetString(
                        _reader.GetFieldDefinition(
                            (FieldDefinitionHandle)
                                member.Handle).Name),
                ClassifiedMemberKind.Event =>
                    _reader.GetString(
                        _reader.GetEventDefinition(
                            (EventDefinitionHandle)
                                member.Handle).Name),
                _ => throw new InvalidOperationException(
                    "Unknown classified Member kind."),
            };

        private MemberSearchResult Project(
            in ClassifiedMember member,
            string memberName,
            string pattern,
            int patternOrdinal,
            bool isGlob,
            int memberOrder,
            TypeDefinitionHandle projectionTypeHandle,
            TypeDefinition projectionType)
        {
            (MemberAnchor anchor, string? signature, string? returnType) =
                member.Handle.Kind switch
                {
                    HandleKind.MethodDefinition =>
                        ProjectMethod(
                            member,
                            memberName,
                            projectionTypeHandle,
                            projectionType),
                    HandleKind.PropertyDefinition =>
                        ProjectProperty(
                            member,
                            projectionTypeHandle,
                            projectionType),
                    HandleKind.FieldDefinition =>
                        ProjectField(
                            member,
                            projectionTypeHandle,
                            projectionType),
                    HandleKind.EventDefinition =>
                        ProjectEvent(
                            member,
                            projectionTypeHandle,
                            projectionType),
                    _ => throw new BadImageFormatException(
                        "A classified Member has an unsupported declaration handle."),
                };
            return new()
            {
                Pattern = pattern,
                PatternOrdinal = patternOrdinal,
                MemberName = memberName,
                DeclaringTypeName = _declaringType,
                Anchor = anchor,
                DeclarationOrder = _declarationOrder,
                MemberOrder = memberOrder,
                DeclaringType =
                    _declaringType.ToMetadataFullName(),
                DeclaringNamespace =
                    _declaringType.Namespace.Length == 0
                        ? null
                        : _declaringType.Namespace,
                Kind = Kind(member.Kind),
                Signature = signature,
                ReturnType = returnType,
                Digest = null,
                Assembly = _assemblyName,
                IsGlob = isGlob,
            };
        }

        private (
            MemberAnchor Anchor,
            string? Signature,
            string? ReturnType)
            ProjectMethod(
                in ClassifiedMember member,
                string memberName,
                TypeDefinitionHandle projectionTypeHandle,
                TypeDefinition projectionType)
        {
            var handle =
                (MethodDefinitionHandle)member.Handle;
            MethodDefinition method =
                _reader.GetMethodDefinition(handle);
            var signature =
                ApiSurfaceExtractor
                    .GetMethodSignatureForIdentity(
                        _reader,
                        GenericContext.ForType(
                            _reader,
                            projectionType),
                        handle,
                        method,
                        NullabilityReader
                            .GetTypeNullableContext(
                                _reader,
                                projectionTypeHandle),
                        captureExtensionReceiver:
                            member.Receiver
                            is MetadataMethodReceiver
                                .Extension);
            MetadataTypeDefinitionName projectionTypeName =
                MetadataTypeDefinitionNameReader.Read(
                    _reader,
                    projectionTypeHandle)
                is MetadataTypeDefinitionNameReadResult.Read
                    read
                    ? read.Name
                    : throw new BadImageFormatException(
                        "The Member declaring Type name "
                            + "could not be read.");
            ApiType receiverType =
                SearchType(
                    _reader,
                    _typeHandle,
                    _type,
                    _declaringType);
            ApiType physicalType =
                SearchType(
                    _reader,
                    projectionTypeHandle,
                    projectionType,
                    projectionTypeName);
            var projected = new ApiMember
            {
                Name = memberName,
                Kind = Kind(member.Kind),
                Signature = signature.Text,
                SignatureModel = signature.Model,
                SignatureDecodeStatus =
                    signature.IsDegraded
                        ? SignatureDecodeStatus.Degraded
                        : null,
                ReturnType =
                    ApiMemberIdentity.IsConversionOperator(
                        memberName)
                        ? signature.Model.ReturnType
                        : null,
                MetadataToken =
                    MetadataTokens.GetToken(handle),
                GenericArity =
                    method.GetGenericParameters().Count,
                IsExtension =
                    member.Receiver
                    is MetadataMethodReceiver.Extension,
                DeclaringType =
                    member.IsAttached
                        ? projectionTypeName
                            .ToMetadataFullName()
                        : null,
                DeclaringTypeCanonicalName =
                    member.IsAttached
                        ? ApiMemberIdentity
                            .FormatTypeAnchorName(
                                physicalType)
                        : null,
                DeclaringTypeDefinitionName =
                    member.IsAttached
                        ? projectionTypeName
                        : null,
            };
            return (
                ApiMemberIdentity.GetMemberAnchor(
                    receiverType,
                    projected),
                signature.Text,
                projected.ReturnType);
        }

        private static ApiType SearchType(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            TypeDefinition type,
            MetadataTypeDefinitionName name) =>
            new()
            {
                Namespace =
                    name.Namespace.Length == 0
                        ? null
                        : name.Namespace,
                Name =
                    string.Join(".", name.Segments),
                DefinitionName = name,
                IntroducedTypeParameterCounts =
                    MetadataDeclarationQuery
                        .GetIntroducedTypeParameterCounts(
                            reader,
                            handle),
                TypeParameters =
                    MetadataDeclarationQuery
                        .GetTypeParameters(
                            reader,
                            type)
                        .ToList(),
            };

        private (
            MemberAnchor Anchor,
            string? Signature,
            string? ReturnType)
            ProjectProperty(
                in ClassifiedMember member,
                TypeDefinitionHandle projectionTypeHandle,
                TypeDefinition projectionType)
        {
            var handle =
                (PropertyDefinitionHandle)member.Handle;
            PropertyDefinition property =
                _reader.GetPropertyDefinition(handle);
            var signature =
                ApiSurfaceExtractor
                    .GetPropertySignatureForIdentity(
                        _reader,
                        GenericContext.ForType(
                            _reader,
                            projectionType),
                        property,
                        property.GetAccessors(),
                        ApiSurfaceExtractor
                            .GetExplicitImplementationBodies(
                                _reader,
                                projectionType),
                        NullabilityReader
                            .GetTypeNullableContext(
                                _reader,
                                projectionTypeHandle));
            return (
                ApiMemberIdentity.CreatePropertyAnchor(
                    _reader,
                    projectionTypeHandle,
                    property,
                    ref _anchorWorkRemaining),
                signature.Text,
                null);
        }

        private (
            MemberAnchor Anchor,
            string? Signature,
            string? ReturnType)
            ProjectField(
                in ClassifiedMember member,
                TypeDefinitionHandle projectionTypeHandle,
                TypeDefinition projectionType)
        {
            var handle =
                (FieldDefinitionHandle)member.Handle;
            FieldDefinition field =
                _reader.GetFieldDefinition(handle);
            (string? fieldType, _) =
                ApiSurfaceExtractor
                    .GetFieldTypeForIdentity(
                        _reader,
                        projectionType,
                        field,
                        NullabilityReader
                            .GetTypeNullableContext(
                                _reader,
                                projectionTypeHandle));
            return (
                ApiMemberIdentity.CreateFieldAnchor(
                    _reader,
                    projectionTypeHandle,
                    field,
                    ref _anchorWorkRemaining),
                null,
                fieldType);
        }

        private (
            MemberAnchor Anchor,
            string? Signature,
            string? ReturnType)
            ProjectEvent(
                in ClassifiedMember member,
                TypeDefinitionHandle projectionTypeHandle,
                TypeDefinition projectionType)
        {
            var handle =
                (EventDefinitionHandle)member.Handle;
            EventDefinition eventDefinition =
                _reader.GetEventDefinition(handle);
            string signature =
                MetadataDeclarationQuery.GetEventSignatureText(
                    _reader,
                    projectionType,
                    eventDefinition);
            int separator = signature.IndexOf(' ');
            return (
                ApiMemberIdentity.CreateEventAnchor(
                    _reader,
                    projectionTypeHandle,
                    eventDefinition,
                    ref _anchorWorkRemaining),
                signature,
                separator < 0
                    ? null
                    : signature[..separator]);
        }

        private static string Kind(
            ClassifiedMemberKind kind) =>
            kind switch
            {
                ClassifiedMemberKind.Method => "method",
                ClassifiedMemberKind.Constructor =>
                    "constructor",
                ClassifiedMemberKind.Operator => "operator",
                ClassifiedMemberKind.Finalizer => "finalizer",
                ClassifiedMemberKind
                    .ExplicitInterfaceImplementation =>
                    "explicit-interface-implementation",
                ClassifiedMemberKind.ExtensionMethod =>
                    "extension-method",
                ClassifiedMemberKind.Property => "property",
                ClassifiedMemberKind.Field => "field",
                ClassifiedMemberKind.Event => "event",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(kind)),
            };
    }
}
