using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace GatewayGuard
{
    internal static class SingleFlightManager
    {
        private static readonly ConcurrentDictionary<string, Task<object>> InFlight = new();

        public static Task<object> ExecuteAsync(string key, Func<Task<object>> action)
        {
            return InFlight.GetOrAdd(key, _ => ExecuteAndRemove(key, action));
        }

        private static async Task<object> ExecuteAndRemove(string key, Func<Task<object>> action)
        {
            try
            {
                return await action();
            }
            finally
            {
                InFlight.TryRemove(key, out _);
            }
        }
    }
}