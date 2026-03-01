using DersDagitim.Persistence;
using DersDagitim.Services;

namespace DersDagitim.Tests;

/// <summary>
/// Simple test to verify scheduling engine works
/// </summary>
public static class SchedulingTest
{
    public static void RunTest(string dbPath)
    {
        Console.WriteLine("=== Dağıtım Motoru Test ===");
        Console.WriteLine();
        
        // Open database
        Console.WriteLine($"📂 Veritabanı açılıyor: {dbPath}");
        if (!DatabaseManager.Shared.OpenDatabase(dbPath))
        {
            Console.WriteLine("❌ Veritabanı açılamadı!");
            return;
        }
        
        // Initialize schema
        Console.WriteLine("📋 Şema kontrol ediliyor...");
        DatabaseSchema.Initialize(DatabaseManager.Shared);
        
        // Load data
        var teacherRepo = new TeacherRepository();
        var classRepo = new ClassRepository();
        var blockRepo = new DistributionBlockRepository();
        
        var teachers = teacherRepo.GetAll();
        var classes = classRepo.GetAll();
        var blocks = blockRepo.GetAll();
        var placedBlocks = blockRepo.GetPlaced();
        var unplacedBlocks = blockRepo.GetUnplaced();
        
        Console.WriteLine();
        Console.WriteLine("📊 Mevcut Durum:");
        Console.WriteLine($"   Öğretmen sayısı: {teachers.Count}");
        Console.WriteLine($"   Sınıf sayısı: {classes.Count}");
        Console.WriteLine($"   Toplam blok sayısı: {blocks.Count}");
        Console.WriteLine($"   Yerleşmiş blok: {placedBlocks.Count}");
        Console.WriteLine($"   Yerleşmemiş blok: {unplacedBlocks.Count}");
        Console.WriteLine();
        
        if (blocks.Count == 0)
        {
            Console.WriteLine("⚠️ Dağıtım bloğu bulunamadı!");
            Console.WriteLine("   Önce sınıf-ders atamaları yapılmalı ve bloklar oluşturulmalı.");
            
            // Check if we have class-lessons
            var clRepo = new ClassLessonRepository();
            var classLessons = clRepo.GetAll();
            Console.WriteLine($"   Sınıf-ders ataması sayısı: {classLessons.Count}");
            
            if (classLessons.Count > 0)
            {
                Console.WriteLine("   📦 Bloklar oluşturuluyor...");
                blockRepo.GenerateBlocksFromAssignments();
                blocks = blockRepo.GetAll();
                Console.WriteLine($"   ✅ {blocks.Count} blok oluşturuldu.");
            }
            else
            {
                Console.WriteLine("   ❌ Sınıf-ders ataması da yok. Dağıtım yapılamaz.");
                return;
            }
        }
        
        if (blocks.Count == 0)
        {
            Console.WriteLine("❌ Dağıtım için blok bulunamadı.");
            return;
        }
        
        // Run the scheduler
        Console.WriteLine("🧠 OR-Tools çizelgeleme motoru başlatılıyor...");
        Console.WriteLine();
        
        var engine = new SchedulingEngine();
        var parameters = new DistributionParameters
        {
            OperationMode = OperationMode.Rebuild,
            MaxTimeInSeconds = 60,
            PlacementMode = PlacementMode.ClearAll
        };
        
        var (success, message) = engine.RunSolver(parameters);
        
        Console.WriteLine();
        if (success)
        {
            Console.WriteLine($"✅ {message}");
            
            // Show results
            var newPlaced = blockRepo.GetPlaced();
            Console.WriteLine();
            Console.WriteLine("📈 Sonuç:");
            Console.WriteLine($"   Yerleştirilen blok: {newPlaced.Count}");
            
            // Show some placements
            Console.WriteLine();
            Console.WriteLine("📋 İlk 10 yerleşim:");
            foreach (var block in newPlaced.Take(10))
            {
                Console.WriteLine($"   {block.LessonCode} (Sınıf {block.ClassId}) -> Gün {block.Day}, Saat {block.Hour}");
            }
        }
        else
        {
            Console.WriteLine($"❌ {message}");
        }
        
        Console.WriteLine();
        Console.WriteLine("=== Test Tamamlandı ===");
    }
}
