using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WeChatPayServer
{
    class TCPServer
    {
        TcpListener tcpListener;
        public void Start() {

            try
            {
                tcpListener = TcpListener.Create(7577);
                tcpListener.Start(500);
                Console.WriteLine("启动服务器了...");
                Accpet();
            }
            catch (Exception e)
            {
                Console.WriteLine($"启动服务器异常:{e.Message}...");
            }
        }

        private async void Accpet()
        {
            try
            {
                TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();
                Agent agent = new Agent(tcpClient);
                Console.WriteLine($"用户连接过来了:{tcpClient.Client.RemoteEndPoint}...");
            }
            catch (Exception e)
            {

                Console.WriteLine($"接受连接异常{e.Message}...");
                return;
            }
            Accpet();

        }
    }
}
