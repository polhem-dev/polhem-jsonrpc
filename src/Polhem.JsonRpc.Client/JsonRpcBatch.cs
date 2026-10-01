namespace Polhem.JsonRpc.Client;

/// <summary>
/// Several calls sent to the server in one message.
/// </summary>
/// <remarks>
/// <see cref="Add{TResult}"/> returns a task for each call's result. The tasks complete when
/// <see cref="SendAsync"/> has received the answer; a call that failed throws when its task is awaited. A batch is
/// sent once.
/// </remarks>
public sealed class JsonRpcBatch
{
    private readonly JsonRpcConnector _connector;
    private readonly List<JsonRpcRequest> _requests = [];
    private readonly Dictionary<JsonRpcId, Action<JsonRpcResponse>> _completions = [];
    private readonly Dictionary<JsonRpcId, Action<Exception>> _failures = [];
    private bool _sent;

    internal JsonRpcBatch(JsonRpcConnector connector) => _connector = connector;

    /// <summary>
    /// Adds a call.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters, or <c>null</c> for none.</param>
    /// <returns>A task for the result, which completes after <see cref="SendAsync"/>.</returns>
    public Task<TResult?> Add<TResult>(string method, object? parameters = null)
    {
        EnsureNotSent();
        var request = _connector.CreateRequest(method, parameters, isNotification: false);
        var completion = new TaskCompletionSource<TResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests.Add(request);
        _completions[request.Id] = response =>
        {
            try
            {
                completion.TrySetResult(_connector.ReadResult<TResult>(response));
            }
            catch (Exception ex)
            {
                // NOTE: whatever reading the result throws belongs to this call's task, where the caller awaits it.
                completion.TrySetException(ex);
            }
        };
        _failures[request.Id] = ex => completion.TrySetException(ex);
        return completion.Task;
    }

    /// <summary>
    /// Adds a notification, which the server does not answer.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The parameters, or <c>null</c> for none.</param>
    public void AddNotification(string method, object? parameters = null)
    {
        EnsureNotSent();
        _requests.Add(_connector.CreateRequest(method, parameters, isNotification: true));
    }

    /// <summary>
    /// Sends the batch and completes the tasks of its calls.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>A task that completes when the answer is received.</returns>
    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotSent();
        _sent = true;
        if (_requests.Count == 0) { return; }

        IReadOnlyList<JsonRpcResponse> responses;
        try
        {
            responses = await _connector.SendBatchAsync(_requests, FindRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // NOTE: the batch failed as a whole, so every call fails with the same cause; the exception is still
            // rethrown to the caller of SendAsync.
            FailAll(ex);
            throw;
        }

        foreach (var response in responses)
        {
            if (_completions.Remove(response.Id, out var complete))
            {
                _failures.Remove(response.Id);
                complete(response);
            }
        }
        FailAll(new InvalidOperationException("The server did not answer this call of the batch."));
    }

    private JsonRpcRequest? FindRequest(JsonRpcResponse response) => _requests.Find(r => r.Id == response.Id);

    private void FailAll(Exception exception)
    {
        foreach (var fail in _failures.Values)
        {
            fail(exception);
        }
        _failures.Clear();
        _completions.Clear();
    }

    private void EnsureNotSent()
    {
        if (_sent) { throw new InvalidOperationException("The batch has already been sent."); }
    }
}
