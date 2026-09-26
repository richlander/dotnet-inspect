using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MsdlProxy;

internal static class PackageChangeProxyClient
{
    internal const string NuGetClientName = "package-changes-nuget";
    internal const string AdvisoryClientName = "package-changes-advisories";
    internal const long MaxNuGetDocumentBytes = 16L * 1024 * 1024;
    internal const long MaxAdvisoryDocumentBytes = 4L * 1024 * 1024;
    private const int MaxLinkHeaderCharacters = 16 * 1024;

    internal static async Task<IActionResult> GetNuGetJsonAsync(
        HttpClient client,
        Uri upstream,
        CancellationToken cancellationToken) =>
        await GetJsonAsync(
            client,
            upstream,
            MaxNuGetDocumentBytes,
            allowMissingJsonContentType:
                upstream.AbsolutePath.StartsWith(
                    "/v3/catalog0/",
                    StringComparison.Ordinal),
            preserveLink: false,
            output: null,
            configureRequest: static request =>
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/json")),
            cancellationToken).ConfigureAwait(false);

    internal static async Task<IActionResult> GetAdvisoryJsonAsync(
        HttpClient client,
        Uri upstream,
        HttpResponse output,
        CancellationToken cancellationToken) =>
        await GetJsonAsync(
            client,
            upstream,
            MaxAdvisoryDocumentBytes,
            allowMissingJsonContentType: false,
            preserveLink: true,
            output,
            configureRequest: static request =>
            {
                request.Headers.UserAgent.ParseAdd("dotnet-inspect");
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/vnd.github+json"));
                request.Headers.TryAddWithoutValidation(
                    "X-GitHub-Api-Version",
                    "2022-11-28");
            },
            cancellationToken).ConfigureAwait(false);

    internal static HttpMessageHandler CreatePrimaryHandler() =>
        new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            Credentials = null,
            PreAuthenticate = false,
            UseCookies = false,
        };

    private static async Task<IActionResult> GetJsonAsync(
        HttpClient client,
        Uri upstream,
        long maximumBytes,
        bool allowMissingJsonContentType,
        bool preserveLink,
        HttpResponse? output,
        Action<HttpRequestMessage> configureRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(upstream);
        using var request = new HttpRequestMessage(HttpMethod.Get, upstream);
        using var upstreamCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        if (client.Timeout != Timeout.InfiniteTimeSpan)
            upstreamCancellation.CancelAfter(client.Timeout);
        configureRequest(request);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                upstreamCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new StatusCodeResult(
                StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            return new StatusCodeResult(
                StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new StatusCodeResult(
                    response.IsSuccessStatusCode
                        ? StatusCodes.Status502BadGateway
                        : (int)response.StatusCode);
            }
            HttpContent? content = response.Content;
            if (content is null)
            {
                return new StatusCodeResult(
                    StatusCodes.Status502BadGateway);
            }
            string? mediaType = content.Headers.ContentType?.MediaType;
            if ((!allowMissingJsonContentType || mediaType is not null)
                && !string.Equals(
                    mediaType,
                    "application/json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new StatusCodeResult(
                    StatusCodes.Status502BadGateway);
            }
            if (content.Headers.ContentLength is { } declaredLength
                && declaredLength > maximumBytes)
            {
                return new StatusCodeResult(
                    StatusCodes.Status413PayloadTooLarge);
            }

            string? link = null;
            if (preserveLink
                && response.Headers.TryGetValues(
                    "Link",
                    out IEnumerable<string>? linkValues))
            {
                link = string.Join(", ", linkValues);
                if (link.Length > MaxLinkHeaderCharacters)
                {
                    return new StatusCodeResult(
                        StatusCodes.Status502BadGateway);
                }
            }

            byte[]? bytes;
            try
            {
                bytes = await ReadBoundedAsync(
                    content,
                    maximumBytes,
                    upstreamCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return new StatusCodeResult(
                    StatusCodes.Status504GatewayTimeout);
            }
            catch (Exception exception)
                when (exception is HttpRequestException or IOException)
            {
                return new StatusCodeResult(
                    StatusCodes.Status502BadGateway);
            }
            if (bytes is null)
            {
                return new StatusCodeResult(
                    StatusCodes.Status413PayloadTooLarge);
            }

            if (link is not null)
                output!.Headers.Link = link;
            return new FileContentResult(bytes, "application/json");
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using Stream input =
            await content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                int read = await input.ReadAsync(
                    buffer,
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return output.ToArray();
                if (output.Length + read > maximumBytes)
                    return null;
                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
