using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
using DecompilerMetadataSource = ILInspector.Decompiler.Pipeline.MetadataSource;

namespace DotnetInspector.RoundTripCompilation;

public enum RoundTripEvidenceStatus
{
    Exact,
    Changed,
    Unavailable,
}

public enum RoundTripComparisonStatus
{
    Completed,
    Failed,
}

public sealed record RoundTripCSharpEvidence(
    ImmutableArray<CSharpDiffRow> Rows,
    ImmutableArray<CSharpDiffFailureRow> FailureRows,
    ImmutableArray<CSharpIdentityResolutionFailure> IdentityFailures);

public sealed record RoundTripIlEvidence(
    IlBodyDiffOutcome Outcome,
    string? Failure,
    ImmutableArray<IlDiffRow> Rows,
    ImmutableArray<IlDiffFailureRow> FailureRows);

public sealed record RoundTripMemberComparison(
    RoundTripTarget Target,
    MethodCorrespondenceResult Correspondence,
    RoundTripEvidenceStatus CSharpStatus,
    IlBodyDiffOutcome IlStatus,
    RoundTripCSharpEvidence? CSharpDiff,
    RoundTripIlEvidence? IlDiff,
    [property: JsonIgnore] ImplementationMemberDiffResult? Evidence,
    string? CSharpFailure,
    string? IlFailure);

public sealed record RoundTripComparisonResult(
    RoundTripComparisonStatus Status,
    RoundTripRequest Request,
    string DonorSha256,
    ImmutableArray<RoundTripMemberComparison> Members,
    string? Failure);

/// <summary>
/// Compares selected methods in an emitted donor against the exact requested
/// artifact. Correspondence is resolved before the product-owned C# and IL
/// diff arbiters run; missing or ambiguous members remain unavailable.
/// </summary>
public static class RoundTripComparison
{
    public static RoundTripComparisonResult Compare(
        RoundTripRequest request,
        byte[] donorPe)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(donorPe);
        string donorHash = System.Convert.ToHexString(SHA256.HashData(donorPe)).ToLowerInvariant();
        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-roundtrip-{Guid.NewGuid():N}.dll");
        try
        {
            string actualInputHash = HashFile(request.Artifact.Path);
            if (!string.Equals(actualInputHash, request.Artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                return Failed(request, donorHash, "input artifact content hash does not match the request");

            File.WriteAllBytes(temporaryPath, donorPe);
            using var original = DecompilerMetadataSource.OpenWithoutSymbols(request.Artifact.Path);
            using var donor = DecompilerMetadataSource.OpenWithoutSymbols(temporaryPath);
            var members = ImmutableArray.CreateBuilder<RoundTripMemberComparison>();
            foreach (var target in request.Targets)
            {
                var correspondence = MemberBodyProducer.ResolveCorrespondence(
                    original,
                    target.Method,
                    donor);
                if (!correspondence.IsExact || correspondence.Target is not { } donorTarget)
                {
                    members.Add(new RoundTripMemberComparison(
                        target,
                        correspondence,
                        RoundTripEvidenceStatus.Unavailable,
                        IlBodyDiffOutcome.Unavailable,
                        CSharpDiff: null,
                        IlDiff: null,
                        Evidence: null,
                        CSharpFailure: correspondence.Failure,
                        IlFailure: correspondence.Failure));
                    continue;
                }

                var subject = new FindingSubject(
                    target.Anchor.StableSelector,
                    $"{target.Anchor.TypeFullName}.{target.Anchor.MemberName}");
                var oldInspection = CSharpFindings.Inspect(original, target.Method.Handle, subject);
                var newInspection = CSharpFindings.Inspect(donor, donorTarget.Handle, subject);
                var evidence = ImplementationDiff.CompareMembers(
                    original,
                    target.Method.Handle,
                    donor,
                    donorTarget.Handle);
                var csharpStatus = oldInspection is FindingInspection<CSharpCanonicalLine>.Complete
                    && newInspection is FindingInspection<CSharpCanonicalLine>.Complete
                        ? evidence.CSharpDiff is { IsExact: true }
                            ? RoundTripEvidenceStatus.Exact
                            : RoundTripEvidenceStatus.Changed
                        : RoundTripEvidenceStatus.Unavailable;
                members.Add(new RoundTripMemberComparison(
                    target,
                    correspondence,
                    csharpStatus,
                    evidence.IlDiff?.Diff.Outcome ?? IlBodyDiffOutcome.Unavailable,
                    ToEvidence(evidence.CSharpDiff),
                    ToEvidence(evidence.IlDiff?.Diff),
                    evidence,
                    CSharpFailure: csharpStatus == RoundTripEvidenceStatus.Unavailable
                        ? InspectionFailure(oldInspection, newInspection)
                        : null,
                    IlFailure: evidence.IlDiff?.Diff.Failure));
            }

            return new RoundTripComparisonResult(
                RoundTripComparisonStatus.Completed,
                request,
                donorHash,
                members.ToImmutable(),
                Failure: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return Failed(request, donorHash, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    static string? InspectionFailure(
        FindingInspection<CSharpCanonicalLine> oldInspection,
        FindingInspection<CSharpCanonicalLine> newInspection)
    {
        List<string> failures = [];
        if (oldInspection is FindingInspection<CSharpCanonicalLine>.Absent oldAbsent)
            failures.Add($"old absent: {oldAbsent.Detail}");
        else if (oldInspection is FindingInspection<CSharpCanonicalLine>.Failed oldFailed)
            failures.Add($"old failed: {oldFailed.Error.Reason}");
        if (newInspection is FindingInspection<CSharpCanonicalLine>.Absent newAbsent)
            failures.Add($"new absent: {newAbsent.Detail}");
        else if (newInspection is FindingInspection<CSharpCanonicalLine>.Failed newFailed)
            failures.Add($"new failed: {newFailed.Error.Reason}");
        return failures.Count == 0 ? null : string.Join("; ", failures);
    }

    static RoundTripCSharpEvidence? ToEvidence(CSharpBodyDiffResult? diff)
        => diff is null
            ? null
            : new RoundTripCSharpEvidence(
                Normalize(diff.Rows),
                Normalize(diff.FailureRows),
                Normalize(diff.IdentityFailures));

    static RoundTripIlEvidence? ToEvidence(IlBodyDiffResult? diff)
        => diff is null
            ? null
            : new RoundTripIlEvidence(
                diff.Outcome,
                diff.Failure,
                Normalize(diff.Rows),
                Normalize(diff.FailureRows));

    static ImmutableArray<T> Normalize<T>(ImmutableArray<T> values)
        => values.IsDefault ? [] : values;

    static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return System.Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static RoundTripComparisonResult Failed(
        RoundTripRequest request,
        string donorHash,
        string failure)
        => new(
            RoundTripComparisonStatus.Failed,
            request,
            donorHash,
            Members: [],
            failure);
}
