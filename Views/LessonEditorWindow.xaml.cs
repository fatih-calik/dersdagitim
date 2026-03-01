using System.Windows;
using DersDagitim.Models;
using DersDagitim.Persistence;

namespace DersDagitim.Views;

public partial class LessonEditorWindow : Window
{
    public Lesson Lesson { get; private set; }
    
    public LessonEditorWindow(Lesson? lesson = null)
    {
        InitializeComponent();

        if (lesson != null)
        {
            Lesson = lesson;
            Title = "Ders Düzenle";

            InputsName.Text = lesson.Name;
            InputsCode.Text = lesson.Code;
            InputsBlock.Text = lesson.DefaultBlock;
            InputsBlock.IsEnabled = false; // Düzenlemede blok değiştirilemez
            InputsPriority.Value = lesson.MorningPriority;
        }
        else
        {
            Lesson = new Lesson();
            Title = "Yeni Ders Ekle";
            InputsBlock.IsEnabled = true; // Yeni derste blok aktif
        }
        
        InputsName.Focus();
    }
    

    
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = InputsName.Text.Trim();
        var code = InputsCode.Text.Trim();
        var block = InputsBlock.Text.Trim();
        
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Ders adı boş olamaz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (string.IsNullOrEmpty(code))
        {
            // Auto-generate code if empty (first 3 letters)
            code = name.Substring(0, Math.Min(3, name.Length)).ToUpper();
        }
        
        Lesson.Name = name;
        Lesson.Code = code;
        Lesson.DefaultBlock = block;
        Lesson.MorningPriority = (int)InputsPriority.Value;
        

        
        try
        {
            new LessonRepository().Save(Lesson);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kaydetme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
