using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace GatewayGuard
{
    public interface IIdempotencyStore
    {
        Task<IdempotencyRecord?> GetAsync(string key);
        Task SetAsync(string key, string requestHash, HttpResponse response);
    }
}