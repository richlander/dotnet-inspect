using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed partial class PackageSourceClientTests
{
    [Fact]
    public async Task PayloadTransportFailureRetainsSafeSourceIdentity()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingPayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadTransportFailureOutranksRacingReadCancellation()
    {
        using var readCancellation = new CancellationTokenSource();
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingPayloadStream(readCancellation.Cancel)),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    readCancellation.Token).AsTask());

        Assert.True(readCancellation.IsCancellationRequested);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
    }

    [Fact]
    public async Task PayloadCallerCancellationDoesNotRetainTransportFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingPayloadStream(cancellation.Cancel)),
            });
        using var operation = new NuGetOperationContext(cancellation.Token);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                cancellationToken: cancellation.Token,
                operationContext: operation));
        await using Stream content = payload.Content;

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => content.ReadAsync(
                    new byte[1],
                    cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadCanceledTransportTimeoutRetainsSafeSourceIdentity(
        bool readAsync)
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new CanceledTimeoutPayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error;
        if (readAsync)
        {
            error = await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());
        }
        else
        {
            error = Assert.Throws<PackageSourceStreamException>(
                () => content.ReadByte());
        }

        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.Timeout);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadCanceledTransportTimeoutDuringDisposalRetainsSafeSourceIdentity(
        bool disposeAsync)
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new CanceledTimeoutDisposePayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        PackageSourceStreamException error;
        if (disposeAsync)
        {
            error = await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => payload.Content.DisposeAsync().AsTask());
        }
        else
        {
            error = Assert.Throws<PackageSourceStreamException>(
                payload.Content.Dispose);
        }

        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.True(error.CleanupFailed);
        Assert.Null(error.Timeout);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadDisposalFailureRetainsSafeSourceIdentity()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingDisposePayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        PackageSourceStreamException error =
            Assert.Throws<PackageSourceStreamException>(
                payload.Content.Dispose);

        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.True(error.CleanupFailed);
        Assert.Null(error.Timeout);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadAsyncDisposalFailureRetainsSafeSourceIdentity()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingDisposePayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => payload.Content.DisposeAsync().AsTask());

        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.True(error.CleanupFailed);
        Assert.Null(error.Timeout);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadConcurrentDisposalTranslatesOutstandingRead(
        bool disposeAsync)
    {
        var inner = new DisposalUnblocksPayloadStream();
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(inner),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Stream content = payload.Content;
        Task<int> read = content.ReadAsync(
                new byte[1],
                TestContext.Current.CancellationToken)
            .AsTask();
        await inner.ReadStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        if (disposeAsync)
            await content.DisposeAsync();
        else
            content.Dispose();

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => read);
        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadConcurrentDisposalEofTranslatesOutstandingRead(
        bool disposeAsync)
    {
        var inner = new DisposalReturnsEofPayloadStream();
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(inner),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Stream content = payload.Content;
        Task<int> read = content.ReadAsync(
                new byte[1],
                TestContext.Current.CancellationToken)
            .AsTask();
        await inner.ReadStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        if (disposeAsync)
            await content.DisposeAsync();
        else
            content.Dispose();

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => read);
        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadConcurrentDisposalTranslatesSynchronousEof(
        bool disposeAsync)
    {
        var inner = new DisposalReturnsEofPayloadStream();
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(inner),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Stream content = payload.Content;
        Task<int> read = Task.Run(
            () => content.Read(new byte[1], 0, 1),
            TestContext.Current.CancellationToken);
        await inner.ReadStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        if (disposeAsync)
            await content.DisposeAsync();
        else
            content.Dispose();

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => read);
        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadObjectDisposedFailureRetainsSafeSourceIdentity(
        bool readAsync)
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ObjectDisposedPayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error;
        if (readAsync)
        {
            error = await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());
        }
        else
        {
            error = Assert.Throws<PackageSourceStreamException>(
                () => content.ReadByte());
        }

        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadObjectDisposedFailurePreservesRequestDeadline()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(20),
            OperationTimeout = TimeSpan.FromSeconds(1),
        };
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new LateObjectDisposedPayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadInvalidDataFailureRetainsSafeSourceIdentity(
        bool readAsync)
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new InvalidDataPayloadStream(delay: false)),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error;
        if (readAsync)
        {
            error = await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());
        }
        else
        {
            error = Assert.Throws<PackageSourceStreamException>(
                () => content.ReadByte());
        }

        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadInvalidDataFailurePreservesRequestDeadline()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(20),
            OperationTimeout = TimeSpan.FromSeconds(1),
        };
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new InvalidDataPayloadStream(delay: true)),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadReadAfterDisposalRemainsObjectDisposed()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1]),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        payload.Content.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => payload.Content.ReadByte());
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => payload.Content.ReadAsync(
                new byte[1],
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task PayloadTimeoutRetainsSourceAndConfiguredDuration()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(40),
            OperationTimeout = TimeSpan.FromSeconds(1),
        };
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new StallingPayloadStream()),
            });
        using var operation = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: operation));
        await using Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Same(runtime.Source, error.ResultSource);
        Assert.Equal(
            runtime.Source.TransportKind,
            error.ResultSource.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task PayloadTimeoutRetainsCleanupFailureWithoutInnerException()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(40),
            OperationTimeout = TimeSpan.FromSeconds(1),
        };
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingDisposeStallingPayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            error.Timeout);
        Assert.True(error.CleanupFailed);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
        _ = await Assert.ThrowsAsync<PackageSourceStreamException>(
            () => content.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task DisposingSharedContextCancelsOutstandingPayloadRead()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(5),
            OperationTimeout = TimeSpan.FromSeconds(10),
        };
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new StallingPayloadStream()),
            });
        using var operation = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: operation));
        await using Stream content = payload.Content;

        Task<int> read = content.ReadAsync(
                new byte[1],
                TestContext.Current.CancellationToken)
            .AsTask();
        operation.Dispose();
        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => read);

        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                options.OperationTimeout),
            error.Timeout);
    }
}
