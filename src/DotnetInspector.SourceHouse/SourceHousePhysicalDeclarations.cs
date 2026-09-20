using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

using CSharpText;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.SourceHouse;

public sealed class SourceHouseResultIdentity
{
    internal SourceHouseResultIdentity()
    {
    }

    public override string ToString() => nameof(SourceHouseResultIdentity);
}

public sealed class SourceHousePhysicalSourceInputIdentity
{
    private SourceHousePhysicalSourceInputIdentity(Guid value) => Value = value;

    internal Guid Value { get; }

    public static SourceHousePhysicalSourceInputIdentity Create() =>
        new(Guid.NewGuid());

    public override string ToString() =>
        nameof(SourceHousePhysicalSourceInputIdentity);
}

public sealed class SourceHouseAttestationIssuerIdentity
{
    private SourceHouseAttestationIssuerIdentity(string name) => Name = name;

    public string Name { get; }

    public static SourceHouseAttestationIssuerIdentity Create(string name) =>
        new(SourceHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public sealed class SourceHouseAttestationProfileIdentity
{
    private SourceHouseAttestationProfileIdentity(string name) => Name = name;

    public string Name { get; }

    public static SourceHouseAttestationProfileIdentity Create(string name) =>
        new(SourceHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public sealed class SourceHouseAttestationGeneration
{
    private SourceHouseAttestationGeneration(string name) => Name = name;

    public string Name { get; }

    public static SourceHouseAttestationGeneration Create(string name) =>
        new(SourceHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public sealed record SourceHouseSha256Digest
{
    public const int ByteLength = 32;

    public SourceHouseSha256Digest(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException(
                "A SHA-256 digest must contain exactly 32 bytes.",
                nameof(bytes));
        }

        Bytes = ImmutableArray.CreateRange(bytes.ToArray());
    }

    public ImmutableArray<byte> Bytes { get; }

    public static SourceHouseSha256Digest Compute(ReadOnlySpan<byte> content) =>
        new(SHA256.HashData(content));

    public bool Matches(SourceHouseSha256Digest other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Bytes.AsSpan().SequenceEqual(other.Bytes.AsSpan());
    }
}

public enum SourceHousePhysicalSourceEncoding
{
    Utf8,
    Utf8WithByteOrderMark,
    Utf16LittleEndian,
    Utf16BigEndian,
    Utf32LittleEndian,
    Utf32BigEndian,
}

public static class SourceHousePhysicalSourceEncodings
{
    public static SourceHousePhysicalSourceEncoding Detect(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
            return SourceHousePhysicalSourceEncoding.Utf32BigEndian;
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
            return SourceHousePhysicalSourceEncoding.Utf32LittleEndian;
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return SourceHousePhysicalSourceEncoding.Utf8WithByteOrderMark;
        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
            return SourceHousePhysicalSourceEncoding.Utf16BigEndian;
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
            return SourceHousePhysicalSourceEncoding.Utf16LittleEndian;
        return SourceHousePhysicalSourceEncoding.Utf8;
    }

    public static bool IsValid(
        ReadOnlySpan<byte> bytes,
        SourceHousePhysicalSourceEncoding encoding)
    {
        try
        {
            _ = StrictEncoding(encoding).GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static Encoding StrictEncoding(
        SourceHousePhysicalSourceEncoding encoding) =>
        encoding switch
        {
            SourceHousePhysicalSourceEncoding.Utf8
                or SourceHousePhysicalSourceEncoding
                    .Utf8WithByteOrderMark =>
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true),
            SourceHousePhysicalSourceEncoding.Utf16LittleEndian =>
                new UnicodeEncoding(
                    bigEndian: false,
                    byteOrderMark: true,
                    throwOnInvalidBytes: true),
            SourceHousePhysicalSourceEncoding.Utf16BigEndian =>
                new UnicodeEncoding(
                    bigEndian: true,
                    byteOrderMark: true,
                    throwOnInvalidBytes: true),
            SourceHousePhysicalSourceEncoding.Utf32LittleEndian =>
                new UTF32Encoding(
                    bigEndian: false,
                    byteOrderMark: true,
                    throwOnInvalidCharacters: true),
            SourceHousePhysicalSourceEncoding.Utf32BigEndian =>
                new UTF32Encoding(
                    bigEndian: true,
                    byteOrderMark: true,
                    throwOnInvalidCharacters: true),
            _ => throw new ArgumentOutOfRangeException(
                nameof(encoding)),
        };
}

public readonly record struct SourceHousePhysicalDeclarationSpan
{
    public SourceHousePhysicalDeclarationSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Start = start;
        Length = length;
    }

    public int Start { get; }
    public int Length { get; }
    public int End => checked(Start + Length);
}

public sealed class SourceHouseDeclarationSyntaxKind
{
    private SourceHouseDeclarationSyntaxKind(string name) => Name = name;

    public string Name { get; }

    public static SourceHouseDeclarationSyntaxKind Create(string name) =>
        new(SourceHouseContractName.Validate(name));

    public override string ToString() => Name;
}

public abstract record SourceHousePhysicalTargetAddress
{
    private protected SourceHousePhysicalTargetAddress()
    {
    }

    public abstract Guid ModuleVersionId { get; }

    public sealed record Type(MetadataTypeDefinitionAddress Address)
        : SourceHousePhysicalTargetAddress
    {
        public override Guid ModuleVersionId => Address.ModuleVersionId;
    }

    public sealed record Method(MetadataMethodAddress Address)
        : SourceHousePhysicalTargetAddress
    {
        public override Guid ModuleVersionId => Address.ModuleVersionId;
    }
}

public sealed record SourceHousePhysicalTargetEvidence
{
    public SourceHousePhysicalTargetEvidence(
        SourceHouseSha256Digest moduleDigest,
        SourceHousePhysicalTargetAddress address,
        XmlDocMemberIdentity? xmlDocumentationIdentity)
    {
        ArgumentNullException.ThrowIfNull(moduleDigest);
        ArgumentNullException.ThrowIfNull(address);

        ModuleDigest = moduleDigest;
        Address = address;
        XmlDocumentationIdentity = xmlDocumentationIdentity;
    }

    public SourceHouseSha256Digest ModuleDigest { get; }
    public SourceHousePhysicalTargetAddress Address { get; }
    public XmlDocMemberIdentity? XmlDocumentationIdentity { get; }
    public Guid ModuleVersionId => Address.ModuleVersionId;
}

public sealed record SourceHousePhysicalSourceEvidence
{
    internal SourceHousePhysicalSourceEvidence(
        SourceHouseResultIdentity result,
        SourceHousePhysicalSourceInputIdentity input,
        SourceHouseSha256Digest contentDigest,
        SourceHousePhysicalSourceEncoding encoding,
        int rawUtf16Length)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(contentDigest);
        ArgumentOutOfRangeException.ThrowIfNegative(rawUtf16Length);

        Result = result;
        Input = input;
        ContentDigest = contentDigest;
        Encoding = encoding;
        RawUtf16Length = rawUtf16Length;
    }

    public SourceHouseResultIdentity Result { get; }
    public SourceHousePhysicalSourceInputIdentity Input { get; }
    public SourceHouseSha256Digest ContentDigest { get; }
    public SourceHousePhysicalSourceEncoding Encoding { get; }
    public int RawUtf16Length { get; }
}

public sealed record SourceHousePhysicalDeclarationAttestation
{
    public SourceHousePhysicalDeclarationAttestation(
        SourceHouseRequestIdentity request,
        LibraryReference library,
        LibraryContentReference selectedAssembly,
        SourceHouseOperationPlanIdentity operationPlan,
        SourceHousePolicyGeneration policyGeneration,
        SourceHouseAttestationIssuerIdentity issuer,
        SourceHouseAttestationProfileIdentity profile,
        SourceHouseAttestationGeneration generation,
        SourceHouseSha256Digest moduleDigest,
        SourceHousePhysicalTargetAddress target,
        XmlDocMemberIdentity xmlDocumentationIdentity,
        SourceHouseResultIdentity sourceResult,
        SourceHousePhysicalSourceInputIdentity sourceInput,
        SourceHouseSha256Digest sourceDigest,
        SourceHousePhysicalSourceEncoding sourceEncoding,
        SourceHousePhysicalDeclarationSpan span,
        SourceHouseDeclarationSyntaxKind syntaxKind)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(selectedAssembly);
        ArgumentNullException.ThrowIfNull(operationPlan);
        ArgumentNullException.ThrowIfNull(policyGeneration);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(moduleDigest);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(xmlDocumentationIdentity);
        ArgumentNullException.ThrowIfNull(sourceResult);
        ArgumentNullException.ThrowIfNull(sourceInput);
        ArgumentNullException.ThrowIfNull(sourceDigest);
        ArgumentNullException.ThrowIfNull(syntaxKind);

        Request = request;
        Library = library;
        SelectedAssembly = selectedAssembly;
        OperationPlan = operationPlan;
        PolicyGeneration = policyGeneration;
        Issuer = issuer;
        Profile = profile;
        Generation = generation;
        ModuleDigest = moduleDigest;
        Target = target;
        XmlDocumentationIdentity = xmlDocumentationIdentity;
        SourceResult = sourceResult;
        SourceInput = sourceInput;
        SourceDigest = sourceDigest;
        SourceEncoding = sourceEncoding;
        Span = span;
        SyntaxKind = syntaxKind;
    }

    public SourceHouseRequestIdentity Request { get; }
    public LibraryReference Library { get; }
    public LibraryContentReference SelectedAssembly { get; }
    public SourceHouseOperationPlanIdentity OperationPlan { get; }
    public SourceHousePolicyGeneration PolicyGeneration { get; }
    public SourceHouseAttestationIssuerIdentity Issuer { get; }
    public SourceHouseAttestationProfileIdentity Profile { get; }
    public SourceHouseAttestationGeneration Generation { get; }
    public SourceHouseSha256Digest ModuleDigest { get; }
    public SourceHousePhysicalTargetAddress Target { get; }
    public XmlDocMemberIdentity XmlDocumentationIdentity { get; }
    public SourceHouseResultIdentity SourceResult { get; }
    public SourceHousePhysicalSourceInputIdentity SourceInput { get; }
    public SourceHouseSha256Digest SourceDigest { get; }
    public SourceHousePhysicalSourceEncoding SourceEncoding { get; }
    public SourceHousePhysicalDeclarationSpan Span { get; }
    public SourceHouseDeclarationSyntaxKind SyntaxKind { get; }
}

public sealed record SourceHousePhysicalDeclarationRequest
{
    public SourceHousePhysicalDeclarationRequest(
        SourceHouseRequestIdentity request,
        LibraryReference library,
        LibraryContentReference selectedAssembly,
        SourceHouseOperationPlanIdentity operationPlan,
        SourceHousePolicyGeneration policyGeneration,
        SourceHousePhysicalTargetEvidence target,
        SourceHousePhysicalSourceEvidence source)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(selectedAssembly);
        ArgumentNullException.ThrowIfNull(operationPlan);
        ArgumentNullException.ThrowIfNull(policyGeneration);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        if (target.XmlDocumentationIdentity is null)
        {
            throw new ArgumentException(
                "The physical target must have a compiler XML identity.",
                nameof(target));
        }

        Request = request;
        Library = library;
        SelectedAssembly = selectedAssembly;
        OperationPlan = operationPlan;
        PolicyGeneration = policyGeneration;
        Target = target;
        Source = source;
    }

    public SourceHouseRequestIdentity Request { get; }
    public LibraryReference Library { get; }
    public LibraryContentReference SelectedAssembly { get; }
    public SourceHouseOperationPlanIdentity OperationPlan { get; }
    public SourceHousePolicyGeneration PolicyGeneration { get; }
    public SourceHousePhysicalTargetEvidence Target { get; }
    public SourceHousePhysicalSourceEvidence Source { get; }
}

public abstract class SourceHouseAttestationCapabilityOutcome
{
    private protected SourceHouseAttestationCapabilityOutcome(
        SourceHouseCapabilityObservation? observation) =>
        Observation = observation;

    public SourceHouseCapabilityObservation? Observation { get; }

    public sealed class Available : SourceHouseAttestationCapabilityOutcome
    {
        public Available(
            IReadOnlyList<SourceHousePhysicalDeclarationAttestation>
                attestations,
            SourceHouseCapabilityObservation? observation = null)
            : base(observation)
        {
            ArgumentNullException.ThrowIfNull(attestations);
            if (attestations.Any(static value => value is null))
            {
                throw new ArgumentException(
                    "Attestation contributions cannot contain null.",
                    nameof(attestations));
            }

            Attestations = ImmutableArray.CreateRange(attestations);
        }

        public IReadOnlyList<SourceHousePhysicalDeclarationAttestation>
            Attestations
        { get; }
    }

    public sealed class Unavailable(
        SourceHouseCapabilityObservation observation)
        : SourceHouseAttestationCapabilityOutcome(
            observation
            ?? throw new ArgumentNullException(nameof(observation)))
    {
    }

    public sealed class Rejected(
        SourceHouseCapabilityObservation observation)
        : SourceHouseAttestationCapabilityOutcome(
            observation
            ?? throw new ArgumentNullException(nameof(observation)))
    {
    }

    public sealed class Failed(
        SourceHouseCapabilityObservation observation)
        : SourceHouseAttestationCapabilityOutcome(
            observation
            ?? throw new ArgumentNullException(nameof(observation)))
    {
    }

    public sealed class Incomplete
        : SourceHouseAttestationCapabilityOutcome
    {
        public Incomplete(
            SourceHouseCapabilityObservation observation,
            int contributionsObserved = 0)
            : base(
                observation
                ?? throw new ArgumentNullException(nameof(observation)))
        {
            ArgumentOutOfRangeException.ThrowIfNegative(
                contributionsObserved);
            ContributionsObserved = contributionsObserved;
        }

        public int ContributionsObserved { get; }
    }
}

public interface ISourceHousePhysicalDeclarationCapability
    : ISourceHouseSourceCapability
{
    SourceHouseAttestationIssuerIdentity Issuer { get; }
    SourceHouseAttestationProfileIdentity Profile { get; }
    SourceHouseAttestationGeneration Generation { get; }

    ValueTask<SourceHouseAttestationCapabilityOutcome> ReadAttestationsAsync(
        SourceHousePhysicalDeclarationRequest request,
        int maximumContributions,
        CancellationToken cancellationToken);
}

public enum SourceHousePhysicalDeclarationOutcomeKind
{
    Exact,
    Unavailable,
    Conflict,
    Rejected,
    Failed,
    Incomplete,
}

public sealed class SourceHousePhysicalDeclarationReceiptIdentity
{
    internal SourceHousePhysicalDeclarationReceiptIdentity()
    {
    }

    public override string ToString() =>
        nameof(SourceHousePhysicalDeclarationReceiptIdentity);
}

public sealed class SourceHousePhysicalDeclarationGeneration
{
    internal SourceHousePhysicalDeclarationGeneration()
    {
    }

    public override string ToString() =>
        nameof(SourceHousePhysicalDeclarationGeneration);
}

public sealed record SourceHousePhysicalDeclarationReceipt
{
    internal SourceHousePhysicalDeclarationReceipt(
        SourceHouseCapabilityIdentity? capability,
        SourceHouseAttestationIssuerIdentity? issuer,
        SourceHouseAttestationProfileIdentity? profile,
        SourceHouseAttestationGeneration? attestationGeneration,
        SourceHousePhysicalTargetEvidence target,
        SourceHousePhysicalSourceEvidence? source,
        int contributionsObserved)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(
            contributionsObserved);

        Identity =
            new SourceHousePhysicalDeclarationReceiptIdentity();
        Generation =
            new SourceHousePhysicalDeclarationGeneration();
        Capability = capability;
        Issuer = issuer;
        Profile = profile;
        AttestationGeneration = attestationGeneration;
        Target = target;
        Source = source;
        ContributionsObserved = contributionsObserved;
    }

    public SourceHousePhysicalDeclarationReceiptIdentity Identity { get; }
    public SourceHousePhysicalDeclarationGeneration Generation { get; }
    public SourceHouseCapabilityIdentity? Capability { get; }
    public SourceHouseAttestationIssuerIdentity? Issuer { get; }
    public SourceHouseAttestationProfileIdentity? Profile { get; }
    public SourceHouseAttestationGeneration? AttestationGeneration { get; }
    public SourceHousePhysicalTargetEvidence Target { get; }
    public SourceHousePhysicalSourceEvidence? Source { get; }
    public int ContributionsObserved { get; }
}

public sealed record SourceHousePhysicalDeclarationClaim(
    SourceHousePhysicalDeclarationSpan Span,
    SourceHouseDeclarationSyntaxKind SyntaxKind);

public abstract class SourceHousePhysicalDeclarationOutcome
{
    private protected SourceHousePhysicalDeclarationOutcome(
        SourceHousePhysicalDeclarationOutcomeKind kind,
        SourceHousePhysicalDeclarationReceipt receipt,
        SourceHouseCapabilityObservation? observation)
    {
        Kind = kind;
        Receipt = receipt;
        Observation = observation;
    }

    public SourceHousePhysicalDeclarationOutcomeKind Kind { get; }
    public SourceHousePhysicalDeclarationReceipt Receipt { get; }
    public SourceHouseCapabilityObservation? Observation { get; }

    public sealed class Exact : SourceHousePhysicalDeclarationOutcome
    {
        internal Exact(
            SourceHousePhysicalDeclarationReceipt receipt,
            SourceHousePhysicalDeclarationSpan span,
            SourceHouseDeclarationSyntaxKind syntaxKind)
            : base(
                SourceHousePhysicalDeclarationOutcomeKind.Exact,
                receipt,
                observation: null)
        {
            Span = span;
            SyntaxKind = syntaxKind
                ?? throw new ArgumentNullException(nameof(syntaxKind));
        }

        public SourceHousePhysicalDeclarationSpan Span { get; }
        public SourceHouseDeclarationSyntaxKind SyntaxKind { get; }
    }

    public sealed class Unavailable
        : SourceHousePhysicalDeclarationOutcome
    {
        internal Unavailable(
            SourceHousePhysicalDeclarationReceipt receipt,
            SourceHouseCapabilityObservation observation)
            : base(
                SourceHousePhysicalDeclarationOutcomeKind.Unavailable,
                receipt,
                observation)
        {
        }
    }

    public sealed class Conflict : SourceHousePhysicalDeclarationOutcome
    {
        internal Conflict(
            SourceHousePhysicalDeclarationReceipt receipt,
            SourceHouseCapabilityObservation observation,
            IReadOnlyList<SourceHousePhysicalDeclarationClaim> claims)
            : base(
                SourceHousePhysicalDeclarationOutcomeKind.Conflict,
                receipt,
                observation)
        {
            Claims = ImmutableArray.CreateRange(
                claims
                ?? throw new ArgumentNullException(nameof(claims)));
        }

        public IReadOnlyList<SourceHousePhysicalDeclarationClaim> Claims
        { get; }
    }

    public sealed class Rejected : SourceHousePhysicalDeclarationOutcome
    {
        internal Rejected(
            SourceHousePhysicalDeclarationReceipt receipt,
            SourceHouseCapabilityObservation observation)
            : base(
                SourceHousePhysicalDeclarationOutcomeKind.Rejected,
                receipt,
                observation)
        {
        }
    }

    public sealed class Failed : SourceHousePhysicalDeclarationOutcome
    {
        internal Failed(
            SourceHousePhysicalDeclarationReceipt receipt,
            SourceHouseCapabilityObservation observation)
            : base(
                SourceHousePhysicalDeclarationOutcomeKind.Failed,
                receipt,
                observation)
        {
        }
    }

    public sealed class Incomplete : SourceHousePhysicalDeclarationOutcome
    {
        internal Incomplete(
            SourceHousePhysicalDeclarationReceipt receipt,
            SourceHouseCapabilityObservation observation)
            : base(
                SourceHousePhysicalDeclarationOutcomeKind.Incomplete,
                receipt,
                observation)
        {
        }
    }
}
