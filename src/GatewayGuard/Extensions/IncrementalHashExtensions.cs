using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace GatewayGuard.Extensions
{
    static public class IncrementalHashExtensions
    {
        extension(IncrementalHash hash)
        {
            public void AppendDataUTF8(string data)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(data));
            }

            public string ToHexString()
            {
                var hashBytes = hash.GetHashAndReset();

                return Convert.ToHexString(hashBytes);
            }
        }
    }
}
