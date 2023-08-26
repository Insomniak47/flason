﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Ttd2089.Flason;
using Rfc6901JsonPointer = Ttd2089.Flason.Rfc6901.JsonPointer;
using Rfc6901ReferenceToken = Ttd2089.Flason.Rfc6901.ReferenceToken;
using Rfc6901ReferenceTokenType = Ttd2089.Flason.Rfc6901.ReferenceTokenType;

class Program
{
    static async Task Main(string[] args)
    {
        var stream = args.Length == 0 ||  args[0] == "-" 
            ? Console.OpenStandardInput() 
            : File.OpenRead(args[0]);


        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        var channel = Channel.CreateBounded<JsonToken>(new BoundedChannelOptions(4096)
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

        thread.Start();
        try
        {

            var firstToken = await channel.Reader.ReadAsync();
            await WriteFlason(channel.Reader, new(), firstToken);
            //while (await channel.Reader.WaitToReadAsync())
            //{
            //    var t = await channel.Reader.ReadAsync();
            //    await Console.Out.WriteLineAsync(t.ToString());
            //}
        }
        catch (Exception ex)
        {
            await Console.Out.WriteLineAsync("Fucked up");
            await Console.Out.WriteLineAsync(ex.Message);
            return;
        }

        thread.Join();
    }


    static int writeDepth = 0;
    static int objectDepth = 0;
    static int arrayDepth = 0;
    static int scalarDepth = 0;


    static async ValueTask WriteFlason(ChannelReader<JsonToken> reader, Stack<Rfc6901ReferenceToken> jsonPointer, JsonToken? nextToken)
    {
        Interlocked.Increment(ref writeDepth);
        await Console.Out.WriteLineAsync($"Write depth is {writeDepth} - Token depth is {nextToken?.Depth ?? 'n'}");

        if (writeDepth > 20)
        {
            var i = 0;
            return;
        }

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
        Interlocked.Decrement(ref writeDepth);

    }

    static async ValueTask WriteFlasonObject(ChannelReader<JsonToken> reader, Stack<Rfc6901ReferenceToken> jsonPointer)
    {
        Interlocked.Increment(ref objectDepth);



        while ((await reader.ReadAsync()) is JsonToken token && token.Type != JsonTokenType.EndObject)
        {
            await Console.Out.WriteLineAsync($"obj depth is {objectDepth} - Token depth is {token.Depth}");
            if (objectDepth > 500) { return; }

            var propertyReferenceToken = new Rfc6901ReferenceToken(
                Rfc6901ReferenceTokenType.Property,
                Encoding.UTF8.GetString(token.Utf8ValueBytes));

            jsonPointer.Push(propertyReferenceToken);

            await WriteFlason(reader, jsonPointer, await reader.ReadAsync());

            jsonPointer.Pop();
        }
        Interlocked.Decrement(ref objectDepth);

    }

    static async ValueTask WriteFlasonArray(ChannelReader<JsonToken> reader, Stack<Rfc6901ReferenceToken> jsonPointer)
    {
        Interlocked.Increment(ref arrayDepth);
        var index = 0;
        while ((await reader.ReadAsync()) is JsonToken token && token.Type != JsonTokenType.EndArray)
        {
            await Console.Out.WriteLineAsync($"arr depth is {arrayDepth} - Token depth is {token.Depth}");
            if (arrayDepth > 500) { return; };
            var indexReferenceToken = new Rfc6901ReferenceToken(Rfc6901ReferenceTokenType.Index, $"{index}");
            jsonPointer.Push(indexReferenceToken);
            await WriteFlason(reader, jsonPointer, token);
            jsonPointer.Pop();
            ++index;
        }
        Interlocked.Decrement(ref arrayDepth);
        jsonPointer.Pop();
    }

    static void WriteFlasonScalar(Stack<Rfc6901ReferenceToken> jsonPointer, JsonToken token)
    {
        Interlocked.Increment(ref scalarDepth);

        var jsonValue = token.Type == JsonTokenType.String
            ? $"\"{Encoding.UTF8.GetString(token.Utf8ValueBytes)}\""
            : $"{Encoding.UTF8.GetString(token.Utf8ValueBytes)}";

        // todo: For some reason this wont print the '𝄞' character in pwsh. The bytes are correct so I THINK it's a
        // a problem with the terminal/font but I'm not 100% sure.
        //Console.WriteLine($"\"{new Rfc6901JsonPointer(jsonPointer.Reverse())}\": {jsonValue}");
        Interlocked.Decrement(ref scalarDepth);
    }

    private static JsonToken GetTokenFromReader(Utf8JsonReader reader) => new(
    type: reader.TokenType,
    value: reader.ValueSpan,
    depth: reader.CurrentDepth,
    valueIsEscaped: reader.ValueIsEscaped);

}