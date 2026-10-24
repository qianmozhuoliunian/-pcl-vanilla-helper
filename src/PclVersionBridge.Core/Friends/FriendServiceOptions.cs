using System.Net;

namespace PclVersionBridge.Core.Friends;

public sealed class FriendServiceOptions
{
    public FriendServiceOptions(Uri serviceUri, bool allowLoopbackDevelopment = false)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        if (!serviceUri.IsAbsoluteUri || !string.IsNullOrEmpty(serviceUri.UserInfo) ||
            serviceUri.AbsolutePath != "/" || serviceUri.Query.Length != 0 || serviceUri.Fragment.Length != 0 ||
            (serviceUri.Scheme != Uri.UriSchemeHttps && !(allowLoopbackDevelopment &&
             serviceUri.Scheme == Uri.UriSchemeHttp && IPAddress.TryParse(serviceUri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip))))
            throw new ArgumentException("好友服务必须使用 HTTPS 源地址；本地开发需明确启用并使用回环 IP。", nameof(serviceUri));
        ServiceUri = serviceUri;
        AllowLoopbackDevelopment = allowLoopbackDevelopment;
    }

    public Uri ServiceUri { get; }
    public bool AllowLoopbackDevelopment { get; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
}

public sealed class FriendServiceException(string code, string message, HttpStatusCode? statusCode = null, int? retryAfterSeconds = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
