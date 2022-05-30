using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net;

namespace MyProxyServer
{
    public class ProxyServer : IDisposable
    {

        //IDisposable implementation

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
                if (started)
                {
                    Stop();
                    server.Dispose();
                }
                ipv4Addr = null;
                clientList = null;
            }

            disposed = true;
        }

        private IPEndPoint CreateEndPoint(string ep_addr)
        {
            IPEndPoint result;
            switch (ep_addr)
            {
                case "loopback":
                    result = new IPEndPoint(IPAddress.Loopback, port);
                    break;
                case "any":
                    result = new IPEndPoint(IPAddress.Any, port);
                    break;
                case "localhost":
                    result = new IPEndPoint(IPAddress.Parse("127.0.0.1"), port);
                    break;
                default:
                    result = new IPEndPoint(IPAddress.Parse(ipv4Addr), port);
                    break;
            }

            return result;
        }

        //Proxy Server

        Socket server;
        string ipv4Addr;
        int port;
        int pclimit;
        List<Socket> clientList = new List<Socket>();
        bool stopping = false;
        bool started = false;

        public bool autoAllow = true;
        public bool autoClean = false;

        struct ReadObj
        {
            public Socket s;
            public byte[] buffer;
            public Request request;
        }

        public ProxyServer(string ipAddress, int portNumber, int pendingLimit)
        {
            ipv4Addr = ipAddress;
            port = portNumber;
            pclimit = pendingLimit;
        }

        //Public methods

        public void Setup(string ipAddress, int portNumber, int pendingLimit)
        {
            ipv4Addr = ipAddress;
            port = portNumber;
            pclimit = pendingLimit;
        }

        public void Start()
        {
            server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint ep = null;
            byte[] buffer = new byte[1024];
            if (ipv4Addr != "") ep = CreateEndPoint(ipv4Addr);
            if (ep != null)
            {
                started = true;
                server.Bind(ep);
                server.Listen(pclimit);
                server.BeginAccept(new AsyncCallback(AcceptClient), null);
            }
        }

        public void Stop()
        {
            stopping = true;

            foreach (Socket s in clientList)
            {
                KillSocket(s, false);
            }

            Console.WriteLine("[+] Client shutdown ok.");

            clientList.Clear();

            if (started)
            {
                if (server.Connected) server.Shutdown(SocketShutdown.Both);
                server.Close();
                server.Dispose();
            }

            Console.WriteLine("[+] Server stopped.");

            stopping = false;
            started = false;
        }

        public void KillSocket(Socket client, bool autoRemove = true)
        {
            if (autoRemove && clientList != null) clientList.Remove(client);

            try
            {
                client.Shutdown(SocketShutdown.Both);
                client.Disconnect(false);
            }
            catch (Exception)
            {
                Console.WriteLine("[-] Graceful killsocket failed!");
            }
            client.Close();
            client.Dispose();
        }

        public void CleanSockets()
        {
            bool result = true;
            foreach (Socket socket in clientList)
            {
                try
                {
                    KillSocket(socket);
                    Console.WriteLine($"[+] Client {GetClientRemoteAddress(socket)} killed.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[-] Client {GetClientRemoteAddress(socket)} kill failed: '{ex.Message}'.");
                    result = false;
                }
            }

            if (result)
            {
                Console.WriteLine("[+] All clients disconnected from server.");
            }
            else
            {
                Console.WriteLine("[-] Some clients failed to disconnect from server!");
            }
            clientList = null;
        }

        //Private methods

        private void AutoClean(object sender, EventArgs e)
        {
            CleanSockets();
        }

        private void AcceptClient(IAsyncResult ar)
        {
            Socket client = null;
            try
            {
                client = server.EndAccept(ar);
            }
            catch (Exception)
            {
                return;
            }

            IPEndPoint clientEndPoint = (IPEndPoint)client.RemoteEndPoint;
            string remoteAddress = clientEndPoint.Address.ToString();
            string remotePort = clientEndPoint.Port.ToString();
            Console.WriteLine($"[.] Client {GetClientRemoteAddress(client)} connected to the server.");

            clientList.Add(client);
            ReadObj obj = new ReadObj
            {
                buffer = new byte[1024],
                s = client
            };
            client.BeginReceive(obj.buffer, 0, obj.buffer.Length, SocketFlags.None, new AsyncCallback(ReadPackets), obj);

            if (!stopping) server.BeginAccept(new AsyncCallback(AcceptClient), null);
        }

        public static string GetClientRemoteAddress(Socket client)
        {
            IPEndPoint clientEndPoint = (IPEndPoint)client.RemoteEndPoint;
            string remoteAddress = clientEndPoint.Address.ToString();
            string remotePort = clientEndPoint.Port.ToString();
            return $"{remoteAddress}:{remotePort}";
        }

        private void ReadPackets(IAsyncResult ar)
        {
            ReadObj obj = (ReadObj)ar.AsyncState;
            Socket client = obj.s;
            byte[] buffer = obj.buffer;
            int read = -1;
            try
            {
                read = client.EndReceive(ar);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[.] Client {GetClientRemoteAddress(client)} disconnected from the server: '{ex.Message}'.");
                KillSocket(client, !stopping);
                return;
            }
            if (read == 0)
            {
                try 
                { 
                    if (client.Connected) 
                        client.BeginReceive(obj.buffer, 0, obj.buffer.Length, SocketFlags.None, new AsyncCallback(ReadPackets), obj); 
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[.] Client {GetClientRemoteAddress(client)} aborted session: '{e.Message}'.");
                    KillSocket(client, !stopping);
                }
                return;
            }

            string text = Encoding.ASCII.GetString(buffer, 0, read);
            Request r;
            bool sslHandlerStarted = false;

            if (obj.request != null)
            {
                if (obj.request.notEnded)
                {
                    string des = obj.request.full;
                    des += text;
                    r = new Request(des);
                }
                else r = new Request(text);
            }
            else r = new Request(text);

            if (!r.notEnded && !r.bogus)
            {
                Tunnel t = new Tunnel(Tunnel.Mode.HTTP, client);
                t.CreateMinimalTunnel(r);
                if (t.sslRead) //Handle HTTPS
                {
                    t.InitHTTPS(client);
                    return;
                }
                else  //Handle HTTP 
                {
                    t.SendHTTP(r, client);
                    return;
                }
            }
            else if (r.notEnded) obj.request = r;
            Array.Clear(buffer, 0, buffer.Length);
            try { if (client.Connected && !sslHandlerStarted) client.BeginReceive(obj.buffer, 0, obj.buffer.Length, SocketFlags.None, new AsyncCallback(ReadPackets), obj); }
            catch (Exception e)
            {
                Console.WriteLine($"[.] Client {GetClientRemoteAddress(client)} aborted session: '{e.Message}'.");
                KillSocket(client, !stopping);
            }
        }
    }
}
