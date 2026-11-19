using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace OffWorkWidget.WinUI;

public partial class App : Application
{
    private MainWindow? window;
    public App()
    {
        UnhandledException += (_, e) => RecordFailure(e.Exception);
        // Threads outside the XAML dispatcher do not reach UnhandledException;
        // record them instead of leaving an unexplained process exit.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            RecordEvent("unhandled-appdomain " + (e.ExceptionObject as Exception)?.GetType().Name + " terminating=" + e.IsTerminating);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            string detail = e.Exception.InnerException?.ToString() ?? e.Exception.ToString();
            if (detail.Length > 400) detail = detail.Substring(0, 400);
            RecordEvent("unobserved-task " + detail.Replace('\r', ' ').Replace('\n', ' '));
            e.SetObserved();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, __) => RecordEvent("process-exit");
        InitializeComponent();
    }
    internal static void RecordEvent(string note)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetEnvironmentVariable("OFFWORKWIDGET_TEST_DATA") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OffWorkWidget"),
                "diagnostics");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "lifecycle.log");
            var info = new System.IO.FileInfo(path);
            if (info.Exists && info.Length > 96 * 1024) System.IO.File.Delete(path);
            File.AppendAllText(path, DateTime.Now.ToString("O") + " " + note + Environment.NewLine, new System.Text.UTF8Encoding(false));
        }
        catch { }
    }
    internal static void RecordFailure(Exception error)
    {
        try
        {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OffWorkWidget","diagnostics");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder,"startup-errors.log"),DateTime.Now.ToString("O")+Environment.NewLine+error.GetType().FullName+" HResult="+error.HResult.ToString("X8")+Environment.NewLine+(error is Microsoft.UI.Xaml.Markup.XamlParseException ? error.Message+Environment.NewLine : "")+error.StackTrace+Environment.NewLine,new System.Text.UTF8Encoding(true));
        }
        catch { }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { window=new MainWindow();window.Activate(); }
        catch(Exception ex)
        {
            RecordFailure(ex);
            MessageBox(IntPtr.Zero,"启动失败，诊断已记录到 %LOCALAPPDATA%\\OffWorkWidget\\diagnostics\\startup-errors.log。\n错误代码："+ex.HResult.ToString("X8"),"有盼",0x10);
            Exit();
        }
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int MessageBox(IntPtr owner,string text,string title,uint flags);
}