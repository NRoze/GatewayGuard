namespace GatewayGuard.Extentions;

static public class StreamExtentions
{
    extension(Stream source)
    {
        /// <summary>
        /// Seeks the provided stream to its beginning.
        /// </summary>
        /// <param name="source">The stream to seek.</param>
        /// <returns>The resulting position (should be 0).</returns>
        public long SeekBegin()
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
        public async Task<byte[]> ToByteArrayAsync()
        {
            using var temp = new MemoryStream();

            await source.CopyToAsync(temp).ConfigureAwait(false);
            return temp.ToArray();
        }

        /// <summary>
        /// Copies the stream content to a byte array, preserving stream position when possible.
        /// </summary>
        /// <param name="source">The stream to copy.</param>
        /// <returns>A byte array containing the stream content.</returns>
        public async Task<byte[]> CopyAsync()
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
    }
}
