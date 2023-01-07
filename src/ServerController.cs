using Sisk.Core.Http;
using System.Runtime;

namespace Sisk.Agirax
{
    internal class ServerController
    {
        static void Server_OnConnectionOpen(object sender, HttpRequest request)
        {
            Program.totalConnectionsOpen++;
        }

        static void Server_OnConnectionClose(object sender, HttpServerExecutionResult e)
        {
            Program.totalSent += e.ResponseSize;
            Program.totalReceived += e.RequestSize;
            if (e.IsSuccessStatus)
            {
                Program.totalConnectionsClosedOk++;
            }
            else
            {
                Program.totalConnectionsClosedErr++;
            }
            if (e.Response != null)
            {
                int statusCode = ((int)e.Response.Status) / 100;
                if (statusCode == 2) Program.total2xxRes++;
                if (statusCode == 3) Program.total3xxRes++;
                if (statusCode == 4) Program.total4xxRes++;
                if (statusCode == 5) Program.total5xxRes++;
            }
        }

        internal static void Start()
        {
            Program.configuration.ListeningHosts = Program.lhRepository;

            Program.server = new HttpServer(Program.configuration);
            Program.server.OnConnectionOpen += Server_OnConnectionOpen;
            Program.server.OnConnectionClose += Server_OnConnectionClose;
            Program.server.Start();
        }
    }
}
