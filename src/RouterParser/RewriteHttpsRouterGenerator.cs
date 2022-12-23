using Sisk.Core.Routing;
using System.Xml;

namespace Sisk.Agirax.RouterParser
{
    internal class RewriteHttpsRouterGenerator : IRouterGenerator
    {
        public Router CreateRouterFromNode(XmlNode routerNode)
        {
            Router r = new Router();
            r.SetRoute(new Route(RouteMethod.Any, ".*", null, (req) =>
            {
                return req.CreateRedirectResponse(req.FullUrl.Replace("http://", "https://"), true);
            }, null)
            {
                UseRegex = true
            });

            return r;
        }
    }
}
