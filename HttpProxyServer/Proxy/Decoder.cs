using System.Text;
using System.IO;


namespace MyProxyServer
{
    public class Decoder
    {
        public byte[] EncodeDeflate(byte[] plainData)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (System.IO.Compression.DeflateStream deflate = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Compress, true))
                {
                    deflate.Write(plainData, 0, plainData.Length);
                }
                return ms.ToArray();
            }
        }

        public byte[] DecodeDeflate(byte[] deflateData)
        {
            return DecDeflateData(deflateData);
        }

        public byte[] DecodeBrotli(byte[] brotliData)
        {
            byte[] result;

            using (MemoryStream ms = new MemoryStream(brotliData))
            {
                using (MemoryStream decoded = new MemoryStream())
                {
                    Brotli.BrotliCompression.Decompress(ms, decoded);
                    result = decoded.ToArray();
                }
            }

            return result;
        }

        public byte[] EncodeBrotli(byte[] plainData)
        {
            byte[] result;

            using (MemoryStream ms = new MemoryStream(plainData))
            {
                using (MemoryStream encoded = new MemoryStream())
                {
                    Brotli.BrotliCompression.Compress(ms, encoded);
                    result = encoded.ToArray();
                }
            }

            return result;
        }

        public string DecodeGzip(byte[] gzipData)
        {
            byte[] result = DecGzipData(gzipData);
            return Encoding.ASCII.GetString(result, 0, result.Length);
        }

        public byte[] DecodeGzipToBytes(byte[] gzipData)
        {
            return DecGzipData(gzipData);
        }

        public byte[] EncodeGzip(string text)
        {
            byte[] gzipData = Encoding.ASCII.GetBytes(text);

            using (MemoryStream ms = new MemoryStream())
            {
                using (System.IO.Compression.GZipStream gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, true))
                {
                    gzip.Write(gzipData, 0, gzipData.Length);
                }
                return ms.ToArray();
            }
        }

        public byte[] EncodeGzip(byte[] gzipData)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (System.IO.Compression.GZipStream gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, true))
                {
                    gzip.Write(gzipData, 0, gzipData.Length);
                }
                return ms.ToArray();
            }
        }

        public string DecodeCharset(string cType, byte[] value, int bytesRead)
        {
            string result = "";

            Encoding e = GetEncoding(cType);
            cType = null;
            result = e.GetString(value, 0, bytesRead);

            return result;
        }

        public byte[] EncodeCharset(string cType, string value)
        {
            Encoding enc = GetEncoding(cType);
            return enc.GetBytes(value);
        }

        public byte[] EncodeCharset(string cType, string value, Encoding current)
        {
            Encoding target = GetEncoding(cType);
            byte[] bytes = current.GetBytes(value);
            return Encoding.Convert(current, target, bytes);
        }

        private Encoding GetEncoding(string cType)
        {
            if (cType.Contains(";"))
            {
                string[] ps = cType.Split(';');
                foreach (string entry in ps)
                {
                    if (entry.StartsWith("charset"))
                    {
                        string enc = entry.Split('=')[1];
                        Encoding encoder = Encoding.GetEncoding(enc);
                        return encoder;
                    }
                }

                return Encoding.GetEncoding("ISO-8859-1");
            }
            else
            {
                return Encoding.GetEncoding("ISO-8859-1");
            }
        }

        private byte[] DecGzipData(byte[] gzipData)
        {
            byte[] bytes = new byte[4096];
            byte[] decoded;

            using (System.IO.Compression.GZipStream stream = new System.IO.Compression.GZipStream(new MemoryStream(gzipData), System.IO.Compression.CompressionMode.Decompress))
            {
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(bytes, 0, 4096);
                        if (count > 0)
                        {
                            memory.Write(bytes, 0, count);
                        }
                    }
                    while (count > 0);
                    decoded = memory.ToArray();
                }
            }

            return decoded;
        }

        private byte[] DecDeflateData(byte[] deflateData)
        {
            byte[] bytes = new byte[4096];
            byte[] decoded;

            using (System.IO.Compression.DeflateStream stream = new System.IO.Compression.DeflateStream(new MemoryStream(deflateData), System.IO.Compression.CompressionMode.Decompress))
            {
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(bytes, 0, 4096);
                        if (count > 0)
                        {
                            memory.Write(bytes, 0, count);
                        }
                    }
                    while (count > 0);
                    decoded = memory.ToArray();
                }
            }

            return decoded;
        }
    }

}
