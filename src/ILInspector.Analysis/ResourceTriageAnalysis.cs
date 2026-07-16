using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ILInspector.Findings;

namespace ILInspector.Analysis;

public enum ResourceTriageBoundaryKind
{
    Unknown,
    ExternalInput,
    InMemoryTransform,
}

public enum ResourceTriageActionability
{
    Unknown,
    TrustedLowActionability,
    UntrustedActionable,
}

public enum ResourceTriageReason
{
    NoBoundaryBeforeCleanup,
    UnclassifiedBoundaryBeforeCleanup,
    InMemoryBoundaryBeforeCleanup,
    ExternalInputBoundaryBeforeCleanup,
}

public enum ResourceTriageImpact
{
    PoolChurnOnException,
}

public enum ResourceTriageRemediation
{
    EnsureExceptionalCleanup,
}

public enum ResourceTriageConfidence
{
    Medium,
}

public readonly record struct ResourceTriageBoundaryAssessment(
    ResourceBoundaryEvidence Evidence,
    ResourceTriageBoundaryKind Kind);

public sealed record ResourceTriageAssessment
{
    ImmutableArray<ResourceTriageBoundaryAssessment> _boundaries;

    public ResourceTriageAssessment(
        string CandidateId,
        Finding<ResourceLifecycleOccurrence> Source,
        ImmutableArray<ResourceTriageBoundaryAssessment> Boundaries,
        ResourceTriageActionability Actionability,
        ResourceTriageReason Reason,
        ResourceTriageImpact Impact,
        ResourceTriageRemediation Remediation,
        ResourceTriageConfidence Confidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CandidateId);
        this.Source = Source ?? throw new ArgumentNullException(nameof(Source));
        this.CandidateId = CandidateId;
        _boundaries = ImmutableArrayValueEquality.RequireInitialized(
            Boundaries,
            nameof(Boundaries));
        this.Actionability = Actionability;
        this.Reason = Reason;
        this.Impact = Impact;
        this.Remediation = Remediation;
        this.Confidence = Confidence;
    }

    public string CandidateId { get; }
    public Finding<ResourceLifecycleOccurrence> Source { get; }
    public ImmutableArray<ResourceTriageBoundaryAssessment> Boundaries
    {
        get => _boundaries;
        init => _boundaries = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Boundaries));
    }
    public ResourceTriageActionability Actionability { get; }
    public ResourceTriageReason Reason { get; }
    public ResourceTriageImpact Impact { get; }
    public ResourceTriageRemediation Remediation { get; }
    public ResourceTriageConfidence Confidence { get; }

    public bool Equals(ResourceTriageAssessment? other)
        => other is not null
            && string.Equals(CandidateId, other.CandidateId, StringComparison.Ordinal)
            && Source == other.Source
            && ImmutableArrayValueEquality.SequenceEqual(Boundaries, other.Boundaries)
            && Actionability == other.Actionability
            && Reason == other.Reason
            && Impact == other.Impact
            && Remediation == other.Remediation
            && Confidence == other.Confidence;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CandidateId, StringComparer.Ordinal);
        hash.Add(Source);
        ImmutableArrayValueEquality.AddToHash(ref hash, Boundaries);
        hash.Add(Actionability);
        hash.Add(Reason);
        hash.Add(Impact);
        hash.Add(Remediation);
        hash.Add(Confidence);
        return hash.ToHashCode();
    }
}

public static class ResourceTriageAnalysis
{
    public static ImmutableArray<ResourceTriageAssessment> Assess(
        FindingInspection<ResourceLifecycleOccurrence>.Complete inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        var assessments =
            ImmutableArray.CreateBuilder<ResourceTriageAssessment>(
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

            assessments.Add(Assess(finding, candidateId));
        }

        return assessments.MoveToImmutable();
    }

    static ResourceTriageAssessment Assess(
        Finding<ResourceLifecycleOccurrence> finding,
        string candidateId)
    {
        var boundaries = finding.Payload.Boundaries
            .Select(boundary => new ResourceTriageBoundaryAssessment(
                boundary,
                ClassifyBoundary(boundary.Operation)))
            .ToImmutableArray();

        ResourceTriageActionability actionability;
        ResourceTriageReason reason;
        if (boundaries.Any(
            static boundary =>
                boundary.Kind == ResourceTriageBoundaryKind.ExternalInput))
        {
            actionability = ResourceTriageActionability.UntrustedActionable;
            reason = ResourceTriageReason.ExternalInputBoundaryBeforeCleanup;
        }
        else if (boundaries.Length == 0)
        {
            actionability = ResourceTriageActionability.Unknown;
            reason = ResourceTriageReason.NoBoundaryBeforeCleanup;
        }
        else if (boundaries.Any(
            static boundary =>
                boundary.Kind == ResourceTriageBoundaryKind.Unknown))
        {
            actionability = ResourceTriageActionability.Unknown;
            reason = ResourceTriageReason.UnclassifiedBoundaryBeforeCleanup;
        }
        else
        {
            actionability = ResourceTriageActionability.TrustedLowActionability;
            reason = ResourceTriageReason.InMemoryBoundaryBeforeCleanup;
        }

        return new ResourceTriageAssessment(
            candidateId,
            finding,
            boundaries,
            actionability,
            reason,
            ResourceTriageImpact.PoolChurnOnException,
            ResourceTriageRemediation.EnsureExceptionalCleanup,
            ResourceTriageConfidence.Medium);
    }

    static ResourceTriageBoundaryKind ClassifyBoundary(MemberRef member)
    {
        TypeRef declaringType = member.DeclaringType;
        string type = SimpleTypeName(declaringType);
        string method = member.Name;

        if ((IsStreamLike(declaringType)
                && method.StartsWith("Read", StringComparison.Ordinal))
            || ((IsType(declaringType, "System.Text", "Decoder")
                    || HasTypeNameSuffix(declaringType, "Decoder"))
                && method is "GetChars" or "GetString" or "Convert")
            || ((IsType(declaringType, "System.Text", "Encoding")
                    || HasTypeNameSuffix(declaringType, "Encoding"))
                && method is "GetChars" or "GetString")
            || (IsType(declaringType, "System.Net.Sockets", "Socket")
                && method.StartsWith("Receive", StringComparison.Ordinal))
            || ((IsType(declaringType, "System.IO", "TextReader")
                    || IsType(declaringType, "System.IO", "StreamReader")
                    || IsType(declaringType, "System.IO", "BinaryReader"))
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
            return ResourceTriageBoundaryKind.ExternalInput;
        }

        if (method.StartsWith("Escape", StringComparison.Ordinal)
            || ((IsType(declaringType, "System.Text", "Encoding")
                    || HasTypeNameSuffix(declaringType, "Encoding"))
                && method is "GetBytes" or "GetByteCount")
            || method.StartsWith("Encode", StringComparison.Ordinal)
            || method.StartsWith("Transcode", StringComparison.Ordinal)
            || method is "ToUtf8" or "EncodeHelper"
            || (IsType(declaringType, "System", "Array")
                && method is "Copy" or "Clear" or "Resize" or "Fill")
            || (IsType(declaringType, "System", "Buffer")
                && method.StartsWith("Block", StringComparison.Ordinal))
            || IsType(
                declaringType,
                "System.Runtime.CompilerServices",
                "Unsafe")
            || IsType(
                declaringType,
                "System.Runtime.InteropServices",
                "MemoryMarshal")
            || (IsType(declaringType, "System", "String")
                && method == ".ctor")
            || (!IsStreamLike(declaringType)
                && method == "CopyTo")
            || method.StartsWith("TryFormat", StringComparison.Ordinal)
            || method.StartsWith("Format", StringComparison.Ordinal)
            || method.StartsWith("WriteString", StringComparison.Ordinal)
            || method.StartsWith("WriteValue", StringComparison.Ordinal)
            || method is "WriteStringByOptions" or "WriteStringByOptionsPropertyName")
        {
            return ResourceTriageBoundaryKind.InMemoryTransform;
        }

        return ResourceTriageBoundaryKind.Unknown;
    }

    static bool IsStreamLike(TypeRef type)
        => IsType(type, "System.IO", "Stream")
            || HasTypeNameSuffix(type, "Stream");

    static bool IsType(TypeRef type, string @namespace, string name)
    {
        TypeRef definition = TypeDefinition(type);
        return string.Equals(
                definition.Namespace,
                @namespace,
                StringComparison.Ordinal)
            && string.Equals(definition.Name, name, StringComparison.Ordinal);
    }

    static bool HasTypeNameSuffix(TypeRef type, string suffix)
    {
        string name = SimpleTypeName(type);
        return name.Length > suffix.Length
            && name.EndsWith(suffix, StringComparison.Ordinal);
    }

    static string SimpleTypeName(TypeRef type)
    {
        TypeRef definition = TypeDefinition(type);
        int tick = definition.Name.IndexOf('`');
        return tick >= 0
            ? definition.Name[..tick]
            : definition.Name;
    }

    static TypeRef TypeDefinition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;

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
