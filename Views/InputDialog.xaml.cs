using System.Windows;

namespace DersDagitim.Views
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; } = string.Empty;

        public InputDialog(string title, string question, string defaultAnswer = "")
        {
            InitializeComponent();
            Title = title;
            lblQuestion.Text = question;
            txtAnswer.Text = defaultAnswer;
            txtAnswer.Focus();
            txtAnswer.SelectAll();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            InputText = txtAnswer.Text;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
