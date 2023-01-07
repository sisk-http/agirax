using Sisk.Core.Http;
using Sisk.Core.Routing.Handlers;

namespace Sisk.Agirax.RequestHandlerParser
{
    internal class Authorize : IRequestHandler
    {
        public string Identifier { get; init; } = Guid.NewGuid().ToString();
        public RequestHandlerExecutionMode ExecutionMode { get; init; } = RequestHandlerExecutionMode.BeforeResponse;
        public string Username { get; set; }
        public string Password { get; set; }
        public string Message { get; set; } = "This website requires authentication.";

        public HttpResponse? Execute(HttpRequest request, HttpContext context)
        {
            string? auth = request.Headers["Authorization"];
            if (auth == null || !auth.StartsWith("Basic"))
            {
                goto errorAuthorization;
            }

            string[] authStr = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(auth.Replace("Basic ", ""))).Split(':');
            string authUser = authStr[0];
            string authPassword = authStr[1];

            if (authUser != Username || authPassword != Password)
            {
                goto errorAuthorization;
            }
            else
            {
                return null;
            }

        errorAuthorization:
            HttpResponse res = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            res.Headers.Add("WWW-Authenticate", $"Basic realm=\"{Message}\", charset=\"UTF-8\"");
            return res;
        }
    }
}
