using DersDagitim.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DersDagitim.Views
{
    public partial class DataValidationView : UserControl
    {
        public DataValidationView()
        {
            InitializeComponent();
        }

        private async void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            BtnValidate.IsEnabled = false;
            if (BtnAnalyze != null) BtnAnalyze.IsEnabled = false;
            if (BtnAiNodeNetwork != null) BtnAiNodeNetwork.IsEnabled = false;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;

            ValidationResults? results = null;

            await Task.Run(() =>
            {
                var service = new DataValidatorService();
                results = service.Validate();
            });

            LoadingPanel.Visibility = Visibility.Collapsed;
            BtnValidate.IsEnabled = true;
            if (BtnAnalyze != null) BtnAnalyze.IsEnabled = true;
            if (BtnAiNodeNetwork != null) BtnAiNodeNetwork.IsEnabled = true;
            BtnValidate.Content = "Yeniden Kontrol Et";

            if (results != null)
            {
                ResultsPanel.Visibility = Visibility.Visible;
                UpdateUI(results);
            }
            else
            {
                MessageBox.Show("Veri kontrolü sırasında bir hata oluştu.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            var win = new DetailedAssignmentWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
        }

        private void BtnAiNodeNetwork_Click(object sender, RoutedEventArgs e)
        {
            var win = new AiNodeNetworkWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
        }

        private void UpdateUI(ValidationResults results)
        {
            // Overall Status
            double score = (results.AssignmentCompleteness + results.TeacherAvailability + results.ResourceBalance + results.ScheduleFeasibility) / 4.0;
            
            TxtOverallStatus.Text = score >= 80 ? "Mükemmel" : (score >= 50 ? "Oldukça İyi" : (score >= 30 ? "Dikkat Gerekli" : "Sorunlu"));
            TxtOverallStatus.Foreground = GetColorBrush(score);
            
            TxtOverallScore.Text = $"{(int)score}%";
            
            // Draw Arc (Simplistic representation)
            // Ideally we do real geometry, but here we just update text. 
            // PathScoreArc could be used if we calculate Start/End points, but that's complex for this snippet.
            // Let's just color the stroke.
            PathScoreArc.Stroke = GetColorBrush(score);

            // Badges
            // DataContext for badges? Or manually set if they are not bound. 
            // I used {Binding} in XAML so I should set DataContext of the Border or UserControl.
            // Let's set DataContext of ResultsPanel or the specific border.
            // But results is not INPC (NotifyPropertyChanged). Let's just create a wrapper or simple object.
            // Actually, CardBinding is easier if I just set properties on the UserControl if I named them.
            // Wait, I used Binding in XAML for badges: Value="{Binding TotalLessons}".
            // So I need to set DataContext of the container.
            
            // Let's set DataContext of the View or Panel.
            ResultsPanel.DataContext = results;
            CriticalAlertBox.Visibility = results.CriticalConflicts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;


            // Cards
            CardAssignment.Percentage = results.AssignmentCompleteness;
            CardAssignment.Details = results.AssignmentDetails;
            CardAssignment.Issues = results.AssignmentIssues;

            CardTeacher.Percentage = results.TeacherAvailability;
            CardTeacher.Details = results.TeacherDetails;
            CardTeacher.Issues = results.TeacherIssues;

            CardResource.Percentage = results.ResourceBalance;
            CardResource.Details = results.ResourceDetails;
            CardResource.Issues = results.ResourceIssues;

            CardSchedule.Percentage = results.ScheduleFeasibility;
            CardSchedule.Details = results.ScheduleDetails;
            CardSchedule.Issues = results.ScheduleIssues;

            // Heatmap
            DrawHeatmap(results.ResourceBalanceGrid);
        }

        private void DrawHeatmap(Dictionary<int, Dictionary<int, string>> gridData)
        {
            HeatmapGrid.Items.Clear();

            // Structure:
            // Header Row: [Scan] [Pzt] [Sal] ...
            // Rows: [1] [cell] [cell] ...

            var mainGrid = new Grid();
            // Columns: 1 for header + 5 days
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); // Hour header
            for (int i = 0; i < 5; i++) mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Rows: 1 for header + 15 max hours (or dynamic)
            // Let's find max hour
            int maxHour = 0;
            foreach (var d in gridData.Values)
                if (d.Keys.Count > 0) maxHour = Math.Max(maxHour, d.Keys.Max());
            if (maxHour == 0) maxHour = 8; // default
            
            // Header Row
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            string[] dayNames = { "Pzt", "Sal", "Çar", "Per", "Cum" };
            for(int i=0; i<5; i++)
            {
                var txt = new TextBlock { Text = dayNames[i], HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2), Foreground = Brushes.Gray, FontSize = 12 };
                Grid.SetRow(txt, 0);
                Grid.SetColumn(txt, i + 1);
                mainGrid.Children.Add(txt);
            }

            for (int h = 1; h <= maxHour; h++)
            {
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                
                // Hour Label
                var txtHour = new TextBlock { Text = h.ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 12 };
                Grid.SetRow(txtHour, h);
                Grid.SetColumn(txtHour, 0);
                mainGrid.Children.Add(txtHour);

                for (int d = 1; d <= 5; d++)
                {
                    string val = "";
                    if (gridData.ContainsKey(d) && gridData[d].ContainsKey(h))
                        val = gridData[d][h];
                    
                    var cell = CreateCell(val);
                    Grid.SetRow(cell, h);
                    Grid.SetColumn(cell, d);
                    mainGrid.Children.Add(cell);
                }
            }
            
            HeatmapGrid.Items.Add(mainGrid);
        }

        private FrameworkElement CreateCell(string value)
        {
            if (string.IsNullOrEmpty(value)) return new Border();

            // Value format: "available/active" e.g. "12/10"
            var parts = value.Split('/');
            int avail = 0, active = 0;
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out avail);
                int.TryParse(parts[1], out active);
            }

            Color bg = Colors.Gray;
            Color fg = Colors.Black;

            if (avail < active) { bg = Color.FromRgb(255, 59, 48); fg = Colors.White; } // Red
            else if (avail == active) { bg = Color.FromRgb(255, 149, 0); fg = Colors.White; } // Orange
            else { bg = Color.FromRgb(52, 199, 89); fg = Colors.White; } // Green

            if (avail == 0 && active == 0) { bg = Colors.Transparent; fg = Colors.Gray; }

            var border = new Border
            {
                Background = new SolidColorBrush(bg),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(1)
            };
            
            border.Child = new TextBlock
            {
                Text = value,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(fg),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            
            return border;
        }

        private SolidColorBrush GetColorBrush(double score)
        {
            if (score >= 80) return new SolidColorBrush(Color.FromRgb(52, 199, 89));
            if (score >= 50) return new SolidColorBrush(Color.FromRgb(255, 149, 0));
            return new SolidColorBrush(Color.FromRgb(255, 59, 48));
        }
    }
}
