using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using Microsoft.Win32.SafeHandles;
using System.Threading.Tasks;
using System.Net.Http;

namespace MyProxyServer
{
    public class Tunnel : IDisposable
    {
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
                _host = null;
                client = null;
                TunnelDestroyed = true;
            }

            disposed = true;
        }

        //Proxy Tunnel

        public Mode Protocol { get; private set; }
        string _host;
        Socket client;
        public bool TunnelDestroyed { get; private set; } = false;

        public bool sslRead = false;

        public enum Mode : int
        {
            HTTP = 1,
            HTTPs = 2
        }

        public Tunnel(Mode protMode, Socket httpClient)
        {
            Protocol = protMode;
            client = httpClient;
        }

        public static void Send(string data, Mode Protocol, Request r = null, NetworkStream targetHttp = null, SslHandler targetHttps = null)
        {
            //ConMod.Debug("Send string");
            BISend(r, targetHttp, targetHttps, Protocol);
        }

        private static void BISend(Request r, NetworkStream ns, SslHandler vSsl, Mode Protocol)
        {
            Task getPage = new Task(new Action(() => {
                string hostString = r.headers["Host"];
                string target = r.target.Replace(hostString, string.Empty);
                if (Protocol == Tunnel.Mode.HTTPs)
                    hostString = "https://" + hostString + target;
                else
                    hostString = "http://" + hostString + target;

                HttpClientHandler handler = new HttpClientHandler() { UseProxy = false, Proxy = null };
                HttpClient client = new HttpClient(handler);
                HttpRequestMessage hrm = new HttpRequestMessage
                {
                    Method = new HttpMethod(r.method),
                    RequestUri = new Uri(hostString)
                };

                foreach (KeyValuePair<string, string> kvp in r.headers.Items)
                {
                    hrm.Headers.Add(kvp.Key, kvp.Value);
                }

                if (r.htmlBody != null) hrm.Content = new StringContent(r.htmlBody);

                client.SendAsync(hrm).ContinueWith(responseTask => {

                    try
                    {
                        HttpResponseMessage resp = responseTask.Result;
                        byte[] content = new byte[0];
                        string strContent = "";
                        int statusCode = 0;
                        string statusDescription = "";
                        string version = "";
                        VDictionary headers = new VDictionary();
                        Task getContent = new Task(() =>
                        {

                            content = resp.Content.ReadAsByteArrayAsync().Result;
                            foreach (KeyValuePair<string, IEnumerable<string>> x in resp.Content.Headers)
                            {
                                string name = x.Key;
                                string value = "";
                                foreach (string val in x.Value)
                                {
                                    value += val + ";";
                                }

                                value = value.Substring(0, value.Length - 1);
                                headers.Add(name, value);
                            }

                            strContent = Encoding.ASCII.GetString(content);

                        });

                        Task getHeaders = new Task(() =>
                        {

                            foreach (KeyValuePair<string, IEnumerable<string>> x in resp.Headers)
                            {
                                string name = x.Key;
                                string value = "";
                                foreach (string val in x.Value)
                                {
                                    value += val + ";";
                                }

                                value = value.Substring(0, value.Length - 1);
                                headers.Add(name, value);
                            }

                        });

                        Task getRest = new Task(() =>
                        {

                            statusCode = (int)resp.StatusCode;
                            statusDescription = resp.ReasonPhrase;
                            version = "HTTP/" + resp.Version.ToString();

                        });

                        getContent.Start();
                        getHeaders.Start();
                        getRest.Start();

                        Task.WaitAll(getContent, getHeaders, getRest);

                        Response _r = new Response(statusCode, statusDescription, version, headers, strContent, content);
                        _r.CheckMimeAndSetBody();
                        if (Protocol == Tunnel.Mode.HTTPs) _r.Deserialize(null, r, vSsl);
                        else _r.Deserialize(ns, r);
                    }
                    catch (Exception)
                    {
                        //ctx.ConMod.Debug("Error: " + ex.ToString() + "\r\nStackTrace:\r\n" + ex.StackTrace);
                        //ctx.ConMod.Debug($"On resource: {r.target}");
                    }

                });

            }));

            getPage.Start();
        }

        public string GetHost()
        {
            return _host;
        }

        public void CreateMinimalTunnel(Request r)
        {
            string host = r.headers["Host"];
            if (r.method == "CONNECT")
            {
                host = host.Replace(":443", string.Empty);
                Protocol = Mode.HTTPs;
                sslRead = true;
                _host = host;
                GenerateVerify();
            }
            else
            {
                sslRead = false;
                Protocol = Mode.HTTP;
                _host = host;
            }
        }

        private void GenerateVerify(Socket clientSocket = null)
        {
            string verifyResponse = "HTTP/1.1 200 OK Tunnel Created\r\nTimestamp: " + DateTime.Now + "\r\nProxy-Agent: ah101\r\n\r\n";
            byte[] resp = Encoding.ASCII.GetBytes(verifyResponse);
            if (clientSocket != null)
            {
                clientSocket.Send(resp, 0, resp.Length, SocketFlags.None);
                return;
            }
            //console.Debug("verify request sent!");
        }

        public string FormatRequest(Request r)
        {
            if (TunnelDestroyed) return null;

            if (_host == null)
            {
                Generate404();
                return null;
            }
            string toSend = r.Deserialize();
            List<String> lines = toSend.Split('\n').ToList();
            lines[0] = lines[0].Replace("http://", String.Empty);
            lines[0] = lines[0].Replace("https://", String.Empty);
            lines[0] = lines[0].Replace(_host, String.Empty);
            toSend = "";
            foreach (string line in lines)
            {
                toSend += line + "\n";
            }

            return toSend;
        }

        private void Generate404()
        {
            string text = "HTTP/1.1 404 Not Found\r\nTimestamp: " + DateTime.Now + "\r\nProxy-Agent: ah101\r\n\r\n";
            byte[] buf = Encoding.ASCII.GetBytes(text);
            client.Send(buf, 0, buf.Length, SocketFlags.None);
        }

        private struct RawObj
        {
            public byte[] data;
            public Socket client;
            public Socket bridge;
        }

        private struct RawSSLObj
        {
            public RawObj rawData;
            public Request request;
            public string fullText;
        }

        private void ForwardRawHTTP(IAsyncResult ar)
        {
            try
            {
                RawObj data = (RawObj)ar.AsyncState;
                if (data.client == null || data.bridge == null) return;
                int bytesRead = data.bridge.EndReceive(ar);
                if (bytesRead > 0)
                {
                    byte[] toSend = new byte[bytesRead];
                    Array.Copy(data.data, toSend, bytesRead);
                    data.client.Send(toSend, 0, bytesRead, SocketFlags.None);
                    Array.Clear(toSend, 0, bytesRead);
                }
                else
                {
                    if (data.client != null)
                    {
                        data.client.Close();
                        data.client.Dispose();
                        data.client = null;
                    }
                    if (data.bridge != null)
                    {
                        data.bridge.Close();
                        data.bridge.Dispose();
                        data.bridge = null;
                    }
                    return;
                }
                data.data = new byte[2048];
                data.bridge.BeginReceive(data.data, 0, 2048, SocketFlags.None, new AsyncCallback(ForwardRawHTTP), data);
            }
            catch (Exception)
            {
                //console.Debug($"Forawrd RAW HTTP failed: {ex.ToString()}");
            }
        }

        private IPAddress GetIPOfHost(string hostname)
        {
            if (!IPAddress.TryParse(hostname, out IPAddress address))
            {
                IPAddress[] ips = Dns.GetHostAddresses(hostname);
                return (ips.Length > 0) ? ips[0] : null;
            }
            else return address;
        }

        public void SendHTTP(Request r, Socket browser)
        {
            try
            {
                string code = FormatRequest(r);
                Socket bridge = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPAddress ip = GetIPOfHost(r.headers["Host"]);
                if (ip == null)
                {
                    if (browser != null)
                    {
                        browser.Close();
                        browser.Dispose();
                        browser = null;
                    }

                    return;
                }
                bridge.Connect(ip, 80);
                RawObj ro = new RawObj() { client = browser, data = new byte[2048], bridge = bridge };
                bridge.BeginReceive(ro.data, 0, 2048, SocketFlags.None, new AsyncCallback(ForwardRawHTTP), ro);
                bridge.Send(Encoding.ASCII.GetBytes(code));
            }
            catch (SocketException socketError)
            {
                Console.WriteLine($"[-] Failed to tunnel http traffic for {r.headers["Host"]}: '{socketError.Message}'.");
            }
        }

        private void ReadBrowser(IAsyncResult ar)
        {
            try
            {
                RawSSLObj rso = (RawSSLObj)ar.AsyncState;
                if (rso.rawData.client == null || rso.rawData.bridge == null) return;
                int bytesRead = rso.rawData.client.EndReceive(ar);
                if (bytesRead > 0)
                {
                    byte[] req = new byte[bytesRead];
                    Array.Copy(rso.rawData.data, req, bytesRead);
                    rso.rawData.bridge.Send(req, 0, bytesRead, SocketFlags.None);
                    Array.Clear(req, 0, bytesRead);
                }
                else
                {
                    if (rso.rawData.client != null)
                    {
                        rso.rawData.client.Close();
                        rso.rawData.client.Dispose();
                        rso.rawData.client = null;
                    }
                    if (rso.rawData.bridge != null)
                    {
                        rso.rawData.bridge.Close();
                        rso.rawData.bridge.Dispose();
                        rso.rawData.bridge = null;
                    }
                    return;
                }

                rso.rawData.data = new byte[2048];
                rso.rawData.client.BeginReceive(rso.rawData.data, 0, 2048, SocketFlags.None, new AsyncCallback(ReadBrowser), rso);
            }
            catch (Exception)
            {
                //console.Debug($"Failed to read raw http from browser: {ex.ToString()}");
            }
        }

        public void InitHTTPS(Socket browser)
        {
            try
            {
                Socket bridge = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPAddress ip = GetIPOfHost(_host);
                if (ip == null)
                {
                    if (browser != null)
                    {
                        browser.Close();
                        browser.Dispose();
                        browser = null;
                    }

                    return;
                }
                bridge.Connect(ip, 443);
                RawSSLObj rso = new RawSSLObj() { fullText = "", request = null, rawData = new RawObj { data = new byte[2048], client = browser, bridge = bridge } };
                RawObj ro = new RawObj() { data = new byte[2048], bridge = bridge, client = browser };
                bridge.BeginReceive(ro.data, 0, 2048, SocketFlags.None, new AsyncCallback(ForwardRawHTTP), ro);
                browser.BeginReceive(rso.rawData.data, 0, 2048, SocketFlags.None, new AsyncCallback(ReadBrowser), rso);
                GenerateVerify(browser);
            }
            catch (SocketException socketError)
            {
                Console.WriteLine($"[-] Failed to create http tunnel: '{socketError.Message}'.");
            }
        }
    }
}
