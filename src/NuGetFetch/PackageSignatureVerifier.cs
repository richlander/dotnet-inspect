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
public static partial class PackageSignatureVerifier
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
