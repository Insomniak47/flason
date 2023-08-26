using System.Buffers;
using System.IO.Pipelines;
using System.Text.Json;

namespace Ttd2089.Flason;

/// <summary>
/// Reads <see cref="JsonToken"/> instances from a <see cref="Stream"/> containing JSON data.
/// </summary>
public sealed class JsonTokenReader
{
    private readonly PipeReader _pipe;
    private JsonReaderState _readerState;

    public JsonTokenReader(Stream stream, JsonTokenReaderOptions options)
    {
        if (options.InitialBufferSize == 0)
        {
            throw new ArgumentException(
                $"{nameof(options)}.{nameof(options.InitialBufferSize)} must be greater than zero.",
                nameof(options));
        }

        _pipe = PipeReader.Create(stream, new());

        _readerState = new JsonReaderState(new JsonReaderOptions()
        {
            AllowTrailingCommas = options.AllowTrailingCommas,
            CommentHandling = options.CommentHandling,
            MaxDepth = options.MaxDepth,
        });
    }

    public async ValueTask<JsonToken?> NextAsync(CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            _pipe.TryRead(out var read);
            if (read.IsCanceled || read.IsCompleted)
            {
                return null;
            }

            if (ReadNextTokenYouShit(read.Buffer) is (long sz, JsonToken tokeyboi))
            {
                // This is saying we've consumed up to the end read size we got from reader
                _pipe.AdvanceTo(read.Buffer.GetPosition(sz));
                return tokeyboi;
            }

            // This advances the "consumed" and the "observed" locations
            // by saying we've *seen* the whole buffer but only consumed to the beginning
            // of the current span. This will get it to load up to the buffer max size again.
            _pipe.AdvanceTo(read.Buffer.Start, read.Buffer.End);

            _ = await _pipe.ReadAsync(token);
        }

        return null;
    }

    public ValueTuple<long, JsonToken>? ReadNextTokenYouShit(ReadOnlySequence<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, isFinalBlock: false, _readerState);
        var read = reader.Read();
        _readerState = reader.CurrentState;

        return read ? (reader.BytesConsumed, GetTokenFromReader(reader)) : null;
    }

    private static JsonToken GetTokenFromReader(Utf8JsonReader reader) => new(
        type: reader.TokenType,
        value: reader.ValueSpan,
        depth: reader.CurrentDepth,
        valueIsEscaped: reader.ValueIsEscaped);
}