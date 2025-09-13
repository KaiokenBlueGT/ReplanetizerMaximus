// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using LibReplanetizer.Serializers;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeometrySwapper
{
    /// <summary>
    /// Creates instances/placements for specified Moby models using positions from a source level.
    /// </summary>
    public static class MobyOltanisInstancer
    {
        /// <summary>
        /// Options for creating Moby instances
        /// </summary>
        [Flags]
        public enum InstancerOptions
        {
            None = 0,
            SetLightToZero = 1,
            UseRC2Template = 2,
            CopyPvars = 4,

            Default = SetLightToZero | UseRC2Template | CopyPvars
        }

        /// <summary>
        /// Creates instances of a specified Moby model using positions from a source level.
        /// </summary>
        /// <param name="targetLevel">RC2 level to modify</param>
        /// <param name="sourceLevel">The level to get positions from (can be RC1 or a converted RC2 level)</param>
        /// <param name="targetModelIds">IDs of models to create instances for</param>
        /// <param name="options">Options for creating instances</param>
        /// <returns>True if operation was successful</returns>
        public static bool CreateMobyInstancesFromLevel(
            Level targetLevel,
            Level sourceLevel,
            int[] targetModelIds,
            InstancerOptions options = InstancerOptions.Default)
        {
            if (targetLevel == null || sourceLevel == null)
            {
                Console.WriteLine("❌ Cannot create moby instances: Invalid level data");
                return false;
            }

            if (targetModelIds == null || targetModelIds.Length == 0)
            {
                Console.WriteLine("❌ No target model IDs provided");
                return false;
            }

            Console.WriteLine("\n==== Creating Moby Instances Using Source Level Positions ====");
            Console.WriteLine($"Target model IDs: {string.Join(", ", targetModelIds)}");
            Console.WriteLine($"Options: Light=0: {options.HasFlag(InstancerOptions.SetLightToZero)}, " +
                             $"UseRC2Template: {options.HasFlag(InstancerOptions.UseRC2Template)}, " +
                             $"CopyPvars: {options.HasFlag(InstancerOptions.CopyPvars)}");

            // Track results
            bool anySuccessful = false;
            int totalCreated = 0;

            // Process each model ID
            foreach (int targetModelId in targetModelIds)
            {
                // Find the model in the target level
                var mobyModel = targetLevel.mobyModels?.FirstOrDefault(m => m.id == targetModelId);
                if (mobyModel == null)
                {
                    Console.WriteLine($"❌ Model ID {targetModelId} not found in target level");
                    continue;
                }

                // Find positions in the source level to use for placement, but only for the current model ID
                var positionSources = FindPositionSources(sourceLevel, targetModelId);
                if (positionSources.Count == 0)
                {
                    Console.WriteLine($"❌ No suitable position sources found in the source level for model ID {targetModelId}");
                    continue;
                }

                Console.WriteLine($"\nCreating instances of model ID {targetModelId}");
                Console.WriteLine($"Found {positionSources.Count} potential position sources in the source level");

                // Find template moby (if enabled)
                Moby? templateMoby = null;
                if (options.HasFlag(InstancerOptions.UseRC2Template))
                {
                    // Try to find a template moby of the same model ID in the target level
                    templateMoby = targetLevel.mobs?.FirstOrDefault(m => m.modelID == targetModelId);

                    if (templateMoby == null)
                    {
                        // If not found, use any moby as a fallback template
                        templateMoby = targetLevel.mobs?.FirstOrDefault();
                    }

                    if (templateMoby != null)
                    {
                        Console.WriteLine($"Using template moby with oClass {templateMoby.mobyID} for properties");
                    }
                }

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

                // Create mobys using the source positions
                int createdCount = 0;
                int pVarsCopiedCount = 0;
                foreach (var positionSource in positionSources)
                {
                    try
                    {
                        // Create a new moby with a unique ID
                        var newMoby = new Moby(GameType.RaC2, mobyModel, positionSource.position, positionSource.rotation, positionSource.scale)
                        {
                            mobyID = nextMobyId++,

                            // Copy properties directly from the source moby instance
                            updateDistance = positionSource.updateDistance,
                            drawDistance = positionSource.drawDistance,
                            spawnBeforeDeath = true, // Default for most mobys
                            spawnType = positionSource.spawnType,
                            light = options.HasFlag(InstancerOptions.SetLightToZero) ? 0 : positionSource.light,
                            color = positionSource.color,
                            occlusion = positionSource.occlusion,
                            cutscene = positionSource.cutscene,
                            groupIndex = positionSource.groupIndex,
                            pvarIndex = positionSource.pvarIndex,

                            // Additional properties that might be important
                            missionID = templateMoby?.missionID ?? positionSource.missionID,
                            dataval = templateMoby?.dataval ?? positionSource.dataval,

                            // Set required unknown values for RC2
                            unk7A = 8192,
                            unk7B = 0,
                            unk8A = 16384,
                            unk8B = 0,
                            unk12A = 256,
                            unk12B = 0
                        };
                        newMoby.modelID = mobyModel.id;

                        // Update transform matrix
                        newMoby.UpdateTransformMatrix();

                        // Handle pVars - Prioritize copying from source (RC1) mobys when possible
                        if (options.HasFlag(InstancerOptions.CopyPvars) && positionSource.pVars != null && positionSource.pVars.Length > 0)
                        {
                            // Ensure the pVar collection exists
                            if (targetLevel.pVars == null)
                                targetLevel.pVars = new List<byte[]>();

                            // Clone the pVar data from the source moby and pad to a full 0x80-byte block
                            byte[] pVarData = (byte[])positionSource.pVars.Clone();
                            if (pVarData.Length < 0x80)
                            {
                                Array.Resize(ref pVarData, 0x80); // new bytes are zero-initialized
                            }

                            // Add the new pVar data to the target level's collection
                            targetLevel.pVars.Add(pVarData);

                            // Assign the new index directly to the moby. This is the index of the item we just added.
                            newMoby.pvarIndex = targetLevel.pVars.Count - 1;
                            newMoby.pVars = pVarData;

                            pVarsCopiedCount++;
                        }
                        else if (options.HasFlag(InstancerOptions.UseRC2Template) && templateMoby?.pvarIndex != -1 && templateMoby?.pVars != null && templateMoby.pVars.Length > 0)
                        {
                            // Fallback: Copy pVars from the RC2 template moby
                            newMoby.pVars = (byte[])templateMoby.pVars.Clone();
                            newMoby.pvarIndex = templateMoby.pvarIndex; // Here it's safe to copy the index
                        }
                        else
                        {
                            // If no pVars, ensure the index is -1 and the array is empty.
                            newMoby.pvarIndex = -1;
                            newMoby.pVars = Array.Empty<byte>();
                        }

                        // Add the new moby to the level
                        targetLevel.mobs.Add(newMoby);
                        createdCount++;

                        if (createdCount % 10 == 0 || createdCount <= 5)
                        {
                            Console.WriteLine($"  Created new moby with ID {newMoby.mobyID} at position {newMoby.position}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error creating new moby: {ex.Message}");
                    }
                }

                Console.WriteLine($"✅ Created {createdCount} instances of model ID {targetModelId}");
                if (pVarsCopiedCount > 0)
                {
                    Console.WriteLine($"  Copied pVars from {pVarsCopiedCount} source mobys");
                }
                totalCreated += createdCount;

                if (createdCount > 0)
                {
                    anySuccessful = true;

                    // Update moby IDs list in the level
                    if (targetLevel.mobyIds != null)
                    {
                        // Make sure mobyIds reflects all mobys in the level
                        targetLevel.mobyIds = targetLevel.mobs.Select(m => m.mobyID).ToList();
                    }
                }
            }

            if (anySuccessful)
            {
                Console.WriteLine($"\n✅ Successfully created {totalCreated} moby instances across all selected models");

                // Run the pvar index validation
                Console.WriteLine("\n==== Final pVar Index Validation ====");
                int pVarsBeforeCount = targetLevel.pVars?.Count ?? 0;
                MobySwapper.ValidateAndFixPvarIndices(targetLevel);
                int pVarsAfterCount = targetLevel.pVars?.Count ?? 0;
                Console.WriteLine($"pVar collection size before validation: {pVarsBeforeCount}, after: {pVarsAfterCount}");

                // Validate group ID conflicts
                MobyImporter.ValidateAndFixGroupIndexConflicts(targetLevel, false);


                return true;
            }
            else
            {
                Console.WriteLine("\n❌ Failed to create any moby instances");
                return false;
            }
        }

        /// <summary>
        /// Prepares the level for saving by fixing inconsistencies.
        /// </summary>
        private static void PrepareLevelForSave(Level level)
        {
            Console.WriteLine("\n=== Preparing Level For Save (Moby Instancer) ===");

            // 1. Make sure mobyIds are properly synced
            if (level.mobs != null)
            {
                level.mobyIds = level.mobs.Select(m => m.mobyID).ToList();
                Console.WriteLine($"  ✅ Updated mobyIds list with {level.mobyIds.Count} entries");
            }

            // 2. Fix model references for each moby
            if (level.mobs != null && level.mobyModels != null)
            {
                int fixedRefs = 0;
                foreach (var moby in level.mobs)
                {
                    if (moby.model == null || moby.model.id != moby.modelID)
                    {
                        var correctModel = level.mobyModels.FirstOrDefault(m => m.id == moby.modelID);
                        if (correctModel != null)
                        {
                            moby.model = correctModel;
                            fixedRefs++;
                        }
                    }
                }
                if (fixedRefs > 0) Console.WriteLine($"  ✅ Fixed {fixedRefs} invalid model references");
            }

            // 3. Fix pVar indices and references
            Console.WriteLine("  Running pVar index validation before save...");
            MobySwapper.ValidateAndFixPvarIndices(level);

            // 4. Ensure critical collections are not null
            if (level.pVars == null) level.pVars = new List<byte[]>();
            if (level.splines == null) level.splines = new List<Spline>();
            if (level.grindPaths == null) level.grindPaths = new List<GrindPath>();

            // 5. Clear chunk data to prevent saving them
            level.terrainChunks = new List<Terrain>();
            level.collisionChunks = new List<Collision>();
            level.collBytesChunks = new List<byte[]>();
            if (level.levelVariables != null)
            {
                level.levelVariables.chunkCount = 0;
            }

            // 6. Update transform matrices
            if (level.mobs != null)
            {
                foreach (var moby in level.mobs)
                {
                    moby.UpdateTransformMatrix();
                }
            }

            Console.WriteLine("✅ Level prepared for saving");
        }

        /// <summary>
        /// Writes a big-endian uint to a file stream.
        /// </summary>
        private static void WriteUintBigEndian(FileStream fs, uint value)
        {
            byte[] b = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            fs.Write(b, 0, 4);
        }

        /// <summary>
        /// Safely saves the level using the robust method from the geometry swapper.
        /// </summary>
        /// <param name="level">The level to save</param>
        /// <param name="outputPath">Path where the level should be saved</param>
        /// <returns>True if save was successful</returns>
        private static bool SaveLevelSafely(Level level, string outputPath)
        {
            try
            {
                string? directory = Path.GetDirectoryName(outputPath);
                if (string.IsNullOrEmpty(directory))
                {
                    Console.WriteLine("❌ Invalid output directory");
                    return false;
                }

                // Ensure directory exists
                Directory.CreateDirectory(directory);

                // Prepare the level for saving using the robust preparation method
                PrepareLevelForSave(level);

                // Save the level using the standard Level.Save method, which handles serialization correctly
                Console.WriteLine($"Saving level to {directory}...");
                level.Save(directory);

                // Patch the engine header with required values for RC2, mimicking the successful save process
                string outputEngineFile = Path.Combine(directory, "engine.ps3");
                Console.WriteLine("Patching engine.ps3 header values...");
                try
                {
                    using (var fs = File.Open(outputEngineFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        fs.Seek(0x08, SeekOrigin.Begin); WriteUintBigEndian(fs, 0x00030003);
                        fs.Seek(0x0C, SeekOrigin.Begin); WriteUintBigEndian(fs, 0x00000000);
                        fs.Seek(0xA0, SeekOrigin.Begin); WriteUintBigEndian(fs, 0xEAA60001);
                    }
                    Console.WriteLine("✅ engine.ps3 patched successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error while patching engine.ps3: {ex.Message}");
                }

                Console.WriteLine("✅ Level saved successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during safe save: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Finds suitable position sources in the source level.
        /// </summary>
        private static List<Moby> FindPositionSources(Level sourceLevel, int modelIdToFind)
        {
            if (sourceLevel.mobs == null)
            {
                Console.WriteLine($"  ⚠️ No mobs collection found in source level");
                return new List<Moby>();
            }

            // Enhanced filtering with better RC1/RC2 compatibility
            var sources = sourceLevel.mobs.Where(m => 
                m != null && 
                m.modelID == modelIdToFind &&
                IsValidMobyInstance(m, sourceLevel)
            ).ToList();

            // Log detailed information about the found sources
            int sourcesWithPvars = sources.Count(s => s.pVars != null && s.pVars.Length > 0);
            int sourcesWithValidModels = sources.Count(s => s.model != null);
            
            if (sources.Any())
            {
                Console.WriteLine($"  Found {sources.Count} source mobys for model {modelIdToFind}:");
                Console.WriteLine($"    - {sourcesWithPvars} have pVar data");
                Console.WriteLine($"    - {sourcesWithValidModels} have valid model references");
                Console.WriteLine($"    - Game type: {DetectGameType(sourceLevel)}");
                
                // Log some sample positions for verification
                var sampleSources = sources.Take(3);
                foreach (var source in sampleSources)
                {
                    Console.WriteLine($"    - Sample position: {source.position}, rotation: {source.rotation}");
                }
            }
            else
            {
                Console.WriteLine($"  ❌ No valid instances found for model {modelIdToFind}");
                Console.WriteLine($"     Available model IDs in source: {string.Join(", ", sourceLevel.mobs.Select(m => m.modelID).Distinct().OrderBy(id => id).Take(10))}...");
            }

            return sources;
        }

        /// <summary>
        /// Validates if a Moby instance is suitable for position extraction
        /// </summary>
        private static bool IsValidMobyInstance(Moby moby, Level sourceLevel)
        {
            if (moby == null) return false;

            // Check for reasonable position values (not at origin unless intentional)
            bool hasReasonablePosition = moby.position != Vector3.Zero || 
                                       sourceLevel.mobs.Count(m => m.position == Vector3.Zero) < sourceLevel.mobs.Count * 0.1f;

            // Check for valid scale (not zero or extremely small/large)
            bool hasValidScale = moby.scale.X > 0.01f && moby.scale.Y > 0.01f && moby.scale.Z > 0.01f &&
                               moby.scale.X < 100f && moby.scale.Y < 100f && moby.scale.Z < 100f;

            // Check if the model ID is reasonable (not negative or extremely high)
            bool hasValidModelId = moby.modelID >= 0 && moby.modelID < 10000;

            return hasReasonablePosition && hasValidScale && hasValidModelId;
        }

        /// <summary>
        /// Attempts to detect the game type of the source level for better compatibility
        /// </summary>
        private static string DetectGameType(Level level)
        {
            // RC1 typically has different moby ID ranges and structure
            if (level.mobs != null && level.mobs.Any())
            {
                var maxMobyId = level.mobs.Max(m => m.mobyID);
                var modelIdRange = level.mobs.Select(m => m.modelID).Distinct().Count();
                
                // RC1 characteristics (these are rough heuristics)
                if (maxMobyId < 1000 && modelIdRange < 200)
                {
                    return "RC1 (detected)";
                }
                else if (maxMobyId >= 1000 || modelIdRange >= 200)
                {
                    return "RC2/GC (detected)";
                }
            }
            
            return "Unknown";
        }

        /// <summary>
        /// Enhanced method to find compatible models across RC1/RC2 with better mapping
        /// </summary>
        public static Dictionary<int, List<int>> FindCompatibleModelMappings(Level sourceLevel, Level targetLevel)
        {
            var mappings = new Dictionary<int, List<int>>();
            
            if (sourceLevel.mobs == null || targetLevel.mobyModels == null)
            {
                Console.WriteLine("⚠️ Cannot create model mappings - missing required data");
                return mappings;
            }

            Console.WriteLine("\n==== Analyzing Model Compatibility ====");
            
            // Get all unique model IDs from source
            var sourceModelIds = sourceLevel.mobs.Select(m => m.modelID).Distinct().OrderBy(id => id).ToList();
            var targetModelIds = targetLevel.mobyModels.Select(m => m.id).Distinct().OrderBy(id => id).ToList();
            
            Console.WriteLine($"Source level has {sourceModelIds.Count} unique model types");
            Console.WriteLine($"Target level has {targetModelIds.Count} available model types");

            // Direct matches - fix the Intersect issue
            var directMatches = sourceModelIds.Where(s => targetModelIds.Contains((short)s)).ToList();
            Console.WriteLine($"Found {directMatches.Count} direct model ID matches");
            
            foreach (var modelId in directMatches)
            {
                mappings[modelId] = new List<int> { modelId };
            }
            
            // For non-matching source models, suggest potential alternatives
            var unmatchedSource = sourceModelIds.Where(s => !directMatches.Contains(s)).ToList();
            if (unmatchedSource.Any())
            {
                Console.WriteLine($"\n{unmatchedSource.Count} source models need mapping suggestions:");
                foreach (var sourceId in unmatchedSource.Take(10)) // Limit output
                {
                    var suggestions = FindSimilarModels(sourceId, targetModelIds.Select(id => (int)id).ToList());
                    if (suggestions.Any())
                    {
                        mappings[sourceId] = suggestions;
                        Console.WriteLine($"  Model {sourceId} → suggested alternatives: {string.Join(", ", suggestions)}");
                    }
                }
            }
            
            return mappings;
        }

        /// <summary>
        /// Finds potentially similar models based on ID proximity and known patterns
        /// </summary>
        private static List<int> FindSimilarModels(int sourceModelId, List<int> targetModelIds)
        {
            var suggestions = new List<int>();
            
            // Look for models with similar IDs (within range)
            var nearby = targetModelIds.Where(id => Math.Abs(id - sourceModelId) <= 50).ToList();
            suggestions.AddRange(nearby);
            
            // Add some common model type mappings if available
            if (MobySwapper.MobyTypes != null)
            {
                foreach (var category in MobySwapper.MobyTypes)
                {
                    // Convert short to int for comparison - FIX for line 506
                    if (category.Value.Contains((short)sourceModelId))
                    {
                        // Find other models in the same category that exist in target - convert shorts to ints
                        var categoryMatches = category.Value
                            .Select(id => (int)id) // Convert short to int - FIX for line 521
                            .Where(id => targetModelIds.Contains(id) && id != sourceModelId);
                        suggestions.AddRange(categoryMatches);
                        break;
                    }
                }
            }
            
            return suggestions.Distinct().OrderBy(id => Math.Abs(id - sourceModelId)).Take(3).ToList();
        }

        /// <summary>
        /// Enhanced interactive method with better RC1/RC2 level detection and model mapping
        /// </summary>
        public static bool CreateMobyInstancesInteractiveEnhanced()
        {
            Console.WriteLine("\n==== Enhanced Create Moby Instances from Source Level ====");

            // Get target level path
            Console.WriteLine("\nEnter path to the target RC2 level's engine.ps3 file:");
            Console.Write("> ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                Console.WriteLine("❌ Invalid target level path");
                return false;
            }

            // Get source level path
            Console.WriteLine("\nEnter path to the source level for placements (RC1 or RC2 engine.ps3):");
            Console.Write("> ");
            string sourceLevelPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(sourceLevelPath) || !File.Exists(sourceLevelPath))
            {
                Console.WriteLine("❌ Invalid source level path");
                return false;
            }

            // Load levels with enhanced error handling
            Level targetLevel, sourceLevel;
            try
            {
                Console.WriteLine("Loading target level...");
                targetLevel = new Level(targetPath);
                Console.WriteLine($"✅ Target level loaded - {targetLevel.mobyModels?.Count ?? 0} models, {targetLevel.mobs?.Count ?? 0} instances");
                
                Console.WriteLine("Loading source level for positions...");
                sourceLevel = new Level(sourceLevelPath);
                Console.WriteLine($"✅ Source level loaded - {sourceLevel.mobs?.Count ?? 0} instances");
                Console.WriteLine($"   Detected as: {DetectGameType(sourceLevel)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading levels: {ex.Message}");
                return false;
            }

            // Analyze compatibility between levels
            var modelMappings = FindCompatibleModelMappings(sourceLevel, targetLevel);
            
            // Display available models with enhanced categorization
            DisplayEnhancedModelList(targetLevel, sourceLevel, modelMappings);

            // Get user selection with suggestions
            Console.WriteLine("\nEnter the model IDs you want to create instances for:");
            Console.WriteLine("(comma-separated, e.g. '122,345' or 'auto' for all compatible models)");
            Console.Write("> ");
            string input = Console.ReadLine()?.Trim() ?? "";

            List<int> selectedModelIds = new List<int>();
            
            if (input.ToLower() == "auto")
            {
                // Auto-select all models that have direct matches
                selectedModelIds = modelMappings.Keys.Where(k => modelMappings[k].Contains(k)).ToList();
                Console.WriteLine($"Auto-selected {selectedModelIds.Count} compatible models: {string.Join(", ", selectedModelIds)}");
            }
            else
            {
                // Parse manual selection
                selectedModelIds = ParseModelSelection(input, targetLevel, modelMappings);
            }

            if (selectedModelIds.Count == 0)
            {
                Console.WriteLine("❌ No valid model IDs selected");
                return false;
            }

            // Enhanced options selection
            var options = GetEnhancedInstancerOptions();

            // Create instances with enhanced logging
            bool success = CreateMobyInstancesFromLevel(
                targetLevel,
                sourceLevel,
                selectedModelIds.ToArray(),
                options);

            if (success)
            {
                return HandleSaveOptions(targetLevel, targetPath);
            }

            return success;
        }

        /// <summary>
        /// Enhanced model list display with compatibility information
        /// </summary>
        private static void DisplayEnhancedModelList(Level targetLevel, Level sourceLevel, Dictionary<int, List<int>> modelMappings)
        {
            Console.WriteLine("\n==== Model Compatibility Analysis ====");
            
            if (targetLevel.mobyModels == null || targetLevel.mobyModels.Count == 0)
            {
                Console.WriteLine("No moby models found in target level!");
                return;
            }

            // Group by compatibility status
            var directMatches = modelMappings.Where(kvp => kvp.Value.Contains(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var needsMapping = modelMappings.Where(kvp => !kvp.Value.Contains(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            // Show direct matches first
            if (directMatches.Any())
            {
                Console.WriteLine($"\n✅ Direct Matches ({directMatches.Count} models):");
                foreach (var match in directMatches.OrderBy(kvp => kvp.Key))
                {
                    int sourceInstances = sourceLevel.mobs?.Count(m => m.modelID == match.Key) ?? 0;
                    int targetInstances = targetLevel.mobs?.Count(m => m.modelID == match.Key) ?? 0;
                    string modelName = GetFriendlyModelName(match.Key);
                    
                    Console.WriteLine($"  ID {match.Key}: {modelName} ({sourceInstances} source → {targetInstances} existing)");
                }
            }
            
            // Show models that need mapping
            if (needsMapping.Any())
            {
                Console.WriteLine($"\n⚠️ Need Model Mapping ({needsMapping.Count} models):");
                foreach (var mapping in needsMapping.OrderBy(kvp => kvp.Key).Take(10))
                {
                    int sourceInstances = sourceLevel.mobs?.Count(m => m.modelID == mapping.Key) ?? 0;
                    Console.WriteLine($"  ID {mapping.Key}: {sourceInstances} instances → suggestions: {string.Join(", ", mapping.Value)}");
                }
            }
        }

        /// <summary>
        /// Enhanced options selection with RC1/RC2 specific recommendations
        /// </summary>
        private static InstancerOptions GetEnhancedInstancerOptions()
        {
            Console.WriteLine("\nInstance creation options:");
            Console.WriteLine("1. Default (recommended for RC1→RC2): light=0, use RC2 template, copy pVars");
            Console.WriteLine("2. RC1 Faithful: copy all RC1 properties including lighting");
            Console.WriteLine("3. RC2 Native: use RC2 templates, ignore RC1 specific data");
            Console.WriteLine("4. Custom options");
            Console.Write("> ");
            
            string choice = Console.ReadLine()?.Trim() ?? "1";
            
            return choice switch
            {
                "2" => InstancerOptions.CopyPvars, // RC1 faithful
                "3" => InstancerOptions.UseRC2Template, // RC2 native
                "4" => GetCustomInstancerOptions(),
                _ => InstancerOptions.Default // Default
            };
        }

        /// <summary>
        /// Gets friendly model name with fallback
        /// </summary>
        private static string GetFriendlyModelName(int modelId)
        {
            if (MobySwapper.MobyTypes != null)
            {
                foreach (var entry in MobySwapper.MobyTypes)
                {
                    if (entry.Value.Contains(modelId))
                    {
                        return entry.Key;
                    }
                }
            }
            return $"Model {modelId}";
        }

        /// <summary>
        /// Parse model selection with validation and suggestions
        /// </summary>
        private static List<int> ParseModelSelection(string input, Level targetLevel, Dictionary<int, List<int>> modelMappings)
        {
            var selectedModelIds = new List<int>();
            
            foreach (string part in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int modelId))
                {
                    // Check if model exists in target
                    if (targetLevel.mobyModels.Any(m => m.id == modelId))
                    {
                        selectedModelIds.Add(modelId);
                    }
                    else if (modelMappings.ContainsKey(modelId))
                    {
                        // Suggest alternative
                        var alternatives = modelMappings[modelId];
                        Console.WriteLine($"⚠️ Model ID {modelId} not in target. Suggestions: {string.Join(", ", alternatives)}");
                        Console.Write($"Use alternative {alternatives.First()}? (y/n): ");
                        if (Console.ReadLine()?.Trim().ToLower() == "y")
                        {
                            selectedModelIds.Add(alternatives.First());
                        }
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ Warning: Model ID {modelId} not found in target level, skipping");
                    }
                }
            }
            
            return selectedModelIds;
        }

        /// <summary>
        /// Custom options configuration
        /// </summary>
        private static InstancerOptions GetCustomInstancerOptions()
        {
            InstancerOptions options = InstancerOptions.None;

            Console.Write("Set light value to 0? (y/n): ");
            if (Console.ReadLine()?.Trim().ToLower() == "y")
                options |= InstancerOptions.SetLightToZero;

            Console.Write("Use existing RC2 moby as template for properties? (y/n): ");
            if (Console.ReadLine()?.Trim().ToLower() == "y")
                options |= InstancerOptions.UseRC2Template;

            Console.Write("Copy pVars from source level mobys? (y/n): ");
            if (Console.ReadLine()?.Trim().ToLower() == "y")
                options |= InstancerOptions.CopyPvars;

            return options;
        }

        /// <summary>
        /// Handle save options with multiple choices
        /// </summary>
        private static bool HandleSaveOptions(Level targetLevel, string targetPath)
        {
            Console.WriteLine("\nHow do you want to save the modified level?");
            Console.WriteLine("1. Save changes to the target level (overwrite)");
            Console.WriteLine("2. Save as a new level file");
            Console.WriteLine("3. Don't save changes");
            Console.Write("> ");

            string saveChoice = Console.ReadLine()?.Trim() ?? "3";

            return saveChoice switch
            {
                "1" => SaveLevelSafely(targetLevel, targetPath),
                "2" => SaveAsNewLevel(targetLevel),
                _ => true // Don't save
            };
        }

        /// <summary>
        /// Save as new level with user-specified path
        /// </summary>
        private static bool SaveAsNewLevel(Level targetLevel)
        {
            Console.WriteLine("\nEnter path for the new level file (e.g. 'C:\\path\\to\\new_level\\engine.ps3'):");
            Console.Write("> ");
            string newPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(newPath))
            {
                Console.WriteLine("❌ Invalid path provided");
                return false;
            }

            Console.WriteLine($"Saving level to: {newPath}");
            return SaveLevelSafely(targetLevel, newPath);
        }
    }
}
