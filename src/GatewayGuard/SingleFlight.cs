using System.Collections.Concurrent;

namespace GatewayGuard
{
    public sealed class SingleFlight
    {
        private readonly ConcurrentDictionary<string, Flight> _inFlight = new();
        private readonly TimeSpan _ttl;
        private readonly int _maxEntries;
        private readonly IdempotencyOptions _options;

        public SingleFlight(IdempotencyOptions options)
        {
            _options = options;
            _ttl = options.SingleFlightExpiration;
            _maxEntries = options.MaxConcurrentRequests;
        }

        public async Task<T> ExecuteAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> action,
            CancellationToken callerToken = default)
        {
            CleanupIfNeeded();

            var flight = new Flight();

            if (_inFlight.TryAdd(key, flight))
            {
                await RunAsync(key, flight, action);
            }
            else
            {
                flight = _inFlight[key];
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
                _inFlight.TryRemove(new KeyValuePair<string, Flight>(key, flight));
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

            public bool IsExpired => Environment.TickCount64 - _created > 30000;// TBD and make this configurable 

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