using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PclVersionBridge.App.Friends;
using PclVersionBridge.Core.Friends;
using PclVersionBridge.Core.Modpacks;
using VanillaReach.Friends.Server;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try {Run(args);}
        catch(Exception ex) {Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    }
    private static void Run(string[] args)
    {
        var live=args.Contains("--live-source");
        var result=Task.Run(()=>StartAsync(live)).GetAwaiter().GetResult();
        Console.WriteLine("CHECK registered independent clients and consented card");
        try
        {
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/VanillaReach;component/Styles.xaml")});
            using var vm=new FriendsViewModel(result.B,new MemoryNotes());
            using var view=new FriendsView {FontFamily=new FontFamily("Microsoft YaHei UI, Segoe UI"),FontSize=13};
            view.Initialize(vm,"本机隔离测试 · 以下两位是实际注册的测试账号",true);
            var click=Dispatcher.CurrentDispatcher.InvokeAsync(()=>Find<Button>(view).First(b=>Equals(b.Content,"启用好友")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            PumpUntil(()=>click.Task.IsCompleted && vm.IsLoggedIn&&!vm.IsBusy);
            Console.WriteLine("CHECK native friends view synchronized with authority");
            Check(vm.Friends.Count==1 && vm.Invitations.Count==1,"Client VM did not synchronize actual service state");
            var output=Path.GetFullPath(".artifacts/friends-check");Directory.CreateDirectory(output);
            foreach(var size in new[]{new Size(660,620),new Size(520,400)})
            {
                var surface=new Border {Background=(Brush)app.Resources["AppBackground"],Padding=new Thickness(16),Child=view};
                surface.Measure(size);surface.Arrange(new Rect(size));surface.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,$"friends-connected-{size.Width}x{size.Height}.png")))encoder.Save(file);
                surface.Child=null;
            }
            Task.Run(()=>FinishAsync(result)).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output,live?"integration-live.json":"integration-offline.json"),JsonSerializer.Serialize(new{verifiedUtc=DateTimeOffset.UtcNow,liveSource=live,clientIdentities=2,consentRequired=true,pack=result.Card.Pack,blockedCardDenied=true,logoutRevoked=true}));
            Console.WriteLine("PASS two independent FriendApiClient identities: register/confirm/login, consent, exact card, VM sync, block and logout revocation");
            Console.WriteLine(live?"PASS production Modrinth validation and actual mrpack preflight (no game installation)":"PASS deterministic source fixture (not a live-source claim)");
        }
        finally {Task.Run(async()=>await result.App.DisposeAsync()).GetAwaiter().GetResult();result.A.Dispose();result.B.Dispose();result.HttpA.Dispose();result.HttpB.Dispose();}
    }
    private static async Task<Context> StartAsync(bool live)
    {
        const string password="LocalPreview!2026";
        var state=Path.GetFullPath(Path.Combine(".artifacts","friends-check","http-"+Guid.NewGuid().ToString("N")));
        var app=FriendsHost.Build(["--environment","Development"],b=>
        {
            b.WebHost.UseUrls("http://127.0.0.1:0");b.Logging.ClearProviders();
            b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"Friends:PublicOrigin","http://127.0.0.1/"},{"Friends:DataDirectory",state},{"Friends:DevelopmentInbox",Path.Combine(state,"inbox")}});
            if(!live)b.Services.Replace(ServiceDescriptor.Singleton<IPackReferenceValidator>(new FixtureValidator()));
        });
        await app.StartAsync();var options=new FriendServiceOptions(new Uri(app.Urls.Single()),true){RequestTimeout=TimeSpan.FromSeconds(45)};
        var ha=FriendApiClient.CreateHttpClient();var hb=FriendApiClient.CreateHttpClient();
        var a=new FriendApiClient(ha,options,new MemorySession());var bclient=new FriendApiClient(hb,options,new MemorySession());
        foreach(var pair in new[]{(Client:a,Name:"A"),(Client:bclient,Name:"B")})
        {
            var email="local-"+pair.Name.ToLowerInvariant()+"@example.test";
            await pair.Client.RegisterAsync(new(email,password,"本机测试玩家 "+pair.Name),default);
            var message=Directory.EnumerateFiles(Path.Combine(state,"inbox"),"*.json").Select(File.ReadAllText).Select(s=>JsonSerializer.Deserialize<Inbox>(s,new JsonSerializerOptions(JsonSerializerDefaults.Web))!).First(x=>x.Email==email);
            await pair.Client.ConfirmEmailAsync(new(email,message.Code),default);
            await pair.Client.LoginAsync(new(email,password),true,default);
        }
        await a.SendRequestAsync(bclient.Session!.Me.FriendCode,default);
        Check((await a.GetFriendsAsync(0,default)).Total==0,"Request skipped consent");
        var request=(await bclient.GetRequestsAsync(0,default)).Items.Single();await bclient.ResolveRequestAsync(request.Id,"accept",default);
        Check((await a.GetFriendsAsync(0,default)).Total==1 && (await bclient.GetFriendsAsync(0,default)).Total==1,"Friends were not reciprocal");
        PackReference pack=FixtureValidator.Reference;
        if(live)
        {
            using var source=ModpackNetwork.CreateClient();var catalog=new ModrinthCatalog(new ModpackNetwork(source));
            var release=(await catalog.GetVersionsAsync("1KVo5zza",false,"1.20.1",default)).First(r=>r.FileSize<=2*1024*1024);
            pack=new(release.ProjectId,release.Id,release.Sha512);
        }
        var card=await a.SendInvitationAsync(new(bclient.Session.Me.UserId,pack,Guid.NewGuid().ToString("N")),default);
        var verified=await bclient.GetInvitationAsync(card.Id,default);Check(verified.Pack.ProjectId==pack.ProjectId && verified.Pack.VersionId==pack.VersionId && verified.Pack.ArchiveSha512.Equals(pack.ArchiveSha512,StringComparison.OrdinalIgnoreCase),"Card reference changed");
        if(live)
        {
            using var source=ModpackNetwork.CreateClient();var network=new ModpackNetwork(source);var catalog=new ModrinthCatalog(network);
            var release=await catalog.GetPinnedReleaseAsync(pack.ProjectId,pack.VersionId,pack.ArchiveSha512,default);
            var preview=await new ModpackContentSource(network).DownloadArchiveAsync(release,Path.Combine(state,"actual-source.mrpack"),default);
            Check(release.GameVersions.Contains(preview.Plan.MinecraftVersion),"Actual archive MC disagreed with source");
        }
        return new(app,a,bclient,ha,hb,card);
    }
    private static async Task FinishAsync(Context c)
    {
        await c.B.BlockAsync(c.A.Session!.Me.UserId,default);
        Check((await c.A.GetFriendsAsync(0,default)).Total==0,"Block did not remove reciprocal relationship");
        try {await c.B.GetInvitationAsync(c.Card.Id,default);throw new Exception("Blocked card remained active");}
        catch(FriendServiceException ex) when(ex.StatusCode==HttpStatusCode.Conflict) { }
        var old=c.A.Session!;await c.A.LogoutAsync(default);
        using var request=new HttpRequestMessage(HttpMethod.Get,new Uri(c.App.Urls.Single()+"/api/me"));request.Headers.Authorization=new("Bearer",old.AccessToken);
        using var response=await c.HttpA.SendAsync(request);Check(response.StatusCode==HttpStatusCode.Unauthorized,"Logged-out access credential remained valid");
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {if(root is T t)yield return t;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var found in Find<T>(child))yield return found;}
    private static void PumpUntil(Func<bool> condition)
    {
        var deadline=DateTime.UtcNow.AddSeconds(15);var frame=new DispatcherFrame();var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>{if(condition() || DateTime.UtcNow>=deadline)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();Check(condition(),"Native UI did not synchronize before timeout");
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private sealed record Context(WebApplication App,FriendApiClient A,FriendApiClient B,HttpClient HttpA,HttpClient HttpB,PackInvitation Card);
    private sealed record Inbox(string Email,string Code,string Action);
    private sealed class FixtureValidator:IPackReferenceValidator
    {public static PackReference Reference=new("fixtureProject","fixtureVersion",new string('a',128));public Task ValidateAsync(PackReference p,CancellationToken t){Check(p==Reference,"Unexpected fixture source");return Task.CompletedTask;}}
    private sealed class MemorySession:IFriendSessionStore
    {private FriendSession? _value;public Task<FriendSession?> ReadAsync(CancellationToken t)=>Task.FromResult(_value);public Task WriteAsync(FriendSession s,CancellationToken t){_value=s;return Task.CompletedTask;}public Task ClearAsync(CancellationToken t){_value=null;return Task.CompletedTask;}}
    private sealed class MemoryNotes:IFriendNotesStore
    {public Task<IReadOnlyDictionary<string,string>> ReadAsync(string u,CancellationToken t)=>Task.FromResult<IReadOnlyDictionary<string,string>>(new Dictionary<string,string>());public Task WriteAsync(string u,IReadOnlyDictionary<string,string> n,CancellationToken t)=>Task.CompletedTask;public Task ClearAsync(CancellationToken t)=>Task.CompletedTask;}
}
