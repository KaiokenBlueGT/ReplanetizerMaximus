// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles swingshot nodes and pulls positioning and creation between RC1 and RC2 levels
    /// </summary>
    public static class SwingshotOltanisSwapper
    {
        /// <summary>
        /// Options for controlling swingshot swapping behavior
        /// </summary>
        [Flags]
        public enum SwingshotSwapOptions
        {
            None = 0,
            RepositionExisting = 1,
            CreateMissing = 2,
            SetLightToZero = 4,

            RepositionOnly = RepositionExisting,
            FullSwap = RepositionExisting | CreateMissing | SetLightToZero,
            Default = FullSwap
        }

        // Model IDs for swingshots
        private static readonly int[] SwingshotNodeIds = { 803 }; // Adjust these IDs to match actual swingshot node IDs
        private static readonly int[] SwingshotPullIds = { 758 }; // Adjust these IDs to match actual swingshot pull IDs

        /// <summary>
        /// Swaps swingshot nodes and pulls to match RC1 Oltanis
        /// </summary>
        /// <param name="targetLevel">RC2 level to modify</param>
        /// <param name="rc1SourceLevel">RC1 Oltanis level to get swingshot data from</param>
        /// <param name="options">Options to control the swap behavior</param>
        /// <returns>True if operation was successful</returns>
        public static bool SwapSwingshotsWithRC1Oltanis(Level targetLevel, Level rc1SourceLevel, SwingshotSwapOptions options = SwingshotSwapOptions.Default)
        {
            if (targetLevel == null || rc1SourceLevel == null)
            {
                Console.WriteLine("❌ Cannot swap swingshots: Invalid level data");
                return false;
            }

            Console.WriteLine("\n==== Swapping Swingshot Nodes/Pulls to match RC1 Oltanis ====");
            Console.WriteLine($"Options: RepositionExisting={options.HasFlag(SwingshotSwapOptions.RepositionExisting)}, " +
                              $"CreateMissing={options.HasFlag(SwingshotSwapOptions.CreateMissing)}, " +
                              $"SetLightToZero={options.HasFlag(SwingshotSwapOptions.SetLightToZero)}");

            // Analyze and report on all available models in both levels
            AnalyzeSwingshotModels(targetLevel, rc1SourceLevel);

            // Process swingshot nodes
            bool nodesProcessed = ProcessSwingshotObjects(targetLevel, rc1SourceLevel, SwingshotNodeIds, "swingshot node", options);

            // Process swingshot pulls
            bool pullsProcessed = ProcessSwingshotObjects(targetLevel, rc1SourceLevel, SwingshotPullIds, "swingshot pull", options);

            Console.WriteLine("\n==== Swingshot Swap Summary ====");
            if (options.HasFlag(SwingshotSwapOptions.RepositionExisting))
                Console.WriteLine("✅ Repositioned existing swingshots to match RC1 Oltanis");
            if (options.HasFlag(SwingshotSwapOptions.CreateMissing))
                Console.WriteLine("✅ Created missing swingshots to match RC1 Oltanis");
            if (options.HasFlag(SwingshotSwapOptions.SetLightToZero))
                Console.WriteLine("✅ Set swingshot light values to 0 to match RC1 Oltanis");
                
            return nodesProcessed || pullsProcessed;
        }

        /// <summary>
        /// Analyzes and reports on all models that could be swingshots in both levels
        /// </summary>
        private static void AnalyzeSwingshotModels(Level targetLevel, Level rc1SourceLevel)
        {
            Console.WriteLine("\n--- Analyzing Available Models ---");
            
            // Check RC1 Level
            Console.WriteLine("RC1 Level Models:");
            if (rc1SourceLevel.mobyModels != null)
            {
                var potentialSwingshotModels = rc1SourceLevel.mobyModels
                    .Where(m => m.id >= 750 && m.id <= 850)
                    .ToList();
                
                foreach (var model in potentialSwingshotModels)
                {
                    int instanceCount = rc1SourceLevel.mobs?.Count(m => m.modelID == model.id) ?? 0;
                    Console.WriteLine($"  Model ID {model.id}: {instanceCount} instances");
                }
            }
            
            // Check Target Level
            Console.WriteLine("Target Level Models:");
            if (targetLevel.mobyModels != null)
            {
                var potentialSwingshotModels = targetLevel.mobyModels
                    .Where(m => m.id >= 750 && m.id <= 850)
                    .ToList();
                
                foreach (var model in potentialSwingshotModels)
                {
                    int instanceCount = targetLevel.mobs?.Count(m => m.modelID == model.id) ?? 0;
                    Console.WriteLine($"  Model ID {model.id}: {instanceCount} instances");
                }
            }
        }

        /// <summary>
        /// Process swingshot objects of a specific type (nodes or pulls)
        /// </summary>
        private static bool ProcessSwingshotObjects(Level targetLevel, Level rc1SourceLevel, int[] modelIds, string objectType, SwingshotSwapOptions options)
        {
            bool processedAny = false;

            // Find valid model IDs by checking which ones actually exist in each level
            var validRc1ModelIds = new List<int>();
            var validTargetModelIds = new List<int>();

            // Check which model IDs actually exist in the RC1 level
            foreach (var modelId in modelIds)
            {
                if ((rc1SourceLevel.mobs?.Any(m => m.modelID == modelId) ?? false) &&
                    (rc1SourceLevel.mobyModels?.Any(m => m.id == modelId) ?? false))
                {
                    validRc1ModelIds.Add(modelId);
                }
            }

            // Check which model IDs actually exist in the target level
            foreach (var modelId in modelIds)
            {
                if (targetLevel.mobyModels?.Any(m => m.id == modelId) ?? false)
                {
                    validTargetModelIds.Add(modelId);
                }
            }

            Console.WriteLine($"\n--- Processing {objectType}s ---");
            Console.WriteLine($"Valid {objectType} model IDs in RC1: {string.Join(", ", validRc1ModelIds)}");
            Console.WriteLine($"Valid {objectType} model IDs in target: {string.Join(", ", validTargetModelIds)}");

            // If we don't have valid models in either level, we can't proceed
            if (validRc1ModelIds.Count == 0)
            {
                Console.WriteLine($"⚠️ No valid {objectType} models found in RC1 Oltanis");
                return false;
            }

            if (validTargetModelIds.Count == 0)
            {
                Console.WriteLine($"⚠️ No valid {objectType} models found in target level");
                return false;
            }

            // Use the first valid model ID from each level
            int rc1ModelId = validRc1ModelIds.First();
            int targetModelId = validTargetModelIds.First();

            // Find swingshots in both levels
            var rc1Swingshots = rc1SourceLevel.mobs?.Where(m => m.modelID == rc1ModelId).ToList() ?? new List<Moby>();
            var targetSwingshots = targetLevel.mobs?.Where(m => m.modelID == targetModelId).ToList() ?? new List<Moby>();

            Console.WriteLine($"Found {rc1Swingshots.Count} {objectType}s in RC1 Oltanis and {targetSwingshots.Count} in target level");

            if (rc1Swingshots.Count == 0)
            {
                Console.WriteLine($"⚠️ No {objectType}s found in RC1 Oltanis level to use as reference");
                return false;
            }

            // Step 1: Reposition existing swingshots if enabled
            // [Code for repositioning remains the same]

            // Step 2: Create missing swingshots if enabled
            int createdCount = 0;
            if (options.HasFlag(SwingshotSwapOptions.CreateMissing) && targetSwingshots.Count < rc1Swingshots.Count)
            {
                // Find the model in the target level - use FirstOrDefault to get a concrete reference
                var swingshotModel = targetLevel.mobyModels?.FirstOrDefault(m => m.id == targetModelId);
                if (swingshotModel == null)
                {
                    Console.WriteLine($"❌ Cannot create new {objectType}s: Model ID {targetModelId} not found in target level");
                }
                else
                {
                    Console.WriteLine($"Found target model ID {targetModelId} for creating new {objectType}s");

                    // Calculate how many new swingshots we need to create
                    int toCreate = rc1Swingshots.Count - targetSwingshots.Count;
                    Console.WriteLine($"Need to create {toCreate} new {objectType}s to match RC1 count");

                    // Find the highest moby ID to ensure we create unique IDs
                    int nextMobyId = 1000; // Start with a safe base value
                    if (targetLevel.mobs != null && targetLevel.mobs.Count > 0)
                    {
                        nextMobyId = targetLevel.mobs.Max(m => m.mobyID) + 1;
                    }
                    Console.WriteLine($"Next moby ID will start at: {nextMobyId}");

                    // Ensure mobs list is initialized
                    if (targetLevel.mobs == null)
                    {
                        targetLevel.mobs = new List<Moby>();
                    }

                    // Find a template swingshot to copy properties from
                    Moby? templateSwingshot = null;
                    if (targetSwingshots.Count > 0)
                    {
                        templateSwingshot = targetSwingshots[0];
                        Console.WriteLine($"Using existing {objectType} as template with properties:");
                        Console.WriteLine($"  Position: {templateSwingshot.position}");
                        Console.WriteLine($"  UpdateDistance: {templateSwingshot.updateDistance}");
                        Console.WriteLine($"  DrawDistance: {templateSwingshot.drawDistance}");
                        Console.WriteLine($"  SpawnType: {templateSwingshot.spawnType}");
                        Console.WriteLine($"  pVars length: {templateSwingshot.pVars?.Length ?? 0}");
                    }
                    else
                    {
                        Console.WriteLine($"No existing {objectType}s to use as template, using default values");
                    }

                    // Create new swingshots for the remaining positions
                    for (int i = targetSwingshots.Count; i < rc1Swingshots.Count; i++)
                    {
                        try
                        {
                            // Create a new moby with a unique ID
                            Moby newSwingshot = new Moby
                            {
                                mobyID = nextMobyId++,
                                modelID = targetModelId,
                                model = swingshotModel, // Important! Link to the actual model
                                position = rc1Swingshots[i].position,
                                rotation = rc1Swingshots[i].rotation,
                                scale = rc1Swingshots[i].scale,

                                // Copy from template or use reasonable defaults
                                updateDistance = templateSwingshot?.updateDistance ?? 200,
                                drawDistance = templateSwingshot?.drawDistance ?? 200,

                                // These properties are important for swingshots to work properly
                                spawnBeforeDeath = true,
                                spawnType = templateSwingshot?.spawnType ?? new Bitmask(), // Create default Bitmask
                                light = 0,

                                // Additional properties that might be important
                                missionID = templateSwingshot?.missionID ?? 0,
                                dataval = templateSwingshot?.dataval ?? 0
                            };

                            // Update transform matrix
                            newSwingshot.UpdateTransformMatrix();

                            // Handle pVars (crucial for swingshot functionality)
                            if (templateSwingshot?.pVars != null && templateSwingshot.pVars.Length > 0)
                            {
                                // Copy pVars from template
                                newSwingshot.pVars = new byte[templateSwingshot.pVars.Length];
                                Array.Copy(templateSwingshot.pVars, newSwingshot.pVars, templateSwingshot.pVars.Length);
                                Console.WriteLine($"  Copied pVars from template ({newSwingshot.pVars.Length} bytes)");
                            }
                            else if (rc1Swingshots[i].pVars != null && rc1Swingshots[i].pVars.Length > 0)
                            {
                                // If no template pVars, try to use pVars from RC1 source
                                newSwingshot.pVars = new byte[rc1Swingshots[i].pVars.Length];
                                Array.Copy(rc1Swingshots[i].pVars, newSwingshot.pVars, rc1Swingshots[i].pVars.Length);
                                Console.WriteLine($"  Copied pVars from RC1 source ({newSwingshot.pVars.Length} bytes)");
                            }
                            else
                            {
                                // Create default pVars - more bytes to be safer
                                newSwingshot.pVars = new byte[8] { 0, 0, 0, 0, 0, 0, 0, 0 };
                                Console.WriteLine("  Created default pVars");
                            }

                            // Add the new swingshot to the level
                            targetLevel.mobs.Add(newSwingshot);
                            targetSwingshots.Add(newSwingshot);
                            createdCount++;

                            Console.WriteLine($"  Created new {objectType} with ID {newSwingshot.mobyID} at position {newSwingshot.position}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Error creating new {objectType}: {ex.Message}");
                            Console.WriteLine(ex.StackTrace);
                        }
                    }

                    Console.WriteLine($"✅ Created {createdCount} new {objectType}s to match RC1 Oltanis");
                    processedAny = true;

                    // Update moby IDs list in the level if we added new ones
                    if (createdCount > 0 && targetLevel.mobyIds != null)
                    {
                        // Make sure mobyIds reflects all mobys in the level
                        targetLevel.mobyIds = targetLevel.mobs.Select(m => m.mobyID).ToList();
                        Console.WriteLine($"Updated mobyIds list with {targetLevel.mobyIds.Count} entries");
                    }
                }
            }

            // Step 3: Set light value to 0 for all swingshots if enabled
            // [Code for light value setting remains the same]

            return processedAny;
        }

        /// <summary>
        /// Interactive wrapper for swingshot swapping function
        /// </summary>
        /// <returns>True if the operation was successful</returns>
        public static bool SwapSwingshotsWithRC1OltanisInteractive()
        {
            Console.WriteLine("\n==== Swap RC2 Swingshots with RC1 Oltanis Swingshots ====");

            // Get target level path
            Console.WriteLine("\nEnter path to the target RC2 level engine.ps3 file:");
            Console.Write("> ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                Console.WriteLine("❌ Invalid target level path");
                return false;
            }

            // Get RC1 Oltanis level path
            Console.WriteLine("\nEnter path to the RC1 Oltanis level engine.ps3 file:");
            Console.Write("> ");
            string rc1OltanisPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(rc1OltanisPath) || !File.Exists(rc1OltanisPath))
            {
                Console.WriteLine("❌ Invalid RC1 Oltanis level path");
                return false;
            }

            // Load levels
            Level targetLevel, rc1OltanisLevel;
            try
            {
                Console.WriteLine("Loading target level...");
                targetLevel = new Level(targetPath);
                Console.WriteLine("Loading RC1 Oltanis level...");
                rc1OltanisLevel = new Level(rc1OltanisPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading levels: {ex.Message}");
                return false;
            }

            // Option selection
            Console.WriteLine("\nSelect swap options:");
            Console.WriteLine("1. Full swap (reposition existing, create missing, set light=0) [default]");
            Console.WriteLine("2. Reposition only (keep existing count)");
            Console.WriteLine("3. Custom options");
            Console.Write("> ");
            string choice = Console.ReadLine()?.Trim() ?? "1";
            SwingshotSwapOptions options;
            switch (choice)
            {
                case "2":
                    options = SwingshotSwapOptions.RepositionOnly;
                    break;
                case "3":
                    options = GetCustomOptions();
                    break;
                case "1":
                default:
                    options = SwingshotSwapOptions.FullSwap;
                    break;
            }

            // Perform swingshot swap with selected options
            bool success = SwapSwingshotsWithRC1Oltanis(targetLevel, rc1OltanisLevel, options);

            if (success)
            {
                Console.Write("\nSave changes to the target level? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLower() == "y")
                {
                    try
                    {
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
        /// Helper method to get custom options
        /// </summary>
        private static SwingshotSwapOptions GetCustomOptions()
        {
            SwingshotSwapOptions options = SwingshotSwapOptions.None;

            if (GetYesNoInput("Reposition existing swingshots to match RC1? (y/n): "))
                options |= SwingshotSwapOptions.RepositionExisting;

            if (GetYesNoInput("Create missing swingshots to match RC1 count? (y/n): "))
                options |= SwingshotSwapOptions.CreateMissing;

            if (GetYesNoInput("Set light value to 0 for all swingshots? (y/n): "))
                options |= SwingshotSwapOptions.SetLightToZero;

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
    }
}
