using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PclVersionBridge.App;
using PclVersionBridge.Core.Presentation;

internal static class DesktopLivePreview
{
    internal static void Run()
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/VanillaReach;component/Styles.xaml")});
        var window=new MainWindow {WindowStartupLocation=WindowStartupLocation.Manual,Left=-3000,Top=0,ShowInTaskbar=false,ShowActivated=false};
        try
        {
            window.Show();var vanilla=(MainViewModel)window.DataContext;
            PumpUntil(()=>!vanilla.IsBusy && vanilla.Versions.Count>0,TimeSpan.FromSeconds(90));
            vanilla.DirectoryPath="";
            var pages=(TabControl)window.FindName("Pages");var surface=(FrameworkElement)window.Content;
            pages.SelectedIndex=0;Pump();
            DesktopPolishChecks.Render(surface,"live-vanilla",surface.ActualWidth,surface.ActualHeight);
            var packs=(ModpackViewModel)typeof(MainWindow).GetField("_packs",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
            pages.SelectedIndex=1;packs.Query="Fabulously Optimized";
            var operation=Dispatcher.CurrentDispatcher.InvokeAsync(async()=>await packs.SearchAsync());
            PumpUntil(()=>operation.Task.IsCompleted && operation.Task.Result.IsCompleted,TimeSpan.FromSeconds(90));
            operation.Task.GetAwaiter().GetResult().GetAwaiter().GetResult();
            if(packs.Projects.Count==0)throw new InvalidOperationException("Live source returned no project; no placeholder results fabricated");
            if(packs.Projects[0].IconUri is { } icon && ProjectImage.Cache is { } cache)
            {
                var image=Dispatcher.CurrentDispatcher.InvokeAsync(async()=>await cache.LoadAsync(icon,packs.Projects[0].Id,default));
                PumpUntil(()=>image.Task.IsCompleted && image.Task.Result.IsCompleted,TimeSpan.FromSeconds(30));
                image.Task.GetAwaiter().GetResult().GetAwaiter().GetResult();Pump();
            }
            DesktopPolishChecks.Render(surface,"live-modpacks",surface.ActualWidth,surface.ActualHeight);
            Console.WriteLine("PASS real official release list and Modrinth search rendered; no game installation or account submission");
        }
        finally {window.Close();}
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void PumpUntil(Func<bool> condition,TimeSpan limit)
    {
        var deadline=DateTime.UtcNow+limit;var frame=new DispatcherFrame();var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(40)};
        timer.Tick+=(_,_)=>{if(condition() || DateTime.UtcNow>=deadline)frame.Continue=false;};
        timer.Start();Dispatcher.PushFrame(frame);timer.Stop();if(!condition())throw new TimeoutException("Live UI source unavailable before deadline");
    }
}
