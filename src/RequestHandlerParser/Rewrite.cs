using Sisk.Core.Http;
using Sisk.Core.Routing.Handlers;
using System.Net;
using System.Text.RegularExpressions;

namespace Sisk.Agirax.RequestHandlerParser
{
    internal class Rewrite : IRequestHandler
    {
        public string Identifier { get; init; } = Guid.NewGuid().ToString();
        public RequestHandlerExecutionMode ExecutionMode { get; init; } = RequestHandlerExecutionMode.BeforeResponse;

        public string Pattern { get; set; }
        public string Action { get; set; }

        public Rewrite(string pattern, string reply)
        {
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
            Action = reply ?? throw new ArgumentNullException(nameof(reply));
        }

        public HttpResponse? Execute(HttpRequest request, HttpContext context)
        {
            if (Regex.IsMatch(request.FullPath, Pattern))
            {
                string actionMode = Action.Contains(' ') ? Action.Substring(0, Action.IndexOf(' ')) : Action;
                string? actionParams = Action.Contains(' ') ? Action.Substring(Action.IndexOf(' ')) : null;

                if (string.Compare(actionMode, "Drop", true) == 0)
                {
                    if (actionParams == null)
                    {
                        throw new Exception("Action Drop expects an status code argument.");
                    }
                    else
                    {
                        bool isInt = Int32.TryParse(actionParams, out int val);
                        if (!isInt)
                        {
                            HttpStatusCode st = Enum.Parse<HttpStatusCode>(actionParams, true);
                            HttpResponse res = new HttpResponse(st);
                            return res;
                        }
                        else
                        {
                            HttpResponse res = new HttpResponse(val);
                            return res;
                        }
                    }
                }
                else if (string.Compare(actionMode, "Redirect", true) == 0)
                {
                    if (actionParams == null)
                    {
                        throw new Exception("Action Redirect expects an path to redirect.");
                    }
                    else
                    {
                        HttpResponse res = new HttpResponse(HttpStatusCode.Moved);
                        res.Headers.Add("Location", actionParams);
                        return res;
                    }
                }
                else if (string.Compare(actionMode, "PermanentRedirect", true) == 0)
                {
                    if (actionParams == null)
                    {
                        throw new Exception("Action PermanentRedirect expects an path to redirect.");
                    }
                    else
                    {
                        HttpResponse res = new HttpResponse(HttpStatusCode.MovedPermanently);
                        res.Headers.Add("Location", actionParams);
                        return res;
                    }
                }
                else
                {
                    throw new Exception("Invalid rewrite rule action name.");
                }
            }
            else
            {
                return null;
            }
        }
    }
}
