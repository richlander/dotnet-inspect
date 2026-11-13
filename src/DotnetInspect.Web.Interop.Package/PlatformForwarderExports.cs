using System.Buffers;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TsJsExport;

namespace DotnetInspect.Web.Interop.Package;

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPlatformForwarderStatus>))]
public enum BrowserPlatformForwarderStatus
{
    [JsonStringEnumMemberName("opened")] Opened,
    [JsonStringEnumMemberName("unavailable")] Unavailable,
    [JsonStringEnumMemberName("stale")] Stale,
    [JsonStringEnumMemberName("ambiguous")] Ambiguous,
    [JsonStringEnumMemberName("refused")] Refused,
    [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("incomplete")] Incomplete,
    [JsonStringEnumMemberName("canceled")] Canceled,
}

public sealed record BrowserPlatformForwarderRow(
    string Id,
    string Name,
    string Namespace,
    string TargetAssembly,
    string Action);

public sealed record BrowserPlatformForwarderView(
    string Id,
    BrowserPackageSurface Surface,
    string Family,
    string Framework,
    string Version,
    string Assembly,
    BrowserPlatformForwarderRow[] Forwarders,
    string? SelectedTypeId);

public sealed record BrowserPlatformForwardingHop(
    string SourceAssembly,
    string TargetAssembly);

public sealed record BrowserPlatformForwarderResult(
    BrowserPlatformForwarderStatus Status,
    string? Message,
    BrowserPlatformForwarderView? View,
    BrowserPlatformForwardingHop[] Hops,
    string? ResolutionKind,
    string? TerminalAssembly,
    string? HouseStatus,
    string? SourceStatus);

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(nameof(OpenPlatformForwarderView), typeof(BrowserPlatformForwarderResult))]
[JsExportJsonOutput(nameof(ActivatePlatformForwarder), typeof(BrowserPlatformForwarderResult))]
public static partial class PackageExports
{
    static readonly Lazy<BrowserPlatformForwarderNavigation> PlatformForwarders =
        new(() => new(
            BrowserPackageWorkspace.NetworkClient,
            BrowserPackageWorkspace.Gallery,
            BrowserPackageWorkspace.PackageSourceAuthorization,
            BrowserPackageWorkspace.PackageOperationTimeout));

    [JSExport]
    public static async Task<string> OpenPlatformForwarderView(
        string framework,
        string version,
        string assembly,
        string pack) =>
        SerializeForwarderResult(
            await PlatformForwarders.Value.OpenAsync(framework, version, assembly, pack));

    [JSExport]
    public static async Task<string> ActivatePlatformForwarder(string action) =>
        SerializeForwarderResult(await PlatformForwarders.Value.ActivateAsync(action));

    [JSExport]
    public static bool ClosePlatformForwarderView(string view) =>
        PlatformForwarders.IsValueCreated && PlatformForwarders.Value.Close(view);

    internal static string SerializeForwarderResult(BrowserPlatformForwarderNavigationResult result)
    {
        // The large compatibility surface must not leave payload-sized staging buffers in the shared pool.
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(
            writer,
            ProjectForwarderResult(result),
            BrowserPackageJsonContext.Default.BrowserPlatformForwarderResult);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static BrowserPlatformForwarderResult ProjectForwarderResult(
        BrowserPlatformForwarderNavigationResult result)
    {
        BrowserPlatformForwarderResolutionProjection<BrowserPlatformForwardingHop> resolution =
            BrowserPlatformForwarderNavigation.ProjectResolution(
                result,
                static (sourceAssembly, targetAssembly) =>
                    new BrowserPlatformForwardingHop(sourceAssembly, targetAssembly));
        return result switch
        {
            BrowserPlatformForwarderNavigationResult.Opened opened =>
                new(
                    BrowserPlatformForwarderStatus.Opened, null, ProjectForwarderView(opened.View),
                    resolution.Hops, resolution.Kind, resolution.TerminalAssembly,
                    null, null),
            BrowserPlatformForwarderNavigationResult.Blocked blocked =>
                new(
                    ForwarderStatus(blocked.Status), blocked.Message, null,
                    resolution.Hops, resolution.Kind, resolution.TerminalAssembly,
                    blocked.Receipt?.SettlementKind.ToString(),
                    blocked.Contribution?.Kind.ToString()),
            _ => throw new InvalidOperationException("Unknown forwarding navigation result."),
        };
    }

    static BrowserPlatformForwarderStatus ForwarderStatus(string status) => status switch
    {
        "unavailable" => BrowserPlatformForwarderStatus.Unavailable,
        "stale" => BrowserPlatformForwarderStatus.Stale,
        "ambiguous" => BrowserPlatformForwarderStatus.Ambiguous,
        "refused" or "rejected" => BrowserPlatformForwarderStatus.Refused,
        "failed" => BrowserPlatformForwarderStatus.Failed,
        "incomplete" => BrowserPlatformForwarderStatus.Incomplete,
        "canceled" => BrowserPlatformForwarderStatus.Canceled,
        _ => throw new InvalidOperationException("Unknown forwarding navigation status."),
    };

    static BrowserPlatformForwarderView ProjectForwarderView(BrowserPlatformForwarderViewInfo view) =>
        new(
            view.Id,
            BrowserPackageWireProjection.Project(view.Surface),
            view.Coordinate.Family,
            view.Coordinate.Framework,
            view.Coordinate.Version,
            view.Coordinate.Assembly
                ?? throw new InvalidOperationException("A forwarding view requires one selected Library."),
            [.. view.Forwarders.Select(row =>
                new BrowserPlatformForwarderRow(
                    $"{view.Coordinate.Assembly}:{row.EscapedDefinitionId}",
                    row.Name,
                    row.Namespace,
                    row.TargetAssembly,
                    row.Action))],
            view.SelectedTypeId);
}

[JsonSerializable(typeof(BrowserPlatformForwarderResult))]
internal sealed partial class BrowserPackageJsonContext;
