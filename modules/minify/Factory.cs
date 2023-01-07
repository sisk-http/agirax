using Sisk.Core.Routing.Handlers;
using System.Collections.Specialized;

namespace minify
{
    internal class Factory : RequestHandlerFactory
    {
        public override IRequestHandler[] BuildRequestHandlers()
        {
            return new[]
            {
                new MinifyHtmlCssJsRequestHandler()
            };
        }

        public override void Setup(NameValueCollection setupParameters)
        {
            ;
        }
    }
}
