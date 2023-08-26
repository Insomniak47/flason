using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ttd2089.Flason;
using Rfc6901JsonPointer = Ttd2089.Flason.Rfc6901.JsonPointer;
using Rfc6901ReferenceToken = Ttd2089.Flason.Rfc6901.ReferenceToken;
using Rfc6901ReferenceTokenType = Ttd2089.Flason.Rfc6901.ReferenceTokenType;

class Program
{
    private static readonly StreamWriter streamyBoi = new(Stream.Null, Encoding.UTF8);

    public static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        using var stdin = Console.OpenStandardInput();
        var sw = Stopwatch.StartNew();
        // Get this file for similar benching
        //var file = File.OpenRead("F:\\RandomCode\\AllPrintings.json");


        var jsonTokenReader = new JsonTokenReader(stdin, new()
        {
            InitialBufferSize = 4096,
            CommentHandling = JsonCommentHandling.Skip,
        });

        //Console.WriteLine("Starting");
        await WriteFlason(jsonTokenReader);
        //Console.WriteLine($"Finished in: {sw.Elapsed.TotalSeconds}");
    }

    static async ValueTask WriteFlason(JsonTokenReader reader) => await WriteFlason(reader, new(), await reader.NextAsync());

    static async ValueTask WriteFlason(JsonTokenReader reader, Stack<Rfc6901ReferenceToken> jsonPointer, JsonToken? nextToken)
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

    static async ValueTask WriteFlasonObject(JsonTokenReader reader, Stack<Rfc6901ReferenceToken> jsonPointer)
    {
        while (await reader.NextAsync() is JsonToken token && token.Type != JsonTokenType.EndObject)
        {
            var propertyReferenceToken = new Rfc6901ReferenceToken(
                Rfc6901ReferenceTokenType.Property,
                Encoding.UTF8.GetString(token.Utf8ValueBytes));

            jsonPointer.Push(propertyReferenceToken);

            await WriteFlason(reader, jsonPointer, await reader.NextAsync());

            jsonPointer.Pop();
        }
    }

    static async ValueTask WriteFlasonArray(JsonTokenReader reader, Stack<Rfc6901ReferenceToken> jsonPointer)
    {
        var index = 0;
        while (await reader.NextAsync() is JsonToken token && token.Type != JsonTokenType.EndArray)
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
        //Console.WriteLine($"\"{new Rfc6901JsonPointer(jsonPointer.Reverse())}\": {jsonValue}");
        // So that we can get console write times out of the bench
        streamyBoi.Write($"\"{new Rfc6901JsonPointer(jsonPointer.Reverse())}\": {jsonValue}");
    }
}