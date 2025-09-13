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
    /// Handles importing cuboids from RC1 levels to RC2 levels, excluding those used for ship exit animation
    /// </summary>
    public static class CuboidImporter
    {
        /// <summary>
        /// Options for cuboid import behavior
        /// </summary>
        [Flags]
        public enum CuboidImportOptions
        {
            None = 0,
            ImportAll = 1,
            SkipShipAnimationCuboids = 2,
            ReplaceExisting = 4,
            PreserveOriginalIDs = 8,

            // Common combinations
            Default = ImportAll | SkipShipAnimationCuboids | PreserveOriginalIDs,
            ReplaceAll = ImportAll | SkipShipAnimationCuboids | ReplaceExisting | PreserveOriginalIDs
        }

        /// <summary>
        /// Imports all cuboids from RC1 source level to RC2 target level, excluding ship animation cuboids
        /// </summary>
        /// <param name="targetLevel">The RC2 level to import cuboids into</param>
        /// <param name="sourceLevel">The RC1 level to import cuboids from</param>
        /// <param name="options">Options controlling import behavior</param>
        /// <returns>True if the import was successful</returns>
        public static bool ImportCuboidsFromRC1(Level targetLevel, Level sourceLevel, CuboidImportOptions options = CuboidImportOptions.Default)
        {
            if (targetLevel == null || sourceLevel == null)
            {
                Console.WriteLine("❌ Cannot import cuboids: Invalid level data");
                return false;
            }

            Console.WriteLine("\n==== Importing RC1 Cuboids ====");
            Console.WriteLine($"Source Level: {sourceLevel.path ?? "Not set"}");
            Console.WriteLine($"Target Level: {targetLevel.path ?? "Not set"}");

            try
            {
                // Ensure target level has cuboids list
                if (targetLevel.cuboids == null)
                    targetLevel.cuboids = new List<Cuboid>();

                // Check if source level has any cuboids
                if (sourceLevel.cuboids == null || sourceLevel.cuboids.Count == 0)
                {
                    Console.WriteLine("⚠️ Source level has no cuboids to import");
                    return true; // Not an error, just nothing to do
                }

                Console.WriteLine($"Found {sourceLevel.cuboids.Count} cuboids in source level");
                Console.WriteLine($"Target level currently has {targetLevel.cuboids.Count} cuboids");

                // Get IDs of cuboids used for ship animation if we should skip them
                HashSet<int> shipAnimationCuboidIds = new HashSet<int>();
                if (options.HasFlag(CuboidImportOptions.SkipShipAnimationCuboids))
                {
                    if (sourceLevel.levelVariables != null)
                    {
                        if (sourceLevel.levelVariables.shipCameraStartID >= 0)
                            shipAnimationCuboidIds.Add(sourceLevel.levelVariables.shipCameraStartID);

                        if (sourceLevel.levelVariables.shipCameraEndID >= 0)
                            shipAnimationCuboidIds.Add(sourceLevel.levelVariables.shipCameraEndID);

                        if (shipAnimationCuboidIds.Count > 0)
                        {
                            Console.WriteLine($"Excluding {shipAnimationCuboidIds.Count} cuboids used for ship animation: {string.Join(", ", shipAnimationCuboidIds)}");
                        }
                    }
                }

                // Clear existing cuboids if ReplaceExisting is enabled
                if (options.HasFlag(CuboidImportOptions.ReplaceExisting))
                {
                    Console.WriteLine($"Replacing existing {targetLevel.cuboids.Count} cuboids");
                    targetLevel.cuboids.Clear();
                }

                // Prepare for ID conflict resolution if preserving original IDs
                Dictionary<int, int> idRemapping = new Dictionary<int, int>();
                HashSet<int> usedIds = new HashSet<int>();
                int nextAvailableId = 0;

                if (options.HasFlag(CuboidImportOptions.PreserveOriginalIDs))
                {
                    Console.WriteLine("Preserving RC1 cuboid IDs and resolving conflicts...");
                    
                    // First pass: collect all RC1 cuboid IDs that we want to import
                    HashSet<int> desiredRC1Ids = new HashSet<int>();
                    foreach (var sourceCuboid in sourceLevel.cuboids)
                    {
                        if (options.HasFlag(CuboidImportOptions.SkipShipAnimationCuboids) &&
                            shipAnimationCuboidIds.Contains(sourceCuboid.id))
                        {
                            continue; // Skip ship animation cuboids
                        }
                        desiredRC1Ids.Add(sourceCuboid.id);
                    }

                    // Find conflicts with existing target cuboids
                    var conflictingTargetCuboids = targetLevel.cuboids
                        .Where(c => desiredRC1Ids.Contains(c.id))
                        .ToList();

                    if (conflictingTargetCuboids.Count > 0)
                    {
                        Console.WriteLine($"Found {conflictingTargetCuboids.Count} ID conflicts. Reassigning target cuboid IDs...");
                        
                        // Find the next available ID starting from the highest existing ID
                        if (targetLevel.cuboids.Count > 0)
                        {
                            nextAvailableId = Math.Max(
                                targetLevel.cuboids.Max(c => c.id),
                                desiredRC1Ids.Max()
                            ) + 1;
                        }

                        // Reassign conflicting target cuboids to new IDs
                        foreach (var conflictingCuboid in conflictingTargetCuboids)
                        {
                            int oldId = conflictingCuboid.id;
                            
                            // Find next available ID
                            while (usedIds.Contains(nextAvailableId) || desiredRC1Ids.Contains(nextAvailableId))
                            {
                                nextAvailableId++;
                            }
                            
                            conflictingCuboid.id = nextAvailableId;
                            usedIds.Add(nextAvailableId);
                            idRemapping[oldId] = nextAvailableId;
                            
                            Console.WriteLine($"  Reassigned conflicting target cuboid {oldId} to new ID {nextAvailableId}");
                            nextAvailableId++;
                        }
                    }

                    // Mark all current target cuboid IDs as used
                    foreach (var cuboid in targetLevel.cuboids)
                    {
                        usedIds.Add(cuboid.id);
                    }
                }
                else
                {
                    // Find the highest existing cuboid ID to avoid conflicts
                    if (targetLevel.cuboids.Count > 0)
                    {
                        nextAvailableId = targetLevel.cuboids.Max(c => c.id) + 1;
                    }
                    Console.WriteLine($"Starting cuboid ID assignment from {nextAvailableId}");
                }

                // Import cuboids from source level
                int importedCount = 0;
                int skippedCount = 0;
                int preservedIdCount = 0;
                int reassignedIdCount = 0;

                foreach (var sourceCuboid in sourceLevel.cuboids)
                {
                    // Skip ship animation cuboids if requested
                    if (options.HasFlag(CuboidImportOptions.SkipShipAnimationCuboids) &&
                        shipAnimationCuboidIds.Contains(sourceCuboid.id))
                    {
                        skippedCount++;
                        Console.WriteLine($"  Skipping cuboid {sourceCuboid.id} (used for ship animation)");
                        continue;
                    }

                    int targetId;
                    
                    if (options.HasFlag(CuboidImportOptions.PreserveOriginalIDs))
                    {
                        // Try to preserve the original ID
                        if (!usedIds.Contains(sourceCuboid.id))
                        {
                            targetId = sourceCuboid.id;
                            preservedIdCount++;
                            Console.WriteLine($"  Imported cuboid {sourceCuboid.id} (preserved original ID)");
                        }
                        else
                        {
                            // Find next available ID
                            while (usedIds.Contains(nextAvailableId))
                            {
                                nextAvailableId++;
                            }
                            targetId = nextAvailableId++;
                            reassignedIdCount++;
                            Console.WriteLine($"  Imported cuboid {sourceCuboid.id} as new ID {targetId} (ID conflict resolved)");
                        }
                    }
                    else
                    {
                        // Always assign new sequential IDs
                        targetId = nextAvailableId++;
                        Console.WriteLine($"  Imported cuboid {sourceCuboid.id} as new ID {targetId}");
                    }

                    // Create a copy of the cuboid
                    Cuboid newCuboid = CloneCuboid(sourceCuboid, targetId);
                    targetLevel.cuboids.Add(newCuboid);
                    usedIds.Add(targetId);
                    importedCount++;
                }

                Console.WriteLine($"✅ Successfully imported {importedCount} cuboids from RC1 source");
                if (skippedCount > 0)
                {
                    Console.WriteLine($"  Skipped {skippedCount} cuboids (ship animation)");
                }
                if (options.HasFlag(CuboidImportOptions.PreserveOriginalIDs))
                {
                    Console.WriteLine($"  Preserved original IDs: {preservedIdCount}");
                    Console.WriteLine($"  Assigned new IDs due to conflicts: {reassignedIdCount}");
                    if (idRemapping.Count > 0)
                    {
                        Console.WriteLine($"  Reassigned {idRemapping.Count} existing target cuboid IDs to resolve conflicts");
                    }
                }
                Console.WriteLine($"Target level now has {targetLevel.cuboids.Count} total cuboids");

                // Validate for duplicate IDs
                ValidateImportedCuboids(targetLevel);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during cuboid import: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Creates a complete clone of a cuboid with a new ID
        /// </summary>
        /// <param name="sourceCuboid">The cuboid to clone</param>
        /// <param name="newId">The ID to assign to the new cuboid</param>
        /// <returns>A new cuboid instance that is a copy of the source cuboid</returns>
        private static Cuboid CloneCuboid(Cuboid sourceCuboid, int newId)
        {
            try
            {
                // Serialize the source cuboid to ensure we get all its data
                byte[] cuboidData = sourceCuboid.ToByteArray();

                // Create a new cuboid from the serialized data
                Cuboid newCuboid = new Cuboid(cuboidData, 0);

                // Assign the new ID
                newCuboid.id = newId;

                // Ensure the transform matrix is updated
                newCuboid.UpdateTransformMatrix();

                return newCuboid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error cloning cuboid {sourceCuboid.id}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Validates imported cuboids for potential issues
        /// </summary>
        /// <param name="level">The level to validate</param>
        private static void ValidateImportedCuboids(Level level)
        {
            if (level.cuboids == null || level.cuboids.Count == 0)
                return;

            Console.WriteLine("\n=== Validating Imported Cuboids ===");

            // Check for duplicate IDs
            var duplicateIds = level.cuboids
                .GroupBy(c => c.id)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateIds.Any())
            {
                Console.WriteLine($"⚠️ Found {duplicateIds.Count} duplicate cuboid ID(s):");
                foreach (var group in duplicateIds)
                {
                    Console.WriteLine($"  ID {group.Key}: {group.Count()} occurrences");
                }
            }
            else
            {
                Console.WriteLine("✅ No duplicate cuboid IDs found");
            }

            Console.WriteLine($"Total cuboids: {level.cuboids.Count}");
            Console.WriteLine("✅ Cuboid validation complete");
        }

        /// <summary>
        /// Interactive version of the cuboid importer
        /// </summary>
        /// <returns>True if the operation was successful</returns>
        public static bool ImportCuboidsInteractive()
        {
            Console.WriteLine("\n==== Import RC1 Cuboids ====");

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
            Console.WriteLine("1. Import all cuboids except ship animation ones (preserve IDs) [default]");
            Console.WriteLine("2. Import all cuboids including ship animation ones (preserve IDs)");
            Console.WriteLine("3. Replace all existing cuboids with RC1 ones (except ship animation)");
            Console.WriteLine("4. Replace all existing cuboids with RC1 ones (including ship animation)");
            Console.WriteLine("5. Import with sequential IDs (no ID preservation)");
            Console.WriteLine("6. Custom options");
            Console.Write("> ");

            string choice = Console.ReadLine()?.Trim() ?? "1";
            CuboidImportOptions options;

            switch (choice)
            {
                case "1":
                    options = CuboidImportOptions.Default;
                    break;
                case "2":
                    options = CuboidImportOptions.ImportAll | CuboidImportOptions.PreserveOriginalIDs;
                    break;
                case "3":
                    options = CuboidImportOptions.ReplaceAll;
                    break;
                case "4":
                    options = CuboidImportOptions.ImportAll | CuboidImportOptions.ReplaceExisting | CuboidImportOptions.PreserveOriginalIDs;
                    break;
                case "5":
                    options = CuboidImportOptions.ImportAll | CuboidImportOptions.SkipShipAnimationCuboids; // No PreserveOriginalIDs
                    break;
                case "6":
                    options = GetCustomOptions();
                    break;
                default:
                    options = CuboidImportOptions.Default;
                    break;
            }

            // Perform the import
            bool success = ImportCuboidsFromRC1(targetLevel, sourceLevel, options);

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
        private static CuboidImportOptions GetCustomOptions()
        {
            CuboidImportOptions options = CuboidImportOptions.None;

            Console.WriteLine("\nCustomize import options:");

            if (GetYesNoInput("Import all cuboids? (Y/N): "))
                options |= CuboidImportOptions.ImportAll;

            if (GetYesNoInput("Skip cuboids used for ship animation? (Y/N): "))
                options |= CuboidImportOptions.SkipShipAnimationCuboids;

            if (GetYesNoInput("Replace existing cuboids in target level? (Y/N): "))
                options |= CuboidImportOptions.ReplaceExisting;

            if (GetYesNoInput("Try to preserve original cuboid IDs? (Y/N): "))
                options |= CuboidImportOptions.PreserveOriginalIDs;

            return options;
        }

        /// <summary>
        /// Helper method for yes/no input
        /// </summary>
        private static bool GetYesNoInput(String prompt)
        {
            Console.Write(prompt);
            string input = Console.ReadLine()?.Trim().ToLower() ?? "";
            return input == "y" || input == "yes";
        }

        /// <summary>
        /// Analyzes cuboids in a level for diagnostic purposes
        /// </summary>
        /// <param name="level">The level to analyze</param>
        /// <param name="levelName">Name/label for the level</param>
        public static void AnalyzeCuboids(Level level, string levelName)
        {
            Console.WriteLine($"\n=== Cuboid Analysis for {levelName} ===");

            if (level.cuboids == null || level.cuboids.Count == 0)
            {
                Console.WriteLine("No cuboids found in this level");
                return;
            }

            Console.WriteLine($"Total cuboids: {level.cuboids.Count}");

            // Show distribution of cuboid IDs
            var idCounts = level.cuboids.GroupBy(c => c.id).ToDictionary(g => g.Key, g => g.Count());
            if (idCounts.Values.Any(count => count > 1))
            {
                Console.WriteLine("⚠️ Duplicate cuboid IDs found:");
                foreach (var kvp in idCounts.Where(kvp => kvp.Value > 1))
                {
                    Console.WriteLine($"  ID {kvp.Key}: {kvp.Value} occurrences");
                }
            }

            // Show ship animation cuboid IDs if available
            if (level.levelVariables != null)
            {
                Console.WriteLine($"Ship camera start ID: {level.levelVariables.shipCameraStartID}");
                Console.WriteLine($"Ship camera end ID: {level.levelVariables.shipCameraEndID}");

                var shipAnimIds = new[] { level.levelVariables.shipCameraStartID, level.levelVariables.shipCameraEndID }
                    .Where(id => id >= 0).ToList();

                if (shipAnimIds.Any())
                {
                    var foundIds = level.cuboids.Where(c => shipAnimIds.Contains(c.id)).Select(c => c.id).ToList();
                    Console.WriteLine($"Ship animation cuboids found: {string.Join(", ", foundIds)}");

                    var missingIds = shipAnimIds.Except(foundIds).ToList();
                    if (missingIds.Any())
                    {
                        Console.WriteLine($"⚠️ Ship animation cuboids missing: {string.Join(", ", missingIds)}");
                    }
                }
            }

            // Show first few cuboids for reference
            Console.WriteLine("\nFirst few cuboids:");
            int sampleCount = Math.Min(5, level.cuboids.Count);
            for (int i = 0; i < sampleCount; i++)
            {
                var cuboid = level.cuboids[i];
                Console.WriteLine($"  Cuboid {i}: ID={cuboid.id}, Position=({cuboid.position.X:F2}, {cuboid.position.Y:F2}, {cuboid.position.Z:F2})");
            }
        }
    }
}
