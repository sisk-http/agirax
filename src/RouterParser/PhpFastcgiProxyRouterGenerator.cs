using Sisk.Core.Entity;
using Sisk.Core.Http;
using Sisk.Core.Routing;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Xml;

namespace Sisk.Agirax.RouterParser
{
    internal class PhpFastcgiProxyRouterGenerator : IRouterGenerator
    {
        public string RootDirectory { get; set; }
        public string IndexFile { get; set; } = "index.php";
        public bool RedirectAllToIndex { get; set; } = false;

        int _nginxPort = GetAvailablePort();
        int _fpmPort = GetAvailablePort();

        string[] notRedirectedHeaders = new[]
            {
                "Transfer-Encoding",
                "Connection"
            };

        static readonly IPEndPoint DefaultLoopbackEndpoint = new IPEndPoint(IPAddress.Loopback, 0);
        static int GetAvailablePort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.Bind(DefaultLoopbackEndpoint);
                return ((IPEndPoint)socket.LocalEndPoint!).Port;
            }
        }

        Process NginxProcess { get; set; }
        Process FpmProcess { get; set; }

        public PhpFastcgiProxyRouterGenerator(string rootDirectory, string? indexFile, bool redirectAllToIndex, string? nginxExePath)
        {
            RootDirectory = Path.GetFullPath(rootDirectory).Replace("\\", "/");
            RedirectAllToIndex = redirectAllToIndex;

            if (indexFile != null)
                IndexFile = indexFile;

            string nginxExecutablePath = "";
            string nginxWorkingDirectory = "";

            if (nginxExePath != null)
            {
                if (Path.IsPathFullyQualified(nginxExePath))
                {
                    nginxExecutablePath = nginxExePath;
                    nginxWorkingDirectory = Path.GetDirectoryName(nginxExePath)!;
                }
                else
                {
                    nginxExecutablePath = Path.GetFullPath(nginxExePath);
                    nginxWorkingDirectory = Path.GetDirectoryName(nginxExecutablePath)!;
                }
            }
            else
            {
                nginxExecutablePath = "nginx";
                nginxWorkingDirectory = Util.Combine(Path.GetDirectoryName(Program.assemblyPath)!, "/nginx");
            }

            string vhostFile = GenerateVHost();
            string vhostTmpRepository = Util.Combine(nginxWorkingDirectory, "agirax-vhosts");
            string vhostFilePath = Util.Combine(vhostTmpRepository, $"vh.{Path.GetFileName(rootDirectory)}.conf");

            if (!Directory.Exists(vhostTmpRepository))
            {
                Directory.CreateDirectory(vhostTmpRepository);
            }

            File.WriteAllText(vhostFilePath, vhostFile);

            NginxProcess = new Process();
            NginxProcess.StartInfo.FileName = nginxExecutablePath;
            NginxProcess.StartInfo.WorkingDirectory = nginxWorkingDirectory;
            NginxProcess.StartInfo.CreateNoWindow = true;
            NginxProcess.StartInfo.Arguments = $"-c {Util.EncodeParameterArgument(vhostFilePath)}";
            NginxProcess.Start();

            FpmProcess = new Process();
            FpmProcess.StartInfo.FileName = "php-cgi";
            FpmProcess.StartInfo.CreateNoWindow = true;
            FpmProcess.StartInfo.Arguments = $"-b 127.0.0.1:{_fpmPort}";
            FpmProcess.Start();

            Program.extProcesses.Add(NginxProcess.Id);
            Program.extProcesses.Add(FpmProcess.Id);
            ChildProcessTracker.AddProcess(NginxProcess);
            ChildProcessTracker.AddProcess(FpmProcess);

            if (!IndexFile.StartsWith("/")) IndexFile = "/" + indexFile;
        }

        public HttpResponse ServeStaticAsset(string rawAssetPath)
        {
            // normalize asset path
            string dirname = Path.GetDirectoryName(rawAssetPath)!;
            string urlDecodeFile = WebUtility.UrlDecode(Path.GetFileName(rawAssetPath));
            string assetPath = Path.Combine(dirname, urlDecodeFile);

            string ext = Path.GetExtension(assetPath);
            string mimeType = MimeTypeMap.GetMimeType(ext);

            if (!File.Exists(assetPath))
            {
                return new HttpResponse(System.Net.HttpStatusCode.NotFound);
            }

            byte[] fileBytes = File.ReadAllBytes(assetPath);

            ByteArrayContent responseContent = new ByteArrayContent(fileBytes);
            responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);

            HttpResponse res = new HttpResponse(200);
            res.Content = responseContent;
            res.SendChunked = true;

            return res;
        }

        public HttpResponse ServePhp(HttpRequest request)
        {
            string rootPath = this.RootDirectory;
            string filePath = rootPath + request.Path;

            if (File.Exists(filePath))
            {
                return ServeStaticAsset(filePath);
            }

            HttpResponse agiraxResponse = new HttpResponse();

            using (HttpClientHandler clientHandler = new HttpClientHandler()
            {
                AllowAutoRedirect = false,
                UseCookies = false
            })
            using (HttpClient client = new HttpClient(clientHandler))
            {
                HttpRequestMessage reqMsg = new HttpRequestMessage(request.Method, $"http://127.0.0.1:{_nginxPort}{request.FullPath}");

                if (request.HasContents)
                {
                    string contentType = request.GetHeader("Content-Type")!;
                    if (contentType.Contains(";")) contentType = contentType.Substring(0, contentType.IndexOf(";"));
                    reqMsg.Content = new ByteArrayContent(request.RawBody);
                    reqMsg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                }

                foreach (string headerKey in request.Headers.Keys)
                {
                    string? headerValue = request.Headers[headerKey];
                    reqMsg.Headers.TryAddWithoutValidation(headerKey, headerValue);
                }

                HttpResponseMessage resMsg = client.Send(reqMsg);
                agiraxResponse.Status = resMsg.StatusCode;

                foreach (var header in resMsg.Headers)
                {
                    bool canAdd = true;
                    foreach (string nnh in notRedirectedHeaders)
                        if (nnh.ToLower() == header.Key.ToLower())
                            canAdd = false;

                    if (!canAdd)
                        continue;

                    foreach (string value in header.Value)
                    {
                        agiraxResponse.Headers.Add(header.Key, value);
                    }
                }

                foreach (var contentHeader in resMsg.Content.Headers)
                {
                    bool canAdd = true;
                    foreach (string nnh in notRedirectedHeaders)
                        if (nnh.ToLower() == contentHeader.Key.ToLower())
                            canAdd = false;

                    if (!canAdd)
                        continue;

                    foreach (string value in contentHeader.Value)
                    {
                        agiraxResponse.Headers.Add(contentHeader.Key, value);
                    }
                }

                byte[] resBytes = resMsg.Content.ReadAsByteArrayAsync().Result;
                agiraxResponse.Content = new ByteArrayContent(resBytes);

                return agiraxResponse;
            }
        }

        public Router GetRouter(XmlNode routerNode)
        {
            Router sPhp = new Router();
            sPhp.SetRoute(new Route(RouteMethod.Any, ".*", null, ServePhp, null)
            {
                UseRegex = true
            });

            return sPhp;
        }

        string GenerateVHost()
        {
            return $$"""
                worker_processes  1;

                events {
                    worker_connections 1024;
                }
                http {
                    default_type  application/octet-stream;
                    #access_log  logs/access.log  main;

                    sendfile        on;

                    keepalive_timeout  65;

                    #gzip  on;

                    upstream php_upstream {
                	    server 127.0.0.1:{{_fpmPort}} weight=1 max_fails=1 fail_timeout=1;
                    }

                    server {
                        listen {{_nginxPort}};
                        server_name localhost;
                
                        access_log off;
                        error_log off;
                
                        root "{{RootDirectory}}";
                        index "{{IndexFile}}";
                
                        location / {
                            try_files $uri $uri/ /index.php$is_args$args;
                		    autoindex on;
                        }
                
                        location ~ \.php$ {
                            fastcgi_param  SCRIPT_FILENAME    $document_root$fastcgi_script_name;
                            fastcgi_param  QUERY_STRING       $query_string;
                            fastcgi_param  REQUEST_METHOD     $request_method;
                            fastcgi_param  CONTENT_TYPE       $content_type;
                            fastcgi_param  CONTENT_LENGTH     $content_length;
                
                            fastcgi_param  SCRIPT_NAME        $fastcgi_script_name;
                            fastcgi_param  REQUEST_URI        $request_uri;
                            fastcgi_param  DOCUMENT_URI       $document_uri;
                            fastcgi_param  DOCUMENT_ROOT      $document_root;
                            fastcgi_param  SERVER_PROTOCOL    $server_protocol;
                            fastcgi_param  REQUEST_SCHEME     $scheme;
                            fastcgi_param  HTTPS              $https if_not_empty;
                
                            fastcgi_param  GATEWAY_INTERFACE  CGI/1.1;
                            fastcgi_param  SERVER_SOFTWARE    nginx/$nginx_version;
                
                            fastcgi_param  REMOTE_ADDR        $remote_addr;
                            fastcgi_param  REMOTE_PORT        $remote_port;
                            fastcgi_param  SERVER_ADDR        $server_addr;
                            fastcgi_param  SERVER_PORT        $server_port;
                            fastcgi_param  SERVER_NAME        $server_name;
                
                            fastcgi_param  REDIRECT_STATUS    200;
                
                            fastcgi_split_path_info ^(.+\.php)(/.+)$;
                            try_files $fastcgi_script_name =404;
                
                            set $path_info $fastcgi_path_info;
                            fastcgi_param PATH_INFO $path_info;
                            fastcgi_read_timeout 3600;
                
                            fastcgi_index index.php;                
                            fastcgi_pass php_upstream;
                        }
                
                        charset utf-8;
                    }

                    client_max_body_size 512M;
                	server_names_hash_bucket_size 64;
                }
                """;
        }
    }
}
