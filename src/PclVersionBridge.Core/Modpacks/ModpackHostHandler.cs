using System.Net;
using PclVersionBridge.Core.Download;

namespace PclVersionBridge.Core.Modpacks;

public sealed class ModpackHostHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public static void ValidateUri(Uri uri)
    {
        if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InstallException("下载地址不符合来源要求。");
        if (uri.Host is "api.modrinth.com" or "cdn.modrinth.com" or "meta.fabricmc.net" or "maven.fabricmc.net") return;
        OfficialHosts.ValidateUri(uri);
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        HttpRequestMessage? owned = null;
        try
        {
            var current = request;
            for (var count = 0; ; count++)
            {
                ValidateUri(current.RequestUri ?? throw new InstallException("下载地址缺失。"));
                var response = await base.SendAsync(current, token);
                if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                    HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                    return response;
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null || count >= 5 || current.Method != HttpMethod.Get)
                    throw new InstallException("下载重定向无效或次数过多。");
                var next = location.IsAbsoluteUri ? location : new Uri(current.RequestUri!, location);
                ValidateUri(next);
                // Modpack content and API calls cannot redirect into another service.
                if (request.RequestUri!.Host is "api.modrinth.com" or "cdn.modrinth.com" &&
                    next.Host != request.RequestUri.Host)
                    throw new InstallException("下载重定向离开了指定来源。");
                owned?.Dispose();
                owned = new HttpRequestMessage(HttpMethod.Get, next);
                owned.Headers.UserAgent.ParseAdd(ModpackNetwork.UserAgent);
                current = owned;
            }
        }
        finally { owned?.Dispose(); }
    }
}
