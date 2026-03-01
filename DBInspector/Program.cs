using System;
using Microsoft.Data.Sqlite;
using System.IO;

namespace DBInspector
{
    class Program
    {
        static void Main(string[] args)
        {
            var folder = @"c:\Users\fth\Desktop\ders.dagıtım"; // Root folder
            var dbPath = Path.Combine(folder, "ders_dagitim.sqlite");
            
            if (!File.Exists(dbPath))
               dbPath = Path.Combine(folder, "ders_dagitim.db");

            Console.WriteLine($"Inspecting: {dbPath}");

            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();

                // 1. Ogretmen Columns
                Console.WriteLine("\n--- Ogretmen Columns ---");
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(ogretmen)";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Console.WriteLine($"{reader["name"]} ({reader["type"]})");
                        }
                    }
                }

                // 2. Ogretmen d_1_1 sample
                Console.WriteLine("\n--- Ogretmen Data Sample (d_1_1, d_1_2) ---");
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, ad_soyad, d_1_1, d_1_2 FROM ogretmen LIMIT 1";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                             Console.WriteLine($"ID: {reader["id"]}, Name: {reader["ad_soyad"]}");
                             Console.WriteLine($"d_1_1: '{reader["d_1_1"]}'");
                             Console.WriteLine($"d_1_2: '{reader["d_1_2"]}'");
                        }
                    }
                }

                // 3. Atama Schema
                Console.WriteLine("\n--- Atama Schema ---");
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(atama)";
                    using (var reader = cmd.ExecuteReader())
                    {
                         while (reader.Read())
                        {
                            Console.WriteLine($"{reader["name"]} ({reader["type"]})");
                        }
                    }
                }
                
                // 4. Dagitim Bloklari Schema
                Console.WriteLine("\n--- Dagitim Bloklari Schema ---");
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(dagitim_bloklari)";
                    using (var reader = cmd.ExecuteReader())
                    {
                         while (reader.Read())
                        {
                            Console.WriteLine($"{reader["name"]} ({reader["type"]})");
                        }
                    }
                }
            }
        }
    }
}
