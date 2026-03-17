using System.Collections.Concurrent;

namespace GatewayGuard
{
    public sealed class SingleFlight
    {
        private readonly ConcurrentDictionary<string, Flight> _inFlight = new();
        private readonly TimeSpan _ttl;
        private readonly int _maxEntries;
        private readonly Timer _resetTimer;

        public SingleFlight(IdempotencyOptions options)
        {
            _ttl = options.SingleFlightExpiration;
            _maxEntries = options.MaxConcurrentRequests;
            _resetTimer = new Timer(_ => CleanupIfNeeded(), null, _ttl, _ttl);
        }

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

            foreach (var pair in _inFlight)
            {
                if (pair.Value.IsExpired)
                {
                    _inFlight.TryRemove(pair);
                }
            }
        }

        private sealed class Flight
        {
            private readonly TaskCompletionSource<object?> _tcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly long _created = Environment.TickCount64;
            private readonly TimeSpan _ttl;
            public bool IsExpired => TimeSpan.FromMilliseconds(Environment.TickCount64 - _created) > _ttl;

            public Flight(TimeSpan ttl)
            {
                _ttl = ttl;
            }
            public async Task<T> WaitAsync<T>(CancellationToken ct)
            {
                using var reg = ct.Register(() => _tcs.TrySetCanceled(ct));
                var result = await _tcs.Task.ConfigureAwait(false);

                return (T)result!;
            }

            public void SetResult<T>(T result) => _tcs.TrySetResult(result);

            public void SetException(Exception ex) => _tcs.TrySetException(ex);
        }
    }
}