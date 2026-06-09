using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher;

public class App : Application
{
	private bool _contentLoaded;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.DispatcherUnhandledException += OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs args)
		{
			LogException(args.ExceptionObject as Exception, "AppDomain");
		};
		TaskScheduler.UnobservedTaskException += delegate(object? _, UnobservedTaskExceptionEventArgs args)
		{
			LogException(args.Exception, "TaskScheduler");
			args.SetObserved();
		};
		base.OnStartup(e);
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		LogException(e.Exception, "Dispatcher");
		e.Handled = true;
	}

	internal static void LogException(Exception? exception, string source)
	{
		if (exception == null)
		{
			return;
		}
		try
		{
			File.AppendAllText(Path.Combine(AppPaths.LogsFolder, "crash.log"), $"[{DateTimeOffset.Now:u}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
		}
		catch
		{
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.26.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			base.StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
			Uri resourceLocator = new Uri("/XboxMetroLauncher;V1.0.0.0;component/app.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.26.0")]
	public static void Main()
	{
		App app = new App();
		app.InitializeComponent();
		app.Run();
	}
}
