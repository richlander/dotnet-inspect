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
        ChangeRoutingPolicy policy = ChangeRoutingPolicy.Load(repository);
        return Compose(provenance, evidence, policy);
    }

    /// <summary>
    /// Composes a plan from already-acquired evidence and loaded policy. This
    /// is the construction path the CLI uses; a fixture supplies only the
    /// evidence, never a substitute plan.
    /// </summary>
    /// <param name="provenance">The validated provenance.</param>
    /// <param name="evidence">The acquired change evidence.</param>
    /// <param name="policy">The loaded routing policy.</param>
    /// <returns>The plan and its scoped evidence.</returns>
    internal static PlanningResult Compose(
        CandidateProvenance provenance,
        ChangeEvidence evidence,
        ChangeRoutingPolicy policy)
    {
        RoutingSelections routing = policy.Route(evidence);
        ValidationSelections validations =
            ValidationSelections.FromRouting(routing, provenance.Kind);

        byte[]? scopeBytes = null;
        List<PlanScopeDescriptor> scopes = [];
        if (validations.Tla)
        {
            scopeBytes = BuildTlaScope(evidence, out int scopeRecords);
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
    /// plan input order, containing the model content and exact-outcome
    /// manifest consumed by the runner. Other infrastructure paths select the
    /// lane without contributing input, so an infrastructure-only selection
    /// produces a valid zero-record scope.
    /// </summary>
    /// <param name="evidence">The acquired change evidence.</param>
    /// <param name="recordCount">The number of scoped records.</param>
    /// <returns>The exact scope file bytes.</returns>
    private static byte[] BuildTlaScope(
        ChangeEvidence evidence,
        out int recordCount)
    {
        List<ChangeRecord> selected = [];
        long length = 0;
        foreach (ChangeRecord record in evidence.Records)
        {
            if (!ChangeRoutingPolicy.IsTlaScopedInput(record.Path))
            {
                continue;
            }

            selected.Add(record);
            length += record.Path.Length + 1;
            if (length > MaximumScopeBytes)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.ScopeOverflow,
                    "the tla scope exceeded its byte ceiling");
            }
        }

        byte[] bytes = new byte[length];
        int offset = 0;
        foreach (ChangeRecord record in selected)
        {
            record.Path.CopyTo(bytes.AsSpan(offset));
            offset += record.Path.Length;
            bytes[offset++] = 0;
        }

        recordCount = selected.Count;
        return bytes;
    }
}
