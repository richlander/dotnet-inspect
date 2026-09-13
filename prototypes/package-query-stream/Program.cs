using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;

return await PackageQueryStream.RunAsync(args);

static class PackageQueryStream
{
    const int MaximumCount = 1_000;
    const int MaximumPageSize = 100;
    const int MaximumSkip = 3_000;
    const int InitialBufferSize = 64 * 1024;
    const int MaximumBufferedTokenSize = 1024 * 1024;
    const string SearchEndpoint = "https://azuresearch-usnc.nuget.org/query";
    static readonly TextWriter StandardError =
        new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };

    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryParseArguments(args, out string prefix, out int count))
            return 1;

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += Cancel;
        try
        {
            using var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "dotnet-inspect-package-query-stream-prototype/1.0");

            var output = new StreamingOutput(prefix, count);
            int skip = 0;
            int pageSize = Math.Min(count, MaximumPageSize);
            while (!output.IsComplete && skip <= MaximumSkip)
            {
                int take = Math.Min(pageSize, count - output.Count);
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    CreateSearchUri(prefix, skip, take));
                using HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellation.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using Stream stream = await response.Content
                    .ReadAsStreamAsync(cancellation.Token)
                    .ConfigureAwait(false);
                var parser = new SearchPageParser(output);
                await ReadPageAsync(stream, parser, cancellation.Token)
                    .ConfigureAwait(false);

                if (output.IsComplete)
                    return 0;
                if (!parser.PageComplete)
                    throw new InvalidDataException(
                        "The NuGet search response ended before its data array completed.");
                if (parser.RawRows == 0 || parser.RawRows < take)
                    break;

                skip += parser.RawRows;
            }

            StandardError.WriteLine(
                $"Only {output.Count.ToString(CultureInfo.InvariantCulture)} "
                + $"package IDs beginning with \"{prefix}\" were found.");
            return 1;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 130;
        }
        catch (OperationCanceledException)
        {
            StandardError.WriteLine("Package query timed out.");
            return 1;
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or IOException
                or InvalidDataException
                or JsonException)
        {
            StandardError.WriteLine($"Package query failed: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }

        void Cancel(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }
    }

    static async Task ReadPageAsync(
        Stream stream,
        SearchPageParser parser,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[InitialBufferSize];
        int buffered = 0;
        while (!parser.PageComplete && !parser.OutputComplete)
        {
            if (buffered == buffer.Length)
            {
                if (buffer.Length >= MaximumBufferedTokenSize)
                {
                    throw new InvalidDataException(
                        "The NuGet search response contains an oversized JSON token.");
                }
                Array.Resize(
                    ref buffer,
                    Math.Min(buffer.Length * 2, MaximumBufferedTokenSize));
            }

            int read = await stream.ReadAsync(
                buffer.AsMemory(buffered),
                cancellationToken).ConfigureAwait(false);
            int available = buffered + read;
            bool isFinalBlock = read == 0;
            int consumed = parser.Parse(
                buffer.AsSpan(0, available),
                isFinalBlock);
            buffered = available - consumed;
            if (buffered > 0)
                buffer.AsSpan(consumed, buffered).CopyTo(buffer);

            if (isFinalBlock)
                break;
        }
    }

    static Uri CreateSearchUri(string prefix, int skip, int take) =>
        new(
            $"{SearchEndpoint}?q={Uri.EscapeDataString(prefix)}"
            + $"&skip={skip.ToString(CultureInfo.InvariantCulture)}"
            + $"&take={take.ToString(CultureInfo.InvariantCulture)}"
            + "&prerelease=false&semVerLevel=2.0.0");

    static bool TryParseArguments(
        string[] args,
        out string prefix,
        out int count)
    {
        prefix = "";
        count = 0;
        if (args.Length == 1
            && args[0] is "-h" or "--help")
        {
            WriteUsage();
            return false;
        }
        if (args.Length != 3
            || args[1] != "-n"
            || !int.TryParse(
                args[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out count)
            || count is < 1 or > MaximumCount)
        {
            WriteUsage();
            return false;
        }

        prefix = args[0].EndsWith('*')
            ? args[0][..^1]
            : args[0];
        if (prefix.Length is < 1 or > 100
            || prefix.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '-' and not '_'))
        {
            StandardError.WriteLine(
                "PREFIX must contain 1-100 package-ID prefix characters.");
            return false;
        }

        return true;
    }

    static void WriteUsage() =>
        StandardError.WriteLine(
            "Usage: package-query-stream PREFIX -n COUNT");
}

sealed class StreamingOutput(string prefix, int targetCount)
{
    readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    internal int Count { get; private set; }
    internal bool IsComplete => Count == targetCount;

    internal void TryWrite(string packageId, string version)
    {
        if (IsComplete
            || !packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !_seen.Add(packageId))
        {
            return;
        }
        if (!IsSafePackageId(packageId) || !IsSafeVersion(version))
        {
            throw new InvalidDataException(
                "The NuGet search response contained an invalid package identity.");
        }

        Console.Write(packageId);
        Console.Write('\t');
        Console.WriteLine(version);
        Console.Out.Flush();
        Count++;
    }

    static bool IsSafePackageId(string value) =>
        value.Length is >= 1 and <= 100
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '.' or '-' or '_');

    static bool IsSafeVersion(string value) =>
        value.Length is >= 1 and <= 256
        && value.All(character => !char.IsControl(character) && character != '\t');
}

sealed class SearchPageParser(StreamingOutput output)
{
    JsonReaderState _readerState = new(new JsonReaderOptions
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
    });
    bool _expectingDataArray;
    bool _insideDataArray;
    int _dataArrayDepth;
    int _itemDepth = -1;
    RequestedProperty _requestedProperty;
    string? _packageId;
    string? _version;
    bool _rowEvaluated;

    internal int RawRows { get; private set; }
    internal bool PageComplete { get; private set; }
    internal bool OutputComplete => output.IsComplete;

    internal int Parse(ReadOnlySpan<byte> json, bool isFinalBlock)
    {
        var reader = new Utf8JsonReader(json, isFinalBlock, _readerState);
        while (reader.Read())
        {
            if (!_insideDataArray)
            {
                ReadEnvelopeToken(ref reader);
                continue;
            }

            if (_itemDepth < 0)
            {
                if (reader.TokenType == JsonTokenType.EndArray
                    && reader.CurrentDepth == _dataArrayDepth)
                {
                    PageComplete = true;
                    return checked((int)reader.BytesConsumed);
                }
                if (reader.TokenType != JsonTokenType.StartObject
                    || reader.CurrentDepth != _dataArrayDepth + 1)
                {
                    throw new InvalidDataException(
                        "The NuGet search data array contains a non-object item.");
                }

                _itemDepth = reader.CurrentDepth;
                _packageId = null;
                _version = null;
                _rowEvaluated = false;
                continue;
            }

            ReadItemToken(ref reader);
            if (output.IsComplete)
                return checked((int)reader.BytesConsumed);
        }

        _readerState = reader.CurrentState;
        return checked((int)reader.BytesConsumed);
    }

    void ReadEnvelopeToken(ref Utf8JsonReader reader)
    {
        if (_expectingDataArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new InvalidDataException(
                    "The NuGet search response data property is not an array.");
            }
            _expectingDataArray = false;
            _insideDataArray = true;
            _dataArrayDepth = reader.CurrentDepth;
            return;
        }

        if (reader.TokenType == JsonTokenType.PropertyName
            && reader.CurrentDepth == 1
            && reader.ValueTextEquals("data"u8))
        {
            _expectingDataArray = true;
        }
    }

    void ReadItemToken(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.PropertyName
            && reader.CurrentDepth == _itemDepth + 1)
        {
            _requestedProperty = reader.ValueTextEquals("id"u8)
                ? RequestedProperty.PackageId
                : reader.ValueTextEquals("version"u8)
                    ? RequestedProperty.Version
                    : RequestedProperty.None;
            return;
        }

        if (_requestedProperty != RequestedProperty.None
            && reader.CurrentDepth == _itemDepth + 1)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new InvalidDataException(
                    "The NuGet search response contains a non-string package identity.");
            }

            string value = reader.GetString()
                ?? throw new InvalidDataException(
                    "The NuGet search response contains a null package identity.");
            if (_requestedProperty == RequestedProperty.PackageId)
                _packageId = value;
            else
                _version = value;
            _requestedProperty = RequestedProperty.None;
            TryWriteRow();
            return;
        }

        if (reader.TokenType == JsonTokenType.EndObject
            && reader.CurrentDepth == _itemDepth)
        {
            if (_packageId is null || _version is null)
            {
                throw new InvalidDataException(
                    "The NuGet search response contains an incomplete package identity.");
            }

            RawRows++;
            _itemDepth = -1;
            _requestedProperty = RequestedProperty.None;
        }
    }

    void TryWriteRow()
    {
        if (_rowEvaluated || _packageId is null || _version is null)
            return;

        _rowEvaluated = true;
        output.TryWrite(_packageId, _version);
    }

    enum RequestedProperty
    {
        None,
        PackageId,
        Version,
    }
}
