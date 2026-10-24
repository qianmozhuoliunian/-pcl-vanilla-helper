using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PclVersionBridge.App;
using PclVersionBridge.App.Friends;
using PclVersionBridge.Core.Friends;

internal static class FriendsChecks
{
    internal static void Run()
    {
        var root=Path.Combine(Environment.CurrentDirectory,".artifacts","friends-check","state-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            FriendPreviewChecks.Run(root);
            FriendFeatureConfiguration Config(string? url,string? dev=null)=>FriendFeatureConfiguration.Load(root,name=>name=="VANILLAREACH_FRIENDS_SERVICE_URL"?url:name=="VANILLAREACH_FRIENDS_DEVELOPMENT"?dev:null);
            Check(Config(null).ServiceUri is null,"Missing service must remain unconfigured");
            Check(Config("http://service.example/","1").ServiceUri is null,"Public HTTP accepted");
            Check(Config("http://localhost/","1").ServiceUri is null,"Nonliteral loopback accepted");
            Check(Config("https://user:secret@service.example/").ServiceUri is null,"Credential URL accepted");
            Check(Config("https://service.example/nested").ServiceUri is null,"Ambiguous base path accepted");
            Check(Config("http://127.0.0.1:59132/").ServiceUri is null,"Loopback did not require opt-in");
            Check(Config("http://127.0.0.1:59132/","1").IsDevelopment,"Explicit local kit was not identified");
            Check(Config("https://service.example/").ServiceUri?.Scheme=="https","HTTPS service rejected");
            File.WriteAllBytes(Path.Combine(root,"friends-service.json"),new byte[4097]);
            Check(Config(null).ServiceUri is null,"Oversized config accepted");

            var sessions=new DpapiFriendSessionStore(Path.Combine(root,"credentials"));
            var session=new FriendSession("fixture-access-secret","fixture-refresh-secret",DateTimeOffset.UtcNow.AddMinutes(10),new("fixture-owner","本机测试夹具","ABCD-EFGH-JKLM"));
            sessions.WriteAsync(session,default).GetAwaiter().GetResult();
            Check(sessions.ReadAsync(default).GetAwaiter().GetResult()==session,"Protected session roundtrip failed");
            var sessionFile=Directory.GetFiles(Path.Combine(root,"credentials"),"*",SearchOption.AllDirectories).Single();
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(sessionFile)).Contains("fixture-access-secret",StringComparison.Ordinal),"Plaintext session persisted");
            File.WriteAllBytes(sessionFile,[1,2,3,4]);
            Check(sessions.ReadAsync(default).GetAwaiter().GetResult() is null,"Corrupt session accepted");
            File.WriteAllBytes(sessionFile,new byte[2*1024*1024+1]);
            Check(sessions.ReadAsync(default).GetAwaiter().GetResult() is null,"Oversized session accepted");
            sessions.ClearAsync(default).GetAwaiter().GetResult();

            var notes=new DpapiFriendNotesStore(Path.Combine(root,"notes"));
            notes.WriteAsync("owner-a",new Dictionary<string,string>{{"peer","备注测试"}},default).GetAwaiter().GetResult();
            Check(notes.ReadAsync("owner-a",default).GetAwaiter().GetResult()["peer"]=="备注测试","Local note roundtrip failed");
            Check(notes.ReadAsync("owner-b",default).GetAwaiter().GetResult().Count==0,"Notes leaked across users");
            notes.ClearAsync(default).GetAwaiter().GetResult();

            var application=Application.Current??new Application();
            application.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/VanillaReach;component/Styles.xaml")});
            var output=Path.Combine(Environment.CurrentDirectory,".artifacts","friends-check");Directory.CreateDirectory(output);
            foreach(var size in new[]{new Size(660,620),new Size(520,400)})
            {
                using var view=new FriendsView();
                view.Initialize(null,"尚未配置好友服务。原版安装和整合包功能可以继续使用。",false);
                var surface=new System.Windows.Controls.Border {Background=(Brush)application.Resources["AppBackground"],Padding=new Thickness(16),Child=view};
                surface.Measure(size);surface.Arrange(new Rect(size));surface.UpdateLayout();
                Check(view.ActualWidth<=size.Width+1,"Friends layout overflow");
                var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file=File.Create(Path.Combine(output,$"friends-unconfigured-{size.Width}x{size.Height}.png"));png.Save(file);
            }
            Console.WriteLine("PASS friends configuration: HTTPS and explicit literal loopback only");
            Console.WriteLine("PASS DPAPI session protection, corrupt/oversized rejection and owner-isolated notes");
            Console.WriteLine("PASS unconfigured friends page rendered at default/minimum content sizes");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
