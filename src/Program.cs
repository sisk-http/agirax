using Sisk.Agirax.RequestHandlerParser;
using Sisk.Core.Entity;
using Sisk.Core.Http;
using Sisk.Core.Routing;
using Sisk.Core.Routing.Handlers;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Xml;


namespace Sisk.Agirax
{
    internal class Program
    {
        static long TotalConnectionsOpen = 0;
        static long TotalConnectionsClosedOk = 0;
        static long TotalConnectionsClosedErr = 0;
        static Process serverProcess = null!;
        static bool wait = false;
        static bool startedWithExtConfig = false;
        static HttpServerConfiguration? configuration = null!;
        static HttpServer? server = null!;
        internal static List<int> extProcesses = new List<int>();
        internal static List<ServerCache> usingCaches = new List<ServerCache>();
        internal static readonly string assemblyPath = Assembly.GetExecutingAssembly().Location;

        static T? TryParse<T>(object? data) where T : IParsable<T>
        {
            T.TryParse(data?.ToString(), null, out T? result);
            return result;
        }

        static void Terminate()
        {
            if (wait)
            {
                Console.WriteLine("\nPress any key to exit. . .");
                Console.ReadKey();
            }
            Environment.Exit(0);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Initializing...");
            DateTime startDate = DateTime.Now;
            string relativePath = Directory.GetCurrentDirectory();
            string xmlFile;

            wait = args.Contains("/wait");
            bool delayedStart = args.Contains("/delay-start");

            if (delayedStart)
            {
                Thread.Sleep(1500);
            }

            if (args.Length > 0)
            {
                xmlFile = args[0];
                relativePath = Path.GetDirectoryName(xmlFile)!;
                startedWithExtConfig = true;
            }
            else
            {
                xmlFile = "config.xml";
            }

            string xmlContents = System.IO.File.ReadAllText(xmlFile);
            XmlDocument xDoc = new XmlDocument();
            xDoc.LoadXml(xmlContents);

            serverProcess = Process.GetCurrentProcess();

            configuration = new HttpServerConfiguration();

            string? defaultEncodingXmlVal = xDoc.SelectSingleNode("//Agirax/ServerConfiguration/DefaultEncoding")?.InnerText;
            if (defaultEncodingXmlVal != null)
            {
                configuration.DefaultEncoding = Encoding.GetEncoding(defaultEncodingXmlVal);
            }
            else
            {
                configuration.DefaultEncoding = Encoding.UTF8;
            }

            string? MaximumContentLengthXmlVal = xDoc.SelectSingleNode("//Agirax/ServerConfiguration/MaximumContentLength")?.InnerText;
            if (MaximumContentLengthXmlVal != null)
            {
                configuration.MaximumContentLength = Int64.Parse(MaximumContentLengthXmlVal);
            }

            string? verboseXmlVal = xDoc.SelectSingleNode("//Agirax/ServerConfiguration/Verbose")?.InnerText;
            if (verboseXmlVal != null)
            {
                configuration.Verbose = Enum.Parse<VerboseMode>(verboseXmlVal, true);
            }

            XmlNodeList listeningHostsList = xDoc.SelectNodes("//Agirax/ListeningHosts/ListeningHost")!;
            List<ListeningHost> hosts = new List<ListeningHost>();

            for (int i = 0; i < listeningHostsList.Count; i++)
            {
                XmlNode listeningHostNode = listeningHostsList[i]!;
                try
                {
                    ListeningPort[] ports;

                    string? portRaw = listeningHostNode.Attributes!["Port"]?.Value;
                    string hostname = listeningHostNode.Attributes!["Hostname"]?.Value ?? throw new Exception("The listening host name is required.");
                    string? name = listeningHostNode.Attributes!["Name"]?.Value;

                    Console.WriteLine($"Setting up host {hostname}...");

                    if (portRaw != null)
                    {
                        bool ok = Int32.TryParse(portRaw, null, out int port);
                        if (!ok)
                        {
                            throw new Exception("The specified port is invalid.");
                        }
                        ports = new ListeningPort[] { new ListeningPort(port) };
                    }
                    else
                    {
                        XmlNodeList endpoints = listeningHostNode.SelectNodes("Endpoints/Endpoint")!;
                        ports = new ListeningPort[endpoints.Count];

                        for (int j = 0; j < endpoints.Count; j++)
                        {
                            XmlNode endpointNode = endpoints[j]!;

                            bool portOk = Int32.TryParse(endpointNode.Attributes!["Port"]?.Value, null, out int port);
                            bool secureOk = bool.TryParse(endpointNode.Attributes!["Secure"]?.Value, out bool secure);
                            if (!portOk || !secureOk)
                            {
                                throw new Exception("The specified port or secure option is invalid.");
                            }

                            ports[j] = new ListeningPort(port, secure);
                        }
                    }

                    CrossOriginResourceSharingHeaders corsPolicy = CrossOriginResourceSharingHeaders.Empty;

                    List<IRequestHandler> handlers = new List<IRequestHandler>();
                    Router resultRouter;

                    XmlNodeList requestHandlerNodes = listeningHostNode.SelectNodes("RequestHandlers/RequestHandler")!;
                    XmlNode? routerNode = listeningHostNode.SelectSingleNode("Router");
                    XmlNode? crossOriginResourceSharingPolicyNode = listeningHostNode.SelectSingleNode("CrossOriginResourceSharingPolicy");

                    if (requestHandlerNodes.Count > 0)
                    {
                        for (int j = 0; j < requestHandlerNodes.Count; j++)
                        {
                            XmlNode requestHandlerNode = requestHandlerNodes[j]!;
                            RequestHandlerType rHandlerType = Enum.Parse<RequestHandlerType>(requestHandlerNode.Attributes!["Type"]!.Value, true);
                            switch (rHandlerType)
                            {
                                case RequestHandlerType.RewriteHost:
                                    string @in = requestHandlerNode.Attributes!["In"]!.Value;
                                    string @out = requestHandlerNode.Attributes!["Out"]!.Value;

                                    handlers.Add(new RequestHandlerParser.RewriteHost(@in, @out));
                                    break;

                                case RequestHandlerType.RewriteHttps:
                                    handlers.Add(new RequestHandlerParser.RewriteHttps());
                                    break;

                                case RequestHandlerType.Cache:
                                    long itemSize = Util.ParseSizeString(requestHandlerNode.Attributes!["MaxItemSize"]?.Value) ?? 2048;
                                    long heapSize = Util.ParseSizeString(requestHandlerNode.Attributes!["HeapSize"]?.Value) ?? (16 * 1024 * 1024);

                                    ServerCache cache = new ServerCache()
                                    {
                                        IndividualMaxSize = itemSize,
                                        MaxHeapSize = heapSize,
                                        Hostname = hostname
                                    };
                                    StoreServerCache storeCache = new StoreServerCache(cache);
                                    handlers.Add(cache);
                                    handlers.Add(storeCache);

                                    usingCaches.Add(cache);

                                    break;

                                default:
                                    throw new Exception("Invalid request handler type.");
                            }
                        }
                    }

                    if (routerNode is null)
                    {
                        throw new Exception($"The listening host requires at least one router.");
                    }

                    if (crossOriginResourceSharingPolicyNode is not null)
                    {
                        CrossOriginResourceSharingHeaders h = new CrossOriginResourceSharingHeaders();
                        h.AllowHeaders = crossOriginResourceSharingPolicyNode.SelectSingleNode("AllowHeaders")?.InnerText.Split(',') ?? new string[0];
                        h.AllowMethods = crossOriginResourceSharingPolicyNode.SelectSingleNode("AllowMethods")?.InnerText.Split(',') ?? new string[0];
                        h.AllowOrigins = crossOriginResourceSharingPolicyNode.SelectSingleNode("AllowOrigins")?.InnerText.Split(',') ?? new string[0];
                        h.MaxAge = TimeSpan.FromSeconds(TryParse<int>(crossOriginResourceSharingPolicyNode.SelectSingleNode("MaxAge")?.InnerText.Trim()));
                        corsPolicy = h;
                    }

                    RouterType routerType = Enum.Parse<RouterType>(routerNode.Attributes!["Type"]!.Value, true);
                    switch (routerType)
                    {
                        case RouterType.Empty:
                            resultRouter = new RouterParser.EmptyRouterGenerator().CreateRouterFromNode(routerNode);
                            break;

                        case RouterType.Module:
                            resultRouter = new RouterParser.ModuleRouterGenerator(relativePath).CreateRouterFromNode(routerNode);
                            break;

                        case RouterType.Static:
                            {
                                string rootDirectory = routerNode.Attributes!["RootDirectory"]!.Value;
                                string indexFile = routerNode.Attributes!["Index"]!.Value;
                                resultRouter = new RouterParser.StaticRouterGenerator(rootDirectory, indexFile).CreateRouterFromNode(routerNode);
                            }
                            break;

                        case RouterType.PhpCgi:
                            {
                                string rootDirectory = routerNode.Attributes!["RootDirectory"]!.Value;
                                string? indexFile = routerNode.Attributes["Index"]?.Value;
                                string? phpCgiExecuteable = routerNode.Attributes["CgiPath"]?.Value;
                                bool redirectToIndex = bool.Parse(routerNode.Attributes["RedirectIndex"]?.Value ?? "false");
                                resultRouter = new RouterParser.PhpCgiRouterGenerator(rootDirectory, indexFile, redirectToIndex, phpCgiExecuteable).CreateRouterFromNode(routerNode);
                            }
                            break;

                        case RouterType.PhpNginxProxy:
                            {
                                string rootDirectory = routerNode.Attributes!["RootDirectory"]!.Value;
                                string? indexFile = routerNode.Attributes["Index"]?.Value;
                                //int outboundingPort = Int32.Parse(routerNode.Attributes!["OutPort"]!.Value);
                                string? nginxPath = routerNode.Attributes["NginxPath"]?.Value;
                                bool redirectToIndex = bool.Parse(routerNode.Attributes["RedirectIndex"]?.Value ?? "false");
                                resultRouter =
                                    new RouterParser.PhpFastcgiProxyRouterGenerator(rootDirectory, indexFile, redirectToIndex, nginxPath)
                                    .CreateRouterFromNode(routerNode);
                            }
                            break;

                        default:
                            throw new Exception("Invalid router type.");
                    }

                    if (handlers.Count > 0)
                    {
                        resultRouter.GlobalRequestHandlers = handlers.ToArray();
                    }

                    hosts.Add(new ListeningHost(hostname, ports, resultRouter)
                    {
                        Label = name,
                        CrossOriginResourceSharingPolicy = corsPolicy
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: an error was thrown while parsing the listening host \"{listeningHostNode.Attributes!["Name"]?.Value ?? "(unknown)"}\": {ex.Message}");
                }
            }

            if (hosts.Count == 0)
            {
                Console.WriteLine($"Warning: no listening hosts were loaded.");
                Terminate();
                return;
            }
            else
            {
                Console.WriteLine($"Initializing server...");
            }

            configuration.ListeningHosts = hosts.ToArray();

            server = new HttpServer(configuration);
            server.OnConnectionOpen += Server_OnConnectionOpen;
            server.OnConnectionClose += Server_OnConnectionClose;
            server.Start();

            Console.Clear();

#if RELEASE
            Console.WriteLine($"Agirax (v. 1.0-beta01 [REL]) powered by Sisk ({server.GetVersion()})");
#else
            Console.WriteLine($"Agirax (v. 1.0-beta01 [DEBUG SESSION]) powered by Sisk ({server.GetVersion()})");
#endif
            Console.WriteLine("Licensed under MIT license");
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
                        Console.WriteLine($"  {cache.Hostname,-30} : {Util.IntToHumanSize(cache.UsedSize)}/{Util.IntToHumanSize(cache.MaxHeapSize)}");
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