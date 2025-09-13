// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using static LibReplanetizer.DataFunctions;

namespace GeometrySwapper
{
    /// <summary>
    /// Diagnostic tool for analyzing ship camera indices and surrounding unknown LevelVariables fields
    /// to investigate potential third camera control cuboids
    /// </summary>
    public static class ShipCameraDiagnostics
    {
        /// <summary>
        /// Analyzes a level's LevelVariables for ship camera-related data and unknown fields
        /// </summary>
        /// <param name="level">The level to analyze</param>
        /// <param name="levelName">Name/label for the level</param>
        /// <param name="outputPath">Optional path to save detailed analysis file</param>
        public static void AnalyzeShipCameraData(Level level, string levelName, string outputPath = null)
        {
            Console.WriteLine($"\n=== Ship Camera Analysis for {levelName} ===");

            if (level.levelVariables == null)
            {
                Console.WriteLine("❌ No LevelVariables found in this level");
                return;
            }

            var lv = level.levelVariables;

            // Basic ship camera data
            Console.WriteLine($"Ship Path ID: {lv.shipPathID}");
            Console.WriteLine($"Ship Camera Start ID: {lv.shipCameraStartID}");
            Console.WriteLine($"Ship Camera End ID: {lv.shipCameraEndID}");

            // Memory addresses based on game type
            Console.WriteLine($"\nMemory Layout Analysis (Game: {level.game.num}):");

            if (level.game.num == 1) // RC1
            {
                Console.WriteLine("RC1 Memory Layout:");
                Console.WriteLine($"  0x3C: shipPathID = {lv.shipPathID}");
                Console.WriteLine($"  0x40: shipCameraStartID = {lv.shipCameraStartID}");
                Console.WriteLine($"  0x44: shipCameraEndID = {lv.shipCameraEndID}");
                Console.WriteLine($"  0x48: off48 = {lv.off48} (0x{lv.off48:X8}) - Always 0?");
                Console.WriteLine($"  0x4C: off4C = {lv.off4C} (0x{lv.off4C:X8}) - Always 0?");

                // Check if off48 or off4C could be a third camera index
                if (lv.off48 != 0)
                {
                    Console.WriteLine($"  ⚠️ off48 is non-zero! Could be third camera index: {lv.off48}");
                }
                if (lv.off4C != 0)
                {
                    Console.WriteLine($"  ⚠️ off4C is non-zero! Could be third camera index: {lv.off4C}");
                }
            }
            else if (level.game.num >= 2) // RC2/RC3/DL
            {
                Console.WriteLine("RC2+ Memory Layout:");
                Console.WriteLine($"  0x4C: shipPathID = {lv.shipPathID}");
                Console.WriteLine($"  0x50: shipCameraStartID = {lv.shipCameraStartID}");
                Console.WriteLine($"  0x54: shipCameraEndID = {lv.shipCameraEndID}");
                Console.WriteLine($"  0x58: off58 = {lv.off58} (0x{lv.off58:X8}) - Always 0?");

                if (level.game.num == 4) // Deadlocked
                {
                    Console.WriteLine($"  0x5C: off5C = {lv.off5C} (0x{lv.off5C:X8})");
                    Console.WriteLine($"  0x60: off60 = {lv.off60} (0x{lv.off60:X8})");
                    Console.WriteLine($"  0x68: off68 = {lv.off68} (0x{lv.off68:X8})");
                    Console.WriteLine($"  0x6C: off6C = {lv.off6C} (0x{lv.off6C:X8})");
                    Console.WriteLine($"  0x70: off70 = {lv.off70} (0x{lv.off70:X8})");
                    Console.WriteLine($"  0x78: off78 = {lv.off78} (0x{lv.off78:X8})");
                    Console.WriteLine($"  0x7C: off7C = {lv.off7C} (0x{lv.off7C:X8})");

                    // Check potential third camera indices in DL
                    if (lv.off58 != 0)
                        Console.WriteLine($"  ⚠️ off58 is non-zero! Could be third camera index: {lv.off58}");
                    if (lv.off5C != 0)
                        Console.WriteLine($"  ⚠️ off5C is non-zero! Could be third camera index: {lv.off5C}");
                    if (lv.off60 != 0)
                        Console.WriteLine($"  ⚠️ off60 is non-zero! Could be third camera index: {lv.off60}");
                }
                else
                {
                    Console.WriteLine($"  0x78: off78 = {lv.off78} (0x{lv.off78:X8})");
                    Console.WriteLine($"  0x7C: off7C = {lv.off7C} (0x{lv.off7C:X8})");
                    Console.WriteLine($"  0x80: off80 = {lv.off80} (0x{lv.off80:X8})");
                    Console.WriteLine($"  0x84: off84 = {lv.off84} (0x{lv.off84:X8})");

                    // Check potential third camera indices in RC2/RC3
                    if (lv.off58 != 0)
                        Console.WriteLine($"  ⚠️ off58 is non-zero! Could be third camera index: {lv.off58}");
                    if (lv.off78 != 0)
                        Console.WriteLine($"  ⚠️ off78 is non-zero! Could be third camera index: {lv.off78}");
                    if (lv.off7C != 0)
                        Console.WriteLine($"  ⚠️ off7C is non-zero! Could be third camera index: {lv.off7C}");
                }
            }

            // Check if the ship camera IDs correspond to actual cuboids
            if (level.cuboids != null)
            {
                Console.WriteLine($"\nCuboid Verification (Total cuboids: {level.cuboids.Count}):");

                var shipCameraIds = new[] { lv.shipCameraStartID, lv.shipCameraEndID }
                    .Where(id => id >= 0).ToList();

                foreach (int camId in shipCameraIds)
                {
                    var cuboid = level.cuboids.FirstOrDefault(c => c.id == camId);
                    if (cuboid != null)
                    {
                        Console.WriteLine($"  ✅ Cuboid {camId} found: Position=({cuboid.position.X:F2}, {cuboid.position.Y:F2}, {cuboid.position.Z:F2})");
                    }
                    else
                    {
                        Console.WriteLine($"  ❌ Cuboid {camId} NOT found in level");
                    }
                }

                // Check if any unknown field values might correspond to cuboids
                CheckPotentialCuboidReferences(level, levelName);
            }

            // Check spline references
            if (level.splines != null && lv.shipPathID >= 0)
            {
                Console.WriteLine($"\nSpline Verification (Total splines: {level.splines.Count}):");
                var spline = level.splines.FirstOrDefault(s => s.id == lv.shipPathID);
                if (spline != null)
                {
                    Console.WriteLine($"  ✅ Ship path spline {lv.shipPathID} found: {spline.GetVertexCount()} vertices");
                }
                else
                {
                    Console.WriteLine($"  ❌ Ship path spline {lv.shipPathID} NOT found in level");
                }
            }

            // Save detailed analysis to file if requested
            if (!string.IsNullOrEmpty(outputPath))
            {
                SaveDetailedAnalysis(level, levelName, outputPath);
            }
        }

        /// <summary>
        /// Checks if any unknown field values might correspond to existing cuboids
        /// </summary>
        private static void CheckPotentialCuboidReferences(Level level, string levelName)
        {
            var lv = level.levelVariables;
            var existingCuboidIds = level.cuboids.Select(c => c.id).ToHashSet();

            Console.WriteLine("\nChecking unknown fields for potential cuboid references:");

            // List of unknown fields that could potentially be cuboid IDs
            var potentialCuboidFields = new List<(string name, int value)>();

            if (level.game.num == 1) // RC1
            {
                potentialCuboidFields.Add(("off48", lv.off48));
                potentialCuboidFields.Add(("off4C", lv.off4C));
            }
            else if (level.game.num == 4) // Deadlocked
            {
                potentialCuboidFields.Add(("off58", lv.off58));
                potentialCuboidFields.Add(("off5C", lv.off5C));
                potentialCuboidFields.Add(("off60", lv.off60));
                potentialCuboidFields.Add(("off68", lv.off68));
                potentialCuboidFields.Add(("off6C", lv.off6C));
                potentialCuboidFields.Add(("off70", lv.off70));
                potentialCuboidFields.Add(("off78", lv.off78));
                potentialCuboidFields.Add(("off7C", lv.off7C));
            }
            else // RC2/RC3
            {
                potentialCuboidFields.Add(("off58", lv.off58));
                potentialCuboidFields.Add(("off78", lv.off78));
                potentialCuboidFields.Add(("off7C", lv.off7C));
                potentialCuboidFields.Add(("off80", lv.off80));
                potentialCuboidFields.Add(("off84", lv.off84));
            }

            bool foundPotentialThirdCamera = false;
            foreach (var (name, value) in potentialCuboidFields)
            {
                if (value > 0 && existingCuboidIds.Contains(value))
                {
                    var cuboid = level.cuboids.First(c => c.id == value);
                    Console.WriteLine($"  🎯 {name} = {value} corresponds to existing cuboid!");
                    Console.WriteLine($"      Position: ({cuboid.position.X:F2}, {cuboid.position.Y:F2}, {cuboid.position.Z:F2})");
                    Console.WriteLine($"      ⭐ POTENTIAL THIRD CAMERA CUBOID! ⭐");
                    foundPotentialThirdCamera = true;
                }
                else if (value > 0)
                {
                    Console.WriteLine($"  ⚠️ {name} = {value} (non-zero but no matching cuboid)");
                }
                else
                {
                    Console.WriteLine($"  ✓ {name} = {value} (zero)");
                }
            }

            if (!foundPotentialThirdCamera)
            {
                Console.WriteLine("  No potential third camera cuboid references found in unknown fields.");
            }
        }

        /// <summary>
        /// Saves detailed analysis to a file
        /// </summary>
        private static void SaveDetailedAnalysis(Level level, string levelName, string outputPath)
        {
            Directory.CreateDirectory(outputPath);
            string filePath = Path.Combine(outputPath, $"{levelName}_ship_camera_analysis.txt");

            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine($"Ship Camera Analysis for {levelName}");
                writer.WriteLine($"Generated: {DateTime.Now}");
                writer.WriteLine($"Game Type: {level.game.num}");
                writer.WriteLine("=".PadRight(50, '='));
                writer.WriteLine();

                var lv = level.levelVariables;

                writer.WriteLine("BASIC SHIP CAMERA DATA:");
                writer.WriteLine($"Ship Path ID: {lv.shipPathID}");
                writer.WriteLine($"Ship Camera Start ID: {lv.shipCameraStartID}");
                writer.WriteLine($"Ship Camera End ID: {lv.shipCameraEndID}");
                writer.WriteLine();

                writer.WriteLine("ALL UNKNOWN FIELDS:");
                writer.WriteLine($"off48: {lv.off48} (0x{lv.off48:X8})");
                writer.WriteLine($"off4C: {lv.off4C} (0x{lv.off4C:X8})");
                writer.WriteLine($"off58: {lv.off58} (0x{lv.off58:X8})");
                writer.WriteLine($"off5C: {lv.off5C} (0x{lv.off5C:X8})");
                writer.WriteLine($"off60: {lv.off60} (0x{lv.off60:X8})");
                writer.WriteLine($"off64: {lv.off64:F6}");
                writer.WriteLine($"off68: {lv.off68} (0x{lv.off68:X8})");
                writer.WriteLine($"off6C: {lv.off6C} (0x{lv.off6C:X8})");
                writer.WriteLine($"off70: {lv.off70} (0x{lv.off70:X8})");
                writer.WriteLine($"off74: {lv.off74:F6}");
                writer.WriteLine($"off78: {lv.off78} (0x{lv.off78:X8})");
                writer.WriteLine($"off7C: {lv.off7C} (0x{lv.off7C:X8})");
                writer.WriteLine($"off80: {lv.off80} (0x{lv.off80:X8})");
                writer.WriteLine($"off84: {lv.off84} (0x{lv.off84:X8})");
                writer.WriteLine($"off98: {lv.off98} (0x{lv.off98:X8})");
                writer.WriteLine($"off9C: {lv.off9C} (0x{lv.off9C:X8})");
                writer.WriteLine($"off100: {lv.off100} (0x{lv.off100:X8})");
                writer.WriteLine();

                writer.WriteLine("CUBOID ANALYSIS:");
                if (level.cuboids != null)
                {
                    writer.WriteLine($"Total cuboids in level: {level.cuboids.Count}");
                    writer.WriteLine("All cuboid IDs and positions:");

                    foreach (var cuboid in level.cuboids.OrderBy(c => c.id))
                    {
                        writer.WriteLine($"  Cuboid {cuboid.id}: Position=({cuboid.position.X:F2}, {cuboid.position.Y:F2}, {cuboid.position.Z:F2})");
                    }
                }
                else
                {
                    writer.WriteLine("No cuboids found in level");
                }

                writer.WriteLine();
                writer.WriteLine("RAW LEVEL VARIABLES BYTE DATA:");
                if (lv != null)
                {
                    try
                    {
                        byte[] rawData = lv.Serialize(level.game);
                        writer.WriteLine($"LevelVariables byte size: {rawData.Length}");
                        writer.WriteLine("Hex dump:");

                        for (int i = 0; i < rawData.Length; i += 16)
                        {
                            writer.Write($"{i:X4}: ");

                            // Hex bytes
                            for (int j = 0; j < 16 && i + j < rawData.Length; j++)
                            {
                                writer.Write($"{rawData[i + j]:X2} ");
                            }

                            // ASCII representation
                            writer.Write(" | ");
                            for (int j = 0; j < 16 && i + j < rawData.Length; j++)
                            {
                                char c = (char) rawData[i + j];
                                writer.Write(char.IsControl(c) ? '.' : c);
                            }

                            writer.WriteLine();
                        }
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine($"Error serializing LevelVariables: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"✅ Detailed analysis saved to: {filePath}");
        }

        /// <summary>
        /// Compares ship camera data between multiple levels
        /// </summary>
        /// <param name="levels">List of levels to compare with their names</param>
        /// <param name="outputPath">Path to save comparison report</param>
        public static void CompareShipCameraData(List<(string name, Level level)> levels, string outputPath)
        {
            Directory.CreateDirectory(outputPath);
            string filePath = Path.Combine(outputPath, "ship_camera_comparison.csv");

            using (var writer = new StreamWriter(filePath))
            {
                // CSV header
                writer.WriteLine("LevelName,GameType,ShipPathID,ShipCameraStartID,ShipCameraEndID," +
                                "off48,off4C,off58,off5C,off60,off68,off6C,off70,off78,off7C,off80,off84," +
                                "CuboidCount,StartCuboidExists,EndCuboidExists,PotentialThirdCameraField,PotentialThirdCameraValue");

                foreach (var (name, level) in levels)
                {
                    if (level.levelVariables == null)
                    {
                        writer.WriteLine($"{name},UNKNOWN,N/A,N/A,N/A,N/A,N/A,N/A,N/A,N/A,N/A,N/A,N/A,N/A,N/A,N/A,N/A,0,false,false,none,0");
                        continue;
                    }

                    var lv = level.levelVariables;
                    var cuboidIds = level.cuboids?.Select(c => c.id).ToHashSet() ?? new HashSet<int>();

                    bool startExists = cuboidIds.Contains(lv.shipCameraStartID);
                    bool endExists = cuboidIds.Contains(lv.shipCameraEndID);

                    // Look for potential third camera
                    string thirdCameraField = "none";
                    int thirdCameraValue = 0;

                    var potentialFields = new (string name, int value)[]
                    {
                        ("off48", lv.off48),
                        ("off4C", lv.off4C),
                        ("off58", lv.off58),
                        ("off5C", lv.off5C),
                        ("off60", lv.off60),
                        ("off68", lv.off68),
                        ("off6C", lv.off6C),
                        ("off70", lv.off70),
                        ("off78", lv.off78),
                        ("off7C", lv.off7C),
                        ("off80", lv.off80),
                        ("off84", lv.off84)
                    };

                    foreach (var (fieldName, value) in potentialFields)
                    {
                        if (value > 0 && cuboidIds.Contains(value))
                        {
                            thirdCameraField = fieldName;
                            thirdCameraValue = value;
                            break;
                        }
                    }

                    writer.WriteLine($"{name},{level.game.num},{lv.shipPathID},{lv.shipCameraStartID},{lv.shipCameraEndID}," +
                                    $"{lv.off48},{lv.off4C},{lv.off58},{lv.off5C},{lv.off60},{lv.off68},{lv.off6C},{lv.off70}," +
                                    $"{lv.off78},{lv.off7C},{lv.off80},{lv.off84}," +
                                    $"{level.cuboids?.Count ?? 0},{startExists},{endExists},{thirdCameraField},{thirdCameraValue}");
                }
            }

            Console.WriteLine($"✅ Ship camera comparison saved to: {filePath}");
        }

        /// <summary>
        /// Interactive version for console use
        /// </summary>
        public static void RunShipCameraDiagnosticsInteractive()
        {
            Console.WriteLine("\n==== Ship Camera Diagnostics Tool ====");
            Console.WriteLine("This tool analyzes LevelVariables for ship camera data and potential third camera cuboids.");
            Console.WriteLine();

            Console.WriteLine("Select analysis mode:");
            Console.WriteLine("1. Analyze single level");
            Console.WriteLine("2. Compare multiple levels");
            Console.WriteLine("3. Quick analysis of current swap levels");
            Console.Write("> ");

            string choice = Console.ReadLine()?.Trim() ?? "1";

            switch (choice)
            {
                case "1":
                    AnalyzeSingleLevelInteractive();
                    break;
                case "2":
                    CompareMultipleLevelsInteractive();
                    break;
                case "3":
                    QuickAnalysisOfSwapLevels();
                    break;
                default:
                    AnalyzeSingleLevelInteractive();
                    break;
            }
        }

        private static void AnalyzeSingleLevelInteractive()
        {
            Console.WriteLine("\nEnter path to level engine.ps3 file:");
            Console.Write("> ");
            string levelPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(levelPath) || !File.Exists(levelPath))
            {
                Console.WriteLine("❌ Invalid level path");
                return;
            }

            Console.WriteLine("\nEnter output directory for detailed analysis (optional, press Enter to skip):");
            Console.Write("> ");
            string outputPath = Console.ReadLine()?.Trim();

            try
            {
                Level level = new Level(levelPath);
                string levelName = Path.GetFileNameWithoutExtension(Path.GetDirectoryName(levelPath) ?? "Unknown");

                AnalyzeShipCameraData(level, levelName, outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading level: {ex.Message}");
            }
        }

        private static void CompareMultipleLevelsInteractive()
        {
            var levels = new List<(string, Level)>();

            Console.WriteLine("\nEnter level paths (press Enter with empty path to finish):");

            int levelCount = 1;
            while (true)
            {
                Console.Write($"Level {levelCount} path: ");
                string path = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(path))
                    break;

                if (!File.Exists(path))
                {
                    Console.WriteLine("❌ File not found, skipping");
                    continue;
                }

                try
                {
                    Level level = new Level(path);
                    string name = Path.GetFileNameWithoutExtension(Path.GetDirectoryName(path) ?? $"Level{levelCount}");
                    levels.Add((name, level));
                    Console.WriteLine($"✅ Loaded {name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error loading {path}: {ex.Message}");
                }

                levelCount++;
            }

            if (levels.Count < 2)
            {
                Console.WriteLine("❌ Need at least 2 levels to compare");
                return;
            }

            Console.WriteLine("\nEnter output directory for comparison report:");
            Console.Write("> ");
            string outputPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = Environment.CurrentDirectory;
            }

            // Analyze each level individually
            foreach (var (name, level) in levels)
            {
                AnalyzeShipCameraData(level, name, outputPath);
            }

            // Generate comparison report
            CompareShipCameraData(levels, outputPath);
        }

        private static void QuickAnalysisOfSwapLevels()
        {
            // Use the default paths from the main geometry swapper
            var defaultPaths = new[]
            {
                (@"C:\Users\Ryan_\Downloads\temp\Oltanis_RaC1\engine.ps3", "RC1_Oltanis"),
                (@"C:\Users\Ryan_\Downloads\temp\Insomniac_Museum\engine.ps3", "RC2_Insomniac_Museum"),
                (@"C:\Users\Ryan_\Downloads\temp\Damosel\engine.ps3", "RC2_Damosel")
            };

            var levels = new List<(string, Level)>();

            foreach (var (path, name) in defaultPaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        Level level = new Level(path);
                        levels.Add((name, level));
                        Console.WriteLine($"✅ Loaded {name}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error loading {name}: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"⚠️ Level not found: {name} at {path}");
                }
            }

            if (levels.Count == 0)
            {
                Console.WriteLine("❌ No levels found at default paths");
                return;
            }

            string outputPath = @"C:\Users\Ryan_\Downloads\temp\ship_camera_diagnostics";
            Directory.CreateDirectory(outputPath);

            Console.WriteLine($"\nRunning quick analysis on {levels.Count} levels...");

            // Analyze each level
            foreach (var (name, level) in levels)
            {
                AnalyzeShipCameraData(level, name, outputPath);
            }

            // Generate comparison if we have multiple levels
            if (levels.Count > 1)
            {
                CompareShipCameraData(levels, outputPath);
            }

            Console.WriteLine($"\nAnalysis complete! Results saved to: {outputPath}");
        }
    }
}
