using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ILInspector.Findings;

namespace ILInspector.Analysis;

public enum ResourceBoundaryKind
{
    Unknown,
    ExternalInput,
    InMemoryTransform,
}

public enum ResourceActionability
{
    Unknown,
    TrustedLowActionability,
    UntrustedActionable,
}

public readonly record struct ResourceBoundaryEvidence(
    int ILOffset,
    MemberRef Operation,
    ResourceBoundaryKind Kind);

public sealed record ResourceLifecycleOccurrence
{
    ImmutableArray<ResourceBoundaryEvidence> _boundaries;

    public ResourceLifecycleOccurrence(
        MethodIdentity Method,
        string Resource,
        string Shape,
        int AcquireOffset,
        ImmutableArray<ResourceBoundaryEvidence> Boundaries,
        ResourceActionability Actionability)
    {
        this.Method = Method ?? throw new ArgumentNullException(nameof(Method));
        ArgumentException.ThrowIfNullOrWhiteSpace(Resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(Shape);
        if (AcquireOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(AcquireOffset));

        this.Resource = Resource;
        this.Shape = Shape;
        this.AcquireOffset = AcquireOffset;
        _boundaries = ImmutableArrayValueEquality.RequireInitialized(
            Boundaries,
            nameof(Boundaries));
        this.Actionability = Actionability;
    }

    public MethodIdentity Method { get; }
    public string Resource { get; }
    public string Shape { get; }
    public int AcquireOffset { get; }
    public ImmutableArray<ResourceBoundaryEvidence> Boundaries
    {
        get => _boundaries;
        init => _boundaries = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Boundaries));
    }
    public ResourceActionability Actionability { get; }

    public bool Equals(ResourceLifecycleOccurrence? other)
        => other is not null
            && Method == other.Method
            && string.Equals(Resource, other.Resource, StringComparison.Ordinal)
            && string.Equals(Shape, other.Shape, StringComparison.Ordinal)
            && AcquireOffset == other.AcquireOffset
            && ImmutableArrayValueEquality.SequenceEqual(Boundaries, other.Boundaries)
            && Actionability == other.Actionability;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Method);
        hash.Add(Resource, StringComparer.Ordinal);
        hash.Add(Shape, StringComparer.Ordinal);
        hash.Add(AcquireOffset);
        ImmutableArrayValueEquality.AddToHash(ref hash, Boundaries);
        hash.Add(Actionability);
        return hash.ToHashCode();
    }
}

public sealed record ResourceTriageCandidate(
    string CandidateId,
    Finding<ResourceLifecycleOccurrence> Source);

public static class ResourceLifecycleAnalysis
{
    public static FindingInspection<ResourceLifecycleOccurrence> InspectAssembly(
        string path,
        FindingSubject subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(subject);

        try
        {
            var occurrences = LeakTriageAnalyzer
                .AnalyzeAssemblyDetailed(path)
                .ExceptionPathCandidates
                .Select(Classify);
            return new FindingInspection<ResourceLifecycleOccurrence>.Complete(
                AnalysisFindings.InspectResourceLifecycles(occurrences, subject));
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException
                or IndexOutOfRangeException)
        {
            return new FindingInspection<ResourceLifecycleOccurrence>.Failed(
                new InspectionError(
                    subject,
                    AnalysisFindings.ResourceLifecycleDescriptor,
                    $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    public static ImmutableArray<ResourceTriageCandidate> SelectCandidates(
        FindingInspection<ResourceLifecycleOccurrence>.Complete inspection,
        ResourceActionability? actionability = null)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        var candidates =
            ImmutableArray.CreateBuilder<ResourceTriageCandidate>(
                inspection.Findings.Length);
        foreach (var finding in inspection.Findings)
        {
            int fingerprintLength = InitialFingerprintLength;
            string candidateId;
            while (true)
            {
                candidateId = CreateCandidateId(
                    finding,
                    fingerprintLength);
                if (candidateIds.Add(candidateId))
                    break;
                if (fingerprintLength == MaximumFingerprintLength)
                {
                    throw new InvalidOperationException(
                        $"Duplicate Resource Triage candidate identity '{candidateId}'.");
                }

                fingerprintLength = Math.Min(
                    fingerprintLength + 8,
                    MaximumFingerprintLength);
            }

            candidates.Add(
                new ResourceTriageCandidate(candidateId, finding));
        }

        return actionability is null
            ? candidates.MoveToImmutable()
            : candidates
                .Where(candidate =>
                    candidate.Source.Payload.Actionability
                        == actionability)
                .ToImmutableArray();
    }

    static ResourceLifecycleOccurrence Classify(ArrayPoolExceptionPathCandidate candidate)
    {
        var boundaries = candidate.Boundaries
            .Select(boundary => new ResourceBoundaryEvidence(
                boundary.ILOffset,
                boundary.Operation,
                ClassifyBoundary(boundary.Operation)))
            .ToImmutableArray();

        ResourceActionability actionability = boundaries.Any(
            static boundary => boundary.Kind == ResourceBoundaryKind.ExternalInput)
                ? ResourceActionability.UntrustedActionable
                : boundaries.Any(
                    static boundary => boundary.Kind == ResourceBoundaryKind.Unknown)
                    || boundaries.Length == 0
                    ? ResourceActionability.Unknown
                    : ResourceActionability.TrustedLowActionability;

        return new ResourceLifecycleOccurrence(
            candidate.Method,
            "ArrayPool<T>",
            "pool-churn-on-exception",
            candidate.RentOffset,
            boundaries,
            actionability);
    }

    static ResourceBoundaryKind ClassifyBoundary(MemberRef member)
    {
        string type = TypeName(member.DeclaringType);
        string method = member.Name;

        if ((IsStreamLike(type)
                && method.StartsWith("Read", StringComparison.Ordinal))
            || ((type is "Decoder"
                    || type.EndsWith("Decoder", StringComparison.Ordinal))
                && method is "GetChars" or "GetString" or "Convert")
            || ((type is "Encoding"
                    || type.EndsWith("Encoding", StringComparison.Ordinal))
                && method is "GetChars" or "GetString")
            || (type == "Socket"
                && method.StartsWith("Receive", StringComparison.Ordinal))
            || (type is "TextReader" or "StreamReader" or "BinaryReader"
                && method.StartsWith("Read", StringComparison.Ordinal))
            || method.StartsWith("Deserialize", StringComparison.Ordinal)
            || method.StartsWith("Tokenize", StringComparison.Ordinal)
            || method == "Decode"
            || ((method is "Parse" or "TryParse"
                    || method.StartsWith("Parse", StringComparison.Ordinal))
                && (type.Contains("Document", StringComparison.Ordinal)
                    || type.Contains("Reader", StringComparison.Ordinal)
                    || type.Contains("Parser", StringComparison.Ordinal))))
        {
            return ResourceBoundaryKind.ExternalInput;
        }

        if (method.StartsWith("Escape", StringComparison.Ordinal)
            || ((type is "Encoding"
                    || type.EndsWith("Encoding", StringComparison.Ordinal))
                && method is "GetBytes" or "GetByteCount")
            || method.StartsWith("Encode", StringComparison.Ordinal)
            || method.StartsWith("Transcode", StringComparison.Ordinal)
            || method is "ToUtf8" or "EncodeHelper"
            || (type == "Array"
                && method is "Copy" or "Clear" or "Resize" or "Fill")
            || (type == "Buffer"
                && method.StartsWith("Block", StringComparison.Ordinal))
            || type is "Unsafe" or "MemoryMarshal"
            || (type == "String" && method == ".ctor")
            || (!IsStreamLike(type)
                && method == "CopyTo")
            || method.StartsWith("TryFormat", StringComparison.Ordinal)
            || method.StartsWith("Format", StringComparison.Ordinal)
            || method.StartsWith("WriteString", StringComparison.Ordinal)
            || method.StartsWith("WriteValue", StringComparison.Ordinal)
            || method is "WriteStringByOptions" or "WriteStringByOptionsPropertyName")
        {
            return ResourceBoundaryKind.InMemoryTransform;
        }

        return ResourceBoundaryKind.Unknown;
    }

    static bool IsStreamLike(string type)
        => type == "Stream"
            || type.EndsWith("Stream", StringComparison.Ordinal);

    static string TypeName(TypeRef type)
    {
        TypeRef definition =
            type.Kind == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
        int tick = definition.Name.IndexOf('`');
        return tick >= 0
            ? definition.Name[..tick]
            : definition.Name;
    }

    const int InitialFingerprintLength = 16;
    const int MaximumFingerprintLength = 64;

    static string CreateCandidateId(
        Finding<ResourceLifecycleOccurrence> finding,
        int fingerprintLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            fingerprintLength,
            InitialFingerprintLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            fingerprintLength,
            MaximumFingerprintLength);

        const string fingerprintNamespace = "dotnet-inspect.resource-triage.v1\n";
        ResourceLifecycleOccurrence occurrence = finding.Payload;
        string coordinate =
            $"0x{occurrence.Method.MetadataToken:X8}+0x{occurrence.AcquireOffset:X4}";
        byte[] input = Encoding.UTF8.GetBytes(
            $"{fingerprintNamespace}{Part(finding.Descriptor.Id)}\n"
            + $"{Part(finding.Key.IdentityKey)}\n"
            + $"{Part(coordinate)}");
        string fingerprint = Convert.ToHexString(SHA256.HashData(input))
            .ToLowerInvariant();
        return $"rt~{fingerprint[..fingerprintLength]}";
    }

    static string Part(string value)
        => $"{value.Length.ToString(CultureInfo.InvariantCulture)}:{value}";
}
