using Sisk.Core.Routing;
using System.Collections.Specialized;
using System.Reflection;
using System.Xml;

namespace Sisk.Agirax.RouterParser
{
    internal class ModuleRouterGenerator : IRouterGenerator
    {
        public string RelativePath { get; set; }

        public ModuleRouterGenerator(string relativePath)
        {
            RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
        }

        public Router CreateRouterFromNode(XmlNode routerNode)
        {
            string routerFilePath = Path.Combine(RelativePath, routerNode.Attributes!["File"]!.Value);
            string routerEntrypoint = routerNode.Attributes!["EntryPoint"]!.Value;

            Assembly application = Assembly.LoadFrom(routerFilePath);
            Type? entrypoint = application.GetType(routerEntrypoint);

            if (entrypoint is null)
            {
                throw new Exception($"Entry point not found.");
            }

            NameValueCollection parameters = new();
            foreach (XmlNode routeParameterNode in routerNode.SelectNodes("Parameter")!)
            {
                parameters.Add(routeParameterNode.Attributes!["Name"]!.Value, routeParameterNode.InnerText);
            }

            RouterFactory appRouterFactory = (RouterFactory)Activator.CreateInstance(entrypoint)!;
            appRouterFactory.Setup(parameters);
            Router applicationRouter = appRouterFactory.BuildRouter();

            return applicationRouter;
        }
    }
}
