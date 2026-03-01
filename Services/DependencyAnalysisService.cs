using System;
using System.Collections.Generic;
using System.Linq;
using DersDagitim.Models;
using DersDagitim.Persistence;

namespace DersDagitim.Services
{
    public class DependencyAnalysisService
    {
        public class Node
        {
            public string id { get; set; }
            public string label { get; set; }
            public int size { get; set; }
            public string color { get; set; }
            public double stress { get; set; }
            public int lessonCount { get; set; }
            public int classCount { get; set; }
            public int roomCount { get; set; }
            public int relationCount { get; set; }
        }

        public class Edge
        {
            public string source { get; set; }
            public string target { get; set; }
            public double weight { get; set; }
            public string color { get; set; }
            public string label { get; set; }
            public string type { get; set; } // class, lesson, room
        }

        public class AnalysisData
        {
            public List<Node> nodes { get; set; } = new();
            public List<Edge> edges { get; set; } = new();
            public double overallStrain { get; set; }
        }

        public AnalysisData GetAnalysisData()
        {
            var data = new AnalysisData();
            var repo = new DistributionRepository();
            var teacherRepo = new TeacherRepository();
            var classRepo = new ClassRepository();
            var schoolInfo = new SchoolRepository().GetSchoolInfo();

            var blocks = repo.GetAllBlocks();
            var teachers = teacherRepo.GetAll();
            var classes = classRepo.GetAll();

            int totalPossible = schoolInfo.Days * schoolInfo.DailyLessonCount;

            // 1. Create Nodes (Teachers)
            foreach (var t in teachers)
            {
                var teacherBlocks = blocks.Where(b => b.TeacherIds.Contains(t.Id)).ToList();
                int load = teacherBlocks.Sum(b => b.BlockDuration);
                if (load == 0) continue;

                int classCount = teacherBlocks.Select(b => b.ClassId).Distinct().Count();
                int roomCount = teacherBlocks.SelectMany(b => b.GetOrtakMekanIds()).Distinct().Count();
                int constraintCount = t.Constraints != null ? t.Constraints.Count(c => c.Value == SlotState.Closed) : 0;
                int totalOpenSlots = totalPossible - constraintCount;
                double loadRatio = totalOpenSlots > 0 ? (double)load / totalOpenSlots : 2.0;
                double stress = Math.Min(100, loadRatio * 100);

                data.nodes.Add(new Node
                {
                    id = $"t_{t.Id}",
                    label = t.Name,
                    size = 25 + (int)(stress / 4),
                    stress = stress,
                    lessonCount = load,
                    classCount = classCount,
                    roomCount = roomCount
                });
            }

            // 2. Create Edges (Dependencies)
            var teacherPairs = new Dictionary<(int, int), (double weight, List<string> types, List<string> labels)>();

            // Relationship A: Common Classes
            var classTeacherMap = blocks
                .GroupBy(b => b.ClassId)
                .ToDictionary(g => g.Key, g => g.SelectMany(b => b.TeacherIds).Distinct().ToList());

            foreach (var ct in classTeacherMap)
            {
                var tIds = ct.Value;
                for (int i = 0; i < tIds.Count; i++)
                {
                    for (int j = i + 1; j < tIds.Count; j++)
                    {
                        var key = tIds[i] < tIds[j] ? (tIds[i], tIds[j]) : (tIds[j], tIds[i]);
                        if (!teacherPairs.ContainsKey(key)) teacherPairs[key] = (0, new(), new());
                        
                        var val = teacherPairs[key];
                        val.weight += 1.0;
                        if (!val.types.Contains("class")) val.types.Add("class");
                        string cName = classes.FirstOrDefault(c => c.Id == ct.Key)?.Name ?? $"C:{ct.Key}";
                        val.labels.Add($"Sınıf ({cName})");
                        teacherPairs[key] = val;
                    }
                }
            }

            // Relationship B: Kardeş Blocks (Shared Lesson Group)
            var kardesGroups = blocks.Where(b => b.KardesId > 0).GroupBy(b => b.KardesId);
            foreach (var grp in kardesGroups)
            {
                var tIds = grp.SelectMany(b => b.TeacherIds).Distinct().ToList();
                for (int i = 0; i < tIds.Count; i++)
                {
                    for (int j = i + 1; j < tIds.Count; j++)
                    {
                        var key = tIds[i] < tIds[j] ? (tIds[i], tIds[j]) : (tIds[j], tIds[i]);
                        if (!teacherPairs.ContainsKey(key)) teacherPairs[key] = (0, new(), new());
                        
                        var val = teacherPairs[key];
                        val.weight += 5.0; 
                        if (!val.types.Contains("lesson")) val.types.Add("lesson");
                        val.labels.Add("Kardeş Ders Grubu");
                        teacherPairs[key] = val;
                    }
                }
            }

            // Relationship C: Same Block Teachers (Team Teaching / Ogretmen 1-2-3-...)
            var teamBlocks = blocks.Where(b => b.TeacherIds.Count > 1);
            foreach (var b in teamBlocks)
            {
                var tIds = b.TeacherIds;
                for (int i = 0; i < tIds.Count; i++)
                {
                    for (int j = i + 1; j < tIds.Count; j++)
                    {
                        var key = tIds[i] < tIds[j] ? (tIds[i], tIds[j]) : (tIds[j], tIds[i]);
                        if (!teacherPairs.ContainsKey(key)) teacherPairs[key] = (0, new(), new());

                        var val = teacherPairs[key];
                        val.weight += 10.0; // Critical dependency
                        if (!val.types.Contains("team")) val.types.Add("team");
                        val.labels.Add($"Birlikte Ders ({b.LessonCode})");
                        teacherPairs[key] = val;
                    }
                }
            }

            // Relationship D: Shared Rooms
            var roomTeacherMap = blocks
                .SelectMany(b => b.GetOrtakMekanIds().Select(rid => new { rid, tIds = b.TeacherIds }))
                .GroupBy(x => x.rid)
                .ToDictionary(g => g.Key, g => g.SelectMany(x => x.tIds).Distinct().ToList());

            foreach (var rt in roomTeacherMap)
            {
                var tIds = rt.Value;
                for (int i = 0; i < tIds.Count; i++)
                {
                    for (int j = i + 1; j < tIds.Count; j++)
                    {
                        var key = tIds[i] < tIds[j] ? (tIds[i], tIds[j]) : (tIds[j], tIds[i]);
                        if (!teacherPairs.ContainsKey(key)) teacherPairs[key] = (0, new(), new());

                        var val = teacherPairs[key];
                        val.weight += 1.5;
                        if (!val.types.Contains("room")) val.types.Add("room");
                        val.labels.Add("Ortak Mekan");
                        teacherPairs[key] = val;
                    }
                }
            }

            foreach (var pair in teacherPairs)
            {
                var nodeA = data.nodes.FirstOrDefault(n => n.id == $"t_{pair.Key.Item1}");
                var nodeB = data.nodes.FirstOrDefault(n => n.id == $"t_{pair.Key.Item2}");
                if (nodeA == null || nodeB == null) continue;

                nodeA.relationCount++;
                nodeB.relationCount++;

                string combinedType = string.Join(",", pair.Value.types);
                data.edges.Add(new Edge
                {
                    source = $"t_{pair.Key.Item1}",
                    target = $"t_{pair.Key.Item2}",
                    weight = pair.Value.weight,
                    color = GetEdgeColor(pair.Value.weight),
                    label = string.Join(", ", pair.Value.labels.Distinct()),
                    type = combinedType
                });
            }

            // Final Node Coloring based on relationCount and stress
            foreach(var n in data.nodes)
            {
                n.color = GetNodeColor(n.stress, n.relationCount);
            }

            data.overallStrain = data.nodes.Count > 0 ? data.nodes.Average(n => n.stress) : 0;
            
            return data;
        }

        private string GetNodeColor(double stress, int relationCount)
        {
            // Calculate a composite intensity score (0 to 100)
            // stress is already 0-100. Let's weigh it with relationCount (normalized, e.g. 10 relations is high)
            double intensity = (stress * 0.5) + (Math.Min(10, relationCount) * 5.0);
            
            if (intensity > 85) return "#7f1d1d"; // Dark Red
            if (intensity > 70) return "#ef4444"; // Red
            if (intensity > 55) return "#f97316"; // Orange
            if (intensity > 40) return "#eab308"; // Yellow
            if (intensity > 25) return "#22c55e"; // Green
            return "#3b82f6"; // Blue
        }

        private string GetEdgeColor(double weight)
        {
            if (weight > 10) return "#ef4444"; // Critical (Red)
            if (weight > 5) return "#f43f5e"; // Strong (Rose)
            if (weight > 2) return "#3b82f6"; // Medium (Blue)
            return "#94a3b8"; // Low (Slate)
        }
    }
}
