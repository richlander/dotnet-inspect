using System.Formats.Asn1;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace NuGetFetch;

/// <summary>
/// Verifies NuGet package signatures using the embedded trusted root certificates.
/// Inspired by the NuGet client's signing verification (Apache 2.0 licensed).
/// </summary>
public static class PackageSignatureVerifier
{
    private const string SignatureFileName = ".signature.p7s";

    // NuGet signing OIDs
    private const string CommitmentTypeIndicationOid = "1.2.840.113549.1.9.16.2.16";
    private const string AuthorCommitmentOid = "1.2.840.113549.1.9.16.6.1";     // proof of origin
    private const string RepositoryCommitmentOid = "1.2.840.113549.1.9.16.6.2";  // proof of receipt
    private const string TimestampTokenOid = "1.2.840.113549.1.9.16.2.14";

    /// <summary>
    /// Verifies the signature of a .nupkg file on disk.
    /// </summary>
    public static SignatureVerificationResult VerifyPackage(string nupkgPath)
    {
        using FileStream stream = File.OpenRead(nupkgPath);
        return VerifyPackage(stream);
    }

    /// <summary>
    /// Verifies the signature of a .nupkg stream. The stream must be seekable.
    /// </summary>
    public static SignatureVerificationResult VerifyPackage(Stream nupkgStream)
    {
        byte[]? signatureBytes = ExtractSignature(nupkgStream);

        if (signatureBytes is null)
        {
            return new SignatureVerificationResult(SignatureStatus.Unsigned, Reason: null);
        }

        return VerifySignature(signatureBytes);
    }

    /// <summary>
    /// Verifies a raw .signature.p7s file (e.g. from an extracted package cache).
    /// </summary>
    public static SignatureVerificationResult VerifySignatureFile(string signaturePath)
    {
        byte[] signatureBytes = File.ReadAllBytes(signaturePath);
        return VerifySignature(signatureBytes);
    }

    private static byte[]? ExtractSignature(Stream nupkgStream)
    {
        using ZipArchive archive = new(nupkgStream, ZipArchiveMode.Read, leaveOpen: true);
        ZipArchiveEntry? signatureEntry = archive.GetEntry(SignatureFileName);

        if (signatureEntry is null)
            return null;

        using Stream entryStream = signatureEntry.Open();
        using MemoryStream ms = new();
        entryStream.CopyTo(ms);
        return ms.ToArray();
    }

    private static SignatureVerificationResult VerifySignature(byte[] signatureBytes)
    {
        SignedCms signedCms = new();

        try
        {
            signedCms.Decode(signatureBytes);
        }
        catch (CryptographicException ex)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                $"Failed to decode package signature: {ex.Message}");
        }

        // Verify CMS integrity (signature math is valid, no tampering)
        try
        {
            signedCms.CheckSignature(verifySignatureOnly: true);
        }
        catch (CryptographicException ex)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                $"Package signature integrity check failed: {ex.Message}");
        }

        // Parse the signed content hash for transparency.
        // The CMS ContentInfo contains "Version:1\n\n{OID}-Hash:{base64}\n\n".
        // CheckSignature above cryptographically verifies this content was signed
        // by the certificate holder. Full content hash verification (comparing this
        // hash against the actual package bytes) requires implementing NuGet's
        // SignedPackageArchiveUtility ZIP hashing algorithm, which manipulates raw
        // ZIP bytes — not implemented in this lightweight client.
        string? signedContentHash = TryExtractSignedHash(signedCms.ContentInfo.Content);

        // Extract the signing certificate
        if (signedCms.SignerInfos.Count == 0)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid, "Package signature contains no signer information.");
        }

        SignerInfo signerInfo = signedCms.SignerInfos[0];
        X509Certificate2? signerCert = signerInfo.Certificate;
        if (signerCert is null)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid, "Could not extract signing certificate.");
        }

        // Extract publisher identity from certificate CN
        string? publisher = ExtractCN(signerCert.Subject);
        string thumbprint = signerCert.GetCertHashString(HashAlgorithmName.SHA256);

        // Detect author vs repository signature type
        SignatureType signatureType = DetectSignatureType(signerInfo);

        // Verify timestamp first — needed to decide if expired certs are acceptable
        DateTimeOffset? timestamp = VerifyTimestamp(signerInfo);

        // Build certificate chain. If the cert is expired but a valid timestamp
        // proves it was signed while the cert was still valid, allow it.
        // This matches NuGet client behavior for long-term signature validity.
        SignatureVerificationResult chainResult = VerifyCertificateChain(
            signerCert, signedCms.Certificates, TrustedRoots.CodeSigningRoots,
            verificationTime: timestamp);

        if (!chainResult.IsValid)
            return chainResult;

        return new SignatureVerificationResult(SignatureStatus.Valid, Reason: null)
        {
            Publisher = publisher,
            Thumbprint = thumbprint,
            SignatureType = signatureType,
            Timestamp = timestamp,
            ContentHash = signedContentHash,
            CounterSignature = signatureType == SignatureType.Author
                ? VerifyRepositoryCounterSignature(signerInfo, signedCms.Certificates)
                : null,
        };
    }

    /// <summary>
    /// Parses the NuGet-specific signed content format to extract the package hash.
    /// Format: "Version:1\n\n{OID}-Hash:{base64Hash}\n\n"
    /// </summary>
    private static string? TryExtractSignedHash(byte[]? content)
    {
        if (content is null || content.Length == 0)
        {
            return null;
        }

        try
        {
            string text = System.Text.Encoding.UTF8.GetString(content);
            const string hashMarker = "-Hash:";
            int hashStart = text.IndexOf(hashMarker, StringComparison.Ordinal);

            if (hashStart < 0)
            {
                return null;
            }

            hashStart += hashMarker.Length;
            int hashEnd = text.IndexOf('\n', hashStart);

            return hashEnd < 0 ? text[hashStart..].Trim() : text[hashStart..hashEnd].Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detects whether the primary signature is an author or repository signature
    /// by checking the commitment type indication signed attribute.
    /// </summary>
    private static SignatureType DetectSignatureType(SignerInfo signerInfo)
    {
        foreach (CryptographicAttributeObject attr in signerInfo.SignedAttributes)
        {
            if (attr.Oid?.Value != CommitmentTypeIndicationOid)
                continue;

            foreach (AsnEncodedData value in attr.Values)
            {
                string? oid = TryReadCommitmentTypeOid(value.RawData);
                if (oid == AuthorCommitmentOid)
                    return SignatureType.Author;
                if (oid == RepositoryCommitmentOid)
                    return SignatureType.Repository;
            }
        }

        // No commitment type found — treat as repository (nuget.org default)
        return SignatureType.Repository;
    }

    /// <summary>
    /// Parses the commitment type indication ASN.1 value to extract the OID.
    /// Format: SEQUENCE { OBJECT IDENTIFIER commitmentTypeId, ... }
    /// </summary>
    private static string? TryReadCommitmentTypeOid(byte[] rawData)
    {
        try
        {
            AsnReader reader = new(rawData, AsnEncodingRules.DER);
            AsnReader sequence = reader.ReadSequence();
            return sequence.ReadObjectIdentifier();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies the RFC 3161 timestamp counter-signature if present.
    /// Returns the timestamp value on success, null if absent or invalid.
    /// </summary>
    private static DateTimeOffset? VerifyTimestamp(SignerInfo signerInfo)
    {
        foreach (CryptographicAttributeObject attr in signerInfo.UnsignedAttributes)
        {
            if (attr.Oid?.Value != TimestampTokenOid)
                continue;

            foreach (AsnEncodedData value in attr.Values)
            {
                DateTimeOffset? ts = VerifyTimestampToken(value.RawData);
                if (ts.HasValue)
                    return ts;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts and verifies the repository counter-signature from an author-signed package.
    /// The counter-signature is a standard PKCS#9 counter-signer with a repository commitment type.
    /// </summary>
    private static SignatureVerificationResult? VerifyRepositoryCounterSignature(
        SignerInfo primarySigner, X509Certificate2Collection extraCerts)
    {
        foreach (SignerInfo counterSigner in primarySigner.CounterSignerInfos)
        {
            SignatureType counterType = DetectSignatureType(counterSigner);
            if (counterType != SignatureType.Repository)
                continue;

            X509Certificate2? cert = counterSigner.Certificate;
            if (cert is null)
                continue;

            string? publisher = ExtractCN(cert.Subject);
            string thumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256);
            DateTimeOffset? timestamp = VerifyTimestamp(counterSigner);

            SignatureVerificationResult chainResult = VerifyCertificateChain(
                cert, extraCerts, TrustedRoots.CodeSigningRoots,
                verificationTime: timestamp);

            if (!chainResult.IsValid)
                continue;

            return new SignatureVerificationResult(SignatureStatus.Valid, Reason: null)
            {
                Publisher = publisher,
                Thumbprint = thumbprint,
                SignatureType = SignatureType.Repository,
                Timestamp = timestamp,
            };
        }

        return null;
    }

    /// <summary>
    /// Decodes and verifies an RFC 3161 timestamp token (which is itself a CMS SignedData).
    /// </summary>
    private static DateTimeOffset? VerifyTimestampToken(byte[] tokenBytes)
    {
        try
        {
            SignedCms timestampCms = new();
            timestampCms.Decode(tokenBytes);
            timestampCms.CheckSignature(verifySignatureOnly: true);

            // Verify timestamp signer certificate chain against timestamping roots
            if (timestampCms.SignerInfos.Count > 0)
            {
                X509Certificate2? tsCert = timestampCms.SignerInfos[0].Certificate;
                if (tsCert is not null)
                {
                    SignatureVerificationResult tsChain = VerifyCertificateChain(
                        tsCert, timestampCms.Certificates, TrustedRoots.TimestampingRoots);

                    if (!tsChain.IsValid)
                        return null;
                }
            }

            // Extract the timestamp from the TSTInfo content
            return TryExtractTimestamp(timestampCms.ContentInfo.Content);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the genTime from an RFC 3161 TSTInfo structure.
    /// TSTInfo ::= SEQUENCE { version INTEGER, policy OBJECT IDENTIFIER,
    ///   messageImprint MessageImprint, serialNumber INTEGER, genTime GeneralizedTime, ... }
    /// </summary>
    private static DateTimeOffset? TryExtractTimestamp(byte[]? tstInfoBytes)
    {
        if (tstInfoBytes is null || tstInfoBytes.Length == 0)
            return null;

        try
        {
            AsnReader reader = new(tstInfoBytes, AsnEncodingRules.DER);
            AsnReader sequence = reader.ReadSequence();
            sequence.ReadInteger(); // version
            sequence.ReadObjectIdentifier(); // policy
            sequence.ReadSequence(); // messageImprint (skip)
            sequence.ReadInteger(); // serialNumber
            return sequence.ReadGeneralizedTime();
        }
        catch
        {
            return null;
        }
    }

    private static SignatureVerificationResult VerifyCertificateChain(
        X509Certificate2 signerCert,
        X509Certificate2Collection extraCerts,
        X509Certificate2Collection trustedRoots,
        DateTimeOffset? verificationTime = null)
    {
        using X509Chain chain = new();

        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(trustedRoots);
        chain.ChainPolicy.ExtraStore.AddRange(extraCerts);

        // Disable revocation checking — matches NuGet SDK behavior for offline scenarios
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        // When a timestamp is available, verify the chain at signing time
        // so expired certificates are accepted if they were valid when signing occurred
        if (verificationTime.HasValue)
            chain.ChainPolicy.VerificationTime = verificationTime.Value.UtcDateTime;

        bool isValid = chain.Build(signerCert);

        if (isValid)
        {
            return new SignatureVerificationResult(SignatureStatus.Valid, Reason: null);
        }

        // Collect chain status for diagnostics
        List<string> issues = new();
        foreach (X509ChainStatus status in chain.ChainStatus)
        {
            issues.Add($"{status.Status}: {status.StatusInformation}");
        }

        string reason = string.Join("; ", issues);
        return new SignatureVerificationResult(SignatureStatus.Invalid, reason);
    }

    /// <summary>
    /// Extracts the CN (Common Name) from an X.509 subject string.
    /// </summary>
    private static string? ExtractCN(string subject)
    {
        // Subject format: "CN=Name, O=Org, L=City, ..."
        const string prefix = "CN=";
        int start = subject.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += prefix.Length;
        int end = subject.IndexOf(',', start);
        return end < 0 ? subject[start..].Trim() : subject[start..end].Trim();
    }
}

/// <summary>
/// The type of NuGet package signature.
/// </summary>
public enum SignatureType
{
    /// <summary>Package signed by the author.</summary>
    Author,

    /// <summary>Package signed by the repository (e.g., nuget.org).</summary>
    Repository,
}

public enum SignatureStatus
{
    Valid,
    Unsigned,
    Invalid,
}

public record SignatureVerificationResult(SignatureStatus Status, string? Reason)
{
    public bool IsValid => Status == SignatureStatus.Valid;
    public bool IsUnsigned => Status == SignatureStatus.Unsigned;

    /// <summary>Publisher identity extracted from the signing certificate CN.</summary>
    public string? Publisher { get; init; }

    /// <summary>SHA-256 thumbprint of the signing certificate.</summary>
    public string? Thumbprint { get; init; }

    /// <summary>Whether this is an author or repository signature.</summary>
    public SignatureType SignatureType { get; init; }

    /// <summary>RFC 3161 timestamp from the counter-signature, if present and valid.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// Base64-encoded content hash from the signed data.
    /// This is the hash the signer committed to. Full content hash verification
    /// (comparing against actual package bytes) requires NuGet's ZIP hashing algorithm.
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// Repository counter-signature, present when the primary signature is an author signature
    /// and the repository (e.g. nuget.org) has counter-signed the package.
    /// </summary>
    public SignatureVerificationResult? CounterSignature { get; init; }
}
