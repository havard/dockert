using System.IO;
using System.Text;

namespace Dockert
{
    internal static class MemoryStreamExtensions
    {
        public static string ReadContentsAsString(this MemoryStream stream, Encoding? encoding = default)
        {
            encoding = encoding ?? Encoding.UTF8;
            return encoding.GetString(stream.ToArray());
        }
    }
}
