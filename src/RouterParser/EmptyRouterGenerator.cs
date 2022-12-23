using Sisk.Core.Routing;
using System.Xml;

namespace Sisk.Agirax.RouterParser
{
    internal class EmptyRouterGenerator : IRouterGenerator
    {
        public Router CreateRouterFromNode(XmlNode routerNode)
        {
            Router r = new Router();
            r.SetRoute(new Route(RouteMethod.Any, ".*", null, (req) =>
            {
                return req.CreateHeadResponse();
            }, null)
            {
                UseRegex = true
            });

            return r;
        }
    }
}
