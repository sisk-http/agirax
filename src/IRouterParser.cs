using Sisk.Core.Routing;
using System.Xml;

namespace Sisk.Agirax
{
    internal interface IRouterGenerator
    {
        public Router CreateRouterFromNode(XmlNode routerNode);
    }
}
