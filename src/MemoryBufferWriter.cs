using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace PostgreSignalR;

internal sealed class MemoryBufferWriter(int minimumSegmentSize = 4096) : IBufferWriter<byte>
{
    [ThreadStatic]
    private static MemoryBufferWriter? _cachedInstance;

#if DEBUG
    private bool _inUse;
#endif

    private int _bytesWritten;

    private List<CompletedBuffer>? _completedSegments;
    private byte[]? _currentSegment;
    private int _position;

    public static MemoryBufferWriter Get()
    {
        var writer = _cachedInstance;
        if (writer == null)
        {
            writer = new MemoryBufferWriter();
        }
        else
        {
            // Taken off the thread static
            _cachedInstance = null;
        }
#if DEBUG
        if (writer._inUse)
        {
            throw new InvalidOperationException("The reader wasn't returned!");
        }

        writer._inUse = true;
#endif

        return writer;
    }

    public static void Return(MemoryBufferWriter writer)
    {
        _cachedInstance = writer;
#if DEBUG
        writer._inUse = false;
#endif
        writer.Reset();
    }

    public void Reset()
    {
        if (_completedSegments != null)
        {
            for (var i = 0; i < _completedSegments.Count; i++)
            {
                _completedSegments[i].Return();
            }

            _completedSegments.Clear();
        }

        if (_currentSegment != null)
        {
            ArrayPool<byte>.Shared.Return(_currentSegment);
            _currentSegment = null;
        }

        _bytesWritten = 0;
        _position = 0;
    }

    public void Advance(int count)
    {
        _bytesWritten += count;
        _position += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);

        return _currentSegment.AsMemory(_position, _currentSegment.Length - _position);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);

        return _currentSegment.AsSpan(_position, _currentSegment.Length - _position);
    }

    public void CopyTo(IBufferWriter<byte> destination)
    {
        if (_completedSegments != null)
        {
            // Copy completed segments
            var count = _completedSegments.Count;
            for (var i = 0; i < count; i++)
            {
                destination.Write(_completedSegments[i].Span);
            }
        }

        destination.Write(_currentSegment.AsSpan(0, _position));
    }

    [MemberNotNull(nameof(_currentSegment))]
    private void EnsureCapacity(int sizeHint)
    {
        // This does the Right Thing. It only subtracts _position from the current segment length if it's non-null.
        // If _currentSegment is null, it returns 0.
        var remainingSize = _currentSegment?.Length - _position ?? 0;

        // If the sizeHint is 0, any capacity will do
        // Otherwise, the buffer must have enough space for the entire size hint, or we need to add a segment.
        if ((sizeHint == 0 && remainingSize > 0) || (sizeHint > 0 && remainingSize >= sizeHint))
        {
            // We have capacity in the current segment
#pragma warning disable CS8774 // Member must have a non-null value when exiting.
            return;
#pragma warning restore CS8774 // Member must have a non-null value when exiting.
        }

        AddSegment(sizeHint);
    }

    [MemberNotNull(nameof(_currentSegment))]
    private void AddSegment(int sizeHint = 0)
    {
        if (_currentSegment != null)
        {
            // We're adding a segment to the list
            _completedSegments ??= [];

            // Position might be less than the segment length if there wasn't enough space to satisfy the sizeHint when
            // GetMemory was called. In that case we'll take the current segment and call it "completed", but need to
            // ignore any empty space in it.
            _completedSegments.Add(new CompletedBuffer(_currentSegment, _position));
        }

        // Get a new buffer using the minimum segment size, unless the size hint is larger than a single segment.
        _currentSegment = ArrayPool<byte>.Shared.Rent(Math.Max(minimumSegmentSize, sizeHint));
        _position = 0;
    }

    public byte[] ToArray()
    {
        if (_currentSegment == null)
        {
            return [];
        }

        var result = new byte[_bytesWritten];

        var totalWritten = 0;

        if (_completedSegments != null)
        {
            // Copy full segments
            var count = _completedSegments.Count;
            for (var i = 0; i < count; i++)
            {
                var segment = _completedSegments[i];
                segment.Span.CopyTo(result.AsSpan(totalWritten));
                totalWritten += segment.Span.Length;
            }
        }

        // Copy current incomplete segment
        _currentSegment.AsSpan(0, _position).CopyTo(result.AsSpan(totalWritten));

        return result;
    }

    public void CopyTo(Span<byte> span)
    {
        Debug.Assert(span.Length >= _bytesWritten);

        if (_currentSegment == null)
        {
            return;
        }

        var totalWritten = 0;

        if (_completedSegments != null)
        {
            // Copy full segments
            var count = _completedSegments.Count;
            for (var i = 0; i < count; i++)
            {
                var segment = _completedSegments[i];
                segment.Span.CopyTo(span.Slice(totalWritten));
                totalWritten += segment.Span.Length;
            }
        }

        // Copy current incomplete segment
        _currentSegment.AsSpan(0, _position).CopyTo(span[totalWritten..]);

        Debug.Assert(_bytesWritten == totalWritten + _position);
    }

    public WrittenBuffers DetachAndReset()
    {
        _completedSegments ??= [];

        if (_currentSegment is not null)
        {
            _completedSegments.Add(new CompletedBuffer(_currentSegment, _position));
        }

        var written = new WrittenBuffers(_completedSegments, _bytesWritten);

        _currentSegment = null;
        _completedSegments = null;
        _bytesWritten = 0;
        _position = 0;

        return written;
    }

    /// <summary>
    /// Holds the written segments from a MemoryBufferWriter and is no longer attached to a MemoryBufferWriter.
    /// You are now responsible for calling Dispose on this type to return the memory to the pool.
    /// </summary>
    internal readonly ref struct WrittenBuffers(List<CompletedBuffer> segments, int bytesWritten)
    {
        public List<CompletedBuffer> Segments => segments;
        public int ByteLength => bytesWritten;

        public void Dispose()
        {
            for (var i = 0; i < Segments.Count; i++)
            {
                Segments[i].Return();
            }
            
            Segments.Clear();
        }
    }

    /// <summary>
    /// Holds a byte[] from the pool and a size value. Basically a Memory but guaranteed to be backed by an ArrayPool byte[], so that we know we can return it.
    /// </summary>
    internal readonly struct CompletedBuffer(byte[] buffer, int length)
    {
        public byte[] Buffer { get; } = buffer;
        public int Length { get; } = length;

        public ReadOnlySpan<byte> Span =>
            Buffer.AsSpan(0, Length);

        public void Return() =>
            ArrayPool<byte>.Shared.Return(Buffer);
    }
}
