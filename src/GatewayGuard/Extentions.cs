using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace GatewayGuard
{
    /// <summary>
    /// Collection of helper stream extension methods used across the library.
    /// </summary>
    static public class Extentions
    {
        /// <summary>
        /// Seeks the provided stream to its beginning.
        /// </summary>
        /// <param name="source">The stream to seek.</param>
        /// <returns>The resulting position (should be 0).</returns>
        static public long SeekBegin(this Stream source)
        {
            if (!source.CanSeek)
            {
                return source.Position;
            }

            return source.Seek(0, SeekOrigin.Begin);
        }

        /// <summary>
        /// Reads the entire stream into a new byte array.
        /// </summary>
        /// <param name="source">The source stream to read.</param>
        /// <returns>A byte array containing the stream contents.</returns>
        static public async Task<byte[]> ToByteArrayAsync(this Stream source)
        {
            using (var temp = new MemoryStream())
            {
                await source.CopyToAsync(temp).ConfigureAwait(false);
                return temp.ToArray();
            }
        }

        /// <summary>
        /// Copies the stream content to a byte array, preserving stream position when possible.
        /// </summary>
        /// <param name="source">The stream to copy.</param>
        /// <returns>A byte array containing the stream content.</returns>
        static public async Task<byte[]> CopyAsync(this Stream source)
        {
            byte[] result = [];

            if (source == null)
            {
                return result;
            }

            if (source.CanSeek)
            {
                source.Position = 0;
                result = await source.ToByteArrayAsync();
                source.Position = 0;
            }
            else
            {
                result = await source.ToByteArrayAsync();
            }

            return result;
        }

        /// <summary>
        /// Sets the HTTP response status code and writes the provided error message.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <param name="statusCode">The HTTP status code to set.</param>
        /// <param name="message">The error message to write to the response body.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        static public async Task SetResponseError(
            this HttpContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(message).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets a 400 Bad Request response indicating that the required idempotency key header is missing.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A task that completes when the response has been written.</returns>
        static public async Task SetResponseErrorMissingIdemKey(this HttpContext context)
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
        static public async Task SetResponseErrorConflictIdemKey(this HttpContext context)
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
        static public async Task SetResponseErrorUnknown(this HttpContext context)
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
        static public async Task SetResponseErrorUnavailableIdemStore(this HttpContext context)
        { 
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync(ErrorMessageUnavailableIdempotencyStore);
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
}
