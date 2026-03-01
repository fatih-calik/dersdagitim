using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DersDagitim.Views
{
    public partial class StatBadge : UserControl
    {
        public StatBadge()
        {
            InitializeComponent();
        }

        public string Label
        {
            get { return (string)GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value); }
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(StatBadge), new PropertyMetadata("", (d, e) => ((StatBadge)d).TxtLabel.Text = (string)e.NewValue));

        public string Value
        {
            get { return (string)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(string), typeof(StatBadge), new PropertyMetadata("0", (d, e) => ((StatBadge)d).TxtValue.Text = (string)e.NewValue));

        public Color Color
        {
            get { return (Color)GetValue(ColorProperty); }
            set { SetValue(ColorProperty, value); }
        }

        public static readonly DependencyProperty ColorProperty =
            DependencyProperty.Register("Color", typeof(Color), typeof(StatBadge), new PropertyMetadata(Colors.Black, (d, e) => {
                ((StatBadge)d).TxtValue.Foreground = new SolidColorBrush((Color)e.NewValue);
                ((StatBadge)d).Background = new SolidColorBrush(Color.FromArgb(20, ((Color)e.NewValue).R, ((Color)e.NewValue).G, ((Color)e.NewValue).B));
            }));
    }
}
