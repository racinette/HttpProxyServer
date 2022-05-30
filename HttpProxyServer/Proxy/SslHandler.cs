using System;
using System.Text;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;


namespace MyProxyServer
{
    public class SslHandler : IDisposable
    {
        //IDisposable Implementation

        bool disposed = false;

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
                Close();
                _ssl = null;
                Array.Clear(buffer, 0, buffer.Length);
                buffer = null;
            }

            disposed = true;
        }

        private SslStream _ssl;
        private byte[] buffer = new byte[2048];

        public SslHandler()
        {

        }

        public class CertificateManagerNotAvailableException : Exception { }
        public class CertificateAutoGenerationException : Exception { }
        public class CertificateNotFoundException : Exception { }
        public class SslProtocolException : Exception { }
        public class SslServerAuthException : Exception { }
        public class SslStreamWriteException : Exception { }
        public class SslStreamDisposedException : Exception { }

        public void WriteSslStream(byte[] data)
        {
            if (_ssl == null) throw new SslStreamDisposedException();
            if (!_ssl.CanWrite) throw new SslStreamWriteException();
            try { _ssl.Write(data, 0, data.Length); }
            catch (Exception)
            {
                throw new SslStreamWriteException();
            }
        }

        public void FlushSslStream()
        {
            _ssl.Flush();
        }

        public void Close()
        {
            if (_ssl == null) throw new SslStreamDisposedException();
            _ssl.Close();
            _ssl.Dispose();
        }

        struct ReadObj
        {
            public string full;
            public Request r;
            public bool requestHandled;
        }

        private void ReadFromStream(IAsyncResult ar)
        {
            ReadObj ro = (ReadObj)ar.AsyncState;
            Request r = ro.r;
            int bytesRead = 0;
            try { bytesRead = _ssl.EndRead(ar); }
            catch (Exception) { return; }
            byte[] read = new byte[bytesRead];
            Array.Copy(buffer, read, bytesRead);
            string text = Encoding.ASCII.GetString(read);

            if (bytesRead > 0)
            {
                if (r == null)
                {
                    r = new Request(text, true);
                }

                if (r.notEnded)
                {
                    if (ro.full == "") ro.full = text;
                    else
                    {
                        ro.full += text;
                        r = new Request(ro.full, true);
                    }
                }

                if (!r.notEnded && !r.bogus)
                {
                    string requestString = r.Deserialize();

                    Tunnel.Send(requestString, Tunnel.Mode.HTTPs, r, null, this);
                    ro.full = "";
                    ro.requestHandled = true;
                }
            }

            Array.Clear(buffer, 0, buffer.Length);
            if (!ro.requestHandled) ro.r = r;
            else
            {
                ro.r = null;
                ro.requestHandled = false;
            }
            try { _ssl.BeginRead(buffer, 0, buffer.Length, new AsyncCallback(ReadFromStream), ro); }
            catch (Exception ex)
            {
                //ctx.LogMod.Log("Ssl stream error MITM\r\n" + ex.Message, VLogger.LogLevel.error);
                Console.WriteLine("St: " + ex.StackTrace);
            }
        }
    }
}
