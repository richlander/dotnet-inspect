namespace CiChangeDetection.Planning;

/// <summary>
/// One planning operation's complete product: the immutable plan and the exact
/// scoped-evidence bytes the plan's descriptors name.
/// </summary>
internal sealed class PlanningResult
{
    private readonly byte[]? tlaScopeBytes;

    internal PlanningResult(ChangePlan plan, byte[]? tlaScopeBytes)
    {
        Plan = plan;
        PlanScopeDescriptor? descriptor = plan.TlaScope;
        if ((descriptor is null) != (tlaScopeBytes is null))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "the tla scope descriptor and evidence presence differ");
        }

        if (tlaScopeBytes is null)
        {
            return;
        }

        if (tlaScopeBytes.Length > ChangePlanner.MaximumScopeBytes)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.ScopeOverflow,
                "the tla scope exceeded its byte ceiling");
        }

        int recordCount = ValidateScope(tlaScopeBytes);
        if (descriptor!.RecordCount != recordCount
            || descriptor.Sha256 != Digest.LowercaseSha256(tlaScopeBytes))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "the tla scope descriptor does not bind its evidence");
        }

        this.tlaScopeBytes = tlaScopeBytes.ToArray();
    }

    internal ChangePlan Plan { get; }

    internal bool HasTlaScope => tlaScopeBytes is not null;

    internal ReadOnlySpan<byte> TlaScopeBytes =>
        tlaScopeBytes ?? ReadOnlySpan<byte>.Empty;

    private static int ValidateScope(ReadOnlySpan<byte> bytes)
    {
        int recordCount = 0;
        int offset = 0;
        while (offset < bytes.Length)
        {
            int terminator = bytes[offset..].IndexOf((byte)0);
            if (terminator < 0)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.EvidenceFraming,
                    "the tla scope ends inside a path record");
            }

            ChangePathRules.Validate(bytes.Slice(offset, terminator));
            recordCount++;
            offset += terminator + 1;
        }

        return recordCount;
    }
}

/// <summary>
/// The planner: from one checked candidate and its event provenance to one
/// immutable plan plus the bounded scoped path evidence that plan names.
/// </summary>
internal static class ChangePlanner
{
    internal const int MaximumScopeBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Plans one candidate. Every failure path refuses; none produces an
    /// empty or all-false plan.
    /// </summary>
    /// <param name="repository">The checked repository root directory.</param>
    /// <param name="kind">The event provenance kind.</param>
    /// <param name="baseObjectId">The base endpoint object ID.</param>
    /// <param name="candidateObjectId">The candidate endpoint object ID.</param>
    /// <returns>The plan and its scoped evidence.</returns>
    internal static PlanningResult Plan(
        string repository,
        PlanEventKind kind,
        string baseObjectId,
        string candidateObjectId)
    {
        CandidateProvenance provenance = GitCandidateReader.ResolveProvenance(
            repository,
            kind,
            baseObjectId,
            candidateObjectId);
        ChangeEvidence evidence =
            GitCandidateReader.ReadChanges(repository, provenance);
        IReadOnlyList<byte[]> changedOutcomePaths =
            GitCandidateReader.ReadTlaManifestChanges(
                repository, provenance, evidence);
        ChangeRoutingPolicy policy = ChangeRoutingPolicy.Load(repository);
        return Compose(provenance, evidence, policy, changedOutcomePaths);
    }

    /// <summary>
    /// Composes a plan from already-acquired evidence and loaded policy. This
    /// is the construction path the CLI uses; a fixture supplies only the
    /// evidence, never a substitute plan.
    /// </summary>
    /// <param name="provenance">The validated provenance.</param>
    /// <param name="evidence">The acquired change evidence.</param>
    /// <param name="policy">The loaded routing policy.</param>
    /// <param name="changedOutcomePaths">
    /// Configuration paths whose exact-outcome mappings differ at the endpoints.
    /// Required when the change evidence contains the manifest.
    /// </param>
    /// <returns>The plan and its scoped evidence.</returns>
    internal static PlanningResult Compose(
        CandidateProvenance provenance,
        ChangeEvidence evidence,
        ChangeRoutingPolicy policy,
        IReadOnlyList<byte[]>? changedOutcomePaths = null)
    {
        RoutingSelections routing = policy.Route(evidence);
        ValidationSelections validations =
            ValidationSelections.FromRouting(routing, provenance.Kind);

        byte[]? scopeBytes = null;
        List<PlanScopeDescriptor> scopes = [];
        if (validations.Tla)
        {
            scopeBytes = BuildTlaScope(
                evidence, changedOutcomePaths, out int scopeRecords);
            scopes.Add(new PlanScopeDescriptor(
                PlanScopeDescriptor.TlaScope,
                PlanScopeDescriptor.TlaArtifact,
                PlanScopeDescriptor.NulTerminatedFraming,
                scopeRecords,
                Digest.LowercaseSha256(scopeBytes)));
        }

        ChangePlan plan = new(
            ChangePlan.CurrentSchemaVersion,
            ChangePlan.PlannedStatus,
            provenance,
            new PlanInputDescriptor(evidence.RecordCount, evidence.Sha256),
            validations,
            scopes,
            policy.Diagnostics);
        return new PlanningResult(plan, scopeBytes);
    }

    /// <summary>
    /// Builds the TLA+ scope file: exact <c>path-bytes NUL</c> records, in
    /// plan input order, followed by affected manifest configuration paths in
    /// byte order. Paths already in the scope are not repeated. The manifest
    /// path itself selects validation, not model execution.
    /// </summary>
    /// <param name="evidence">The acquired change evidence.</param>
    /// <param name="changedOutcomePaths">The acquired mapping delta.</param>
    /// <param name="recordCount">The number of scoped records.</param>
    /// <returns>The exact scope file bytes.</returns>
    private static byte[] BuildTlaScope(
        ChangeEvidence evidence,
        IReadOnlyList<byte[]>? changedOutcomePaths,
        out int recordCount)
    {
        List<byte[]> selected = [];
        long length = 0;
        bool manifestChanged = false;
        foreach (ChangeRecord record in evidence.Records)
        {
            if (!ChangeRoutingPolicy.IsTlaScopedInput(record.Path))
            {
                continue;
            }

            AddPath(record.Path);
            manifestChanged |= record.Path.SequenceEqual(
                "eng/tla-expected-exit-codes.txt"u8);
        }

        if (manifestChanged)
        {
            if (changedOutcomePaths is null)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.EvidenceUnavailable,
                    "a changed TLA manifest requires its endpoint mapping delta");
            }

            foreach (byte[] path in changedOutcomePaths)
            {
                AddPath(path);
            }
        }

        byte[] bytes = new byte[length];
        int offset = 0;
        foreach (byte[] path in selected)
        {
            path.CopyTo(bytes.AsSpan(offset));
            offset += path.Length;
            bytes[offset++] = 0;
        }

        recordCount = selected.Count;
        return bytes;

        void AddPath(ReadOnlySpan<byte> path)
        {
            foreach (byte[] existing in selected)
            {
                if (path.SequenceEqual(existing))
                {
                    return;
                }
            }

            length += path.Length + 1;
            if (length > MaximumScopeBytes)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.ScopeOverflow,
                    "the tla scope exceeded its byte ceiling");
            }

            selected.Add(path.ToArray());
        }
    }
}
