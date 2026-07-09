using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace XboxMetroLauncher.Views;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _hideTimer;

    public ToastWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            BeginHide();
        };
    }

    public void ShowToast(string title, string body)
    {
        ToastTitle.Text = title;
        ToastBody.Text = body;

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Bottom - ActualHeight - 64;

        ToastRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        });
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void BeginHide()
    {
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (ToastRoot.Opacity < 0.05)
            {
                Hide();
            }
        };
        ToastRoot.BeginAnimation(OpacityProperty, fadeOut);
    }
}
