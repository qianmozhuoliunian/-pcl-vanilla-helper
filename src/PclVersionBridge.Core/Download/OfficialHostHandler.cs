using System.Net;

namespace PclVersionBridge.Core.Download;

public sealed class OfficialHostHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public static HttpClient CreateClient() => new(new OfficialHostHandler(new HttpClientHandler
    {
        AllowAutoRedirect = false
    })) { Timeout = TimeSpan.FromMinutes(5) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var current = request;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (current.RequestUri is null) throw new InstallException("下载请求没有地址。");
            OfficialHosts.ValidateUri(current.RequestUri);
            var response = await base.SendAsync(current, cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or
                    HttpStatusCode.Redirect or HttpStatusCode.SeeOther or
                    HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirects == 5 || current.Method != HttpMethod.Get)
                throw new InstallException("下载地址重定向无效。");
            var next = location.IsAbsoluteUri ? location : new Uri(current.RequestUri, location);
            OfficialHosts.ValidateUri(next);
            current = new HttpRequestMessage(HttpMethod.Get, next);
        }
        throw new InstallException("下载地址重定向次数过多。");
    }
}
