using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowShellBar.App.Integrations;

internal sealed class NamedPipeJsonClient
{
    private readonly TimeSpan _timeout;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public NamedPipeJsonClient(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    public T Deserialize<T>(JsonElement value)
    {
        var result = JsonSerializer.Deserialize<T>(value.GetRawText(), _jsonOptions);
        return result ?? throw new InvalidOperationException($"Failed to deserialize IPC payload as {typeof(T).Name}.");
    }

    public async Task<JsonPipeResponseEnvelope> SendAsync(
        string pipeName,
        object request,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        await using var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        await pipe.ConnectAsync((int)_timeout.TotalMilliseconds, timeoutCts.Token);
        pipe.ReadMode = PipeTransmissionMode.Message;

        var payload = JsonSerializer.SerializeToUtf8Bytes(request, _jsonOptions);
        await pipe.WriteAsync(payload, timeoutCts.Token);
        await pipe.FlushAsync(timeoutCts.Token);

        var responseText = await ReadMessageAsync(pipe, timeoutCts.Token);
        var response = JsonSerializer.Deserialize<JsonPipeResponseEnvelope>(responseText, _jsonOptions);
        return response ?? throw new InvalidOperationException("Failed to deserialize IPC response envelope.");
    }

    private static async Task<string> ReadMessageAsync(PipeStream pipe, CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using var buffer = new MemoryStream();
            do
            {
                var read = await pipe.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                buffer.Write(rented, 0, read);
            }
            while (!pipe.IsMessageComplete);

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}

internal sealed record JsonPipeResponseEnvelope(
    int ProtocolVersion,
    string RequestId,
    bool Ok,
    JsonElement Result,
    JsonPipeErrorEnvelope? Error);

internal sealed record JsonPipeErrorEnvelope(
    string Code,
    string Message,
    string? Category,
    bool? Retryable,
    JsonElement Details);
