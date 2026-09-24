using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace DotnetInspect.Web.Interop.Source;

[SupportedOSPlatform("browser")]
internal static class BrowserSourceDiffJson
{
    internal const string BoundedFailureError = "Source comparison failed.";
    internal const string BoundedFailureDiagnostic =
        "Source comparison failure details exceeded the diagnostic limit.";

    internal static string Serialize(BrowserSourceComparisonResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        try
        {
            BrowserSourceDiffProjection.AdmitAuxiliaryText(
                AuxiliaryText(result));
            return SerializeBounded(result);
        }
        catch (BrowserSourceDiffCapacityException)
            when (result is
            {
                Kind: BrowserSourceComparisonResultKind.Failed,
                FailureKind: not null,
            })
        {
            var boundedFailure = new BrowserSourceComparisonResult(
                Version: 1,
                BrowserSourceComparisonResultKind.Failed,
                Value: null,
                result.FailureKind,
                BoundedFailureError,
                BoundedFailureDiagnostic,
                Reason: null,
                Capacity: null);
            return SerializeBounded(boundedFailure);
        }
        catch (BrowserSourceDiffCapacityException error)
        {
            var refusal = new BrowserSourceComparisonResult(
                Version: 1,
                BrowserSourceComparisonResultKind.TooComplex,
                Value: null,
                FailureKind: null,
                Error: null,
                Diagnostic: null,
                Reason: null,
                error.Capacity);
            return SerializeBounded(refusal);
        }
    }

    static IEnumerable<string?> AuxiliaryText(
        BrowserSourceComparisonResult result)
    {
        yield return result.Error;
        yield return result.Diagnostic;
        yield return result.Reason;
        if (result.Value is not { } comparison)
            yield break;

        BrowserSourceComparisonRequest request = comparison.Request;
        yield return request.PackageId;
        yield return request.BeforeVersion;
        yield return request.AfterVersion;
        yield return request.Framework;
        yield return request.Assembly;
        foreach (BrowserSourceComparisonEndpointRequest? endpoint in
            new[] { request.Before, request.After })
        {
            yield return endpoint?.TypeIdentity;
            yield return endpoint?.StableSelector;
            yield return endpoint?.CanonicalSignature;
            yield return endpoint?.Fingerprint;
            yield return endpoint?.TypeFullName;
            yield return endpoint?.MemberName;
        }
        yield return comparison.Status;
        yield return comparison.Failure;
        yield return comparison.Diff?.Before.Label;
        yield return comparison.Diff?.After.Label;
        foreach (BrowserSourceComparisonEndpoint endpoint in
            new[] { comparison.Before, comparison.After })
        {
            yield return endpoint.PackageId;
            yield return endpoint.Version;
            yield return endpoint.Framework;
            yield return endpoint.Assembly;
            yield return endpoint.AssetPath;
            yield return endpoint.ModuleVersionId;
            yield return endpoint.AssemblyIdentity;
            yield return endpoint.MemberIdentity;
            yield return endpoint.State;
            yield return endpoint.Detail;
            yield return endpoint.BrowseUrl;
            yield return endpoint.RepositoryUrl;
            yield return endpoint.Revision;
        }
    }

    static string SerializeBounded(BrowserSourceComparisonResult result)
    {
        using var stream = new BoundedUtf8Stream(
            BrowserSourceDiffProjection.MaximumEncodedResultBytes);
        JsonSerializer.Serialize(
            stream,
            result,
            BrowserSourceJsonContext.Default.BrowserSourceComparisonResult);
        return Encoding.UTF8.GetString(
            stream.GetBuffer(),
            0,
            checked((int)stream.Length));
    }

    sealed class BoundedUtf8Stream(int maximumBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Admit(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Admit(buffer.Length);
            base.Write(buffer);
        }

        void Admit(int count)
        {
            long attempted = Position + count;
            if (attempted <= maximumBytes)
                return;

            throw new BrowserSourceDiffCapacityException(
                BrowserSourceDiffCapacityDimension.EncodedResultBytes,
                maximumBytes,
                attempted > int.MaxValue ? int.MaxValue : (int)attempted);
        }
    }
}
