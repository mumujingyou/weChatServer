using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeChatPayServer
{
    class Program
    {
        static void Main(string[] args)
        {
            TCPServer tcpServer = new TCPServer();
            tcpServer.Start();

            Console.ReadLine();
        }
    }
}
