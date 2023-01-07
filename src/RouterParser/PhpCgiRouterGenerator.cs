using Sisk.Core.Entity;
using Sisk.Core.Http;
using Sisk.Core.Routing;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Xml;

namespace Sisk.Agirax.RouterParser
{
    internal class PhpCgiRouterGenerator : IRouterGenerator
    {
        public string RootDirectory { get; set; }
        public string IndexFile { get; set; } = "index.php";
        public string PhpCgiExecutable { get; set; } = "php-cgi";
        public bool RedirectAllToIndex { get; set; } = false;

        public PhpCgiRouterGenerator(string rootDirectory, string? indexFile, bool redirectAllToIndex, string? phpCgiExecuteable)
        {
            RootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
            RedirectAllToIndex = redirectAllToIndex;

            if (indexFile != null)
                IndexFile = indexFile;
            if (phpCgiExecuteable != null)
                PhpCgiExecutable = phpCgiExecuteable;

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

            return res;
        }

        public HttpResponse ServePhp(HttpRequest request)
        {
            string rootPath = this.RootDirectory;
            string indexFile = this.IndexFile;
            string tempPath = Path.GetTempPath();
            string? ext = Path.GetExtension(request.Path);

            if (!string.IsNullOrEmpty(ext))
            {
                return ServeStaticAsset(rootPath + request.Path);
            }

            using (var process = new Process())
            {
                process.StartInfo.FileName = PhpCgiExecutable;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.WorkingDirectory = RootDirectory;
                process.StartInfo.Arguments = rootPath + indexFile + $" --no-chdir";

                process.StartInfo.EnvironmentVariables.Clear();
                //REQUEST_SCHEME
                process.StartInfo.EnvironmentVariables.Add("GATEWAY_INTERFACE", "CGI/1.1");
                process.StartInfo.EnvironmentVariables.Add("SERVER_PROTOCOL", "HTTP/1.1");
                process.StartInfo.EnvironmentVariables.Add("REDIRECT_STATUS", "200");
                process.StartInfo.EnvironmentVariables.Add("DOCUMENT_ROOT", rootPath);
                process.StartInfo.EnvironmentVariables.Add("SCRIPT_NAME", indexFile);
                process.StartInfo.EnvironmentVariables.Add("SCRIPT_FILENAME", rootPath + indexFile);
                process.StartInfo.EnvironmentVariables.Add("QUERY_STRING", request.QueryString);
                process.StartInfo.EnvironmentVariables.Add("CONTENT_LENGTH", request.Headers["Content-Length"] ?? "0");
                process.StartInfo.EnvironmentVariables.Add("CONTENT_TYPE", request.Headers["Content-Type"]);
                process.StartInfo.EnvironmentVariables.Add("REQUEST_METHOD", request.Method.ToString());
                process.StartInfo.EnvironmentVariables.Add("REQUEST_SCHEME", request.IsSecure ? "https" : "http");
                process.StartInfo.EnvironmentVariables.Add("USER_AGENT", request.Headers["User-Agent"]);
                process.StartInfo.EnvironmentVariables.Add("SERVER_ADDR", "127.0.0.1");
                process.StartInfo.EnvironmentVariables.Add("REMOTE_ADDR", request.Origin.ToString());
                process.StartInfo.EnvironmentVariables.Add("REMOTE_PORT", "0");
                process.StartInfo.EnvironmentVariables.Add("REFERER", request.Headers["Referer"] ?? "");
                process.StartInfo.EnvironmentVariables.Add("REQUEST_URI", request.FullPath);

                foreach (string headerKey in request.Headers)
                {
                    process.StartInfo.EnvironmentVariables.Add("HTTP_" + headerKey.ToUpper().Replace('-', '_'), request.Headers[headerKey]);
                }

                process.StartInfo.EnvironmentVariables.Add("TMPDIR", tempPath);
                process.StartInfo.EnvironmentVariables.Add("TEMP", tempPath);

                process.Start();

                if (request.HasContents)
                {
                    using (var sw = process.StandardInput)
                    {
                        sw.BaseStream.Write(request.RawBody);
                    }
                }

                HttpResponse cgiResponse = new HttpResponse();

                using (Stream ss = process.StandardOutput.BaseStream)
                using (StreamReader sr = new StreamReader(ss, request.RequestEncoding))
                {
                    bool headersParsed = false;
                    string output = sr.ReadToEnd();
                    StringBuilder bodySb = new StringBuilder();
                    foreach (string LINE in output.Split('\n'))
                    {
                        string line = LINE.Trim();
                        if (line == "") headersParsed = true;

                        if (!headersParsed)
                        {
                            int headerSeparatorIndex = line.IndexOf(':');
                            string headerName = line.Substring(0, headerSeparatorIndex);
                            string headerVal = line.Substring(headerSeparatorIndex + 1).Trim();

                            if (headerName == "Status")
                            {
                                int statusNum = Int32.Parse(headerVal.Substring(0, headerVal.IndexOf(' ')));
                                cgiResponse.Status = (HttpStatusCode)statusNum;
                            }
                            else
                            {
                                cgiResponse.Headers.Add(headerName, headerVal);
                            }
                        }
                        else
                        {
                            bodySb.AppendLine(line);
                        }
                    }

                    cgiResponse.Content = new StringContent(bodySb.ToString().Trim());
                }

                return cgiResponse;
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
    }
}
