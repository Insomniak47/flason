using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Ttd2089.Flason;

/// <summary>
/// Reads <see cref="JsonToken"/> instances from a <see cref="Stream"/> containing JSON data.
/// </summary>
public sealed class JsonTokenReader
{
    private readonly ChannelWriter<JsonToken> _channel;
    private readonly Stream _stream;

    private byte[] _buffer;

    private ReadOnlyMemory<byte> _bufferView;
    private int _bufferViewStartOffset;

    private JsonReaderState _readerState;
    
    public JsonTokenReader(ChannelWriter<JsonToken> channel, Stream stream, JsonTokenReaderOptions options)
    {
        if (options.InitialBufferSize == 0)
        {
            throw new ArgumentException(
                $"{nameof(options)}.{nameof(options.InitialBufferSize)} must be greater than zero.",
                nameof(options));
        }

        _channel = channel;
        _stream = stream;

        _buffer = new byte[options.InitialBufferSize];

        _readerState = new JsonReaderState(new JsonReaderOptions()
        {
            AllowTrailingCommas = options.AllowTrailingCommas,
            CommentHandling = options.CommentHandling,
            MaxDepth = options.MaxDepth,
        });
    }

    public void Run()
    {
        while (true)
        {
            // Check the buffer first to ensure we get all the JSON tokens from the last stream
            // read before reading again or shifting the buffer data forward.
            if (ReadNextTokenFromBuffer() is JsonToken token)
            {
                _channel.WaitToWriteAsync().AsTask().Wait();
                _channel.TryWrite(token);
                continue;
            }

            if (!ReadStreamIntoBuffer())
            {
                _channel.Complete();
                return;
            }
        }
    }

    private JsonToken? ReadNextTokenFromBuffer()
    {
        // Reading an empty buffer throws an exception because it doesn't contain valid JSON. We'll
        // try again after we load more data from the stream.
        if (_bufferView.Length == 0)
        {
            return null;
        }


        var reader = new Utf8JsonReader(_bufferView.Span, isFinalBlock: false, _readerState); 
        var read = reader.Read();

        _bufferView = _bufferView[(int)reader.BytesConsumed..];
        _bufferViewStartOffset += (int)reader.BytesConsumed;

        _readerState = reader.CurrentState;

        return read ? GetTokenFromReader(reader) : null;
    }

    private static JsonToken GetTokenFromReader(Utf8JsonReader reader) => new(
        type: reader.TokenType,
        value: reader.ValueSpan,
        depth: reader.CurrentDepth,
        valueIsEscaped: reader.ValueIsEscaped);

    private bool ReadStreamIntoBuffer()
    {
        if (BufferIsFull())
        {
            if (BufferContainsAlreadyConsumedData())
            {
                ShiftUnconsumedDataToFrontOfBuffer();
            }
            else
            {
                Array.Resize(ref _buffer, _buffer.Length * 2);
            }
        }

        var read = _stream.Read(_buffer.AsSpan(_bufferView.Length..));
        if (read == 0)
        {
            return false;
        }

        var newBufferViewLength = _bufferView.Length + read;
        _bufferView = _buffer.AsMemory()[..newBufferViewLength];

        return true;
    }

    private bool BufferIsFull() => _bufferViewStartOffset + _bufferView.Length == _buffer.Length;

    private bool BufferContainsAlreadyConsumedData() => _bufferViewStartOffset > 0;

    private void ShiftUnconsumedDataToFrontOfBuffer()
    {
        _bufferView.CopyTo(_buffer);
        _bufferView = _buffer.AsMemory()[.._bufferView.Length];
        _bufferViewStartOffset = 0;
    }
}
