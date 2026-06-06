// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

namespace DotnetInspector.Services;

/// <summary>
/// Result of NuGet package signature verification.
/// </summary>
public record SignatureVerificationResult
{
    /// <summary>
    /// Author/publisher identity from the author signature CN.
    /// </summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// Whether the author signature was verified successfully.
    /// </summary>
    public bool AuthorVerified { get; init; }

    /// <summary>
    /// Whether the repository signature was verified successfully.
    /// </summary>
    public bool RepositoryVerified { get; init; }

    /// <summary>
    /// Repository source (e.g., "nuget.org").
    /// </summary>
    public string? Repository { get; init; }

    /// <summary>
    /// Status message when verification was skipped or failed.
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Whether the package has no signatures at all.
    /// </summary>
    public bool IsUnsigned { get; init; }
}

/// <summary>
/// Verifies NuGet package signatures using NuGet.Packaging.
/// </summary>
public static class SignatureVerifier
{
    /// <summary>
    /// Verifies the signature of a NuGet package.
    /// </summary>
    /// <param name="nupkgPath">Path to the .nupkg file</param>
    /// <returns>Verification result, or null if verification could not be performed</returns>
    public static SignatureVerificationResult? Verify(string nupkgPath)
    {
        if (!File.Exists(nupkgPath))
            return null;

        try
        {
            using FileStream nupkgStream = File.OpenRead(nupkgPath);
            using var reader = new PackageArchiveReader(nupkgStream);
            if (!reader.IsSignedAsync(CancellationToken.None).GetAwaiter().GetResult())
            {
                return new SignatureVerificationResult
                {
                    IsUnsigned = true,
                    StatusMessage = "Package is not signed"
                };
            }

            PrimarySignature? signature = reader.GetPrimarySignatureAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (signature == null)
            {
                return new SignatureVerificationResult
                {
                    StatusMessage = "Verification failed: package signature could not be read"
                };
            }

            reader.ValidateIntegrityAsync(signature.SignatureContent, CancellationToken.None).GetAwaiter().GetResult();

            RepositoryCountersignature? repositoryCountersignature = RepositoryCountersignature.GetRepositoryCountersignature(signature);
            bool isAuthorSignature = signature.Type == SignatureType.Author;
            bool hasRepositorySignature = signature.Type == SignatureType.Repository || repositoryCountersignature != null;

            return new SignatureVerificationResult
            {
                // Only report publisher for author-signed packages;
                // for repo-only signatures the CN is the repository, not the author.
                Publisher = isAuthorSignature ? GetCertificateDisplayName(signature.SignerInfo.Certificate) : null,
                AuthorVerified = isAuthorSignature,
                RepositoryVerified = hasRepositorySignature,
                Repository = hasRepositorySignature ? "nuget.org" : null,
            };
        }
        catch (CryptographicException ex)
        {
            return new SignatureVerificationResult
            {
                StatusMessage = $"Verification failed: {ex.Message}"
            };
        }
        catch (SignatureException ex)
        {
            return new SignatureVerificationResult
            {
                StatusMessage = $"Verification failed: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new SignatureVerificationResult
            {
                StatusMessage = $"Verification failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Async wrapper for compatibility with existing callers.
    /// The underlying verification is synchronous (in-process crypto).
    /// </summary>
    public static Task<SignatureVerificationResult?> VerifyAsync(string nupkgPath) =>
        Task.FromResult(Verify(nupkgPath));

    private static string? GetCertificateDisplayName(X509Certificate2? certificate)
        => certificate?.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
}
