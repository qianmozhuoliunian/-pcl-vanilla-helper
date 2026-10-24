using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PclVersionBridge.App;
using PclVersionBridge.Core.Modpacks;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if(args.Contains("--live-ui")){DesktopLivePreview.Run();return;}
        var bitmap=new RenderTargetBitmap(16,16,96,96,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();using(var context=visual.RenderOpen())context.DrawRectangle(Brushes.Blue,null,new Rect(0,0,16,16));bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var buffer=new MemoryStream();encoder.Save(buffer);var png=buffer.ToArray();
        var tall=new RenderTargetBitmap(8,4096,96,96,PixelFormats.Pbgra32);var tallEncoder=new PngBitmapEncoder();tallEncoder.Frames.Add(BitmapFrame.Create(tall));using var tallBuffer=new MemoryStream();tallEncoder.Save(tallBuffer);var tallPng=tallBuffer.ToArray();
        var requests=new List<string>();
        using var client=new HttpClient(new Handler(r=>
        {
            requests.Add(r.RequestUri!.AbsolutePath);
            var content=new ByteArrayContent(r.RequestUri.AbsolutePath.EndsWith("bad.webp")?new byte[]{1,2,3,4}:r.RequestUri.AbsolutePath.EndsWith("tall.png")?tallPng:png);
            content.Headers.ContentType=new MediaTypeHeaderValue("image/png");return new(HttpStatusCode.OK){Content=content};
        }));
        var cache=new ModpackImageCache(new ModpackNetwork(client),new Catalog());
        var source=new Uri("https://cdn.modrinth.com/bad.webp");
        var image=cache.LoadAsync(source,"abc12345",default).GetAwaiter().GetResult();
        if(image.PixelWidth<=0 || !image.IsFrozen || requests.Last()!="/actual-source.png")throw new Exception("Image fallback did not use provided source");
        var count=requests.Count;cache.LoadAsync(source,"abc12345",default).GetAwaiter().GetResult();
        if(requests.Count!=count)throw new Exception("Image cache missed");
        var thumbnail=cache.LoadAsync(new Uri("https://cdn.modrinth.com/tall.png"),null,default).GetAwaiter().GetResult();
        if(thumbnail.PixelWidth>96 || thumbnail.PixelHeight>96 || thumbnail.PixelWidth>8)throw new Exception($"Tall image enlarged to {thumbnail.PixelWidth}x{thumbnail.PixelHeight}");
        Console.WriteLine("PASS WPF decode, source-only raw fallback, frozen thumbnail and cache reuse");
        Console.WriteLine("PASS tall image stays within 96x96 without upscaling");
        FriendsChecks.Run();
        DesktopPolishChecks.Run();
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(respond(request));}
    private sealed class Catalog:IModrinthCatalog
    {
        public Task<ModpackSearchPage> SearchAsync(string q,string? mc,int offset,CancellationToken token)=>throw new NotSupportedException();
        public Task<IReadOnlyList<ModpackRelease>> GetVersionsAsync(string p,bool pre,CancellationToken token)=>throw new NotSupportedException();
        public Task<ModpackDetails> GetProjectAsync(string id,CancellationToken token)=>Task.FromResult(new ModpackDetails(id,"Fixture","Fixture",null,1,Array.Empty<string>(),Array.Empty<string>()){RawIconUri=new Uri("https://cdn.modrinth.com/actual-source.png")});
    }
}
