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
using LibReplanetizer.Models;
using LibReplanetizer.Models.Animations;
using static LibReplanetizer.DataFunctions;

namespace GeometrySwapper
{
    /// <summary>
    /// Options for animation restoration operations
    /// </summary>
    [Flags]
    public enum AnimationRestoreOptions
    {
        None = 0,
        PlayerAnimations = 1 << 0,        // Restore player/Ratchet animations
        RatchetModelAnimations = 1 << 1,  // Restore Ratchet model (ID 0) animations 
        AllMobyAnimations = 1 << 2,       // Restore all moby model animations
        PreserveTargetAnimations = 1 << 3, // Keep existing animations that aren't being replaced
        Default = PlayerAnimations | RatchetModelAnimations | PreserveTargetAnimations
    }

    /// <summary>
    /// Represents an animation from a source level with metadata
    /// </summary>
    public class SourceAnimation
    {
        public int AnimationID { get; set; }
        public Animation Animation { get; set; }
        public string SourceLevelName { get; set; }
        public GameType SourceGame { get; set; }
        public int ModelID { get; set; } = -1; // -1 for player animations, model ID for moby animations
        public string ModelName { get; set; } = "Player";
        public int FrameCount => Animation?.frames?.Count ?? 0;
        public float Speed => Animation?.speed ?? 0.0f;
        public bool HasData => Animation != null && FrameCount > 0;

        public SourceAnimation(int animId, Animation anim, string levelName, GameType game, int modelId = -1, string modelName = "Player")
        {
            AnimationID = animId;
            Animation = anim;
            SourceLevelName = levelName;
            SourceGame = game;
            ModelID = modelId;
            ModelName = modelName;
        }

        public override string ToString()
        {
            string status = HasData ? "✅" : "❌";
            string modelInfo = ModelID >= 0 ? $" (Model {ModelID}: {ModelName})" : "";
            return $"{status} [{SourceGame.Name}] {SourceLevelName}{modelInfo} - Anim {AnimationID}: {FrameCount} frames, speed {Speed:F2}";
        }
    }

    /// <summary>
    /// Manages animation restoration between different R&C games
    /// </summary>
    public static class AnimationRestorer
    {
        /// <summary>
        /// Analyzes animations in multiple source levels and presents them for selection
        /// </summary>
        public static void AnalyzeAndRestoreAnimationsInteractive()
        {
            Console.WriteLine("\n==== Animation Restoration Tool ====");
            Console.WriteLine("This tool allows you to view animations by ID from different games");
            Console.WriteLine("and selectively restore them to fix missing or broken animations.");

            // Get target level
            Console.Write("\nEnter path to TARGET level engine.ps3 (level to restore animations to): ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(targetPath))
            {
                Console.WriteLine("❌ Target level not found");
                return;
            }

            // Get source levels
            List<string> sourcePaths = new List<string>();
            Console.WriteLine("\nEnter paths to SOURCE levels (press Enter when done):");

            int sourceCount = 0;
            while (true)
            {
                Console.Write($"Source level #{sourceCount + 1} (or Enter to finish): ");
                string sourcePath = Console.ReadLine()?.Trim() ?? "";

                if (string.IsNullOrEmpty(sourcePath))
                    break;

                if (File.Exists(sourcePath))
                {
                    sourcePaths.Add(sourcePath);
                    sourceCount++;
                    Console.WriteLine($"✅ Added source level: {Path.GetFileName(Path.GetDirectoryName(sourcePath))}");
                }
                else
                {
                    Console.WriteLine("❌ File not found, skipping");
                }
            }

            if (sourcePaths.Count == 0)
            {
                Console.WriteLine("❌ No valid source levels provided");
                return;
            }

            // Load and analyze animations
            Console.WriteLine("\n📊 Loading and analyzing animations...");

            Level targetLevel;
            try
            {
                targetLevel = new Level(targetPath);
                Console.WriteLine($"✅ Target level loaded: {targetLevel.game.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading target level: {ex.Message}");
                return;
            }

            var animationDatabase = AnalyzeAnimationsFromSources(sourcePaths);

            if (animationDatabase.Count == 0)
            {
                Console.WriteLine("❌ No animations found in source levels");
                return;
            }

            // Present analysis and get user selections
            var selectedAnimations = PresentAnimationAnalysis(animationDatabase, targetLevel);

            if (selectedAnimations.Count == 0)
            {
                Console.WriteLine("No animations selected for restoration");

                // 🆕 NEW: Even if no manual selections, still offer zero-frame detection
                var zeroFrameSelections = DetectAndOfferZeroFrameRestoration(targetLevel, animationDatabase);
                if (zeroFrameSelections.Count > 0)
                {
                    selectedAnimations = zeroFrameSelections;
                }
                else
                {
                    return;
                }
            }
            else
            {
                // 🆕 NEW: After manual restoration, offer zero-frame detection
                Console.WriteLine($"\n✅ Manual restoration selections complete ({selectedAnimations.Count} animations selected)");

                var zeroFrameSelections = DetectAndOfferZeroFrameRestoration(targetLevel, animationDatabase);
                if (zeroFrameSelections.Count > 0)
                {
                    Console.WriteLine($"\n🔄 Combining manual selections with zero-frame restoration...");

                    // Merge the two dictionaries (zero-frame takes priority if there are conflicts)
                    foreach (var kvp in zeroFrameSelections)
                    {
                        selectedAnimations[kvp.Key] = kvp.Value;
                        Console.WriteLine($"   Added zero-frame restoration: ID {kvp.Key}");
                    }

                    Console.WriteLine($"✅ Total animations to restore: {selectedAnimations.Count}");
                }
            }

            // Get restoration options
            var options = GetAnimationRestoreOptions();

            // Perform restoration
            bool success = RestoreSelectedAnimations(targetLevel, selectedAnimations, options);

            if (success)
            {
                // 🆕 QoL IMPROVEMENT 3: Fix save directory bug
                if (GetYesNoInput("\nSave changes to target level? (Y/N): "))
                {
                    try
                    {
                        // Get the directory containing the engine.ps3 file
                        string targetDirectory = Path.GetDirectoryName(targetPath) ?? "";
                        
                        // Ensure the directory path ends with a separator if it's not empty
                        if (!string.IsNullOrEmpty(targetDirectory) && 
                            !targetDirectory.EndsWith(Path.DirectorySeparatorChar.ToString()) && 
                            !targetDirectory.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                        {
                            targetDirectory += Path.DirectorySeparatorChar;
                        }
                        
                        Console.WriteLine($"💾 Saving to directory: {targetDirectory}");
                        targetLevel.Save(targetDirectory);
                        Console.WriteLine("✅ Target level saved successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error saving target level: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Analyzes animations from source levels and builds a database
        /// </summary>
        private static Dictionary<int, List<SourceAnimation>> AnalyzeAnimationsFromSources(List<string> sourcePaths)
        {
            var animationDatabase = new Dictionary<int, List<SourceAnimation>>();

            foreach (string sourcePath in sourcePaths)
            {
                try
                {
                    Console.WriteLine($"📖 Analyzing {Path.GetFileName(Path.GetDirectoryName(sourcePath))}...");
                    
                    Level sourceLevel = new Level(sourcePath);
                    string levelName = Path.GetFileName(Path.GetDirectoryName(sourcePath));

                    // Analyze player animations
                    if (sourceLevel.playerAnimations != null)
                    {
                        for (int i = 0; i < sourceLevel.playerAnimations.Count; i++)
                        {
                            var anim = sourceLevel.playerAnimations[i];
                            if (anim != null)
                            {
                                if (!animationDatabase.ContainsKey(i))
                                    animationDatabase[i] = new List<SourceAnimation>();

                                animationDatabase[i].Add(new SourceAnimation(i, anim, levelName, sourceLevel.game));
                            }
                        }
                    }

                    // Analyze Ratchet model animations (model ID 0)
                    var ratchetModel = sourceLevel.mobyModels?.FirstOrDefault(m => m != null && m.id == 0) as MobyModel;
                    if (ratchetModel?.animations != null)
                    {
                        for (int i = 0; i < ratchetModel.animations.Count; i++)
                        {
                            var anim = ratchetModel.animations[i];
                            if (anim != null)
                            {
                                if (!animationDatabase.ContainsKey(i))
                                    animationDatabase[i] = new List<SourceAnimation>();

                                animationDatabase[i].Add(new SourceAnimation(i, anim, levelName, sourceLevel.game, 0, "Ratchet"));
                            }
                        }
                    }

                    Console.WriteLine($"  ✅ Found {sourceLevel.playerAnimations?.Count ?? 0} player animations, {ratchetModel?.animations?.Count ?? 0} Ratchet model animations");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error analyzing {sourcePath}: {ex.Message}");
                }
            }

            return animationDatabase;
        }

        /// <summary>
        /// Presents animation analysis to user and gets their selections
        /// </summary>
        private static Dictionary<int, SourceAnimation> PresentAnimationAnalysis(
            Dictionary<int, List<SourceAnimation>> animationDatabase, 
            Level targetLevel)
        {
            Console.WriteLine($"\n📋 Animation Analysis Results ({animationDatabase.Keys.Count} unique animation IDs found)");
            Console.WriteLine("================================================================================");

            // Show current target level status
            Console.WriteLine($"\n🎯 TARGET LEVEL STATUS ({targetLevel.game.Name}):");
            Console.WriteLine($"   Player animations: {targetLevel.playerAnimations?.Count ?? 0}");
            
            var targetRatchet = targetLevel.mobyModels?.FirstOrDefault(m => m != null && m.id == 0) as MobyModel;
            Console.WriteLine($"   Ratchet model animations: {targetRatchet?.animations?.Count ?? 0}");

            // Analyze missing or broken animations in target
            var missingAnimations = new List<int>();
            var brokenAnimations = new List<int>();

            foreach (var animId in animationDatabase.Keys.OrderBy(x => x))
            {
                bool targetHasPlayer = targetLevel.playerAnimations != null && 
                                     animId < targetLevel.playerAnimations.Count && 
                                     targetLevel.playerAnimations[animId] != null &&
                                     targetLevel.playerAnimations[animId].frames.Count > 0;

                bool targetHasRatchet = targetRatchet?.animations != null && 
                                      animId < targetRatchet.animations.Count && 
                                      targetRatchet.animations[animId] != null &&
                                      targetRatchet.animations[animId].frames.Count > 0;

                if (!targetHasPlayer && !targetHasRatchet)
                {
                    missingAnimations.Add(animId);
                }
                else if (targetHasPlayer && targetLevel.playerAnimations[animId].frames.Count == 0)
                {
                    brokenAnimations.Add(animId);
                }
                else if (targetHasRatchet && targetRatchet.animations[animId].frames.Count == 0)
                {
                    brokenAnimations.Add(animId);
                }
            }

            if (missingAnimations.Count > 0)
            {
                Console.WriteLine($"\n❌ MISSING ANIMATIONS ({missingAnimations.Count}): {string.Join(", ", missingAnimations)}");
            }

            if (brokenAnimations.Count > 0)
            {
                Console.WriteLine($"\n🔧 BROKEN ANIMATIONS ({brokenAnimations.Count}): {string.Join(", ", brokenAnimations)}");
            }

            // Show detailed analysis
            Console.WriteLine("\n🔍 DETAILED ANALYSIS:");
            foreach (var animId in animationDatabase.Keys.OrderBy(x => x).Take(20)) // Limit display
            {
                Console.WriteLine($"\n--- Animation ID {animId} ---");
                foreach (var sourceAnim in animationDatabase[animId])
                {
                    Console.WriteLine($"  {sourceAnim}");
                }
            }

            if (animationDatabase.Keys.Count > 20)
            {
                Console.WriteLine($"\n... and {animationDatabase.Keys.Count - 20} more animation IDs");
            }

            // Get user selections
            return GetUserAnimationSelections(animationDatabase, missingAnimations, brokenAnimations);
        }

        /// <summary>
        /// Gets user's selection of which animations to restore
        /// </summary>
        private static Dictionary<int, SourceAnimation> GetUserAnimationSelections(
            Dictionary<int, List<SourceAnimation>> animationDatabase,
            List<int> missingAnimations,
            List<int> brokenAnimations)
        {
            var selectedAnimations = new Dictionary<int, SourceAnimation>();

            Console.WriteLine("\n🎯 SELECT ANIMATIONS TO RESTORE:");
            Console.WriteLine("1. Auto-restore all missing animations");
            Console.WriteLine("2. Auto-restore all broken animations");
            Console.WriteLine("3. Auto-restore missing + broken animations");
            Console.WriteLine("4. Manual selection");
            Console.WriteLine("5. Cancel");
            Console.Write("> ");

            string choice = Console.ReadLine()?.Trim() ?? "5";

            switch (choice)
            {
                case "1":
                    return AutoSelectAnimations(animationDatabase, missingAnimations);
                case "2":
                    return AutoSelectAnimations(animationDatabase, brokenAnimations);
                case "3":
                    var allProblematic = missingAnimations.Concat(brokenAnimations).Distinct().ToList();
                    return AutoSelectAnimations(animationDatabase, allProblematic);
                case "4":
                    return ManualSelectAnimations(animationDatabase);
                case "5":
                default:
                    return new Dictionary<int, SourceAnimation>();
            }
        }

        /// <summary>
        /// Auto-selects best animations for specified IDs
        /// </summary>
        private static Dictionary<int, SourceAnimation> AutoSelectAnimations(
            Dictionary<int, List<SourceAnimation>> animationDatabase,
            List<int> animationIds)
        {
            var selected = new Dictionary<int, SourceAnimation>();

            foreach (int animId in animationIds)
            {
                if (animationDatabase.ContainsKey(animId))
                {
                    // Pick the best animation (prioritize RC2/RC3, then highest frame count)
                    var bestAnim = animationDatabase[animId]
                        .Where(a => a.HasData)
                        .OrderByDescending(a => a.SourceGame.num >= 2 ? 1 : 0) // Prioritize RC2/RC3
                        .ThenByDescending(a => a.FrameCount)
                        .FirstOrDefault();

                    if (bestAnim != null)
                    {
                        selected[animId] = bestAnim;
                        Console.WriteLine($"✅ Auto-selected for ID {animId}: {bestAnim}");
                    }
                }
            }

            return selected;
        }

        /// <summary>
        /// Enhanced manual animation selection interface with custom ID mapping support
        /// </summary>
        private static Dictionary<int, SourceAnimation> ManualSelectAnimations(
            Dictionary<int, List<SourceAnimation>> animationDatabase)
        {
            var selected = new Dictionary<int, SourceAnimation>();

            Console.WriteLine("\n📝 MANUAL SELECTION MODE");
            Console.WriteLine("Choose your selection method:");
            Console.WriteLine("1. Simple mode (restore animations to same IDs)");
            Console.WriteLine("2. Advanced mode (map source animations to custom target IDs)");
            Console.Write("> ");

            string modeChoice = Console.ReadLine()?.Trim() ?? "1";

            if (modeChoice == "2")
            {
                return AdvancedManualSelection(animationDatabase);
            }
            else
            {
                return SimpleManualSelection(animationDatabase);
            }
        }

        /// <summary>
        /// Simple manual selection (original behavior)
        /// </summary>
        private static Dictionary<int, SourceAnimation> SimpleManualSelection(
            Dictionary<int, List<SourceAnimation>> animationDatabase)
        {
            var selected = new Dictionary<int, SourceAnimation>();

            Console.WriteLine("\n📝 SIMPLE MANUAL SELECTION");
            Console.WriteLine("Enter animation IDs to restore (comma-separated), or 'list' to see all IDs:");
            Console.Write("> ");

            string input = Console.ReadLine()?.Trim() ?? "";

            if (input.ToLower() == "list")
            {
                Console.WriteLine($"\nAvailable animation IDs: {string.Join(", ", animationDatabase.Keys.OrderBy(x => x))}");
                Console.Write("Enter animation IDs to restore (comma-separated): ");
                input = Console.ReadLine()?.Trim() ?? "";
            }

            var requestedIds = input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .ToList();

            foreach (int animId in requestedIds)
            {
                if (animationDatabase.ContainsKey(animId))
                {
                    var availableAnimations = animationDatabase[animId].Where(a => a.HasData).ToList();

                    if (availableAnimations.Count == 1)
                    {
                        selected[animId] = availableAnimations[0];
                        Console.WriteLine($"✅ Selected: {availableAnimations[0]}");
                    }
                    else if (availableAnimations.Count > 1)
                    {
                        Console.WriteLine($"\n🔄 Multiple options for Animation ID {animId}:");
                        for (int i = 0; i < availableAnimations.Count; i++)
                        {
                            Console.WriteLine($"  {i + 1}. {availableAnimations[i]}");
                        }

                        Console.Write($"Choose option (1-{availableAnimations.Count}): ");
                        if (int.TryParse(Console.ReadLine()?.Trim(), out int choice) &&
                            choice >= 1 && choice <= availableAnimations.Count)
                        {
                            selected[animId] = availableAnimations[choice - 1];
                            Console.WriteLine($"✅ Selected: {availableAnimations[choice - 1]}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Animation ID {animId} not found in source levels");
                }
            }

            return selected;
        }

        /// <summary>
        /// Advanced manual selection with custom ID mapping
        /// </summary>
        private static Dictionary<int, SourceAnimation> AdvancedManualSelection(
            Dictionary<int, List<SourceAnimation>> animationDatabase)
        {
            var selected = new Dictionary<int, SourceAnimation>();

            Console.WriteLine("\n🎯 ADVANCED MANUAL SELECTION WITH ID MAPPING");
            Console.WriteLine("This mode allows you to map source animation IDs to different target IDs.");
            Console.WriteLine("Perfect for cases like Rail Grinding where RC2 and RC3 use different animation IDs!");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  'list' - Show all available source animation IDs");
            Console.WriteLine("  'map X Y' - Map source animation ID X to target ID Y");
            Console.WriteLine("  'info X' - Show detailed info about source animation ID X");
            Console.WriteLine("  'summary' - Show current mappings");
            Console.WriteLine("  'done' - Finish selection");
            Console.WriteLine();

            while (true)
            {
                Console.Write("Enter command: ");
                string input = Console.ReadLine()?.Trim() ?? "";

                if (string.IsNullOrEmpty(input))
                    continue;

                string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string command = parts[0].ToLower();

                switch (command)
                {
                    case "list":
                        ShowAvailableAnimations(animationDatabase);
                        break;

                    case "map":
                        if (parts.Length >= 3 &&
                            int.TryParse(parts[1], out int sourceId) &&
                            int.TryParse(parts[2], out int targetId))
                        {
                            MapAnimation(animationDatabase, selected, sourceId, targetId);
                        }
                        else
                        {
                            Console.WriteLine("❌ Invalid syntax. Use: map <sourceID> <targetID>");
                            Console.WriteLine("   Example: map 15 23  (maps source animation 15 to target slot 23)");
                        }
                        break;

                    case "info":
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int infoId))
                        {
                            ShowAnimationInfo(animationDatabase, infoId);
                        }
                        else
                        {
                            Console.WriteLine("❌ Invalid syntax. Use: info <animationID>");
                        }
                        break;

                    case "summary":
                        ShowCurrentMappings(selected);
                        break;

                    case "done":
                        if (selected.Count > 0)
                        {
                            Console.WriteLine($"\n✅ Mapping complete! {selected.Count} animations mapped.");
                            return selected;
                        }
                        else
                        {
                            Console.WriteLine("❌ No animations mapped yet. Add some mappings or type 'done' again to exit.");
                        }
                        break;

                    case "help":
                        Console.WriteLine("\nCommands:");
                        Console.WriteLine("  list - Show all available source animation IDs");
                        Console.WriteLine("  map X Y - Map source animation ID X to target ID Y");
                        Console.WriteLine("  info X - Show detailed info about source animation ID X");
                        Console.WriteLine("  summary - Show current mappings");
                        Console.WriteLine("  done - Finish selection");
                        break;

                    default:
                        Console.WriteLine($"❌ Unknown command '{command}'. Type 'help' for available commands.");
                        break;
                }
            }
        }

        /// <summary>
        /// Shows all available source animations grouped by game
        /// </summary>
        private static void ShowAvailableAnimations(Dictionary<int, List<SourceAnimation>> animationDatabase)
        {
            Console.WriteLine("\n📋 AVAILABLE SOURCE ANIMATIONS:");

            // Group by source game for better organization
            var gameGroups = animationDatabase
                .SelectMany(kvp => kvp.Value.Select(anim => new { AnimId = kvp.Key, Anim = anim }))
                .Where(x => x.Anim.HasData)
                .GroupBy(x => x.Anim.SourceGame.Name)
                .OrderBy(g => g.Key);

            foreach (var gameGroup in gameGroups)
            {
                Console.WriteLine($"\n🎮 {gameGroup.Key}:");
                var animsByLevel = gameGroup.GroupBy(x => x.Anim.SourceLevelName);

                foreach (var levelGroup in animsByLevel)
                {
                    Console.WriteLine($"  📁 {levelGroup.Key}:");
                    var sortedAnims = levelGroup.OrderBy(x => x.AnimId);

                    foreach (var item in sortedAnims)
                    {
                        var anim = item.Anim;
                        string modelInfo = anim.ModelID >= 0 ? $" ({anim.ModelName})" : "";
                        Console.WriteLine($"    ID {item.AnimId}: {anim.FrameCount} frames, speed {anim.Speed:F2}{modelInfo}");
                    }
                }
            }

            Console.WriteLine($"\nAnimation IDs available: {string.Join(", ", animationDatabase.Keys.OrderBy(x => x))}");
        }

        /// <summary>
        /// Shows detailed information about a specific animation ID
        /// </summary>
        private static void ShowAnimationInfo(Dictionary<int, List<SourceAnimation>> animationDatabase, int animId)
        {
            if (!animationDatabase.ContainsKey(animId))
            {
                Console.WriteLine($"❌ Animation ID {animId} not found in source levels.");
                return;
            }

            Console.WriteLine($"\n🔍 DETAILED INFO FOR ANIMATION ID {animId}:");
            var animations = animationDatabase[animId].Where(a => a.HasData).ToList();

            if (animations.Count == 0)
            {
                Console.WriteLine("❌ No valid animations found for this ID.");
                return;
            }

            for (int i = 0; i < animations.Count; i++)
            {
                var anim = animations[i];
                Console.WriteLine($"\n  Option {i + 1}: {anim}");

                // Show additional details
                if (anim.Animation.sounds?.Count > 0)
                {
                    Console.WriteLine($"    Sound events: {anim.Animation.sounds.Count}");
                }

                Console.WriteLine($"    Technical: unk1={anim.Animation.unk1}, unk2={anim.Animation.unk2}");
            }
        }

        /// <summary>
        /// Maps a source animation to a target ID with user confirmation
        /// </summary>
        private static void MapAnimation(
            Dictionary<int, List<SourceAnimation>> animationDatabase,
            Dictionary<int, SourceAnimation> selected,
            int sourceId,
            int targetId)
        {
            if (!animationDatabase.ContainsKey(sourceId))
            {
                Console.WriteLine($"❌ Source animation ID {sourceId} not found.");
                return;
            }

            var availableAnimations = animationDatabase[sourceId].Where(a => a.HasData).ToList();
            if (availableAnimations.Count == 0)
            {
                Console.WriteLine($"❌ No valid animations found for source ID {sourceId}.");
                return;
            }

            // Check if target ID is already mapped
            if (selected.ContainsKey(targetId))
            {
                var existing = selected[targetId];
                Console.WriteLine($"⚠️ Target ID {targetId} is already mapped to:");
                Console.WriteLine($"   {existing}");
                Console.Write("Overwrite existing mapping? (Y/N): ");

                string overwrite = Console.ReadLine()?.Trim().ToUpper() ?? "N";
                if (overwrite != "Y")
                {
                    Console.WriteLine("❌ Mapping cancelled.");
                    return;
                }
            }

            SourceAnimation selectedAnim;

            if (availableAnimations.Count == 1)
            {
                selectedAnim = availableAnimations[0];
                Console.WriteLine($"✅ Auto-selected only option: {selectedAnim}");
            }
            else
            {
                Console.WriteLine($"\n🔄 Multiple options for source animation ID {sourceId}:");
                for (int i = 0; i < availableAnimations.Count; i++)
                {
                    Console.WriteLine($"  {i + 1}. {availableAnimations[i]}");
                }

                Console.Write($"Choose option (1-{availableAnimations.Count}): ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out int choice) &&
                    choice >= 1 && choice <= availableAnimations.Count)
                {
                    selectedAnim = availableAnimations[choice - 1];
                }
                else
                {
                    Console.WriteLine("❌ Invalid choice. Mapping cancelled.");
                    return;
                }
            }

            // Create a new SourceAnimation with the target ID for the mapping
            var mappedAnim = new SourceAnimation(
                targetId, // Use target ID instead of source ID
                selectedAnim.Animation,
                selectedAnim.SourceLevelName,
                selectedAnim.SourceGame,
                selectedAnim.ModelID,
                selectedAnim.ModelName
            );

            selected[targetId] = mappedAnim;
            Console.WriteLine($"✅ Mapped: Source ID {sourceId} → Target ID {targetId}");
            Console.WriteLine($"   Animation: {selectedAnim.SourceLevelName} ({selectedAnim.SourceGame.Name}) - {selectedAnim.FrameCount} frames");
        }

        /// <summary>
        /// Shows current animation mappings
        /// </summary>
        private static void ShowCurrentMappings(Dictionary<int, SourceAnimation> selected)
        {
            if (selected.Count == 0)
            {
                Console.WriteLine("\n📝 No animations mapped yet.");
                return;
            }

            Console.WriteLine($"\n📝 CURRENT MAPPINGS ({selected.Count} total):");
            foreach (var kvp in selected.OrderBy(x => x.Key))
            {
                int targetId = kvp.Key;
                var sourceAnim = kvp.Value;

                Console.WriteLine($"  Target ID {targetId} ← {sourceAnim.SourceLevelName} ({sourceAnim.SourceGame.Name})");
                Console.WriteLine($"    {sourceAnim.FrameCount} frames, speed {sourceAnim.Speed:F2}");

                if (sourceAnim.ModelID >= 0)
                {
                    Console.WriteLine($"    Source: {sourceAnim.ModelName} model animation");
                }
                else
                {
                    Console.WriteLine($"    Source: Player animation");
                }
            }
        }

        /// <summary>
        /// Gets restoration options from user
        /// </summary>
        private static AnimationRestoreOptions GetAnimationRestoreOptions()
        {
            Console.WriteLine("\n⚙️ RESTORATION OPTIONS:");
            Console.WriteLine("1. Restore to player animations only");
            Console.WriteLine("2. Restore to Ratchet model animations only"); 
            Console.WriteLine("3. Restore to both player and Ratchet model animations [recommended]");
            Console.Write("> ");

            string choice = Console.ReadLine()?.Trim() ?? "3";

            var options = AnimationRestoreOptions.PreserveTargetAnimations;

            switch (choice)
            {
                case "1":
                    options |= AnimationRestoreOptions.PlayerAnimations;
                    break;
                case "2":
                    options |= AnimationRestoreOptions.RatchetModelAnimations;
                    break;
                case "3":
                default:
                    options |= AnimationRestoreOptions.PlayerAnimations | AnimationRestoreOptions.RatchetModelAnimations;
                    break;
            }

            return options;
        }

        /// <summary>
        /// Restores selected animations to the target level
        /// </summary>
        private static bool RestoreSelectedAnimations(
            Level targetLevel, 
            Dictionary<int, SourceAnimation> selectedAnimations,
            AnimationRestoreOptions options)
        {
            Console.WriteLine("\n🔧 RESTORING ANIMATIONS...");

            try
            {
                // Ensure target collections exist
                if (targetLevel.playerAnimations == null)
                    targetLevel.playerAnimations = new List<Animation>();

                var targetRatchet = targetLevel.mobyModels?.FirstOrDefault(m => m != null && m.id == 0) as MobyModel;
                if (targetRatchet != null && targetRatchet.animations == null)
                    targetRatchet.animations = new List<Animation>();

                foreach (var kvp in selectedAnimations)
                {
                    int animId = kvp.Key;
                    var sourceAnim = kvp.Value;

                    Console.WriteLine($"📥 Restoring animation {animId} from {sourceAnim.SourceLevelName}...");

                    // Restore to player animations
                    if (options.HasFlag(AnimationRestoreOptions.PlayerAnimations))
                    {
                        // Expand list if necessary
                        while (targetLevel.playerAnimations.Count <= animId)
                        {
                            targetLevel.playerAnimations.Add(new Animation());
                        }

                        // Deep copy the animation to avoid reference issues
                        targetLevel.playerAnimations[animId] = CloneAnimation(sourceAnim.Animation);
                        Console.WriteLine($"  ✅ Restored to player animations slot {animId}");
                    }

                    // Restore to Ratchet model animations
                    if (options.HasFlag(AnimationRestoreOptions.RatchetModelAnimations) && targetRatchet != null)
                    {
                        // Expand list if necessary
                        while (targetRatchet.animations.Count <= animId)
                        {
                            targetRatchet.animations.Add(new Animation());
                        }

                        // Deep copy the animation to avoid reference issues
                        targetRatchet.animations[animId] = CloneAnimation(sourceAnim.Animation);
                        Console.WriteLine($"  ✅ Restored to Ratchet model animations slot {animId}");
                    }
                }

                Console.WriteLine($"\n✅ Successfully restored {selectedAnimations.Count} animations");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during animation restoration: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates a deep copy of an animation to avoid reference issues
        /// </summary>
        private static Animation CloneAnimation(Animation source)
        {
            var clone = new Animation
            {
                unk1 = source.unk1,
                unk2 = source.unk2,
                unk3 = source.unk3,
                unk4 = source.unk4,
                unk5 = source.unk5,
                unk7 = source.unk7,
                null1 = source.null1,
                speed = source.speed,
                frames = new List<Frame>(source.frames), // Shallow copy of frames list - frames themselves are immutable in this context
                sounds = new List<int>(source.sounds),
                unknownBytes = new List<byte>(source.unknownBytes)
            };

            return clone;
        }

        /// <summary>
        /// Helper method for yes/no input
        /// </summary>
        private static bool GetYesNoInput(string prompt)
        {
            Console.Write(prompt);
            string input = Console.ReadLine() ?? "n";
            return input.Trim().ToUpper().StartsWith("Y");
        }

        /// <summary>
        /// Simplified animation restoration that works with already-loaded levels during the main geometry swap process
        /// </summary>
        /// <param name="targetLevel">Level to restore animations to</param>
        /// <param name="rc1SourceLevel">RC1 source level (NOT USED for animations in RC2→RC3 conversion)</param>
        /// <param name="animationSourceLevel">🔧 FIXED: The correct animation source level (RC3 level1 or RC2 level21)</param>
        /// <returns>True if restoration was successful</returns>
        public static bool RestoreAnimationsFromLoadedLevels(Level targetLevel, Level rc1SourceLevel, Level animationSourceLevel, AnimationRestoreOptions options = AnimationRestoreOptions.Default)
        {
            Console.WriteLine("🎭 Animation Restoration Tool (Integrated Mode)");
            Console.WriteLine("This will restore animations using the currently loaded levels.");

            try
            {
                // Build a simple animation database from loaded levels
                var animationDatabase = new Dictionary<int, List<SourceAnimation>>();

                // 🔧 CRITICAL FIX: Get the level name from the ANIMATION SOURCE LEVEL, not the RC1/RC2 source level
                string animationSourceLevelName = Path.GetFileName(Path.GetDirectoryName(animationSourceLevel.path)) ?? "Animation_Source";

                // 🔧 CRITICAL FIX: Use the correct animation source level instead of RC1 source level
                Console.WriteLine($"🔧 Using animation source: {animationSourceLevel.game.Name} from {animationSourceLevelName}");

                // Analyze animation source level animations (RC3 level1 or RC2 level21)
                if (animationSourceLevel.playerAnimations != null)
                {
                    for (int i = 0; i < animationSourceLevel.playerAnimations.Count; i++)
                    {
                        var anim = animationSourceLevel.playerAnimations[i];
                        if (anim != null && anim.frames.Count > 0)
                        {
                            if (!animationDatabase.ContainsKey(i))
                                animationDatabase[i] = new List<SourceAnimation>();

                            animationDatabase[i].Add(new SourceAnimation(i, anim, animationSourceLevelName, animationSourceLevel.game));
                        }
                    }
                }

                // Analyze animation source level Ratchet model animations
                var animationSourceRatchetModel = animationSourceLevel.mobyModels?.FirstOrDefault(m => m != null && m.id == 0) as MobyModel;
                if (animationSourceRatchetModel?.animations != null)
                {
                    for (int i = 0; i < animationSourceRatchetModel.animations.Count; i++)
                    {
                        var anim = animationSourceRatchetModel.animations[i];
                        if (anim != null && anim.frames.Count > 0)
                        {
                            if (!animationDatabase.ContainsKey(i))
                                animationDatabase[i] = new List<SourceAnimation>();

                            animationDatabase[i].Add(new SourceAnimation(i, anim, animationSourceLevelName, animationSourceLevel.game, 0, "Ratchet"));
                        }
                    }
                }

                Console.WriteLine($"📊 Analysis Results:");
                Console.WriteLine($"   Available source animations from {animationSourceLevel.game.Name}: {animationDatabase.Keys.Count}");

                // Analyze target level to find missing/broken animations
                var missingAnimations = new List<int>();
                var brokenAnimations = new List<int>();

                foreach (var animId in animationDatabase.Keys.OrderBy(x => x))
                {
                    bool targetHasPlayer = targetLevel.playerAnimations != null &&
                                         animId < targetLevel.playerAnimations.Count &&
                                         targetLevel.playerAnimations[animId] != null &&
                                         targetLevel.playerAnimations[animId].frames.Count > 0;

                    var targetRatchet = targetLevel.mobyModels?.FirstOrDefault(m => m != null && m.id == 0) as MobyModel;
                    bool targetHasRatchet = targetRatchet?.animations != null &&
                                          animId < targetRatchet.animations.Count &&
                                          targetRatchet.animations[animId] != null &&
                                          targetRatchet.animations[animId].frames.Count > 0;

                    if (!targetHasPlayer && !targetHasRatchet)
                    {
                        missingAnimations.Add(animId);
                    }
                    else if (targetHasPlayer && targetLevel.playerAnimations[animId].frames.Count == 0)
                    {
                        brokenAnimations.Add(animId);
                    }
                    else if (targetHasRatchet && targetRatchet.animations[animId].frames.Count == 0)
                    {
                        brokenAnimations.Add(animId);
                    }
                }

                Console.WriteLine($"   Missing in target: {missingAnimations.Count}");
                Console.WriteLine($"   Broken in target: {brokenAnimations.Count}");

                // 🆕 NEW: Add zero-frame detection
                Console.WriteLine("\n🔍 Checking for zero-frame animations...");
                var zeroFrameAnimations = new List<int>();

                // Check player animations
                if (targetLevel.playerAnimations != null)
                {
                    for (int i = 0; i < targetLevel.playerAnimations.Count; i++)
                    {
                        var anim = targetLevel.playerAnimations[i];
                        if (anim != null && anim.frames.Count == 0 && animationDatabase.ContainsKey(i))
                        {
                            var hasValidSource = animationDatabase[i].Any(a => a.HasData);
                            if (hasValidSource && !missingAnimations.Contains(i) && !brokenAnimations.Contains(i))
                            {
                                zeroFrameAnimations.Add(i);
                            }
                        }
                    }
                }

                // Check Ratchet model animations
                var targetRatchetForZeroFrames = targetLevel.mobyModels?.FirstOrDefault(m => m != null && m.id == 0) as MobyModel;
                if (targetRatchetForZeroFrames?.animations != null)
                {
                    for (int i = 0; i < targetRatchetForZeroFrames.animations.Count; i++)
                    {
                        var anim = targetRatchetForZeroFrames.animations[i];
                        if (anim != null && anim.frames.Count == 0 && animationDatabase.ContainsKey(i))
                        {
                            var hasValidSource = animationDatabase[i].Any(a => a.HasData);
                            if (hasValidSource && !missingAnimations.Contains(i) && !brokenAnimations.Contains(i) && !zeroFrameAnimations.Contains(i))
                            {
                                zeroFrameAnimations.Add(i);
                            }
                        }
                    }
                }

                if (zeroFrameAnimations.Count > 0)
                {
                    Console.WriteLine($"⚠️ Found {zeroFrameAnimations.Count} zero-frame animations that can be restored: {string.Join(", ", zeroFrameAnimations)}");
                    Console.WriteLine("These will be automatically restored to prevent crashes.");
                }
                else
                {
                    Console.WriteLine("✅ No problematic zero-frame animations detected.");
                }

                if (missingAnimations.Count == 0 && brokenAnimations.Count == 0 && zeroFrameAnimations.Count == 0)
                {
                    Console.WriteLine("✅ No missing, broken, or zero-frame animations detected. Target level looks good!");
                    return true;
                }

                // Auto-restore missing + broken + zero-frame animations (simplified for integration)
                var animationsToRestore = missingAnimations.Concat(brokenAnimations).Concat(zeroFrameAnimations).Distinct().ToList();

                Console.WriteLine($"\n🔧 Auto-restoring {animationsToRestore.Count} problematic animations...");

                // Restore animations
                var selectedAnimations = new Dictionary<int, SourceAnimation>();
                foreach (int animId in animationsToRestore)
                {
                    if (animationDatabase.ContainsKey(animId))
                    {
                        // 🔧 CRITICAL FIX: Now we're using the correct game animations - prioritize by frame count for quality
                        var bestAnim = animationDatabase[animId]
                            .Where(a => a.HasData)
                            .OrderByDescending(a => a.FrameCount)
                            .FirstOrDefault();

                        if (bestAnim != null)
                        {
                            selectedAnimations[animId] = bestAnim;
                            Console.WriteLine($"✅ Selected for ID {animId}: {bestAnim}");
                        }
                    }
                }

                // Perform the restoration
                if (selectedAnimations.Count > 0)
                {
                    bool success = RestoreSelectedAnimations(targetLevel, selectedAnimations, options);

                    if (success)
                    {
                        Console.WriteLine($"✅ Successfully restored {selectedAnimations.Count} {animationSourceLevel.game.Name} animations");
                        Console.WriteLine("   These animations should now work properly for grind rails and other features!");
                    }
                    else
                    {
                        Console.WriteLine("❌ Animation restoration failed");
                    }

                    return success;
                }
                else
                {
                    Console.WriteLine("No animations selected for restoration");
                    return true; // Not a failure if nothing needed restoration
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during animation restoration: {ex.Message}");
                Console.WriteLine("You can still run the full Animation Restoration Tool (option 28) separately if needed.");
                return false;
            }
        }

        /// <summary>
        /// Detects animations with zero frames and offers to restore them from source levels
        /// </summary>
        /// <param name="targetLevel">Level to check for zero-frame animations</param>
        /// <param name="animationDatabase">Database of available source animations</param>
        /// <returns>Dictionary of selected animations to restore</returns>
        private static Dictionary<int, SourceAnimation> DetectAndOfferZeroFrameRestoration(
            Level targetLevel, 
            Dictionary<int, List<SourceAnimation>> animationDatabase)
        {
            Console.WriteLine("\n🔍 Scanning for animations with zero frames...");

            var zeroFrameAnimations = new List<int>();

            // Check player animations for zero frames
            if (targetLevel.playerAnimations != null)
            {
                for (int i = 0; i < targetLevel.playerAnimations.Count; i++)
                {
                    var anim = targetLevel.playerAnimations[i];
                    if (anim != null && anim.frames.Count == 0)
                    {
                        zeroFrameAnimations.Add(i);
                    }
                }
            }

            // Check Ratchet model animations for zero frames
            var targetRatchet = targetLevel.mobyModels?.FirstOrDefault(m => m != null && m.id == 0) as MobyModel;
            if (targetRatchet?.animations != null)
            {
                for (int i = 0; i < targetRatchet.animations.Count; i++)
                {
                    var anim = targetRatchet.animations[i];
                    if (anim != null && anim.frames.Count == 0 && !zeroFrameAnimations.Contains(i))
                    {
                        zeroFrameAnimations.Add(i);
                    }
                }
            }

            if (zeroFrameAnimations.Count == 0)
            {
                Console.WriteLine("✅ No zero-frame animations detected! All animations look good.");
                return new Dictionary<int, SourceAnimation>();
            }

            Console.WriteLine($"⚠️ Found {zeroFrameAnimations.Count} animations with zero frames:");
            Console.WriteLine($"   Animation IDs: {string.Join(", ", zeroFrameAnimations.OrderBy(x => x))}");

            // Find which of these zero-frame animations can be restored from source
            var restorableZeroFrameAnimations = new List<int>();
            foreach (int animId in zeroFrameAnimations)
            {
                if (animationDatabase.ContainsKey(animId))
                {
                    var availableSourceAnims = animationDatabase[animId].Where(a => a.HasData).ToList();
                    if (availableSourceAnims.Count > 0)
                    {
                        restorableZeroFrameAnimations.Add(animId);
                    }
                }
            }

            if (restorableZeroFrameAnimations.Count == 0)
            {
                Console.WriteLine("❌ Unfortunately, none of these zero-frame animations can be restored from the available source levels.");
                return new Dictionary<int, SourceAnimation>();
            }

            Console.WriteLine($"\n✅ Good news! {restorableZeroFrameAnimations.Count} of these can be restored from source levels:");
            
            // Show what can be restored
            foreach (int animId in restorableZeroFrameAnimations.Take(10)) // Show first 10
            {
                var availableOptions = animationDatabase[animId].Where(a => a.HasData).ToList();
                var bestOption = availableOptions
                    .OrderByDescending(a => a.SourceGame.num >= 2 ? 1 : 0) // Prioritize RC2/RC3
                    .ThenByDescending(a => a.FrameCount)
                    .FirstOrDefault();
                
                if (bestOption != null)
                {
                    Console.WriteLine($"   ID {animId}: Can restore from {bestOption}");
                }
            }

            if (restorableZeroFrameAnimations.Count > 10)
            {
                Console.WriteLine($"   ... and {restorableZeroFrameAnimations.Count - 10} more");
            }

            // Ask user if they want to restore these
            Console.WriteLine($"\n🤔 Would you like to automatically restore these {restorableZeroFrameAnimations.Count} zero-frame animations?");
            Console.WriteLine("This will help prevent crashes caused by animations with no frame data.");
            Console.WriteLine("1. Yes, restore all zero-frame animations automatically");
            Console.WriteLine("2. No, leave them as-is");
            Console.Write("> ");

            string choice = Console.ReadLine()?.Trim() ?? "2";

            if (choice != "1")
            {
                Console.WriteLine("⏭️ Skipping zero-frame animation restoration");
                return new Dictionary<int, SourceAnimation>();
            }

            // Auto-select best animations for zero-frame restoration
            Console.WriteLine("\n🔧 Auto-selecting best replacements for zero-frame animations...");
            return AutoSelectAnimations(animationDatabase, restorableZeroFrameAnimations);
        }
    }
}
