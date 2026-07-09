using System.IO.Pipelines;

namespace AppCap.Protocol;

// A bidirectional stream that reads from one underlying stream and writes to another.
// It lets the JSON-RPC codec — which is written against a single duplex Stream — run
// over a pair of one-way channels, which is how the in-proc transport is built.
internal sealed class DuplexStream : Stream
{
    private readonly Stream _readSide;
    private readonly Stream _writeSide;

    public DuplexStream(Stream readSide, Stream writeSide)
    {
        _readSide = readSide;
        _writeSide = writeSide;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _writeSide.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _writeSide.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => _readSide.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _readSide.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _readSide.ReadAsync(buffer, offset, count, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => _writeSide.Write(buffer, offset, count);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _writeSide.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _writeSide.WriteAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writeSide.Dispose();
            _readSide.Dispose();
        }

        base.Dispose(disposing);
    }
}

// Creates a connected pair of in-memory duplex streams, so two protocol peers can
// speak the same JSON-RPC protocol without a named pipe. This is how a "server side"
// of a protocol runs in-proc: the exact same codec and dispatch (WorkerServer or
// TargetServer) used over a pipe are used over these streams, with no OS resources.
internal static class InProcDuplexTransport
{
    // Returns two ends of a connected duplex channel. Bytes written to one end are
    // read from the other. Disposing an end completes its outbound direction, which
    // the peer observes as end-of-stream.
    public static (Stream Client, Stream Server) CreatePair()
    {
        Pipe clientToServer = new();
        Pipe serverToClient = new();

        Stream client = new DuplexStream(serverToClient.Reader.AsStream(), clientToServer.Writer.AsStream());
        Stream server = new DuplexStream(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        return (client, server);
    }
}
