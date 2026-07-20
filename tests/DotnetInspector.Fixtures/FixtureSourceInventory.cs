using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Fixtures;

public enum FixtureSourceApplicability
{
    Unclassified,
    Required,
    NotApplicable,
}

public sealed record FixtureSourcePolicy(FixtureSourceApplicability Applicability, string? Reason = null)
{
    public static FixtureSourcePolicy Unclassified { get; } = new(
        FixtureSourceApplicability.Unclassified,
        "Source applicability has not been classified.");

    public static FixtureSourcePolicy Required { get; } = new(FixtureSourceApplicability.Required);

    public static FixtureSourcePolicy NotApplicable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(FixtureSourceApplicability.NotApplicable, reason);
    }
}

public sealed record FixtureBinaryIdentity(string AssemblyName, Guid ModuleVersionId, string Sha256);

public sealed record FixtureSourceDocument(
    string LogicalPath,
    string Sha256,
    string? PdbChecksumAlgorithm,
    string? PdbChecksum);

public sealed record FixtureCompilationOptions(
    string TargetFramework,
    string LanguageVersion,
    string Optimization,
    bool Nullable,
    bool AllowUnsafe,
    IReadOnlyList<string> ConditionalSymbols);

public sealed record FixtureTypeIdentity(
    string AssemblyName,
    string FullName,
    int GenericArity,
    string? FileLocalIdentity = null);

public sealed record FixtureSourceSpan(
    string LogicalPath,
    int Start,
    int Length);

public enum SynthesizedFixtureMemberKind
{
    StateMachine,
    Closure,
    Accessor,
    RecordMember,
}

public abstract record FixtureSourceOwner
{
    public sealed record Authored(MemberAnchor Member) : FixtureSourceOwner;

    public sealed record Synthesized(
        MemberAnchor Owner,
        SynthesizedFixtureMemberKind Kind) : FixtureSourceOwner;

    public sealed record Unresolved(string Reason) : FixtureSourceOwner;
}

public abstract record FixtureSourceCompilationScope
{
    public sealed record Type(FixtureTypeIdentity ContainingType)
        : FixtureSourceCompilationScope;

    public sealed record Assembly(string FixtureId)
        : FixtureSourceCompilationScope;
}

public sealed record FixtureSourceTarget(
    string FixtureId,
    FixtureBinaryIdentity Binary,
    MemberAnchor Target,
    FixtureSourceOwner SourceOwner,
    FixtureSourceSpan SourceSpan,
    FixtureSourceCompilationScope Scope);

public enum FixtureSourceVerification
{
    NotAttempted,
    Verified,
    AssemblyMismatch,
    SourceChecksumMismatch,
    PdbChecksumMismatch,
}

public enum FixtureSourceInventoryStatus
{
    Unclassified,
    SourceDiscovered,
    SourceMissing,
    NotApplicable,
}

public sealed record FixtureSourceInventoryRow(
    string FixtureId,
    FixtureSourceApplicability Applicability,
    FixtureSourceInventoryStatus Status,
    int DiscoveredDocumentCount,
    string? Reason);

public sealed record FixtureSourceInventoryReport(IReadOnlyList<FixtureSourceInventoryRow> Fixtures)
{
    public int Required => Fixtures.Count(row =>
        row.Applicability == FixtureSourceApplicability.Required);

    public int SourceDiscovered => Fixtures.Count(row =>
        row.Status == FixtureSourceInventoryStatus.SourceDiscovered);

    public int Unresolved => Fixtures.Count(row =>
        row.Status is FixtureSourceInventoryStatus.Unclassified
            or FixtureSourceInventoryStatus.SourceMissing);
}

public static class FixtureSourceInventory
{
    public static FixtureSourceInventoryReport Create(IEnumerable<FixtureDefinition> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        return new(fixtures
            .OrderBy(fixture => fixture.Id, StringComparer.Ordinal)
            .Select(CreateRow)
            .ToArray());
    }

    static FixtureSourceInventoryRow CreateRow(FixtureDefinition fixture)
    {
        var policy = fixture.SourcePolicy;
        if (policy.Applicability == FixtureSourceApplicability.NotApplicable)
        {
            return new(fixture.Id, policy.Applicability,
                FixtureSourceInventoryStatus.NotApplicable, 0, policy.Reason);
        }

        if (policy.Applicability == FixtureSourceApplicability.Unclassified)
        {
            return new(fixture.Id, policy.Applicability,
                FixtureSourceInventoryStatus.Unclassified, 0, policy.Reason);
        }

        var sourcePaths = fixture.SourcePaths();
        return new(
            fixture.Id,
            policy.Applicability,
            sourcePaths.Count > 0
                ? FixtureSourceInventoryStatus.SourceDiscovered
                : FixtureSourceInventoryStatus.SourceMissing,
            sourcePaths.Count,
            sourcePaths.Count > 0 ? null : "No source documents were discovered.");
    }
}
