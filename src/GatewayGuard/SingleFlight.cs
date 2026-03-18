using System.Collections.Concurrent;

namespace GatewayGuard;

/// <summary>
/// Manages single-flight request execution to coordinate concurrent requests for the same idempotency key.
/// Ensures that duplicate concurrent requests wait for the first request to complete rather than executing independently.
/// </summary>
public sealed class SingleFlight
{
    private readonly ConcurrentDictionary<string, Flight> _inFlight = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;
    private readonly Timer _resetTimer;

    /// <summary>
    /// Initializes a new instance of <see cref="SingleFlight"/>.
    /// </summary>
    /// <param name="options">Configuration options containing TTL and maximum concurrent request settings.</param>
    public SingleFlight(IdempotencyOptions options)
    {
        _ttl = options.SingleFlightExpiration;
        _maxEntries = options.MaxConcurrentRequests;
        _resetTimer = new Timer(_ => CleanupIfNeeded(), null, _ttl, _ttl);
    }

    /// <summary>
    /// Executes an action once for a given key, with concurrent requests for the same key waiting for the result.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the action.</typeparam>
    /// <param name="key">The key identifying the flight coordination group.</param>
    /// <param name="action">The async function to execute.</param>
    /// <param name="callerToken">Optional cancellation token for the caller.</param>
    /// <returns>A task that completes with the result from the first (or cached) execution.</returns>
    public async Task<T> ExecuteAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> action,
        CancellationToken callerToken = default)
    {
        var flight = new Flight(_ttl);

        if (_inFlight.TryAdd(key, flight))
        {
            await RunAsync(key, flight, action);
        }
        else if (_inFlight.TryGetValue(key, out var existing))
        {
            flight = existing;
        }

        return await flight.WaitAsync<T>(callerToken);
    }

    private async Task RunAsync<T>(string key, Flight flight, Func<CancellationToken, Task<T>> action)
    {
        using var cts = new CancellationTokenSource(_ttl);

        try
        {
            var result = await action(cts.Token).ConfigureAwait(false);

            flight.SetResult(result);
        }
        catch (Exception ex)
        {
            flight.SetException(ex);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private void CleanupIfNeeded()
    {
        if (_inFlight.Count <= _maxEntries)
            return;

        foreach (var pair in _inFlight.Where(x => x.Value.IsExpired))
        {
            _inFlight.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>
    /// Internal class representing a single-flight coordination request.
    /// </summary>
    private sealed class Flight
    {
        private readonly TaskCompletionSource<object?> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly long _created = Environment.TickCount64;
        private readonly TimeSpan _ttl;
        public bool IsExpired => TimeSpan.FromMilliseconds(Environment.TickCount64 - _created) > _ttl;

        /// <summary>
        /// Initializes a new <see cref="Flight" /> instance with the given TTL.
        /// </summary>
        /// <param name="ttl">Time to live for this flight.</param>
        public Flight(TimeSpan ttl)
        {
            _ttl = ttl;
        }
        /// <summary>
        /// Waits for the flight result with support for cancellation.
        /// </summary>
        /// <typeparam name="T">The type of result to wait for.</typeparam>
        /// <param name="ct">Cancellation token to cancel the wait operation.</param>
        /// <returns>A task that completes with the flight result.</returns>
        public async Task<T> WaitAsync<T>(CancellationToken ct)
        {
            using var reg = ct.Register(() => _tcs.TrySetCanceled(ct));
            var result = await _tcs.Task.ConfigureAwait(false);

            return (T)result!;
        }

        /// <summary>
        /// Sets the result for this flight, waking any waiting tasks.
        /// </summary>
        /// <typeparam name="T">The type of result to set.</typeparam>
        /// <param name="result">The result value.</param>
        public void SetResult<T>(T result) => _tcs.TrySetResult(result);

        /// <summary>
        /// Sets an exception for this flight, propagating it to any waiting tasks.
        /// </summary>
        /// <param name="ex">The exception to set.</param>
        public void SetException(Exception ex) => _tcs.TrySetException(ex);
    }
}