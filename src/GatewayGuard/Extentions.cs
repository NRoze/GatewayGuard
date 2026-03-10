using System;
using System.Collections.Generic;
using System.Text;

namespace GatewayGuard
{
    static public class Extentions
    {
        static public long SeekBegin(this Stream source)
        {
            return source.Seek(0, SeekOrigin.Begin);
        }

        static public async Task<byte[]> ToByteArray(this Stream source)
        {
            using (var temp = new MemoryStream())
            {
                await source.CopyToAsync(temp).ConfigureAwait(false);
                return temp.ToArray();
            }
        }
        static public async Task<byte[]> CopyAsync(this Stream source)
        {
            byte[] result = Array.Empty<byte>();

            if (source == null)
            {
                return result;
            }
            else if (source.CanSeek)
            {
                source.Position = 0;
                result = await source.ToByteArray().ConfigureAwait(false);
                source.Position = 0;
            }
            else
            {
                result = await source.ToByteArray().ConfigureAwait(false);
                source = new MemoryStream(result);
            }

            return result;
        }
    }
}
