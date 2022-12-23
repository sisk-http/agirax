namespace Sisk.Agirax
{
    internal enum RouterType
    {
        Empty,
        Static,
        Module,
        PhpCgi,
        PhpNginxProxy
    }

    internal enum RequestHandlerType
    {
        RewriteHttps,
        RewriteHost,
        Cache
    }
}
