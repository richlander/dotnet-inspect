using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace NuGetFetch;

/// <summary>
/// Loads trusted root CA certificates from embedded PEM bundles.
/// These are the same certificates shipped with the .NET SDK for NuGet signing verification.
/// </summary>
public static class TrustedRoots
{
    private static X509Certificate2Collection? s_codeSigningRoots;
    private static X509Certificate2Collection? s_timestampingRoots;
    private static readonly object s_lock = new();

    public static X509Certificate2Collection CodeSigningRoots =>
        LazyLoad(ref s_codeSigningRoots, "codesignctl.pem");

    public static X509Certificate2Collection TimestampingRoots =>
        LazyLoad(ref s_timestampingRoots, "timestampctl.pem");

    private static X509Certificate2Collection LazyLoad(
        ref X509Certificate2Collection? field, string resourceName)
    {
        if (field is not null)
            return field;

        lock (s_lock)
        {
            if (field is not null)
                return field;

            field = LoadFromResource(resourceName);
            return field;
        }
    }

    private static X509Certificate2Collection LoadFromResource(string resourceName)
    {
        Assembly assembly = typeof(TrustedRoots).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

        using StreamReader reader = new(stream);
        string pemText = reader.ReadToEnd();

        X509Certificate2Collection certs = new();
        certs.ImportFromPem(pemText);
        return certs;
    }
}
