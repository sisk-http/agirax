using Sisk.Core.Http;
using Sisk.Core.Routing.Handlers;

namespace Sisk.Agirax.RequestHandlerParser
{
    internal class RewriteHost : IRequestHandler
    {
        public string Identifier { get; init; } = Guid.NewGuid().ToString();
        public RequestHandlerExecutionMode ExecutionMode { get; init; } = RequestHandlerExecutionMode.BeforeResponse;
        public string In { get; set; }
        public string Out { get; set; }

        public RewriteHost(string @in, string @out)
        {
            In = @in ?? throw new ArgumentNullException(nameof(@in));
            Out = @out ?? throw new ArgumentNullException(nameof(@out));
        }

        public HttpResponse? Execute(HttpRequest request, HttpContext context)
        {
            if (request.Host == In)
            {
                return request.CreateRedirectResponse(request.FullUrl.Replace(In, Out), true);
            }
            return null;
        }
    }
}
