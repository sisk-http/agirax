using Sisk.Core.Http;
using Sisk.Core.Routing.Handlers;
using System.Text.RegularExpressions;

namespace minify
{
    public class MinifyHtmlCssJsRequestHandler : IRequestHandler
    {
        public string Identifier { get; init; } = Guid.NewGuid().ToString();
        public RequestHandlerExecutionMode ExecutionMode { get; init; } = RequestHandlerExecutionMode.AfterResponse;

        public HttpResponse? Execute(HttpRequest request, HttpContext context)
        {
            var rgxOptions = RegexOptions.Compiled | RegexOptions.Multiline;

            if (context.RouterResponse == null)
                return null;
            if (context.RouterResponse.Content == null)
                return null;

            string? mediaType = context.RouterResponse.Headers["Content-Type"] ?? context.RouterResponse.Content.Headers.ContentType?.MediaType;
            string content = context.RouterResponse.Content.ReadAsStringAsync().Result;

            if (mediaType == null)
                return null;

            else if (mediaType.StartsWith("text/html"))
            {
                string minified = content;

                // comments and line start
                minified = Regex.Replace(minified, @"<!--(.*?)-->|\s\B", "", rgxOptions);

                // spaced tags
                minified = Regex.Replace(minified, @">[\r\n\s]+<", "><", rgxOptions);

                context.RouterResponse.Content = new StringContent(minified);
                context.RouterResponse.Headers.Set("Content-Type", mediaType);
                context.RouterResponse.Headers.Set("X-Minified-By", "Agirax");
                return context.RouterResponse;
            }
            else if (mediaType.StartsWith("application/javascript"))
            {
                string minified = content;

                // start space
                minified = Regex.Replace(minified, @"^\s+", "", rgxOptions);
                // single line comments
                minified = Regex.Replace(minified, @"^[^\n][\t\s]+//.*\n", "><", rgxOptions);
                // multiline line comments
                minified = Regex.Replace(minified, @"\/\*[\s\S]*?\*\/", "><", rgxOptions);

                context.RouterResponse.Content = new StringContent(minified);
                context.RouterResponse.Headers.Set("Content-Type", mediaType);
                context.RouterResponse.Headers.Set("X-Minified-By", "Agirax");
                return context.RouterResponse;
            }
            else if (mediaType.StartsWith("text/css"))
            {
                string minified = content;

                // multiline line comments
                minified = Regex.Replace(minified, @"\/\*[\s\S]*?\*\/", "><", rgxOptions);
                // trim all spaces
                minified = Regex.Replace(minified, @"^\s+|[\r\n\t]+", "", rgxOptions);

                context.RouterResponse.Content = new StringContent(minified);
                context.RouterResponse.Headers.Set("Content-Type", mediaType);
                context.RouterResponse.Headers.Set("X-Minified-By", "Agirax");
                return context.RouterResponse;
            }
            else
            {
                return null;
            }
        }
    }
}
