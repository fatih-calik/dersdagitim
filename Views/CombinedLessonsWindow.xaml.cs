using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DersDagitim.Models;
using DersDagitim.Persistence;

namespace DersDagitim.Views;

public partial class CombinedLessonsWindow : Window
{
    private readonly CombinedLessonRepository _repo = new();
    private readonly ClassRepository _classRepo = new();
    
    private KardesGrup _selectedGroup;
    private SchoolClass _selectedClass;

    public CombinedLessonsWindow()
    {
        InitializeComponent();
        LoadGroups();
        LoadClasses();
    }

    // --- 1. LİSTE: GRUPLAR ---
    private void LoadGroups()
    {
        GroupsList.ItemsSource = _repo.GetAllGroups();
    }

    private void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedGroup = GroupsList.SelectedItem as KardesGrup;
        if (_selectedGroup != null)
        {
            TxtSelectedGroup.Text = _selectedGroup.Ad;
            LoadGroupContent();
        }
        else
        {
            TxtSelectedGroup.Text = "(Grup Seçin)";
            GroupLessonsList.ItemsSource = null;
        }
    }
    
    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var existingCount = GroupsList.Items.Count;
        var name = $"GRUP_{existingCount + 1}";
        
        _repo.CreateGroup(name);
        LoadGroups();
        
        // Select last added group (usually the first in list if DESC, or last)
        // Repo returns DESC order as per my impl? Let's check repo. Repo had ORDER BY id DESC. So select index 0.
        if (GroupsList.Items.Count > 0)
            GroupsList.SelectedIndex = 0;
            
        // Refocus group
        _selectedGroup = GroupsList.SelectedItem as KardesGrup;
    }
    
    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsList.SelectedItem is KardesGrup g)
        {
            if (MessageBox.Show($"'{g.Ad}' grubunu ve içindeki TÜM birleştirmeleri silmek istediğinize emin misiniz?\nDağıtım tablosundaki ilişkiler de sıfırlanacaktır.", 
                "Grubu Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _repo.DeleteGroup(g.Id);
                LoadGroups();
            }
        }
    }
    
    private void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        // Name can be edited directly in the list now. 
        // This menu item could be used to focus the textbox if we had a way to find it, 
        // but since it's already edible, we'll just show a small reminder.
        MessageBox.Show("Grup adını listedeki kart üzerinden doğrudan değiştirebilirsiniz.", "Bilgi");
    }

    private void GroupName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is int groupId)
        {
            var newName = tb.Text.Trim();
            if (!string.IsNullOrEmpty(newName))
            {
                _repo.UpdateGroupName(groupId, newName);
                
                // If it's the currently selected group, update the header text too
                if (_selectedGroup != null && _selectedGroup.Id == groupId)
                {
                    _selectedGroup.Ad = newName;
                    TxtSelectedGroup.Text = newName;
                }
            }
        }
    }

    // --- 2. LİSTE: SINIFLAR ---
    private void LoadClasses()
    {
        ClassesList.ItemsSource = _classRepo.GetAll();
    }
    
    private void ClassesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedClass = ClassesList.SelectedItem as SchoolClass;
        if (_selectedClass != null)
        {
            LoadClassLessons();
        }
        else
        {
            ClassLessonsList.ItemsSource = null;
        }
    }

    // --- 3. LİSTE: DERS HAVUZU (SOURCE) ---
    private void LoadClassLessons()
    {
        if (_selectedClass == null) return;
        
        var db = DatabaseManager.Shared;
        var sql = $@"
            SELECT sd.id, d.kod, d.ad, sd.toplam_saat, 
                   (SELECT GROUP_CONCAT(g.ad, ', ') 
                    FROM kardes_bloklar kb 
                    JOIN kardes_gruplar g ON kb.kardes_id = g.id 
                    WHERE kb.sinif_ders_id = sd.id) as group_names,
                   (SELECT COUNT(*) FROM kardes_bloklar kb WHERE kb.sinif_ders_id = sd.id) as group_count
            FROM sinif_ders sd
            JOIN ders d ON sd.ders_id = d.id
            WHERE sd.sinif_id = {_selectedClass.Id}
            ORDER BY d.ad";
            
        var list = new System.Collections.Generic.List<ClassLessonViewModel>();
        foreach(var r in db.Query(sql))
        {
            var gNames = DatabaseManager.GetString(r, "group_names");
            int gCount = DatabaseManager.GetInt(r, "group_count");
            list.Add(new ClassLessonViewModel
            {
                Id = DatabaseManager.GetInt(r, "id"),
                ShortName = DatabaseManager.GetString(r, "kod"),
                FullName = DatabaseManager.GetString(r, "ad"),
                TotalHours = DatabaseManager.GetInt(r, "toplam_saat"),
                GroupNames = gNames,
                IsInsideGroup = gCount > 0,
                IsMultiGroup = gCount > 1
            });
        }
        
        ClassLessonsList.ItemsSource = list;
    }
    
    private void ClassLessonsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedGroup == null) return;
    
        var item = ItemsControl.ContainerFromElement(ClassLessonsList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item == null) return;
        
        var data = item.Content as ClassLessonViewModel;
        if (data == null || data.IsInsideGroup) return; // Prevent drag if already in a group
        
        DragDrop.DoDragDrop(item, data, DragDropEffects.Copy);
    }


    // --- 4. LİSTE: GRUP İÇERİĞİ (TARGET) ---
    private void LoadGroupContent()
    {
        if (_selectedGroup == null) return;
        GroupLessonsList.ItemsSource = _repo.GetGroupContent(_selectedGroup.Id);
    }
    
    private void GroupLessonsList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(ClassLessonViewModel)))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void GroupLessonsList_Drop(object sender, DragEventArgs e)
    {
        if (_selectedGroup == null) return;
        
        if (e.Data.GetDataPresent(typeof(ClassLessonViewModel)))
        {
            var data = e.Data.GetData(typeof(ClassLessonViewModel)) as ClassLessonViewModel;
            if (data != null)
            {
                _repo.AddLessonToGroup(_selectedGroup.Id, data.Id);
                LoadGroupContent();
                LoadClassLessons(); // Refresh source list
            }
        }
    }
    
    private void RemoveFromGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupLessonsList.SelectedItem is CombinedLessonView cl)
        {
            _repo.RemoveLessonFromGroup(cl.Id);
            LoadGroupContent();
            LoadClassLessons(); // Refresh source list
        }
    }
    private void AddLessonToGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGroup == null)
        {
            MessageBox.Show("Lütfen önce bir grup seçiniz.");
            return;
        }

        if (sender is MenuItem menuItem && menuItem.DataContext is ClassLessonViewModel lesson)
        {
            if (lesson.IsInsideGroup)
            {
                MessageBox.Show("Bu ders zaten bir gruba ekli.");
                return;
            }

            _repo.AddLessonToGroup(_selectedGroup.Id, lesson.Id);
            LoadGroupContent();
            LoadClassLessons(); // Refresh source list
        }
    }
}

public class ClassLessonViewModel
{
    public int Id { get; set; }
    public string ShortName { get; set; } = "";
    public string FullName { get; set; } = "";
    public int TotalHours { get; set; }
    public bool IsInsideGroup { get; set; }
    public bool IsMultiGroup { get; set; }
    public string GroupNames { get; set; } = "";

    public string DisplayName => $"{ShortName} - {(FullName != null && FullName.Length > 16 ? FullName.Substring(0, 16) + "..." : FullName ?? "")}";
    public string Initial => !string.IsNullOrEmpty(FullName) ? FullName.Substring(0, 1) : "?";
}
