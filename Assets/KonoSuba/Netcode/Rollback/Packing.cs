using System.IO;
using System.IO.Compression;

namespace AnimLab.Netcode.Rollback
{
    /// <summary>Compressão dos estados que viajam inteiros (handshake, diagnóstico de desync): cabem num datagrama.</summary>
    public static class Packing
    {
        public static byte[] Deflate(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var z = new DeflateStream(output, CompressionLevel.Optimal, true)) z.Write(data, 0, data.Length);
                return output.ToArray();
            }
        }
        /// <summary>Descomprime até max bytes; mais do que isso é um pacote inválido.</summary>
        public static byte[] Inflate(byte[] data, int max = 65536)
        {
            using (var input = new MemoryStream(data, false))
            using (var z = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[4096]; int read;
                while ((read = z.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    if (output.Length > max) throw new InvalidDataException("State too large");
                }
                return output.ToArray();
            }
        }
    }
}
