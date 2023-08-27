using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Rfc6901JsonPointer = Ttd2089.Flason.Rfc6901.JsonPointer;
using Rfc6901ReferenceToken = Ttd2089.Flason.Rfc6901.ReferenceToken;
using Rfc6901ReferenceTokenType = Ttd2089.Flason.Rfc6901.ReferenceTokenType;

namespace Ttd2089.Flason.Cli;

class Program
{
    private static readonly Stream _nullStream = Stream.Null;
    static async Task Main(string[] args)
    {
        var stream = args.Length == 0 ||  args[0] == "-" 
            ? Console.OpenStandardInput() 
            : File.OpenRead(args[0]);


        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        var channel = Channel.CreateBounded<JsonToken>(new BoundedChannelOptions(10000)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        //var f = File.ReadAllBytes(args[0]);
        //var reader = new Utf8JsonReader(f, isFinalBlock: true, state: default);
        //while (reader.Read())
        //{
        //    Console.WriteLine(GetTokenFromReader(reader));
        //}


        var jsonTokenReader = new JsonTokenReader(channel.Writer, stream, new()
        {
            InitialBufferSize = 8,
            CommentHandling = JsonCommentHandling.Skip,
        });

        var thread = new Thread(() =>
        {
            jsonTokenReader.Run();
        });

        Console.WriteLine("starting");
        var sw = Stopwatch.StartNew();
        thread.Start();

        var firstToken = await channel.Reader.ReadAsync();
        await WriteFlason(channel.Reader, new(), firstToken);

        thread.Join();
        Console.WriteLine($"Done in: {sw.Elapsed.TotalSeconds}");
    }


    static async ValueTask WriteFlason(ChannelReader<JsonToken> reader, Stack<Rfc6901ReferenceToken> jsonPointer, JsonToken? nextToken)
    {
        if (nextToken is not JsonToken token)
        {
            return;
        }

        switch (token.Type)
        {
            case JsonTokenType.StartArray:
                await WriteFlasonArray(reader, jsonPointer);
                break;
            case JsonTokenType.StartObject:
                await WriteFlasonObject(reader, jsonPointer);
                break;
            default:
                WriteFlasonScalar(jsonPointer, token);
                break;
        }

    }

    static async ValueTask WriteFlasonObject(ChannelReader<JsonToken> reader, Stack<Rfc6901ReferenceToken> jsonPointer)
    {
        while ((await reader.ReadAsync()) is var token && token.Type != JsonTokenType.EndObject)
        {
            var propertyReferenceToken = new Rfc6901ReferenceToken(
                Rfc6901ReferenceTokenType.Property,
                Encoding.UTF8.GetString(token.Utf8ValueBytes));

            jsonPointer.Push(propertyReferenceToken);

            await WriteFlason(reader, jsonPointer, await reader.ReadAsync());

            jsonPointer.Pop();
        }
    }

    static async ValueTask WriteFlasonArray(ChannelReader<JsonToken> reader, Stack<Rfc6901ReferenceToken> jsonPointer)
    {
        var index = 0;
        while ((await reader.ReadAsync()) is var token && token.Type != JsonTokenType.EndArray)
        {
            var indexReferenceToken = new Rfc6901ReferenceToken(Rfc6901ReferenceTokenType.Index, $"{index}");
            jsonPointer.Push(indexReferenceToken);
            await WriteFlason(reader, jsonPointer, token);
            jsonPointer.Pop();
            ++index;
        }
    }

    static void WriteFlasonScalar(Stack<Rfc6901ReferenceToken> jsonPointer, JsonToken token)
    {
        var jsonValue = token.Type == JsonTokenType.String
            ? $"\"{Encoding.UTF8.GetString(token.Utf8ValueBytes)}\""
            : $"{Encoding.UTF8.GetString(token.Utf8ValueBytes)}";

        // todo: For some reason this wont print the '𝄞' character in pwsh. The bytes are correct so I THINK it's a
        // a problem with the terminal/font but I'm not 100% sure.
        // Console.WriteLine($"\"{new Rfc6901JsonPointer(jsonPointer.Reverse())}\": {jsonValue}");
        _nullStream.Write(Encoding.UTF8.GetBytes($"\"{new Rfc6901JsonPointer(jsonPointer.Reverse())}\": {jsonValue}"));
    }
}