using System;
using System.Windows;
using System.Windows.Controls;
using DersDagitim.Models;
using DersDagitim.Persistence;

namespace DersDagitim.Views
{
    public partial class AddTeacherWindow : Window
    {
        public Teacher? CreatedTeacher { get; private set; }

        public AddTeacherWindow()
        {
            InitializeComponent();
            LoadDefaults();
            Loaded += (s, e) => TxtName.Focus();
        }

        private void LoadDefaults()
        {
            // TxtMaxDailyHours.Text = "12"; // Removed
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TxtName.Text))
                {
                    MessageBox.Show("Lütfen öğretmen adını giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int maxHours = 30;
                int maxDailyHours = 8;
                
                // Old logic removed as UI elements are gone
                // if (!int.TryParse(TxtMaxHours.Text, out int maxHours)) maxHours = 30;
                // if (!int.TryParse(TxtMaxDailyHours.Text, out int maxDailyHours)) maxDailyHours = 12;

                var teacher = new Teacher
                {
                    Name = TxtName.Text.Trim(),
                    Position = (ComboPosition.SelectedValue as string) ?? "ogretmen",
                    Branch = TxtBranch.Text.Trim(),
                    MaxHours = maxHours,
                    MaxHoursPerDay = maxDailyHours,
                    TcNo = TxtTcNo.Text.Trim(),
                    DutyDay = "",
                    DutyLocation = "",
                    Club = ""
                };

                var repo = new TeacherRepository();
                repo.Save(teacher);

                CreatedTeacher = teacher;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kaydetme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NumberValidationTextBox(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}
