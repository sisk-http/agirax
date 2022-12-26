using Sisk.Agirax.RequestHandlerParser;
using Sisk.Core.Http;
using Sisk.Core.Routing.Handlers;
using System.Diagnostics;
using System.Reflection;
using System.Xml;


namespace Sisk.Agirax
{
    internal class Program
    {
        public const string VERSION = "v.0.5-alpha-4";
        internal static List<int> extProcesses = new List<int>();
        internal static List<ServerCache> usingCaches = new List<ServerCache>();
        internal static readonly string assemblyPath = Assembly.GetExecutingAssembly().Location;
        internal static string currentRelativePath = Directory.GetCurrentDirectory();
        internal static bool waitForExit = false;
        private static long TotalConnectionsOpen = 0;
        private static long TotalConnectionsClosedOk = 0;
        private static long TotalConnectionsClosedErr = 0;
        private static bool startedWithExtConfig = false;
        private static bool delayedStart = false;
        private static HttpServer? server = null!;
        private static HttpServerConfiguration configuration = new HttpServerConfiguration();
        private static Process serverProcess = Process.GetCurrentProcess();

        static void Main(string[] args)
        {
            Cli.Log("Initializing...");

            // initialize main properties
            DateTime startDate = DateTime.Now;
            string inputFile;

            waitForExit = args.Contains("/wait");
            delayedStart = args.Contains("/delay-start");

            if (delayedStart)
            {
                Thread.Sleep(1500);
            }

            if (args.Length > 0)
            {
                inputFile = args[0];

                if (!File.Exists(inputFile))
                {
                    Cli.TerminateWithError("Cannot open configuration file: input file was not found.");
                }

                currentRelativePath = Path.GetDirectoryName(inputFile)!;
                startedWithExtConfig = true;
            }
            else
            {
                inputFile = "config.xml";
            }

            string fullInputFilePath = Path.GetFullPath(inputFile);
            string xmlContents = System.IO.File.ReadAllText(fullInputFilePath);
            XmlDocument xDoc = new XmlDocument();

            try
            {
                xDoc.LoadXml(xmlContents);
            }
            catch (XmlException xmlExp)
            {
                Cli.TerminateWithError($"Cannot parse the input XML file at line {xmlExp.LineNumber}:{xmlExp.LinePosition}: {xmlExp.Message}");
            }

            ListeningHost[] hosts = new ConfigParser(configuration).ParseConfiguration(xDoc);

            if (hosts.Length == 0)
            {
                Cli.TerminateWithError($"Cannot initialize Agirax: no listening hosts were loaded.");
                return;
            }

            configuration.ListeningHosts = hosts.ToArray();

            server = new HttpServer(configuration);
            server.OnConnectionOpen += Server_OnConnectionOpen;
            server.OnConnectionClose += Server_OnConnectionClose;
            server.Start();

            Console.Clear();

#if RELEASE
            Console.WriteLine($"Agirax ({VERSION} [REL]) powered by Sisk ({server.GetVersion()})");
#else
            Console.WriteLine($"Agirax ({VERSION} [DEBUG SESSION]) powered by Sisk ({server.GetVersion()})");
#endif
            Console.WriteLine("Licensed under Apache License 2.0");
            Console.WriteLine("Visit: https://github.com/CypherPotato/Sisk");
            Console.WriteLine();

            foreach (ListeningHost listeningHost in configuration.ListeningHosts)
            {
                string name = listeningHost.Label ?? listeningHost.Hostname;
                foreach (ListeningPort port in listeningHost.Ports)
                {
                    string portString = "";
                    if (port.Port != 80 && port.Port != 443)
                        portString = $":{port.Port}";

                    Console.WriteLine($"  {name,-40} => {(port.Secure ? "https" : "http")}://{listeningHost.Hostname}{portString}/");
                    name = "";
                }
                if (listeningHost.Router.GlobalRequestHandlers != null)
                    foreach (IRequestHandler handler in listeningHost.Router.GlobalRequestHandlers)
                    {
                        Console.WriteLine($"{new string(' ', 46)}+ {handler.GetType().Name}");
                    }
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine();
            Console.WriteLine("     [TAB]         Show statistics information");
            Console.WriteLine("     [BACKSPACE]   Clear the window");
            Console.WriteLine("     [ENTER]       Reload the server");
            Console.WriteLine("     [ESC]         Stop the server");
            Console.WriteLine();

            int restartRequested = 0;
            ConsoleKeyInfo key;

            do
            {
                key = Console.ReadKey();
                if (key.Key == ConsoleKey.Enter)
                {
                    restartRequested = 1;
                }
                else if (key.Key == ConsoleKey.X)
                {
                    restartRequested = 2;
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    Console.Clear();
                }
                else if (key.Key == ConsoleKey.Tab)
                {
                    var i = (DateTime.Now - startDate);
                    Console.WriteLine();
                    Console.WriteLine("Statistics:");
                    Console.WriteLine($"  {"Running for",-30} : {i.Days} days, {i.Hours}h, {i.Minutes}m and {i.Seconds}s");
                    Console.WriteLine($"  {"Start date",-30} : {startDate}");
                    Console.WriteLine($"  {"Openned connections",-30} : {TotalConnectionsOpen}");
                    Console.WriteLine($"  {"Closed OK connections",-30} : {TotalConnectionsClosedOk}");
                    Console.WriteLine($"  {"Error connections",-30} : {TotalConnectionsClosedErr}");
                    Console.WriteLine($"  {"Memory usage",-30} : {Util.IntToHumanSize(serverProcess.WorkingSet64)}");
                    Console.WriteLine($"  {"Peak Memory usage",-30} : {Util.IntToHumanSize(serverProcess.PeakWorkingSet64)}");
                    Console.WriteLine("Server caches:");
                    foreach (ServerCache cache in usingCaches)
                    {
                        Console.WriteLine($"  {cache.Authority,-30} : {Util.IntToHumanSize(cache.UsedSize)}/{Util.IntToHumanSize(cache.MaxHeapSize)}");
                    }

                }
            } while (key.Key != ConsoleKey.Escape && restartRequested <= 0);

            Console.WriteLine("Terminating...");
            if (restartRequested == 1)
            {
                string cmd = $"{Util.EncodeParameterArgument(assemblyPath)} ";

                if (!startedWithExtConfig)
                {
                    cmd += "config.xml ";
                }

                foreach (string arg in args)
                    cmd += Util.EncodeParameterArgument(arg) + " ";

                cmd += "/delay-start";

                Process.Start("dotnet", cmd);
            }
            DisposeResources();
        }

        static void DisposeResources()
        {
            if (server != null)
            {
                server.Dispose();
            }

            TotalConnectionsOpen = 0;
            TotalConnectionsClosedErr = 0;
            TotalConnectionsClosedOk = 0;

            extProcesses.Clear();
            usingCaches.Clear();
            Environment.Exit(0);
        }

        static void Server_OnConnectionOpen(object sender, HttpRequest request)
        {
            TotalConnectionsOpen++;
        }

        static void Server_OnConnectionClose(object sender, HttpServerExecutionResult e)
        {
            if (e.Status == HttpServerExecutionStatus.Executed)
            {
                TotalConnectionsClosedOk++;
            }
            else
            {
                TotalConnectionsClosedErr++;
            }
        }
    }
}