using System.Text.Json;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.TestHost;

namespace ILInspector.Decompiler.Tests.Gating;

internal sealed class MtpGateReceiptConsumer : IDataConsumer
{
    internal const string ReceiptPathEnvironmentVariable =
        "DOTNET_INSPECT_DECOMPILER_TEST_RECEIPT";

    private readonly string _receiptPath;

    internal MtpGateReceiptConsumer(string receiptPath)
    {
        _receiptPath = receiptPath;
    }

    public string Uid => "dotnet-inspect-decompiler-test-receipt";

    public string Version => "1.0.0";

    public string DisplayName => "dotnet-inspect decompiler test receipt";

    public string Description =>
        "Records MTP execution identities for the decompiler gate.";

    public Type[] DataTypesConsumed { get; } = [typeof(TestNodeUpdateMessage)];

    public Task<bool> IsEnabledAsync() => Task.FromResult(true);

    public Task ConsumeAsync(
        IDataProducer dataProducer,
        IData value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (value is not TestNodeUpdateMessage update
            || State(update.TestNode.Properties) is not string state)
        {
            return Task.CompletedTask;
        }

        MtpGateReceiptWriter.Write(_receiptPath, update.TestNode, state);

        return Task.CompletedTask;
    }

    private static string? State(PropertyBag properties)
    {
        if (properties.Any<DiscoveredTestNodeStateProperty>())
            return "discovered";
        if (properties.Any<InProgressTestNodeStateProperty>())
            return "started";
        if (properties.Any<PassedTestNodeStateProperty>())
            return "passed";
        if (properties.Any<FailedTestNodeStateProperty>())
            return "failed";
        if (properties.Any<SkippedTestNodeStateProperty>())
            return "skipped";
        if (properties.Any<ErrorTestNodeStateProperty>())
            return "error";
        if (properties.Any<TimeoutTestNodeStateProperty>())
            return "timeout";

        return null;
    }
}

internal static class MtpGateReceiptWriter
{
    private const string Schema = "dotnet-inspect.decompiler-test-receipt.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly object WriteLock = new();

    internal static void Initialize(string receiptPath)
    {
        string? directory = Path.GetDirectoryName(receiptPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(receiptPath, string.Empty);
    }

    internal static void Write(string receiptPath, TestNode node, string state)
    {
        TestMethodIdentifierProperty? method =
            node.Properties.SingleOrDefault<TestMethodIdentifierProperty>();
        string? className = method is null
            ? null
            : string.IsNullOrEmpty(method.Namespace)
                ? method.TypeName
                : $"{method.Namespace}.{method.TypeName}";
        string? methodName = method?.MethodName;
        string? methodSignature = methodName;
        if (method is not null && method.ParameterTypeFullNames.Length > 0)
        {
            methodSignature +=
                $"({string.Join(',', method.ParameterTypeFullNames)})";
        }

        Write(
            receiptPath,
            node.Uid.Value,
            node.DisplayName,
            className,
            methodName,
            methodSignature,
            state);
    }

    internal static void Write(
        string receiptPath,
        string uid,
        string displayName,
        string? className,
        string? methodName,
        string? methodSignature,
        string state)
    {
        var entry = new ReceiptEntry(
            Schema,
            uid,
            displayName,
            className,
            methodName,
            methodSignature,
            state);
        string line = JsonSerializer.Serialize(entry, SerializerOptions)
            + Environment.NewLine;

        lock (WriteLock)
        {
            File.AppendAllText(receiptPath, line);
        }
    }

    private sealed record ReceiptEntry(
        string Schema,
        string Uid,
        string DisplayName,
        string? Class,
        string? Method,
        string? MethodSignature,
        string State);
}
