using System.Text;

using DotnetInspect.Cli.Options;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace DotnetInspect.Cli.Commands;

internal sealed class WorkspacePatBindingException(string message)
    : Exception(message);

internal static class WorkspacePatCredentialResolver
{
    internal const int MaxSecretUtf8Bytes = 64 * 1024;

    private static readonly UTF8Encoding s_strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static async Task<PackageSource[]> ResolveAsync(
        IReadOnlyList<WorkspacePackageSourceDefinition> sourceDefinitions,
        IReadOnlyList<WorkspacePatBindingInput> bindings,
        CancellationToken cancellationToken) =>
        await ResolveAsync(
            sourceDefinitions,
            bindings,
            Console.IsInputRedirected,
            Console.OpenStandardInput,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<PackageSource[]> ResolveAsync(
        IReadOnlyList<WorkspacePackageSourceDefinition> sourceDefinitions,
        IReadOnlyList<WorkspacePatBindingInput> bindings,
        bool isInputRedirected,
        Func<Stream> openStandardInput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinitions);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(openStandardInput);

        Dictionary<string, WorkspacePatBindingInput> suppliedBindings =
            ValidateBindingsCore(sourceDefinitions, bindings);

        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string endpoint, WorkspacePatBindingInput binding)
            in suppliedBindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            secrets.Add(
                endpoint,
                await ReadSecretAsync(
                        binding,
                        isInputRedirected,
                        openStandardInput,
                        cancellationToken)
                    .ConfigureAwait(false));
        }

        return
        [
            .. sourceDefinitions.Select(source =>
                new PackageSource(
                    source.Endpoint,
                    source.Endpoint,
                    suppliedBindings.TryGetValue(
                        source.Endpoint,
                        out WorkspacePatBindingInput? binding)
                        ? new PackageSourceCredential(
                            binding.Username,
                            secrets[source.Endpoint])
                        : null)),
        ];
    }

    internal static async Task<WorkspacePackageSourceRuntime> BindAsync(
        IReadOnlyList<WorkspacePackageSourceDefinition> sourceDefinitions,
        IReadOnlyList<WorkspacePatBindingInput> bindings,
        CancellationToken cancellationToken)
    {
        PackageSource[] sources = await ResolveAsync(
            sourceDefinitions,
            bindings,
            cancellationToken).ConfigureAwait(false);
        HashSet<string> explicitlyBoundEndpoints =
        [
            .. bindings.Select(static binding => binding.Endpoint),
        ];
        WorkspacePackageSourceDefinition[] providerSources =
        [
            .. sourceDefinitions.Where(source =>
                source.Authentication
                    == WorkspacePackageSourceAuthentication.AuthenticationRequired
                && !explicitlyBoundEndpoints.Contains(source.Endpoint)),
        ];
        if (providerSources.Length == 0)
        {
            return new WorkspacePackageSourceRuntime(
                sources,
                DotnetInspector.Networking.HttpClientFactory
                    .CreateClientWithAuthentication(
                        inner => CreatePackageRequestHandler(
                            inner,
                            credentialSource: null)),
                UnavailableWorkspaceCredentialSource.Instance,
                credentialProvider: null);
        }

        var provider = new PluginCredentialProvider();
        try
        {
            var scopedProvider = new WorkspaceCredentialSource(
                provider,
                providerSources.Select(
                    static source => new Uri(source.Endpoint)));
            HttpClient client =
                DotnetInspector.Networking.HttpClientFactory
                    .CreateClientWithAuthentication(
                        inner => CreatePackageRequestHandler(
                            inner,
                            scopedProvider));
            return new WorkspacePackageSourceRuntime(
                sources,
                client,
                scopedProvider,
                provider);
        }
        catch
        {
            await provider.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static void ValidateBindings(
        IReadOnlyList<WorkspacePackageSourceDefinition> sourceDefinitions,
        IReadOnlyList<WorkspacePatBindingInput> bindings)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinitions);
        ArgumentNullException.ThrowIfNull(bindings);
        _ = ValidateBindingsCore(sourceDefinitions, bindings);
    }

    private static Dictionary<string, WorkspacePatBindingInput>
        ValidateBindingsCore(
        IReadOnlyList<WorkspacePackageSourceDefinition> sourceDefinitions,
        IReadOnlyList<WorkspacePatBindingInput> bindings)
    {
        WorkspacePackageSourceDefinition.ValidateSet(sourceDefinitions);
        var requirements = sourceDefinitions
            .Where(static source =>
                source.Authentication
                    == WorkspacePackageSourceAuthentication.AuthenticationRequired)
            .ToDictionary(
                static source => source.Endpoint,
                StringComparer.Ordinal);
        var suppliedBindings = new Dictionary<
            string,
            WorkspacePatBindingInput>(StringComparer.Ordinal);
        foreach (WorkspacePatBindingInput binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Username))
            {
                throw new WorkspacePatBindingException(
                    $"PAT binding username for endpoint '{binding.Endpoint}' "
                        + "must not be empty.");
            }
            if (!suppliedBindings.TryAdd(binding.Endpoint, binding))
            {
                throw new WorkspacePatBindingException(
                    $"A PAT binding for endpoint '{binding.Endpoint}' was "
                        + "supplied more than once.");
            }
            if (!requirements.ContainsKey(binding.Endpoint))
            {
                throw new WorkspacePatBindingException(
                    $"PAT binding endpoint '{binding.Endpoint}' is not an "
                        + "authentication-required source in this Workspace.");
            }
        }

        foreach (IGrouping<string, WorkspacePackageSourceDefinition> origin
            in requirements.Values.GroupBy(
                static source => new Uri(source.Endpoint)
                    .GetLeftPart(UriPartial.Authority),
                StringComparer.Ordinal))
        {
            int explicitCount = origin.Count(
                source => suppliedBindings.ContainsKey(source.Endpoint));
            if (explicitCount != 0 && explicitCount != origin.Count())
            {
                throw new WorkspacePatBindingException(
                    $"Authenticated Workspace source origin '{origin.Key}' "
                        + "mixes explicit PAT bindings with credential-provider "
                        + "fallback. Bind every endpoint on that origin or none.");
            }
        }

        return suppliedBindings;
    }

    internal static HttpMessageHandler CreatePackageRequestHandler(
        HttpMessageHandler innerHandler,
        ICredentialSource? credentialSource)
    {
        ArgumentNullException.ThrowIfNull(innerHandler);
        HttpMessageHandler handler = innerHandler;
        if (credentialSource is not null)
        {
            handler = new PluginAuthenticationHandler(
                credentialSource,
                handler);
        }

        return new NuGetCredentialRedirectHandler(handler);
    }

    private static async Task<string> ReadSecretAsync(
        WorkspacePatBindingInput binding,
        bool isInputRedirected,
        Func<Stream> openStandardInput,
        CancellationToken cancellationToken)
    {
        string value = binding.Kind switch
        {
            WorkspacePatInputKind.Environment =>
                ReadEnvironment(binding.Endpoint, binding.Value!),
            WorkspacePatInputKind.StandardInput =>
                await ReadStandardInputAsync(
                    binding.Endpoint,
                    isInputRedirected,
                    openStandardInput,
                    cancellationToken).ConfigureAwait(false),
            WorkspacePatInputKind.File =>
                await ReadFileAsync(
                    binding.Endpoint,
                    binding.Value!,
                    cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "Unknown Workspace PAT input kind."),
        };
        return Validate(
            binding.Endpoint,
            value,
            trimTrailingLineEnding:
                binding.Kind is WorkspacePatInputKind.StandardInput
                    or WorkspacePatInputKind.File);
    }

    private static string ReadEnvironment(string endpoint, string variable)
    {
        if (variable.Contains('=') || variable.Contains('\0'))
        {
            throw new WorkspacePatBindingException(
                $"The environment variable name for endpoint '{endpoint}' is invalid.");
        }

        return Environment.GetEnvironmentVariable(variable)
            ?? throw new WorkspacePatBindingException(
                $"Environment variable '{variable}' for Workspace source "
                    + $"'{endpoint}' is not set.");
    }

    private static async Task<string> ReadStandardInputAsync(
        string endpoint,
        bool isInputRedirected,
        Func<Stream> openStandardInput,
        CancellationToken cancellationToken)
    {
        if (!isInputRedirected)
        {
            throw new WorkspacePatBindingException(
                $"PAT input for Workspace source '{endpoint}' selected stdin, "
                    + "but stdin is a terminal. Pipe the credential instead.");
        }

        return await ReadUtf8Async(
            openStandardInput(),
            endpoint,
            "stdin",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadFileAsync(
        string endpoint,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await ReadUtf8Async(
                stream,
                endpoint,
                "file",
                cancellationToken).ConfigureAwait(false);
        }
        catch (WorkspacePatBindingException)
        {
            throw;
        }
        catch (Exception ex) when (ex is
            ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            throw new WorkspacePatBindingException(
                $"The PAT file for Workspace source '{endpoint}' could not be read.");
        }
    }

    private static async Task<string> ReadUtf8Async(
        Stream stream,
        string endpoint,
        string provider,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        using var payload = new MemoryStream();
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(
                    buffer,
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (payload.Length + read > MaxSecretUtf8Bytes)
                {
                    throw new WorkspacePatBindingException(
                        $"PAT input from {provider} for Workspace source "
                            + $"'{endpoint}' exceeds the "
                            + $"{MaxSecretUtf8Bytes}-byte limit.");
                }
                payload.Write(buffer, 0, read);
                Array.Clear(buffer, 0, read);
            }

            try
            {
                return s_strictUtf8.GetString(payload.GetBuffer(), 0, (int)payload.Length);
            }
            catch (DecoderFallbackException)
            {
                throw new WorkspacePatBindingException(
                    $"PAT input from {provider} for Workspace source "
                        + $"'{endpoint}' is not valid UTF-8.");
            }
        }
        finally
        {
            Array.Clear(buffer);
            if (payload.TryGetBuffer(out ArraySegment<byte> segment))
                Array.Clear(segment.Array!, segment.Offset, segment.Count);
        }
    }

    private static string Validate(
        string endpoint,
        string value,
        bool trimTrailingLineEnding)
    {
        try
        {
            if (s_strictUtf8.GetByteCount(value) > MaxSecretUtf8Bytes)
            {
                throw new WorkspacePatBindingException(
                    $"PAT input for Workspace source '{endpoint}' exceeds the "
                        + $"{MaxSecretUtf8Bytes}-byte limit.");
            }
        }
        catch (EncoderFallbackException)
        {
            throw new WorkspacePatBindingException(
                $"PAT input for Workspace source '{endpoint}' contains invalid Unicode.");
        }

        string validated = trimTrailingLineEnding
            ? value.EndsWith("\r\n", StringComparison.Ordinal)
                ? value[..^2]
                : value.EndsWith('\r') || value.EndsWith('\n')
                    ? value[..^1]
                    : value
            : value;
        if (validated.Length == 0)
        {
            throw new WorkspacePatBindingException(
                $"PAT input for Workspace source '{endpoint}' is empty.");
        }
        return validated;
    }
}
