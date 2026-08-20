using System.Buffers.Binary;
using System.Formats.Asn1;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace NuGetFetch;

/// <summary>
/// Verifies NuGet package signatures, signer chains, and signed archive content
/// using the embedded trusted root certificates.
/// Inspired by the NuGet client's signing verification (Apache 2.0 licensed).
/// </summary>
public static class PackageSignatureVerifier
{
    private const string SignatureFileName = ".signature.p7s";

    // NuGet signing OIDs
    private const string DataContentTypeOid = "1.2.840.113549.1.7.1";
    private const string CommitmentTypeIndicationOid = "1.2.840.113549.1.9.16.2.16";
    private const string AuthorCommitmentOid = "1.2.840.113549.1.9.16.6.1";     // proof of origin
    private const string RepositoryCommitmentOid = "1.2.840.113549.1.9.16.6.2";  // proof of receipt
    private const string RepositoryServiceIndexOid = "1.3.6.1.4.1.311.84.2.1.1.1";
    private const string SigningTimeOid = "1.2.840.113549.1.9.5";
    private const string SigningCertificateOid = "1.2.840.113549.1.9.16.2.12";
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    private const string TimestampTokenOid = "1.2.840.113549.1.9.16.2.14";
    private const string TimestampInfoContentTypeOid = "1.2.840.113549.1.9.16.1.4";
    private const string BaselineTimestampPolicyOid = "0.4.0.2023.1.1";
    private const string CodeSigningEkuOid = "1.3.6.1.5.5.7.3.3";
    private const string TimestampingEkuOid = "1.3.6.1.5.5.7.3.8";
    private const string LifetimeSigningEkuOid = "1.3.6.1.4.1.311.10.3.13";
    private const string EnhancedKeyUsageOid = "2.5.29.37";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    private const string Sha384Oid = "2.16.840.1.101.3.4.2.2";
    private const string Sha512Oid = "2.16.840.1.101.3.4.2.3";
    private const string Sha256WithRsaOid = "1.2.840.113549.1.1.11";
    private const string Sha384WithRsaOid = "1.2.840.113549.1.1.12";
    private const string Sha512WithRsaOid = "1.2.840.113549.1.1.13";
    private const uint LocalFileHeaderSignature = 0x04034B50;
    private const uint CentralDirectoryHeaderSignature = 0x02014B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const int LocalFileHeaderFixedLength = 30;
    private const int CentralDirectoryFixedLength = 46;
    private const int EndOfCentralDirectoryFixedLength = 22;
    private const int MaximumZipCommentLength = ushort.MaxValue;
    private const int MaximumSignatureBytes = 1024 * 1024;

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
        byte[]? signatureBytes;
        try
        {
            signatureBytes = ExtractSignature(nupkgStream);
        }
        catch (InvalidDataException)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package signature entry is invalid.");
        }

        if (signatureBytes is null)
        {
            return new SignatureVerificationResult(SignatureStatus.Unsigned, Reason: null);
        }

        SignatureVerificationResult result = VerifySignature(signatureBytes);
        if (!result.IsValid)
            return result;

        if (result.ContentHash is null
            || result.ContentHashAlgorithm is null
            || !VerifyPackageContentHash(
                nupkgStream,
                result.ContentHashAlgorithm,
                result.ContentHash))
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package content hash could not be verified against the signed hash.");
        }

        return result with
        {
            PackageContentVerified = true,
            CounterSignature = result.CounterSignature is null
                ? null
                : result.CounterSignature with { PackageContentVerified = true },
        };
    }

    /// <summary>
    /// Verifies a raw .signature.p7s file (e.g. from an extracted package cache).
    /// </summary>
    public static SignatureVerificationResult VerifySignatureFile(string signaturePath)
    {
        using FileStream stream = File.OpenRead(signaturePath);
        if (stream.Length > MaximumSignatureBytes)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package signature entry is too large.");
        }

        byte[] signatureBytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
        stream.ReadExactly(signatureBytes);
        return VerifySignature(signatureBytes);
    }

    private static byte[]? ExtractSignature(Stream nupkgStream)
    {
        if (!nupkgStream.CanSeek)
            throw new InvalidDataException("Package stream must be seekable.");

        ZipEndRecord end = ReadZipEndRecord(nupkgStream);
        List<ZipEntryRecord> entries = ReadCentralDirectory(nupkgStream, end);
        ZipEntryRecord? signature = null;
        foreach (ZipEntryRecord entry in entries)
        {
            if (!entry.IsSignature)
                continue;
            if (signature is not null)
                throw new InvalidDataException("Package contains multiple signature entries.");
            signature = entry;
        }

        if (signature is null)
            return null;
        if (!HasValidSignatureCentralDirectoryProfile(signature))
            throw new InvalidDataException("Package signature entry profile is invalid.");
        if (signature.UncompressedSize > MaximumSignatureBytes)
        {
            throw new InvalidDataException("Package signature entry is too large.");
        }

        return ReadStoredSignatureEntry(nupkgStream, signature, end);
    }

    private static bool HasValidSignatureCentralDirectoryProfile(
        ZipEntryRecord signature)
        => signature.GeneralPurposeBitFlags == 0
            && signature.CompressionMethod == 0
            && signature.CompressedSize == signature.UncompressedSize
            && signature.ExternalFileAttributes == 0;

    private static byte[] ReadStoredSignatureEntry(
        Stream stream,
        ZipEntryRecord signature,
        ZipEndRecord end)
    {
        if (signature.LocalOffset < 0
            || signature.LocalOffset + LocalFileHeaderFixedLength
                > end.CentralDirectoryOffset)
        {
            throw new InvalidDataException("Invalid signature local header bounds.");
        }

        byte[] header = new byte[LocalFileHeaderFixedLength];
        stream.Position = signature.LocalOffset;
        stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header)
                != LocalFileHeaderSignature
            || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6)) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8)) != 0)
        {
            throw new InvalidDataException("Invalid signature local header profile.");
        }

        uint compressedSize =
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(18));
        uint uncompressedSize =
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(22));
        if (compressedSize != uncompressedSize
            || compressedSize != signature.CompressedSize
            || uncompressedSize != signature.UncompressedSize)
        {
            throw new InvalidDataException("Inconsistent signature entry sizes.");
        }

        ushort fileNameLength =
            BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        ushort extraLength =
            BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        long dataOffset = checked(
            signature.LocalOffset
            + LocalFileHeaderFixedLength
            + fileNameLength
            + extraLength);
        long dataEnd = checked(dataOffset + compressedSize);
        if (dataEnd > end.CentralDirectoryOffset)
            throw new InvalidDataException("Invalid signature entry bounds.");

        byte[] fileName = new byte[fileNameLength];
        stream.ReadExactly(fileName);
        if (!fileName.AsSpan().SequenceEqual(".signature.p7s"u8))
            throw new InvalidDataException("Signature local header name does not match.");

        stream.Position = dataOffset;
        byte[] signatureBytes =
            GC.AllocateUninitializedArray<byte>((int)uncompressedSize);
        stream.ReadExactly(signatureBytes);
        return signatureBytes;
    }

    private static SignatureVerificationResult VerifySignature(byte[] signatureBytes)
    {
        try
        {
            return VerifySignatureCore(signatureBytes);
        }
        catch (Exception ex) when (IsMalformedCryptographicPayload(ex))
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package signature cryptographic payload is invalid.");
        }
    }

    private static SignatureVerificationResult VerifySignatureCore(
        byte[] signatureBytes)
    {
        if (!ContainsExactlyOneCmsValue(signatureBytes))
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package signature payload must contain exactly one CMS value.");
        }

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
        if (signedCms.ContentInfo.ContentType.Value != DataContentTypeOid)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package signature has an invalid signed-content type.");
        }

        // Parse the signed content hash that binds the CMS identity to the package archive.
        // The CMS ContentInfo contains "Version:1\n\n{OID}-Hash:{base64}\n\n".
        SignedContentHash? signedContentHash = TryExtractSignedHash(
            signedCms.ContentInfo.Content);
        if (signedContentHash is null)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package signed content does not use the NuGet V1 format.");
        }

        // Extract the signing certificate
        if (signedCms.SignerInfos.Count != 1)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package signature must contain exactly one primary signer.");
        }

        SignerInfo signerInfo = signedCms.SignerInfos[0];
        if (!IsSupportedHashAlgorithm(signerInfo.DigestAlgorithm.Value ?? string.Empty))
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package signature uses an unsupported digest algorithm.");
        }

        X509Certificate2? signerCert = signerInfo.Certificate;
        if (signerCert is null)
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid, "Could not extract signing certificate.");
        }
        if (!HasValidNuGetSignerAttributes(signerInfo, signerCert))
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package signature has an invalid NuGet signer-attribute profile.");
        }
        SignatureVerificationResult? profileFailure =
            ValidateCertificateProfile(signerCert, rejectLifetimeSigning: true);
        if (profileFailure is not null)
            return profileFailure;

        // Extract publisher identity from certificate CN
        string? publisher = ExtractCN(signerCert.Subject);
        string thumbprint = signerCert.GetCertHashString(HashAlgorithmName.SHA256);

        // Detect author vs repository signature type
        if (!TryDetectSignatureType(signerInfo, out SignatureType signatureType))
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package signature has an invalid commitment type.");
        }
        if (signatureType == SignatureType.Repository
            && (!HasValidRepositoryServiceIndex(signerInfo)
                || CountRepositoryCounterSignatures(signerInfo) > 0))
        {
            return new SignatureVerificationResult(
                SignatureStatus.Invalid,
                "Package repository signature has an invalid signed-attribute profile.");
        }

        // Verify timestamp first — needed to decide if expired certs are acceptable
        VerifiedTimestamp? timestamp = VerifyTimestamp(signerInfo);
        if (timestamp is not null
            && !IsCertificateValidForTimestamp(signerCert, timestamp.Value))
        {
            timestamp = null;
        }

        // Build certificate chain. If the cert is expired but a valid timestamp
        // proves it was signed while the cert was still valid, allow it.
        // This matches NuGet client behavior for long-term signature validity.
        SignatureVerificationResult chainResult = VerifyCertificateChain(
            signerCert, signedCms.Certificates, TrustedRoots.CodeSigningRoots,
            CodeSigningEkuOid,
            verificationTime: timestamp?.UpperBound);

        if (!chainResult.IsValid)
            return chainResult;

        SignedContentHash contentHash = signedContentHash.Value;
        return new SignatureVerificationResult(SignatureStatus.Valid, Reason: null)
        {
            Publisher = publisher,
            Thumbprint = thumbprint,
            SignatureType = signatureType,
            Timestamp = timestamp?.Time,
            ContentHash = contentHash.Value,
            ContentHashAlgorithm = contentHash.AlgorithmOid,
            CounterSignature = signatureType == SignatureType.Author
                ? VerifyRepositoryCounterSignature(signerInfo, signedCms.Certificates)
                : null,
        };
    }

    /// <summary>
    /// Parses the NuGet-specific signed content format to extract the package hash.
    /// Format: "Version:1\n\n{OID}-Hash:{base64Hash}\n\n"
    /// </summary>
    private static SignedContentHash? TryExtractSignedHash(byte[]? content)
    {
        if (content is null || content.Length == 0)
        {
            return null;
        }

        string text;
        try
        {
            text = new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(content);
        }
        catch (System.Text.DecoderFallbackException)
        {
            return null;
        }

        const string Header = "Version:1\n\n";
        if (text.Length <= Header.Length + 2
            || !text.StartsWith(Header, StringComparison.Ordinal)
            || !text.EndsWith("\n\n", StringComparison.Ordinal))
        {
            return null;
        }

        ReadOnlySpan<char> hashPair = text.AsSpan(
            Header.Length,
            text.Length - Header.Length - 2);
        if (hashPair.Contains('\n') || hashPair.Contains('\r'))
            return null;

        int separator = hashPair.IndexOf(':');
        const string HashSuffix = "-Hash";
        if (separator <= HashSuffix.Length)
            return null;

        ReadOnlySpan<char> key = hashPair[..separator];
        if (!key.EndsWith(HashSuffix, StringComparison.Ordinal))
            return null;

        string algorithmOid = key[..^HashSuffix.Length].ToString();
        if (!IsSupportedHashAlgorithm(algorithmOid))
            return null;

        string value = hashPair[(separator + 1)..].ToString();
        if (value.Length == 0)
            return null;

        try
        {
            _ = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }

        return new SignedContentHash(algorithmOid, value);
    }

    private static bool IsSupportedHashAlgorithm(string algorithmOid)
        => algorithmOid is Sha256Oid or Sha384Oid or Sha512Oid;

    private static bool VerifyPackageContentHash(
        Stream packageStream,
        string algorithmOid,
        string expectedHash)
    {
        try
        {
            HashAlgorithmName algorithm = algorithmOid switch
            {
                Sha256Oid => HashAlgorithmName.SHA256,
                Sha384Oid => HashAlgorithmName.SHA384,
                Sha512Oid => HashAlgorithmName.SHA512,
                _ => default,
            };
            if (algorithm.Name is null || !packageStream.CanSeek)
                return false;

            byte[] expected = Convert.FromBase64String(expectedHash);
            ZipEndRecord end = ReadZipEndRecord(packageStream);
            List<ZipEntryRecord> entries = ReadCentralDirectory(packageStream, end);
            ZipEntryRecord? signature = null;
            foreach (ZipEntryRecord entry in entries)
            {
                if (!entry.IsSignature)
                    continue;
                if (signature is not null)
                    return false;
                signature = entry;
            }
            if (signature is null)
                return false;

            List<ZipEntryRecord> localOrder = entries
                .OrderBy(entry => entry.LocalOffset)
                .ToList();
            long startOfLocalEntries = localOrder[0].LocalOffset;
            for (int index = 0; index < localOrder.Count; index++)
            {
                long endOffset = index + 1 < localOrder.Count
                    ? localOrder[index + 1].LocalOffset
                    : end.CentralDirectoryOffset;
                if (endOffset <= localOrder[index].LocalOffset)
                    return false;
                localOrder[index].LocalSize = endOffset - localOrder[index].LocalOffset;
            }

            List<ZipEntryRecord> unsignedLocalOrder = localOrder
                .Where(entry => !entry.IsSignature)
                .ToList();
            if (unsignedLocalOrder.Count == 0)
                return false;

            long previousUnsignedEnd = 0;
            foreach (ZipEntryRecord entry in unsignedLocalOrder)
            {
                entry.OffsetChange = previousUnsignedEnd - entry.LocalOffset;
                previousUnsignedEnd = checked(
                    entry.LocalOffset + entry.LocalSize + entry.OffsetChange);
            }

            using IncrementalHash hash = IncrementalHash.CreateHash(algorithm);
            byte[] hashBuffer = new byte[64 * 1024];
            AppendRange(packageStream, hash, hashBuffer, 0, startOfLocalEntries);
            foreach (ZipEntryRecord entry in unsignedLocalOrder)
            {
                AppendRange(
                    packageStream,
                    hash,
                    hashBuffer,
                    entry.LocalOffset,
                    entry.LocalOffset + entry.LocalSize);
            }

            foreach (ZipEntryRecord entry in entries.Where(value => !value.IsSignature))
            {
                AppendRange(
                    packageStream,
                    hash,
                    hashBuffer,
                    entry.CentralPosition,
                    entry.CentralPosition + 42);
                packageStream.Position = entry.CentralPosition + 42;
                uint originalOffset = ReadUInt32(packageStream);
                long adjustedOffset = checked((long)originalOffset + entry.OffsetChange);
                if (adjustedOffset is < 0 or > uint.MaxValue)
                    return false;
                AppendUInt32(hash, (uint)adjustedOffset);
                AppendRange(
                    packageStream,
                    hash,
                    hashBuffer,
                    entry.CentralPosition + CentralDirectoryFixedLength,
                    entry.CentralPosition + entry.CentralHeaderSize);
            }

            AppendRange(
                packageStream,
                hash,
                hashBuffer,
                end.Position,
                end.Position + 8);
            if (end.EntriesOnDisk == 0 || end.TotalEntries == 0)
                return false;
            AppendUInt16(hash, (ushort)(end.EntriesOnDisk - 1));
            AppendUInt16(hash, (ushort)(end.TotalEntries - 1));
            if (end.CentralDirectorySize < signature.CentralHeaderSize
                || end.CentralDirectoryOffset < signature.LocalSize)
            {
                return false;
            }
            AppendUInt32(
                hash,
                end.CentralDirectorySize - (uint)signature.CentralHeaderSize);
            AppendUInt32(
                hash,
                end.CentralDirectoryOffset - (uint)signature.LocalSize);
            AppendRange(
                packageStream,
                hash,
                hashBuffer,
                end.Position + 20,
                packageStream.Length);

            byte[] actual = hash.GetHashAndReset();
            return expected.Length == actual.Length
                && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool ContainsExactlyOneCmsValue(byte[] signatureBytes)
    {
        try
        {
            AsnReader reader = new(signatureBytes, AsnEncodingRules.BER);
            if (!reader.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
                return false;
            _ = reader.ReadEncodedValue();
            reader.ThrowIfNotEmpty();
            return true;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static ZipEndRecord ReadZipEndRecord(Stream stream)
    {
        int tailLength = checked((int)Math.Min(
            stream.Length,
            EndOfCentralDirectoryFixedLength + MaximumZipCommentLength));
        byte[] tail = new byte[tailLength];
        stream.Position = stream.Length - tailLength;
        stream.ReadExactly(tail);

        for (int index = tail.Length - EndOfCentralDirectoryFixedLength; index >= 0; index--)
        {
            ReadOnlySpan<byte> candidate = tail.AsSpan(index);
            if (BinaryPrimitives.ReadUInt32LittleEndian(candidate) != EndOfCentralDirectorySignature)
                continue;

            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(candidate[20..]);
            if (index + EndOfCentralDirectoryFixedLength + commentLength != tail.Length)
                continue;

            ushort diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(candidate[4..]);
            ushort centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(candidate[6..]);
            ushort entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(candidate[8..]);
            ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(candidate[10..]);
            uint centralDirectorySize = BinaryPrimitives.ReadUInt32LittleEndian(candidate[12..]);
            uint centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(candidate[16..]);
            if (diskNumber != 0
                || centralDirectoryDisk != 0
                || entriesOnDisk != totalEntries
                || totalEntries == ushort.MaxValue
                || centralDirectorySize == uint.MaxValue
                || centralDirectoryOffset == uint.MaxValue)
            {
                throw new InvalidDataException("Unsupported ZIP layout.");
            }

            long position = stream.Length - tailLength + index;
            if ((long)centralDirectoryOffset + centralDirectorySize != position)
                throw new InvalidDataException("Invalid central directory bounds.");

            return new ZipEndRecord(
                position,
                entriesOnDisk,
                totalEntries,
                centralDirectorySize,
                centralDirectoryOffset);
        }

        throw new InvalidDataException("ZIP end record was not found.");
    }

    private static List<ZipEntryRecord> ReadCentralDirectory(
        Stream stream,
        ZipEndRecord end)
    {
        var entries = new List<ZipEntryRecord>(end.TotalEntries);
        byte[] header = new byte[CentralDirectoryFixedLength];
        byte[] signatureName = new byte[SignatureFileName.Length];
        stream.Position = end.CentralDirectoryOffset;
        for (int index = 0; index < end.TotalEntries; index++)
        {
            long position = stream.Position;
            if (position > end.Position
                || end.Position - position < CentralDirectoryFixedLength)
            {
                throw new InvalidDataException(
                    "Invalid central directory entry bounds.");
            }
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != CentralDirectoryHeaderSignature)
                throw new InvalidDataException("Invalid central directory header.");

            ushort fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            ushort diskStart = BinaryPrimitives.ReadUInt16LittleEndian(header[34..]);
            uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[42..]);
            if (diskStart != 0 || localOffset == uint.MaxValue)
                throw new InvalidDataException("Unsupported ZIP entry layout.");

            bool isSignature = false;
            if (fileNameLength == signatureName.Length)
            {
                stream.ReadExactly(signatureName);
                isSignature = signatureName.AsSpan().SequenceEqual(".signature.p7s"u8);
            }
            else
            {
                stream.Position = checked(stream.Position + fileNameLength);
            }
            int headerSize = checked(
                CentralDirectoryFixedLength + fileNameLength + extraLength + commentLength);
            long nextPosition = checked(position + headerSize);
            if (nextPosition > end.Position)
                throw new InvalidDataException("Invalid central directory entry bounds.");
            stream.Position = nextPosition;

            entries.Add(new ZipEntryRecord(
                position,
                localOffset,
                headerSize,
                isSignature,
                BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8)),
                BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10)),
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20)),
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24)),
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(38))));
        }

        if (stream.Position != end.Position || entries.Count == 0)
            throw new InvalidDataException("Invalid central directory size.");

        return entries;
    }

    private static void AppendRange(
        Stream stream,
        IncrementalHash hash,
        byte[] buffer,
        long start,
        long end)
    {
        if (start < 0 || end < start || end > stream.Length)
            throw new InvalidDataException("Invalid ZIP range.");

        stream.Position = start;
        long remaining = end - start;
        while (remaining > 0)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
                throw new EndOfStreamException();
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static void AppendUInt16(IncrementalHash hash, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

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

    private readonly record struct ZipEndRecord(
        long Position,
        ushort EntriesOnDisk,
        ushort TotalEntries,
        uint CentralDirectorySize,
        uint CentralDirectoryOffset);

    private sealed class ZipEntryRecord(
        long centralPosition,
        long localOffset,
        int centralHeaderSize,
        bool isSignature,
        ushort generalPurposeBitFlags,
        ushort compressionMethod,
        uint compressedSize,
        uint uncompressedSize,
        uint externalFileAttributes)
    {
        public long CentralPosition { get; } = centralPosition;
        public long LocalOffset { get; } = localOffset;
        public int CentralHeaderSize { get; } = centralHeaderSize;
        public bool IsSignature { get; } = isSignature;
        public ushort GeneralPurposeBitFlags { get; } = generalPurposeBitFlags;
        public ushort CompressionMethod { get; } = compressionMethod;
        public uint CompressedSize { get; } = compressedSize;
        public uint UncompressedSize { get; } = uncompressedSize;
        public uint ExternalFileAttributes { get; } = externalFileAttributes;
        public long LocalSize { get; set; }
        public long OffsetChange { get; set; }
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
    /// <summary>
    /// Whether the CMS signature and signer chain are valid. Package callers must
    /// additionally require <see cref="PackageContentVerified"/>.
    /// </summary>
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
    /// Base64-encoded package content hash from the signed data.
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>OID of the package content hash algorithm.</summary>
    public string? ContentHashAlgorithm { get; init; }

    /// <summary>Whether the signed content hash matched the package archive.</summary>
    public bool PackageContentVerified { get; init; }

    /// <summary>
    /// Repository counter-signature, present when the primary signature is an author signature
    /// and the repository (e.g. nuget.org) has counter-signed the package.
    /// </summary>
    public SignatureVerificationResult? CounterSignature { get; init; }
}
