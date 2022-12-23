using Sisk.Core.Http;
using Sisk.Core.Routing.Handlers;

namespace Sisk.Agirax.RequestHandlerParser
{
    internal class RewriteHttps : IRequestHandler
    {
        public string Identifier { get; init; } = Guid.NewGuid().ToString();
        public RequestHandlerExecutionMode ExecutionMode { get; init; } = RequestHandlerExecutionMode.BeforeResponse;

        public HttpResponse? Execute(HttpRequest request, HttpContext context)
        {
            if (request.FullUrl.StartsWith("http://"))
            {
                return request.CreateRedirectResponse(request.FullUrl.Replace("http://", "https://"), true);
            }
            return null;
        }
    }
}
