using System.Globalization;
using Microsoft.Diagnostics.Tracing.Etlx;

readonly record struct MethodTextMatch(
    AllocationCandidate Candidate,
    bool IsRuntimeBody);

readonly record struct AttributionGroupHash(
    ulong First,
    ulong Second);

readonly record struct ByteRemainderState(
    AttributionGroupHash Hash,
    int Offset);

sealed record LogicalAttributionGroup(
    string Key,
    AllocationCandidate[] Candidates);

sealed record LogicalAttributionPlan(
    LogicalAttributionGroup[] Groups);

sealed class CandidateLookup
{
    const int ByteRemainderStateCapacity = 4096;
    readonly Dictionary<(int Token, int Offset), List<AllocationCandidate>> _byTokenOffset;
    readonly Dictionary<(int Token, int Offset), AllocationCandidate[]> _rejectedByTokenOffset;
    readonly Dictionary<(string Module, int Token), List<AllocationCandidate>> _byModuleMethodToken;
    readonly Dictionary<(string Module, int Token), List<AllocationCandidate>>
        _rawLibrariesByModuleMethodToken;
    readonly Dictionary<(string Module, int Token), List<AllocationCandidate>>
        _supportsByModuleMethodToken;
    readonly HashSet<string> _candidateModules;
    readonly Dictionary<int, string>
        _stableAttributionKeys = [];
    readonly Dictionary<
        AttributionGroupHash,
        LinkedListNode<ByteRemainderState>>
        _byteRemainderStates = [];
    readonly LinkedList<ByteRemainderState>
        _byteRemainderLru = [];
    internal int RemainderStateCount =>
        _byteRemainderStates.Count;
    readonly List<(
        string Fragment,
        AllocationCandidate Candidate,
        bool IsRuntimeBody)> _methodFragments;

    CandidateLookup(
        Dictionary<(int Token, int Offset), List<AllocationCandidate>> byTokenOffset,
        Dictionary<(int Token, int Offset), AllocationCandidate[]> rejectedByTokenOffset,
        Dictionary<(string Module, int Token), List<AllocationCandidate>> byModuleMethodToken,
        Dictionary<(string Module, int Token), List<AllocationCandidate>>
            rawLibrariesByModuleMethodToken,
        Dictionary<(string Module, int Token), List<AllocationCandidate>>
            supportsByModuleMethodToken,
        HashSet<string> candidateModules,
        List<(
            string Fragment,
            AllocationCandidate Candidate,
            bool IsRuntimeBody)> methodFragments)
    {
        _byTokenOffset = byTokenOffset;
        _rejectedByTokenOffset =
            rejectedByTokenOffset;
        _byModuleMethodToken = byModuleMethodToken;
        _rawLibrariesByModuleMethodToken =
            rawLibrariesByModuleMethodToken;
        _supportsByModuleMethodToken =
            supportsByModuleMethodToken;
        _candidateModules = candidateModules;
        _methodFragments = methodFragments;
    }

    public static CandidateLookup Create(IReadOnlyList<AllocationCandidate> candidates)
    {
        var byTokenOffset = new Dictionary<(int Token, int Offset), List<AllocationCandidate>>();
        var byModuleMethodToken = new Dictionary<(string Module, int Token), List<AllocationCandidate>>();
        var rawLibrariesByModuleMethodToken =
            new Dictionary<
                (string Module, int Token),
                List<AllocationCandidate>>();
        var supportsByModuleMethodToken =
            new Dictionary<
                (string Module, int Token),
                List<AllocationCandidate>>();
        var fragments = new List<(
            string Fragment,
            AllocationCandidate Candidate,
            bool IsRuntimeBody)>();
        foreach (var candidate in candidates)
        {
            if (candidate.HasRuntimeCoordinate)
            {
                if (!candidate.SupportingCallSite)
                {
                    var key = (
                        candidate.RuntimeMethodToken,
                        candidate.IlOffset);
                    if (!byTokenOffset.TryGetValue(
                            key,
                            out var list))
                    {
                        list = [];
                        byTokenOffset.Add(key, list);
                    }
                    list.Add(candidate);
                }

                foreach (var moduleKey in CandidateModuleKeys(candidate))
                {
                    var moduleTokenKey = (
                        moduleKey,
                        candidate.RuntimeMethodToken);
                    var index = candidate.SupportingCallSite
                        ? supportsByModuleMethodToken
                        : byModuleMethodToken;
                    if (!index.TryGetValue(
                            moduleTokenKey,
                            out var tokenList))
                    {
                        tokenList = [];
                        index.Add(
                            moduleTokenKey,
                            tokenList);
                    }
                    tokenList.Add(candidate);

                    if (string.Equals(
                            candidate.Source,
                            "library",
                            StringComparison.Ordinal))
                    {
                        if (!rawLibrariesByModuleMethodToken
                            .TryGetValue(
                                moduleTokenKey,
                                out var rawList))
                        {
                            rawList = [];
                            rawLibrariesByModuleMethodToken
                                .Add(
                                    moduleTokenKey,
                                    rawList);
                        }
                        rawList.Add(candidate);
                    }
                }
            }

            bool isRuntimeBody =
                candidate.SourceMethodIdentifiesRuntimeBody;
            AddFragment(
                candidate.MethodKey,
                candidate,
                isRuntimeBody);
            AddFragment(
                candidate.MethodStackKey,
                candidate,
                isRuntimeBody);
            AddFragment(
                candidate.Method,
                candidate,
                isRuntimeBody);
            AddMethodLeafFragment(
                candidate.MethodKey,
                candidate,
                isRuntimeBody);
            AddMethodLeafFragment(
                candidate.MethodStackKey,
                candidate,
                isRuntimeBody);
        }

        foreach (var tokenList in byModuleMethodToken.Values)
            tokenList.Sort(static (left, right) => left.IlOffset.CompareTo(right.IlOffset));
        foreach (var tokenList
                 in rawLibrariesByModuleMethodToken.Values)
        {
            tokenList.Sort(static (left, right) =>
                left.IlOffset.CompareTo(
                    right.IlOffset));
        }
        foreach (var tokenList
                 in supportsByModuleMethodToken.Values)
        {
            tokenList.Sort(static (left, right) =>
                left.IlOffset.CompareTo(
                    right.IlOffset));
        }

        var textSupersessions =
            new List<TextSupersession>();
        foreach (var (key, coordinateCandidates)
                 in byTokenOffset)
        {
            var supersessions =
                PlanTextSupersessions(
                    coordinateCandidates);
            if (supersessions.Length == 0)
                continue;

            textSupersessions.AddRange(
                supersessions);
            var supersededIds = supersessions
                .Select(static supersession =>
                    supersession.Library.Id)
                .ToHashSet();
            coordinateCandidates.RemoveAll(
                candidate => supersededIds.Contains(
                    candidate.Id));
        }

        if (textSupersessions.Count > 0)
        {
            var supersededIds = textSupersessions
                .Select(static supersession =>
                    supersession.Library.Id)
                .ToHashSet();
            foreach (var supersession
                     in textSupersessions)
            {
                supersession.Library
                    .ProjectedByTriage = true;
                foreach (var target
                         in supersession
                             .TriageCandidates)
                {
                    if (!target.ProjectedLibraries
                        .Any(library =>
                            library.Id
                                == supersession
                                    .Library.Id))
                    {
                        target.ProjectedLibraries.Add(
                            supersession.Library);
                    }
                }
            }
            foreach (var tokenList
                     in byModuleMethodToken.Values)
            {
                tokenList.RemoveAll(candidate =>
                    supersededIds.Contains(
                        candidate.Id));
            }

            var targetsByLibraryId =
                textSupersessions
                    .GroupBy(static supersession =>
                        supersession.Library.Id)
                    .ToDictionary(
                        static group => group.Key,
                        static group => group
                            .SelectMany(
                                static supersession =>
                                    supersession
                                        .TriageCandidates)
                            .DistinctBy(
                                static target =>
                                    target.Id)
                            .ToArray());
            var redirectedFragments =
                new List<(
                    string Fragment,
                    AllocationCandidate Candidate,
                    bool IsRuntimeBody)>();
            foreach (var (
                         fragment,
                         candidate,
                         isRuntimeBody)
                     in fragments)
            {
                if (!targetsByLibraryId.TryGetValue(
                        candidate.Id,
                        out var targets))
                {
                    redirectedFragments.Add(
                        (
                            fragment,
                            candidate,
                            isRuntimeBody));
                    continue;
                }

                foreach (var target in targets)
                {
                    redirectedFragments.Add(
                        (
                            fragment,
                            target,
                            isRuntimeBody));
                }
            }
            fragments = redirectedFragments
                .Distinct()
                .ToList();
        }

        foreach (var support in candidates.Where(
                     static candidate =>
                         candidate.SupportingCallSite))
        {
            foreach (var library in candidates.Where(candidate =>
                         string.Equals(
                             candidate.Source,
                             "library",
                             StringComparison.Ordinal)
                         && string.Equals(
                             candidate.AssemblyModuleKey,
                             support.AssemblyModuleKey,
                             StringComparison.OrdinalIgnoreCase)
                         && candidate.RuntimeMethodToken
                             == support.RuntimeMethodToken
                         && candidate.IlOffset
                             == support.IlOffset
                         && ProgramSupport.SameBuild(
                             candidate,
                             support)))
            {
                if (!support.SupportingLibraries.Any(
                        projected =>
                            projected.Id == library.Id))
                {
                    support.SupportingLibraries.Add(
                        library);
                }
            }
        }

        foreach (var group in candidates
                     .Where(static candidate =>
                         !candidate.ProjectedByTriage)
                     .GroupBy(static candidate => (
                         candidate.AssemblyModuleKey,
                         BuildIdentity:
                            candidate.ModuleVersionId
                                ?.ToString("D")
                            ?? candidate
                                .UnknownBuildInputIdentity,
                         candidate.RuntimeMethodToken,
                         MethodWithoutToken:
                            candidate.RuntimeMethodToken == 0
                                ? candidate.Method
                                : "",
                         candidate.AllocationKind)))
        {
            int count = group
                .Select(candidate =>
                    StableAttributionKey(candidate))
                .Distinct(StringComparer.Ordinal)
                .Count();
            foreach (var candidate in group)
                candidate.SameMethodShapeRows = count;
        }

        var rejectedByTokenOffset =
            new Dictionary<
                (int Token, int Offset),
                AllocationCandidate[]>();
        foreach (var (key, coordinateCandidates)
                 in byTokenOffset.ToArray())
        {
            if (!HasAmbiguousTextCoordinateIdentity(
                    coordinateCandidates))
            {
                continue;
            }

            rejectedByTokenOffset.Add(
                key,
                [.. coordinateCandidates]);
            byTokenOffset.Remove(key);
        }

        return new CandidateLookup(
            byTokenOffset,
            rejectedByTokenOffset,
            byModuleMethodToken,
            rawLibrariesByModuleMethodToken,
            supportsByModuleMethodToken,
            [.. byModuleMethodToken.Keys.Select(static key => key.Module)],
            fragments);

        void AddFragment(
            string value,
            AllocationCandidate candidate,
            bool isRuntimeBody)
        {
            if (value.Length >= 8)
            {
                fragments.Add(
                    (
                        value,
                        candidate,
                        isRuntimeBody));
            }
        }

        void AddMethodLeafFragment(
            string value,
            AllocationCandidate candidate,
            bool isRuntimeBody)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var normalized = value.Replace("::", ".", StringComparison.Ordinal);
            int paren = normalized.IndexOf('(');
            if (paren >= 0)
                normalized = normalized[..paren];

            int methodDot = normalized.LastIndexOf('.');
            if (methodDot <= 0 || methodDot == normalized.Length - 1)
                return;

            var methodName = normalized[(methodDot + 1)..];
            var declaringType = normalized[..methodDot];
            int typeDot = declaringType.LastIndexOf('.');
            var typeName = typeDot >= 0 ? declaringType[(typeDot + 1)..] : declaringType;
            AddFragment(
                $"{typeName}.{methodName}",
                candidate,
                isRuntimeBody);
        }
    }

    public IReadOnlyList<AllocationCandidate> FindByTokenOffset(int token, int offset)
        => _byTokenOffset.TryGetValue((token, offset), out var candidates) ? candidates : [];

    public IReadOnlyDictionary<int, long>
        AttributeBytes(
            IReadOnlyList<AllocationCandidate> candidates,
            long totalBytes)
        => AttributeLong(
            CreateAttributionPlan(candidates),
            totalBytes,
            groupDomain: 'B',
            duplicateDomain: 'D');

    public IReadOnlyDictionary<int, long>
        AttributeCounts(
            LogicalAttributionPlan plan,
            long totalCount)
        => AttributeLong(
            plan,
            totalCount,
            groupDomain: 'T',
            duplicateDomain: 'U');

    public IReadOnlyDictionary<int, double>
        AttributeWeight(
            IReadOnlyList<AllocationCandidate> candidates,
            double totalWeight)
    {
        var plan = CreateAttributionPlan(candidates);
        var result = candidates.ToDictionary(
            static candidate => candidate.Id,
            static _ => 0d);
        if (plan.Groups.Length == 0)
            return result;

        double groupWeight =
            totalWeight / plan.Groups.Length;
        foreach (var group in plan.Groups)
        {
            double candidateWeight =
                groupWeight
                / group.Candidates.Length;
            foreach (var candidate
                     in group.Candidates)
            {
                result[candidate.Id] =
                    candidateWeight;
            }
        }
        return result;
    }

    public LogicalAttributionPlan
        CreateAttributionPlan(
            IReadOnlyList<AllocationCandidate>
                candidates)
        => new(
            [
                .. candidates
                    .GroupBy(
                        GetStableAttributionKey)
                    .OrderBy(
                        static group => group.Key,
                        StringComparer.Ordinal)
                    .Select(static group =>
                        new LogicalAttributionGroup(
                            group.Key,
                            [
                                .. group.OrderBy(
                                    static candidate =>
                                        candidate.Id)
                            ]))
            ]);

    IReadOnlyDictionary<int, long>
        AttributeLong(
            LogicalAttributionPlan plan,
            long total,
            char groupDomain,
            char duplicateDomain)
    {
        if (total < 0)
        {
            throw new InvalidDataException(
                "runtime attribution total must be non-negative");
        }
        var result =
            plan.Groups
                .SelectMany(static group =>
                    group.Candidates)
                .ToDictionary(
                static candidate =>
                    candidate.Id,
                static _ => 0L);
        if (plan.Groups.Length == 0)
            return result;

        long[] groupShares = SplitLong(
            total,
            plan.Groups.Length,
            ComputeAttributionGroupHash(
                plan.Groups.Select(static group =>
                    group.Key),
                domain: groupDomain));
        for (int groupIndex = 0;
             groupIndex < plan.Groups.Length;
             groupIndex++)
        {
            var group = plan.Groups[groupIndex];
            long[] candidateShares = SplitLong(
                groupShares[groupIndex],
                group.Candidates.Length,
                ComputeAttributionGroupHash(
                    [
                        group.Key,
                        group.Candidates.Length
                            .ToString(
                                CultureInfo
                                    .InvariantCulture)
                    ],
                    domain: duplicateDomain));
            for (int candidateIndex = 0;
                 candidateIndex
                    < group.Candidates.Length;
                 candidateIndex++)
            {
                result[group.Candidates[
                    candidateIndex].Id] =
                    candidateShares[candidateIndex];
            }
        }
        return result;
    }

    long[] SplitLong(
        long total,
        int count,
        AttributionGroupHash groupHash)
    {
        if (total < 0)
        {
            throw new InvalidDataException(
                "runtime attribution total must be non-negative");
        }
        var result = new long[count];
        long baseBytes = total / count;
        int remainder = checked((int)(
            total % count));
        Array.Fill(result, baseBytes);

        int start = 0;
        if (_byteRemainderStates.TryGetValue(
                groupHash,
                out var node))
        {
            start = node.Value.Offset;
            _byteRemainderLru.Remove(node);
            _byteRemainderLru.AddLast(node);
        }
        if (remainder == 0)
            return result;

        for (int index = 0;
             index < remainder;
             index++)
        {
            int recipient =
                (start + index)
                % count;
            result[recipient]++;
        }
        int nextOffset =
            (start + remainder)
            % count;
        if (node is not null)
        {
            node.Value = new ByteRemainderState(
                groupHash,
                nextOffset);
        }
        else
        {
            if (_byteRemainderStates.Count
                == ByteRemainderStateCapacity)
            {
                var oldest =
                    _byteRemainderLru.First!;
                _byteRemainderLru.RemoveFirst();
                _byteRemainderStates.Remove(
                    oldest.Value.Hash);
            }
            var state = new ByteRemainderState(
                groupHash,
                nextOffset);
            var added =
                _byteRemainderLru.AddLast(state);
            _byteRemainderStates.Add(
                groupHash,
                added);
        }
        return result;
    }

    public IReadOnlyList<AllocationCandidate>
        FindRejectedByTokenOffset(
            int token,
            int offset)
        => _rejectedByTokenOffset.TryGetValue(
            (token, offset),
            out var candidates)
            ? candidates
            : [];

    public IReadOnlyList<AllocationCandidate> FindNearestByCodeAddress(TraceCodeAddress address)
    {
        return FindNearestByTokenOffset(
            address.Method?.MethodToken ?? 0,
            address.ILOffset,
            address.ModuleFilePath,
            address.ModuleName,
            address.FullMethodName);
    }

    public bool IsCandidateModule(TraceCodeAddress address)
        => ModuleLookupKeys(
                address.ModuleFilePath,
                address.ModuleName)
            .Any(_candidateModules.Contains);

    public IReadOnlyList<AllocationCandidate> FindNearestByTokenOffset(
        int token,
        int ilOffset,
        string? modulePath,
        string? moduleName,
        string? methodName)
    {
        if (ilOffset < 0)
            return [];

        if (token == 0)
            return [];

        var moduleKeys = ModuleLookupKeys(modulePath, moduleName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var moduleCandidates = moduleKeys
            .SelectMany(moduleKey => _byModuleMethodToken.TryGetValue((moduleKey, token), out var candidates) ? candidates : [])
            .DistinctBy(static candidate => candidate.Id)
            .ToArray();

        var search = moduleCandidates
            .Where(candidate => candidate.IlOffset <= ilOffset)
            .ToArray();
        if (search.Length == 0
            && moduleKeys.Length == 0
            && !string.IsNullOrWhiteSpace(methodName))
        {
            search = _byModuleMethodToken
                .Where(pair => pair.Key.Token == token)
                .SelectMany(static pair => pair.Value)
                .Where(candidate => candidate.IlOffset <= ilOffset && MethodMatches(candidate, methodName))
                .DistinctBy(static candidate => candidate.Id)
                .ToArray();
        }
        if (search.Length == 0)
            return [];

        var result = NearestByBuild(search)
            .ToList();
        var rawSearch = moduleKeys
            .SelectMany(moduleKey =>
                _rawLibrariesByModuleMethodToken
                    .TryGetValue(
                        (moduleKey, token),
                        out var candidates)
                    ? candidates
                    : [])
            .Where(candidate =>
                candidate.IlOffset <= ilOffset)
            .DistinctBy(static candidate =>
                candidate.Id)
            .ToArray();
        var supports = moduleKeys
            .SelectMany(moduleKey =>
                _supportsByModuleMethodToken
                    .TryGetValue(
                        (moduleKey, token),
                        out var candidates)
                    ? candidates
                    : [])
            .DistinctBy(static candidate =>
                candidate.Id)
            .ToArray();
        if (moduleKeys.Length == 0
            && !string.IsNullOrWhiteSpace(methodName))
        {
            rawSearch =
            [
                .. _rawLibrariesByModuleMethodToken
                    .Where(pair =>
                        pair.Key.Token == token)
                    .SelectMany(static pair =>
                        pair.Value)
                    .Where(candidate =>
                        candidate.IlOffset <= ilOffset
                        && MethodMatches(
                            candidate,
                            methodName))
                    .DistinctBy(static candidate =>
                        candidate.Id),
            ];
            supports =
            [
                .. _supportsByModuleMethodToken
                    .Where(pair =>
                        pair.Key.Token == token)
                    .SelectMany(static pair =>
                        pair.Value)
                    .Where(candidate =>
                        MethodMatches(
                            candidate,
                            methodName))
                    .DistinctBy(static candidate =>
                        candidate.Id),
            ];
        }

        foreach (var raw in NearestByBuild(rawSearch))
        {
            if (!result.Any(candidate =>
                    candidate.IlOffset == raw.IlOffset
                    && ProgramSupport.SameBuild(
                        candidate,
                        raw)))
            {
                continue;
            }
            result.AddRange(supports.Where(support =>
                support.IlOffset == raw.IlOffset
                && ProgramSupport.SameBuild(
                    support,
                    raw)));
        }
        return
        [
            .. result.DistinctBy(
                static candidate => candidate.Id),
        ];
    }

    static IEnumerable<AllocationCandidate>
        NearestByBuild(
            IEnumerable<AllocationCandidate> candidates)
        => candidates
            .GroupBy(static candidate => (
                candidate.AssemblyModuleKey,
                BuildIdentity:
                    candidate.ModuleVersionId
                        ?.ToString("D")
                    ?? candidate
                        .UnknownBuildInputIdentity))
            .SelectMany(static buildCandidates =>
            {
                int nearest = buildCandidates.Max(
                    static candidate =>
                        candidate.IlOffset);
                return buildCandidates.Where(
                    candidate =>
                        candidate.IlOffset == nearest);
            });

    public List<MethodTextMatch> FindByMethodText(string line)
    {
        var candidates = new List<MethodTextMatch>();
        foreach (var (
                     fragment,
                     candidate,
                     isRuntimeBody)
                 in _methodFragments)
        {
            if (line.Contains(fragment, StringComparison.Ordinal))
            {
                candidates.Add(
                    new MethodTextMatch(
                        candidate,
                        isRuntimeBody));
            }
        }

        return candidates;
    }

    static IEnumerable<string> CandidateModuleKeys(AllocationCandidate candidate)
    {
        if (candidate.AssemblyModuleKey is { Length: > 0 })
            yield return candidate.AssemblyModuleKey;
        if (string.Equals(
                candidate.Source,
                "library",
                StringComparison.Ordinal))
        {
            if (ProgramSupport.NormalizeModuleKey(candidate.LibraryPath) is { Length: > 0 } path)
                yield return path;
            if (ProgramSupport.NormalizeModuleKey(Path.GetFileName(candidate.LibraryPath)) is { Length: > 0 } file)
                yield return file;
        }
    }

    static IEnumerable<string> ModuleLookupKeys(string? modulePath, string? moduleName)
    {
        if (ProgramSupport.NormalizeModuleKey(modulePath) is { Length: > 0 } path)
            yield return path;
        if (ProgramSupport.NormalizeModuleKey(Path.GetFileName(modulePath)) is { Length: > 0 } file)
            yield return file;
        if (ProgramSupport.NormalizeModuleKey(moduleName) is { Length: > 0 } name)
            yield return name;
    }

    static bool MethodMatches(AllocationCandidate candidate, string? method)
    {
        return candidate.SourceMethodIdentifiesRuntimeBody
            && !string.IsNullOrWhiteSpace(method)
            && (method.Contains(candidate.MethodStackKey, StringComparison.Ordinal)
                || method.Contains(candidate.MethodKey, StringComparison.Ordinal)
                || method.Contains(candidate.Method, StringComparison.Ordinal));
    }

    string GetStableAttributionKey(
        AllocationCandidate candidate)
    {
        if (_stableAttributionKeys.TryGetValue(
                candidate.Id,
                out var key))
        {
            return key;
        }

        key = StableAttributionKey(candidate);
        _stableAttributionKeys.Add(
            candidate.Id,
            key);
        return key;
    }

    internal static string StableAttributionKey(
        AllocationCandidate candidate)
        => string.Concat(
            EncodeAttributionPart(
                candidate.AssemblyModuleKey),
            EncodeAttributionPart(
                candidate.ModuleVersionId
                    ?.ToString("D")
                ?? ""),
            EncodeAttributionPart(
                candidate.RuntimeMethodToken
                    .ToString(
                        "X8",
                        CultureInfo.InvariantCulture)),
            EncodeAttributionPart(
                candidate.MethodToken.ToString(
                    "X8",
                    CultureInfo.InvariantCulture)),
            EncodeAttributionPart(
                candidate
                    .UnknownBuildInputIdentity),
            EncodeAttributionPart(
                candidate.IlOffset.ToString(
                    "X8",
                    CultureInfo.InvariantCulture)),
            EncodeAttributionPart(
                candidate.Source),
            EncodeAttributionPart(
                candidate.Method),
            EncodeAttributionPart(
                candidate.AllocationKind),
            EncodeAttributionPart(
                candidate.Operation ?? ""),
            EncodeAttributionPart(
                candidate.OperandToken
                    ?.ToString(
                        "X8",
                        CultureInfo.InvariantCulture)
                ?? ""),
            EncodeAttributionPart(
                candidate.PredictedType ?? ""),
            EncodeAttributionPart(
                candidate.CandidateId ?? ""),
            EncodeAttributionPart(
                candidate.Provenance ?? ""));

    static string EncodeAttributionPart(string value)
        => string.Concat(
            value.Length.ToString(
                CultureInfo.InvariantCulture),
            ":",
            value);

    static AttributionGroupHash
        ComputeAttributionGroupHash(
            IEnumerable<string> keys,
            char domain)
    {
        const ulong firstOffset =
            14695981039346656037UL;
        const ulong secondOffset =
            7809847782465536322UL;
        const ulong firstPrime =
            1099511628211UL;
        const ulong secondPrime =
            14029467366897019727UL;
        ulong first = firstOffset ^ domain;
        ulong second = secondOffset ^ domain;
        foreach (string key in keys)
        {
            foreach (char character in key)
            {
                first ^= character;
                first *= firstPrime;
                second ^= character;
                second *= secondPrime;
            }
        }
        return new AttributionGroupHash(
            first,
            second);
    }

    static bool HasAmbiguousTextCoordinateIdentity(
        IReadOnlyList<AllocationCandidate> candidates)
    {
        if (candidates
            .Select(static candidate =>
                candidate.AssemblyModuleKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any())
        {
            return true;
        }

        int buildIdentityCount = candidates
            .Where(static candidate =>
                candidate.ModuleVersionId is not null
                || candidate.UnknownBuildInputIdentity.Length > 0)
            .Select(static candidate =>
                candidate.ModuleVersionId is Guid moduleVersionId
                    ? $"mvid:{moduleVersionId:D}"
                    : $"path:{candidate.UnknownBuildInputIdentity}")
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Count();
        return buildIdentityCount > 1;
    }

    static TextSupersession[] PlanTextSupersessions(
        IReadOnlyList<AllocationCandidate> candidates)
    {
        var triageCandidates = candidates
            .Where(static candidate => string.Equals(
                candidate.Source,
                "triage",
                StringComparison.Ordinal))
            .ToArray();
        var result = new List<TextSupersession>();
        foreach (var library in candidates.Where(
                     static candidate => string.Equals(
                         candidate.Source,
                         "library",
                         StringComparison.Ordinal)))
        {
            var compatibleTriage = triageCandidates
                .Where(triage =>
                    string.Equals(
                        library.AssemblyModuleKey,
                        triage.AssemblyModuleKey,
                        StringComparison.OrdinalIgnoreCase)
                    && PredictedTypesCompatible(
                        library,
                        triage))
                .ToArray();
            if (compatibleTriage.Length == 0)
                continue;

            AllocationCandidate[] buildTriage;
            if (library.ModuleVersionId is Guid moduleVersionId)
            {
                buildTriage = compatibleTriage
                    .Where(triage =>
                        triage.ModuleVersionId
                            == moduleVersionId)
                    .ToArray();
                if (buildTriage.Length > 0)
                {
                    result.Add(
                        new TextSupersession(
                            library,
                            buildTriage));
                    continue;
                }
            }

            buildTriage = compatibleTriage
                .Where(static triage =>
                    triage.ModuleVersionId is null)
                .ToArray();
            if (buildTriage.Length == 0)
                continue;

            int compatibleLibraryBuilds =
                candidates
                    .Where(candidate =>
                        string.Equals(
                            candidate.Source,
                            "library",
                            StringComparison.Ordinal)
                        && string.Equals(
                            candidate.AssemblyModuleKey,
                            library.AssemblyModuleKey,
                            StringComparison
                                .OrdinalIgnoreCase)
                        && buildTriage.Any(triage =>
                            PredictedTypesCompatible(
                                candidate,
                                triage)))
                    .Select(static candidate =>
                        candidate.ModuleVersionId
                            is Guid candidateMvid
                            ? $"mvid:{candidateMvid:D}"
                            : $"path:{candidate.UnknownBuildInputIdentity}")
                    .Distinct(
                        StringComparer.Ordinal)
                    .Take(2)
                    .Count();
            if (compatibleLibraryBuilds == 1)
            {
                result.Add(
                    new TextSupersession(
                        library,
                        buildTriage));
            }
        }

        return [.. result];
    }

    static bool PredictedTypesCompatible(
        AllocationCandidate left,
        AllocationCandidate right)
        => (left.PredictedType is { Length: > 0 } leftType
                && right.MatchesAllocatedType(
                    leftType))
            || (right.PredictedType is { Length: > 0 } rightType
                && left.MatchesAllocatedType(
                    rightType));
}

readonly record struct TextSupersession(
    AllocationCandidate Library,
    AllocationCandidate[] TriageCandidates);
