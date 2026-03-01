using System.Windows;
using System.Windows.Input;
using DersDagitim.Services;

namespace DersDagitim.Views;

public partial class DatabaseSelectorWindow : Window
{
    public class DatabaseItem
    {
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
        public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Name);
        public string StatusText => IsActive ? "Aktif" : "";
        public string Icon => "�";
    }

    public string? SelectedDatabase { get; private set; }
    
    public DatabaseSelectorWindow()
    {
        InitializeComponent();
        LoadDatabases();
    }
    
    private void LoadDatabases()
    {
        DatabaseList.DisplayMemberPath = null;
        DatabaseList.Items.Clear();
        
        var databases = ConfigManager.Shared.ListDatabases();
        var activeDb = System.IO.Path.GetFileName(ConfigManager.Shared.GetActiveDatabase());
        
        foreach (var db in databases)
        {
            DatabaseList.Items.Add(new DatabaseItem 
            { 
                Name = db, 
                IsActive = db.Equals(activeDb, StringComparison.OrdinalIgnoreCase) 
            });
        }
    }
    
    private string? GetSelectedDatabaseName()
    {
        if (DatabaseList.SelectedItem is DatabaseItem item)
        {
            return item.Name;
        }
        return null;
    }
    
    private void DatabaseList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectAndClose();
    }
    
    private void Select_Click(object sender, RoutedEventArgs e)
    {
        SelectAndClose();
    }
    
    private void SelectAndClose()
    {
        var selected = GetSelectedDatabaseName();
        if (string.IsNullOrEmpty(selected))
        {
            MessageBox.Show("Lütfen bir veritabanı seçin.", "Uyarı", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        SelectedDatabase = selected;
        DialogResult = true;
    }
    
    private void CreateNew_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Yeni Veritabanı", "Veritabanı adı (boşluk kullanmayın):", "yeni_calisma");
        if (dialog.ShowDialog() != true) return;
        
        var name = dialog.InputText.Trim().Replace(" ", "_");
        if (string.IsNullOrWhiteSpace(name)) return;
        
        if (ConfigManager.Shared.CreateDatabase(name))
        {
            LoadDatabases();
        }
        else
        {
            MessageBox.Show("Veritabanı oluşturulamadı. Bu isimde bir dosya zaten olabilir.", 
                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedDatabaseName();
        if (string.IsNullOrEmpty(selected))
        {
            MessageBox.Show("Lütfen bir veritabanı seçin.", "Uyarı", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        var baseName = System.IO.Path.GetFileNameWithoutExtension(selected);
        var dialog = new InputDialog("Çoğalt", $"'{baseName}' için yeni isim:", baseName + "_kopya");
        if (dialog.ShowDialog() != true) return;
        
        var newName = dialog.InputText.Trim().Replace(" ", "_");
        if (string.IsNullOrWhiteSpace(newName)) return;
        
        if (ConfigManager.Shared.DuplicateDatabase(selected, newName))
        {
            LoadDatabases();
        }
        else
        {
            MessageBox.Show("Çoğaltma başarısız. Bu isimde bir dosya zaten olabilir.", 
                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedDatabaseName();
        if (string.IsNullOrEmpty(selected))
        {
            MessageBox.Show("Lütfen bir veritabanı seçin.", "Uyarı", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        var baseName = System.IO.Path.GetFileNameWithoutExtension(selected);
        var dialog = new InputDialog("Adını Değiştir", "Yeni isim:", baseName);
        if (dialog.ShowDialog() != true) return;
        
        var newName = dialog.InputText.Trim().Replace(" ", "_");
        if (string.IsNullOrWhiteSpace(newName)) return;
        
        try
        {
            if (ConfigManager.Shared.RenameDatabase(selected, newName))
            {
                LoadDatabases();
            }
            else
            {
                MessageBox.Show("Yeniden adlandırma başarısız.\nAktif veritabanı yeniden adlandırılamaz.", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedDatabaseName();
        if (string.IsNullOrEmpty(selected))
        {
            MessageBox.Show("Lütfen bir veritabanı seçin.", "Uyarı", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        var result = MessageBox.Show(
            $"'{selected}' veritabanını silmek istediğinize emin misiniz?\n\nBu işlem geri alınamaz!", 
            "Silme Onayı", 
            MessageBoxButton.YesNo, 
            MessageBoxImage.Warning);
        
        if (result != MessageBoxResult.Yes) return;
        
        try
        {
            if (ConfigManager.Shared.DeleteDatabase(selected))
            {
                LoadDatabases();
            }
            else
            {
                MessageBox.Show("Silme başarısız.\nAktif veritabanı silinemez.", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadDatabases();
    }
    
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
