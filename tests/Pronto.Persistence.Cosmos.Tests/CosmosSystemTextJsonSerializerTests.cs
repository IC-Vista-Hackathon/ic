using Xunit;

namespace Pronto.Persistence.Cosmos.Tests;

public sealed class CosmosSystemTextJsonSerializerTests
{
    private sealed record Doc(string BillerId, int AmountCents);

    private static CosmosSystemTextJsonSerializer Serializer() => new(CosmosJson.CreateOptions());

    [Fact]
    public void ToStreamThenFromStreamRoundTrips()
    {
        var serializer = Serializer();
        var doc = new Doc("b-1", 8420);

        using var stream = serializer.ToStream(doc);
        var roundTripped = serializer.FromStream<Doc>(stream);

        Assert.Equal(doc, roundTripped);
    }

    [Fact]
    public void ToStreamWritesSnakeCase()
    {
        using var stream = Serializer().ToStream(new Doc("b-1", 1));
        using var reader = new StreamReader(stream);

        Assert.Contains("\"biller_id\"", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void FromStreamPassesRawStreamsThroughUndisposed()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        var result = Serializer().FromStream<Stream>(stream);

        Assert.Same(stream, result);
        Assert.True(stream.CanRead); // not disposed
    }

    [Fact]
    public void FromStreamReturnsDefaultForEmptyStream()
    {
        var result = Serializer().FromStream<Doc>(new MemoryStream());

        Assert.Null(result);
    }

    [Fact]
    public void FromStreamReturnsDefaultForEmptyNonSeekableStream()
    {
        using var stream = new NonSeekableStream([]);

        var result = Serializer().FromStream<Doc>(stream);

        Assert.Null(result);
    }

    [Fact]
    public void FromStreamDeserializesNonSeekableContent()
    {
        var serializer = Serializer();
        byte[] payload;
        using (var buffer = (MemoryStream)serializer.ToStream(new Doc("b-1", 8420)))
        {
            payload = buffer.ToArray();
        }

        using var stream = new NonSeekableStream(payload);
        var result = serializer.FromStream<Doc>(stream);

        Assert.Equal(new Doc("b-1", 8420), result);
    }

    [Fact]
    public void FromStreamDisposesTheSourceStream()
    {
        var serializer = Serializer();
        var stream = serializer.ToStream(new Doc("b-1", 1));

        _ = serializer.FromStream<Doc>(stream);

        Assert.False(stream.CanRead);
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
