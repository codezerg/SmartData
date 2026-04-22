namespace SmartData.Core.BinarySerialization;

/// <summary>
/// A thin stream wrapper with a 1-byte lookahead buffer that enables
/// BinarySerializationReader's PeekType()/IsEnd() to work on non-seekable
/// streams (e.g. zip entry streams). Only the Seek(-1, Current) pattern
/// used by the reader is supported.
/// </summary>
public sealed class ReadAheadStream : Stream
{
    private readonly Stream _inner;
    private int _pushback = -1;
    private int _lastByte = -1;
    private long _position;

    public ReadAheadStream(Stream inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _inner.CanSeek ? _inner.Length : throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int ReadByte()
    {
        int b;
        if (_pushback >= 0)
        {
            b = _pushback;
            _pushback = -1;
        }
        else
        {
            b = _inner.ReadByte();
        }

        if (b >= 0)
        {
            _lastByte = b;
            _position++;
        }
        return b;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0) return 0;

        var totalRead = 0;

        if (_pushback >= 0)
        {
            buffer[offset] = (byte)_pushback;
            _lastByte = _pushback;
            _pushback = -1;
            _position++;
            totalRead = 1;
            if (count == 1) return 1;
            offset++;
            count--;
        }

        var read = _inner.Read(buffer, offset, count);
        if (read > 0)
        {
            _lastByte = buffer[offset + read - 1];
            _position += read;
        }
        totalRead += read;
        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        // Support the single pattern BinarySerializationReader uses: Seek(-1, Current)
        if (origin == SeekOrigin.Current && offset == -1)
        {
            if (_pushback >= 0)
                throw new InvalidOperationException("Cannot push back more than one byte.");
            if (_lastByte < 0)
                throw new InvalidOperationException("No byte to push back.");

            _pushback = _lastByte;
            _lastByte = -1;
            _position--;
            return _position;
        }

        if (_inner.CanSeek)
        {
            _pushback = -1;
            _lastByte = -1;
            _position = _inner.Seek(offset, origin);
            return _position;
        }

        throw new NotSupportedException("Only Seek(-1, Current) is supported on non-seekable streams.");
    }

    public override void Flush() => _inner.Flush();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
