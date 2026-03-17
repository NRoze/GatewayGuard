using Microsoft.AspNetCore.Http;

namespace GatewayGuard.Extentions;

static public class HttpContextExtentions
{
    extension(HttpContext context)
    {
        /// <summary>
        /// Sets the HTTP response status code and writes the provided error message.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <param name="statusCode">The HTTP status code to set.</param>
        /// <param name="message">The error message to write to the response body.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        public async Task SetResponseError(int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(message).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets a 400 Bad Request response indicating that the required idempotency key header is missing.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        public async Task SetResponseErrorMissingIdemKey()
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(
                ErrorMessageMissingIdempotencyKey).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets a 409 Conflict response indicating that the idempotency key was used with a different request payload.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        public async Task SetResponseErrorConflictIdemKey()
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsync(
                ErrorMessageConflictIdempotencyKey).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets a 500 Internal Server Error response for an unknown error condition.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        public async Task SetResponseErrorUnknown()
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(
                ErrorMessageUnknown).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets a 503 Service Unavailable response indicating that the idempotency store is temporarily unreachable.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        public async Task SetResponseErrorUnavailableIdemStore()
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync(ErrorMessageUnavailableIdempotencyStore);
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
