using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using ILInspector.Decompiler;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
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
    [property: JsonIgnore] LocalComparisonQueryResult? Evidence,
    string? CSharpFailure,
    string? IlFailure);

public sealed record RoundTripComparisonResult(
    RoundTripComparisonStatus Status,
    RoundTripRequest Request,
    string DonorSha256,
    ImmutableArray<RoundTripMemberComparison> Members,
    string? Failure,
    RoundTripCompilationProvenance? Compilation);

/// <summary>
/// Compares selected methods in an emitted donor against the exact requested
/// artifact. Correspondence is resolved before the product-owned C# and IL
/// diff arbiters run; missing or ambiguous members remain unavailable.
/// </summary>
public static class RoundTripComparison
{
    public static RoundTripComparisonResult Compare(
        RoundTripRequest request,
        byte[] donorPe,
        RoundTripCompilationProvenance? compilation = null)
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
            using var workspace = new InspectionWorkspace();
            var query = new RoundTripComparisonQuery(workspace, original, donor);
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

                var evidence = query.Compare(target.Method, donorTarget);
                members.Add(new RoundTripMemberComparison(
                    target,
                    correspondence,
                    evidence.CSharpStatus,
                    evidence.IlStatus,
                    evidence.CSharpDiff,
                    evidence.IlDiff,
                    evidence.Evidence,
                    evidence.CSharpFailure,
                    evidence.IlFailure));
            }

            return new RoundTripComparisonResult(
                RoundTripComparisonStatus.Completed,
                request,
                donorHash,
                members.ToImmutable(),
                Failure: null,
                compilation);
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
            failure,
            Compilation: null);
}
