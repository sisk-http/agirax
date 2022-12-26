using Sisk.Agirax.RequestHandlerParser;
using Sisk.Core.Entity;
using Sisk.Core.Http;
using Sisk.Core.Routing;
using Sisk.Core.Routing.Handlers;
using System.Text;
using System.Xml;

namespace Sisk.Agirax
{
    internal class ConfigParser
    {
        private HttpServerConfiguration _configuration;

        public ConfigParser(HttpServerConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public ListeningHost[] ParseConfiguration(XmlDocument xDoc)
        {
            string? defaultEncodingXmlVal = xDoc.SelectSingleNode("//Agirax/ServerConfiguration/DefaultEncoding")?.InnerText;
            try
            {
                if (defaultEncodingXmlVal != null)
                {
                    _configuration.DefaultEncoding = Encoding.GetEncoding(defaultEncodingXmlVal);
                }
                else
                {
                    _configuration.DefaultEncoding = Encoding.UTF8;
                }
            }
            catch (Exception)
            {
                Cli.TerminateWithError("Cannot parse the specified encoding: " + defaultEncodingXmlVal);
            }

            string? MaximumContentLengthXmlVal = xDoc.SelectSingleNode("//Agirax/ServerConfiguration/MaximumContentLength")?.InnerText;
            try
            {
                if (MaximumContentLengthXmlVal != null)
                {
                    _configuration.MaximumContentLength = Int64.Parse(MaximumContentLengthXmlVal);
                }
            }
            catch (Exception)
            {
                Cli.TerminateWithError("Cannot parse the maximum content length: " + MaximumContentLengthXmlVal);
            }


            string? verboseXmlVal = xDoc.SelectSingleNode("//Agirax/ServerConfiguration/Verbose")?.InnerText;
            try
            {
                if (verboseXmlVal != null)
                {
                    _configuration.Verbose = Enum.Parse<VerboseMode>(verboseXmlVal, true);
                }
            }
            catch (Exception)
            {
                Cli.TerminateWithError("Cannot parse the verbose string: " + verboseXmlVal);
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

                    Cli.Log($"Setting up host {hostname}...");

                    if (portRaw != null)
                    {
                        bool ok = Int32.TryParse(portRaw, null, out int port);
                        if (!ok)
                        {
                            Cli.Warn("The specified port is invalid.");
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
                                Cli.Warn("The specified port or secure option is invalid.");
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
                            string rHandlerTypeStr = requestHandlerNode.Attributes!["Type"]?.Value ?? "(empty)";
                            RequestHandlerType rHandlerType = Enum.Parse<RequestHandlerType>(rHandlerTypeStr, true);
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

                                    List<string> contentTypes = new List<string>();
                                    foreach (XmlNode node in requestHandlerNode.SelectNodes("Content-Types/Content-Type")!)
                                    {
                                        contentTypes.Add(node.InnerText);
                                    }

                                    ServerCache cache = new ServerCache()
                                    {
                                        IndividualMaxSize = itemSize,
                                        MaxHeapSize = heapSize,
                                        Authority = hostname,
                                        AllowedContentTypes = contentTypes.ToArray()
                                    };
                                    StoreServerCache storeCache = new StoreServerCache(cache);
                                    handlers.Add(cache);
                                    handlers.Add(storeCache);

                                    Program.usingCaches.Add(cache);

                                    break;

                                default:
                                    Cli.Warn($"Invalid request handler type \"{rHandlerTypeStr}\".");
                                    break;
                            }
                        }
                    }

                    if (routerNode is null)
                    {
                        Cli.Warn($"The listening host requires at least one router.");
                        continue;
                    }

                    if (crossOriginResourceSharingPolicyNode is not null)
                    {
                        CrossOriginResourceSharingHeaders h = new CrossOriginResourceSharingHeaders();
                        h.AllowHeaders = crossOriginResourceSharingPolicyNode.SelectSingleNode("AllowHeaders")?.InnerText.Split(',') ?? new string[0];
                        h.AllowMethods = crossOriginResourceSharingPolicyNode.SelectSingleNode("AllowMethods")?.InnerText.Split(',') ?? new string[0];
                        h.AllowOrigins = crossOriginResourceSharingPolicyNode.SelectSingleNode("AllowOrigins")?.InnerText.Split(',') ?? new string[0];

                        string? maxAgeStr = crossOriginResourceSharingPolicyNode.SelectSingleNode("MaxAge")?.InnerText;
                        if (maxAgeStr != null)
                        {
                            if (Int32.TryParse(maxAgeStr, out int maxAge))
                            {
                                h.MaxAge = TimeSpan.FromSeconds(maxAge);
                            }
                            else
                            {
                                Cli.Warn("Cannot parse CORS max-age value to integer value.");
                            }
                        }

                        corsPolicy = h;
                    }

                    string routerTypeStr = routerNode.Attributes!["Type"]?.Value ?? "(empty)";
                    RouterType routerType = Enum.Parse<RouterType>(routerTypeStr, true);
                    switch (routerType)
                    {
                        case RouterType.Empty:
                            resultRouter = new RouterParser.EmptyRouterGenerator().GetRouter(routerNode);
                            break;

                        case RouterType.Module:
                            resultRouter = new RouterParser.ModuleRouterGenerator(Program.currentRelativePath).GetRouter(routerNode);
                            break;

                        case RouterType.Static:
                            {
                                string rootDirectory = routerNode.Attributes!["RootDirectory"]!.Value;
                                string indexFile = routerNode.Attributes!["Index"]!.Value;
                                resultRouter = new RouterParser.StaticRouterGenerator(rootDirectory, indexFile).GetRouter(routerNode);
                            }
                            break;

                        case RouterType.PhpCgi:
                            {
                                string rootDirectory = routerNode.Attributes!["RootDirectory"]!.Value;
                                string? indexFile = routerNode.Attributes["Index"]?.Value;
                                string? phpCgiExecuteable = routerNode.Attributes["CgiPath"]?.Value;
                                bool redirectToIndex = bool.Parse(routerNode.Attributes["RedirectIndex"]?.Value ?? "false");
                                resultRouter = new RouterParser.PhpCgiRouterGenerator(rootDirectory, indexFile, redirectToIndex, phpCgiExecuteable).GetRouter(routerNode);
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
                                    .GetRouter(routerNode);
                            }
                            break;

                        default:
                            Cli.Warn($"Invalid router type \"{routerTypeStr}\".");
                            continue;
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
                    Cli.Log($"Warning: an error was thrown while parsing the listening host \"{listeningHostNode.Attributes!["Name"]?.Value ?? "(unknown)"}\": {ex.Message}");
                }
            }

            return hosts.ToArray();
        }
    }
}
