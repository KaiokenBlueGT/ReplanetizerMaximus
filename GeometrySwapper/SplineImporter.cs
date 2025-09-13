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
    /// Handles importing splines from RC1 levels to RC2 levels, excluding those used for ship path animation
    /// </summary>
    public static class SplineImporter
    {
        /// <summary>
        /// Options for spline import behavior
        /// </summary>
        [Flags]
        public enum SplineImportOptions
        {
            None = 0,
            ImportAll = 1,
            SkipShipPathSplines = 2,
            ReplaceExisting = 4,
            PreserveOriginalIDs = 8,

            // Common combinations
            Default = ImportAll | SkipShipPathSplines | PreserveOriginalIDs,
            ReplaceAll = ImportAll | SkipShipPathSplines | ReplaceExisting | PreserveOriginalIDs
        }

        /// <summary>
        /// Imports all splines from RC1 source level to RC2 target level, excluding ship path splines
        /// </summary>
        /// <param name="targetLevel">The RC2 level to import splines into</param>
        /// <param name="sourceLevel">The RC1 level to import splines from</param>
        /// <param name="options">Options controlling import behavior</param>
        /// <returns>True if the import was successful</returns>
        public static bool ImportSplinesFromRC1(Level targetLevel, Level sourceLevel, SplineImportOptions options = SplineImportOptions.Default)
        {
            if (targetLevel == null || sourceLevel == null)
            {
                Console.WriteLine("❌ Cannot import splines: Invalid level data");
                return false;
            }

            Console.WriteLine("\n==== Importing RC1 Splines ====");
            Console.WriteLine($"Source Level: {sourceLevel.path ?? "Not set"}");
            Console.WriteLine($"Target Level: {targetLevel.path ?? "Not set"}");

            try
            {
                // Ensure target level has splines list
                if (targetLevel.splines == null)
                    targetLevel.splines = new List<Spline>();

                // Check if source level has any splines
                if (sourceLevel.splines == null || sourceLevel.splines.Count == 0)
                {
                    Console.WriteLine("⚠️ Source level has no splines to import");
                    return true; // Not an error, just nothing to do
                }

                Console.WriteLine($"Found {sourceLevel.splines.Count} splines in source level");
                Console.WriteLine($"Target level currently has {targetLevel.splines.Count} splines");

                // Get IDs of splines used for ship path animation if we should skip them
                HashSet<int> shipPathSplineIds = new HashSet<int>();
                if (options.HasFlag(SplineImportOptions.SkipShipPathSplines))
                {
                    if (sourceLevel.levelVariables != null)
                    {
                        if (sourceLevel.levelVariables.shipPathID >= 0)
                            shipPathSplineIds.Add(sourceLevel.levelVariables.shipPathID);

                        if (shipPathSplineIds.Count > 0)
                        {
                            Console.WriteLine($"Excluding {shipPathSplineIds.Count} splines used for ship path animation: {string.Join(", ", shipPathSplineIds)}");
                        }
                    }
                }

                // Clear existing splines if ReplaceExisting is enabled
                if (options.HasFlag(SplineImportOptions.ReplaceExisting))
                {
                    Console.WriteLine($"Replacing existing {targetLevel.splines.Count} splines");
                    targetLevel.splines.Clear();
                }

                // Prepare ID mapping strategy
                Dictionary<int, int> idMapping = new Dictionary<int, int>();
                HashSet<int> usedIds = new HashSet<int>(targetLevel.splines.Select(s => s.id));
                int nextAvailableId = 0;

                // Find next available ID if we need to assign new IDs
                if (!options.HasFlag(SplineImportOptions.PreserveOriginalIDs) || usedIds.Count > 0)
                {
                    while (usedIds.Contains(nextAvailableId))
                    {
                        nextAvailableId++;
                    }
                }

                Console.WriteLine($"Starting spline ID assignment from {nextAvailableId}");

                // Import splines from source level
                int importedCount = 0;
                int skippedCount = 0;
                int conflictCount = 0;

                foreach (var sourceSpline in sourceLevel.splines)
                {
                    // Skip ship path splines if requested
                    if (options.HasFlag(SplineImportOptions.SkipShipPathSplines) &&
                        shipPathSplineIds.Contains(sourceSpline.id))
                    {
                        skippedCount++;
                        Console.WriteLine($"  Skipping spline {sourceSpline.id} (used for ship path animation)");
                        continue;
                    }

                    // Determine the ID for the new spline
                    int newId = sourceSpline.id;

                    if (options.HasFlag(SplineImportOptions.PreserveOriginalIDs))
                    {
                        // Try to preserve original ID if possible
                        if (usedIds.Contains(sourceSpline.id))
                        {
                            // Original ID is taken, find next available
                            while (usedIds.Contains(nextAvailableId))
                            {
                                nextAvailableId++;
                            }
                            newId = nextAvailableId++;
                            conflictCount++;
                            Console.WriteLine($"  ID conflict: spline {sourceSpline.id} assigned new ID {newId}");
                        }
                        else
                        {
                            // Can preserve original ID
                            newId = sourceSpline.id;
                        }
                    }
                    else
                    {
                        // Always assign new sequential IDs
                        while (usedIds.Contains(nextAvailableId))
                        {
                            nextAvailableId++;
                        }
                        newId = nextAvailableId++;
                    }

                    // Create a copy of the spline
                    Spline newSpline = CloneSpline(sourceSpline, newId);
                    targetLevel.splines.Add(newSpline);
                    usedIds.Add(newId);
                    idMapping[sourceSpline.id] = newId;
                    importedCount++;

                    if (sourceSpline.id == newId)
                    {
                        Console.WriteLine($"  Imported spline {sourceSpline.id} (preserved original ID, {newSpline.GetVertexCount()} vertices)");
                    }
                    else
                    {
                        Console.WriteLine($"  Imported spline {sourceSpline.id} as new ID {newId} ({newSpline.GetVertexCount()} vertices)");
                    }
                }

                Console.WriteLine($"✅ Successfully imported {importedCount} splines from RC1 source");
                if (skippedCount > 0)
                {
                    Console.WriteLine($"  Skipped {skippedCount} splines (ship path animation)");
                }
                if (conflictCount > 0)
                {
                    Console.WriteLine($"  Resolved {conflictCount} ID conflicts by assigning new IDs");
                }
                Console.WriteLine($"Target level now has {targetLevel.splines.Count} total splines");

                // Validate splines after import
                ValidateImportedSplines(targetLevel);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during spline import: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Creates a complete clone of a spline with a new ID
        /// </summary>
        /// <param name="sourceSpline">The spline to clone</param>
        /// <param name="newId">The ID to assign to the new spline</param>
        /// <returns>A new spline instance that is a copy of the source spline</returns>
        private static Spline CloneSpline(Spline sourceSpline, int newId)
        {
            try
            {
                // Create a new vertex buffer with the same data
                float[] newVertexBuffer = new float[sourceSpline.vertexBuffer.Length];
                Array.Copy(sourceSpline.vertexBuffer, newVertexBuffer, sourceSpline.vertexBuffer.Length);

                // Create new w values array, ensuring it matches the vertex count
                int vertexCount = sourceSpline.GetVertexCount();
                float[] newWVals = new float[vertexCount];

                // Handle the case where wVals might be a different length than the vertex count
                if (sourceSpline.wVals.Length == vertexCount)
                {
                    Array.Copy(sourceSpline.wVals, newWVals, sourceSpline.wVals.Length);
                }
                else if (sourceSpline.wVals.Length < vertexCount)
                {
                    // Copy what we have and extrapolate the rest
                    Array.Copy(sourceSpline.wVals, newWVals, sourceSpline.wVals.Length);
                    for (int i = sourceSpline.wVals.Length; i < vertexCount; i++)
                    {
                        newWVals[i] = i > 0 ? newWVals[i - 1] + 0.1f : 0f;
                    }
                }
                else
                {
                    // Just copy what we need
                    Array.Copy(sourceSpline.wVals, newWVals, vertexCount);
                }

                // Create a new spline with the copied data
                Spline newSpline = new Spline(newId, newVertexBuffer)
                {
                    wVals = newWVals,
                    position = sourceSpline.position,
                    rotation = sourceSpline.rotation,
                    scale = sourceSpline.scale,
                    reflection = sourceSpline.reflection
                };

                // Ensure the transform matrix is updated
                newSpline.UpdateTransformMatrix();

                return newSpline;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error cloning spline {sourceSpline.id}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Validates imported splines for potential issues
        /// </summary>
        /// <param name="level">The level to validate</param>
        private static void ValidateImportedSplines(Level level)
        {
            if (level.splines == null || level.splines.Count == 0)
                return;

            Console.WriteLine("\n=== Validating Imported Splines ===");

            // Check for duplicate IDs
            var duplicateIds = level.splines
                .GroupBy(s => s.id)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateIds.Any())
            {
                Console.WriteLine($"⚠️ Found {duplicateIds.Count} duplicate spline ID(s):");
                foreach (var group in duplicateIds)
                {
                    Console.WriteLine($"  ID {group.Key}: {group.Count()} occurrences");
                }
            }

            // Check for splines with no vertices
            var emptySplines = level.splines.Where(s => s.GetVertexCount() == 0).ToList();
            if (emptySplines.Count > 0)
            {
                Console.WriteLine($"⚠️ Found {emptySplines.Count} spline(s) with no vertices:");
                foreach (var spline in emptySplines)
                {
                    Console.WriteLine($"  Spline ID {spline.id}");
                }
            }

            // Check for vertex buffer/wVals mismatches
            var inconsistentSplines = level.splines
                .Where(s => s.GetVertexCount() != s.wVals.Length && s.GetVertexCount() > 0)
                .ToList();

            if (inconsistentSplines.Count > 0)
            {
                Console.WriteLine($"⚠️ Found {inconsistentSplines.Count} spline(s) with mismatched vertex/W value counts:");
                foreach (var spline in inconsistentSplines)
                {
                    Console.WriteLine($"  Spline ID {spline.id}: {spline.GetVertexCount()} vertices, {spline.wVals.Length} W values");
                }
            }

            // Show summary
            var minVertices = level.splines.Min(s => s.GetVertexCount());
            var maxVertices = level.splines.Max(s => s.GetVertexCount());
            var avgVertices = level.splines.Average(s => s.GetVertexCount());

            Console.WriteLine($"Spline statistics: Min={minVertices}, Max={maxVertices}, Avg={avgVertices:F1} vertices");
            Console.WriteLine("✅ Spline validation complete");
        }

        /// <summary>
        /// Interactive version of the spline importer
        /// </summary>
        /// <returns>True if the operation was successful</returns>
        public static bool ImportSplinesInteractive()
        {
            Console.WriteLine("\n==== Import RC1 Splines ====");

            // Get source level path
            Console.WriteLine("Enter path to the RC1 source level engine.ps3 file:");
            Console.Write("> ");
            string sourcePath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                Console.WriteLine("❌ Invalid RC1 source level path");
                return false;
            }

            // Get target level path
            Console.WriteLine("\nEnter path to the target RC2 level engine.ps3 file:");
            Console.Write("> ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                Console.WriteLine("❌ Invalid target level path");
                return false;
            }

            // Load levels
            Level sourceLevel, targetLevel;
            try
            {
                Console.WriteLine($"\nLoading RC1 source level: {Path.GetFileName(sourcePath)}...");
                sourceLevel = new Level(sourcePath);
                Console.WriteLine($"✅ Successfully loaded source level");

                Console.WriteLine($"\nLoading target level: {Path.GetFileName(targetPath)}...");
                targetLevel = new Level(targetPath);
                Console.WriteLine($"✅ Successfully loaded target level");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading levels: {ex.Message}");
                return false;
            }

            // Get import options
            Console.WriteLine("\nSelect import options:");
            Console.WriteLine("1. Import all splines except ship path ones (default)");
            Console.WriteLine("2. Import all splines including ship path ones");
            Console.WriteLine("3. Replace all existing splines with RC1 ones (except ship path)");
            Console.WriteLine("4. Replace all existing splines with RC1 ones (including ship path)");
            Console.WriteLine("5. Custom options");
            Console.Write("> ");

            string choice = Console.ReadLine()?.Trim() ?? "1";
            SplineImportOptions options;

            switch (choice)
            {
                case "1":
                    options = SplineImportOptions.Default;
                    break;
                case "2":
                    options = SplineImportOptions.ImportAll | SplineImportOptions.PreserveOriginalIDs;
                    break;
                case "3":
                    options = SplineImportOptions.ReplaceAll;
                    break;
                case "4":
                    options = SplineImportOptions.ImportAll | SplineImportOptions.ReplaceExisting | SplineImportOptions.PreserveOriginalIDs;
                    break;
                case "5":
                    options = GetCustomOptions();
                    break;
                default:
                    options = SplineImportOptions.Default;
                    break;
            }

            // Perform the import
            bool success = ImportSplinesFromRC1(targetLevel, sourceLevel, options);

            if (success)
            {
                // Ask if the user wants to save
                Console.Write("\nSave changes to the target level? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLower() == "y")
                {
                    try
                    {
                        Console.WriteLine("Saving target level...");
                        targetLevel.Save(targetPath);
                        Console.WriteLine("✅ Target level saved successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error saving target level: {ex.Message}");
                        return false;
                    }
                }
            }

            return success;
        }

        /// <summary>
        /// Gets custom import options from user input
        /// </summary>
        /// <returns>Custom options based on user input</returns>
        private static SplineImportOptions GetCustomOptions()
        {
            SplineImportOptions options = SplineImportOptions.None;

            Console.WriteLine("\nCustomize import options:");

            if (GetYesNoInput("Import all splines? (Y/N): "))
                options |= SplineImportOptions.ImportAll;

            if (GetYesNoInput("Skip splines used for ship path animation? (Y/N): "))
                options |= SplineImportOptions.SkipShipPathSplines;

            if (GetYesNoInput("Replace existing splines in target level? (Y/N): "))
                options |= SplineImportOptions.ReplaceExisting;

            if (GetYesNoInput("Try to preserve original spline IDs? (Y/N): "))
                options |= SplineImportOptions.PreserveOriginalIDs;

            return options;
        }

        /// <summary>
        /// Helper method for yes/no input
        /// </summary>
        private static bool GetYesNoInput(string prompt)
        {
            Console.Write(prompt);
            string input = Console.ReadLine()?.Trim().ToLower() ?? "";
            return input == "y" || input == "yes";
        }

        /// <summary>
        /// Analyzes splines in a level for diagnostic purposes
        /// </summary>
        /// <param name="level">The level to analyze</param>
        /// <param name="levelName">Name/label for the level</param>
        public static void AnalyzeSplines(Level level, string levelName)
        {
            Console.WriteLine($"\n=== Spline Analysis for {levelName} ===");

            if (level.splines == null || level.splines.Count == 0)
            {
                Console.WriteLine("No splines found in this level");
                return;
            }

            Console.WriteLine($"Total splines: {level.splines.Count}");

            // Show distribution of spline IDs
            var idCounts = level.splines.GroupBy(s => s.id).ToDictionary(g => g.Key, g => g.Count());
            if (idCounts.Values.Any(count => count > 1))
            {
                Console.WriteLine("⚠️ Duplicate spline IDs found:");
                foreach (var kvp in idCounts.Where(kvp => kvp.Value > 1))
                {
                    Console.WriteLine($"  ID {kvp.Key}: {kvp.Value} occurrences");
                }
            }

            // Show ship path spline IDs if available
            if (level.levelVariables != null)
            {
                Console.WriteLine($"Ship path spline ID: {level.levelVariables.shipPathID}");

                if (level.levelVariables.shipPathID >= 0)
                {
                    var shipPathSpline = level.splines.FirstOrDefault(s => s.id == level.levelVariables.shipPathID);
                    if (shipPathSpline != null)
                    {
                        Console.WriteLine($"Ship path spline found: {shipPathSpline.GetVertexCount()} vertices");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ Ship path spline ID {level.levelVariables.shipPathID} not found in splines list");
                    }
                }
            }

            // Show first few splines for reference
            Console.WriteLine("\nFirst few splines:");
            int sampleCount = Math.Min(5, level.splines.Count);
            for (int i = 0; i < sampleCount; i++)
            {
                var spline = level.splines[i];
                Console.WriteLine($"  Spline {i}: ID={spline.id}, Vertices={spline.GetVertexCount()}");
            }

            // Show vertex count statistics
            if (level.splines.Count > 0)
            {
                var vertexCounts = level.splines.Select(s => s.GetVertexCount()).ToList();
                Console.WriteLine($"\nVertex count statistics:");
                Console.WriteLine($"  Min: {vertexCounts.Min()}");
                Console.WriteLine($"  Max: {vertexCounts.Max()}");
                Console.WriteLine($"  Average: {vertexCounts.Average():F1}");
                Console.WriteLine($"  Total vertices across all splines: {vertexCounts.Sum()}");
            }
        }
    }
}
