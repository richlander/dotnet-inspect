using System.Net.Http.Headers;
using System.Text;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Pins the boundary of credential acquisition: which of the mechanisms a user can plausibly
/// reach for actually authenticate a NuGet source, and which are silently ignored.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one mechanism is supported: a <c>&lt;packageSourceCredentials&gt;</c> entry carrying
/// <em>both</em> <c>Username</c> and <c>ClearTextPassword</c>. Everything else on this list is
/// dropped without a diagnostic, and — because an unauthenticated feed currently reports as
/// "package not found" rather than as an authentication failure — a dropped credential is
/// indistinguishable from a typo in the package name. That is what makes these worth pinning:
/// the failure mode is silent, so only a test keeps the boundary honest.
/// </para>
/// <para>
/// Worth knowing while reading these: NuGet ranks credential mechanisms from most to least
/// secure, and the one mechanism supported here is the one it ranks last and warns about
/// leaking. The four it prefers — credential provider, encrypted password, environment-variable
/// macros, and environment-variable credentials — are each covered below as unsupported. See
/// docs/design/nuget-authentication.md.
/// </para>
/// <para>
/// The "not supported" tests below assert current behaviour, not desired behaviour. Implementing
/// any of them should turn the corresponding test red; update the test in the same change, so
/// support is added deliberately and visibly rather than by accident.
/// </para>
/// <para>
/// All tests here are hermetic — config parsing and header construction only, no network. The
/// live counterparts that prove these same conclusions against a real Azure DevOps feed are in
/// <see cref="AzureDevOpsFeedTests"/>, gated behind <c>Network=Live</c>.
/// </para>
/// </remarks>
public sealed class CredentialMechanismTests : IDisposable
{
    private const string FeedUrl = "https://feed.example/v3/index.json";

    private readonly string _tempDir;

    public CredentialMechanismTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nf-cred-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ---------------------------------------------------------------------
    // The one supported mechanism.
    // ---------------------------------------------------------------------

    [Fact]
    public void UsernameAndClearTextPassword_AuthenticateAsBasic()
    {
        string config = WriteConfig(Credentials("""
                  <add key="Username" value="pat" />
                  <add key="ClearTextPassword" value="s3cret" />
            """));

        PackageSource source = Single(SourceResolver.ResolveSources(configPath: config));

        Assert.NotNull(source.Credential);
        Assert.Equal("pat", source.Credential!.Username);
        Assert.Equal("s3cret", source.Credential.Password);

        // Basic is the only scheme the client can speak: a PAT and an Entra access token both
        // work only because each is simply "the password".
        AuthenticationHeaderValue? header = source.GetAuthHeader();
        Assert.NotNull(header);
        Assert.Equal("Basic", header!.Scheme);
        Assert.Equal("pat:s3cret", DecodeBasic(header));
    }

    [Fact]
    public void AccessTokenAsPassword_IsPassedThroughUnaltered()
    {
        // Azure DevOps ignores the username entirely and accepts either a PAT or an Entra
        // access token as the password. Neither is special-cased; both are opaque strings.
        const string Token = "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.payload.signature";

        string config = WriteConfig(Credentials($"""
                  <add key="Username" value="anything" />
                  <add key="ClearTextPassword" value="{Token}" />
            """));

        PackageSource source = Single(SourceResolver.ResolveSources(configPath: config));

        Assert.Equal($"anything:{Token}", DecodeBasic(source.GetAuthHeader()));
    }

    [Fact]
    public void NoCredentials_LeavesSourceUnauthenticated()
    {
        PackageSource source = Single(SourceResolver.ResolveSources(configPath: WriteConfig()));

        Assert.Null(source.Credential);
        Assert.Null(source.GetAuthHeader());
    }

    // ---------------------------------------------------------------------
    // Mechanisms that look like they should work, and do not.
    // ---------------------------------------------------------------------

    [Fact]
    public void EncryptedPasswordElement_IsNotSupported()
    {
        // `dotnet nuget add source --password` writes an encrypted <Password> by default on
        // Windows. The parser only reads <ClearTextPassword>, so such a config authenticates
        // nothing. NuGetFetch ships no DPAPI decryptor, and DPAPI is Windows-only regardless.
        string config = WriteConfig(Credentials("""
                  <add key="Username" value="pat" />
                  <add key="Password" value="AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA" />
            """));

        PackageSource source = Single(SourceResolver.ResolveSources(configPath: config));

        Assert.Null(source.Credential);
    }

    [Fact]
    public void ClearTextPasswordWithoutUsername_IsNotSupported()
    {
        // Azure DevOps ignores the username, so omitting it is a natural thing to do. The
        // parser requires both halves, so the credential is dropped.
        string config = WriteConfig(Credentials("""
                  <add key="ClearTextPassword" value="s3cret" />
            """));

        PackageSource source = Single(SourceResolver.ResolveSources(configPath: config));

        Assert.Null(source.Credential);
    }

    [Fact]
    public void UsernameWithoutPassword_IsNotSupported()
    {
        string config = WriteConfig(Credentials("""
                  <add key="Username" value="pat" />
            """));

        PackageSource source = Single(SourceResolver.ResolveSources(configPath: config));

        Assert.Null(source.Credential);
    }

    [Fact]
    public void NuGetPackageSourceCredentialsEnvironmentVariable_IsNotSupported()
    {
        // The official NuGet client reads NuGetPackageSourceCredentials_<name>, which is the
        // documented way to keep a secret out of a config file. NuGetFetch never consults the
        // environment for credentials.
        using EnvironmentVariable _ = new(
            $"NuGetPackageSourceCredentials_{SourceName}",
            "Username=pat;ClearTextPassword=s3cret");

        PackageSource source = Single(SourceResolver.ResolveSources(configPath: WriteConfig()));

        Assert.Null(source.Credential);
    }

    [Fact]
    public void EnvironmentVariableExpansion_InConfigValues_IsNotSupported()
    {
        // The official NuGet client expands %VAR% in config values. This parser takes values
        // verbatim, so the placeholder is sent as the literal password.
        using EnvironmentVariable _ = new("NUGETFETCH_TEST_TOKEN", "s3cret");

        string config = WriteConfig(Credentials("""
                  <add key="Username" value="pat" />
                  <add key="ClearTextPassword" value="%NUGETFETCH_TEST_TOKEN%" />
            """));

        PackageSource source = Single(SourceResolver.ResolveSources(configPath: config));

        Assert.NotNull(source.Credential);
        Assert.Equal("%NUGETFETCH_TEST_TOKEN%", source.Credential!.Password);
    }

    [Fact]
    public void InlineCredentialsInSourceUrl_AreNotSupported()
    {
        // A userinfo-bearing URL carries no credential into the request: nothing parses it out
        // into an Authorization header, and HttpClient does not send userinfo on its own.
        PackageSource source = Single(SourceResolver.ResolveSources(
            explicitSource: "https://pat:s3cret@feed.example/v3/index.json"));

        Assert.Null(source.Credential);
        Assert.Null(source.GetAuthHeader());
    }

    [Fact]
    public void CredentialProviderEnvironment_IsNotConsulted()
    {
        // The Azure Artifacts Credential Provider is Microsoft's recommended mechanism, and
        // these variables are how it is fed in CI. Nothing in NuGetFetch reads them, and no
        // NuGet plugin is ever invoked, so a pipeline configured the recommended way still
        // reaches an Azure DevOps feed unauthenticated.
        using EnvironmentVariable token = new("ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN", "s3cret");
        using EnvironmentVariable prefixes = new("ARTIFACTS_CREDENTIALPROVIDER_URI_PREFIXES", "https://feed.example/");
        using EnvironmentVariable legacy = new(
            "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS",
            $$"""{"endpointCredentials":[{"endpoint":"{{FeedUrl}}","username":"pat","password":"s3cret"}]}""");
        using EnvironmentVariable plugins = new("NUGET_PLUGIN_PATHS", "/nonexistent/credential-provider");

        PackageSource source = Single(SourceResolver.ResolveSources(configPath: WriteConfig()));

        Assert.Null(source.Credential);
    }

    // ---------------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------------

    private const string SourceName = "contoso";

    private static string Credentials(string entries) => $"""
          <packageSourceCredentials>
            <{SourceName}>
        {entries}
            </{SourceName}>
          </packageSourceCredentials>
        """;

    private string WriteConfig(string credentials = "")
    {
        string path = Path.Combine(_tempDir, $"nuget-{Guid.NewGuid():N}.config");

        File.WriteAllText(path, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="{SourceName}" value="{FeedUrl}" />
              </packageSources>
            {credentials}
            </configuration>
            """);

        return path;
    }

    private static PackageSource Single(IReadOnlyList<PackageSource> sources) => Assert.Single(sources);

    private static string DecodeBasic(AuthenticationHeaderValue? header)
    {
        Assert.NotNull(header);
        Assert.Equal("Basic", header!.Scheme);
        Assert.NotNull(header.Parameter);

        return Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter!));
    }

    /// <summary>Sets an environment variable for the life of the scope and restores it after.</summary>
    private sealed class EnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        public EnvironmentVariable(string name, string? value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }
}
