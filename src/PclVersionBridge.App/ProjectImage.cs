using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PclVersionBridge.App;

public sealed class ProjectImage : Border
{
    public static ModpackImageCache? Cache {get;set;}
    public static readonly DependencyProperty SourceUriProperty=DependencyProperty.Register(nameof(SourceUri),typeof(Uri),typeof(ProjectImage),new PropertyMetadata(null,Changed));
    public static readonly DependencyProperty ProjectIdProperty=DependencyProperty.Register(nameof(ProjectId),typeof(string),typeof(ProjectImage),new PropertyMetadata(null,Changed));
    public Uri? SourceUri {get=>(Uri?)GetValue(SourceUriProperty);set=>SetValue(SourceUriProperty,value);}
    public string? ProjectId {get=>(string?)GetValue(ProjectIdProperty);set=>SetValue(ProjectIdProperty,value);}
    private readonly Image _image=new(){Stretch=Stretch.Uniform};
    private readonly TextBlock _placeholder=new(){Text="暂无图片",FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(82,100,123)),TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    private CancellationTokenSource? _cancellation;
    public ProjectImage()
    {
        SetResourceReference(BackgroundProperty,"SurfaceInset");CornerRadius=new CornerRadius(10);Padding=new Thickness(3);
        _placeholder.SetResourceReference(TextBlock.ForegroundProperty,"TextSecondary");
        var grid=new Grid();grid.Children.Add(_placeholder);grid.Children.Add(_image);Child=grid;
        Loaded+=(_,_)=>Start();Unloaded+=(_,_)=>{_cancellation?.Cancel();};
    }
    private static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        var control=(ProjectImage)value;
        control._image.Source=null;control._placeholder.Text=control.SourceUri is null?"暂无图片":"待加载";
        control._placeholder.Visibility=Visibility.Visible;
        if(control.IsLoaded)control.Start();
    }
    private async void Start()
    {
        _cancellation?.Cancel();
        using var cancellation=new CancellationTokenSource();_cancellation=cancellation;
        _image.Source=null;_placeholder.Visibility=Visibility.Visible;
        if(SourceUri is null || Cache is null){_placeholder.Text="暂无图片";_cancellation=null;return;}
        _placeholder.Text="加载中";
        try
        {
            var source=await Cache.LoadAsync(SourceUri,ProjectId,cancellation.Token);
            if(cancellation.IsCancellationRequested)return;
            _image.Source=source;_placeholder.Visibility=Visibility.Collapsed;
        }
        catch(OperationCanceledException) {if(!cancellation.IsCancellationRequested)_placeholder.Text="图片超时";}
        catch(Exception ex) when(ModpackImageCache.IsImageFailure(ex)){if(!cancellation.IsCancellationRequested)_placeholder.Text="图片不可用";}
        finally {if(ReferenceEquals(_cancellation,cancellation))_cancellation=null;}
    }
}
