using System;
using System.Windows;
using System.Windows.Controls;
using DersDagitim.Models;
using DersDagitim.Persistence;

namespace DersDagitim.Views
{
    public partial class TeacherEditorWindow : Window
    {
        private Teacher _teacher;

        public TeacherEditorWindow(Teacher teacher)
        {
            InitializeComponent();
            _teacher = teacher;
            LoadTeacherData();
        }

        private void LoadTeacherData()
        {
            TxtName.Text = _teacher.Name;
            TxtTcNo.Text = _teacher.TcNo;
            ComboPosition.SelectedValue = _teacher.Position;
            TxtBranch.Text = _teacher.Branch;
            TxtMaxHours.Text = _teacher.MaxHours.ToString();
            TxtMaxDailyHours.Text = _teacher.MaxHoursPerDay.ToString();
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

                if (!int.TryParse(TxtMaxHours.Text, out int maxHours)) maxHours = 20;
                if (!int.TryParse(TxtMaxDailyHours.Text, out int maxDailyHours)) maxDailyHours = 8;

                _teacher.Name = TxtName.Text.Trim();
                _teacher.TcNo = TxtTcNo.Text.Trim();
                _teacher.Position = (ComboPosition.SelectedValue as string) ?? "ogretmen";
                _teacher.Branch = TxtBranch.Text.Trim();
                _teacher.MaxHours = maxHours;
                _teacher.MaxHoursPerDay = maxDailyHours;

                new TeacherRepository().Save(_teacher);

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
