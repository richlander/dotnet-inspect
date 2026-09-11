using System.Buffers.Binary;
using System.Formats.Asn1;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Integration tests for PackageSignatureVerifier.
/// Downloads real packages from nuget.org to verify signatures.
/// </summary>
[Collection("NuGet Integration")]
public class PackageSignatureVerifierTests : IDisposable
{
    private const string AuthorCommitmentOid = "1.2.840.113549.1.9.16.6.1";
    private const string CodeSigningEkuOid = "1.3.6.1.5.5.7.3.3";
    private const string CommitmentTypeIndicationOid = "1.2.840.113549.1.9.16.2.16";
    private const string LifetimeSigningEkuOid = "1.3.6.1.4.1.311.10.3.13";
    private const string RepositoryCommitmentOid = "1.2.840.113549.1.9.16.6.2";
    private const string ServerAuthenticationEkuOid = "1.3.6.1.5.5.7.3.1";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    private const string SigningTimeOid = "1.2.840.113549.1.9.5";
    private const string TimestampInfoContentTypeOid = "1.2.840.113549.1.9.16.1.4";
    private const string TimestampTokenOid = "1.2.840.113549.1.9.16.2.14";
    private const string BaselineTimestampPolicyOid = "0.4.0.2023.1.1";

    private readonly HttpClient _http = new();
    private readonly NuGetClient _client;

    public PackageSignatureVerifierTests()
    {
        _client = new NuGetClient(_http);
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task VerifyPackage_AuthorSignedPackage_ReturnsValidWithPublisher()
    {
        // Newtonsoft.Json is author-signed by James Newton-King
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid, result.Reason);
            Assert.Equal(SignatureStatus.Valid, result.Status);
            Assert.NotNull(result.Publisher);
            Assert.Contains("Json.NET", result.Publisher);
            Assert.NotNull(result.Thumbprint);
            Assert.NotEmpty(result.Thumbprint);
            Assert.Equal(SignatureType.Author, result.SignatureType);
            Assert.Null(result.Reason);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_MicrosoftPackage_ReturnsValidWithPublisher()
    {
        // Microsoft packages are author-signed
        string nupkgPath = await DownloadPackageAsync("System.Text.Json", "9.0.4");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Publisher);
            Assert.Contains("Microsoft", result.Publisher);
            Assert.Equal(SignatureType.Author, result.SignatureType);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_DotNetFoundationPackage_ReturnsValid()
    {
        // Humanizer.Core is signed by the .NET Foundation
        string nupkgPath = await DownloadPackageAsync("Humanizer.Core", "2.14.1");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Publisher);
            Assert.Contains("Humanizer", result.Publisher);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_SignedPackage_HasTimestamp()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            // Most nuget.org packages have timestamps
            Assert.NotNull(result.Timestamp);
            // Timestamp should be in the past
            Assert.True(result.Timestamp < DateTimeOffset.UtcNow);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifySignatureFile_TransplantedTimestampIsNotTrusted()
    {
        string targetPath = await DownloadPackageAsync("System.Text.Json", "9.0.4");
        string donorPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        string signaturePath = Path.Combine(
            Path.GetTempPath(),
            $"transplanted-timestamp-{Guid.NewGuid():N}.p7s");
        try
        {
            SignedCms target = DecodeSignature(targetPath);
            SignedCms donor = DecodeSignature(donorPath);
            SignerInfo targetSigner = Assert.Single(target.SignerInfos.Cast<SignerInfo>());
            SignerInfo donorSigner = Assert.Single(donor.SignerInfos.Cast<SignerInfo>());
            AsnEncodedData targetTimestamp = GetTimestampValue(targetSigner);
            AsnEncodedData donorTimestamp = GetTimestampValue(donorSigner);

            targetSigner.RemoveUnsignedAttribute(targetTimestamp);
            targetSigner.AddUnsignedAttribute(new AsnEncodedData(
                new Oid(TimestampTokenOid),
                donorTimestamp.RawData));
            File.WriteAllBytes(signaturePath, target.Encode());

            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.True(result.IsValid, result.Reason);
            Assert.Null(result.Timestamp);
        }
        finally
        {
            File.Delete(targetPath);
            File.Delete(donorPath);
            File.Delete(signaturePath);
        }
    }

    [Fact]
    public void VerifySignatureFile_RejectsLooseSignedContent()
    {
        string hash = Convert.ToBase64String(new byte[32]);
        string signaturePath = CreateTestSignature(
            $"2.16.840.1.101.3.4.2.1-Hash:{hash}",
            AuthorCommitmentOid);
        try
        {
            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.False(result.IsValid);
            Assert.Contains("NuGet V1 format", result.Reason, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(signaturePath);
        }
    }

    [Theory]
    [InlineData("Version:1\n\n")]
    [InlineData("Version:1\n\n\n")]
    public void VerifySignatureFile_RejectsOverlappingEmptyHashSection(
        string content)
    {
        string signaturePath = CreateTestSignature(
            content,
            AuthorCommitmentOid);
        try
        {
            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.False(result.IsValid);
            Assert.Contains("NuGet V1 format", result.Reason, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(signaturePath);
        }
    }

    [Fact]
    public void VerifySignatureFile_RejectsMissingCommitmentType()
    {
        string hash = Convert.ToBase64String(new byte[32]);
        string signaturePath = CreateTestSignature(
            $"Version:1\n\n2.16.840.1.101.3.4.2.1-Hash:{hash}\n\n",
            commitmentOid: null);
        try
        {
            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.False(result.IsValid);
            Assert.Contains("commitment type", result.Reason, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(signaturePath);
        }
    }

    [Fact]
    public void VerifySignatureFile_RejectsMissingSigningTime()
    {
        string hash = Convert.ToBase64String(new byte[32]);
        string signaturePath = CreateTestSignature(
            $"Version:1\n\n{Sha256Oid}-Hash:{hash}\n\n",
            AuthorCommitmentOid,
            includeSigningTime: false);
        try
        {
            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.False(result.IsValid);
            Assert.Contains(
                "signer-attribute profile",
                result.Reason,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(signaturePath);
        }
    }

    [Fact]
    public void VerifySignatureFile_RejectsMissingSigningCertificateV2()
    {
        string hash = Convert.ToBase64String(new byte[32]);
        string signaturePath = CreateTestSignature(
            $"Version:1\n\n{Sha256Oid}-Hash:{hash}\n\n",
            AuthorCommitmentOid,
            includeSigningCertificateV2: false);
        try
        {
            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.False(result.IsValid);
            Assert.Contains(
                "signer-attribute profile",
                result.Reason,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(signaturePath);
        }
    }

    [Fact]
    public void VerifySignatureFile_RejectsMismatchedSigningCertificateV2()
    {
        string hash = Convert.ToBase64String(new byte[32]);
        string signaturePath = CreateTestSignature(
            $"Version:1\n\n{Sha256Oid}-Hash:{hash}\n\n",
            AuthorCommitmentOid,
            mismatchSigningCertificateHash: true);
        try
        {
            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.False(result.IsValid);
            Assert.Contains(
                "signer-attribute profile",
                result.Reason,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(signaturePath);
        }
    }

    [Fact]
    public void VerifySignatureFile_RejectsRepositorySignatureWithoutServiceIndex()
    {
        string hash = Convert.ToBase64String(new byte[32]);
        string signaturePath = CreateTestSignature(
            $"Version:1\n\n2.16.840.1.101.3.4.2.1-Hash:{hash}\n\n",
            RepositoryCommitmentOid);
        try
        {
            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.False(result.IsValid);
            Assert.Contains(
                "signed-attribute profile",
                result.Reason,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(signaturePath);
        }
    }

    [Fact]
    public void VerifySignatureFile_RejectsWeakPackageSignerKey()
    {
        string hash = Convert.ToBase64String(new byte[32]);
        string signaturePath = CreateTestSignature(
            $"Version:1\n\n2.16.840.1.101.3.4.2.1-Hash:{hash}\n\n",
            AuthorCommitmentOid,
            keySize: 1024);
        try
        {
            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.False(result.IsValid);
            Assert.Contains("RSA key requirement", result.Reason, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(signaturePath);
        }
    }

    [Fact]
    public void VerifySignatureFile_RejectsLifetimeSigningEku()
    {
        string hash = Convert.ToBase64String(new byte[32]);
        string signaturePath = CreateTestSignature(
            $"Version:1\n\n2.16.840.1.101.3.4.2.1-Hash:{hash}\n\n",
            AuthorCommitmentOid,
            includeLifetimeSigningEku: true);
        try
        {
            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.False(result.IsValid);
            Assert.Contains("Lifetime Signing", result.Reason, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(signaturePath);
        }
    }

    [Fact]
    public void VerifyCertificateChain_RejectsTlsOnlyLeafForCodeSigning()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using RSA rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(
            "CN=Package test root",
            rootKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        rootRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));
        using X509Certificate2 root = rootRequest.CreateSelfSigned(
            now.AddDays(-1),
            now.AddDays(2));

        using RSA leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest(
            "CN=TLS-only package signer",
            leafKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        leafRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));
        var usages = new OidCollection
        {
            new Oid(ServerAuthenticationEkuOid),
        };
        leafRequest.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(usages, critical: true));
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        using X509Certificate2 issuedLeaf = leafRequest.Create(
            root,
            now.AddHours(-1),
            now.AddDays(1),
            serial);
        using X509Certificate2 leaf = issuedLeaf.CopyWithPrivateKey(leafKey);

        var roots = new X509Certificate2Collection(root);
        var extra = new X509Certificate2Collection(root);
        SignatureVerificationResult result =
            PackageSignatureVerifier.VerifyCertificateChain(
                leaf,
                extra,
                roots,
                CodeSigningEkuOid);

        Assert.False(result.IsValid);
        Assert.Contains("NotValidForUsage", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureCertificateChainPolicy_DisablesCertificateDownloads()
    {
        using X509Chain chain = new();

        PackageSignatureVerifier.ConfigureCertificateChainPolicy(
            chain.ChainPolicy,
            [],
            [],
            CodeSigningEkuOid);

        Assert.True(chain.ChainPolicy.DisableCertificateDownloads);
        Assert.Equal(X509RevocationMode.NoCheck, chain.ChainPolicy.RevocationMode);
    }

    [Fact]
    public async Task VerifyPackage_SignedPackage_HasContentHash()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.True(result.PackageContentVerified);
            Assert.NotNull(result.ContentHash);
            Assert.NotEmpty(result.ContentHash);
            // Should be valid base64
            byte[] decoded = Convert.FromBase64String(result.ContentHash);
            Assert.Equal(32, decoded.Length); // SHA-256 = 32 bytes
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_MutatedSignedPackageFailsContentHash()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(
                nupkgPath,
                System.IO.Compression.ZipArchiveMode.Update))
            {
                var entry = archive.Entries.First(value =>
                    value.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                string entryName = entry.FullName;
                entry.Delete();
                var replacement = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(replacement.Open());
                writer.Write("<package><metadata><id>mutated</id></metadata></package>");
            }

            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.False(result.IsValid);
            Assert.False(result.PackageContentVerified);
            Assert.Contains("content hash", result.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_InvalidCentralDirectoryBoundsFailsClosed()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            byte[] package = File.ReadAllBytes(nupkgPath);
            int endRecord = FindEndRecord(package);
            uint centralDirectorySize =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    package.AsSpan(endRecord + 12));
            BinaryPrimitives.WriteUInt32LittleEndian(
                package.AsSpan(endRecord + 12),
                centralDirectorySize + 1);
            File.WriteAllBytes(nupkgPath, package);

            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.False(result.IsValid);
            Assert.Contains(
                "signature entry",
                result.Reason,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public void VerifyPackage_TruncatedCentralDirectoryReturnsInvalid()
    {
        byte[] package = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(
            package,
            0x06054B50);
        BinaryPrimitives.WriteUInt16LittleEndian(
            package.AsSpan(8),
            1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            package.AsSpan(10),
            1);
        using var stream = new MemoryStream(package);

        SignatureVerificationResult result =
            PackageSignatureVerifier.VerifyPackage(stream);

        Assert.False(result.IsValid);
        Assert.Equal(SignatureStatus.Invalid, result.Status);
        Assert.Contains(
            "signature entry",
            result.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyPackage_RejectsSignatureCmsSuffix()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            byte[] package = AppendSignatureSuffix(
                File.ReadAllBytes(nupkgPath),
                "NOT-ASN1-SUFFIX"u8);
            File.WriteAllBytes(nupkgPath, package);

            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.False(result.IsValid);
            Assert.Contains(
                "exactly one CMS value",
                result.Reason,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_MalformedTimestampCryptoDoesNotPromoteTimestamp()
    {
        string nupkgPath = await DownloadPackageAsync(
            "Newtonsoft.Json",
            "13.0.4");
        try
        {
            byte[] package = File.ReadAllBytes(nupkgPath);
            int timestampModulusLsb =
                FindTimestampAuthorityModulusLsb(package);
            package = MutateSignatureByte(package, timestampModulusLsb);
            File.WriteAllBytes(nupkgPath, package);

            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.NotEqual(SignatureStatus.Unsigned, result.Status);
            Assert.Null(result.Timestamp);
            Assert.Equal(result.IsValid, result.PackageContentVerified);

            if (result.IsValid)
            {
                Assert.Equal(SignatureType.Author, result.SignatureType);
                SignatureVerificationResult repository =
                    Assert.IsType<SignatureVerificationResult>(
                        result.CounterSignature);
                Assert.Equal(SignatureStatus.Valid, repository.Status);
                Assert.Equal(
                    SignatureType.Repository,
                    repository.SignatureType);
                Assert.True(repository.PackageContentVerified);
                Assert.NotNull(repository.Timestamp);
            }
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_MalformedRepositoryKeyReturnsTypedInvalid()
    {
        string nupkgPath = await DownloadPackageAsync(
            "Newtonsoft.Json",
            "13.0.4");
        try
        {
            byte[] package = File.ReadAllBytes(nupkgPath);
            int repositoryModulusLsb =
                FindRepositorySignerModulusLsb(package);
            package = MutateSignatureByte(package, repositoryModulusLsb);
            File.WriteAllBytes(nupkgPath, package);

            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.Equal(SignatureStatus.Invalid, result.Status);
            Assert.False(result.PackageContentVerified);
            Assert.NotNull(result.Reason);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_RejectsInvalidSignatureEntryProfiles()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        byte[] original = File.ReadAllBytes(nupkgPath);
        (int central, int local) = FindSignatureHeaders(original);
        (string Name, Action<byte[]> Mutate)[] mutations =
        [
            ("central flags", bytes =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(central + 8), 1)),
            ("central compression", bytes =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(central + 10), 8)),
            ("central external attributes", bytes =>
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(central + 38), 1)),
            ("local flags", bytes =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(local + 6), 1)),
            ("local compression", bytes =>
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(local + 8), 8)),
            ("central/local size mismatch", bytes =>
            {
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(central + 20));
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(central + 20), size + 1);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(central + 24), size + 1);
            }),
        ];

        try
        {
            foreach ((string name, Action<byte[]> mutate) in mutations)
            {
                byte[] package = (byte[])original.Clone();
                mutate(package);
                File.WriteAllBytes(nupkgPath, package);

                SignatureVerificationResult result =
                    PackageSignatureVerifier.VerifyPackage(nupkgPath);

                Assert.False(result.IsValid, name);
                Assert.Contains(
                    "entry is invalid",
                    result.Reason,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public void TryExtractTimestampInfo_BaselinePolicyDefaultsToOneSecond()
    {
        DateTimeOffset timestamp = new(
            2026,
            8,
            20,
            3,
            0,
            0,
            TimeSpan.Zero);
        byte[] tstInfo = CreateTimestampInfo(
            SHA256.HashData([1, 2, 3]),
            timestamp,
            accuracySeconds: null);

        PackageSignatureVerifier.TimestampInfo info = Assert.IsType<
            PackageSignatureVerifier.TimestampInfo>(
                PackageSignatureVerifier.TryExtractTimestampInfo(tstInfo));

        Assert.Equal(timestamp, info.Timestamp.Time);
        Assert.Equal(timestamp.AddSeconds(-1), info.Timestamp.LowerBound);
        Assert.Equal(timestamp.AddSeconds(1), info.Timestamp.UpperBound);
    }

    [Fact]
    public void VerifySignatureFile_UnrepresentableTimestampAccuracyIsInvalid()
    {
        string hash = Convert.ToBase64String(new byte[32]);
        string signaturePath = CreateTestSignature(
            $"Version:1\n\n{Sha256Oid}-Hash:{hash}\n\n",
            AuthorCommitmentOid,
            timestampAccuracySeconds: 500_000_000_000);
        try
        {
            SignatureVerificationResult result =
                PackageSignatureVerifier.VerifySignatureFile(signaturePath);

            Assert.False(result.IsValid);
        }
        finally
        {
            File.Delete(signaturePath);
        }
    }

    [Fact]
    public void VerifyPackage_UnsignedPackage_ReturnsUnsigned()
    {
        // Create a minimal zip with no .signature.p7s
        string tempPath = Path.Combine(Path.GetTempPath(), $"unsigned-{Guid.NewGuid():N}.nupkg");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("test.nuspec");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("<package />");
            }

            var result = PackageSignatureVerifier.VerifyPackage(tempPath);

            Assert.True(result.IsUnsigned);
            Assert.Equal(SignatureStatus.Unsigned, result.Status);
            Assert.Null(result.Publisher);
            Assert.Null(result.Thumbprint);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void VerifyPackage_TamperedSignature_ReturnsInvalid()
    {
        // Create a zip with a bogus .signature.p7s
        string tempPath = Path.Combine(Path.GetTempPath(), $"tampered-{Guid.NewGuid():N}.nupkg");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                var nuspec = archive.CreateEntry("test.nuspec");
                using (var writer = new StreamWriter(nuspec.Open()))
                    writer.Write("<package />");

                var sig = archive.CreateEntry(".signature.p7s");
                using (var stream = sig.Open())
                    stream.Write(new byte[] { 0x30, 0x00 }); // minimal invalid DER
            }

            var result = PackageSignatureVerifier.VerifyPackage(tempPath);

            Assert.False(result.IsValid);
            Assert.Equal(SignatureStatus.Invalid, result.Status);
            Assert.NotNull(result.Reason);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_Stream_WorksLikePath()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            using FileStream stream = File.OpenRead(nupkgPath);
            var result = PackageSignatureVerifier.VerifyPackage(stream);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Publisher);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public void TrustedRoots_CodeSigningRoots_LoadsSuccessfully()
    {
        var roots = TrustedRoots.CodeSigningRoots;
        Assert.NotEmpty(roots);
    }

    [Fact]
    public void TrustedRoots_TimestampingRoots_LoadsSuccessfully()
    {
        var roots = TrustedRoots.TimestampingRoots;
        Assert.NotEmpty(roots);
    }

    [Fact]
    public async Task VerifyPackage_AuthorSignedPackage_HasRepositoryCounterSignature()
    {
        // Newtonsoft.Json is author-signed; nuget.org adds a repository counter-signature
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.Equal(SignatureType.Author, result.SignatureType);
            Assert.NotNull(result.CounterSignature);
            Assert.True(result.CounterSignature.IsValid);
            Assert.Equal(SignatureType.Repository, result.CounterSignature.SignatureType);
            Assert.NotNull(result.CounterSignature.Publisher);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifySignatureFile_MatchesCmsIdentityWithoutPackageBinding()
    {
        string nupkgPath = await DownloadPackageAsync("Newtonsoft.Json", "13.0.3");
        try
        {
            var packageResult = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            // Extract the .signature.p7s from the nupkg
            string tempDir = Path.Combine(Path.GetTempPath(), $"sig-test-{Guid.NewGuid():N}");
            System.IO.Compression.ZipFile.ExtractToDirectory(nupkgPath, tempDir);
            try
            {
                string sigPath = Path.Combine(tempDir, ".signature.p7s");
                Assert.True(File.Exists(sigPath));

                var fileResult = PackageSignatureVerifier.VerifySignatureFile(sigPath);

                Assert.Equal(packageResult.Status, fileResult.Status);
                Assert.Equal(packageResult.Publisher, fileResult.Publisher);
                Assert.Equal(packageResult.SignatureType, fileResult.SignatureType);
                Assert.True(packageResult.PackageContentVerified);
                Assert.False(fileResult.PackageContentVerified);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    [Fact]
    public async Task VerifyPackage_RepositorySignedPackage_HasNoCounterSignature()
    {
        // dotnet-install is repository-signed only (no author signature)
        string nupkgPath = await DownloadPackageAsync("dotnet-install", "0.1.1");
        try
        {
            var result = PackageSignatureVerifier.VerifyPackage(nupkgPath);

            Assert.True(result.IsValid);
            Assert.Equal(SignatureType.Repository, result.SignatureType);
            Assert.Null(result.CounterSignature);
        }
        finally
        {
            File.Delete(nupkgPath);
        }
    }

    private static SignedCms DecodeSignature(string nupkgPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(nupkgPath);
        ZipArchiveEntry signature = Assert.IsType<ZipArchiveEntry>(
            archive.GetEntry(".signature.p7s"));
        using Stream stream = signature.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var cms = new SignedCms();
        cms.Decode(buffer.ToArray());
        return cms;
    }

    private static AsnEncodedData GetTimestampValue(SignerInfo signer)
    {
        CryptographicAttributeObject attribute = Assert.Single(
            signer.UnsignedAttributes.Cast<CryptographicAttributeObject>(),
            value => value.Oid?.Value == TimestampTokenOid);
        return Assert.Single(attribute.Values.Cast<AsnEncodedData>());
    }

    private static string CreateTestSignature(
        string content,
        string? commitmentOid,
        int keySize = 2048,
        bool includeLifetimeSigningEku = false,
        bool includeSigningTime = true,
        bool includeSigningCertificateV2 = true,
        bool mismatchSigningCertificateHash = false,
        long? timestampAccuracySeconds = null)
    {
        using RSA key = RSA.Create(keySize);
        var request = new CertificateRequest(
            "CN=Test package signer",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));
        var usages = new OidCollection
        {
            new Oid(CodeSigningEkuOid),
        };
        if (includeLifetimeSigningEku)
            usages.Add(new Oid(LifetimeSigningEkuOid));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(usages, critical: true));
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(content)));
        var signer = new CmsSigner(certificate);
        if (commitmentOid is not null)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteObjectIdentifier(commitmentOid);
            writer.PopSequence();
            signer.SignedAttributes.Add(new CryptographicAttributeObject(
                new Oid(CommitmentTypeIndicationOid),
                new AsnEncodedDataCollection(
                    new AsnEncodedData(
                        new Oid(CommitmentTypeIndicationOid),
                        writer.Encode()))));
        }
        if (includeSigningTime)
        {
            var signingTime = new Pkcs9SigningTime(DateTime.UtcNow);
            signer.SignedAttributes.Add(new CryptographicAttributeObject(
                new Oid(SigningTimeOid),
                new AsnEncodedDataCollection(signingTime)));
        }
        if (includeSigningCertificateV2)
        {
            AsnEncodedData signingCertificateV2 =
                CreateSigningCertificateV2(
                    certificate,
                    mismatchSigningCertificateHash);
            signer.SignedAttributes.Add(new CryptographicAttributeObject(
                new Oid(SigningCertificateV2Oid),
                new AsnEncodedDataCollection(signingCertificateV2)));
        }
        cms.ComputeSignature(signer);
        if (timestampAccuracySeconds.HasValue)
        {
            SignerInfo primarySigner = Assert.Single(
                cms.SignerInfos.Cast<SignerInfo>());
            byte[] token = CreateTimestampToken(
                primarySigner.GetSignature(),
                timestampAccuracySeconds.Value);
            primarySigner.AddUnsignedAttribute(new AsnEncodedData(
                new Oid(TimestampTokenOid),
                token));
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            $"package-signature-{Guid.NewGuid():N}.p7s");
        File.WriteAllBytes(path, cms.Encode());
        return path;
    }

    private static AsnEncodedData CreateSigningCertificateV2(
        X509Certificate2 certificate,
        bool mismatchHash)
    {
        byte[] certificateHash = certificate.GetCertHash(HashAlgorithmName.SHA256);
        if (mismatchHash)
            certificateHash[0] ^= 0xFF;

        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSequence();
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteObjectIdentifier(Sha256Oid);
        writer.WriteNull();
        writer.PopSequence();
        writer.WriteOctetString(certificateHash);
        writer.PushSequence();
        writer.PushSequence();
        var directoryNameTag = new Asn1Tag(
            TagClass.ContextSpecific,
            4,
            isConstructed: true);
        writer.PushSequence(directoryNameTag);
        writer.WriteEncodedValue(certificate.IssuerName.RawData);
        writer.PopSequence(directoryNameTag);
        writer.PopSequence();
        byte[] serialNumber = certificate.GetSerialNumber();
        Array.Reverse(serialNumber);
        writer.WriteIntegerUnsigned(serialNumber);
        writer.PopSequence();
        writer.PopSequence();
        writer.PopSequence();
        writer.PopSequence();
        return new AsnEncodedData(
            new Oid(SigningCertificateV2Oid),
            writer.Encode());
    }

    private static byte[] CreateTimestampToken(
        byte[] parentSignature,
        long accuracySeconds)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Test timestamp signer",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));
        var usages = new OidCollection
        {
            new Oid("1.3.6.1.5.5.7.3.8"),
        };
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(usages, critical: true));
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        byte[] tstInfo = CreateTimestampInfo(
            SHA256.HashData(parentSignature),
            DateTimeOffset.UtcNow,
            accuracySeconds);
        var timestampCms = new SignedCms(
            new ContentInfo(new Oid(TimestampInfoContentTypeOid), tstInfo));
        timestampCms.ComputeSignature(new CmsSigner(certificate));
        return timestampCms.Encode();
    }

    private static byte[] CreateTimestampInfo(
        byte[] messageHash,
        DateTimeOffset timestamp,
        long? accuracySeconds)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteInteger(1);
        writer.WriteObjectIdentifier(BaselineTimestampPolicyOid);
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteObjectIdentifier(Sha256Oid);
        writer.WriteNull();
        writer.PopSequence();
        writer.WriteOctetString(messageHash);
        writer.PopSequence();
        writer.WriteInteger(1);
        writer.WriteGeneralizedTime(timestamp, omitFractionalSeconds: true);
        if (accuracySeconds.HasValue)
        {
            writer.PushSequence();
            writer.WriteInteger(accuracySeconds.Value);
            writer.PopSequence();
        }
        writer.PopSequence();
        return writer.Encode();
    }

    private static int FindEndRecord(byte[] package)
    {
        for (int index = package.Length - 22; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(index))
                == 0x06054B50)
            {
                return index;
            }
        }

        throw new InvalidDataException("ZIP end record was not found.");
    }

    private static (int Central, int Local) FindSignatureHeaders(byte[] package)
    {
        int endRecord = FindEndRecord(package);
        int central = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            package.AsSpan(endRecord + 16)));
        ushort entries = BinaryPrimitives.ReadUInt16LittleEndian(
            package.AsSpan(endRecord + 10));
        for (int index = 0; index < entries; index++)
        {
            Assert.Equal(
                0x02014B50u,
                BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(central)));
            ushort fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                package.AsSpan(central + 28));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
                package.AsSpan(central + 30));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(
                package.AsSpan(central + 32));
            ReadOnlySpan<byte> fileName = package.AsSpan(
                central + 46,
                fileNameLength);
            if (fileName.SequenceEqual(".signature.p7s"u8))
            {
                int local = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    package.AsSpan(central + 42)));
                return (central, local);
            }

            central = checked(
                central + 46 + fileNameLength + extraLength + commentLength);
        }

        throw new InvalidDataException("Signature entry was not found.");
    }

    private static byte[] AppendSignatureSuffix(
        byte[] package,
        ReadOnlySpan<byte> suffix)
    {
        int endRecord = FindEndRecord(package);
        int centralDirectoryOffset = checked(
            (int)BinaryPrimitives.ReadUInt32LittleEndian(
                package.AsSpan(endRecord + 16)));
        (int central, int local) = FindSignatureHeaders(package);
        uint signatureLength = BinaryPrimitives.ReadUInt32LittleEndian(
            package.AsSpan(central + 20));
        ushort fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(
            package.AsSpan(local + 26));
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
            package.AsSpan(local + 28));
        int dataOffset = checked(local + 30 + fileNameLength + extraLength);
        Assert.Equal(
            centralDirectoryOffset,
            checked(dataOffset + (int)signatureLength));

        byte[] mutated = new byte[checked(package.Length + suffix.Length)];
        package.AsSpan(0, centralDirectoryOffset).CopyTo(mutated);
        suffix.CopyTo(mutated.AsSpan(centralDirectoryOffset));
        package.AsSpan(centralDirectoryOffset).CopyTo(
            mutated.AsSpan(centralDirectoryOffset + suffix.Length));

        uint updatedLength = checked(signatureLength + (uint)suffix.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mutated.AsSpan(local + 18),
            updatedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mutated.AsSpan(local + 22),
            updatedLength);
        int shiftedCentral = checked(central + suffix.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mutated.AsSpan(shiftedCentral + 20),
            updatedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mutated.AsSpan(shiftedCentral + 24),
            updatedLength);
        uint crc = CalculateCrc32(mutated.AsSpan(dataOffset, (int)updatedLength));
        BinaryPrimitives.WriteUInt32LittleEndian(
            mutated.AsSpan(local + 14),
            crc);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mutated.AsSpan(shiftedCentral + 16),
            crc);
        int shiftedEndRecord = checked(endRecord + suffix.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mutated.AsSpan(shiftedEndRecord + 16),
            checked((uint)(centralDirectoryOffset + suffix.Length)));
        return mutated;
    }

    private static byte[] MutateSignatureByte(
        byte[] package,
        int signatureRelativeOffset)
    {
        (int central, int local) = FindSignatureHeaders(package);
        uint signatureLength = BinaryPrimitives.ReadUInt32LittleEndian(
            package.AsSpan(central + 20));
        Assert.InRange(
            signatureRelativeOffset,
            0,
            checked((int)signatureLength - 1));
        ushort fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(
            package.AsSpan(local + 26));
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
            package.AsSpan(local + 28));
        int dataOffset = checked(local + 30 + fileNameLength + extraLength);

        byte[] mutated = (byte[])package.Clone();
        mutated[dataOffset + signatureRelativeOffset] ^= 0x01;
        uint crc = CalculateCrc32(
            mutated.AsSpan(dataOffset, checked((int)signatureLength)));
        BinaryPrimitives.WriteUInt32LittleEndian(
            mutated.AsSpan(local + 14),
            crc);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mutated.AsSpan(central + 16),
            crc);
        return mutated;
    }

    private static int FindTimestampAuthorityModulusLsb(byte[] package)
    {
        byte[] signature = ExtractSignatureBytes(package);
        var cms = new SignedCms();
        cms.Decode(signature);
        SignerInfo signer = Assert.Single(
            cms.SignerInfos.Cast<SignerInfo>());
        byte[] token = GetTimestampValue(signer).RawData;
        int tokenOffset = IndexOfUnique(signature, token);

        var timestampCms = new SignedCms();
        timestampCms.Decode(token);
        X509Certificate2 certificate = Assert.IsType<X509Certificate2>(
            Assert.Single(timestampCms.SignerInfos.Cast<SignerInfo>())
                .Certificate);
        return tokenOffset + FindCertificateModulusLsb(token, certificate);
    }

    private static int FindRepositorySignerModulusLsb(byte[] package)
    {
        byte[] signature = ExtractSignatureBytes(package);
        var cms = new SignedCms();
        cms.Decode(signature);
        SignerInfo signer = Assert.Single(
            cms.SignerInfos.Cast<SignerInfo>());
        SignerInfo repositorySigner = Assert.Single(
            signer.CounterSignerInfos.Cast<SignerInfo>());
        X509Certificate2 certificate = Assert.IsType<X509Certificate2>(
            repositorySigner.Certificate);
        return FindCertificateModulusLsb(signature, certificate);
    }

    private static int FindCertificateModulusLsb(
        byte[] container,
        X509Certificate2 certificate)
    {
        byte[] certificateBytes = certificate.RawData;
        int certificateOffset = IndexOfUnique(container, certificateBytes);
        byte[] keyBytes = certificate.PublicKey.EncodedKeyValue.RawData;
        int keyOffset = IndexOfUnique(certificateBytes, keyBytes);
        var keyReader = new AsnReader(keyBytes, AsnEncodingRules.DER);
        AsnReader keySequence = keyReader.ReadSequence();
        ReadOnlyMemory<byte> modulus = keySequence.ReadIntegerBytes();
        keySequence.ReadIntegerBytes();
        keySequence.ThrowIfNotEmpty();
        keyReader.ThrowIfNotEmpty();
        int modulusOffset = IndexOfUnique(keyBytes, modulus.Span);
        return checked(
            certificateOffset
            + keyOffset
            + modulusOffset
            + modulus.Length
            - 1);
    }

    private static byte[] ExtractSignatureBytes(byte[] package)
    {
        (int central, int local) = FindSignatureHeaders(package);
        int signatureLength = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(
                package.AsSpan(central + 20)));
        ushort fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(
            package.AsSpan(local + 26));
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
            package.AsSpan(local + 28));
        int dataOffset = checked(local + 30 + fileNameLength + extraLength);
        return package.AsSpan(dataOffset, signatureLength).ToArray();
    }

    private static int IndexOfUnique(
        ReadOnlySpan<byte> container,
        ReadOnlySpan<byte> value)
    {
        Assert.False(value.IsEmpty);
        int first = container.IndexOf(value);
        Assert.True(first >= 0, "Expected the encoded value in its container.");
        int second = container[(first + 1)..].IndexOf(value);
        Assert.Equal(-1, second);
        return first;
    }

    private static uint CalculateCrc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0
                    ? crc >> 1
                    : (crc >> 1) ^ 0xEDB88320;
            }
        }

        return ~crc;
    }

    private async Task<string> DownloadPackageAsync(string id, string version)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{id}.{version}.nupkg");
        await _client.DownloadToFileAsync(id, version, path);
        return path;
    }
}
