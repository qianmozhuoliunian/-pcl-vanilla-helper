using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PclVersionBridge.App;
using PclVersionBridge.App.Friends;
using PclVersionBridge.Core.Presentation;

internal static class DesktopPolishChecks
{
    internal static void Run()
    {
        Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var window=new MainWindow();
        try
        {
            var nav=window.FindName("Navigation") as ListBox ?? throw new InvalidOperationException("Missing keyboard-accessible desktop navigation");
            var pages=(TabControl)window.FindName("Pages");
            var directory=(Border)window.FindName("DirectoryCard");
            var surface=(FrameworkElement)window.Content;
            surface.Measure(new Size(980,740));surface.Arrange(new Rect(0,0,980,740));surface.UpdateLayout();Pump();
            for(var index=0;index<3;index++)
            {
                nav.SelectedIndex=index;Pump();surface.UpdateLayout();
                Check(pages.SelectedIndex==index,"Navigation did not select requested page");
                Check(directory.Visibility==(index==2?Visibility.Collapsed:Visibility.Visible),"Directory visibility disagrees with page");
                Render(surface,$"page-{index}-980x740",980,740,1);
            }
            nav.SelectedIndex=0;Pump();
            var gate=(InstallationGate)typeof(MainWindow).GetField("_gate",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            using(gate.Acquire())
            {
                Check(!nav.IsEnabled,"Navigation stayed interactive during installation");
                pages.SelectedIndex=1;Pump();Check(pages.SelectedIndex==0,"Shared installation gate allowed page switch");
            }
            Check(nav.IsEnabled,"Navigation did not recover after installation lock");
            foreach(var size in new[]{new Size(740,570),new Size(980,740)})
            {
                surface.Measure(size);surface.Arrange(new Rect(size));surface.UpdateLayout();
                foreach(var scale in new[]{1.0,1.25,1.5})Render(surface,$"vanilla-{size.Width}x{size.Height}-{scale:0.##}",size.Width,size.Height,scale);
            }
            Console.WriteLine("PASS desktop navigation, directory visibility and installation lock/recovery; default/minimum/scaled WPF renders");
            ThemeControlChecks.Run();
            var dialog=new FriendDialog(null,"登录好友账号","原生账号窗口的视觉检查；未提交登录信息。","登录",(_,_)=>Task.FromResult(false));
            dialog.Input("email","邮箱");dialog.Password("password","密码");dialog.Check("remember","在这台电脑记住登录");
            var dialogBody=(FrameworkElement)dialog.Content;dialog.Content=null;
            var dialogSurface=new Border {Background=(Brush)Application.Current.FindResource("AppBackground"),Child=dialogBody};
            Render(dialogSurface,"account-dialog",460,480);dialogSurface.Child=null;dialog.Content=dialogBody;dialog.Close();
        }
        finally {window.Close();}
    }
    internal static void Render(FrameworkElement element,string name,double width,double height,double scale=1)
    {
        element.Measure(new Size(width,height));element.Arrange(new Rect(0,0,width,height));element.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width*scale),(int)Math.Ceiling(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory=Path.GetFullPath(".artifacts/desktop-polish");Directory.CreateDirectory(directory);
        using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
