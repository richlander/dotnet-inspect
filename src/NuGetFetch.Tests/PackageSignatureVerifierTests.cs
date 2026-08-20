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
    private const string ServerAuthenticationEkuOid = "1.3.6.1.5.5.7.3.1";
    private const string TimestampTokenOid = "1.2.840.113549.1.9.16.2.14";

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
        string? commitmentOid)
    {
        using RSA key = RSA.Create(2048);
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
        cms.ComputeSignature(signer);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"package-signature-{Guid.NewGuid():N}.p7s");
        File.WriteAllBytes(path, cms.Encode());
        return path;
    }

    private async Task<string> DownloadPackageAsync(string id, string version)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{id}.{version}.nupkg");
        await _client.DownloadToFileAsync(id, version, path);
        return path;
    }
}
