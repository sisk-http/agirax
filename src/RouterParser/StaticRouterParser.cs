using Sisk.Core.Entity;
using Sisk.Core.Http;
using Sisk.Core.Routing;
using System.Xml;

namespace Sisk.Agirax.RouterParser
{
    internal class StaticRouterGenerator : IRouterGenerator
    {
        public string RootDirectory { get; set; }
        public string IndexFile { get; set; } = "index.html";

        public StaticRouterGenerator(string rootDirectory, string indexFile)
        {
            RootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
            IndexFile = indexFile ?? throw new ArgumentNullException(nameof(indexFile));
            if (!IndexFile.StartsWith("/")) IndexFile = "/" + indexFile;
        }

        public HttpResponse ServeStaticDirectory(HttpRequest request)
        {
            string servingDirectory = RootDirectory;
            string requestFile = IndexFile;
            if (request.FullPath != "/")
            {
                requestFile = request.FullPath;
            }

            string ext = Path.GetExtension(requestFile);
            string mimeType = MimeTypeMap.GetMimeType(ext);

            string servingDirectoryRealPath = Path.GetFullPath(servingDirectory);
            string filePath = servingDirectoryRealPath + requestFile;
            if (!File.Exists(filePath))
            {
                return request.CreateResponse(System.Net.HttpStatusCode.NotFound);
            }

            byte[] fileBytes = File.ReadAllBytes(filePath);

            ByteArrayContent responseContent = new ByteArrayContent(fileBytes);
            responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);

            HttpResponse res = new HttpResponse(200);
            res.Content = responseContent;

            return res;
        }

        public Router GetRouter(XmlNode routerNode)
        {
            Router r = new Router();
            r.SetRoute(new Route(RouteMethod.Any, ".*", null, ServeStaticDirectory, null)
            {
                UseRegex = true
            });

            return r;
        }
    }
}
