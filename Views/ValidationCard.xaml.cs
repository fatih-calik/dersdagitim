using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DersDagitim.Views
{
    public partial class ValidationCard : UserControl
    {
        public ValidationCard()
        {
            InitializeComponent();
        }

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(ValidationCard), new PropertyMetadata("", (d,e) => ((ValidationCard)d).TxtTitle.Text = (string)e.NewValue));

        public string Details
        {
            get { return (string)GetValue(DetailsProperty); }
            set { SetValue(DetailsProperty, value); }
        }
        public static readonly DependencyProperty DetailsProperty =
            DependencyProperty.Register("Details", typeof(string), typeof(ValidationCard), new PropertyMetadata("", (d, e) => ((ValidationCard)d).TxtDetails.Text = (string)e.NewValue));

        public string Icon
        {
            get { return (string)GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }
        // Simplified icon mapping for now
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(string), typeof(ValidationCard), new PropertyMetadata("User", (d, e) => {
                var card = (ValidationCard)d;
                string val = (string)e.NewValue;
                if (val == "User") card.TxtIcon.Text = "\xE77B";
                else if (val == "Calendar") card.TxtIcon.Text = "\xE787";
                else if (val == "Scale") card.TxtIcon.Text = "\xE945"; // fallback
                else if (val == "Check") card.TxtIcon.Text = "\xE73E";
            }));

        public double Percentage
        {
            get { return (double)GetValue(PercentageProperty); }
            set { SetValue(PercentageProperty, value); }
        }
        public static readonly DependencyProperty PercentageProperty =
            DependencyProperty.Register("Percentage", typeof(double), typeof(ValidationCard), new PropertyMetadata(0.0, (d, e) => {
                var card = (ValidationCard)d;
                double val = (double)e.NewValue;
                card.TxtPercentage.Text = $"{(int)val}%";
                card.PbStatus.Value = val;
                
                // Color logic
                Color c = Colors.Red; 
                if (val >= 80) c = Color.FromRgb(52, 199, 89); // Success Green
                else if (val >= 50) c = Color.FromRgb(255, 149, 0); // Warning Orange
                else c = Color.FromRgb(255, 59, 48); // Error Red

                card.TxtPercentage.Foreground = new SolidColorBrush(c);
                card.PbStatus.Foreground = new SolidColorBrush(c);
                card.TxtIcon.Foreground = new SolidColorBrush(c);
                card.IconBorder.Background = new SolidColorBrush(Color.FromArgb(30, c.R, c.G, c.B));
            }));
            
        public List<string> Issues
        {
            get { return (List<string>)GetValue(IssuesProperty); }
            set { SetValue(IssuesProperty, value); }
        }
        public static readonly DependencyProperty IssuesProperty =
            DependencyProperty.Register("Issues", typeof(List<string>), typeof(ValidationCard), new PropertyMetadata(null, (d, e) => {
                var card = (ValidationCard)d;
                var list = (List<string>)e.NewValue;
                card.IssuesList.ItemsSource = list;
                // If issues exist, maybe auto-expand or just show logic handled by user click
            }));

        private bool _isExpanded;
        private void Header_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isExpanded = !_isExpanded;
            IssuesPanel.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
            TxtChevron.Text = _isExpanded ? "\xE70E" : "\xE70D";
        }
    }
}
