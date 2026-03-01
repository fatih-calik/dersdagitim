using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DersDagitim.Models;
using DersDagitim.Persistence;

namespace DersDagitim.Views
{
    public partial class TeacherAssignmentWindow : Window
    {
        private readonly TeacherRepository _teacherRepo = new();
        private readonly ClassRepository _classRepo = new();
        private readonly ClassLessonRepository _classLessonRepo = new();
        private readonly LessonRepository _lessonRepo = new();

        private List<Teacher> _allTeachers = new();
        private List<SchoolClass> _allClasses = new();
        private List<Lesson> _allLessons = new();

        private Teacher? _selectedTeacher;
        private SchoolClass? _selectedClass;

        public TeacherAssignmentWindow()
        {
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            _teacherRepo.SyncAllTeacherHours();
            _allTeachers = _teacherRepo.GetAll().OrderBy(t => t.Name).ToList();
            _allClasses = _classRepo.GetAll().OrderBy(c => c.Name).ToList();
            _allLessons = _lessonRepo.GetAll();

            TeachersList.ItemsSource = _allTeachers;
            ClassesList.ItemsSource = _allClasses;
        }

        // ── Search ──────────────────────────────────────────────
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = TxtSearch.Text?.Trim().ToLowerInvariant() ?? "";

            if (string.IsNullOrEmpty(query))
            {
                TeachersList.ItemsSource = _allTeachers;
            }
            else
            {
                TeachersList.ItemsSource = _allTeachers
                    .Where(t => (t.Name?.ToLowerInvariant().Contains(query) ?? false) ||
                                (t.Branch?.ToLowerInvariant().Contains(query) ?? false))
                    .ToList();
            }
        }

        // ── Teacher Selected ────────────────────────────────────
        private void TeachersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedTeacher = TeachersList.SelectedItem as Teacher;

            if (_selectedTeacher != null)
            {
                TxtSelectedTeacher.Text = _selectedTeacher.Name;
                LoadTeacherAssignments();
                // Refresh class lessons to show assignment status
                if (_selectedClass != null)
                    LoadClassLessons();
            }
            else
            {
                TxtSelectedTeacher.Text = "(Öğretmen Seçin)";
                AssignmentsList.ItemsSource = null;
                TxtTotalHours.Text = "0 / 0 saat";
            }
        }

        // ── Class Selected ──────────────────────────────────────
        private void ClassesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedClass = ClassesList.SelectedItem as SchoolClass;

            if (_selectedClass != null)
            {
                TxtLessonsHeader.Text = $"{_selectedClass.Name} DERSLERİ";
                LoadClassLessons();
            }
            else
            {
                TxtLessonsHeader.Text = "SINIF DERSLERİ";
                LessonsList.ItemsSource = null;
            }
        }

        // ── Load Class Lessons ──────────────────────────────────
        private void LoadClassLessons()
        {
            if (_selectedClass == null) return;

            var classLessons = _classLessonRepo.GetByClassId(_selectedClass.Id);
            var viewModels = new List<TeacherClassLessonItem>();

            foreach (var cl in classLessons)
            {
                var lesson = _allLessons.FirstOrDefault(l => l.Id == cl.LessonId);
                if (lesson == null) continue;

                var assignments = _classLessonRepo.GetTeacherAssignments(cl.Id);
                var teacherNames = new List<string>();
                bool isAssignedToSelected = false;

                foreach (var a in assignments)
                {
                    var teacher = _allTeachers.FirstOrDefault(t => t.Id == a.TeacherId);
                    if (teacher != null)
                        teacherNames.Add(teacher.Name);

                    if (_selectedTeacher != null && a.TeacherId == _selectedTeacher.Id)
                        isAssignedToSelected = true;
                }

                // Calculate block pattern
                string defaultBlock = lesson.DefaultBlock ?? "2";
                string pattern = CalculatePattern(defaultBlock, cl.TotalHours);

                viewModels.Add(new TeacherClassLessonItem
                {
                    ClassLessonId = cl.Id,
                    LessonName = lesson.Name ?? "",
                    LessonCode = lesson.Code ?? "",
                    BlockPattern = pattern,
                    TotalHours = cl.TotalHours,
                    AssignedTeachers = teacherNames.Count > 0 ? string.Join(", ", teacherNames) : "Atanmamış",
                    IsAssignedToSelected = isAssignedToSelected
                });
            }

            LessonsList.ItemsSource = viewModels;
        }

        // ── Load Teacher Assignments (List 4) ───────────────────
        private void LoadTeacherAssignments()
        {
            if (_selectedTeacher == null) return;

            var details = _teacherRepo.GetAssignmentsWithId(_selectedTeacher.Id);
            AssignmentsList.ItemsSource = details;

            int totalAssigned = details.Sum(d => d.TotalHours);
            TxtTotalHours.Text = $"{totalAssigned} / {_selectedTeacher.MaxHours} saat";
        }

        // ── Double-click on lesson to assign ────────────────────
        private void LessonsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DoAssignTeacher();
        }

        // ── Assign Teacher (button) ────────────────────────────
        private void AssignTeacher_Click(object sender, RoutedEventArgs e)
        {
            DoAssignTeacher();
        }

        private void DoAssignTeacher()
        {
            if (_selectedTeacher == null)
            {
                MessageBox.Show("Lütfen önce bir öğretmen seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedLesson = LessonsList.SelectedItem as TeacherClassLessonItem;
            if (selectedLesson == null)
            {
                MessageBox.Show("Lütfen bir ders seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check if already assigned
            if (selectedLesson.IsAssignedToSelected)
            {
                MessageBox.Show($"{_selectedTeacher.Name} zaten bu derse atanmış.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _classLessonRepo.AddTeacherAssignment(selectedLesson.ClassLessonId, _selectedTeacher.Id, selectedLesson.TotalHours);
                RefreshAfterChange();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Atama sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Remove Assignment ───────────────────────────────────
        private void RemoveAssignment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int assignmentId)
            {
                try
                {
                    _classLessonRepo.RemoveTeacherAssignment(assignmentId);
                    RefreshAfterChange();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Silme sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ── Refresh After Change ────────────────────────────────
        private void RefreshAfterChange()
        {
            // Refresh teacher hours from DB
            _teacherRepo.SyncAllTeacherHours();

            // Refresh the selected teacher's data
            if (_selectedTeacher != null)
            {
                var updated = _teacherRepo.GetAll().FirstOrDefault(t => t.Id == _selectedTeacher.Id);
                if (updated != null)
                    _selectedTeacher = updated;
            }

            // Reload teachers list (for hour badges) while preserving selection
            int selectedTeacherId = _selectedTeacher?.Id ?? 0;
            _allTeachers = _teacherRepo.GetAll().OrderBy(t => t.Name).ToList();

            string query = TxtSearch.Text?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(query))
                TeachersList.ItemsSource = _allTeachers;
            else
                TeachersList.ItemsSource = _allTeachers
                    .Where(t => (t.Name?.ToLowerInvariant().Contains(query) ?? false) ||
                                (t.Branch?.ToLowerInvariant().Contains(query) ?? false))
                    .ToList();

            // Restore teacher selection
            if (selectedTeacherId > 0)
            {
                var list = TeachersList.ItemsSource as IEnumerable<Teacher>;
                var match = list?.FirstOrDefault(t => t.Id == selectedTeacherId);
                if (match != null)
                {
                    TeachersList.SelectedItem = match;
                }
            }

            // Reload List 4 (assignments)
            LoadTeacherAssignments();

            // Reload List 3 (class lessons - to update green highlights)
            if (_selectedClass != null)
                LoadClassLessons();
        }

        // ── Helper: Calculate Pattern ───────────────────────────
        private string CalculatePattern(string defaultBlock, int totalHours)
        {
            if (string.IsNullOrEmpty(defaultBlock)) defaultBlock = "2";

            var parts = defaultBlock.Split('+').Select(s => int.TryParse(s, out int n) ? n : 1).ToList();
            if (parts.Count == 0 || parts.All(p => p == 0)) parts = new List<int> { 2 };

            var pattern = new List<int>();
            int currentTotal = 0;
            int partIndex = 0;

            while (currentTotal < totalHours)
            {
                int next = parts[partIndex % parts.Count];
                if (currentTotal + next > totalHours)
                    next = totalHours - currentTotal;

                if (next > 0)
                {
                    pattern.Add(next);
                    currentTotal += next;
                }
                else
                {
                    currentTotal++;
                }
                partIndex++;
            }

            return string.Join("+", pattern);
        }
    }

    // ── ViewModel for List 3 ────────────────────────────────────
    public class TeacherClassLessonItem
    {
        public int ClassLessonId { get; set; }
        public string LessonName { get; set; } = "";
        public string LessonCode { get; set; } = "";
        public string BlockPattern { get; set; } = "";
        public int TotalHours { get; set; }
        public string AssignedTeachers { get; set; } = "";
        public bool IsAssignedToSelected { get; set; }
    }
}
