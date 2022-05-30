using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace MyProxyServer
{
    public class Response : IDisposable
    {
        //IDisposable Implementation

        bool disposed = false;
        SafeFileHandle handle = new SafeFileHandle(IntPtr.Zero, true);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing)
            {
                handle.Dispose();
                FullText = null;
                Array.Clear(FullBytes, 0, FullBytes.Length);
                FullBytes = null;
                version = null;
                statusCode = 0;
                httpMessage = null;
                headers.Clear();
                headers = null;
                Array.Clear(body, 0, body.Length);
                bodyText = null;
            }

            disposed = true;
        }

        //Main response parser class

        public string FullText { get; private set; } = "";
        public byte[] FullBytes { get; private set; }
        public string version = "";
        public int statusCode = 0;
        public string httpMessage = "";
        public VDictionary headers = new VDictionary();
        public byte[] body = new byte[2048];
        public string bodyText = "";
        public bool notEnded = false;
        public bool bogus = false;

        public Response(int _statusCode, string _httpMessage, string _version, VDictionary _headers, string _body, byte[] fullBytes)
        {
            statusCode = _statusCode;
            httpMessage = _httpMessage;
            version = _version;
            bodyText = _body;
            body = fullBytes;
            headers = _headers;
        }

        public void CheckMimeAndSetBody()
        {
            if (headers.ContainsKey("Content-Length") && headers["Content-Length"] == "0") return;
            if (!headers.ContainsKey("Content-Type"))
            {
                body = new byte[0];
                return;
            }
            DecodeArray();
        }

        private void DecodeArray()
        {
            notEnded = false;
            string cType = headers["Content-Type"];
            if (cType.Contains(";")) cType = cType.Substring(0, cType.IndexOf(';'));
            Decoder vd = new Decoder();
            bool isConvertable = false;
            if (isConvertable && !headers.ContainsKey("Content-Encoding"))
            {
                bodyText = vd.DecodeCharset(headers["Content-Type"], body, body.Length);
            }
            else if (isConvertable && headers.ContainsKey("Content-Encoding"))
            {
                string enc = headers["Content-Encoding"];
                if (enc == "gzip") body = vd.DecodeGzipToBytes(body);
                else if (enc == "deflate") body = vd.DecodeDeflate(body);
                else if (enc == "br") body = vd.DecodeBrotli(body);

                bodyText = vd.DecodeCharset(headers["Content-Type"], body, body.Length);
                //IMPORTANT: Use push end -- the data is converted to text correctly
            }
            else if (!isConvertable && headers.ContainsKey("Content-Encoding"))
            {

                //Decode contents to byte array
                string enc = headers["Content-Encoding"];
                if (enc == "gzip") body = vd.DecodeGzipToBytes(body);
                else if (enc == "deflate") body = vd.DecodeDeflate(body);
                else if (enc == "br") body = vd.DecodeBrotli(body);
            }
            else
            {
                //Data is in clearText, not convertable to printable (text) format for ex. image file, exe file
                bodyText = "";
            }
        }

        public void Deserialize(NetworkStream ns, Request req, SslHandler vsh = null)
        {
            string sResult = version + " " + statusCode + " " + httpMessage + "\r\n";
            int ctLength = 0;

            //edit bodyText here

            Decoder vd = new Decoder();

            if (headers.ContainsKey("Content-Length") && headers["Content-Length"] != "0" && headers["Content-Length"] != null)
            {
                if (bodyText != "" && headers.ContainsKey("Content-Encoding"))
                {
                    Array.Clear(body, 0, body.Length);
                    byte[] toCode = vd.EncodeCharset(headers["Content-Type"], bodyText);
                    string enc = headers["Content-Encoding"];
                    if (enc == "gzip") body = vd.EncodeGzip(toCode);
                    else if (enc == "deflate") body = vd.EncodeDeflate(toCode);
                    else if (enc == "br") body = vd.EncodeBrotli(toCode);
                    Array.Clear(toCode, 0, toCode.Length);
                }
                else if (bodyText == "" && headers.ContainsKey("Content-Encoding"))
                {
                    string enc = headers["Content-Encoding"];
                    if (enc == "gzip") body = vd.EncodeGzip(body);
                    else if (enc == "deflate") body = vd.EncodeDeflate(body);
                    else if (enc == "br") body = vd.EncodeBrotli(body);
                }
                else if (bodyText != "" && !headers.ContainsKey("Content-Encoding"))
                {
                    body = vd.EncodeCharset(headers["Content-Type"], bodyText);
                }

                ctLength = body.Length;
            }

            foreach (KeyValuePair<string, string> kvp in headers.Items)
            {
                string line = "";
                if (kvp.Key == "Content-Length" && ctLength > 0) line = "Content-Length: " + ctLength + "\r\n";
                else if (kvp.Key == "Transfer-Encoding" && kvp.Value == "chunked" && ctLength > 0)
                {
                    // insert the content-length and skip the transfer-encoding header, because we concatanated it.
                    line = "Content-Length: " + ctLength.ToString() + "\r\n";
                }
                else line = kvp.Key + ": " + kvp.Value + "\r\n";

                sResult += line;
            }

            //console.Debug($"{req.target} - responded with content-type: {headers["Content-Type"]}");

            sResult += "\r\n";
            byte[] text = Encoding.ASCII.GetBytes(sResult);
            if (vsh == null)
            {
                ns.Write(text, 0, text.Length);
                if (ctLength > 0) ns.Write(body, 0, body.Length);
                ns.Flush();
            }
            else
            {
                //console.Debug("Handler " + vsh.HandlerID + " receiving " + (headers.ContainsKey("Content-Type") ? headers["Content-Type"] : "No content type sent"));
                vsh.WriteSslStream(text);
                if (ctLength > 0) vsh.WriteSslStream(body);
                vsh.FlushSslStream();
            }
        }
    }
}
