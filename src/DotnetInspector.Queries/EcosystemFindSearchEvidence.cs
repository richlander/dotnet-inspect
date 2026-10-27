using System.Collections.Immutable;

namespace DotnetInspector.Queries;

public sealed record EcosystemFindRequestEvidence(
    Guid Identity,
    FindQuestion Question,
    ImmutableArray<EcosystemFindRegistrationEvidence> Registrations,
    ImmutableArray<string> PrefixDemand,
    ImmutableArray<string> Prefixes,
    int MaximumPrefixPackages,
    int? MaximumRows);

public sealed record EcosystemFindRegistrationEvidence(
    string Identity,
    bool Platform,
    ImmutableArray<string> CorePackages,
    ImmutableArray<string> Prefixes);

/// <summary>One evaluator invocation, including evaluations yielding no rows.</summary>
public sealed record EcosystemFindWorkEvidence(
    string Phase,
    string Source,
    int RowCount,
    bool HasFailures,
    bool Incomplete);

/// <summary>A page supplied by discovery, not an HTTP request or transfer receipt.</summary>
public sealed record EcosystemFindPageEvidence(
    string Prefix,
    int CandidateCount,
    EcosystemFindPrefixPageCompletion Completion);

public sealed record EcosystemFindSearchEvidence(
    EcosystemFindRequestEvidence Request,
    ImmutableArray<string> EvaluatedIndexes,
    ImmutableArray<EcosystemFindWorkEvidence> Work,
    ImmutableArray<EcosystemFindPageEvidence> Pages,
    int PrefixCandidatesEvaluated);
