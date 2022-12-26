using Sisk.Core.Http;
using Sisk.Core.Routing.Handlers;

namespace Sisk.Agirax.RequestHandlerParser
{
    internal class StoreServerCache : IRequestHandler
    {
        public string Identifier { get; init; } = Guid.NewGuid().ToString();
        public RequestHandlerExecutionMode ExecutionMode { get; init; } = RequestHandlerExecutionMode.AfterResponse;

        public ServerCache ServerCache { get; set; }

        public StoreServerCache(ServerCache serverCache)
        {
            ServerCache = serverCache ?? throw new ArgumentNullException(nameof(serverCache));
        }

        public HttpResponse? Execute(HttpRequest request, HttpContext context)
        {
            if (context.RouterResponse != null)
            {
                HttpResponse res = context.RouterResponse;
                long length = res.Content?.Headers.ContentLength ?? 0;
                if (length < ServerCache.IndividualMaxSize && (ServerCache.UsedSize + length < ServerCache.MaxHeapSize))
                {

                    if (ServerCache.AllowedContentTypes != null && ServerCache.AllowedContentTypes.Length != 0)
                    {
                        if (!ServerCache.AllowedContentTypes.Contains(res.Content!.Headers.ContentType?.MediaType ?? "-"))
                        {
                            return null;
                        }
                    }

                    return ServerCache.CacheRequest(new ServerCache.Cache()
                    {
                        Response = res,
                        Expires = TimeSpan.FromHours(6),
                        Path = request.FullPath,
                        RequestBody = request.Body,
                        Authority = request.Authority
                    });
                }
            }

            return null;
        }
    }

    internal class ServerCache : IRequestHandler
    {
        public RequestHandlerExecutionMode ExecutionMode { get; init; } = RequestHandlerExecutionMode.BeforeResponse;
        private List<Cache> Repository = new List<Cache>();
        public string Identifier { get; init; } = Guid.NewGuid().ToString();

        public long IndividualMaxSize { get; set; }
        public long UsedSize { get; set; }
        public long MaxHeapSize { get; set; }
        public string Authority { get; set; } = "";
        public string[]? AllowedContentTypes;

        public HttpResponse CacheRequest(Cache cache)
        {
            cache.Response.Headers.Add("X-Agirax-Cache", "NEW");
            UsedSize += cache.Response.Content?.Headers.ContentLength ?? 0;
            Repository.Add(cache);
            return cache.Response;
        }

        private Cache? GetCache(HttpRequest req)
        {
            try
            {
                for (int i = 0; i < Repository.Count; i++)
                {
                    Cache cache = Repository[i];
                    if (req.Authority == cache.Authority && cache.Path == req.Path)
                    {
                        if (cache.RequestBody != null && string.Compare(req.Body, cache.RequestBody) != 0)
                        {
                            continue;
                        }
                        if (cache.IsExpired())
                        {
                            UsedSize -= cache.Response.Content?.Headers.ContentLength ?? 0;
                            Repository.Remove(cache);
                            continue;
                        }

                        return cache;
                    }
                }
            }
            catch (Exception)
            {
                // maybe Repository can be changed to be smaller after
                // some cache being removed
                return null;
            }
            return null;
        }

        public HttpResponse? Execute(HttpRequest request, HttpContext context)
        {
            if (request.GetQueryValue("No-Cache") != null
             || request.GetHeader("Cache-Control") == "no-cache"
             || request.GetHeader("Cache-Control") == "no-store")
                return null;
            HttpResponse? cacheRes = GetCache(request)?.Response;
            if (cacheRes != null)
            {
                cacheRes.Headers.Set("X-Agirax-Cache", $"HIT-{DateTime.Now}");
                return cacheRes;
            }
            return null;
        }

        public class Cache
        {
            public string? RequestBody { get; set; }
            public HttpResponse Response { get; set; } = null!;
            public string? Path { get; set; }
            protected internal DateTime CachedAt { get; set; } = DateTime.UtcNow;
            public TimeSpan Expires { get; set; } = TimeSpan.FromMinutes(10);
            public bool IsExpired() => DateTime.UtcNow > (CachedAt + Expires);
            public string Authority { get; set; }
        }
    }
}
