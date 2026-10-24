using System.IO;
using System.Windows.Media.Imaging;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Modpacks;

namespace PclVersionBridge.App;

public sealed class ModpackImageCache(ModpackNetwork network, IModrinthCatalog catalog)
{
    private readonly object _sync=new();
    private readonly Dictionary<string,BitmapSource> _cache=new();
    private readonly Queue<string> _order=new();
    private readonly SemaphoreSlim _slots=new(3);

    public async Task<BitmapSource> LoadAsync(Uri source,string? projectId,CancellationToken token)
    {
        var key=projectId+"|"+source.AbsoluteUri;
        lock(_sync)if(_cache.TryGetValue(key,out var cached))return cached;
        await _slots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock(_sync)if(_cache.TryGetValue(key,out var cached))return cached;
            BitmapSource image;
            try { image=await LoadUri(source,token).ConfigureAwait(false); }
            catch(Exception ex) when(!token.IsCancellationRequested && IsImageFailure(ex) && !string.IsNullOrEmpty(projectId))
            {
                // Ask the source for its original image; never guess a filename or URL.
                var details=await catalog.GetProjectAsync(projectId,token).ConfigureAwait(false);
                var original=details.RawIconUri;
                if(original is null || original==source) throw new InstallException("项目图片暂不可用。",ex);
                image=await LoadUri(original,token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            lock(_sync)
            {
                if(!_cache.ContainsKey(key))
                {
                    while(_cache.Count>=64)_cache.Remove(_order.Dequeue());
                    _cache[key]=image;_order.Enqueue(key);
                }
            }
            return image;
        }
        finally {_slots.Release();}
    }
    private async Task<BitmapSource> LoadUri(Uri uri,CancellationToken token)
    {
        var bytes=await network.GetImageBytesAsync(uri,token).ConfigureAwait(false);
        return await Task.Run(()=>Decode(bytes),token).ConfigureAwait(false);
    }
    private static BitmapSource Decode(byte[] bytes)
    {
        using var headerStream=new MemoryStream(bytes,writable:false);
        var decoder=BitmapDecoder.Create(headerStream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
        var frame=decoder.Frames[0];
        if(frame.PixelWidth is <1 or >4096 || frame.PixelHeight is <1 or >4096 || (long)frame.PixelWidth*frame.PixelHeight>4_194_304)
            throw new InstallException("项目图片尺寸过大。");
        using var stream=new MemoryStream(bytes,writable:false);
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
        if(frame.PixelWidth>=frame.PixelHeight) image.DecodePixelWidth=Math.Min(96,frame.PixelWidth);
        else image.DecodePixelHeight=Math.Min(96,frame.PixelHeight);
        image.StreamSource=stream;image.EndInit();
        if(image.PixelWidth>96 || image.PixelHeight>96)throw new InstallException("项目缩略图尺寸无效。");
        image.Freeze();return image;
    }
    internal static bool IsImageFailure(Exception ex)=>ex is InstallException or IOException or NotSupportedException or ArgumentException or FormatException
        or InvalidOperationException or System.Net.Http.HttpRequestException or System.Runtime.InteropServices.COMException;
}
