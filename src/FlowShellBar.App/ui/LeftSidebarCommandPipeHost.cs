using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using FlowShellBar.App.Diagnostics;
using FlowShellBar.App.Integrations;

using Microsoft.UI.Dispatching;

namespace FlowShellBar.App.Ui;

internal sealed class LeftSidebarCommandPipeHost : IDisposable
{
    private const string PipeName = "flowshellbar.sidebar.left";

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly IAppLogger _logger;
    private readonly Func<LeftSidebarCommandKind, LeftSidebarCommandSnapshot> _commandExecutor;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task? _listenTask;

    public LeftSidebarCommandPipeHost(
        DispatcherQueue dispatcherQueue,
        IAppLogger logger,
        Func<LeftSidebarCommandKind, LeftSidebarCommandSnapshot> commandExecutor)
    {
        _dispatcherQueue = dispatcherQueue;
        _logger = logger;
        _commandExecutor = commandExecutor;
    }

    public void Start()
    {
        if (_listenTask is not null)
        {
            return;
        }

        _listenTask = Task.Run(() => ListenAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        _logger.Info($"Left sidebar pipe host started: {PipeName}.");
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleConnectionAsync(pipe, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error("Left sidebar pipe host failed.", exception);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var requestText = await ReadMessageAsync(pipe, cancellationToken);
        JsonPipeRequestEnvelope? request;

        try
        {
            request = JsonSerializer.Deserialize<JsonPipeRequestEnvelope>(requestText, _jsonOptions);
        }
        catch (JsonException exception)
        {
            _logger.Warning($"Left sidebar pipe received invalid JSON: {exception.Message}");
            await WriteResponseAsync(
                pipe,
                requestId: Guid.NewGuid().ToString("N"),
                ok: false,
                result: null,
                error: new JsonPipeErrorEnvelope(
                    Code: "invalid_json",
                    Message: "The pipe payload is not valid JSON.",
                    Category: "request",
                    Retryable: false,
                    Details: JsonSerializer.SerializeToElement(new { })),
                cancellationToken);
            return;
        }

        if (request is null)
        {
            return;
        }

        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString("N")
            : request.RequestId!;

        if (!TryMapCommand(request.MessageType, out var commandKind))
        {
            _logger.Warning($"Left sidebar pipe received unsupported message type: {request.MessageType}.");
            await WriteResponseAsync(
                pipe,
                requestId,
                ok: false,
                result: null,
                error: new JsonPipeErrorEnvelope(
                    Code: "unsupported_message_type",
                    Message: $"Unsupported left sidebar message type: {request.MessageType}.",
                    Category: "request",
                    Retryable: false,
                    Details: JsonSerializer.SerializeToElement(new { })),
                cancellationToken);
            return;
        }

        var snapshot = await ExecuteOnUiThreadAsync(commandKind, cancellationToken);
        _logger.Info($"Left sidebar pipe command handled: message={request.MessageType}; mode={snapshot.Mode}; open={snapshot.IsOpen}.");
        await WriteResponseAsync(
            pipe,
            requestId,
            ok: true,
            result: snapshot,
            error: null,
            cancellationToken);
    }

    private async Task<LeftSidebarCommandSnapshot> ExecuteOnUiThreadAsync(
        LeftSidebarCommandKind commandKind,
        CancellationToken cancellationToken)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            return _commandExecutor(commandKind);
        }

        var completion = new TaskCompletionSource<LeftSidebarCommandSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    completion.SetResult(_commandExecutor(commandKind));
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
        {
            throw new InvalidOperationException("Failed to enqueue left sidebar pipe command on the UI dispatcher.");
        }

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return await completion.Task;
    }

    private async Task WriteResponseAsync(
        NamedPipeServerStream pipe,
        string requestId,
        bool ok,
        object? result,
        JsonPipeErrorEnvelope? error,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                protocol_version = 1,
                request_id = requestId,
                ok,
                result = result ?? new { },
                error,
            },
            _jsonOptions);
        await pipe.WriteAsync(payload, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
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

    private static bool TryMapCommand(string? messageType, out LeftSidebarCommandKind commandKind)
    {
        commandKind = messageType switch
        {
            "left_sidebar.toggle" => LeftSidebarCommandKind.Toggle,
            "left_sidebar.open" => LeftSidebarCommandKind.Open,
            "left_sidebar.close" => LeftSidebarCommandKind.Close,
            "left_sidebar.detach" => LeftSidebarCommandKind.Detach,
            "left_sidebar.pin" => LeftSidebarCommandKind.Pin,
            "left_sidebar.attach" => LeftSidebarCommandKind.Attach,
            _ => default,
        };

        return messageType is
            "left_sidebar.toggle"
            or "left_sidebar.open"
            or "left_sidebar.close"
            or "left_sidebar.detach"
            or "left_sidebar.pin"
            or "left_sidebar.attach";
    }

    private sealed record JsonPipeRequestEnvelope(
        int ProtocolVersion,
        string MessageType,
        string? RequestId,
        JsonElement Payload);
}
