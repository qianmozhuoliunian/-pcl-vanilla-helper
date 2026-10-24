using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PclVersionBridge.App;

internal static class FriendPreviewChecks
{
    internal static void Run(string root)
    {
        using var current=Process.GetCurrentProcess();
        var processFile=Path.Combine(root,"preview-process.json");
        using var http=new HttpClient(new HealthHandler(current.Id));
        var failures=new List<string>();
        void RejectsForeign(string reason)
        {
            try
            {
                if(FriendPreviewLauncher.CanReuseForKitAsync(http,processFile,current.MainModule!.FileName!).GetAwaiter().GetResult())failures.Add(reason);
            }
            catch(IOException) { }
        }
        RejectsForeign("healthy foreign service with no kit record was reused");
        File.WriteAllText(processFile,JsonSerializer.Serialize(new {Id=current.Id,StartTicks=current.StartTime.ToUniversalTime().Ticks-1}));
        RejectsForeign("stale process identity was reused");
        File.WriteAllText(processFile,JsonSerializer.Serialize(new {Id=current.Id,StartTicks=current.StartTime.ToUniversalTime().Ticks}));
        try
        {
            if(FriendPreviewLauncher.CanReuseForKitAsync(http,processFile,Path.Combine(root,"other-kit","server.exe")).GetAwaiter().GetResult())failures.Add("other-kit executable was reused");
        }
        catch(IOException) { }
        if(!FriendPreviewLauncher.CanReuseForKitAsync(http,processFile,current.MainModule!.FileName!).GetAwaiter().GetResult())failures.Add("owned healthy service was rejected");
        using var foreignHttp=new HttpClient(new HealthHandler(-1));
        try
        {
            if(FriendPreviewLauncher.CanReuseForKitAsync(foreignHttp,processFile,current.MainModule!.FileName!).GetAwaiter().GetResult())failures.Add("health responder did not match recorded process");
        }
        catch(IOException) { }
        if(FriendPreviewLauncher.StopOwnedServerAsync(processFile,Path.Combine(root,"other-kit","server.exe")).GetAwaiter().GetResult())failures.Add("stop claimed ownership of another executable");
        if(current.HasExited)failures.Add("foreign process was stopped");
        if(FriendPreviewLauncher.StopOwnedServerAsync(processFile,current.MainModule!.FileName!).GetAwaiter().GetResult())failures.Add("missing service was reported as stopped");
        if(failures.Count!=0)throw new InvalidOperationException(string.Join("; ",failures));
        Console.WriteLine("PASS preview kit reuses only its own live process and matching health responder");
    }
    private sealed class HealthHandler(int processId):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(JsonSerializer.Serialize(new {service="VanillaReach.Friends",status="ready",processId}),Encoding.UTF8,"application/json")});
    }
}
