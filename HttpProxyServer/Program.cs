using System;

namespace MyProxyServer
{
    class Program
    {
        static void Main()
        {
            ProxyServer proxyServer = new ProxyServer("0.0.0.0", 8888, 100);
            proxyServer.Start();
            Console.ReadKey();
            proxyServer.Stop();
        }
    }
}
