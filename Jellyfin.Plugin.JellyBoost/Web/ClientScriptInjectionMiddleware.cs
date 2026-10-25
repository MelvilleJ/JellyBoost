using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyBoost.Boost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Jellyfin.Plugin.JellyBoost.Web;

// Rewrites the served web client index.html in memory, so nothing on disk is modified
// and it keeps working in read-only installs such as Docker images.
public class ClientScriptInjectionMiddleware
{
    public const string ScriptTag = "<script plugin=\"JellyBoost\" defer src=\"../JellyBoost/jellyboost.js\"></script>";

    private readonly RequestDelegate _next;
    private readonly BoostSettingsStore _store;

    public ClientScriptInjectionMiddleware(RequestDelegate next, BoostSettingsStore store)
    {
        _next = next;
        _store = store;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_store.Configuration.InjectWebClient || !IsWebIndexRequest(context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Ask for an uncompressed, non-304 response so the HTML can be edited.
        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("If-None-Match");
        context.Request.Headers.Remove("If-Modified-Since");

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Position = 0;
        var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
            && (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ?? false)
            && StringValues.IsNullOrEmpty(context.Response.Headers.ContentEncoding);

        if (!isHtml)
        {
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        var html = Encoding.UTF8.GetString(buffer.ToArray());
        var bytes = Encoding.UTF8.GetBytes(InjectScript(html));
        context.Response.ContentLength = bytes.Length;
        context.Response.Headers.ETag = default;
        context.Response.Headers.LastModified = default;
        await originalBody.WriteAsync(bytes).ConfigureAwait(false);
    }

    internal static string InjectScript(string html)
    {
        if (html.Contains(ScriptTag, StringComparison.Ordinal))
        {
            return html;
        }

        var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyEnd < 0 ? html + ScriptTag : html.Insert(bodyEnd, ScriptTag);
    }

    private static bool IsWebIndexRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var path = request.Path.Value ?? string.Empty;
        return path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase);
    }
}

public class ClientScriptStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<ClientScriptInjectionMiddleware>();
            next(app);
        };
    }
}
