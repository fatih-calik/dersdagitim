using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using DersDagitim.Models;
using DersDagitim.Persistence;

namespace DersDagitim.Services
{
    public class PdfReportGenerator
    {
        private readonly SchoolInfo _schoolInfo;
        private readonly List<Lesson> _allLessons;
        private readonly Dictionary<int, string> _teacherNames;

        public PdfReportGenerator()
        {
            // QuestPDF License (Community)
            Settings.License = LicenseType.Community;
            
            var schoolRepo = new SchoolRepository();
            _schoolInfo = schoolRepo.GetSchoolInfo();

            var lessonRepo = new LessonRepository();
            _allLessons = lessonRepo.GetAll();

            var teacherRepo = new TeacherRepository();
            _teacherNames = teacherRepo.GetAll().ToDictionary(t => t.Id, t => t.Name);
        }

        public byte[] GenerateTeacherSchedule(string documentNo, string reportDate)
        {
            var teacherRepo = new TeacherRepository();
            var teachers = teacherRepo.GetAll().OrderBy(t => t.Name).ToList();

            var classRepo = new ClassRepository();
            var classes = classRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);

            var document = Document.Create(container =>
            {
                foreach (var teacher in teachers)
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Portrait());
                        page.Margin(0.5f, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

                        page.Header().Element(header => ComposeHeader(header, "ÖĞRETMEN DERS PROGRAMI", documentNo));
                        
                        page.Content().Element(content => ComposeContent(content, teacher, classes, reportDate, _schoolInfo, documentNo));

                        page.Footer().Element(footer => ComposeFooter(footer, reportDate));
                    });
                }
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        public byte[] GenerateClassSchedule(string documentNo, string reportDate)
        {
            var classRepo = new ClassRepository();
            var classes = classRepo.GetAll().OrderBy(c => c.Name).ToList();

            var document = Document.Create(container =>
            {
                foreach (var cls in classes)
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Portrait());
                        page.Margin(0.5f, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

                        page.Header().Element(header => ComposeHeader(header, "SINIF DERS PROGRAMI", documentNo));
                        
                        page.Content().Element(content => ComposeClassContent(content, cls, reportDate, _schoolInfo, documentNo));

                        page.Footer().Element(footer => ComposeClassFooter(footer));
                    });
                }
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        private void ComposeHeader(IContainer container, string title, string documentNo)
        {
            var year = GetAcademicYear();
            
            container.Column(column =>
            {
                column.Item().AlignCenter().Text("T.C.").FontSize(11).Bold();
                column.Item().AlignCenter().Text(_schoolInfo.Name.ToUpper()).FontSize(12).Bold();
                column.Item().AlignCenter().Text($"{year} EĞİTİM ÖĞRETİM YILI").FontSize(11).Bold();
                column.Item().AlignCenter().Text(title.ToUpper()).FontSize(11).Bold();
                column.Item().PaddingTop(5);
            });
        }

        private void ComposeContent(IContainer container, Teacher teacher, Dictionary<int, string> classes, string reportDate, Models.SchoolInfo schoolInfo, string documentNo)
        {
            string guidance = teacher.Guidance > 0 && classes.ContainsKey(teacher.Guidance) ? classes[teacher.Guidance] : "-";
            string club = teacher.Club ?? "-";
            string duty = string.IsNullOrEmpty(teacher.DutyDay) ? "-" : $"{teacher.DutyDay} {teacher.DutyLocation}";
            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            container.Column(column =>
            {
                // Info Table
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(4); // Adı Soyadı + Sayı
                        columns.RelativeColumn(3); // Sınıf Öğrt / Eğitici Kol
                        columns.RelativeColumn(3); // Nöbet
                    });

                    // Row 1: Document No & Date Context
                    table.Cell().RowSpan(2).AlignLeft().Column(c => 
                    {
                        if (!string.IsNullOrEmpty(documentNo))
                            c.Item().Text($"Sayı : {documentNo}").FontSize(9);
                        
                        c.Item().Text($"Adı Soyadı : {teacher.Name}").FontSize(11).Bold();
                    });
                    
                    table.Cell().Text($"Sınıf Öğretmenliği : {guidance}");
                    table.Cell().AlignRight().Text($"Nöbet Günü ve Yeri : {duty}");

                    // Row 2
                    table.Cell().Text($"Eğitici Kolu(Kulüp) : {club}");
                    table.Cell().Text("");
                });
                
                column.Item().PaddingTop(10);

                // Main Schedule Table
                column.Item().Element(c => ComposeScheduleTable(c, teacher));
                
                column.Item().PaddingTop(10).Text($"Yukarıdaki dersler {pDate} tarihinde şahsınıza verilmiştir. Bilgilerinizi rica ederim.").FontSize(10).Italic();

                column.Item().PaddingTop(10).Element(ComposeSignature);
                
                column.Item().PaddingTop(20);
                
                // Assigned Classes/Lessons Summary (Bottom Left)
                column.Item().Element(c => ComposeLessonSummary(c, teacher));
            });
        }

        private void ComposeSignature(IContainer container)
        {
            container.PaddingTop(10).Row(row => 
            {
                row.RelativeItem().Text("Aslını aldım.\n\n\nİmza");
                
                row.RelativeItem().AlignRight().Column(c => {
                    c.Item().Text("Okul Müdürü").Bold();
                    c.Item().Text(_schoolInfo.Principal).Bold();
                });
            });
        }

        private void ComposeClassContent(IContainer container, SchoolClass cls, string reportDate, Models.SchoolInfo schoolInfo, string documentNo)
        {
            container.Column(column =>
            {
                // Title: Sınıf Adı
                column.Item().PaddingBottom(10).Text($"Sınıf: {cls.Name}").FontSize(14).Bold();

                // Schedule Table
                column.Item().Element(c => ComposeClassScheduleTable(c, cls));

                column.Item().PaddingTop(15);

                // Assigned Lessons/Teachers Summary
                column.Item().Element(c => ComposeClassLessonSummary(c, cls));
            });
        }

        private void ComposeLessonSummary(IContainer container, Teacher teacher)
        {
            var summary = new Dictionary<string, int>(); // Key: "Class - LessonCode", Value: Count
            
            foreach(var val in teacher.ScheduleInfo.Values)
            {
                 if (string.IsNullOrEmpty(val) || val.Contains("KAPALI", StringComparison.OrdinalIgnoreCase)) continue;
                 
                 string v = string.Join(" ", val.Split(new[]{' ','\t'}, StringSplitOptions.RemoveEmptyEntries));
                 if(summary.ContainsKey(v)) summary[v]++;
                 else summary[v] = 1;
            }
            
            if (summary.Count == 0) return;

            container.Table(table => 
            {
                table.ColumnsDefinition(cols => 
                {
                    cols.ConstantColumn(25); // Seq
                    cols.ConstantColumn(60); // Class
                    cols.RelativeColumn(2);  // Lesson Name
                    cols.RelativeColumn(1);  // Lesson Code
                    cols.ConstantColumn(35); // Hours
                });
                
                table.Header(header => 
                {
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).Text("Sıra").Bold().FontSize(8);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).Text("Sınıf").Bold().FontSize(8);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).Text("Ders Adı").Bold().FontSize(8);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).Text("Ders").Bold().FontSize(8);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).Text("Saat").Bold().FontSize(8);
                });
                
                int seq = 1;
                int grandTotal = 0;
                var parsedList = summary.Select(kv => 
                {
                    var parts = kv.Key.Split(' ');
                    string cName = parts[0];
                    string lCode = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";
                    
                    // Find full name if available
                    string lFullName = _allLessons.FirstOrDefault(l => l.Code == lCode)?.Name ?? "";
                    
                    return new { ClassName = cName, LessonCode = lCode, LessonName = lFullName, Count = kv.Value };
                }).OrderBy(x => x.ClassName).ThenBy(x => x.LessonCode).ToList();

                foreach(var item in parsedList)
                {
                     table.Cell().Text($"{seq++}").FontSize(8);
                     table.Cell().Text(item.ClassName).FontSize(8);
                     table.Cell().Text(item.LessonName).FontSize(8);
                     table.Cell().Text(item.LessonCode).FontSize(8);
                     table.Cell().AlignCenter().Text($"{item.Count}").FontSize(8);
                     grandTotal += item.Count;
                }
                
                table.Cell().ColumnSpan(4).AlignRight().Text("Toplam :").Bold().FontSize(8);
                table.Cell().AlignCenter().Text($"{grandTotal}").Bold().FontSize(8);
            });
        }
        
        private void ComposeClassLessonSummary(IContainer container, SchoolClass cls)
        {
            var distRepo = new DistributionRepository();
            var allBlocks = distRepo.GetAllBlocks().Where(b => b.ClassId == cls.Id).ToList();
            
            if (allBlocks.Count == 0) return;

            // Group by LessonCode to keep same subject together
            var groupedByLesson = allBlocks
                .GroupBy(b => b.LessonCode)
                .OrderBy(g => g.Key)
                .ToList();

            container.Table(table => 
            {
                table.ColumnsDefinition(cols => 
                {
                    cols.ConstantColumn(25); // Seq
                    cols.RelativeColumn(3);  // Lesson (Code)
                    cols.RelativeColumn(4);  // Teacher
                    cols.ConstantColumn(35); // Hours
                });
                
                table.Header(header => 
                {
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).Text("Sıra").Bold().FontSize(8);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).Text("Ders").Bold().FontSize(8);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).Text("Öğretmen").Bold().FontSize(8);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).Text("Saat").Bold().FontSize(8);
                });
                
                int seq = 1;
                int grandTotal = 0;
                
                foreach(var lessonGroup in groupedByLesson)
                {
                    string lessonCode = lessonGroup.Key;
                    string lessonFullName = _allLessons.FirstOrDefault(l => l.Code == lessonCode)?.Name ?? "";
                    string lessonDisplay = !string.IsNullOrEmpty(lessonFullName) ? $"{lessonFullName}  -  {lessonCode}" : lessonCode;
                    int lessonTotalHours = lessonGroup.Sum(b => b.BlockDuration);
                    grandTotal += lessonTotalHours;

                    // Find all unique teacher IDs for this lesson in this class
                    var teacherIds = lessonGroup.SelectMany(b => b.TeacherIds).Distinct().OrderBy(id => id).ToList();

                    bool isFirstInLesson = true;
                    foreach(var tid in teacherIds)
                    {
                        string teacherName = _teacherNames.ContainsKey(tid) ? _teacherNames[tid] : $"ID:{tid}";

                        table.Cell().Text(isFirstInLesson ? $"{seq++}" : "").FontSize(8);
                        table.Cell().Text(isFirstInLesson ? lessonDisplay : "").FontSize(8);
                        table.Cell().Text(teacherName).FontSize(8);
                        table.Cell().AlignCenter().Text(isFirstInLesson ? $"{lessonTotalHours}" : "").FontSize(8);

                        isFirstInLesson = false;
                    }

                    if (teacherIds.Count == 0)
                    {
                        table.Cell().Text($"{seq++}").FontSize(8);
                        table.Cell().Text(lessonDisplay).FontSize(8);
                        table.Cell().Text("-").FontSize(8);
                        table.Cell().AlignCenter().Text($"{lessonTotalHours}").FontSize(8);
                    }
                }
                
                table.Cell().ColumnSpan(3).AlignRight().Text("Toplam :").Bold().FontSize(8);
                table.Cell().AlignCenter().Text($"{grandTotal}").Bold().FontSize(8);
            });
        }
        
        private int GetDynamicMaxLessons()
        {
            // Default to configured daily count if anything goes wrong
            int maxLessons = _schoolInfo.DailyLessonCount > 0 ? _schoolInfo.DailyLessonCount : 8;
            
            // If we have a timetable, check actual usage
            if (_schoolInfo.DefaultTimetable != null && _schoolInfo.DefaultTimetable.Count > 0)
            {
                int calculatedMax = 0;
                
                // Iterate through each day
                for (int d = 1; d <= _schoolInfo.Days; d++)
                {
                    // Find the last open hour for this day. Check up to 12 as that is the hard limit in schema.
                    for (int h = 12; h >= 1; h--)
                    {
                        var slot = new TimeSlot(d, h);
                        // If slot exists and is NOT Closed, then this is the max for this day
                        if (_schoolInfo.DefaultTimetable.TryGetValue(slot, out var state))
                        {
                            if (state != SlotState.Closed)
                            {
                                if (h > calculatedMax) calculatedMax = h;
                                break; // Found the max for this day
                            }
                        }
                    }
                }
                
                // If we found a valid calculated max, use it. 
                if (calculatedMax > 0)
                {
                    maxLessons = calculatedMax;
                }
            }
            
            return maxLessons;
        }

        private void ComposeScheduleTable(IContainer container, Teacher teacher)
        {
            string[] dayNames = { "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar" };
            int visibleDays = _schoolInfo.Days < 5 ? 5 : _schoolInfo.Days; 
            
            int maxLessons = GetDynamicMaxLessons();

            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(55); // Day Column reduced for Portrait
                    for(int i=0; i<maxLessons; i++) columns.RelativeColumn();
                });

                // Header Row
                table.Header(header =>
                {
                    header.Cell().Border(0.5f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4).AlignCenter().AlignMiddle().Text("Günler").Bold().FontSize(8);
                    
                    for (int h = 1; h <= maxLessons; h++)
                    {
                        string timeInfo = "";
                        if (_schoolInfo.LessonHours != null && h <= _schoolInfo.LessonHours.Length)
                        {
                            timeInfo = _schoolInfo.LessonHours[h-1].Replace(" ", "").Trim();
                        }
                        
                        header.Cell().Border(0.5f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4).AlignCenter().AlignMiddle().Column(col => 
                        {
                            col.Item().AlignCenter().Text($"{h}").Bold().FontSize(8);
                            if(!string.IsNullOrEmpty(timeInfo)) 
                                col.Item().AlignCenter().Text(timeInfo).FontSize(7);
                        });
                    }
                });

                // Data Rows
                for (int d = 1; d <= visibleDays; d++)
                {
                    // Day Name row setup
                    table.Cell().Border(0.5f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten5).MinHeight(40).AlignCenter().AlignMiddle().Text(dayNames[d-1]).Bold().FontSize(8);

                    // Lessons
                    for (int h = 1; h <= maxLessons; h++)
                    {
                        var slot = new Models.TimeSlot(d, h);
                        string cls = "";
                        string lsn = "";
                        
                        if (teacher.ScheduleInfo.ContainsKey(slot))
                        {
                            var raw = teacher.ScheduleInfo[slot];
                            if (!string.IsNullOrEmpty(raw) && !raw.Contains("KAPALI", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2)
                                {
                                    cls = parts[0];
                                    lsn = string.Join(" ", parts.Skip(1));
                                }
                                else
                                {
                                    cls = raw;
                                }
                            }
                        }

                        table.Cell().Border(0.5f).BorderColor(Colors.Black).MinHeight(40).AlignCenter().AlignMiddle().Column(col =>
                        {
                            if (!string.IsNullOrEmpty(cls))
                            {
                                col.Item().Text(cls).Bold().FontSize(9);
                                col.Item().Text(lsn).FontSize(8);
                            }
                        });
                    }
                }
            });
        }

        private void ComposeClassScheduleTable(IContainer container, SchoolClass cls)
        {
            string[] dayNames = { "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar" };
            int visibleDays = _schoolInfo.Days < 5 ? 5 : _schoolInfo.Days; 
            
            int maxLessons = GetDynamicMaxLessons();

            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(55); // Day Column
                    for(int i=0; i<maxLessons; i++) columns.RelativeColumn();
                });

                // Header Row
                table.Header(header =>
                {
                    header.Cell().Border(0.5f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4).AlignCenter().AlignMiddle().Text("Günler").Bold().FontSize(8);
                    
                    for (int h = 1; h <= maxLessons; h++)
                    {
                        string timeInfo = "";
                        if (_schoolInfo.LessonHours != null && h <= _schoolInfo.LessonHours.Length)
                        {
                            timeInfo = _schoolInfo.LessonHours[h-1].Replace(" ", "").Trim();
                        }
                        
                        header.Cell().Border(0.5f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4).AlignCenter().AlignMiddle().Column(col => 
                        {
                            col.Item().AlignCenter().Text($"{h}").Bold().FontSize(8);
                            if(!string.IsNullOrEmpty(timeInfo)) 
                                col.Item().AlignCenter().Text(timeInfo).FontSize(7);
                        });
                    }
                });

                // Data Rows
                for (int d = 1; d <= visibleDays; d++)
                {
                    table.Cell().Border(0.5f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten5).MinHeight(60).AlignCenter().AlignMiddle().Text(dayNames[d-1]).Bold().FontSize(8); // Increased MinHeight

                    for (int h = 1; h <= maxLessons; h++)
                    {
                        var key = $"d_{d}_{h}";
                        string lessonName = "";
                        string teacherName = "";
                        
                        if (cls.Schedule.ContainsKey(key))
                        {
                            var raw = cls.Schedule[key];
                            if (!string.IsNullOrEmpty(raw) && !raw.Contains("KAPALI", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = raw.Split(new[] { " - ", "\t", "   ", "  " }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2)
                                {
                                    lessonName = parts[0];
                                    string rawTeacher = string.Join(" ", parts.Skip(1));
                                    var teacherList = rawTeacher.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                    var formattedList = teacherList.Select(t => FormatTeacherName(t.Trim()));
                                    teacherName = string.Join("\n", formattedList); // Use newline for PDF text block or handle in Column items
                                }
                                else
                                {
                                    lessonName = raw;
                                }
                            }
                        }

                        table.Cell().Border(0.5f).BorderColor(Colors.Black).MinHeight(60).AlignCenter().AlignMiddle().Column(col => // Increased MinHeight
                        {
                            if (!string.IsNullOrEmpty(lessonName))
                            {
                                col.Item().Text(lessonName).Bold().FontSize(9);
                                if (!string.IsNullOrEmpty(teacherName))
                                {
                                    // Split by newline to ensure strict separate lines if needed, or rely on Text.
                                    // QuestPDF Text supports \n but sometimes it's cleaner to have separate items for control.
                                    var lines = teacherName.Split('\n');
                                    foreach(var line in lines)
                                    {
                                         col.Item().Text(line).FontSize(7).FontColor(Colors.Grey.Darken2); 
                                    }
                                }
                            }
                        });
                    }
                }
            });
        }

        private void ComposeFooter(IContainer container, string reportDate)
        {
            container.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8);
        }

        private void ComposeClassFooter(IContainer container)
        {
            container.Column(col => 
            {
               col.Item().PaddingTop(20).Row(row => 
               {
                   row.RelativeItem().Text(""); 
                   
                   row.RelativeItem().AlignRight().Column(c => {
                       c.Item().Text("Okul Müdürü").Bold();
                       c.Item().Text(_schoolInfo.Principal).Bold();
                   });
               });
            });
        }
        
        public byte[] GenerateTeacherMasterSchedule()
        {
            var teacherRepo = new TeacherRepository();
            var teachers = teacherRepo.GetAll().OrderBy(t => t.Name).ToList();
            string academicYear = GetAcademicYear();
            int visibleDays = _schoolInfo.Days;
            int dailyLessons = GetDynamicMaxLessons();

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(0.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(7));
                    
                    page.Header().Element(h => ComposeHeader(h, "ÖĞRETMEN MASTER (ÇARŞAF) LİSTESİ", ""));

                    page.Content().PaddingTop(5).Table(table => 
                    {
                        table.ColumnsDefinition(cols => 
                        {
                            cols.RelativeColumn(3); // Name
                            for(int d=0; d<visibleDays; d++)
                                for(int h=0; h<dailyLessons; h++)
                                    cols.RelativeColumn(1);
                        });

                        table.Header(h => 
                        {
                            h.Cell().RowSpan(2).Border(0.5f).Background(Colors.Grey.Lighten4).AlignCenter().AlignMiddle().Text("Öğretmen").Bold();
                            
                            string[] dayNames = { "PTESİ", "SALI", "ÇARŞ", "PERŞ", "CUMA", "CMT", "PAZ" };
                            for (int d = 1; d <= visibleDays; d++)
                                h.Cell().ColumnSpan((uint)dailyLessons).Border(0.5f).Background(Colors.Grey.Lighten4).AlignCenter().Text(dayNames[d-1]).Bold();
                            
                            for (int d = 1; d <= visibleDays; d++)
                                for (int hour = 1; hour <= dailyLessons; hour++)
                                    h.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).AlignCenter().Text($"{hour}").Bold();
                        });

                        foreach(var t in teachers)
                        {
                            table.Cell().Border(0.5f).PaddingLeft(2).AlignLeft().AlignMiddle().Text(FormatTeacherName(t.Name)).Bold().FontSize(8);
                            for (int d = 1; d <= visibleDays; d++)
                            {
                                for (int hour = 1; hour <= dailyLessons; hour++)
                                {
                                    var slot = new TimeSlot(d, hour);
                                    string content = "";
                                    if (t.ScheduleInfo.ContainsKey(slot))
                                    {
                                        var raw = t.ScheduleInfo[slot];
                                        if(!string.IsNullOrEmpty(raw) && !raw.Contains("KAPALI", StringComparison.OrdinalIgnoreCase))
                                        {
                                            var parts = raw.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
                                            content = parts.Length > 0 ? parts[0] : "";
                                        }
                                    }
                                    table.Cell().Border(0.5f).AlignCenter().AlignMiddle().Text(content).FontSize(6);
                                }
                            }
                        }
                    });

                    page.Footer().Element(f => f.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8));
                });
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        public byte[] GenerateClassMasterSchedule()
        {
            var classRepo = new ClassRepository();
            var classes = classRepo.GetAll().OrderBy(c => c.Name).ToList();
            string academicYear = GetAcademicYear();
            int visibleDays = _schoolInfo.Days;
            int dailyLessons = GetDynamicMaxLessons();

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(0.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(7));
                    
                    page.Header().Element(h => ComposeHeader(h, "SINIF MASTER (ÇARŞAF) LİSTESİ", ""));

                    page.Content().PaddingTop(5).Table(table => 
                    {
                        table.ColumnsDefinition(cols => 
                        {
                            cols.RelativeColumn(2); // Class Name
                            for(int d=0; d<visibleDays; d++)
                                for(int h=0; h<dailyLessons; h++)
                                    cols.RelativeColumn(1);
                        });

                        table.Header(h => 
                        {
                            h.Cell().RowSpan(2).Border(0.5f).Background(Colors.Grey.Lighten4).AlignCenter().AlignMiddle().Text("Sınıf").Bold();
                            
                            string[] dayNames = { "PTESİ", "SALI", "ÇARŞ", "PERŞ", "CUMA", "CMT", "PAZ" };
                            for (int d = 1; d <= visibleDays; d++)
                                h.Cell().ColumnSpan((uint)dailyLessons).Border(0.5f).Background(Colors.Grey.Lighten4).AlignCenter().Text(dayNames[d-1]).Bold();
                            
                            for (int d = 1; d <= visibleDays; d++)
                                for (int hour = 1; hour <= dailyLessons; hour++)
                                    h.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).AlignCenter().Text($"{hour}").Bold();
                        });

                        foreach(var c in classes)
                        {
                            table.Cell().Border(0.5f).AlignCenter().AlignMiddle().Text(c.Name).Bold().FontSize(8);
                            for (int d = 1; d <= visibleDays; d++)
                            {
                                for (int hour = 1; hour <= dailyLessons; hour++)
                                {
                                    var key = $"d_{d}_{hour}";
                                    string content = "";
                                    if (c.Schedule.ContainsKey(key))
                                    {
                                        var raw = c.Schedule[key];
                                        if(!string.IsNullOrEmpty(raw) && !raw.Contains("KAPALI", StringComparison.OrdinalIgnoreCase))
                                        {
                                            var parts = raw.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
                                            content = parts.Length > 0 ? parts[0] : "";
                                        }
                                    }
                                    table.Cell().Border(0.5f).AlignCenter().AlignMiddle().Text(content).FontSize(6);
                                }
                            }
                        }
                    });

                    page.Footer().Element(f => f.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8));
                });
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        public byte[] GenerateAssignmentsReport()
        {
            var db = DatabaseManager.Shared;
            var sql = @"
                SELECT s.ad as ClassName, d.ad as LessonName, o.ad_soyad as TeacherName, a.atanan_saat as Hours 
                FROM atama a 
                JOIN sinif_ders sd ON a.sinif_ders_id = sd.id
                JOIN sinif s ON sd.sinif_id = s.id 
                JOIN ders d ON sd.ders_id = d.id 
                JOIN ogretmen o ON a.ogretmen_id = o.id 
                ORDER BY s.ad, o.ad_soyad";
            
            var results = db.Query(sql);
            var teachers = results.GroupBy(r => DatabaseManager.GetString(r, "TeacherName"))
                                  .OrderBy(g => g.Key).ToList();

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Portrait());
                    page.Margin(1, Unit.Centimetre);
                    page.Header().Element(h => ComposeHeader(h, "ÖĞRETMEN BAZLI DERS DAĞILIM LİSTESİ", ""));

                    page.Content().PaddingTop(10).Table(table => 
                    {
                        table.ColumnsDefinition(cols => 
                        {
                            cols.RelativeColumn(3); // Teacher
                            cols.RelativeColumn(2); // Class
                            cols.RelativeColumn(4); // Lesson
                            cols.ConstantColumn(30); // Hours
                        });

                        table.Header(h => 
                        {
                            h.Cell().BorderBottom(1).Padding(3).Text("Öğretmen").Bold();
                            h.Cell().BorderBottom(1).Padding(3).Text("Sınıf").Bold();
                            h.Cell().BorderBottom(1).Padding(3).Text("Ders").Bold();
                            h.Cell().BorderBottom(1).Padding(3).Text("Saat").Bold();
                        });

                        foreach(var grp in teachers)
                        {
                            bool isFirstLine = true;
                            int tTotal = 0;
                            foreach(var row in grp)
                            {
                                int h = DatabaseManager.GetInt(row, "Hours");
                                tTotal += h;
                                table.Cell().BorderBottom(0.1f).Padding(2).Text(isFirstLine ? grp.Key : "").Bold();
                                table.Cell().BorderBottom(0.1f).Padding(2).Text(DatabaseManager.GetString(row, "ClassName"));
                                table.Cell().BorderBottom(0.1f).Padding(2).Text(DatabaseManager.GetString(row, "LessonName"));
                                table.Cell().BorderBottom(0.1f).Padding(2).AlignCenter().Text($"{h}");
                                isFirstLine = false;
                            }
                            table.Cell().RowSpan(1).ColumnSpan(3).AlignRight().PaddingRight(5).Text("Öğretmen Toplam:").Bold().FontSize(8);
                            table.Cell().AlignCenter().Text($"{tTotal}").Bold();
                        }
                    });
                    page.Footer().Element(f => f.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8));
                });
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        public byte[] GenerateElectiveRatioReport()
        {
            var db = DatabaseManager.Shared;
            var sql = @"
                SELECT d.ad as LessonName, d.kod as LessonCode, SUM(a.toplam_saat) as TotalHours 
                FROM atama a 
                JOIN ders d ON a.ders_id = d.id 
                GROUP BY d.id";
                
            var results = db.Query(sql);
            int totalComp = 0, totalElec = 0;
            var elecList = new List<(string Name, int Hours)>();
            var compList = new List<(string Name, int Hours)>();

            foreach(var row in results)
            {
                string name = DatabaseManager.GetString(row, "LessonName");
                string code = DatabaseManager.GetString(row, "LessonCode");
                int h = DatabaseManager.GetInt(row, "TotalHours");
                bool isElec = name.Contains("SEÇMELİ", StringComparison.OrdinalIgnoreCase) || code.StartsWith("S.");
                if(isElec) { totalElec += h; elecList.Add((name, h)); }
                else { totalComp += h; compList.Add((name, h)); }
            }

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Portrait());
                    page.Margin(1, Unit.Centimetre);
                    page.Header().Element(h => ComposeHeader(h, "SEÇMELİ / ZORUNLU DERS ORANLARI", ""));

                    page.Content().PaddingTop(10).Column(col => 
                    {
                        col.Item().PaddingBottom(10).Row(row => 
                        {
                            row.RelativeItem().Column(c => {
                                c.Item().AlignCenter().Text("Zorunlu").FontSize(10);
                                c.Item().AlignCenter().Text($"{totalComp}").FontSize(18).Bold().FontColor(Colors.Green.Medium);
                            });
                            row.RelativeItem().Column(c => {
                                c.Item().AlignCenter().Text("Seçmeli").FontSize(10);
                                c.Item().AlignCenter().Text($"{totalElec}").FontSize(18).Bold().FontColor(Colors.Red.Medium);
                            });
                        });

                        col.Item().Table(table => 
                        {
                            table.ColumnsDefinition(cols => { cols.RelativeColumn(); cols.ConstantColumn(50); });
                            table.Header(h => { h.Cell().BorderBottom(1).Text("Ders Adı").Bold(); h.Cell().BorderBottom(1).Text("Saat").Bold(); });
                            foreach(var item in compList.OrderByDescending(x => x.Hours))
                            {
                                table.Cell().Padding(2).Text(item.Name);
                                table.Cell().Padding(2).AlignCenter().Text($"{item.Hours}");
                            }
                            table.Cell().Padding(2).Background(Colors.Grey.Lighten4).Text("Seçmeli Dersler").Bold();
                            table.Cell().Padding(2).Background(Colors.Grey.Lighten4).Text("");
                            foreach(var item in elecList.OrderByDescending(x => x.Hours))
                            {
                                table.Cell().Padding(2).Text(item.Name).FontColor(Colors.Red.Darken2);
                                table.Cell().Padding(2).AlignCenter().Text($"{item.Hours}");
                            }
                        });
                    });
                    page.Footer().Element(f => f.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8));
                });
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        public byte[] GenerateTeacherDailySchedule()
        {
            var teacherRepo = new TeacherRepository();
            var teachers = teacherRepo.GetAll().OrderBy(t => t.Name).ToList();
            string[] dayNames = { "PAZARTESİ", "SALI", "ÇARŞAMBA", "PERŞEMBE", "CUMA", "CUMARTESİ", "PAZAR" };

            // Determine max lessons - same logic as personal schedules
            int lessons = _schoolInfo.DailyLessonCount;
            if (_schoolInfo.LessonHours != null) 
            {
                int hoursWithData = _schoolInfo.LessonHours.Count(h => !string.IsNullOrWhiteSpace(h));
                lessons = Math.Max(lessons, hoursWithData);
            }
            if (lessons <= 0) lessons = 8;

            var document = Document.Create(container =>
            {
                foreach(int day in Enumerable.Range(1, _schoolInfo.Days))
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Portrait());
                        page.Margin(0.5f, Unit.Centimetre);
                        page.Header().Element(h => ComposeHeader(h, $"ÖĞRETMEN GÜNLÜK DERS PROGRAMI ({dayNames[day-1]})", ""));
                        page.Content().PaddingTop(10).Table(table => 
                        {
                            table.ColumnsDefinition(cols => {
                                cols.RelativeColumn(3);
                                for(int i=0; i<lessons; i++) cols.RelativeColumn(1);
                            });
                            table.Header(h => {
                                h.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).Padding(2).Text("Öğretmen").Bold();
                                for(int i=1; i<=lessons; i++)
                                {
                                    string timeInfo = "";
                                    if(_schoolInfo.LessonHours != null && i <= _schoolInfo.LessonHours.Length)
                                    {
                                        timeInfo = _schoolInfo.LessonHours[i-1].Replace(" ", "").Trim();
                                    }
                                    
                                    h.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).AlignCenter().AlignMiddle().Column(col => 
                                    {
                                        col.Item().AlignCenter().Text($"{i}").Bold().FontSize(8);
                                        if(!string.IsNullOrEmpty(timeInfo)) 
                                            col.Item().AlignCenter().Text(timeInfo).FontSize(7);
                                    });
                                }
                            });
                            foreach(var t in teachers)
                            {
                                table.Cell().Border(0.5f).Padding(2).Text(FormatTeacherName(t.Name)).FontSize(8);
                                for(int h=1; h<=lessons; h++)
                                {
                                    var slot = new TimeSlot(day, h);
                                    string val = "";
                                    if(t.ScheduleInfo.ContainsKey(slot))
                                    {
                                        var raw = t.ScheduleInfo[slot];
                                        if(!string.IsNullOrEmpty(raw) && !raw.Contains("KAPALI")) val = raw.Split(' ')[0];
                                    }
                                    table.Cell().Border(0.5f).AlignCenter().Text(val).FontSize(7);
                                }
                            }
                        });

                        page.Footer().Element(f => f.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8));
                    });
                }
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }


        public byte[] GenerateDutySchedule(string documentNo, string reportDate)
        {
            var teacherRepo = new TeacherRepository();
            var teachers = teacherRepo.GetAll();
            string academicYear = GetAcademicYear();
            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            var locations = teachers
                .Where(t => !string.IsNullOrEmpty(t.DutyLocation))
                .Select(t => t.DutyLocation.Trim())
                .Distinct()
                .OrderBy(l => l)
                .ToList();

            string[] displayDays = { "PAZARTESİ", "SALI", "ÇARŞAMBA", "PERŞEMBE", "CUMA" };
            var dayMatchers = new Dictionary<string, string[]>
            {
                { "PAZARTESİ", new[] { "PAZARTESİ", "PAZARTESI", "PZT", "1" } },
                { "SALI", new[] { "SALI", "SAL", "2" } },
                { "ÇARŞAMBA", new[] { "ÇARŞAMBA", "CARSAMBA", "ÇAR", "CAR", "3" } },
                { "PERŞEMBE", new[] { "PERŞEMBE", "PERSEMBE", "PER", "4" } },
                { "CUMA", new[] { "CUMA", "CUM", "5" } }
            };

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Portrait());
                    page.Margin(1, Unit.Centimetre);
                    page.Header().Element(h => ComposeHeader(h, "NÖBET ÇİZELGESİ", ""));

                    page.Content().PaddingTop(10).Table(table => 
                    {
                        table.ColumnsDefinition(cols => 
                        {
                            cols.RelativeColumn(2); // Location
                            foreach(var d in displayDays) cols.RelativeColumn(3);
                        });

                        table.Header(h => 
                        {
                            h.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).Padding(3).Text("NÖBET YERİ").Bold().FontSize(9);
                            foreach(var d in displayDays)
                                h.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).Padding(3).AlignCenter().Text(d).Bold().FontSize(9);
                        });

                        foreach(var loc in locations)
                        {
                            table.Cell().Border(0.5f).Padding(3).Text(loc).Bold().FontSize(9);
                            foreach(var d in displayDays)
                            {
                                var matchKeys = dayMatchers[d];
                                var onDuty = teachers
                                    .Where(t => 
                                        !string.IsNullOrEmpty(t.DutyLocation) && t.DutyLocation.Trim() == loc &&
                                        !string.IsNullOrEmpty(t.DutyDay) && 
                                        matchKeys.Any(key => t.DutyDay.Trim().ToUpper(new System.Globalization.CultureInfo("tr-TR")).Contains(key))
                                    )
                                    .Select(t => t.Name)
                                    .ToList();
                                
                                table.Cell().Border(0.5f).Padding(3).AlignCenter().AlignMiddle().Text(string.Join("\n", onDuty)).FontSize(8);
                            }
                        }
                    });

                    page.Footer().Element(f => f.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8));
                });
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        public byte[] GenerateGuidanceReport()
        {
            var teacherRepo = new TeacherRepository();
            var classRepo = new ClassRepository();
            var teachers = teacherRepo.GetAll().OrderBy(t => t.Name).ToList();
            var classes = classRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);

            var list = teachers.Where(t => t.Guidance > 0).ToList();

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Portrait());
                    page.Margin(1, Unit.Centimetre);
                    page.Header().Element(h => ComposeHeader(h, "SINIF REHBER ÖĞRETMENLERİ LİSTESİ", ""));

                    page.Content().PaddingTop(10).Table(table => 
                    {
                        table.ColumnsDefinition(cols => 
                        {
                            cols.ConstantColumn(30);
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(2);
                        });

                        table.Header(h => 
                        {
                            h.Cell().BorderBottom(1).Padding(5).Text("No").Bold();
                            h.Cell().BorderBottom(1).Padding(5).Text("Öğretmen Adı Soyadı").Bold();
                            h.Cell().BorderBottom(1).Padding(5).Text("Rehber Olduğu Sınıf").Bold();
                        });

                        int i = 1;
                        foreach(var t in list)
                        {
                            table.Cell().Padding(5).Text($"{i++}");
                            table.Cell().Padding(5).Text(t.Name);
                            table.Cell().Padding(5).Text(classes.ContainsKey(t.Guidance) ? classes[t.Guidance] : "-");
                        }
                    });

                    page.Footer().Element(f => f.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8));
                });
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        public byte[] GenerateClubsReport()
        {
            var teacherRepo = new TeacherRepository();
            var teachers = teacherRepo.GetAll().Where(t => !string.IsNullOrEmpty(t.Club)).OrderBy(t => t.Club).ThenBy(t => t.Name).ToList();

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Portrait());
                    page.Margin(1, Unit.Centimetre);
                    page.Header().Element(h => ComposeHeader(h, "EĞİTİCİ KOL (KULÜP) LİSTESİ", ""));

                    page.Content().PaddingTop(10).Table(table => 
                    {
                        table.ColumnsDefinition(cols => 
                        {
                            cols.ConstantColumn(30);
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(3);
                        });

                        table.Header(h => 
                        {
                            h.Cell().BorderBottom(1).Padding(5).Text("No").Bold();
                            h.Cell().BorderBottom(1).Padding(5).Text("Kulüp Adı").Bold();
                            h.Cell().BorderBottom(1).Padding(5).Text("Sorumlu Öğretmen").Bold();
                        });

                        int i = 1;
                        foreach(var t in teachers)
                        {
                            table.Cell().Padding(5).Text($"{i++}");
                            table.Cell().Padding(5).Text(t.Club);
                            table.Cell().Padding(5).Text(t.Name);
                        }
                    });

                    page.Footer().Element(f => f.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8));
                });
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        public byte[] GenerateUnassignedReport()
        {
            var distRepo = new DistributionRepository();
            var classRepo = new ClassRepository();
            var lessonRepo = new LessonRepository();
            var allLessons = lessonRepo.GetAll();
            var unassigned = distRepo.GetAllBlocks().Where(b => b.Day == 0).OrderBy(b => b.ClassId).ToList();
            var classes = classRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Portrait());
                    page.Margin(1, Unit.Centimetre);
                    page.Header().Element(h => ComposeHeader(h, "YERLEŞMEYEN / BÖLÜNEN DERSLER LİSTESİ", ""));

                    page.Content().PaddingTop(10).Table(table => 
                    {
                        table.ColumnsDefinition(cols => 
                        {
                            cols.ConstantColumn(30);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                            cols.ConstantColumn(50);
                            cols.RelativeColumn(3);
                        });

                        table.Header(h => 
                        {
                            h.Cell().BorderBottom(1).Padding(5).Text("No").Bold();
                            h.Cell().BorderBottom(1).Padding(5).Text("Sınıf").Bold();
                            h.Cell().BorderBottom(1).Padding(5).Text("Ders Kodu").Bold();
                            h.Cell().BorderBottom(1).Padding(5).Text("Saat").Bold();
                            h.Cell().BorderBottom(1).Padding(5).Text("Ders Adı").Bold();
                        });

                        int i = 1;
                        foreach(var b in unassigned)
                        {
                            var lFull = allLessons.FirstOrDefault(l => l.Code == b.LessonCode)?.Name ?? "-";
                            table.Cell().Padding(5).Text($"{i++}");
                            table.Cell().Padding(5).Text(classes.ContainsKey(b.ClassId) ? classes[b.ClassId] : "-");
                            table.Cell().Padding(5).Text(b.LessonCode);
                            table.Cell().Padding(5).Text($"{b.BlockDuration}");
                            table.Cell().Padding(5).Text(lFull);
                        }
                    });

                    page.Footer().Element(f => f.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8));
                });
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        public byte[] GenerateRoomSchedule(string documentNo, string reportDate)
        {
            var distRepo = new DistributionRepository();
            var allBlocks = distRepo.GetAllBlocks().Where(b => b.GetOrtakMekanIds().Count > 0 && b.Day > 0).ToList();
            
            var roomRepo = new OrtakMekanRepository();
            var allRooms = roomRepo.GetAll();
            var rooms = allRooms.ToDictionary(r => r.Id, r => r.Name);
            
            // Collect used room IDs not in dictionary
            var usedIds = allBlocks.SelectMany(b => b.GetOrtakMekanIds()).Distinct();
            foreach(var id in usedIds)
            {
                if (!rooms.ContainsKey(id)) rooms[id] = $"Mekan {id}";
            }
            
            var teacherRepo = new TeacherRepository();
            var teachers = teacherRepo.GetAll().ToDictionary(t => t.Id, t => t.Name);
            
            var classRepo = new ClassRepository();
            var classes = classRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);

            var document = Document.Create(container =>
            {
                foreach (var roomKvp in rooms.OrderBy(r => r.Value))
                {
                    int roomId = roomKvp.Key;
                    string roomName = roomKvp.Value;
                    var roomBlocks = allBlocks.Where(b => b.GetOrtakMekanIds().Contains(roomId)).ToList();
                    
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(0.5f, Unit.Centimetre);
                        page.DefaultTextStyle(x => x.FontSize(9));
                        
                        page.Header().Element(h => ComposeHeader(h, $"ORTAK MEKAN PROGRAMI - {roomName.ToUpper()}", documentNo));

                        page.Content().PaddingTop(10).Element(c => 
                        {
                            ComposeRoomScheduleTable(c, roomId, roomBlocks, classes, teachers);
                        });
                        
                        page.Footer().Element(f => f.AlignRight().Text($"{DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(8));
                    });
                }
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        private void ComposeRoomScheduleTable(IContainer container, int roomId, List<DistributionBlock> blocks, Dictionary<int, string> classes, Dictionary<int, string> teachers)
        {
            string[] dayNames = { "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar" };
            int visibleDays = _schoolInfo.Days < 5 ? 5 : _schoolInfo.Days; 
            int maxLessons = GetDynamicMaxLessons();

            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(80); // Day
                    for(int i=0; i<maxLessons; i++) columns.RelativeColumn();
                });

                // Header
                table.Header(header =>
                {
                    header.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).AlignCenter().AlignMiddle().Text("GÜNLER").Bold();
                    for (int h = 1; h <= maxLessons; h++)
                        header.Cell().Border(0.5f).Background(Colors.Grey.Lighten4).AlignCenter().AlignMiddle().Text($"{h}").Bold();
                });

                // Rows
                for (int d = 1; d <= visibleDays; d++)
                {
                    table.Cell().Border(0.5f).Background(Colors.Grey.Lighten5).MinHeight(45).AlignCenter().AlignMiddle().Text(dayNames[d-1]).Bold();

                    for (int h = 1; h <= maxLessons; h++)
                    {
                        var block = blocks.FirstOrDefault(b => b.Day == d && h >= b.Hour && h < b.Hour + b.BlockDuration);
                        
                        table.Cell().Border(0.5f).AlignCenter().AlignMiddle().Column(col => 
                        {
                            if (block != null)
                            {
                                string cName = classes.ContainsKey(block.ClassId) ? classes[block.ClassId] : "-";
                                string tNames = string.Join(", ", block.TeacherIds.Select(tid => teachers.ContainsKey(tid) ? teachers[tid] : ""));
                                
                                col.Item().Text(cName).Bold().FontSize(10);
                                col.Item().Text(block.LessonCode).FontSize(9);
                                col.Item().Text(tNames).FontSize(7).FontColor(Colors.Grey.Darken2);
                            }
                        });
                    }
                }
            });
        }

        private string GetAcademicYear()
        {
             if (!string.IsNullOrEmpty(_schoolInfo.Date)) return _schoolInfo.Date;
             
             int month = DateTime.Now.Month;
             int year = DateTime.Now.Year;
             if (month >= 9) return $"{year}-{year+1}";
             return $"{year-1}-{year}";
        }


        private string FormatTeacherName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "";
            var parts = fullName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1) return fullName;

            var sb = new System.Text.StringBuilder();
            // All except last are initials
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Length > 0)
                    sb.Append(parts[i][0]).Append(". ");
            }
            // Last part full
            sb.Append(parts.Last());
            return sb.ToString();
        }
    }
}
