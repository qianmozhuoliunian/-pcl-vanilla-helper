using System.IO;
using System.Net;
using System.Text.Json;

namespace PclVersionBridge.App;

public sealed record FriendFeatureConfiguration(Uri? ServiceUri, bool IsDevelopment, string Status)
{
    public static FriendFeatureConfiguration Load(string? directory=null, Func<string,string?>? environment=null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        try
        {
            var url=environment("VANILLAREACH_FRIENDS_SERVICE_URL");
            var development=environment("VANILLAREACH_FRIENDS_DEVELOPMENT") is "1" or "true";
            if(string.IsNullOrWhiteSpace(url))
            {
                var path=Path.Combine(directory??AppContext.BaseDirectory,"friends-service.json");
                if(!File.Exists(path))return Missing();
                if(new FileInfo(path).Length>4096)return Invalid();
                var configuration=JsonSerializer.Deserialize<Settings>(File.ReadAllBytes(path),new JsonSerializerOptions {PropertyNameCaseInsensitive=true});
                url=configuration?.ServiceUrl;
                development=configuration?.AllowLoopbackDevelopment??false;
            }
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.UserInfo.Length!=0 || uri.Query.Length!=0 || uri.Fragment.Length!=0 || uri.AbsolutePath!="/")return Invalid();
            var literalLoopback=IPAddress.TryParse(uri.Host.Trim('[',']'),out var address) && IPAddress.IsLoopback(address);
            var local=development && uri.Scheme=="http" && literalLoopback;
            if(uri.Scheme!="https" && !local)return Invalid();
            return new(uri,local,local ? "本机好友测试服务 · 验证邮件保存在测试收件箱，不会发到真实邮箱。" : "好友资料会发送到已配置的好友服务，安装功能可独立使用。");
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {return Invalid();}
    }
    private static FriendFeatureConfiguration Missing()=>new(null,false,"尚未配置好友服务。原版安装和整合包功能可以继续使用。");
    private static FriendFeatureConfiguration Invalid()=>new(null,false,"好友服务配置无效。需要 HTTPS 服务地址；本机测试须显式启用。原有安装功能不受影响。");
    private sealed record Settings(string? ServiceUrl, bool AllowLoopbackDevelopment=false);
}
