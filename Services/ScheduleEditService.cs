using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DersDagitim.Models;

namespace DersDagitim.Services;

/// <summary>
/// Öğretmen programında blok taşıma ve cascade çakışma çözümü servisi.
/// OR-Tools CP-SAT solver kullanarak tüm çakışmaları matematiksel olarak çözer.
/// Tüm hesaplamalar in-memory yapılır, veritabanına dokunmaz.
/// </summary>
public static class ScheduleEditService
{
    public class BlockChange
    {
        public int BlockId { get; set; }
        public int OldDay { get; set; }
        public int OldHour { get; set; }
        public int NewDay { get; set; }
        public int NewHour { get; set; }
        public string Description { get; set; } = "";
    }

    public class MoveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<BlockChange> Changes { get; set; } = new();
        public int RemainingConflicts { get; set; }
    }

    // ===== OR-TOOLS ASYNC WRAPPER =====

    /// <summary>
    /// OR-Tools CP-SAT solver ile bloğu taşır ve tüm çakışmaları çözer.
    /// </summary>
    public static async Task<MoveResult> CascadeMoveAsync(
        DistributionBlock sourceBlock,
        int targetDay, int targetHour,
        List<DistributionBlock> allBlocks,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes,
        Dictionary<int, OrtakMekan> rooms,
        int maxDays, int maxHours)
    {
        return await OrToolsScheduleEditEngineFree.SolveEdit(
            sourceBlock, targetDay, targetHour,
            allBlocks, teachers, classes, rooms,
            maxDays, maxHours);
    }

    // ===== KAPALI KONTROL YARDIMCILARI =====

    private static bool IsTeacherSlotTrulyClosed(Teacher teacher, int day, int hour)
    {
        var slot = new TimeSlot(day, hour);
        return teacher.Constraints.TryGetValue(slot, out var state) && state == SlotState.Closed
            && !teacher.ScheduleInfo.ContainsKey(slot);
    }

    private static bool IsClassSlotTrulyClosed(SchoolClass cls, int day, int hour)
    {
        var slot = new TimeSlot(day, hour);
        if (!cls.Constraints.TryGetValue(slot, out var state) || state != SlotState.Closed)
            return false;

        var key = $"d_{day}_{hour}";
        if (cls.Schedule.TryGetValue(key, out var val))
            return val == "KAPALI" || string.IsNullOrEmpty(val);

        return true;
    }

    private static bool IsAnySlotTrulyClosed(
        DistributionBlock block, int day, int hour,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes)
    {
        for (int i = 0; i < block.BlockDuration; i++)
        {
            int h = hour + i;

            foreach (var tid in block.TeacherIds)
            {
                if (teachers.TryGetValue(tid, out var teacher) && IsTeacherSlotTrulyClosed(teacher, day, h))
                    return true;
            }

            if (classes.TryGetValue(block.ClassId, out var cls) && IsClassSlotTrulyClosed(cls, day, h))
                return true;
        }

        return false;
    }

    // ===== LEGACY SENKRON CascadeMove =====

    /// <summary>
    /// Greedy cascade algoritması — geriye uyumluluk için.
    /// Yeni kod CascadeMoveAsync kullanmalı.
    /// </summary>
    public static MoveResult CascadeMove(
        DistributionBlock sourceBlock,
        int targetDay,
        int targetHour,
        List<DistributionBlock> allBlocks,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes,
        Dictionary<int, OrtakMekan> rooms,
        int maxDays,
        int maxHours)
    {
        var result = new MoveResult();

        // Kilitli blok kontrolü
        if (sourceBlock.IsLocked)
        {
            result.Message = "Bu blok kilitli, taşınamaz.";
            return result;
        }

        // Saat aşım kontrolü
        for (int i = 0; i < sourceBlock.BlockDuration; i++)
        {
            if (targetHour + i > maxHours)
            {
                result.Message = $"Blok süresi gün sonunu aşıyor (saat {targetHour + i} > {maxHours}).";
                return result;
            }
        }

        // Hedef slot gerçek KAPALI kontrolü
        if (IsAnySlotTrulyClosed(sourceBlock, targetDay, targetHour, teachers, classes))
        {
            result.Message = "Hedef slot KAPALI, buraya yerleştirilemez.";
            return result;
        }

        // ===== In-memory çalışma kopyası =====
        var workingBlocks = allBlocks
            .Where(b => b.IsPlaced)
            .Select(b => b.Clone())
            .ToList();

        // Orijinal pozisyonları sakla (rollback için)
        var originalPositions = workingBlocks.ToDictionary(
            b => b.Id,
            b => (day: b.Day, hour: b.Hour));

        // Kaynak bloğu taşı
        var workSource = workingBlocks.First(b => b.Id == sourceBlock.Id);
        workSource.Day = targetDay;
        workSource.Hour = targetHour;

        // Her bloğun kaç kez taşındığını takip et (sonsuz döngü engeli)
        var moveCount = new Dictionary<int, int>();
        moveCount[sourceBlock.Id] = 1;

        const int MAX_MOVES_PER_BLOCK = 5;
        const int MAX_ITERATIONS = 200;

        // ===== CASCADE ÇÖZÜM DÖNGÜSÜ =====
        for (int iteration = 0; iteration < MAX_ITERATIONS; iteration++)
        {
            var conflictIds = FindConflicts(workingBlocks);
            if (conflictIds.Count == 0)
                break; // Tüm çakışmalar çözüldü!

            bool anyMoved = false;

            // Çakışan blokları işle — kaynak blok HARİÇ, diğerleri taşınabilir
            foreach (var conflictBlockId in conflictIds)
            {
                // Kaynak bloğu asla geri taşıma — kullanıcı bunu oraya istedi
                if (conflictBlockId == sourceBlock.Id)
                    continue;

                // Kilitli blokları taşıma
                var block = workingBlocks.First(b => b.Id == conflictBlockId);
                if (block.IsLocked)
                    continue;

                // Sonsuz döngü engeli: her blok max N kez taşınabilir
                int currentMoves = moveCount.GetValueOrDefault(conflictBlockId, 0);
                if (currentMoves >= MAX_MOVES_PER_BLOCK)
                    continue;

                // Bu blok hâlâ gerçekten çakışıyor mu? (önceki taşımalar çözmüş olabilir)
                if (!IsBlockInConflict(block, workingBlocks))
                    continue;

                // Önce çakışmasız slot bul
                var bestSlot = FindBestSlot(block, workingBlocks, teachers, classes, rooms, maxDays, maxHours);

                if (bestSlot.HasValue)
                {
                    block.Day = bestSlot.Value.day;
                    block.Hour = bestSlot.Value.hour;
                    moveCount[conflictBlockId] = currentMoves + 1;
                    anyMoved = true;
                }
                else
                {
                    // Çakışmasız slot yok — en az çakışmalı slota zorla yerleştir
                    var forcedSlot = FindLeastConflictSlot(block, workingBlocks, teachers, classes, rooms, maxDays, maxHours);
                    if (forcedSlot.HasValue)
                    {
                        block.Day = forcedSlot.Value.day;
                        block.Hour = forcedSlot.Value.hour;
                        moveCount[conflictBlockId] = currentMoves + 1;
                        anyMoved = true;
                    }
                }
            }

            if (!anyMoved)
                break; // Hiçbir şey taşınamadı — deadlock
        }

        // ===== SONUÇ DEĞERLENDİRME =====
        var remainingConflicts = FindConflicts(workingBlocks);

        if (remainingConflicts.Count > 0)
        {
            // Çakışmalar çözülemedi — dağıtımı bozmamak için işlemi REDDET
            result.Success = false;
            result.RemainingConflicts = remainingConflicts.Count;
            result.Message = $"Bu taşıma {remainingConflicts.Count} çakışma oluşturuyor ve çözülemiyor.\n" +
                             "Dağıtımın bozulmaması için işlem iptal edildi.\n" +
                             "Farklı bir hedef slot deneyin.";
            return result;
        }

        // Tüm çakışmalar çözüldü — değişiklikleri hesapla
        var changes = new List<BlockChange>();
        foreach (var block in workingBlocks)
        {
            if (!originalPositions.TryGetValue(block.Id, out var orig))
                continue;

            if (block.Day != orig.day || block.Hour != orig.hour)
            {
                changes.Add(new BlockChange
                {
                    BlockId = block.Id,
                    OldDay = orig.day,
                    OldHour = orig.hour,
                    NewDay = block.Day,
                    NewHour = block.Hour
                });
            }
        }

        // Açıklama metinlerini oluştur
        foreach (var change in changes)
        {
            var block = workingBlocks.First(b => b.Id == change.BlockId);
            string className = classes.TryGetValue(block.ClassId, out var c) ? c.Name : "";

            string[] dayNames = { "", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz" };
            string oldPos = $"{dayNames[change.OldDay]} {change.OldHour}.saat";
            string newPos = $"{dayNames[change.NewDay]} {change.NewHour}.saat";
            change.Description = $"{block.LessonCode} {className}: {oldPos} → {newPos}";
        }

        result.Success = true;
        result.Changes = changes;
        result.RemainingConflicts = 0;
        result.Message = $"✓ {changes.Count} blok değişti, tüm çakışmalar çözüldü.";
        return result;
    }

    // ===== ÇAKIŞMA TESPİTİ =====

    /// <summary>
    /// Tüm çakışan blok ID'lerini döner.
    /// </summary>
    private static List<int> FindConflicts(List<DistributionBlock> blocks)
    {
        var conflicting = new HashSet<int>();

        for (int i = 0; i < blocks.Count; i++)
        {
            for (int j = i + 1; j < blocks.Count; j++)
            {
                var a = blocks[i];
                var b = blocks[j];

                if (a.Day == 0 || b.Day == 0) continue;
                if (a.Day != b.Day) continue;

                if (!HoursOverlap(a.Hour, a.BlockDuration, b.Hour, b.BlockDuration))
                    continue;

                bool hasConflict = false;

                // Öğretmen çakışması
                if (a.TeacherIds.Intersect(b.TeacherIds).Any())
                    hasConflict = true;

                // Sınıf çakışması
                if (a.ClassId == b.ClassId)
                    hasConflict = true;

                // Ortak mekan çakışması
                if (a.GetOrtakMekanIds().Intersect(b.GetOrtakMekanIds()).Any())
                    hasConflict = true;

                if (hasConflict)
                {
                    conflicting.Add(a.Id);
                    conflicting.Add(b.Id);
                }
            }
        }

        return conflicting.ToList();
    }

    /// <summary>
    /// Tek bir bloğun başka bloklarla çakışıp çakışmadığını kontrol eder.
    /// </summary>
    private static bool IsBlockInConflict(DistributionBlock block, List<DistributionBlock> allBlocks)
    {
        if (block.Day == 0) return false;

        foreach (var other in allBlocks)
        {
            if (other.Id == block.Id || other.Day != block.Day || other.Day == 0) continue;
            if (!HoursOverlap(block.Hour, block.BlockDuration, other.Hour, other.BlockDuration)) continue;

            if (block.TeacherIds.Intersect(other.TeacherIds).Any()) return true;
            if (block.ClassId == other.ClassId) return true;
            if (block.GetOrtakMekanIds().Intersect(other.GetOrtakMekanIds()).Any()) return true;
        }

        return false;
    }

    private static bool HoursOverlap(int hourA, int durA, int hourB, int durB)
    {
        int endA = hourA + durA;
        int endB = hourB + durB;
        return hourA < endB && hourB < endA;
    }

    // ===== SLOT BULMA =====

    /// <summary>
    /// Blok için en uygun çakışmasız slotu bulur.
    /// </summary>
    private static (int day, int hour)? FindBestSlot(
        DistributionBlock block,
        List<DistributionBlock> allBlocks,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes,
        Dictionary<int, OrtakMekan> rooms,
        int maxDays,
        int maxHours)
    {
        (int day, int hour)? best = null;
        int bestScore = int.MaxValue;

        for (int d = 1; d <= maxDays; d++)
        {
            for (int h = 1; h <= maxHours - block.BlockDuration + 1; h++)
            {
                if (!IsSlotValid(block, d, h, allBlocks, teachers, classes, rooms, maxHours))
                    continue;

                int score = CalculateSlotScore(block, d, h, allBlocks, maxHours);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = (d, h);
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Slot geçerli mi? Gerçek KAPALI + çakışma kontrolü.
    /// </summary>
    private static bool IsSlotValid(
        DistributionBlock block,
        int day,
        int hour,
        List<DistributionBlock> allBlocks,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes,
        Dictionary<int, OrtakMekan> rooms,
        int maxHours)
    {
        for (int i = 0; i < block.BlockDuration; i++)
        {
            int checkHour = hour + i;
            if (checkHour > maxHours) return false;

            // Gerçek KAPALI kontrolü (öğretmen)
            foreach (var tid in block.TeacherIds)
            {
                if (teachers.TryGetValue(tid, out var teacher) && IsTeacherSlotTrulyClosed(teacher, day, checkHour))
                    return false;
            }

            // Gerçek KAPALI kontrolü (sınıf)
            if (classes.TryGetValue(block.ClassId, out var cls) && IsClassSlotTrulyClosed(cls, day, checkHour))
                return false;

            // Öğretmen çakışması
            foreach (var tid in block.TeacherIds)
            {
                if (allBlocks.Any(b => b.Id != block.Id && b.Day == day &&
                    b.TeacherIds.Contains(tid) &&
                    HoursOverlap(b.Hour, b.BlockDuration, checkHour, 1)))
                    return false;
            }

            // Sınıf çakışması
            if (allBlocks.Any(b => b.Id != block.Id && b.Day == day &&
                b.ClassId == block.ClassId &&
                HoursOverlap(b.Hour, b.BlockDuration, checkHour, 1)))
                return false;

            // Ortak mekan çakışması
            var blockRooms = block.GetOrtakMekanIds();
            if (blockRooms.Count > 0)
            {
                foreach (var rid in blockRooms)
                {
                    if (allBlocks.Any(b => b.Id != block.Id && b.Day == day &&
                        b.GetOrtakMekanIds().Contains(rid) &&
                        HoursOverlap(b.Hour, b.BlockDuration, checkHour, 1)))
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Slot kalite skoru (düşük = daha iyi).
    /// </summary>
    private static int CalculateSlotScore(
        DistributionBlock block,
        int day,
        int hour,
        List<DistributionBlock> allBlocks,
        int maxHours)
    {
        int score = 0;

        // Orijinal konuma yakınlık
        score += Math.Abs(day - block.Day) * 1000;
        score += Math.Abs(hour - block.Hour) * 500;

        // Sabah önceliği
        score += hour * 100;

        // Gap cezası
        foreach (var tid in block.TeacherIds)
        {
            var teacherBlocksOnDay = allBlocks
                .Where(b => b.Id != block.Id && b.Day == day && b.TeacherIds.Contains(tid))
                .ToList();

            if (teacherBlocksOnDay.Count > 0)
            {
                int minHour = Math.Min(hour, teacherBlocksOnDay.Min(b => b.Hour));
                int maxH = Math.Max(hour + block.BlockDuration,
                    teacherBlocksOnDay.Max(b => b.Hour + b.BlockDuration));
                int span = maxH - minHour;
                int totalDuration = block.BlockDuration + teacherBlocksOnDay.Sum(b => b.BlockDuration);
                int gap = span - totalDuration;
                score += gap * 2000;

                if (gap == 0) score -= 3000;
            }
        }

        return score;
    }

    /// <summary>
    /// En az çakışmalı slotu bulur (zorla yerleştirme).
    /// Sadece gerçek KAPALI slotlar engellenir.
    /// </summary>
    private static (int day, int hour)? FindLeastConflictSlot(
        DistributionBlock block,
        List<DistributionBlock> allBlocks,
        Dictionary<int, Teacher> teachers,
        Dictionary<int, SchoolClass> classes,
        Dictionary<int, OrtakMekan> rooms,
        int maxDays,
        int maxHours)
    {
        (int day, int hour)? best = null;
        int bestConflicts = int.MaxValue;
        int bestScore = int.MaxValue;

        for (int d = 1; d <= maxDays; d++)
        {
            for (int h = 1; h <= maxHours - block.BlockDuration + 1; h++)
            {
                // Gerçek KAPALI kontrolü
                if (IsAnySlotTrulyClosed(block, d, h, teachers, classes))
                    continue;

                int conflicts = CountConflicts(block, d, h, allBlocks);
                int score = CalculateSlotScore(block, d, h, allBlocks, maxHours);

                if (conflicts < bestConflicts || (conflicts == bestConflicts && score < bestScore))
                {
                    bestConflicts = conflicts;
                    bestScore = score;
                    best = (d, h);
                }
            }
        }

        return best;
    }

    private static int CountConflicts(DistributionBlock block, int day, int hour, List<DistributionBlock> allBlocks)
    {
        int count = 0;
        for (int i = 0; i < block.BlockDuration; i++)
        {
            int checkHour = hour + i;

            foreach (var other in allBlocks)
            {
                if (other.Id == block.Id || other.Day != day) continue;
                if (!HoursOverlap(other.Hour, other.BlockDuration, checkHour, 1)) continue;

                if (other.TeacherIds.Intersect(block.TeacherIds).Any()) count++;
                if (other.ClassId == block.ClassId) count++;
                if (other.GetOrtakMekanIds().Intersect(block.GetOrtakMekanIds()).Any()) count++;
            }
        }
        return count;
    }
}
