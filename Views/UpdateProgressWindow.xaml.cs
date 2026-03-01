using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DersDagitim.Services;

namespace DersDagitim.Views;

public partial class UpdateProgressWindow : Window
{
    private CancellationTokenSource? _downloadCts;
    private Storyboard? _spinStoryboard;
    private double _progressTrackWidth;
    private readonly List<string> _logLines = new();

    public UpdateProgressWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _progressTrackWidth = ProgressFill.Parent is FrameworkElement parent
            ? parent.ActualWidth : 436;

        StartSpinAnimation();
        await StartDownload();
    }

    private async Task StartDownload()
    {
        _downloadCts = new CancellationTokenSource();

        try
        {
            AddLog("Güncelleme sunucusuna bağlanılıyor...");

            var progress = new Progress<(int percent, string status)>(p =>
            {
                if (p.percent >= 0)
                {
                    SetProgress(p.percent);
                    ProgressText.Text = $"%{p.percent}";
                }
                ProgressLabel.Text = p.status;
            });

            var zipPath = await UpdateService.DownloadUpdateAsync(progress, _downloadCts.Token);

            StopSpinAnimation();
            BtnCancel.Visibility = Visibility.Collapsed;

            // Başarı durumu
            IconCanvas.RenderTransform = new RotateTransform(0, 12, 12);
            IconPath.Data = Geometry.Parse("M6,12 L10,16 L18,8");
            IconPath.Fill = Brushes.Transparent;
            IconPath.Stroke = Brushes.White;
            IconBorder.Background = new LinearGradientBrush(
                Color.FromRgb(16, 185, 129),
                Color.FromRgb(5, 150, 105), 45);

            ProgressGradStart.Color = Color.FromRgb(16, 185, 129);
            ProgressGradEnd.Color = Color.FromRgb(5, 150, 105);

            StatusText.Text = "Güncelleme uygulanıyor...";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));

            AddLog("İndirme tamamlandı. Uygulama yeniden başlatılacak...");

            await Task.Delay(1500);

            UpdateService.ApplyUpdate(zipPath);
        }
        catch (OperationCanceledException)
        {
            ShowError("İndirme iptal edildi.");
        }
        catch (Exception ex)
        {
            ShowError($"İndirme hatası: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        StopSpinAnimation();
        IconCanvas.RenderTransform = new RotateTransform(0, 12, 12);
        IconPath.Data = Geometry.Parse("M7,7 L17,17 M17,7 L7,17");
        IconPath.Fill = Brushes.Transparent;
        IconPath.Stroke = Brushes.White;

        IconBorder.Background = new LinearGradientBrush(
            Color.FromRgb(239, 68, 68),
            Color.FromRgb(220, 38, 38), 45);

        ProgressGradStart.Color = Color.FromRgb(239, 68, 68);
        ProgressGradEnd.Color = Color.FromRgb(220, 38, 38);

        StatusText.Text = "Hata oluştu";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        ProgressLabel.Text = "İşlem başarısız";

        BtnCancel.Visibility = Visibility.Collapsed;
        BtnClose.Visibility = Visibility.Visible;
        AddLog($"HATA: {message}");
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // === Animasyon ve yardımcı metotlar ===

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
        Storyboard.SetTargetProperty(animation,
            new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        _spinStoryboard.Begin();
    }

    private void StopSpinAnimation()
    {
        _spinStoryboard?.Stop();
    }

    private void SetProgress(int percent)
    {
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

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _logLines.Add($"[{timestamp}] {message}");
        LogText.Text = string.Join("\n", _logLines);
        LogScroller.ScrollToEnd();
    }
}
