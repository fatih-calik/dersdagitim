using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DersDagitim.Views;

public partial class DistributionProgressWindow : Window
{
    private readonly List<string> _logLines = new();
    private Storyboard? _spinStoryboard;
    private double _progressTrackWidth;

    public bool IsCompleted { get; private set; } = false;
    public bool HasError { get; private set; } = false;
    public bool RetryRequested { get; private set; } = false;
    public string? ErrorMessage { get; private set; }

    public DistributionProgressWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartSpinAnimation();

        // Track the actual width of the progress track for fill calculations
        _progressTrackWidth = ProgressFill.Parent is FrameworkElement parent ? parent.ActualWidth : 436;
    }

    private void StartSpinAnimation()
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(1.5),
            RepeatBehavior = RepeatBehavior.Forever
        };

        _spinStoryboard = new Storyboard();
        _spinStoryboard.Children.Add(animation);
        Storyboard.SetTarget(animation, IconCanvas);
        Storyboard.SetTargetProperty(animation, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        _spinStoryboard.Begin();
    }

    private void StopSpinAnimation()
    {
        _spinStoryboard?.Stop();
    }

    public void UpdateStatus(string status)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = status;

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logLines.Add($"[{timestamp}] {status}");
            LogText.Text = string.Join("\n", _logLines);
            LogScroller.ScrollToEnd();

            UpdateProgressFromStatus(status);
        });
    }

    private void UpdateProgressFromStatus(string status)
    {
        int progress = 0;

        if (status.Contains("AI")) progress = 5;
        else if (status.Contains("Veriler")) progress = 15;
        else if (status.Contains("Model")) progress = 30;
        else if (status.Contains("Çakışma") || status.Contains("Kısıt")) progress = 45;
        else if (status.Contains("Planlayıcı")) progress = 60;
        else if (status.Contains("Yerleşim") || status.Contains("Kayded")) progress = 80;
        else if (status.Contains("Öğretmen Saat")) progress = 90;
        else if (status.Contains("Tamamlandı")) progress = 100;
        else if (status.Contains("HATA") || status.Contains("Bulunamadı"))
        {
            HasError = true;
            progress = 100;
        }

        if (progress > 0)
        {
            var currentPercent = GetCurrentPercent();
            if (progress >= currentPercent)
            {
                SetProgress(progress);
                ProgressText.Text = $"%{progress}";
                ProgressLabel.Text = status.Length > 45 ? status[..45] + "..." : status;
            }
        }

        if (progress >= 100)
        {
            IsCompleted = true;
            StopSpinAnimation();

            if (HasError)
            {
                ShowErrorState();
            }
            else
            {
                ShowSuccessState();
            }
        }
    }

    private int GetCurrentPercent()
    {
        if (_progressTrackWidth <= 0) return 0;
        return (int)(ProgressFill.Width / _progressTrackWidth * 100);
    }

    private void SetProgress(int percent)
    {
        // Re-measure track width in case layout changed
        if (ProgressFill.Parent is FrameworkElement parent && parent.ActualWidth > 0)
            _progressTrackWidth = parent.ActualWidth;

        var targetWidth = _progressTrackWidth * percent / 100.0;

        var anim = new DoubleAnimation
        {
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        ProgressFill.BeginAnimation(WidthProperty, anim);
    }

    private void ShowSuccessState()
    {
        // Change status text
        StatusText.Text = "Tamamlandı. Sorun bulunamadı.";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // #10B981

        // Change icon to checkmark
        StopSpinAnimation();
        IconCanvas.RenderTransform = new RotateTransform(0, 12, 12);
        IconPath.Data = Geometry.Parse("M6,12 L10,16 L18,8");
        IconPath.StrokeThickness = 2.5;

        // Change icon background to green gradient
        IconBorder.Background = new LinearGradientBrush(
            Color.FromRgb(16, 185, 129),  // #10B981
            Color.FromRgb(5, 150, 105),   // #059669
            45);

        // Change progress bar to green
        ProgressGradStart.Color = Color.FromRgb(16, 185, 129);
        ProgressGradEnd.Color = Color.FromRgb(5, 150, 105);

        ProgressLabel.Text = "Tüm işlemler başarıyla tamamlandı";

        // Show close button
        BtnClose.Visibility = Visibility.Visible;
    }

    private void ShowErrorState()
    {
        // Change status text
        StatusText.Text = "Hata ile sonuçlandı";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // #EF4444

        // Change icon to X
        StopSpinAnimation();
        IconCanvas.RenderTransform = new RotateTransform(0, 12, 12);
        IconPath.Data = Geometry.Parse("M7,7 L17,17 M17,7 L7,17");
        IconPath.StrokeThickness = 2.5;

        // Change icon background to red gradient
        IconBorder.Background = new LinearGradientBrush(
            Color.FromRgb(239, 68, 68),   // #EF4444
            Color.FromRgb(220, 38, 38),   // #DC2626
            45);

        // Change progress bar to red
        ProgressGradStart.Color = Color.FromRgb(239, 68, 68);
        ProgressGradEnd.Color = Color.FromRgb(220, 38, 38);

        ProgressLabel.Text = "İşlem başarısız oldu";

        // Show error buttons
        BtnCloseError.Visibility = Visibility.Visible;
        BtnRetry.Visibility = Visibility.Visible;
    }

    public void SetError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            HasError = true;
            ErrorMessage = message;

            _logLines.Add($"[{DateTime.Now:HH:mm:ss}] HATA: {message}");
            LogText.Text = string.Join("\n", _logLines);
            LogScroller.ScrollToEnd();

            SetProgress(100);
            ProgressText.Text = "HATA";
            IsCompleted = true;

            ShowErrorState();
        });
    }

    public void SetComplete(int placedCount, int totalCount)
    {
        Dispatcher.Invoke(() =>
        {
            IsCompleted = true;

            _logLines.Add($"[{DateTime.Now:HH:mm:ss}] BAŞARILI: {placedCount}/{totalCount} blok yerleştirildi.");
            LogText.Text = string.Join("\n", _logLines);
            LogScroller.ScrollToEnd();

            SetProgress(100);
            ProgressText.Text = "%100";

            ShowSuccessState();
            StatusText.Text = $"Tamamlandı! ({placedCount}/{totalCount} blok yerleştirildi)";
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        RetryRequested = true;
        this.Close();
    }
}
