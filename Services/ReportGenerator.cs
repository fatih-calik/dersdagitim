using System.Linq;
using System.Text;
using System.Text.Json;
using DersDagitim.Models;
using DersDagitim.Persistence;

namespace DersDagitim.Services
{
    public class ReportGenerator
    {
        private readonly SchoolInfo _schoolInfo;
        
        public ReportGenerator(SchoolInfo schoolInfo)
        {
            _schoolInfo = schoolInfo ?? new SchoolInfo { Name = "Ders Dağıtım Sistemi" };
        }
        
        private string GetBaseHtml(string title, string content)
        {
            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta http-equiv='X-UA-Compatible' content='IE=edge'>
    <title>{title}</title>
    <style>
        @page {{ size: A4 portrait; margin: 1cm; }}
        @page landscape-page {{ size: A4 landscape; margin: 1cm; }}
        
        body {{ 
            font-family: 'SF UI Display', 'SF Pro Display', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; 
            margin: 0; 
            padding: 0; 
            color: #000; 
            line-height: 1.2;
            font-size: 14px;
        }}
        
        .report-wrapper {{ padding: 0; }}
        
        .page-container {{
            width: 210mm;
            min-height: 297mm;
            padding: 10mm;
            margin: 0 auto;
            background: white;
            box-sizing: border-box;
            page-break-after: always;
        }}
        
        .header-section {{ text-align: center; margin-bottom: 25px; }}
        .header-tc {{ font-size: 18px; font-weight: bold; margin-bottom: 2px; }}
        .school-name {{ font-size: 22px; font-weight: bold; margin-bottom: 4px; text-transform: uppercase; }}
        .academic-year {{ font-size: 20px; font-weight: bold; text-transform: uppercase; margin-bottom: 15px; }}
        
        table {{ border-collapse: collapse; width: 100%; border: 2px solid #000; table-layout: fixed; }}
        th, td {{ border: 1px solid #000; padding: 4px; text-align: center; vertical-align: middle; word-wrap: break-word; }}
        th {{ background-color: #fff; font-weight: bold; font-size: 14px; }}
        
        /* Timetable Specific */
        .grid-header {{ height: 65px; vertical-align: top; }}
        .grid-header th {{ vertical-align: top; padding-top: 5px; }}
        .day-row {{ height: 55px; }}
        .hour-col {{ font-weight: bold; width: 100px; text-align: left; padding-left: 10px; font-size: 16px; }}
        .time-range {{ font-size: 12px; display: block; font-weight: normal; margin-top: 3px; line-height: 1.1; }}
        
        .page-break {{ page-break-after: always; display: block; clear: both; height: 1px; }}
        
        .cell-content {{ width: 100%; text-align: center; }}
        .cell-top {{ display: block; font-weight: bold; font-size: 13px; line-height: 1.2; margin-bottom: 2px; }}
        .cell-bottom {{ display: block; font-size: 11px; line-height: 1.1; font-weight: normal; }}
        
        /* Compact formatting for Master/Chart reports */
        .master-cell {{ font-size: 9px; line-height: 1; }}
        .master-top {{ display: block; font-weight: bold; margin-bottom: 1px; }}
        .master-bottom {{ display: block; font-weight: normal; }}
        
        .info-table {{ width: 100%; margin-bottom: 5px; font-weight: normal; border: none !important; }}
        .info-table td {{ border: none !important; text-align: left; padding: 1px 0; font-size: 16px; height: auto; vertical-align: bottom; }}
        .info-sayi {{ font-size: 16px; font-weight: bold; margin-bottom: 5px; }}
        .info-name {{ font-size: 24px !important; font-weight: bold !important; width: 40%; white-space: nowrap; }}
        
        .footer-section {{ margin-top: 20px; position: relative; width: 100%; }}
        .footer-text {{ font-size: 18px; margin-bottom: 10px; float: left; width: 65%; line-height: 1.3; }}
        .signature-box {{ float: right; text-align: center; min-width: 250px; padding-top: 5px; }}
        .signature-title {{ font-weight: bold; margin-bottom: 2px; font-size: 18px; }}
        .signature-name {{ font-weight: bold; font-size: 18px; }}
        
        .landscape {{ page: landscape-page; }}
        
        @media print {{
            .no-print {{ display: none; }}
            body {{ padding: 0; }}
        }}
    </style>
</head>
<body>
    <div class='report-wrapper'>
        {content}
    </div>
</body>
</html>";
        }

        private string GetAcademicYear()
        {
            if (!string.IsNullOrEmpty(_schoolInfo.Date))
            {
                if (_schoolInfo.Date.Contains("/"))
                {
                    var parts = _schoolInfo.Date.Split('/');
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int year))
                    {
                        if (int.TryParse(parts[1], out int month))
                        {
                            return month >= 9 ? $"{year}-{year + 1}" : $"{year - 1}-{year}";
                        }
                    }
                }
                else if (_schoolInfo.Date.Contains("-") && _schoolInfo.Date.Length >= 9)
                {
                    return _schoolInfo.Date;
                }
            }
            return DateTime.Now.Month >= 9 ? $"{DateTime.Now.Year}-{DateTime.Now.Year+1}" : $"{DateTime.Now.Year-1}-{DateTime.Now.Year}";
        }

        public string GenerateTeacherScheduleReport(string documentNo = "", string reportDate = "")
        {
            var repo = new TeacherRepository();
            var teachers = repo.GetAll();
            
            var classRepo = new ClassRepository();
            var classes = classRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);
            
            var sb = new StringBuilder();
            
            string[] dayNames = { "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar" };
            int visibleDays = _schoolInfo.Days < 5 ? 5 : _schoolInfo.Days; // Minimum 5 days
            int dailyLessons = GetMaxVisibleLessonCount();

            string academicYear = GetAcademicYear();
            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd.MM.yyyy");

            foreach (var teacher in teachers)
            {
                sb.Append("<div class='page-container' style='page-break-after: always; padding: 20px;'>");
                
                // --- TOP HEADER SECTION ---
                sb.Append("<div style='text-align: center; border-bottom: 2px solid #000; padding-bottom: 5px; margin-bottom: 10px;'>");
                sb.Append("<div style='font-size: 14px; font-weight: bold;'>T.C.</div>");
                sb.Append($"<div style='font-size: 14px; font-weight: bold;'>{_schoolInfo.Name.ToUpper()}</div>");
                sb.Append($"<div style='font-size: 14px; font-weight: bold;'>{academicYear} EĞİTİM ÖĞRETİM YILI DERS PROGRAMI</div>");
                sb.Append("</div>");
                
                // --- INFO SECTION (Clean Table Layout) ---
                sb.Append("<table style='width: 100%; margin-bottom: 15px; border-collapse: collapse; border: none;'>");
                
                // Row 1: Document No & Date
                if (!string.IsNullOrEmpty(documentNo))
                {
                    sb.Append("<tr>");
                    sb.Append($"<td style='border: none; font-size: 12px; width: 50%;'>Sayı: {documentNo}</td>");
                    sb.Append($"<td style='border: none; text-align: right; font-size: 12px; width: 50%;'>Tarih: {pDate}</td>");
                    sb.Append("</tr>");
                }
                
                // Row 2: Teacher Name (Large)
                sb.Append("<tr>");
                sb.Append($"<td colspan='2' style='border: none; padding-top: 5px; font-size: 20px; font-weight: bold;'>Adı Soyadı: {teacher.Name}</td>");
                sb.Append("</tr>");
                
                // Row 3: Details (Guidance, Club, Duty) - Using nested table for alignment if needed, or just lines
                string guidanceClass = teacher.Guidance > 0 && classes.ContainsKey(teacher.Guidance) ? classes[teacher.Guidance] : "-";
                string club = teacher.Club ?? "-";
                string dutyInfo = string.IsNullOrEmpty(teacher.DutyDay) ? "-" : $"{teacher.DutyDay} / {teacher.DutyLocation}";

                sb.Append("<tr><td colspan='2' style='border:none;'>");
                sb.Append("<table style='width:100%; border:none;'><tr>");
                sb.Append($"<td style='border:none; width: 33%; font-size: 13px;'><b>Sınıf Öğretmenliği:</b> {guidanceClass}</td>");
                sb.Append($"<td style='border:none; width: 33%; font-size: 13px;'><b>Eğitici Kol:</b> {club}</td>");
                sb.Append($"<td style='border:none; width: 33%; font-size: 13px; text-align:right;'><b>Nöbet:</b> {dutyInfo}</td>");
                sb.Append("</tr></table>");
                sb.Append("</td></tr>");
                
                sb.Append("</table>");
                
                // --- SCHEDULE TABLE ---
                sb.Append("<table style='width: 100%; border-collapse: collapse; border: 1px solid #000; table-layout: fixed;'>");
                
                // Table Head
                sb.Append("<thead>");
                sb.Append("<tr style='background-color: #f0f0f0;'>");
                sb.Append("<th style='border: 1px solid #000; width: 100px; padding: 5px;'>Günler</th>"); // Wider column for Days
                for (int h = 1; h <= dailyLessons; h++)
                {
                    string timeRange = _schoolInfo.LessonHours != null && h <= _schoolInfo.LessonHours.Length && !string.IsNullOrEmpty(_schoolInfo.LessonHours[h-1]) 
                         ? $"<br><span style='font-size:10px; font-weight:normal;'>{_schoolInfo.LessonHours[h-1]}</span>" : "";
                    sb.Append($"<th style='border: 1px solid #000; padding: 2px;'>{h}.Ders{timeRange}</th>");
                }
                sb.Append("</tr>");
                sb.Append("</thead>");
                
                // Table Body
                sb.Append("<tbody>");
                for (int d = 1; d <= visibleDays; d++)
                {
                    sb.Append("<tr>");
                    sb.Append($"<td style='border: 1px solid #000; font-weight: bold; text-align: center; background-color: #fafafa;'>{dayNames[d-1]}</td>");
                    
                    for (int h = 1; h <= dailyLessons; h++)
                    {
                        var slot = new TimeSlot(d, h);
                        string content = "&nbsp;"; 
                        
                        if (teacher.ScheduleInfo.ContainsKey(slot))
                        {
                            var raw = teacher.ScheduleInfo[slot];
                            if (!string.IsNullOrEmpty(raw) && !raw.Contains("KAPALI", StringComparison.OrdinalIgnoreCase))
                            {
                                // Parse: ClassName LessonCode (e.g., "12-A MAT")
                                // Sometimes raw is "12-A MAT" or just "MAT"
                                var parts = raw.Split(new[] { "    ", "\t", "   ", "  ", " " }, StringSplitOptions.RemoveEmptyEntries);
                                
                                string line1 = "";
                                string line2 = "";
                                
                                if (parts.Length >= 2)
                                {
                                    line1 = parts[0]; // Class (12-A)
                                    line2 = string.Join(" ", parts.Skip(1)); // Lesson (MAT)
                                }
                                else
                                {
                                    line1 = raw;
                                }

                                content = $"<div style='display:flex; flex-direction:column; align-items:center; justify-content:center; height:100%; line-height:1.2;'>" +
                                          $"<span style='font-weight:bold; font-size:12px;'>{line1}</span>" +
                                          $"<span style='font-size:11px;'>{line2}</span>" +
                                          $"</div>";
                            }
                        }
                        
                        sb.Append($"<td style='border: 1px solid #000; height: 50px; vertical-align: middle; text-align: center; padding: 2px;'>{content}</td>");
                    }
                    sb.Append("</tr>");
                }
                sb.Append("</tbody>");
                sb.Append("</table>");
                
                // --- FOOTER SECTION ---
                sb.Append("<div style='margin-top: 20px;'>");
                sb.Append($"<div style='font-size: 13px; margin-bottom: 20px;'>Yukarıdaki dersler {pDate} tarihinde şahsınıza verilmiştir. Bilgilerinizi rica ederim.</div>");
                
                sb.Append("<table style='width: 100%; border: none;'><tr>");
                sb.Append("<td style='border: none; text-align: left; vertical-align: top; width: 50%;'>");
                sb.Append("Aslını aldım.<br><br><br><b>Imza</b>");
                sb.Append("</td>");
                sb.Append("<td style='border: none; text-align: right; vertical-align: top; width: 50%;'>");
                sb.Append($"<div style='margin-bottom:5px;'>Okul Müdürü</div><div style='font-weight:bold; font-size:14px; margin-top:30px;'>{_schoolInfo.Principal}</div>");
                sb.Append("</td>");
                sb.Append("</tr></table>");
                sb.Append("</div>"); // End Footer
                
                sb.Append("</div>"); // End Page
            }
            
            return GetBaseHtml("Öğretmen El Programı", sb.ToString());
        }

        public string GenerateClassScheduleReport(string reportDate = "")
        {
            var repo = new ClassRepository();
            var classes = repo.GetAll();
            var sb = new StringBuilder();
            
            string[] dayNames = { "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar" };
            int visibleDays = _schoolInfo.Days;
            int dailyLessons = GetMaxVisibleLessonCount();

            string academicYear = GetAcademicYear();
            if (string.IsNullOrEmpty(academicYear)) academicYear = DateTime.Now.Month >= 9 ? $"{DateTime.Now.Year}-{DateTime.Now.Year+1}" : $"{DateTime.Now.Year-1}-{DateTime.Now.Year}";

            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd.MM.yyyy");

            foreach (var cls in classes)
            {
                sb.Append("<div class='page-break'>");
                
                // Header explicitly matching the request
                sb.Append($"<div style='font-size: 24px; font-weight: bold; margin-bottom: 10px; text-align: left;'>Sınıf: {cls.Name}</div>");
                
                // Table
                sb.Append("<table style='border-width: 1.5px;'>");
                sb.Append("<thead><tr class='grid-header'>");
                sb.Append("<th style='width: 70px;'>Günler</th>");
                for (int h = 1; h <= 12; h++)
                {
                    string timeRange = _schoolInfo.LessonHours != null && h <= _schoolInfo.LessonHours.Length && !string.IsNullOrEmpty(_schoolInfo.LessonHours[h-1]) 
                         ? $"<span class='time-range'>{_schoolInfo.LessonHours[h-1]}</span>" : "";
                    sb.Append($"<th style='width: 60px;'>{h}{timeRange}</th>");
                }
                sb.Append("</tr></thead>");
                
                sb.Append("<tbody>");
                for (int d = 1; d <= visibleDays; d++)
                {
                    sb.Append($"<tr class='day-row' style='height: 75px;'>"); // Increased height
                    sb.Append($"<td class='hour-col'>{dayNames[d-1]}</td>");
                    
                    for (int h = 1; h <= 12; h++)
                    {
                        var key = $"d_{d}_{h}";
                        string content = "";
                        
                        if (cls.Schedule.ContainsKey(key))
                        {
                            var raw = cls.Schedule[key];
                            if (!string.IsNullOrEmpty(raw))
                            {
                                if (raw.IndexOf("KAPALI", StringComparison.OrdinalIgnoreCase) >= 0 || raw.IndexOf("Kapalı", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    content = "";
                                }
                                else
                                {
                                    var parts = raw.Split(new[] { " - ", "    ", "\t", "   ", "  " }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 2)
                                    {
                                        string top = parts[0];
                                        string rawTeacher = string.Join(" ", parts.Skip(1));
                                        
                                        // Handle multiple teachers separated by comma
                                        var teacherList = rawTeacher.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                        var formattedTeachers = teacherList.Select(t => FormatTeacherName(t.Trim()));
                                        string bottom = string.Join("<br>", formattedTeachers);

                                        // Decreased font size for teacher (cell-bottom)
                                        content = $"<div class='cell-content'><span class='cell-top'>{top}</span><span class='cell-bottom' style='font-size: 9px;'>{bottom}</span></div>";
                                    }
                                    else
                                    {
                                        content = raw;
                                    }
                                }
                            }
                        }
                        
                        sb.Append($"<td>{content}</td>");
                    }
                    sb.Append("</tr>");
                }
                // Empty days
                for (int d = visibleDays + 1; d <= 5; d++)
                {
                    sb.Append($"<tr class='day-row'>");
                    sb.Append($"<td class='hour-col'>{dayNames[d-1]}</td>");
                    for (int h = 1; h <= 12; h++) sb.Append("<td></td>");
                    sb.Append("</tr>");
                }
                sb.Append("</tbody></table>");
                
                // Footer
                sb.Append("<div class='footer-section'>");
                sb.Append("<div class='signature-box'>");
                sb.Append("<div class='signature-title'>Okul Müdürü</div>");
                sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
                sb.Append("</div>");
                sb.Append("<div style='clear: both;'></div>");
                sb.Append("</div>");
                
                sb.Append("</div>"); // End Page
            }
            
            return GetBaseHtml("Sınıf Ders Programı", sb.ToString());
        }

        public string GenerateTeacherDailyScheduleReport(string reportDate = "")
        {
            var teacherRepo = new TeacherRepository();
            var teachers = teacherRepo.GetAll().OrderBy(t => t.Name).ToList();
            var sb = new StringBuilder();
            
            string[] dayNames = { "PAZARTESİ", "SALI", "ÇARŞAMBA", "PERŞEMBE", "CUMA", "CUMARTESİ", "PAZAR" };
            int visibleDays = _schoolInfo.Days;
            int dailyLessons = GetMaxVisibleLessonCount();

            string academicYear = GetAcademicYear();

            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            for (int d = 1; d <= visibleDays; d++)
            {
                sb.Append("<div class='page-container landscape'>");
                
                // Top Header
                sb.Append("<div class='header-section'>");
                sb.Append("<div class='header-tc'>T.C.</div>");
                sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
                sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI GÜNLÜK ÖĞRETMEN DERS PROGRAMI</div>");
                sb.Append($"<div style='font-size: 22px; font-weight: bold; margin-top: 10px;'>GÜN: {dayNames[d-1]}</div>");
                sb.Append($"<div style='text-align: right; font-size: 14px;'>Tarih: {pDate}</div>");
                sb.Append("</div>");
                
                // Table
                sb.Append("<table style='table-layout: fixed; width: 100%; border: 2px solid #000;'>");
                
                // Header Row
                sb.Append("<thead><tr class='grid-header'>");
                sb.Append("<th style='width: 180px;'>Öğretmen</th>");
                for (int h = 1; h <= 12; h++)
                {
                    string timeStr = "";
                    if (_schoolInfo.LessonHours != null && h <= 12 && !string.IsNullOrEmpty(_schoolInfo.LessonHours[h-1]))
                    {
                        var rawValue = _schoolInfo.LessonHours[h-1];
                        if (rawValue.Contains("-"))
                        {
                            var parts = rawValue.Split('-');
                            timeStr = $"{parts[0].Trim()}<br>{parts[1].Trim()}";
                        }
                        else { timeStr = rawValue; }
                    }
                    string timeRange = !string.IsNullOrEmpty(timeStr) ? $"<span class='time-range'>{timeStr}</span>" : "";
                    sb.Append($"<th style='width: 70px; vertical-align: top; padding-top: 5px; font-size: 16px;'>{h}{timeRange}</th>");
                }
                sb.Append("</tr></thead>");
                
                sb.Append("<tbody>");
                foreach (var teacher in teachers)
                {
                    sb.Append("<tr style='height: 45px;'>");
                    sb.Append($"<td style='text-align: left; padding-left: 10px; font-weight: bold; font-size: 15px;'>{teacher.Name}</td>");
                    
                    for (int h = 1; h <= 12; h++)
                    {
                        var slot = new TimeSlot(d, h);
                        string content = "";
                        
                        if (teacher.ScheduleInfo.ContainsKey(slot))
                        {
                            var raw = teacher.ScheduleInfo[slot];
                            if (!string.IsNullOrEmpty(raw))
                            {
                                if (raw.IndexOf("KAPALI", StringComparison.OrdinalIgnoreCase) >= 0 || raw.IndexOf("Kapalı", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    content = "";
                                }
                                else
                                {
                                    var parts = raw.Split(new[] { "    ", "\t", "   ", "  ", " " }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 2)
                                    {
                                        string top = parts[0];
                                        string bottom = string.Join(" ", parts.Skip(1));
                                        content = $"<div class='cell-content'><span class='cell-top'>{top}</span><span class='cell-bottom'>{bottom}</span></div>";
                                    }
                                    else
                                    {
                                        content = raw;
                                    }
                                }
                            }
                        }
                        sb.Append($"<td>{content}</td>");
                    }
                    sb.Append("</tr>");
                }
                sb.Append("</tbody></table>");
                
                // Footer
                sb.Append("<div class='footer-section'>");
                sb.Append("<div class='signature-box'>");
                sb.Append("<div class='signature-title'>Okul Müdürü</div>");
                sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
                sb.Append("</div>");
                sb.Append("<div style='clear: both;'></div>");
                sb.Append("</div>");
                
                sb.Append("</div>"); // End Day Page
            }
            
            return GetBaseHtml("Günlük Öğretmen Programı", sb.ToString());
        }

        public string GenerateTeacherMasterScheduleReport(string reportDate = "")
        {
            var repo = new TeacherRepository();
            var teachers = repo.GetAll();
            var sb = new StringBuilder();
            
            string[] dayNames = { "PAZARTESİ", "SALI", "ÇARŞAMBA", "PERŞEMBE", "CUMA", "CUMARTESİ", "PAZAR" };
            int visibleDays = _schoolInfo.Days;
            int dailyLessons = GetMaxVisibleLessonCount();

            string academicYear = GetAcademicYear();

            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            sb.Append("<div class='landscape'>");
            sb.Append("<div class='header-section'>");
            sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
            sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI ÖĞRETMEN ÇARŞAF DERS PROGRAMI</div>");
            sb.Append($"<div style='text-align: right; font-size: 12px;'>Tarih: {pDate}</div>");
            sb.Append("</div>");
            
            sb.Append("<table style='font-size: 9px; border-width: 1.5px;'>");
            
            // Header Row (Days & Hours)
            sb.Append("<thead>");
            sb.Append("<tr class='grid-header'><th rowspan='2' style='width:120px;'>ÖĞRETMEN</th>");
            
            for (int d = 1; d <= visibleDays; d++)
            {
                sb.Append($"<th colspan='{dailyLessons}'>{dayNames[d-1]}</th>");
            }
            sb.Append("</tr>");
            
            sb.Append("<tr class='grid-header'>");
            for (int d = 1; d <= visibleDays; d++)
            {
                for (int h = 1; h <= dailyLessons; h++)
                {
                    sb.Append($"<th style='width: 25px;'>{h}</th>");
                }
            }
            sb.Append("</tr>");
            sb.Append("</thead>");
            
            sb.Append("<tbody>");
            foreach (var teacher in teachers)
            {
                sb.Append("<tr style='height: 25px;'>");
                sb.Append($"<td style='text-align:left; font-weight:bold; padding-left:5px;'>{teacher.Name}</td>");
                
                for (int d = 1; d <= visibleDays; d++)
                {
                    for (int h = 1; h <= dailyLessons; h++)
                    {
                        var slot = new TimeSlot(d, h);
                        string content = "";
                        string style = "";
                        
                        if (teacher.ScheduleInfo.ContainsKey(slot))
                        {
                            var raw = teacher.ScheduleInfo[slot];
                            if (!string.IsNullOrEmpty(raw))
                            {
                                if (raw.IndexOf("KAPALI", StringComparison.OrdinalIgnoreCase) >= 0 || raw.IndexOf("Kapalı", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    content = "";
                                }
                                else
                                {
                                    var parts = raw.Split(new[] { "    ", "\t", "   ", "  ", " " }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 2)
                                    {
                                        string top = parts[0];
                                        string bottom = string.Join(" ", parts.Skip(1));
                                        content = $"<div class='master-cell'><span class='master-top'>{top}</span><span class='master-bottom'>{bottom}</span></div>";
                                    }
                                }
                            }
                        }
                        else if (teacher.Constraints.ContainsKey(slot) && teacher.Constraints[slot] == SlotState.Closed)
                        {
                            // Keep empty and white, no special styling for 'Closed'
                        }
                        
                        sb.Append($"<td style='{style}'>{content}</td>");
                    }
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
            
            // Footer
            sb.Append("<div class='footer-section'>");
            sb.Append("<div class='signature-box'>");
            sb.Append("<div class='signature-title'>Okul Müdürü</div>");
            sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
            sb.Append("</div>");
            sb.Append("</div>");
            sb.Append("</div>");
            
            return GetBaseHtml("Öğretmen Çarşaf Programı", sb.ToString());
        }

        public string GenerateClassMasterScheduleReport(string reportDate = "")
        {
            var repo = new ClassRepository();
            var classes = repo.GetAll();
            var sb = new StringBuilder();
            
            string[] dayNames = { "PAZARTESİ", "SALI", "ÇARŞAMBA", "PERŞEMBE", "CUMA", "CUMARTESİ", "PAZAR" };
            int visibleDays = _schoolInfo.Days;
            int dailyLessons = GetMaxVisibleLessonCount();

            string academicYear = GetAcademicYear();

            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            sb.Append("<div class='landscape'>");
            sb.Append("<div class='header-section'>");
            sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
            sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI SINIF ÇARŞAF DERS PROGRAMI</div>");
            sb.Append($"<div style='text-align: right; font-size: 12px;'>Tarih: {pDate}</div>");
            sb.Append("</div>");
            
            sb.Append("<table style='font-size: 9px; border-width: 1.5px;'>");
            
            // Header Row (Days & Hours)
            sb.Append("<thead>");
            sb.Append("<tr class='grid-header'><th rowspan='2' style='width:120px;'>SINIF</th>");
            
            for (int d = 1; d <= visibleDays; d++)
            {
                sb.Append($"<th colspan='{dailyLessons}'>{dayNames[d-1]}</th>");
            }
            sb.Append("</tr>");
            
            sb.Append("<tr class='grid-header'>");
            for (int d = 1; d <= visibleDays; d++)
            {
                for (int h = 1; h <= dailyLessons; h++)
                {
                    sb.Append($"<th style='width: 25px;'>{h}</th>");
                }
            }
            sb.Append("</tr>");
            sb.Append("</thead>");
            
            sb.Append("<tbody>");
            foreach (var cls in classes)
            {
                sb.Append("<tr style='height: 25px;'>");
                sb.Append($"<td style='text-align:left; font-weight:bold; padding-left:5px;'>{cls.Name}</td>");
                
                for (int d = 1; d <= visibleDays; d++)
                {
                    for (int h = 1; h <= dailyLessons; h++)
                    {
                        var key = $"d_{d}_{h}";
                        string content = "";
                        string style = "";
                        
                        if (cls.Schedule.ContainsKey(key))
                        {
                            var raw = cls.Schedule[key];
                            if (!string.IsNullOrEmpty(raw))
                            {
                                if (raw.IndexOf("KAPALI", StringComparison.OrdinalIgnoreCase) >= 0 || raw.IndexOf("Kapalı", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    content = "";
                                }
                                else
                                {
                                    var parts = raw.Split(new[] { " - ", "    ", "\t", "   ", "  " }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 2)
                                    {
                                        string top = parts[0];
                                        string bottom = string.Join(" ", parts.Skip(1));
                                        content = $"<div class='master-cell'><span class='master-top'>{top}</span><span class='master-bottom'>{bottom}</span></div>";
                                    }
                                }
                            }
                        }
                        
                        sb.Append($"<td style='{style}'>{content}</td>");
                    }
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
            
            // Footer
            sb.Append("<div class='footer-section'>");
            sb.Append("<div class='signature-box'>");
            sb.Append("<div class='signature-title'>Okul Müdürü</div>");
            sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
            sb.Append("</div>");
            sb.Append("</div>");
            sb.Append("</div>");
            
            return GetBaseHtml("Sınıf Çarşaf Programı", sb.ToString());
        }

        public string GenerateDutyScheduleReport(string reportDate = "")
        {
            var repo = new TeacherRepository();
            var teachers = repo.GetAll();
            var sb = new StringBuilder();
            
            // Collect unique locations
            var locations = teachers
                .Where(t => !string.IsNullOrEmpty(t.DutyLocation))
                .Select(t => t.DutyLocation.Trim())
                .Distinct()
                .OrderBy(l => l)
                .ToList();
                
            if (locations.Count == 0)
            {
                return GetBaseHtml("Nöbet Çizelgesi", "<h3>Kayıtlı nöbet yeri bulunamadı.</h3>");
            }

            // Display names for columns
            string[] displayDays = { "PAZARTESİ", "SALI", "ÇARŞAMBA", "PERŞEMBE", "CUMA" };
    
            // Helpers for robust matching (Full, Short, Index)
            var dayMatchers = new Dictionary<string, string[]>
            {
                { "PAZARTESİ", new[] { "PAZARTESİ", "PAZARTESI", "PZT", "1" } },
                { "SALI", new[] { "SALI", "SAL", "2" } },
                { "ÇARŞAMBA", new[] { "ÇARŞAMBA", "CARSAMBA", "ÇAR", "CAR", "3" } },
                { "PERŞEMBE", new[] { "PERŞEMBE", "PERSEMBE", "PER", "4" } },
                { "CUMA", new[] { "CUMA", "CUM", "5" } }
            };
            
            string academicYear = GetAcademicYear();
            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            sb.Append("<div class='header-section'>");
            sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
            sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI NÖBET ÇİZELGESİ</div>");
            sb.Append($"<div style='text-align: right; font-size: 12px;'>Tarih: {pDate}</div>");
            sb.Append("</div>");
            
            sb.Append("<table style='table-layout: auto;'>");
            
            // Header
            sb.Append("<thead><tr class='grid-header'>");
            sb.Append("<th style='width:180px;'>NÖBET YERİ</th>");
            foreach(var d in displayDays) sb.Append($"<th>{d}</th>");
            sb.Append("</tr></thead>");
            
            sb.Append("<tbody>");
            foreach(var loc in locations)
            {
                sb.Append("<tr style='height: 80px;'>");
                sb.Append($"<td style='font-weight: bold; background-color: #fcfcfc;'>{loc}</td>");
                
                foreach(var d in displayDays)
                {
                    var matchKeys = dayMatchers[d];
                    
                    // Find teachers for this location and day (checking all variants)
                    var onDuty = teachers
                        .Where(t => 
                            !string.IsNullOrEmpty(t.DutyLocation) && t.DutyLocation.Trim() == loc &&
                            !string.IsNullOrEmpty(t.DutyDay) && 
                            matchKeys.Any(key => t.DutyDay.Trim().ToUpper(new System.Globalization.CultureInfo("tr-TR")).Contains(key))
                        )
                        .Select(t => t.Name)
                        .ToList();
                        
                    string cellContent = onDuty.Count > 0 ? string.Join("<br>", onDuty) : "";
                    sb.Append($"<td style='vertical-align: middle; font-size: 11px;'>{cellContent}</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
            
            // Footer
            sb.Append("<div class='footer-section'>");
            sb.Append("<div class='signature-box'>");
            sb.Append("<div class='signature-title'>Okul Müdürü</div>");
            sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
            sb.Append("</div>");
            sb.Append("</div>");
            
            return GetBaseHtml("Nöbet Çizelgesi", sb.ToString());
        }

        public string GenerateGuidanceReport(string reportDate = "")
        {
            var teacherRepo = new TeacherRepository();
            var teachers = teacherRepo.GetAll();
            
            var classRepo = new ClassRepository();
            var classes = classRepo.GetAll().ToDictionary(c => c.Id, c => c.Name);
            
            var sb = new StringBuilder();
            
            var filtered = teachers.Where(t => t.Guidance > 0).OrderBy(t => t.Name).ToList();
             
            if (filtered.Count == 0) return GetBaseHtml("Sınıf Rehber Öğretmenliği", "<h3>Kayıt bulunamadı.</h3>");

            string academicYear = GetAcademicYear();

            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            sb.Append("<div class='header-section'>");
            sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
            sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI SINIF REHBER ÖĞRETMENLERİ LİSTESİ</div>");
            sb.Append($"<div style='text-align: right; font-size: 12px;'>Tarih: {pDate}</div>");
            sb.Append("</div>");
            
            sb.Append("<table>");
            sb.Append("<thead><tr class='grid-header'><th style='width:50px;'>Sıra</th><th>Öğretmen Adı Soyadı</th><th>Görevi</th><th>Sınıfı</th></tr></thead>");
            sb.Append("<tbody>");
            
            int i = 1;
            foreach(var t in filtered)
            {
                string className = classes.ContainsKey(t.Guidance) ? classes[t.Guidance] : "-";
                sb.Append("<tr>");
                sb.Append($"<td>{i++}</td>");
                sb.Append($"<td style='text-align: left; padding-left: 10px;'>{t.Name}</td>");
                sb.Append($"<td style='text-align: left; padding-left: 10px;'>{t.Position}</td>");
                sb.Append($"<td style='font-weight:bold;'>{className}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
            
            // Footer
            sb.Append("<div class='footer-section'>");
            sb.Append("<div class='signature-box'>");
            sb.Append("<div class='signature-title'>Okul Müdürü</div>");
            sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
            sb.Append("</div>");
            sb.Append("</div>");
            
            return GetBaseHtml("Sınıf Rehber Öğretmenliği", sb.ToString());
        }

        public string GenerateClubsReport(string reportDate = "")
        {
            var repo = new TeacherRepository();
            var teachers = repo.GetAll();
            var sb = new StringBuilder();
            
            var clubs = teachers
                .Where(t => !string.IsNullOrEmpty(t.Club))
                .GroupBy(t => t.Club.Trim())
                .Select(g => new { ClubName = g.Key, Teachers = g.Select(t => t.Name).ToList() })
                .OrderBy(c => c.ClubName)
                .ToList();
             
            if (clubs.Count == 0) return GetBaseHtml("Eğitsel Kulüpler", "<h3>Kayıt bulunamadı.</h3>");

            string academicYear = GetAcademicYear();

            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            sb.Append("<div class='header-section'>");
            sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
            sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI EĞİTSEL KULÜPLER LİSTESİ</div>");
            sb.Append($"<div style='text-align: right; font-size: 12px;'>Tarih: {pDate}</div>");
            sb.Append("</div>");
            
            sb.Append("<table>");
            sb.Append("<thead><tr class='grid-header'><th style='width:60px;'>Sıra</th><th>Kulüp Adı</th><th>Görevli Öğretmenler</th></tr></thead>");
            sb.Append("<tbody>");
            
            int i = 1;
            foreach(var c in clubs)
            {
                sb.Append("<tr>");
                sb.Append($"<td>{i++}</td>");
                sb.Append($"<td style='font-size: 14px;'>{c.ClubName}</td>");
                sb.Append($"<td style='font-size: 14px;'>{string.Join("<br>", c.Teachers)}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
            
            // Footer
            sb.Append("<div class='footer-section'>");
            sb.Append("<div class='signature-box'>");
            sb.Append("<div class='signature-title'>Okul Müdürü</div>");
            sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
            sb.Append("</div>");
            sb.Append("</div>");
            
            return GetBaseHtml("Eğitsel Kulüpler", sb.ToString());
        }

        public string GenerateAssignmentsReport(string reportDate = "")
        {
            var db = DatabaseManager.Shared;
            var sql = @"
                SELECT 
                    s.ad as ClassName, 
                    d.ad as LessonName, 
                    o.ad_soyad as TeacherName, 
                    a.atanan_saat as Hours 
                FROM atama a 
                JOIN sinif_ders sd ON a.sinif_ders_id = sd.id
                JOIN sinif s ON sd.sinif_id = s.id 
                JOIN ders d ON sd.ders_id = d.id 
                JOIN ogretmen o ON a.ogretmen_id = o.id 
                WHERE a.ogretmen_id IS NOT NULL AND a.ogretmen_id > 0
                ORDER BY s.ad, o.ad_soyad, d.ad";
                
            var results = db.Query(sql);
            var sb = new StringBuilder();
            
            if (results.Count == 0) return GetBaseHtml("Ders Atama Listesi", "<h3>Atama kaydı bulunamadı.</h3>");

            string academicYear = GetAcademicYear();
            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            // Styles
            // We use a column layout (masonry style) for the two columns
            string containerStyle = "column-count: 2; column-gap: 1cm; width: 100%;";
            
            // Each group (Class or Teacher) is a card that avoids breaking inside
            string cardStyle = "break-inside: avoid; page-break-inside: avoid; margin-bottom: 15px; border: 1px solid #000; box-sizing: border-box;";
            
            // Header within card
            string cardHeaderStyle = "background-color: #f0f0f0; padding: 5px; font-weight: bold; text-align: center; border-bottom: 1px solid #000; font-size: 13px;";
            
            string tableStyle = "width: 100%; border-collapse: collapse; font-size: 11px;";
            string tdStyle = "border: 1px solid #ccc; padding: 3px; text-align: left;";
            string centerTdStyle = "border: 1px solid #ccc; padding: 3px; text-align: center; width: 40px;";

            var assignments = new List<(string ClassName, string TeacherName, string LessonName, int Hours)>();
            foreach(var row in results)
            {
                assignments.Add((
                    DatabaseManager.GetString(row, "ClassName"),
                    DatabaseManager.GetString(row, "TeacherName"),
                    DatabaseManager.GetString(row, "LessonName"),
                    DatabaseManager.GetInt(row, "Hours")
                ));
            }

            // ==========================================
            // PART 1: SINIF BAZLI (Class Based)
            // ==========================================
            sb.Append("<div class='page-container'>");
            
            // Report Header (Full Width)
            sb.Append("<div class='header-section'>");
            sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
            sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI</div>");
            sb.Append("<div style='font-size: 18px; font-weight: bold; margin-top: 5px;'>SINIF BAZLI DERS ATAMA LİSTESİ</div>");
            sb.Append($"<div style='text-align: right; font-size: 12px;'>Tarih: {pDate}</div>");
            sb.Append("</div>");

            // 2-Column Container
            sb.Append($"<div style='{containerStyle}'>");

            var byClass = assignments.GroupBy(x => x.ClassName).OrderBy(g => g.Key).ToList();
            
            foreach(var grp in byClass)
            {
                // Start Card
                sb.Append($"<div style='{cardStyle}'>");
                
                // Card Header (Class Name)
                sb.Append($"<div style='{cardHeaderStyle}'>{grp.Key} SINIFI</div>");
                
                // Mini Table
                sb.Append($"<table style='{tableStyle}'>");
                sb.Append("<thead><tr style='background-color:#fafafa;'><th style='text-align:left; padding:3px;'>Ders</th><th style='text-align:left; padding:3px;'>Öğretmen</th><th style='width:35px;'>Saat</th></tr></thead>");
                sb.Append("<tbody>");
                
                var clsRows = grp.OrderBy(x => x.LessonName).ToList();
                int totalH = 0;
                foreach(var item in clsRows)
                {
                    sb.Append("<tr>");
                    sb.Append($"<td style='{tdStyle}'>{item.LessonName}</td>");
                    sb.Append($"<td style='{tdStyle} font-weight:500;'>{item.TeacherName}</td>");
                    sb.Append($"<td style='{centerTdStyle} font-weight:bold;'>{item.Hours}</td>");
                    sb.Append("</tr>");
                    totalH += item.Hours;
                }
                
                // Total Row for Class
                sb.Append($"<tr style='background-color:#f9f9f9;'><td colspan='2' style='{tdStyle} text-align:right; font-weight:bold;'>Toplam:</td><td style='{centerTdStyle} font-weight:bold;'>{totalH}</td></tr>");
                
                sb.Append("</tbody></table>");
                sb.Append("</div>"); // End Card
            }

            sb.Append("</div>"); // End 2-Column Container
            
            // Footer 1
            sb.Append("<div class='footer-section'>");
            sb.Append("<div class='signature-box'>");
            sb.Append("<div class='signature-title'>Okul Müdürü</div>");
            sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
            sb.Append("</div><div style='clear:both;'></div>");
            sb.Append("</div>");

            sb.Append("</div>"); // End Page 1

            // ==========================================
            // PAGE BREAK
            // ==========================================
            sb.Append("<div class='page-break'></div>");

            // ==========================================
            // PART 2: ÖĞRETMEN BAZLI (Teacher Based)
            // ==========================================
            sb.Append("<div class='page-container'>");
            
            // Report Header
            sb.Append("<div class='header-section'>");
            sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
            sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI</div>");
            sb.Append("<div style='font-size: 18px; font-weight: bold; margin-top: 5px;'>ÖĞRETMEN BAZLI DERS ATAMA LİSTESİ</div>");
            sb.Append($"<div style='text-align: right; font-size: 12px;'>Tarih: {pDate}</div>");
            sb.Append("</div>");

            // 2-Column Container
            sb.Append($"<div style='{containerStyle}'>");

            var byTeacher = assignments.GroupBy(x => x.TeacherName).OrderBy(g => g.Key).ToList();
            
            foreach(var grp in byTeacher)
            {
                // Start Card
                sb.Append($"<div style='{cardStyle}'>");
                
                // Card Header (Teacher Name)
                sb.Append($"<div style='{cardHeaderStyle}'>{grp.Key}</div>");
                
                // Mini Table
                sb.Append($"<table style='{tableStyle}'>");
                sb.Append("<thead><tr style='background-color:#fafafa;'><th style='text-align:left; padding:3px;'>Sınıf</th><th style='text-align:left; padding:3px;'>Ders</th><th style='width:35px;'>Saat</th></tr></thead>");
                sb.Append("<tbody>");
                
                var tRows = grp.OrderBy(x => x.ClassName).ThenBy(x => x.LessonName).ToList();
                int totalHours = 0;

                foreach(var item in tRows)
                {
                    sb.Append("<tr>");
                    sb.Append($"<td style='{tdStyle} font-weight:600;'>{item.ClassName}</td>");
                    sb.Append($"<td style='{tdStyle}'>{item.LessonName}</td>");
                    sb.Append($"<td style='{centerTdStyle} font-weight:bold;'>{item.Hours}</td>");
                    sb.Append("</tr>");
                    totalHours += item.Hours;
                }
                
                // Teacher Total Row
                sb.Append($"<tr style='background-color:#eef2ff;'><td colspan='2' style='{tdStyle} text-align: right; font-weight: bold;'>Toplam Ders Saati:</td><td style='{centerTdStyle} font-weight: bold;'>{totalHours}</td></tr>");
                
                sb.Append("</tbody></table>");
                sb.Append("</div>"); // End Card
            }

            sb.Append("</div>"); // End 2-Column Container
            
            // Footer 2
            sb.Append("<div class='footer-section'>");
            sb.Append("<div class='signature-box'>");
            sb.Append("<div class='signature-title'>Okul Müdürü</div>");
            sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
            sb.Append("</div><div style='clear:both;'></div>");
            sb.Append("</div>");

            sb.Append("</div>"); // End Page 2
            
            return GetBaseHtml("Ders Atama Listesi", sb.ToString());
        }

        public string GenerateUnassignedReport(string reportDate = "")
        {
            var db = DatabaseManager.Shared;
            var sql = @"
                SELECT s.ad as ClassName, d.ad as Lesson, a.toplam_saat as Hours 
                FROM atama a 
                JOIN sinif s ON a.sinif_id = s.id 
                JOIN ders d ON a.ders_id = d.id 
                WHERE a.ogretmen_id IS NULL OR a.ogretmen_id = 0
                ORDER BY s.ad, d.ad";
                
            var results = db.Query(sql);
            var sb = new StringBuilder();
            
            if (results.Count == 0) return GetBaseHtml("Atanmamış Dersler", "<h3>Atanmamış ders bulunmamaktadır.</h3>");

            string academicYear = GetAcademicYear();

            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            sb.Append("<div class='header-section'>");
            sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
            sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI ATANMAMIŞ DERSLER LİSTESİ</div>");
            sb.Append($"<div style='text-align: right; font-size: 12px;'>Tarih: {pDate}</div>");
            sb.Append("</div>");
            
            sb.Append("<div style='margin-bottom:15px; color:#ef4444; font-weight:bold; text-align: center; border: 1px solid #ef4444; padding: 10px;'>DİKKAT: Bu derslere henüz bir öğretmen atanmamıştır.</div>");
            
            sb.Append("<table>");
            sb.Append("<thead><tr class='grid-header'><th style='width:60px;'>Sıra</th><th>Sınıf</th><th>Ders</th><th style='width:80px;'>Saat</th></tr></thead>");
            sb.Append("<tbody>");
            
            int i = 1;
            int totalUnassigned = 0;
            foreach(var row in results)
            {
                string className = DatabaseManager.GetString(row, "ClassName");
                string lesson = DatabaseManager.GetString(row, "Lesson");
                int hours = DatabaseManager.GetInt(row, "Hours");
                totalUnassigned += hours;
                
                sb.Append("<tr>");
                sb.Append($"<td>{i++}</td>");
                sb.Append($"<td style='font-weight:bold; text-align: left; padding-left: 10px;'>{className}</td>");
                sb.Append($"<td style='text-align: left; padding-left: 10px;'>{lesson}</td>");
                sb.Append($"<td>{hours}</td>");
                sb.Append("</tr>");
            }
             sb.Append($"<tr style='background-color:#fee2e2;'><td colspan='3' style='text-align:right; font-weight:bold; padding-right: 15px;'>TOPLAM ATANMAMIŞ SAAT:</td><td style='text-align:center; font-weight:bold; color:#ef4444;'>{totalUnassigned}</td></tr>");
            
            sb.Append("</tbody></table>");
            
            // Footer
            sb.Append("<div class='footer-section'>");
            sb.Append("<div class='signature-box'>");
            sb.Append("<div class='signature-title'>Okul Müdürü</div>");
            sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
            sb.Append("</div>");
            sb.Append("</div>");

            return GetBaseHtml("Atanmamış Dersler", sb.ToString());
        }

        public string GenerateElectiveRatioReport(string reportDate = "")
        {
            var db = DatabaseManager.Shared;
            var sql = @"
                SELECT d.ad as LessonName, d.kod as LessonCode, SUM(a.toplam_saat) as TotalHours 
                FROM atama a 
                JOIN ders d ON a.ders_id = d.id 
                GROUP BY d.id
                ORDER BY d.ad";
                
            var results = db.Query(sql);
            var sb = new StringBuilder();
            
            string academicYear = GetAcademicYear();
            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd/MM/yyyy");

            sb.Append("<div class='header-section'>");
            sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
            sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI SEÇMELİ / ZORUNLU DERS ORANLARI</div>");
            sb.Append($"<div style='text-align: right; font-size: 12px;'>Tarih: {pDate}</div>");
            sb.Append("</div>");
            
            // Calculate Stats
            int totalCompulsory = 0;
            int totalElective = 0;
            
            var electiveLessons = new List<(string Name, int Hours)>();
            var compulsoryLessons = new List<(string Name, int Hours)>();
            
            foreach(var row in results)
            {
                string name = DatabaseManager.GetString(row, "LessonName");
                string code = DatabaseManager.GetString(row, "LessonCode");
                int hours = DatabaseManager.GetInt(row, "TotalHours");
                
                bool isElective = name.IndexOf("SEÇMELİ", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                  name.IndexOf("SEÇ.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  (!string.IsNullOrEmpty(code) && code.StartsWith("S.", StringComparison.OrdinalIgnoreCase));
                                  
                if (isElective)
                {
                    totalElective += hours;
                    electiveLessons.Add((name, hours));
                }
                else
                {
                    totalCompulsory += hours;
                    compulsoryLessons.Add((name, hours));
                }
            }
            
            int grandTotal = totalCompulsory + totalElective;
            double electiveRatio = grandTotal > 0 ? (double)totalElective / grandTotal * 100 : 0;
            double compulsoryRatio = grandTotal > 0 ? (double)totalCompulsory / grandTotal * 100 : 0;
            
            // Summary Box
            sb.Append("<div style='margin-bottom: 25px; padding: 15px; border: 1px solid #ccc; background-color: #f9f9f9; border-radius: 8px;'>");
            sb.Append("<table style='width: 100%; border: none;'>");
            sb.Append("<tr>");
            
            sb.Append("<td style='border: none; text-align: center;'>");
            sb.Append("<div style='font-size: 14px; color: #666;'>Toplam Ders Saati</div>");
            sb.Append($"<div style='font-size: 24px; font-weight: bold;'>{grandTotal}</div>");
            sb.Append("</td>");
            
            sb.Append("<td style='border: none; text-align: center;'>");
            sb.Append("<div style='font-size: 14px; color: #15803d;'>Zorunlu Dersler</div>");
            sb.Append($"<div style='font-size: 24px; font-weight: bold; color: #15803d;'>{totalCompulsory} <span style='font-size: 16px; color: #888;'>(%{compulsoryRatio:F1})</span></div>");
            sb.Append("</td>");
            
            sb.Append("<td style='border: none; text-align: center;'>");
            sb.Append("<div style='font-size: 14px; color: #b91c1c;'>Seçmeli Dersler</div>");
            sb.Append($"<div style='font-size: 24px; font-weight: bold; color: #b91c1c;'>{totalElective} <span style='font-size: 16px; color: #888;'>(%{electiveRatio:F1})</span></div>");
            sb.Append("</td>");
            
            sb.Append("</tr>");
            sb.Append("</table>");
            sb.Append("</div>");
            
            // Detailed Tables
            sb.Append("<div style='display: flex; gap: 20px;'>");
            
            // Compulsory Table
            sb.Append("<div style='flex: 1;'>");
            sb.Append("<div style='font-weight: bold; margin-bottom: 5px; color: #15803d;'>Zorunlu Dersler Listesi</div>");
            sb.Append("<table>");
            sb.Append("<thead><tr class='grid-header'><th>Ders Adı</th><th style='width: 60px;'>Saat</th></tr></thead>");
            sb.Append("<tbody>");
            foreach(var item in compulsoryLessons.OrderByDescending(x => x.Hours))
            {
                sb.Append("<tr>");
                sb.Append($"<td style='text-align: left; padding-left: 10px;'>{item.Name}</td>");
                sb.Append($"<td>{item.Hours}</td>");
                sb.Append("</tr>");
            }
            if (compulsoryLessons.Count == 0) sb.Append("<tr><td colspan='2'>Kayıt yok.</td></tr>");
            sb.Append("</tbody></table>");
            sb.Append("</div>");
            
            // Elective Table
            sb.Append("<div style='flex: 1;'>");
            sb.Append("<div style='font-weight: bold; margin-bottom: 5px; color: #b91c1c;'>Seçmeli Dersler Listesi</div>");
            sb.Append("<table>");
            sb.Append("<thead><tr class='grid-header'><th>Ders Adı</th><th style='width: 60px;'>Saat</th></tr></thead>");
            sb.Append("<tbody>");
            if (electiveLessons.Count > 0)
            {
                foreach(var item in electiveLessons.OrderByDescending(x => x.Hours))
                {
                    sb.Append("<tr>");
                    sb.Append($"<td style='text-align: left; padding-left: 10px;'>{item.Name}</td>");
                    sb.Append($"<td>{item.Hours}</td>");
                    sb.Append("</tr>");
                }
            }
            else
            {
                sb.Append("<tr><td colspan='2'>Seçmeli ders bulunamadı.</td></tr>");
            }
            sb.Append("</tbody></table>");
            sb.Append("</div>");
            
            sb.Append("</div>"); // End Flex
            
            // Footer
            sb.Append("<div class='footer-section'>");
            sb.Append("<div class='signature-box'>");
            sb.Append("<div class='signature-title'>Okul Müdürü</div>");
            sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
            sb.Append("</div>");
            sb.Append("</div>");
            
            return GetBaseHtml("Seçmeli/Zorunlu Ders Oranı", sb.ToString());
        }
        public string GenerateJointSpaceScheduleReport(string reportDate = "")
        {
            var mekanRepo = new OrtakMekanRepository();
            var mekanlar = mekanRepo.GetAll().OrderBy(m => m.Name).ToList();

            if (mekanlar.Count == 0) return GetBaseHtml("Mekan Programı (Ortak Mekanlar)", "<h3>Hiç ortak mekan tanımlanmamış.</h3>");

            var sb = new StringBuilder();

            string[] dayNames = { "PAZARTESİ", "SALI", "ÇARŞAMBA", "PERŞEMBE", "CUMA", "CUMARTESİ", "PAZAR" };
            int visibleDays = _schoolInfo.Days;
            int dailyLessons = GetMaxVisibleLessonCount();

            string academicYear = GetAcademicYear();
            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd.MM.yyyy");

            foreach (var mekan in mekanlar)
            {
                // Page Break
                sb.Append("<div class='page-container landscape'>");

                // Header
                sb.Append("<div class='header-section'>");
                sb.Append($"<div class='school-name'>{_schoolInfo.Name}</div>");
                sb.Append($"<div class='academic-year'>{academicYear} EĞİTİM ÖĞRETİM YILI MEKAN DERS PROGRAMI</div>");
                sb.Append($"<div style='font-size: 20px; font-weight: bold; margin-top: 10px;'>MEKAN: {mekan.Name}</div>");
                sb.Append($"<div style='text-align: right; font-size: 14px;'>Tarih: {pDate}</div>");
                sb.Append("</div>");

                // Table
                sb.Append("<table style='width: 100%; border-collapse: collapse; border: 2px solid #000; table-layout: fixed;'>");

                // Table Head
                sb.Append("<thead>");
                sb.Append("<tr class='grid-header'>");
                sb.Append("<th style='width: 80px; padding: 5px;'>GÜNLER</th>");
                for (int h = 1; h <= dailyLessons; h++)
                {
                    string timeStr = "";
                    if (_schoolInfo.LessonHours != null && h <= 12 && !string.IsNullOrEmpty(_schoolInfo.LessonHours[h - 1]))
                    {
                        var rawValue = _schoolInfo.LessonHours[h - 1];
                        if (rawValue.Contains("-"))
                        {
                            var parts = rawValue.Split('-');
                            timeStr = $"{parts[0].Trim()}<br>{parts[1].Trim()}";
                        }
                        else { timeStr = rawValue; }
                    }
                    string timeRange = !string.IsNullOrEmpty(timeStr) ? $"<span class='time-range'>{timeStr}</span>" : "";
                    sb.Append($"<th style='padding: 2px;'>{h}.Ders{timeRange}</th>");
                }
                sb.Append("</tr>");
                sb.Append("</thead>");

                // Table Body
                sb.Append("<tbody>");
                for (int d = 1; d <= visibleDays; d++)
                {
                    sb.Append("<tr style='height: 60px;'>");
                    sb.Append($"<td style='border: 1px solid #000; font-weight: bold; text-align: center; background-color: #fafafa;'>{dayNames[d - 1]}</td>");

                    for (int h = 1; h <= dailyLessons; h++)
                    {
                        var slot = new TimeSlot(d, h);
                        string content = "&nbsp;"; 

                        if (mekan.ScheduleInfo.ContainsKey(slot))
                        {
                             var raw = mekan.ScheduleInfo[slot];
                             if (!string.IsNullOrEmpty(raw))
                             {
                                 // Format: Class    Lesson    Teacher (Tab/Space separated)
                                 var parts = raw.Split(new[]{"    ", "\t", "   "}, StringSplitOptions.RemoveEmptyEntries);
                                 
                                 if (parts.Length >= 2)
                                 {
                                     string pClass = parts[0];
                                     string pLesson = parts[1];
                                     string pTeacher = parts.Length > 2 ? parts[2] : "";
                                     
                                     content = $"<div style='display:flex; flex-direction:column; align-items:center; justify-content:center; height:100%; font-size:11px; line-height:1.2;'>" +
                                          $"<span style='font-weight:bold; font-size:12px; margin-bottom:2px;'>{pClass}</span>" +
                                          $"<span style='margin-bottom:2px;'>{pLesson}</span>" +
                                          $"<span>{pTeacher}</span>" +
                                          $"</div>";
                                 }
                                 else
                                 {
                                     content = raw;
                                 }
                             }
                        }

                        sb.Append($"<td style='border: 1px solid #000; vertical-align: middle; text-align: center; padding: 2px;'>{content}</td>");
                    }
                    sb.Append("</tr>");
                }
                sb.Append("</tbody>");
                sb.Append("</table>");

                // --- SUMMARY TABLE (Derslerin Listesi) ---
                var dbSummary = DatabaseManager.Shared;
                // Fetch blocks assigned to this room
                var sqlSum = $@"
                    SELECT b.ders_kodu, b.blok_suresi, b.sinif_id, s.ad as SinifAdi,
                           b.ogretmen_1_id, b.ogretmen_2_id, b.ogretmen_3_id, b.ogretmen_4_id, b.ogretmen_5_id,
                           b.ortak_mekan_1_id, b.ortak_mekan_2_id, b.ortak_mekan_3_id, b.ortak_mekan_4_id, b.ortak_mekan_5_id,
                           IFNULL(d.ad, '') as DersAdi
                    FROM dagitim_bloklari b
                    JOIN sinif s ON b.sinif_id = s.id
                    LEFT JOIN sinif_ders sd ON b.sinif_ders_id = sd.id
                    LEFT JOIN ders d ON sd.ders_id = d.id
                    WHERE b.ortak_mekan_1_id = {mekan.Id} OR b.ortak_mekan_2_id = {mekan.Id} OR b.ortak_mekan_3_id = {mekan.Id} OR b.ortak_mekan_4_id = {mekan.Id} OR b.ortak_mekan_5_id = {mekan.Id}";
                
                var dataSum = dbSummary.Query(sqlSum);
                
                var summaryList = new List<(string Lesson, string ClassName, string Teacher, int Hours)>();
                
                // Helper to get teacher name (cache if possible but queries are fine for report generation)
                string GetTeacherName(int tid) 
                {
                    if (tid <= 0) return "-";
                    var tRows = dbSummary.Query($"SELECT ad_soyad FROM ogretmen WHERE id={tid}");
                    return tRows.Count > 0 ? DatabaseManager.GetString(tRows[0], "ad_soyad") : "-";
                }

                foreach(var rowSum in dataSum)
                {
                    string lCode = DatabaseManager.GetString(rowSum, "ders_kodu");
                    string lName = DatabaseManager.GetString(rowSum, "DersAdi");
                    string lDisplay = !string.IsNullOrEmpty(lName) ? $"{lName}  -  {lCode}" : lCode;
                    string cName = DatabaseManager.GetString(rowSum, "SinifAdi");
                    int dur = DatabaseManager.GetInt(rowSum, "blok_suresi");
                    
                    // Find the teacher for THIS room
                    int tid = 0;
                    if (DatabaseManager.GetInt(rowSum, "ortak_mekan_1_id") == mekan.Id) tid = DatabaseManager.GetInt(rowSum, "ogretmen_1_id");
                    else if (DatabaseManager.GetInt(rowSum, "ortak_mekan_2_id") == mekan.Id) tid = DatabaseManager.GetInt(rowSum, "ogretmen_2_id");
                    else if (DatabaseManager.GetInt(rowSum, "ortak_mekan_3_id") == mekan.Id) tid = DatabaseManager.GetInt(rowSum, "ogretmen_3_id");
                    else if (DatabaseManager.GetInt(rowSum, "ortak_mekan_4_id") == mekan.Id) tid = DatabaseManager.GetInt(rowSum, "ogretmen_4_id");
                    else if (DatabaseManager.GetInt(rowSum, "ortak_mekan_5_id") == mekan.Id) tid = DatabaseManager.GetInt(rowSum, "ogretmen_5_id");
                    
                    string tName = GetTeacherName(tid);
                    
                    summaryList.Add((lDisplay, cName, tName, dur));
                }
                
                // Group by Class + Lesson + Teacher
                var groupedSum = summaryList
                    .GroupBy(x => new { x.ClassName, x.Lesson, x.Teacher })
                    .Select(g => new { 
                        g.Key.ClassName, 
                        g.Key.Lesson, 
                        g.Key.Teacher, 
                        TotalHours = g.Sum(x => x.Hours) 
                    })
                    .OrderBy(x => x.ClassName).ThenBy(x => x.Lesson)
                    .ToList();

                if (groupedSum.Count > 0)
                {
                    sb.Append("<div style='margin-top: 20px;'>");
                    sb.Append("<div style='font-weight: bold; margin-bottom: 5px; font-size: 14px;'>MEKANDAKİ DERSLERİN LİSTESİ</div>");
                    sb.Append("<table style='width: 100%; border-collapse: collapse; border: 1px solid #ccc; font-size: 11px;'>");
                    sb.Append("<thead>");
                    sb.Append("<tr style='background-color: #f0f0f0;'>");
                    sb.Append("<th style='border: 1px solid #ccc; padding: 4px; text-align: left;'>Sıra</th>");
                    sb.Append("<th style='border: 1px solid #ccc; padding: 4px; text-align: left;'>Sınıf</th>");
                    sb.Append("<th style='border: 1px solid #ccc; padding: 4px; text-align: left;'>Ders</th>");
                    sb.Append("<th style='border: 1px solid #ccc; padding: 4px; text-align: left;'>Öğretmen</th>");
                    sb.Append("<th style='border: 1px solid #ccc; padding: 4px; text-align: center; width: 60px;'>Toplam Saat</th>");
                    sb.Append("</tr>");
                    sb.Append("</thead>");
                    sb.Append("<tbody>");
                    
                    int idx = 1;
                    int GrandTotal = 0;
                    foreach(var item in groupedSum)
                    {
                        sb.Append("<tr>");
                        sb.Append($"<td style='border: 1px solid #ccc; padding: 4px;'>{idx++}</td>");
                        sb.Append($"<td style='border: 1px solid #ccc; padding: 4px; font-weight: bold;'>{item.ClassName}</td>");
                        sb.Append($"<td style='border: 1px solid #ccc; padding: 4px;'>{item.Lesson}</td>");
                        sb.Append($"<td style='border: 1px solid #ccc; padding: 4px;'>{item.Teacher}</td>");
                        sb.Append($"<td style='border: 1px solid #ccc; padding: 4px; text-align: center;'>{item.TotalHours}</td>");
                        sb.Append("</tr>");
                        GrandTotal += item.TotalHours;
                    }
                    // Final Total Row
                    sb.Append($"<tr style='background-color: #f9f9f9; font-weight: bold;'>");
                    sb.Append($"<td colspan='4' style='border: 1px solid #ccc; padding: 4px; text-align: right;'>GENEL TOPLAM:</td>");
                    sb.Append($"<td style='border: 1px solid #ccc; padding: 4px; text-align: center;'>{GrandTotal}</td>");
                    sb.Append("</tr>");
                    
                    sb.Append("</tbody>");
                    sb.Append("</table>");
                    sb.Append("</div>");
                }

                // Footer
                sb.Append("<div class='footer-section'>");
                sb.Append("<div class='signature-box'>");
                sb.Append("<div class='signature-title'>Okul Müdürü</div>");
                sb.Append($"<div class='signature-name'>{_schoolInfo.Principal}</div>");
                sb.Append("</div>");
                sb.Append("</div>");

                sb.Append("</div>"); // End Page
            }

            return GetBaseHtml("Mekan Programı Listesi", sb.ToString());
        }
        public int GetMaxVisibleLessonCount()
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
        private string FormatTeacherName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "";
            var parts = fullName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1) return fullName;

            var sb = new StringBuilder();
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
        public string GenerateJointTeacherReport(string reportDate = "")
        {
            var sb = new StringBuilder();
            string academicYear = GetAcademicYear();
            string pDate = !string.IsNullOrEmpty(reportDate) ? reportDate : DateTime.Now.ToString("dd.MM.yyyy");

            // HEADER
            sb.Append("<div class='page-container' style='padding: 20px;'>");
            sb.Append("<div style='text-align: center; border-bottom: 2px solid #000; padding-bottom: 5px; margin-bottom: 10px;'>");
            sb.Append("<div style='font-size: 14px; font-weight: bold;'>T.C.</div>");
            sb.Append($"<div style='font-size: 14px; font-weight: bold;'>{_schoolInfo.Name.ToUpper()}</div>");
            sb.Append($"<div style='font-size: 14px; font-weight: bold;'>{academicYear} EĞİTİM ÖĞRETİM YILI</div>");
            sb.Append("<div style='font-size: 14px; font-weight: bold;'>BİRLİKTE DERSE GİREN ÖĞRETMENLER LİSTESİ</div>");
            sb.Append("</div>");

            sb.Append("<div style='margin-top: 20px;'>");
            sb.Append("<table style='width: 100%; border-collapse: collapse; border: 1px solid #000;'>");
            sb.Append("<thead><tr style='background-color: #f0f0f0;'>");
            sb.Append("<th style='border: 1px solid #000; padding: 5px; width: 50px;'>No</th>");
            sb.Append("<th style='border: 1px solid #000; padding: 5px;'>Sınıf</th>");
            sb.Append("<th style='border: 1px solid #000; padding: 5px;'>Ders</th>");
            sb.Append("<th style='border: 1px solid #000; padding: 5px; width: 50px;'>Saat</th>");
            sb.Append("<th style='border: 1px solid #000; padding: 5px;'>Öğretmenler</th>");
            sb.Append("</tr></thead>");
            sb.Append("<tbody>");

            // Query Data - Corrected Tables
            string query = @"
                SELECT 
                    s.ad AS ClassName,
                    d.ad AS LessonName,
                    sd.toplam_saat AS Hours,
                    GROUP_CONCAT(o.ad_soyad, '<br>') AS Teachers
                FROM sinif_ders sd
                JOIN sinif s ON sd.sinif_id = s.id
                JOIN ders d ON sd.ders_id = d.id
                JOIN atama a ON a.sinif_ders_id = sd.id
                JOIN ogretmen o ON a.ogretmen_id = o.id
                GROUP BY sd.id
                HAVING COUNT(a.id) > 1
                ORDER BY s.ad, d.ad";

            var rows = DatabaseManager.Shared.Query(query);
            int count = 0;

            foreach (var row in rows)
            {
                count++;
                string className = DatabaseManager.GetString(row, "ClassName");
                string lessonName = DatabaseManager.GetString(row, "LessonName");
                int hours = DatabaseManager.GetInt(row, "Hours");
                string teachers = DatabaseManager.GetString(row, "Teachers");

                sb.Append("<tr>");
                sb.Append($"<td style='border: 1px solid #000; padding: 5px; text-align: center;'>{count}</td>");
                sb.Append($"<td style='border: 1px solid #000; padding: 5px;'>{className}</td>");
                sb.Append($"<td style='border: 1px solid #000; padding: 5px;'>{lessonName}</td>");
                sb.Append($"<td style='border: 1px solid #000; padding: 5px; text-align: center;'>{hours}</td>");
                sb.Append($"<td style='border: 1px solid #000; padding: 5px;'>{teachers}</td>");
                sb.Append("</tr>");
            }

            if (count == 0)
            {
                sb.Append("<tr><td colspan='5' style='border: 1px solid #000; padding: 20px; text-align: center;'>Birlikte girilen ders bulunamadı.</td></tr>");
            }

            sb.Append("</tbody></table>");
            sb.Append("</div>");

            // Footer
            sb.Append("<div style='margin-top: 30px;'>");
            sb.Append($"<div style='text-align: right; font-size: 12px;'>Rapor Tarihi: {pDate}</div>");
            sb.Append("<div style='margin-top: 50px; text-align: right;'>");
            sb.Append("<div>Okul Müdürü</div>");
            sb.Append($"<div style='font-weight: bold;'>{_schoolInfo.Principal}</div>");
            sb.Append("</div>");
            sb.Append("</div>"); // End Footer
            
            sb.Append("</div>"); // End Page Container

            return GetBaseHtml("Birlikte Derse Giren Öğretmenler", sb.ToString());
        }
    }
}
