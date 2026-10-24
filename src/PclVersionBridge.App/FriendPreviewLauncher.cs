using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace PclVersionBridge.App;

/// <summary>Explicit local kit entry point. Never used by normal application startup.</summary>
internal static class FriendPreviewLauncher
{
    private static readonly Uri Origin=new("http://127.0.0.1:59132/");
    internal static async Task RunAsync(string action)
    {
        if(action is not ("A" or "B" or "stop" or "mail"))throw new ArgumentException("Unknown preview action");
        var kit=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));
        var server=Path.Combine(kit,"server","VanillaReach.Friends.Server.exe");
        if(!File.Exists(server))throw new FileNotFoundException("Full local kit required");
        var state=Path.Combine(kit,".friends-local");Directory.CreateDirectory(state);
        var processFile=Path.Combine(state,"server-process.json");
        if(action=="mail") {ShowInbox(Path.Combine(state,"inbox"));return;}
        using var startLease=await AcquireStartLease(Path.Combine(state,"server-start.lock"));
        if(action=="stop")
        {
            var stopped=await StopOwnedServerAsync(processFile,server);
            MessageBox.Show(stopped ? "这份套件的本机好友测试服务已停止。" : "未找到由这份套件启动的服务；未停止其他进程。","本机好友测试");return;
        }
        using var http=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false,UseCookies=false}) {Timeout=TimeSpan.FromSeconds(1)};
        if(!await CanReuseForKitAsync(http,processFile,server))
        {
            var start=new ProcessStartInfo(server) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(server)!};
            start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";
            start.Environment["ASPNETCORE_URLS"]=Origin.AbsoluteUri;
            start.Environment["Friends__PublicOrigin"]=Origin.AbsoluteUri;
            start.Environment["Friends__DataDirectory"]=Path.Combine(state,"data");
            start.Environment["Friends__DevelopmentInbox"]=Path.Combine(state,"inbox");
            using var process=Process.Start(start)??throw new IOException("Server not started");
            await File.WriteAllTextAsync(processFile,JsonSerializer.Serialize(new PreviewProcess(process.Id,process.StartTime.ToUniversalTime().Ticks)));
            var ready=false;
            for(var attempt=0;attempt<30;attempt++)
            {
                if(process.HasExited)break;
                if(await CanReuseForKitAsync(http,processFile,server)){ready=true;break;}
                await Task.Delay(200);
            }
            if(!ready)
            {
                await StopOwnedServerAsync(processFile,server);
                throw new IOException("本机好友测试服务未能启动。请检查端口 59132 是否被占用。");
            }
        }
        var client=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"VanillaReach.exe")) {UseShellExecute=false};
        client.Environment["VANILLAREACH_FRIENDS_SERVICE_URL"]=Origin.AbsoluteUri;
        client.Environment["VANILLAREACH_FRIENDS_DEVELOPMENT"]="1";
        client.Environment["VANILLAREACH_PROFILE_DIRECTORY"]=Path.Combine(state,"profile-"+action.ToLowerInvariant());
        Process.Start(client)?.Dispose();
    }
    internal static async Task<bool> CanReuseForKitAsync(HttpClient http,string processFile,string expectedExecutable)
    {
        var responder=await HealthyProcessIdAsync(http);
        if(responder is null)return false;
        var info=ReadProcess(processFile);
        if(info is null || responder!=info.Id || !IsOwnedProcess(info,expectedExecutable))
            throw new IOException("端口 59132 正由其他服务或另一份测试套件使用。请先在那份套件中停止服务，再打开本套件。");
        return true;
    }
    private static bool IsOwnedProcess(PreviewProcess info,string executable)
    {
        try
        {
            using var process=Process.GetProcessById(info.Id);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks==info.StartTicks &&
                string.Equals(process.MainModule?.FileName,Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) {return false;}
    }
    internal static async Task<bool> StopOwnedServerAsync(string processFile,string expectedExecutable)
    {
        var info=ReadProcess(processFile);
        if(info is null)return false;
        var stopped=false;
        if(IsOwnedProcess(info,expectedExecutable))
        {
            try
            {
                using var process=Process.GetProcessById(info.Id);
                // Recheck after opening a fresh handle; a PID may have been reused.
                if(process.StartTime.ToUniversalTime().Ticks==info.StartTicks && string.Equals(process.MainModule?.FileName,Path.GetFullPath(expectedExecutable),StringComparison.OrdinalIgnoreCase))
                {process.Kill();await process.WaitForExitAsync();stopped=true;}
            }
            catch(ArgumentException) { }
        }
        File.Delete(processFile);
        return stopped;
    }
    private static async Task<FileStream> AcquireStartLease(string path)
    {
        for(var attempt=0;attempt<50;attempt++)
        {
            try {return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException) when(attempt<49) {await Task.Delay(200);}
        }
        throw new IOException("Preview launch busy");
    }
    private static async Task<int?> HealthyProcessIdAsync(HttpClient http)
    {
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(1));
            using var response=await http.GetAsync(new Uri(Origin,"health"),HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(!response.IsSuccessStatusCode || response.Content.Headers.ContentLength>4096)return null;
            using var input=await response.Content.ReadAsStreamAsync(timeout.Token);var bytes=new byte[4097];var length=0;
            while(length<bytes.Length){var read=await input.ReadAsync(bytes.AsMemory(length),timeout.Token);if(read==0)break;length+=read;}
            if(length>4096)return null;
            using var json=JsonDocument.Parse(bytes.AsMemory(0,length));
            if(!json.RootElement.TryGetProperty("service",out var value) || value.ValueKind!=JsonValueKind.String || value.GetString()!="VanillaReach.Friends")return null;
            return json.RootElement.TryGetProperty("processId",out var pid) && pid.ValueKind==JsonValueKind.Number && pid.TryGetInt32(out var id) && id>0 ? id : 0;
        }
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or JsonException or IOException) {return null;}
    }
    private static PreviewProcess? ReadProcess(string path)
    {
        if(!File.Exists(path) || new FileInfo(path).Length>4096)return null;
        try {return JsonSerializer.Deserialize<PreviewProcess>(File.ReadAllBytes(path));}catch(JsonException){return null;}
    }
    private static void ShowInbox(string directory)
    {
        var emails=new List<PreviewEmail>();
        if(Directory.Exists(directory))
            foreach(var file in Directory.EnumerateFiles(directory,"*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(50))
            {
                if(new FileInfo(file).Length>32768)continue;
                try
                {
                    using var json=JsonDocument.Parse(File.ReadAllBytes(file));var item=json.RootElement;
                    emails.Add(new(item.GetProperty("email").GetString()??"",item.GetProperty("action").GetString()??"",item.GetProperty("code").GetString()??""));
                }
                catch(Exception ex) when(ex is JsonException or KeyNotFoundException or IOException) { }
            }
        var window=new Window {Title="本机测试收件箱（邮件没有对外发送）",Width=620,Height=460,MinWidth=460,MinHeight=340,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        var panel=new DockPanel {Margin=new Thickness(16)};
        var hint=new TextBlock {Text="仅用于本机测试。选中邮件，复制代码到好友页确认邮箱或重置密码。重新打开此窗口可刷新。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(hint,Dock.Top);panel.Children.Add(hint);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var copy=new Button {Content="复制代码",Margin=new Thickness(0,0,8,0)};var close=new Button {Content="关闭",IsCancel=true};buttons.Children.Add(copy);buttons.Children.Add(close);close.Click+=(_,_)=>window.Close();DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);
        var code=new TextBox {IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=120,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(code,Dock.Bottom);panel.Children.Add(code);
        var list=new ListBox {ItemsSource=emails,DisplayMemberPath=nameof(PreviewEmail.Label)};
        list.SelectionChanged+=(_,_)=>code.Text=(list.SelectedItem as PreviewEmail)?.Code??"";
        copy.Click+=(_,_)=> {if(!string.IsNullOrEmpty(code.Text))try{Clipboard.SetText(code.Text);}catch(System.Runtime.InteropServices.COMException){}};
        panel.Children.Add(list);window.Content=panel;if(emails.Count>0)list.SelectedIndex=0;window.ShowDialog();
    }
    private sealed record PreviewProcess(int Id,long StartTicks);
    private sealed record PreviewEmail(string Email,string Action,string Code) {public string Label=>Email+" · "+Action;}
}
