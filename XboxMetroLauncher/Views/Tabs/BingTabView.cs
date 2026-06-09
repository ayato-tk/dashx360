using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using XboxMetroLauncher.ViewModels.Tabs;

namespace XboxMetroLauncher.Views.Tabs;

public class BingTabView : UserControl, IComponentConnector
{
	internal Canvas DashboardCanvas;

	internal Canvas HomeAlignedFrame;

	internal Canvas ContentTileLayer;

	internal Border SearchBoxFrame;

	internal TextBox BingSearchBox;

	private bool _contentLoaded;

	public BingTabView()
	{
		InitializeComponent();
	}

	private void SearchBoxFrame_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (!BingSearchBox.IsKeyboardFocusWithin)
		{
			BingSearchBox.Focus();
			BingSearchBox.CaretIndex = BingSearchBox.Text.Length;
			e.Handled = true;
		}
	}

	private void BingSearchBox_OnKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return && base.DataContext is BingTabViewModel bingTabViewModel && bingTabViewModel.SubmitSearchCommand.CanExecute(null))
		{
			bingTabViewModel.SubmitSearchCommand.Execute(null);
			e.Handled = true;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.26.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/XboxMetroLauncher;V1.0.0.0;component/views/tabs/bingtabview.xaml", UriKind.Relative);
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
			ContentTileLayer = (Canvas)target;
			break;
		case 4:
			SearchBoxFrame = (Border)target;
			SearchBoxFrame.MouseLeftButtonDown += SearchBoxFrame_OnMouseLeftButtonDown;
			break;
		case 5:
			BingSearchBox = (TextBox)target;
			BingSearchBox.KeyDown += BingSearchBox_OnKeyDown;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
