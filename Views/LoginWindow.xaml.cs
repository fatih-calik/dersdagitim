using System.Windows;
using System.Windows.Input;
using System.Diagnostics;

namespace DersDagitim.Views
{
    public partial class LoginWindow : Window
    {
        private string _correctPassword;
        public bool IsAuthenticated { get; private set; } = false;

        public LoginWindow(string correctPassword)
        {
            InitializeComponent();
            _correctPassword = correctPassword;
            PasswordInput.Focus();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            if (PasswordInput.Password == _correctPassword)
            {
                IsAuthenticated = true;
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                ErrorText.Visibility = Visibility.Visible;
                PasswordInput.SelectAll();
                PasswordInput.Focus();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Login_Click(sender, e);
            }
        }
    }
}
