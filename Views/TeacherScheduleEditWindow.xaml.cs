using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DersDagitim.Models;
using DersDagitim.Persistence;
using DersDagitim.Services;

namespace DersDagitim.Views;

public partial class TeacherScheduleEditWindow : Window
{
    private DistributionBlock? _selectedBlock;
    private List<DistributionBlock> _originalBlocks = new();
    private List<DistributionBlock> _workingBlocks = new();
    private List<ScheduleEditService.BlockChange> _pendingChanges = new();
    private Dictionary<int, Teacher> _teachers = new();
    private Dictionary<int, SchoolClass> _classes = new();
    private Dictionary<int, OrtakMekan> _rooms = new();
    private Dictionary<string, SolidColorBrush> _colorCache = new();
    // UI kapanmadan çoklu kaydetme işleminde DialogResult=true iletebilmek için
    private bool _hasSavedChanges = false;
    private int _maxDays = 5;
    private int _maxHours = 8;
    private SchoolInfo _schoolInfo = new();

    // Değişen öğretmenlerin eski durumu (snapshot)
    private Dictionary<int, Dictionary<string, string>> _originalTeacherCells = new();

    public TeacherScheduleEditWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Tam ekran: taskbar'ı kapatmaması için MaxHeight ayarla
        MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
        MaxWidth = SystemParameters.MaximizedPrimaryScreenWidth;
        LoadData();
    }

    private void LoadData()
    {
        var db = DatabaseManager.Shared;

        // Okul bilgisi
        var schoolRows = db.Query("SELECT gun_sayisi, ders_sayisi FROM okul LIMIT 1");
        if (schoolRows.Count > 0)
        {
            _maxDays = DatabaseManager.GetInt(schoolRows[0], "gun_sayisi");
            _maxHours = DatabaseManager.GetInt(schoolRows[0], "ders_sayisi");
            if (_maxDays <= 0) _maxDays = 5;
            if (_maxHours <= 0) _maxHours = 8;
        }

        _schoolInfo = new SchoolRepository().GetSchoolInfo();
        
        // Okul ayarlarına göre sabit ders_sayisi girilmiş olsa da, 
        // bazı günlerin zaman tablosunda (DefaultTimetable) daha yüksek saatler 'ACIK' (Open) olabilir.
        // Kullanıcının belirttiği gibi "Pazartesi 9, diğer günler 8" durumunda grid 9 satıra sahip olmalıdır.
        if (_schoolInfo != null && _schoolInfo.DefaultTimetable != null)
        {
            int calculatedMaxHour = _maxHours;
            foreach (var kvp in _schoolInfo.DefaultTimetable)
            {
                // Eğer o saat kapalı DEĞİLSE (Açık ise) ve mevcut maximumdan büyükse
                if (kvp.Value != SlotState.Closed && kvp.Key.Hour > calculatedMaxHour)
                {
                    calculatedMaxHour = kvp.Key.Hour;
                }
            }
            _maxHours = calculatedMaxHour;
        }

        // Öğretmenler
        var teacherRepo = new TeacherRepository();
        var teacherList = teacherRepo.GetAll();
        _teachers = teacherList.ToDictionary(t => t.Id);

        // Sınıflar
        var classRepo = new ClassRepository();
        var classList = classRepo.GetAll();
        _classes = classList.ToDictionary(c => c.Id);

        // Ortak mekanlar
        var roomRows = db.Query("SELECT * FROM ortak_mekan");
        _rooms = new Dictionary<int, OrtakMekan>();
        foreach (var r in roomRows)
        {
            var room = new OrtakMekan
            {
                Id = DatabaseManager.GetInt(r, "id"),
                Name = DatabaseManager.GetString(r, "ad")
            };
            _rooms[room.Id] = room;
        }

        // Tüm blokları al ve snapshot oluştur
        var repo = new DistributionRepository();
        _originalBlocks = repo.GetAllBlocks().Where(b => b.IsPlaced).ToList();
        _workingBlocks = _originalBlocks.Select(b => b.Clone()).ToList();

        // Öğretmen combo doldur
        TeacherCombo.ItemsSource = teacherList.OrderBy(t => t.Name).ToList();

        StatusText.Text = "Öğretmen seçip bir bloğa sol tıklayın.";
    }

    // ===== ÖĞRETMEN SEÇİMİ =====

    private void TeacherCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedBlock = null;
        SelectedBlockInfo.Text = "";
        RenderScheduleGrid(ScheduleGridPanel, GetCurrentTeacherId(), _workingBlocks, true);
        StatusText.Text = "Taşımak istediğiniz bloğa sol tıklayın.";
    }

    private int GetCurrentTeacherId()
    {
        if (TeacherCombo.SelectedValue is int id) return id;
        if (TeacherCombo.SelectedItem is Teacher t) return t.Id;
        return 0;
    }

    // ===== GRID OLUŞTURMA =====

    private void RenderScheduleGrid(StackPanel panel, int teacherId, List<DistributionBlock> blocks,
        bool interactive, HashSet<int>? highlightBlockIds = null, bool isOldState = false)
    {
        panel.Children.Clear();
        _colorCache.Clear();

        if (teacherId == 0) return;

        var teacherBlocks = blocks.Where(b =>
            b.TeacherIds.Contains(teacherId) && b.IsPlaced).ToList();

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(interactive ? 40 : 32) });
        for (int i = 0; i < _maxDays; i++)
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Header
        string[] days = { "Saat", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz" };
        var headerBg = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        var headerFg = new SolidColorBrush(Color.FromRgb(30, 41, 59));

        for (int i = 0; i <= _maxDays; i++)
        {
            var headerCell = new Border
            {
                Background = headerBg,
                Padding = new Thickness(1),
                Child = new TextBlock
                {
                    Text = days[i],
                    FontWeight = FontWeights.SemiBold,
                    FontSize = interactive ? 9.5 : 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = headerFg
                }
            };
            Grid.SetColumn(headerCell, i);
            mainGrid.Children.Add(headerCell);
        }

        // Rows
        int rowHeight = interactive ? 32 : 24;
        for (int h = 1; h <= _maxHours; h++)
        {
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowHeight) });

            var hourCell = new Border
            {
                Background = headerBg,
                Child = new TextBlock
                {
                    Text = h.ToString(),
                    FontWeight = FontWeights.Bold,
                    FontSize = interactive ? 10 : 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = headerFg
                }
            };
            Grid.SetRow(hourCell, h);
            Grid.SetColumn(hourCell, 0);
            mainGrid.Children.Add(hourCell);

            for (int d = 1; d <= _maxDays; d++)
            {
                var block = teacherBlocks.FirstOrDefault(b =>
                    b.Day == d && b.Hour <= h && (b.Hour + b.BlockDuration) > h);

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(0.5),
                    Margin = new Thickness(0.5),
                    CornerRadius = new CornerRadius(3)
                };

                string displayText = "";
                bool isClosed = false;
                bool isSchoolClosed = false;

                var slot = new TimeSlot(d, h);

                // 1. Okul o saatte kapalı mı?
                if (_schoolInfo?.DefaultTimetable != null
                    && _schoolInfo.DefaultTimetable.TryGetValue(slot, out var sState) && sState == SlotState.Closed)
                {
                    isClosed = true;
                    isSchoolClosed = true;
                }

                // 2. Sınıf o saatte kapalı mı? (Sadece o an seçili bir blok varsa ve onun sınıfını taşıyorsak o sınıfın saatine bakarız)
                if (!isClosed && _selectedBlock != null && interactive)
                {
                    if (_classes.TryGetValue(_selectedBlock.ClassId, out var selectedClass))
                    {
                        if (selectedClass.Constraints.TryGetValue(slot, out var cState) && cState == SlotState.Closed)
                        {
                            var key = $"d_{d}_{h}";
                            if (selectedClass.Schedule.TryGetValue(key, out var val))
                            {
                                if (val == "KAPALI" || string.IsNullOrEmpty(val))
                                    isClosed = true;
                            }
                            else
                            {
                                isClosed = true;
                            }
                        }
                    }
                }

                // 3. KAPALI kontrolü — sadece gerçek KAPALI slotlar (Öğretmen için)
                if (!isClosed && _teachers.TryGetValue(teacherId, out var teacher))
                {
                    if (teacher.Constraints.TryGetValue(slot, out var state) && state == SlotState.Closed
                        && !teacher.ScheduleInfo.ContainsKey(slot))
                        isClosed = true;
                }

                if (isSchoolClosed)
                {
                    displayText = "";
                    border.Background = Brushes.Transparent;
                    border.BorderThickness = new Thickness(0);
                }
                else if (isClosed)
                {
                    displayText = "--";
                    border.Background = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                }
                else if (block != null)
                {
                    string className = _classes.TryGetValue(block.ClassId, out var c) ? c.Name : "";
                    displayText = $"{className}  {block.LessonCode}";
                    if (block.TeacherIds.Count > 1) displayText = $"{className} 👥 {block.LessonCode}";

                    // Renklendirme
                    bool isHighlighted = highlightBlockIds != null && highlightBlockIds.Contains(block.Id);
                    bool isSelected = _selectedBlock != null && block.Id == _selectedBlock.Id && interactive;

                    if (isSelected)
                    {
                        border.Background = new SolidColorBrush(Color.FromRgb(255, 237, 213)); // Turuncu
                        border.BorderBrush = new SolidColorBrush(Color.FromRgb(249, 115, 22));
                        border.BorderThickness = new Thickness(2.5);
                    }
                    else if (isHighlighted && isOldState)
                    {
                        border.Background = new SolidColorBrush(Color.FromRgb(254, 226, 226)); // Kırmızımsı
                    }
                    else if (isHighlighted)
                    {
                        border.Background = new SolidColorBrush(Color.FromRgb(254, 249, 195)); // Sarı
                    }
                    else if (block.IsLocked)
                    {
                        border.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231)); // Yeşilimsi
                        border.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                        border.BorderThickness = new Thickness(1.5);
                    }
                    else
                    {
                        var colorKey = className;
                        border.Background = GetScheduleColor(colorKey);
                    }
                }
                else
                {
                    border.Background = Brushes.White;
                }

                var txt = new TextBlock
                {
                    Text = displayText,
                    FontSize = interactive ? 8.5 : 7,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(1, 0, 1, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontWeight = string.IsNullOrEmpty(displayText) ? FontWeights.Normal : FontWeights.SemiBold
                };

                if (isClosed)
                    txt.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));

                border.Child = txt;

                if (interactive)
                {
                    border.Tag = new int[] { d, h };
                    border.Cursor = Cursors.Hand;
                    border.MouseLeftButtonUp += OnGridCellLeftClick;
                    border.MouseRightButtonUp += OnGridCellRightClick;
                }

                Grid.SetRow(border, h);
                Grid.SetColumn(border, d);
                mainGrid.Children.Add(border);
            }
        }

        mainGrid.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(mainGrid);
    }

    // ===== GRID ETKİLEŞİMLERİ =====

    private void OnGridCellLeftClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not int[] tag) return;
        int day = tag[0], hour = tag[1];
        int teacherId = GetCurrentTeacherId();

        var block = _workingBlocks.FirstOrDefault(b =>
            b.TeacherIds.Contains(teacherId) && b.Day == day &&
            b.Hour <= hour && (b.Hour + b.BlockDuration) > hour);

        if (block == null)
        {
            _selectedBlock = null;
            SelectedBlockInfo.Text = "";
            StatusText.Text = "Boş hücre. Taşımak için dolu bir bloğa tıklayın.";
        }
        else if (block.IsLocked)
        {
            StatusText.Text = "Bu blok kilitli, taşınamaz.";
            return;
        }
        else
        {
            _selectedBlock = block;
            string className = _classes.TryGetValue(block.ClassId, out var c) ? c.Name : "";
            SelectedBlockInfo.Text = $"Seçili: {block.LessonCode} {className}";
            StatusText.Text = "Hedef hücreye sağ tıklayıp 'Buraya Taşı' seçin.";
        }

        RenderScheduleGrid(ScheduleGridPanel, teacherId, _workingBlocks, true);
    }

    private void OnGridCellRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedBlock == null)
        {
            StatusText.Text = "Önce taşımak istediğiniz bloğa sol tıklayın.";
            return;
        }

        if (sender is not Border border || border.Tag is not int[] tag) return;
        int targetDay = tag[0], targetHour = tag[1];

        var cm = new ContextMenu();

        var moveItem = new MenuItem { Header = "Buraya Taşı", FontWeight = FontWeights.SemiBold };
        moveItem.Click += (s, ev) => ExecuteMove(targetDay, targetHour);
        cm.Items.Add(moveItem);

        cm.Items.Add(new Separator());

        var cancelItem = new MenuItem { Header = "İptal" };
        cancelItem.Click += (s, ev) =>
        {
            _selectedBlock = null;
            SelectedBlockInfo.Text = "";
            StatusText.Text = "Taşıma iptal edildi.";
            RenderScheduleGrid(ScheduleGridPanel, GetCurrentTeacherId(), _workingBlocks, true);
        };
        cm.Items.Add(cancelItem);

        border.ContextMenu = cm;
        cm.IsOpen = true;
    }

    // ===== TAŞIMA İŞLEMİ =====

    private async void ExecuteMove(int targetDay, int targetHour)
    {
        if (_selectedBlock == null) return;

        // Değişiklik öncesi snapshot al (etkilenen öğretmenler için)
        SaveAffectedTeacherSnapshots();

        // Önceki bekleyen değişiklikler varsa temiz başla
        if (_pendingChanges.Count > 0)
        {
            _workingBlocks = _originalBlocks.Select(b => b.Clone()).ToList();
            _pendingChanges.Clear();
        }

        // Loading overlay göster
        LoadingOverlay.Visibility = Visibility.Visible;
        IsEnabled = false; // Pencereyi kilitle

        ScheduleEditService.MoveResult result;
        try
        {
            var sourceBlock = _workingBlocks.First(b => b.Id == _selectedBlock.Id);
            result = await ScheduleEditService.CascadeMoveAsync(
                sourceBlock, targetDay, targetHour,
                _workingBlocks, _teachers, _classes, _rooms,
                _maxDays, _maxHours);
        }
        finally
        {
            // Loading overlay gizle
            LoadingOverlay.Visibility = Visibility.Collapsed;
            IsEnabled = true;
        }

        if (!result.Success)
        {
            MessageBox.Show(
                string.IsNullOrEmpty(result.Message) ? "Taşıma yapılamadı." : result.Message,
                "Taşıma Başarısız", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (result.Changes.Count == 0)
        {
            StatusText.Text = "Blok zaten bu konumda.";
            return;
        }

        // Değişiklikleri in-memory blok listesine uygula
        foreach (var change in result.Changes)
        {
            var block = _workingBlocks.FirstOrDefault(b => b.Id == change.BlockId);
            if (block != null)
            {
                block.Day = change.NewDay;
                block.Hour = change.NewHour;
            }
        }

        _pendingChanges.AddRange(result.Changes);

        // UI güncelle
        _selectedBlock = null;
        SelectedBlockInfo.Text = "";
        BtnUndo.IsEnabled = true;
        BtnApply.IsEnabled = true;

        // Değişen hücreleri vurgula
        var changedBlockIds = _pendingChanges.Select(c => c.BlockId).ToHashSet();
        RenderScheduleGrid(ScheduleGridPanel, GetCurrentTeacherId(), _workingBlocks, true, changedBlockIds);

        // Değişen öğretmenler listesini güncelle
        UpdateChangedTeachersList();

        StatusText.Text = result.Message;
    }

    private void SaveAffectedTeacherSnapshots()
    {
        // Mevcut durumu snapshot olarak sakla (her öğretmenin d_X_Y hücreleri)
        var db = DatabaseManager.Shared;
        var affectedTeacherIds = _workingBlocks
            .Where(b => b.IsPlaced)
            .SelectMany(b => b.TeacherIds)
            .Distinct();

        foreach (var tid in affectedTeacherIds)
        {
            if (_originalTeacherCells.ContainsKey(tid)) continue;

            var rows = db.Query($"SELECT * FROM ogretmen WHERE id = {tid}");
            if (rows.Count > 0)
            {
                var cells = new Dictionary<string, string>();
                for (int d = 1; d <= 7; d++)
                    for (int h = 1; h <= 12; h++)
                    {
                        var key = $"d_{d}_{h}";
                        cells[key] = DatabaseManager.GetString(rows[0], key);
                    }
                _originalTeacherCells[tid] = cells;
            }
        }
    }

    private void UpdateChangedTeachersList()
    {
        var changedTeacherIds = _pendingChanges
            .SelectMany(c =>
            {
                var block = _workingBlocks.FirstOrDefault(b => b.Id == c.BlockId)
                    ?? _originalBlocks.FirstOrDefault(b => b.Id == c.BlockId);
                return block?.TeacherIds ?? new List<int>();
            })
            .Distinct()
            .ToList();

        var changedTeachers = changedTeacherIds
            .Where(id => _teachers.ContainsKey(id))
            .Select(id => _teachers[id])
            .OrderBy(t => t.Name)
            .ToList();

        ChangedTeachersList.ItemsSource = changedTeachers;
        ChangedTeachersHeader.Text = $"Değişen Öğretmenler ({changedTeachers.Count})";
    }

    // ===== DEĞIŞEN ÖĞRETMEN SEÇİMİ (Eski/Yeni durum) =====

    private void ChangedTeachersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChangedTeachersList.SelectedItem is not Teacher selectedTeacher)
        {
            OldStateGridPanel.Children.Clear();
            NewStateGridPanel.Children.Clear();
            OldStateTitle.Text = "Eski Durum";
            NewStateTitle.Text = "Yeni Durum";
            return;
        }

        OldStateTitle.Text = $"Eski Durum - {selectedTeacher.Name}";
        NewStateTitle.Text = $"Yeni Durum - {selectedTeacher.Name}";

        var changedBlockIds = _pendingChanges.Select(c => c.BlockId).ToHashSet();

        // Eski durum: orijinal blokları kullan
        RenderScheduleGrid(OldStateGridPanel, selectedTeacher.Id, _originalBlocks, false, changedBlockIds, true);

        // Yeni durum: çalışma kopyasını kullan
        RenderScheduleGrid(NewStateGridPanel, selectedTeacher.Id, _workingBlocks, false, changedBlockIds, false);
    }

    // ===== GERİ AL =====

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        _workingBlocks = _originalBlocks.Select(b => b.Clone()).ToList();
        _pendingChanges.Clear();
        _selectedBlock = null;
        _originalTeacherCells.Clear();

        BtnUndo.IsEnabled = false;
        BtnApply.IsEnabled = false;
        SelectedBlockInfo.Text = "";
        ChangedTeachersList.ItemsSource = null;
        ChangedTeachersHeader.Text = "Değişen Öğretmenler";
        OldStateGridPanel.Children.Clear();
        NewStateGridPanel.Children.Clear();
        OldStateTitle.Text = "Eski Durum";
        NewStateTitle.Text = "Yeni Durum";

        RenderScheduleGrid(ScheduleGridPanel, GetCurrentTeacherId(), _workingBlocks, true);
        StatusText.Text = "Tüm değişiklikler geri alındı.";
    }

    // ===== ONAYLA =====

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingChanges.Count == 0) return;

        var confirm = MessageBox.Show(
            $"{_pendingChanges.Count} blok değişecek. Onaylıyor musunuz?",
            "Değişiklikleri Onayla",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var repo = new DistributionRepository();
            var db = DatabaseManager.Shared;
            db.Execute("BEGIN TRANSACTION");

            // Her değişen bloğu güncelle
            foreach (var change in _pendingChanges)
            {
                repo.ClearBlock(change.BlockId);
            }

            foreach (var change in _pendingChanges)
            {
                var block = _workingBlocks.First(b => b.Id == change.BlockId);
                repo.PlaceBlock(block, "manuel");
                db.Execute($"UPDATE dagitim_bloklari SET manuel = 1 WHERE id = {change.BlockId}");
            }

            // Görsel tabloları senkronize et
            repo.SyncSignalTables();

            db.Execute("COMMIT");

            _hasSavedChanges = true;

            // UI'yi sıfırla ve yeni başlangıç durumuna dön
            _originalBlocks = _workingBlocks.Select(b => b.Clone()).ToList();
            _pendingChanges.Clear();
            _selectedBlock = null;
            _originalTeacherCells.Clear();

            BtnUndo.IsEnabled = false;
            BtnApply.IsEnabled = false;
            SelectedBlockInfo.Text = "";
            ChangedTeachersList.ItemsSource = null;
            ChangedTeachersHeader.Text = "Değişen Öğretmenler";
            OldStateGridPanel.Children.Clear();
            NewStateGridPanel.Children.Clear();
            OldStateTitle.Text = "Eski Durum";
            NewStateTitle.Text = "Yeni Durum";

            RenderScheduleGrid(ScheduleGridPanel, GetCurrentTeacherId(), _workingBlocks, true);

            StatusText.Text = "Değişiklikler başarıyla kaydedildi.";
            // (Window kapanmaz, aynı ekranda değişikliklere devam edilebilir)
        }
        catch (Exception ex)
        {
            DatabaseManager.Shared.Execute("ROLLBACK");
            MessageBox.Show($"Hata: {ex.Message}", "Kayıt Hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===== YARDIMCI =====

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingChanges.Count > 0)
        {
            var result = MessageBox.Show(
                "Kaydedilmemiş değişiklikler var. Çıkmak istediğinize emin misiniz?",
                "Uyarı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        if (_hasSavedChanges)
        {
            DialogResult = true;
        }
        else
        {
            Close();
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private SolidColorBrush GetScheduleColor(string key)
    {
        if (string.IsNullOrEmpty(key))
            return new SolidColorBrush(Colors.White);

        if (!_colorCache.ContainsKey(key))
        {
            var hash = key.GetHashCode();
            var r = (byte)(200 + (hash & 0x3F));         // 200-255
            var g = (byte)(210 + ((hash >> 6) & 0x2F));  // 210-255
            var b = (byte)(220 + ((hash >> 12) & 0x1F)); // 220-255
            _colorCache[key] = new SolidColorBrush(Color.FromRgb(r, g, b));
        }
        return _colorCache[key];
    }
}
