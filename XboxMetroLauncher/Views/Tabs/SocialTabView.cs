using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace XboxMetroLauncher.Views.Tabs;

public class SocialTabView : UserControl, IComponentConnector
{
	internal Canvas DashboardCanvas;

	internal Canvas HomeAlignedFrame;

	internal Canvas SocialArtLayer;

	internal Canvas ContentTileLayer;

	private bool _contentLoaded;

	public SocialTabView()
	{
		InitializeComponent();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.26.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/XboxMetroLauncher;V1.0.0.0;component/views/tabs/socialtabview.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.26.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			DashboardCanvas = (Canvas)target;
			break;
		case 2:
			HomeAlignedFrame = (Canvas)target;
			break;
		case 3:
			SocialArtLayer = (Canvas)target;
			break;
		case 4:
			ContentTileLayer = (Canvas)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
