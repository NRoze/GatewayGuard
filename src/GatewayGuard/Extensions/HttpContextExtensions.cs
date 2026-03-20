using Microsoft.AspNetCore.Http;

namespace GatewayGuard.Extensions;

/// <summary>
/// HttpContext extension helpers used by GatewayGuard to produce consistent error responses.
/// These helpers centralize status/message handling so middleware and stores can return consistent error payloads.
/// </summary>
static public class HttpContextExtensions
{
    extension(HttpContext context)
    {
        private async Task SetResponseError(int statusCode, string message)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = statusCode;
                await context.Response.WriteAsync(message).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sets a 400 Bad Request response indicating that the required idempotency key header is missing.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        public async Task SetResponseErrorMissingIdemKey()
        {
            await context.SetResponseError(
                StatusCodes.Status400BadRequest, 
                ErrorMessageMissingIdempotencyKey);
        }

        /// <summary>
        /// Sets a 409 Conflict response indicating that the idempotency key was used with a different request payload.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        public async Task SetResponseErrorConflictIdemKey()
        {
            await context.SetResponseError(
                StatusCodes.Status409Conflict,
                ErrorMessageConflictIdempotencyKey).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets a 500 Internal Server Error response for an unknown error condition.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        public async Task SetResponseErrorUnknown()
        {
            await context.SetResponseError(
                StatusCodes.Status500InternalServerError,
                ErrorMessageUnknown).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets a 503 Service Unavailable response indicating that the idempotency store is temporarily unreachable.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        public async Task SetResponseErrorUnavailableIdemStore()
        {
            await context.SetResponseError(
                StatusCodes.Status503ServiceUnavailable,
                ErrorMessageUnavailableIdempotencyStore);
        }
    }

    private const string ErrorMessageMissingIdempotencyKey =
        "Missing required idempotency key header.";
    private const string ErrorMessageConflictIdempotencyKey =
        "Idempotency key already used with a different payload.";
    private const string ErrorMessageUnknown =
        "An unknown error occurred while processing the request.";
    private const string ErrorMessageUnavailableIdempotencyStore =
        "Idempotency store is temporarily unavailable.";
}
