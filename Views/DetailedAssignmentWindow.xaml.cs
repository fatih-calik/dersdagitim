using DersDagitim.Services;
using System.Windows;

namespace DersDagitim.Views
{
    public partial class DetailedAssignmentWindow : Window
    {
        private readonly DetailedDiagnosisService _service = new();

        public DetailedAssignmentWindow()
        {
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                var classes = _service.GetFullHierarchy();
                ClassItemsControl.ItemsSource = classes;

                var rooms = _service.GetRoomHierarchy();
                RoomItemsControl.ItemsSource = rooms;

                var combined = _service.GetCombinedHierarchy();
                CombinedItemsControl.ItemsSource = combined;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Veri yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
