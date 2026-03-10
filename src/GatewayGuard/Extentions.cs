using System;
using System.Collections.Generic;
using System.Text;

namespace GatewayGuard
{
    static public class Extentions
    {
        static public long SeekBegin(this MemoryStream stream)
        {
            return stream.Seek(0, SeekOrigin.Begin);
        }
    }
}
