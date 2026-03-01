using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace DersDagitim.Views
{
    public partial class ReportViewerWindow : Window
    {
        private string _initialHtml;
        private string _reportTitle = "Rapor";
        private string _pdfFilePath; // For PDF mode

        public ReportViewerWindow(string htmlContent)
        {
            InitializeComponent();
            _initialHtml = htmlContent;
            
            // Try to extract title from HTML for filename
            try 
            {
                int titleStart = htmlContent.IndexOf("<title>") + 7;
                int titleEnd = htmlContent.IndexOf("</title>");
                if (titleStart > 6 && titleEnd > titleStart)
                {
                    _reportTitle = htmlContent.Substring(titleStart, titleEnd - titleStart).Trim();
                }
            }
            catch {}

            InitializeAsync();
        }

        // New Constructor for PDF Bytes
        public ReportViewerWindow(byte[] pdfData, string title)
        {
            InitializeComponent();
            _reportTitle = title;
            
            // Save to temp file
            try
            {
                _pdfFilePath = Path.Combine(Path.GetTempPath(), $"DersDagitim_{Guid.NewGuid()}.pdf");
                File.WriteAllBytes(_pdfFilePath, pdfData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Geçici dosya oluşturulamadı: {ex.Message}", "Hata");
                return;
            }
            
            // UI Adjustments
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                await ReportBrowser.EnsureCoreWebView2Async(null);
                
                if (!string.IsNullOrEmpty(_pdfFilePath))
                {
                    // Navigate to local PDF file
                    ReportBrowser.CoreWebView2.Navigate(_pdfFilePath);
                }
                else
                {
                    LoadHtml(_initialHtml);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 başlatılamadı: {ex.Message}\nLütfen 'WebView2 Runtime' yüklü olduğundan emin olun.", "Hata");
            }
        }

        private void LoadHtml(string content)
        {
            if (ReportBrowser.CoreWebView2 != null)
                ReportBrowser.NavigateToString(content);
        }

        private string CleanFileName(string name)
        {
            foreach(char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Dispose WebView2 to release browser process
            try { ReportBrowser?.Dispose(); } catch {}

            base.OnClosed(e);
            // Cleanup temp file
            if (!string.IsNullOrEmpty(_pdfFilePath) && File.Exists(_pdfFilePath))
            {
                try { File.Delete(_pdfFilePath); } catch {}
            }
        }
    }
}
