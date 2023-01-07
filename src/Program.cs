using Sisk.Agirax.RequestHandlerParser;
using Sisk.Core.Http;
using Sisk.Core.Routing.Handlers;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Xml;
using static Sisk.Agirax.RequestHandlerParser.ServerCache;

namespace Sisk.Agirax
{
    internal class Program
    {
        public const string VERSION = "v.0.5-alpha-6";
        internal static List<ServerCache> usingCaches = new List<ServerCache>();
        internal static readonly string baseDirectory = System.AppContext.BaseDirectory;
        internal static string currentRelativePath = Directory.GetCurrentDirectory();
        internal static HttpServerConfiguration configuration = new HttpServerConfiguration();
        internal static ListeningHostRepository lhRepository = new ListeningHostRepository();
        internal static NameValueCollection lhRouteRelation = new NameValueCollection();
        internal static Task[] lhTaskQueue = new Task[] { };
        internal static HttpServer? server = null!;
        internal static bool waitForExit = false;
        internal static bool startedWithWarnings = false;
        internal static long totalConnectionsOpen = 0;
        internal static long totalConnectionsClosedOk = 0;
        internal static long totalConnectionsClosedErr = 0;
        internal static long total2xxRes = 0;
        internal static long total3xxRes = 0;
        internal static long total4xxRes = 0;
        internal static long total5xxRes = 0;
        internal static long totalReceived = 0;
        internal static long totalSent = 0;
        private static bool delayedStart = false;
        private static Process serverProcess = Process.GetCurrentProcess();
        private static string[] startArgs = null!;
        private static DateTime startDate = DateTime.Now;

        static void Header()
        {
            Console.Clear();
            Console.ResetColor();

            string rel = "DEBUG";

#if RELEASE
            rel = "REL";
#endif

            Console.WriteLine($"Agirax ({VERSION} [{rel}])");
            Console.WriteLine("Licensed under Apache License 2.0");
            Console.WriteLine("Visit: https://github.com/sisk-http/agirax");
            Console.WriteLine();
        }

        static (XmlDocument, string) ReadConfigurationXml(string[] args)
        {
            string inputFile;

            if (args.Length > 0)
            {
                inputFile = args[0];

                if (!File.Exists(inputFile))
                {
                    Cli.TerminateWithError("Cannot open configuration file: input file was not found.");
                }

                currentRelativePath = Path.GetDirectoryName(inputFile)!;
                Directory.SetCurrentDirectory(currentRelativePath);
            }
            else
            {
                inputFile = "config.xml";

                if (!File.Exists(inputFile))
                {
                    Cli.TerminateWithError("Cannot open configuration file.");
                }
            }

            Cli.Log($"Reading configuration from {Path.GetFileName(inputFile)}");

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

            return (xDoc, inputFile);
        }

        static void LoadConfigurationWrapper()
        {
            var config = ReadConfigurationXml(startArgs);

            DateTime A = DateTime.Now;

            ConfigParser.ParseConfiguration(config);
            ServerController.Start();

            Task.WaitAll(lhTaskQueue);
            DateTime B = DateTime.Now;

            Cli.Log("Done!");
            Cli.Log($"All listening hosts initialized in {(B - A).TotalMilliseconds:N0} ms.");

            int hosts = lhRepository.Count;
            Console.WriteLine();
            Console.WriteLine("Agirax service");
            Console.WriteLine("    Listening hosts");
            for (int i = 0; i < hosts; i++)
            {//
                ListeningHost h = lhRepository[i];
                int ports = h.Ports.Length;
                int handlers = h.Router?.GlobalRequestHandlers?.Length ?? 0;
                bool isLastPort = false;
                bool isLastLh = i == hosts - 1;
                string? routerName = lhRouteRelation[h.Handle.ToString()];

                Console.WriteLine($"    {(isLastLh ? '└' : '├')}─ {h.Label}");

                for (int j = 0; j < ports; j++)
                {
                    isLastPort = (j == ports - 1);
                    ListeningPort p = h.Ports[j];
                    Console.WriteLine($"    {(isLastLh ? " " : "│")}  {(isLastPort ? '└' : '├')}─ {(p.Secure ? "https" : "http")}://{h.Hostname}" +
                        $"{(p.Port == 443 || p.Port == 80 ? "" : $":{p.Port}")}/");
                }
                for (int j = 0; j < handlers; j++)
                {
                    IRequestHandler r = h.Router!.GlobalRequestHandlers![j];
                    Console.WriteLine($"    {(isLastLh ? " " : "│")}     ├─ ↓ {r.GetType().Name}");
                }

                Console.WriteLine($"    {(isLastLh ? " " : "│")}     └─ × {routerName ?? "Failed to start the listening host"}");
                Console.WriteLine($"    {(isLastLh ? " " : "│")}");
            }

            Console.WriteLine("");
            Console.WriteLine("Commands:");
            Console.WriteLine("");
            Console.WriteLine("     [TAB]         Show statistics information");
            Console.WriteLine("     [BACKSPACE]   Clear the window");
            Console.WriteLine("     [ENTER]       Soft reload the server");
            Console.WriteLine("     [ESC]         Stop the server");
            Console.WriteLine("");
        }

        static void Main(string[] args)
        {
            Cli.Log("Initializing...");

            startArgs = args;
            waitForExit = args.Contains("/wait");
            delayedStart = args.Contains("/kpid");

            if (delayedStart)
            {
                int delayedPid = Int32.Parse(args[Array.IndexOf(args, "/kpid") + 1]);
                Process p = Process.GetProcessById(delayedPid);
                p.WaitForExit();
            }

            LoadConfigurationWrapper();
            ConsoleKeyInfo key;

            do
            {
                key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    server?.Dispose();
                    configuration.Dispose();
                    usingCaches.Clear();
                    lhRepository.Clear();
                    lhRouteRelation.Clear();
                    lhTaskQueue = new Task[] { };
                    total2xxRes = 0;
                    total3xxRes = 0;
                    total4xxRes = 0;
                    total5xxRes = 0;
                    totalReceived = 0;
                    totalSent = 0;
                    totalConnectionsOpen = 0;
                    totalConnectionsClosedOk = 0;
                    totalConnectionsClosedErr = 0;
                    LoadConfigurationWrapper();
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    Console.Clear();
                }
                else if (key.Key == ConsoleKey.Tab)
                {
                    var i = (DateTime.Now - startDate);
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.WriteLine($"Statistics:");
                    Console.WriteLine($"  ├─ Server statistics");
                    Console.WriteLine($"  │  ├─ {"Running for",-30} : {i.Days} days, {i.Hours}h, {i.Minutes}m and {i.Seconds}s");
                    Console.WriteLine($"  │  ├─ {"Start date",-30} : {startDate}");
                    Console.WriteLine($"  │  ├─ {"Openned connections",-30} : {totalConnectionsOpen}");
                    Console.WriteLine($"  │  ├─ {"Closed OK connections",-30} : {totalConnectionsClosedOk}");
                    Console.WriteLine($"  │  ├─ {"Error connections",-30} : {totalConnectionsClosedErr}");
                    Console.WriteLine($"  │  ├─ {"Received bytes",-30} : {Util.IntToHumanSize(totalReceived)}");
                    Console.WriteLine($"  │  ├─ {"Sent bytes",-30} : {Util.IntToHumanSize(totalSent)}");
                    Console.WriteLine($"  │  ├─ {"Memory usage",-30} : {Util.IntToHumanSize(serverProcess.WorkingSet64)}");
                    Console.WriteLine($"  │  └─ {"Peak Memory usage",-30} : {Util.IntToHumanSize(serverProcess.PeakWorkingSet64)}");
                    Console.WriteLine($"  │");
                    Console.WriteLine($"  ├─ Responses by Status codes");
                    Console.WriteLine($"  │  ├─ {"2xx responses",-30} : {total2xxRes}");
                    Console.WriteLine($"  │  ├─ {"3xx responses",-30} : {total3xxRes}");
                    Console.WriteLine($"  │  ├─ {"4xx responses",-30} : {total4xxRes}");
                    Console.WriteLine($"  │  └─ {"5xx responses",-30} : {total5xxRes}");
                    Console.WriteLine($"  │");
                    Console.WriteLine($"  └─ Server caches:");
                    int totalCaches = usingCaches.Count;
                    if (totalCaches == 0)
                    {
                        Console.WriteLine($"     └─ There's no server cache enabled.");
                    }
                    else
                    {
                        for (int b = 0; b < totalCaches; b++)
                        {
                            ServerCache cache = usingCaches[b];
                            Console.WriteLine($"     {(b == totalCaches - 1 ? '└' : '├')}─ {cache.Authority,-30} : {Util.IntToHumanSize(cache.UsedSize)}/{Util.IntToHumanSize(cache.MaxHeapSize)}");
                        }
                    }
                    Console.WriteLine();
                }
            } while (key.Key != ConsoleKey.Escape);
        }
    }
}