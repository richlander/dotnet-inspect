using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace NuGetFetch;

public static partial class PackageSignatureVerifier
{
    private static bool HasValidNuGetSignerAttributes(
        SignerInfo signerInfo,
        X509Certificate2 certificate)
    {
        if (HasSignedAttribute(signerInfo, SigningCertificateOid)
            || !TryGetSingleSignedAttribute(
                signerInfo,
                SigningTimeOid,
                out AsnEncodedData signingTime)
            || !IsValidSigningTime(signingTime.RawData)
            || !TryGetSingleSignedAttribute(
                signerInfo,
                SigningCertificateV2Oid,
                out AsnEncodedData signingCertificateV2))
        {
            return false;
        }

        return SigningCertificateV2Matches(
            signingCertificateV2.RawData,
            certificate);
    }

    private static bool HasSignedAttribute(SignerInfo signerInfo, string oid)
    {
        foreach (CryptographicAttributeObject attribute in signerInfo.SignedAttributes)
        {
            if (attribute.Oid?.Value == oid)
                return true;
        }

        return false;
    }

    private static bool TryGetSingleSignedAttribute(
        SignerInfo signerInfo,
        string oid,
        out AsnEncodedData value)
    {
        value = null!;
        foreach (CryptographicAttributeObject attribute in signerInfo.SignedAttributes)
        {
            if (attribute.Oid?.Value != oid)
                continue;
            if (value is not null || attribute.Values.Count != 1)
                return false;
            value = attribute.Values[0];
        }

        return value is not null;
    }

    private static bool IsValidSigningTime(byte[] rawData)
    {
        try
        {
            AsnReader reader = new(rawData, AsnEncodingRules.DER);
            Asn1Tag tag = reader.PeekTag();
            if (tag.HasSameClassAndValue(
                    new Asn1Tag(UniversalTagNumber.UtcTime)))
            {
                _ = reader.ReadUtcTime();
            }
            else if (tag.HasSameClassAndValue(
                         new Asn1Tag(UniversalTagNumber.GeneralizedTime)))
            {
                _ = reader.ReadGeneralizedTime();
            }
            else
            {
                return false;
            }

            reader.ThrowIfNotEmpty();
            return true;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static bool SigningCertificateV2Matches(
        byte[] rawData,
        X509Certificate2 certificate)
    {
        try
        {
            AsnReader reader = new(rawData, AsnEncodingRules.DER);
            AsnReader signingCertificate = reader.ReadSequence();
            AsnReader certificates = signingCertificate.ReadSequence();
            bool first = true;
            bool firstMatches = false;
            while (certificates.HasData)
            {
                if (!TryReadEssCertIdV2(
                        certificates,
                        out string hashAlgorithmOid,
                        out byte[] certificateHash,
                        out ReadOnlyMemory<byte>? issuerName,
                        out BigInteger? serialNumber))
                {
                    return false;
                }

                if (!IsSupportedHashAlgorithm(hashAlgorithmOid))
                    return false;

                if (first)
                {
                    if (issuerName is null || serialNumber is null)
                        return false;

                    byte[] actualHash = hashAlgorithmOid switch
                    {
                        Sha256Oid => certificate.GetCertHash(HashAlgorithmName.SHA256),
                        Sha384Oid => certificate.GetCertHash(HashAlgorithmName.SHA384),
                        Sha512Oid => certificate.GetCertHash(HashAlgorithmName.SHA512),
                        _ => [],
                    };
                    BigInteger expectedSerial = new(
                        certificate.GetSerialNumber(),
                        isUnsigned: true,
                        isBigEndian: false);
                    firstMatches = actualHash.Length == certificateHash.Length
                        && CryptographicOperations.FixedTimeEquals(
                            actualHash,
                            certificateHash)
                        && issuerName.Value.Span.SequenceEqual(
                            certificate.IssuerName.RawData)
                        && serialNumber.Value == expectedSerial;
                    first = false;
                }
            }

            if (first || !firstMatches)
                return false;

            if (signingCertificate.HasData
                && !TryReadSigningCertificatePolicies(
                    signingCertificate.ReadSequence()))
            {
                return false;
            }
            signingCertificate.ThrowIfNotEmpty();
            reader.ThrowIfNotEmpty();
            return true;
        }
        catch (AsnContentException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryReadEssCertIdV2(
        AsnReader certificates,
        out string hashAlgorithmOid,
        out byte[] certificateHash,
        out ReadOnlyMemory<byte>? issuerName,
        out BigInteger? serialNumber)
    {
        AsnReader certificate = certificates.ReadSequence();
        if (certificate.HasData
            && certificate.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
        {
            AsnReader algorithm = certificate.ReadSequence();
            hashAlgorithmOid = algorithm.ReadObjectIdentifier();
            if (algorithm.HasData)
                algorithm.ReadNull();
            algorithm.ThrowIfNotEmpty();
        }
        else
        {
            hashAlgorithmOid = Sha256Oid;
        }

        certificateHash = certificate.ReadOctetString();
        issuerName = null;
        serialNumber = null;
        if (certificate.HasData)
        {
            AsnReader issuerSerial = certificate.ReadSequence();
            AsnReader generalNames = issuerSerial.ReadSequence();
            if (!generalNames.HasData)
                return false;

            var directoryNameTag = new Asn1Tag(
                TagClass.ContextSpecific,
                4,
                isConstructed: true);
            AsnReader directoryName = generalNames.ReadSequence(directoryNameTag);
            issuerName = directoryName.ReadEncodedValue();
            directoryName.ThrowIfNotEmpty();
            generalNames.ThrowIfNotEmpty();
            serialNumber = issuerSerial.ReadInteger();
            issuerSerial.ThrowIfNotEmpty();
        }

        certificate.ThrowIfNotEmpty();
        return true;
    }

    private static bool TryReadSigningCertificatePolicies(AsnReader policies)
    {
        if (!policies.HasData)
            return false;

        while (policies.HasData)
        {
            AsnReader policy = policies.ReadSequence();
            _ = policy.ReadObjectIdentifier();
            if (policy.HasData)
            {
                AsnReader qualifiers = policy.ReadSequence();
                if (!qualifiers.HasData)
                    return false;
                while (qualifiers.HasData)
                {
                    AsnReader qualifier = qualifiers.ReadSequence();
                    _ = qualifier.ReadObjectIdentifier();
                    _ = qualifier.ReadEncodedValue();
                    qualifier.ThrowIfNotEmpty();
                }
            }
            policy.ThrowIfNotEmpty();
        }

        return true;
    }

    /// <summary>
    /// Detects whether the primary signature is an author or repository signature
    /// by checking the commitment type indication signed attribute.
    /// </summary>
    private static bool TryDetectSignatureType(
        SignerInfo signerInfo,
        out SignatureType signatureType)
    {
        signatureType = default;
        int recognizedValues = 0;
        foreach (CryptographicAttributeObject attr in signerInfo.SignedAttributes)
        {
            if (attr.Oid?.Value != CommitmentTypeIndicationOid)
                continue;

            foreach (AsnEncodedData value in attr.Values)
            {
                string? oid = TryReadCommitmentTypeOid(value.RawData);
                if (oid == AuthorCommitmentOid)
                    signatureType = SignatureType.Author;
                else if (oid == RepositoryCommitmentOid)
                    signatureType = SignatureType.Repository;
                else
                    return false;

                recognizedValues++;
                if (recognizedValues > 1)
                    return false;
            }
        }

        return recognizedValues == 1;
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
            string oid = sequence.ReadObjectIdentifier();
            if (sequence.HasData)
            {
                AsnReader qualifiers = sequence.ReadSequence();
                if (!qualifiers.HasData)
                    return null;

                while (qualifiers.HasData)
                {
                    AsnReader qualifier = qualifiers.ReadSequence();
                    _ = qualifier.ReadObjectIdentifier();
                    if (qualifier.HasData)
                        _ = qualifier.ReadEncodedValue();
                    qualifier.ThrowIfNotEmpty();
                }
            }
            sequence.ThrowIfNotEmpty();
            reader.ThrowIfNotEmpty();
            return oid;
        }
        catch (AsnContentException)
        {
            return null;
        }
    }

    private static int CountRepositoryCounterSignatures(SignerInfo primarySigner)
    {
        int count = 0;
        foreach (SignerInfo counterSigner in primarySigner.CounterSignerInfos)
        {
            if (TryDetectSignatureType(counterSigner, out SignatureType type)
                && type == SignatureType.Repository)
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasValidRepositoryServiceIndex(SignerInfo signerInfo)
    {
        CryptographicAttributeObject? serviceIndex = null;
        foreach (CryptographicAttributeObject attribute in signerInfo.SignedAttributes)
        {
            if (attribute.Oid?.Value != RepositoryServiceIndexOid)
                continue;
            if (serviceIndex is not null)
                return false;
            serviceIndex = attribute;
        }

        if (serviceIndex is null || serviceIndex.Values.Count != 1)
            return false;

        try
        {
            AsnReader reader = new(
                serviceIndex.Values[0].RawData,
                AsnEncodingRules.DER);
            string value = reader.ReadCharacterString(
                UniversalTagNumber.IA5String);
            reader.ThrowIfNotEmpty();
            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                && uri.Scheme.Equals("https", StringComparison.Ordinal);
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies the RFC 3161 timestamp counter-signature if present.
    /// Returns the timestamp value on success, null if absent or invalid.
    /// </summary>
    private static VerifiedTimestamp? VerifyTimestamp(SignerInfo signerInfo)
    {
        foreach (CryptographicAttributeObject attr in signerInfo.UnsignedAttributes)
        {
            if (attr.Oid?.Value != TimestampTokenOid)
                continue;

            foreach (AsnEncodedData value in attr.Values)
            {
                VerifiedTimestamp? ts = VerifyTimestampToken(
                    value.RawData,
                    signerInfo.GetSignature());
                if (ts is not null)
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
        if (CountRepositoryCounterSignatures(primarySigner) != 1)
            return null;

        foreach (SignerInfo counterSigner in primarySigner.CounterSignerInfos)
        {
            if (!TryDetectSignatureType(counterSigner, out SignatureType counterType)
                || counterType != SignatureType.Repository
                || !IsSupportedHashAlgorithm(
                    counterSigner.DigestAlgorithm.Value ?? string.Empty)
                || !HasValidRepositoryServiceIndex(counterSigner))
            {
                continue;
            }

            X509Certificate2? cert = counterSigner.Certificate;
            if (cert is null
                || !HasValidNuGetSignerAttributes(counterSigner, cert))
            {
                continue;
            }
            SignatureVerificationResult? profileFailure =
                ValidateCertificateProfile(cert, rejectLifetimeSigning: true);
            if (profileFailure is not null)
                continue;

            string? publisher = ExtractCN(cert.Subject);
            string thumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256);
            VerifiedTimestamp? timestamp = VerifyTimestamp(counterSigner);
            if (timestamp is not null
                && !IsCertificateValidForTimestamp(cert, timestamp.Value))
            {
                timestamp = null;
            }

            SignatureVerificationResult chainResult = VerifyCertificateChain(
                cert, extraCerts, TrustedRoots.CodeSigningRoots,
                CodeSigningEkuOid,
                verificationTime: timestamp?.UpperBound);

            if (!chainResult.IsValid)
                continue;

            return new SignatureVerificationResult(SignatureStatus.Valid, Reason: null)
            {
                Publisher = publisher,
                Thumbprint = thumbprint,
                SignatureType = SignatureType.Repository,
                Timestamp = timestamp?.Time,
            };
        }

        return null;
    }

    /// <summary>
    /// Decodes and verifies an RFC 3161 timestamp token (which is itself a CMS SignedData).
    /// </summary>
    private static VerifiedTimestamp? VerifyTimestampToken(
        byte[] tokenBytes,
        byte[] parentSignature)
    {
        try
        {
            SignedCms timestampCms = new();
            timestampCms.Decode(tokenBytes);
            timestampCms.CheckSignature(verifySignatureOnly: true);
            if (timestampCms.ContentInfo.ContentType.Value != TimestampInfoContentTypeOid
                || timestampCms.SignerInfos.Count != 1)
            {
                return null;
            }
            if (!IsSupportedHashAlgorithm(
                timestampCms.SignerInfos[0].DigestAlgorithm.Value ?? string.Empty))
            {
                return null;
            }

            TimestampInfo? timestampInfo = TryExtractTimestampInfo(
                timestampCms.ContentInfo.Content);
            if (timestampInfo is null
                || !VerifyTimestampImprint(timestampInfo.Value, parentSignature))
            {
                return null;
            }

            X509Certificate2? tsCert = timestampCms.SignerInfos[0].Certificate;
            if (tsCert is null)
                return null;
            SignatureVerificationResult? profileFailure =
                ValidateCertificateProfile(tsCert, rejectLifetimeSigning: false);
            if (profileFailure is not null)
                return null;

            VerifiedTimestamp timestamp = timestampInfo.Value.Timestamp;
            if (!IsCertificateValidForTimestamp(tsCert, timestamp))
                return null;

            SignatureVerificationResult tsChain = VerifyCertificateChain(
                tsCert,
                timestampCms.Certificates,
                TrustedRoots.TimestampingRoots,
                TimestampingEkuOid,
                verificationTime: timestamp.UpperBound);
            return tsChain.IsValid ? timestamp : null;
        }
        catch (Exception ex) when (IsMalformedCryptographicPayload(ex))
        {
            return null;
        }
    }

    private static bool IsMalformedCryptographicPayload(Exception exception)
        => exception is CryptographicException
            or AsnContentException
            or ArgumentException
            or OverflowException;

    /// <summary>
    /// Extracts the genTime from an RFC 3161 TSTInfo structure.
    /// TSTInfo ::= SEQUENCE { version INTEGER, policy OBJECT IDENTIFIER,
    ///   messageImprint MessageImprint, serialNumber INTEGER, genTime GeneralizedTime, ... }
    /// </summary>
    internal static TimestampInfo? TryExtractTimestampInfo(byte[]? tstInfoBytes)
    {
        if (tstInfoBytes is null || tstInfoBytes.Length == 0)
            return null;

        AsnReader reader = new(tstInfoBytes, AsnEncodingRules.DER);
        AsnReader sequence = reader.ReadSequence();
        if (sequence.ReadInteger() != 1)
            return null;
        string policyOid = sequence.ReadObjectIdentifier();
        AsnReader imprint = sequence.ReadSequence();
        AsnReader algorithm = imprint.ReadSequence();
        string algorithmOid = algorithm.ReadObjectIdentifier();
        if (algorithm.HasData)
            algorithm.ReadNull();
        algorithm.ThrowIfNotEmpty();
        byte[] messageHash = imprint.ReadOctetString();
        imprint.ThrowIfNotEmpty();
        _ = sequence.ReadInteger();
        DateTimeOffset timestamp = sequence.ReadGeneralizedTime();
        TimeSpan accuracy = policyOid == BaselineTimestampPolicyOid
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.Zero;
        if (sequence.HasData
            && sequence.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
        {
            accuracy = ReadTimestampAccuracy(sequence.ReadSequence());
        }
        if (sequence.HasData
            && sequence.PeekTag().HasSameClassAndValue(
                new Asn1Tag(UniversalTagNumber.Boolean)))
        {
            _ = sequence.ReadBoolean();
        }
        if (sequence.HasData
            && sequence.PeekTag().HasSameClassAndValue(
                new Asn1Tag(UniversalTagNumber.Integer)))
        {
            _ = sequence.ReadInteger();
        }
        if (sequence.HasData
            && sequence.PeekTag().TagClass == TagClass.ContextSpecific
            && sequence.PeekTag().TagValue == 0)
        {
            _ = sequence.ReadEncodedValue();
        }
        if (sequence.HasData
            && sequence.PeekTag().TagClass == TagClass.ContextSpecific
            && sequence.PeekTag().TagValue == 1)
        {
            _ = sequence.ReadEncodedValue();
        }
        sequence.ThrowIfNotEmpty();
        reader.ThrowIfNotEmpty();
        if (!IsSupportedHashAlgorithm(algorithmOid))
            return null;

        DateTimeOffset lowerBound;
        DateTimeOffset upperBound;
        try
        {
            lowerBound = timestamp.Subtract(accuracy);
            upperBound = timestamp.Add(accuracy);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        return new TimestampInfo(
            algorithmOid,
            messageHash,
            new VerifiedTimestamp(timestamp, lowerBound, upperBound));
    }

    private static TimeSpan ReadTimestampAccuracy(AsnReader accuracy)
    {
        long ticks = 0;
        if (accuracy.HasData
            && accuracy.PeekTag().HasSameClassAndValue(
                new Asn1Tag(UniversalTagNumber.Integer)))
        {
            BigInteger seconds = accuracy.ReadInteger();
            if (seconds < 0 || seconds > long.MaxValue / TimeSpan.TicksPerSecond)
                throw new AsnContentException();
            ticks = checked((long)seconds * TimeSpan.TicksPerSecond);
        }

        var millisecondsTag = new Asn1Tag(TagClass.ContextSpecific, 0);
        if (accuracy.HasData
            && accuracy.PeekTag().HasSameClassAndValue(millisecondsTag))
        {
            BigInteger milliseconds = accuracy.ReadInteger(millisecondsTag);
            if (milliseconds < 1 || milliseconds > 999)
                throw new AsnContentException();
            ticks = checked(
                ticks + (long)milliseconds * TimeSpan.TicksPerMillisecond);
        }

        var microsecondsTag = new Asn1Tag(TagClass.ContextSpecific, 1);
        if (accuracy.HasData
            && accuracy.PeekTag().HasSameClassAndValue(microsecondsTag))
        {
            BigInteger microseconds = accuracy.ReadInteger(microsecondsTag);
            if (microseconds < 1 || microseconds > 999)
                throw new AsnContentException();
            ticks = checked(ticks + (long)microseconds * 10);
        }

        accuracy.ThrowIfNotEmpty();
        return TimeSpan.FromTicks(ticks);
    }

    private static bool VerifyTimestampImprint(
        TimestampInfo timestampInfo,
        byte[] parentSignature)
    {
        byte[] actual = timestampInfo.AlgorithmOid switch
        {
            Sha256Oid => SHA256.HashData(parentSignature),
            Sha384Oid => SHA384.HashData(parentSignature),
            Sha512Oid => SHA512.HashData(parentSignature),
            _ => [],
        };
        return actual.Length == timestampInfo.MessageHash.Length
            && CryptographicOperations.FixedTimeEquals(
                actual,
                timestampInfo.MessageHash);
    }

    private static bool IsCertificateValidForTimestamp(
        X509Certificate2 certificate,
        VerifiedTimestamp timestamp)
        => certificate.NotBefore.ToUniversalTime() <= timestamp.LowerBound.UtcDateTime
            && certificate.NotAfter.ToUniversalTime() >= timestamp.UpperBound.UtcDateTime;

    private static SignatureVerificationResult? ValidateCertificateProfile(
        X509Certificate2 certificate,
        bool rejectLifetimeSigning)
    {
        try
        {
            if (certificate.SignatureAlgorithm.Value
                is not (Sha256WithRsaOid or Sha384WithRsaOid or Sha512WithRsaOid))
            {
                return new SignatureVerificationResult(
                    SignatureStatus.Invalid,
                    "Signer certificate uses an unsupported signature algorithm.");
            }

            using RSA? publicKey = certificate.GetRSAPublicKey();
            if (publicKey is null || publicKey.KeySize < 2048)
            {
                return new SignatureVerificationResult(
                    SignatureStatus.Invalid,
                    "Signer certificate does not meet the NuGet V1 RSA key requirement.");
            }

            if (rejectLifetimeSigning
                && HasExtendedKeyUsage(certificate, LifetimeSigningEkuOid))
            {
                return new SignatureVerificationResult(
                    SignatureStatus.Invalid,
                    "Package signer certificate has the Lifetime Signing EKU.");
            }
        }
        catch (CryptographicException)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Signer certificate profile could not be decoded.");
        }

        return null;
    }

    private static bool HasExtendedKeyUsage(
        X509Certificate2 certificate,
        string usageOid)
    {
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != EnhancedKeyUsageOid)
                continue;

            var usages = extension as X509EnhancedKeyUsageExtension
                ?? new X509EnhancedKeyUsageExtension(
                    new AsnEncodedData(extension.Oid, extension.RawData),
                    extension.Critical);
            foreach (Oid usage in usages.EnhancedKeyUsages)
            {
                if (usage.Value == usageOid)
                    return true;
            }
        }

        return false;
    }

    internal static SignatureVerificationResult VerifyCertificateChain(
        X509Certificate2 signerCert,
        X509Certificate2Collection extraCerts,
        X509Certificate2Collection trustedRoots,
        string applicationPolicyOid,
        DateTimeOffset? verificationTime = null)
    {
        using X509Chain chain = new();
        ConfigureCertificateChainPolicy(
            chain.ChainPolicy,
            extraCerts,
            trustedRoots,
            applicationPolicyOid,
            verificationTime);

        bool isValid;
        try
        {
            isValid = chain.Build(signerCert);
        }
        catch (CryptographicException ex)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                $"Signer certificate chain could not be built: {ex.Message}");
        }

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

    internal static void ConfigureCertificateChainPolicy(
        X509ChainPolicy policy,
        X509Certificate2Collection extraCerts,
        X509Certificate2Collection trustedRoots,
        string applicationPolicyOid,
        DateTimeOffset? verificationTime = null)
    {
        policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        policy.CustomTrustStore.AddRange(trustedRoots);
        policy.ExtraStore.AddRange(extraCerts);
        policy.ApplicationPolicy.Add(new Oid(applicationPolicyOid));
        policy.DisableCertificateDownloads = true;
        policy.RevocationMode = X509RevocationMode.NoCheck;
        if (verificationTime.HasValue)
            policy.VerificationTime = verificationTime.Value.UtcDateTime;
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

    private readonly record struct SignedContentHash(
        string AlgorithmOid,
        string Value);

    internal readonly record struct TimestampInfo(
        string AlgorithmOid,
        byte[] MessageHash,
        VerifiedTimestamp Timestamp);

    internal readonly record struct VerifiedTimestamp(
        DateTimeOffset Time,
        DateTimeOffset LowerBound,
        DateTimeOffset UpperBound);

}
