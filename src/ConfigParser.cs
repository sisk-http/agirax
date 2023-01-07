using Sisk.Agirax.RequestHandlerParser;
using Sisk.Core.Entity;
using Sisk.Core.Http;
using Sisk.Core.Routing;
using Sisk.Core.Routing.Handlers;
using System.Collections.Specialized;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Sisk.Agirax
{
    internal static class ConfigParser
    {
        public static void ParseConfiguration((XmlDocument xDoc, string xDocFilePath) input)
        {
            Program.configuration = new HttpServerConfiguration();
            if (!Path.IsPathRooted(input.xDocFilePath))
                input.xDocFilePath = Path.Combine(Program.currentRelativePath, input.xDocFilePath);

            {
                string? defaultEncodingXmlVal = Util.GetXmlInnerText(input.xDoc.SelectSingleNode("//Agirax/ServerConfiguration/DefaultEncoding"));
                try
                {
                    if (defaultEncodingXmlVal != null)
                    {
                        Program.configuration.DefaultEncoding = Encoding.GetEncoding(defaultEncodingXmlVal);
                        Cli.Log($"Property DefaultEncoding = \"{Program.configuration.DefaultEncoding}\"");
                    }
                    else
                    {
                        Program.configuration.DefaultEncoding = Encoding.UTF8;
                    }
                }
                catch (Exception)
                {
                    Cli.TerminateWithError("Cannot parse the specified encoding: " + defaultEncodingXmlVal);
                }
            }

            {
                string? accessLogXmlVal = Util.GetXmlInnerText(input.xDoc.SelectSingleNode("//Agirax/ServerConfiguration/AccessLogs"));
                try
                {
                    if (accessLogXmlVal != null)
                    {
                        if (accessLogXmlVal == "")
                        {
                            Program.configuration.AccessLogsStream = null;
                            Cli.Log($"Property AccessLogsStream = empty");
                        }
                        else
                        {
                            Program.configuration.AccessLogsStream = new StreamWriter(accessLogXmlVal, true, Program.configuration.DefaultEncoding)
                            {
                                AutoFlush = true
                            };
                            Cli.Log($"Property AccessLogsStream = \"{accessLogXmlVal}\"");
                        }
                    }
                    else
                    {
                        Program.configuration.AccessLogsStream = Console.Out;
                    }
                }
                catch (Exception)
                {
                    Cli.TerminateWithError("Cannot parse AccessLogsStream: " + accessLogXmlVal);
                }
            }

            {
                string? errorLogsXmlVal = Util.GetXmlInnerText(input.xDoc.SelectSingleNode("//Agirax/ServerConfiguration/ErrorLogs"));
                try
                {
                    if (errorLogsXmlVal != null)
                    {
                        Program.configuration.ErrorsLogsStream = new StreamWriter(errorLogsXmlVal, true, Program.configuration.DefaultEncoding)
                        {
                            AutoFlush = true
                        };
                        Cli.Log($"Property ErrorsLogsStream = \"{errorLogsXmlVal}\"");
                    }
                    else
                    {
                        Program.configuration.ErrorsLogsStream = null;
                    }
                }
                catch (Exception)
                {
                    Cli.TerminateWithError("Cannot parse ErrorsLogsStream: " + errorLogsXmlVal);
                }
            }

            {
                string? throwExceptionsXmlVal = input.xDoc.SelectSingleNode("//Agirax/ServerConfiguration/ThrowExceptions")?.InnerText;
                try
                {
                    if (throwExceptionsXmlVal != null)
                    {
                        Program.configuration.ThrowExceptions = bool.Parse(throwExceptionsXmlVal);
                        Cli.Log($"Property ThrowExceptions = \"{Program.configuration.ThrowExceptions}\"");
                    }
                }
                catch (Exception)
                {
                    Cli.TerminateWithError("Cannot parse ThrowExceptions to boolean: " + throwExceptionsXmlVal);
                }
            }

            {
                string? resolveIpaddressXmlVal = input.xDoc.SelectSingleNode("//Agirax/ServerConfiguration/ResolveForwardedOriginAddress")?.InnerText;
                try
                {
                    if (resolveIpaddressXmlVal != null)
                    {
                        Program.configuration.ResolveForwardedOriginAddress = bool.Parse(resolveIpaddressXmlVal);
                        Cli.Log($"Property ResolveForwardedOriginAddress = \"{Program.configuration.ResolveForwardedOriginAddress}\"");
                    }
                }
                catch (Exception)
                {
                    Cli.TerminateWithError("Cannot parse ResolveForwardedOriginAddress to boolean: " + resolveIpaddressXmlVal);
                }
            }

            {
                string? resolveHostXmlVal = input.xDoc.SelectSingleNode("//Agirax/ServerConfiguration/ResolveForwardedOriginHost")?.InnerText;
                try
                {
                    if (resolveHostXmlVal != null)
                    {
                        Program.configuration.ResolveForwardedOriginHost = bool.Parse(resolveHostXmlVal);
                        Cli.Log($"Property ResolveForwardedOriginHost = \"{Program.configuration.ResolveForwardedOriginHost}\"");
                    }
                }
                catch (Exception)
                {
                    Cli.TerminateWithError("Cannot parse ResolveForwardedOriginHost to boolean: " + resolveHostXmlVal);
                }
            }

            {
                string? MaximumContentLengthXmlVal = input.xDoc.SelectSingleNode("//Agirax/ServerConfiguration/MaximumContentLength")?.InnerText;
                try
                {
                    if (MaximumContentLengthXmlVal != null)
                    {
                        Program.configuration.MaximumContentLength = Util.ParseSizeString(MaximumContentLengthXmlVal) ?? 0;
                        Cli.Log($"Property MaximumContentLength = \"{Program.configuration.MaximumContentLength}\"");
                    }
                }
                catch (Exception)
                {
                    Cli.TerminateWithError("Cannot parse the maximum content length: " + MaximumContentLengthXmlVal);
                }
            }

            Cli.Log("");

            // resolve imports
            string parsingNode = "Init";
            string currentDir = Path.GetDirectoryName(input.xDocFilePath)!;

        parseIncludes:

            XmlNodeList includes = input.xDoc.SelectNodes("//Include")!;
            foreach (XmlNode includeNode in includes)
            {
                List<XmlDocument> documentsToImport = new List<XmlDocument>();
                string? filename = Util.GetXmlAttribute(includeNode, "File");
                string? directory = Util.GetXmlAttribute(includeNode, "Directory");

                if (directory != null)
                {
                    if (!Path.IsPathRooted(directory))
                    {
                        currentDir = Path.GetFullPath(directory, currentDir);
                    }
                    else
                    {
                        currentDir = Path.GetFullPath(currentDir);
                    }
                }

                if (filename != null && !Path.IsPathRooted(filename))
                    filename = Path.GetFullPath(filename, currentDir);

                parsingNode = $"Include.{filename ?? directory ?? "(unknown)"}";

                if (File.Exists(filename))
                {
                    string xmlContents = File.ReadAllText(filename);
                    XmlDocument fileDoc = new XmlDocument();
                    fileDoc.LoadXml(xmlContents);
                    documentsToImport.Add(fileDoc);
                }
                else if (Directory.Exists(directory))
                {
                    foreach (string xmlFile in Directory.GetFiles(directory, "*.xml", SearchOption.AllDirectories))
                    {
                        string xmlContents = File.ReadAllText(xmlFile);
                        XmlDocument fileDoc = new XmlDocument();
                        fileDoc.LoadXml(xmlContents);
                        documentsToImport.Add(fileDoc);
                    }
                }
                else
                {
                    Cli.Warn($"Cannot resolve import for {filename ?? directory ?? "(unknown)"}");
                    goto removeAndContinue;
                }

                foreach (XmlDocument doc in documentsToImport)
                {
                    XmlNode fileMainNode = doc.FirstChild!;
                    XmlNode fileCloneNode = input.xDoc.ImportNode(fileMainNode, true);
                    includeNode.ParentNode!.AppendChild(fileCloneNode);
                }

            removeAndContinue:
                includeNode.ParentNode!.RemoveChild(includeNode);
            }

            if (includes.Count > 0)
            {
                goto parseIncludes;
            }

            XmlNodeList listeningHostsList = input.xDoc.SelectNodes("//Agirax/ListeningHosts/ListeningHost")!;

            Program.lhRepository.Clear();
            int lhCount = listeningHostsList.Count;
            Program.lhTaskQueue = new Task[lhCount];

            Cli.Log($"Pre-loading {lhCount} listening hosts...\n");
            for (int i = 0; i < lhCount; i++)
            {
                XmlNode listeningHostNode = listeningHostsList[i]!;
                ListeningHost? lh;
                string? hostname = Util.GetXmlAttribute(listeningHostNode, "Hostname");
                string? name = Util.GetXmlAttribute(listeningHostNode, "Name");

                bool lhEnabledOk = bool.TryParse(Util.GetXmlAttribute(listeningHostNode, "Enabled", ""), out bool lhEnabled);
                if (lhEnabledOk && !lhEnabled)
                {
                    Cli.Log($"Listening host {name ?? hostname ?? "(unknown)"} skipped");
                    Program.lhTaskQueue[i] = Task.Run(() => { });
                    continue;
                }

                try
                {
                    ListeningPort[] ports;
                    XmlNodeList requestHandlerNodes = listeningHostNode.SelectNodes("RequestHandlers/RequestHandler")!;
                    XmlNode? routerNode = listeningHostNode.SelectSingleNode("Router");
                    XmlNode? crossOriginResourceSharingPolicyNode = listeningHostNode.SelectSingleNode("CrossOriginResourceSharingPolicy");
                    CrossOriginResourceSharingHeaders corsPolicy = CrossOriginResourceSharingHeaders.Empty;

                    List<IRequestHandler> handlers = new List<IRequestHandler>();
                    Router resultRouter;

                    parsingNode = "Parse.Header";
                    int? port = Util.GetXmlAttributeAs<int>(listeningHostNode, "Port");


                    if (hostname == null)
                    {
                        Cli.TerminateWithError("The listening host must have an valid hostname.");
                        return;
                    }

                    if (routerNode is null)
                    {
                        Cli.Warn($"The listening host requires at least one router.");
                        continue;
                    }

                    if (crossOriginResourceSharingPolicyNode is not null)
                    {
                        parsingNode = $"Parse.CorsHeaders";
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

                    parsingNode = "Parse.Endpoints";
                    if (port != default(int))
                    {
                        ports = new ListeningPort[] { new ListeningPort((int)port) };
                    }
                    else
                    {
                        XmlNodeList endpoints = listeningHostNode.SelectNodes("Endpoints/Endpoint")!;
                        ports = new ListeningPort[endpoints.Count];

                        for (int j = 0; j < endpoints.Count; j++)
                        {
                            parsingNode = $"Parse.Endpoints.{j}";
                            XmlNode endpointNode = endpoints[j]!;

                            bool portOk = Int32.TryParse(endpointNode.Attributes!["Port"]?.Value, null, out int sPort);
                            bool secureOk = bool.TryParse(endpointNode.Attributes!["Secure"]?.Value, out bool secure);
                            if (!portOk || !secureOk)
                            {
                                Cli.Warn("The specified port or secure option is invalid.");
                            }

                            ports[j] = new ListeningPort(sPort, secure);
                        }
                    }

                    lh = new ListeningHost(hostname, ports)
                    {
                        Label = name,
                        CrossOriginResourceSharingPolicy = corsPolicy
                    };

                    Program.lhRepository.Add(lh);

                    Program.lhTaskQueue[i] = Task.Run(() =>
                    {
                        try
                        {
                            DateTime A = DateTime.Now;
                            if (requestHandlerNodes.Count > 0)
                            {
                                for (int j = 0; j < requestHandlerNodes.Count; j++)
                                {
                                    parsingNode = $"Parse.RequestHandler.Position.{j}";
                                    XmlNode requestHandlerNode = requestHandlerNodes[j]!;

                                    bool rhEnabledOk = bool.TryParse(Util.GetXmlAttribute(requestHandlerNode, "Enabled", ""), out bool rhEnabled);
                                    if (rhEnabledOk && !rhEnabled)
                                    {
                                        continue;
                                    }

                                    string rHandlerTypeStr = Util.GetXmlAttribute(requestHandlerNode, "Type", "(empty)")!;
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
                                                contentTypes.Add(node.InnerText.ToLower());
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

                                        case RequestHandlerType.Authorize:
                                            string user = requestHandlerNode.Attributes!["Username"]!.Value;
                                            string pass = requestHandlerNode.Attributes!["Password"]!.Value;
                                            string? message = requestHandlerNode.Attributes!["Message"]?.Value;
                                            // TODO: implement hashes

                                            Authorize at = new RequestHandlerParser.Authorize();
                                            at.Username = user;
                                            at.Password = pass;
                                            at.Message = message ?? at.Message;

                                            handlers.Add(at);
                                            break;

                                        case RequestHandlerType.Rewrite:
                                            string pattern = requestHandlerNode.Attributes!["Pattern"]!.Value;
                                            string action = requestHandlerNode.Attributes!["Action"]!.Value;
                                            // TODO: implement hashes

                                            Rewrite rw = new RequestHandlerParser.Rewrite(pattern, action);
                                            handlers.Add(rw);
                                            break;

                                        case RequestHandlerType.Module:
                                            string moduleFile = requestHandlerNode.Attributes!["File"]!.Value;
                                            string entrypoint = requestHandlerNode.Attributes!["EntryPoint"]!.Value;

                                            NameValueCollection parameters = new();
                                            foreach (XmlNode routeParameterNode in requestHandlerNode.SelectNodes("Parameter")!)
                                            {
                                                parameters.Add(routeParameterNode.Attributes!["Name"]!.Value, routeParameterNode.InnerText);
                                            }

                                            Assembly application = Assembly.LoadFrom(moduleFile);
                                            Type? entrypointType = application.GetType(entrypoint);

                                            if (entrypointType is null)
                                            {
                                                throw new Exception($"Entry point not found.");
                                            }

                                            parsingNode = $"Parse.RequestHandler.ModuleActivation.{entrypointType.Name}";

                                            RequestHandlerFactory factory = (RequestHandlerFactory)Activator.CreateInstance(entrypointType)!;
                                            factory.Setup(parameters);
                                            IRequestHandler[] list = factory.BuildRequestHandlers();

                                            handlers.AddRange(list);
                                            break;

                                        default:
                                            Cli.Warn($"Invalid request handler type \"{rHandlerTypeStr}\".");
                                            break;
                                    }
                                }
                            }

                            string routerTypeStr = routerNode.Attributes!["Type"]?.Value ?? "(empty)";
                            RouterType routerType = Enum.Parse<RouterType>(routerTypeStr, true);
                            parsingNode = $"Parse.Router";
                            switch (routerType)
                            {
                                case RouterType.Empty:
                                    resultRouter = new RouterParser.EmptyRouterGenerator().GetRouter(routerNode);
                                    break;

                                case RouterType.Module:
                                    parsingNode = $"Parse.Router.ModuleActivation";
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

                                default:
                                    Cli.Warn($"Cannot load the Listening host {name ?? hostname}: invalid router type \"{routerTypeStr}\".");
                                    return;
                            }

                            if (handlers.Count > 0)
                            {
                                resultRouter.GlobalRequestHandlers = handlers.ToArray();
                            }

                            DateTime B = DateTime.Now;
                            lh.Router = resultRouter;
                            Program.lhRouteRelation.Add(lh.Handle.ToString(), routerType.ToString());
                            Cli.Log($"Loaded: {name ?? hostname} after {(B - A).TotalMilliseconds:N0} ms");
                        }
                        catch (Exception ex)
                        {
                            Cli.Warn($"An error was thrown while initializing the listening host \"{name ?? hostname}\" at {parsingNode}: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Cli.Warn($"An error was thrown while parsing the listening host \"{listeningHostNode.Attributes!["Name"]?.Value ?? "(unknown)"}\" at {parsingNode}: {ex.Message}");
                }
            }
        }
    }
}
