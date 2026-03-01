using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace LicenseGenerator
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            string schoolName = txtSchoolName.Text.Trim();
            string requestCode = txtRequestCode.Text.Trim();
            string yearStr = txtYear.Text.Trim();

            if (string.IsNullOrEmpty(schoolName) || string.IsNullOrEmpty(requestCode) || string.IsNullOrEmpty(yearStr))
            {
                ShowMessage("Lütfen tüm alanları doldurun.", Brushes.Red);
                return;
            }

            // 1. Decrypt Request Code
            string? mac = GeneratorLogic.Shared.DecryptRequestCode(requestCode);
            if (string.IsNullOrEmpty(mac))
            {
                ShowMessage("Hata: İstek kodu geçersiz!", Brushes.Red);
                pnlOutput.Visibility = Visibility.Collapsed;
                return;
            }

            // 2. Year check
            if (!int.TryParse(yearStr, out int yearInt))
            {
                ShowMessage("Hata: Yıl sayısal olmalıdır.", Brushes.Red);
                return;
            }

            // 3. Generate License
            string? licenseContent = GeneratorLogic.Shared.GenerateLicenseContent(mac, schoolName, yearInt);

            if (!string.IsNullOrEmpty(licenseContent))
            {
                txtGeneratedCode.Text = licenseContent;
                pnlOutput.Visibility = Visibility.Visible;
                ShowMessage($"Lisans başarıyla üretildi. MAC: {mac}", Brushes.Green);
            }
            else
            {
                ShowMessage("Hata: Lisans üretilemedi.", Brushes.Red);
                pnlOutput.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnGenerateDemo_Click(object sender, RoutedEventArgs e)
        {
             // Generate Demo
             string? licenseContent = GeneratorLogic.Shared.GenerateDemoContent();

             if (!string.IsNullOrEmpty(licenseContent))
             {
                 txtGeneratedCode.Text = licenseContent;
                 pnlOutput.Visibility = Visibility.Visible;
                 ShowMessage($"Demo lisansı başarıyla üretildi. (DEMO:BUGUN:0)", Brushes.BlueViolet);
                 
                 // Auto copy?
                 // Clipboard.SetText(licenseContent);
             }
             else
             {
                 ShowMessage("Hata: Demo lisansı üretilemedi.", Brushes.Red);
                 pnlOutput.Visibility = Visibility.Collapsed;
             }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtGeneratedCode.Text);
            ShowMessage("Kod panoya kopyalandı!", Brushes.Green);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                FileName = "ls.clk",
                Filter = "License Key file (*.clk)|*.clk|All files (*.*)|*.*",
                Title = "Lisans Dosyasını Kaydet"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(saveFileDialog.FileName, txtGeneratedCode.Text);
                    ShowMessage($"Dosya kaydedildi: {Path.GetFileName(saveFileDialog.FileName)}", Brushes.Green);
                }
                catch (Exception ex)
                {
                    ShowMessage($"Kaydetme hatası: {ex.Message}", Brushes.Red);
                }
            }
        }

        private void ShowMessage(string msg, Brush color)
        {
            txtMessage.Text = msg;
            txtMessage.Foreground = color;
        }
    }
}
