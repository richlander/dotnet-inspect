#nullable enable
using System.Threading.Tasks;

namespace ILInspector.Decompiler.Fixtures.ClassicAsync;

public sealed class ContinuationFixtures
{
    object? _first;
    object? _second;
    System.Memory<byte> _memory;
    public System.Memory<byte> Memory => _memory;
    public object? First => _first;
    public object? Second => _second;
    void Cleanup() { _first = new object(); _second = new object(); }
    public async Task AwaitReceiverCleanup()
    {
        await Work();
        Cleanup();
        _first = null;
        _second = null;
    }
    public async Task GuardedReceiverCleanup(bool enabled)
    {
        if (!enabled) return;
        await Work();
        Cleanup();
        _first = null;
        _second = null;
    }
    public async Task GuardedAwait(bool enabled)
    {
        if (!enabled) return;
        await Work();
    }
    public async Task InvertedGuardedAwait(bool disabled)
    {
        if (disabled) return;
        await Work();
    }
    public async Task CompoundGuardedAwait(bool first, bool second)
    {
        if (!first && !second) return;
        await Work();
    }
    public async Task ReferenceGuardedAwait()
    {
        if (_first is null && _second is null) return;
        await Work();
    }
    public async ValueTask CompoundReceiverCleanup()
    {
        if (_first is null && _second is null) return;
        await WorkToken(default).ConfigureAwait(false);
        Cleanup();
        _first = null;
        _second = null;
    }
    public Task WorkToken(System.Threading.CancellationToken token) => Task.CompletedTask;
    public async Task AwaitFieldReset()
    {
        await Work();
        _memory = default;
        Cleanup();
    }
    public async ValueTask GuardedAwaitFieldReset(bool enabled)
    {
        if (!enabled) return;
        await Work();
        _memory = default;
        Cleanup();
    }
    public Task Work() => Task.CompletedTask;
}

public sealed class FlushFixtures
{
    System.IO.Stream? _stream;
    System.Buffers.IBufferWriter<byte>? _output;
    readonly System.Buffers.ArrayBufferWriter<byte> _buffer = new();
    System.Memory<byte> _memory;
    public int Pending { get; private set; }
    public long Committed { get; private set; }
    public System.Memory<byte> Memory => _memory;
    public FlushFixtures(System.IO.Stream? stream, System.Buffers.IBufferWriter<byte>? output)
    {
        _stream = stream;
        _output = output;
    }
    public void Check() { if (_stream is null && _output is null) throw new System.ObjectDisposedException("writer"); }
    public async Task FlushConditional(System.Threading.CancellationToken token)
    {
        Check();
        _memory = default;
        if (_stream is not null)
        {
            if (Pending != 0)
            {
                _buffer.Advance(Pending);
                Pending = 0;
                await _stream.WriteAsync(_buffer.WrittenMemory, token).ConfigureAwait(false);
                Committed += _buffer.WrittenCount;
                _buffer.Clear();
            }
            await _stream.FlushAsync(token).ConfigureAwait(false);
        }
        else
        {
            if (Pending != 0)
            {
                _output!.Advance(Pending);
                Committed += Pending;
                Pending = 0;
            }
        }
    }
    public async ValueTask FlushConditionalValueTask(System.Threading.CancellationToken token)
    {
        Check();
        _memory = default;
        if (_stream is not null)
        {
            if (Pending != 0)
            {
                _buffer.Advance(Pending);
                Pending = 0;
                await _stream.WriteAsync(_buffer.WrittenMemory, token).ConfigureAwait(false);
                Committed += _buffer.WrittenCount;
                _buffer.Clear();
            }
            await _stream.FlushAsync(token).ConfigureAwait(false);
        }
        else
        {
            if (Pending != 0)
            {
                _output!.Advance(Pending);
                Committed += Pending;
                Pending = 0;
            }
        }
    }
}
