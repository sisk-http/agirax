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
        Rewrite,
        RewriteHttps,
        RewriteHost,
        Cache,
        Authorize,
        Module
    }
}
