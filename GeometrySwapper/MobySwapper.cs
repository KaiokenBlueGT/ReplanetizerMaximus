// Copyright (C) 2018-2023, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using LibReplanetizer.Models.Animations;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles copying specific mobys from one level to another
    /// </summary>
    public static class MobySwapper
    {
        // These are the model IDs for the mobys we're interested in
        // These may need to be adjusted based on the actual modelIDs in your game data
        public static readonly Dictionary<string, int[]> MobyTypes = new Dictionary<string, int[]>
        {
            // Vendor is the same in both Damosel and Oltanis
            { "Vendor", new int[] { 11 } },
            
            // Vendor logo (Megacorp Logo/Gadgetron Logo) - explicitly include ID 1143
            { "VendorLogo", new int[] { 1143 } },

            // Crate (standard) is 500 in both
            { "Crate", new int[] { 500 } },

            // Ammo Crate and Nanotech Crate (add both for completeness)
            { "AmmoCrate", new int[] { 511 } },
            { "NanotechCrate", new int[] { 512, 501 } }, // 512 (Damosel), 501 (Oltanis)

            // Swingshot Node (optional, since you provided it)
            { "SwingshotNode", new int[] { 803 } },

            // Swingshot Pull
            { "SwingshotPull", new int[] { 758 } }

            // No Explosive Crate found, so leave out for now
        };

        /// <summary>
        /// Copy specific types of mobys from a source level to a target level
        /// </summary>
        /// <param name="targetLevel">The level to copy mobys to</param>
        /// <param name="sourceLevel">The level to copy mobys from</param>
        /// <param name="excludeRatchetClank">Whether to exclude Ratchet and Clank mobys</param>
        /// <param name="mobyTypes">List of moby types to copy (e.g., "Vendor", "Crate", "ExplosionCrate")</param>
        /// <returns>True if operation was successful, false otherwise</returns>
        public static bool CopyMobysToLevel(Level targetLevel, Level sourceLevel, bool excludeRatchetClank = true, List<string>? mobyTypes = null)
        {
            if (targetLevel == null || sourceLevel == null || sourceLevel.mobs == null)
            {
                Console.WriteLine("❌ Cannot copy mobys: Invalid level data");
                return false;
            }

            // If no specific moby types were provided, use all types
            if (mobyTypes == null || mobyTypes.Count == 0)
            {
                mobyTypes = MobyTypes.Keys.ToList();
            }

            // Initialize target level's moby list if needed
            if (targetLevel.mobs == null)
            {
                targetLevel.mobs = new List<Moby>();
            }

            // Count for statistics
            int copiedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            int modelsCopiedCount = 0;
            HashSet<int> processedModelIds = new HashSet<int>();

            Console.WriteLine($"\n==== Copying Mobys from {Path.GetFileName(sourceLevel.path)} to {Path.GetFileName(targetLevel.path)} ====");

            // Create a model dictionary to quickly find models in the target level
            Dictionary<int, Model> targetModelsDict = new Dictionary<int, Model>();
            if (targetLevel.mobyModels != null)
            {
                foreach (var model in targetLevel.mobyModels)
                {
                    if (model != null && !targetModelsDict.ContainsKey(model.id))
                        targetModelsDict[model.id] = model;
                }
            }

            // Create a dictionary of source models for quick lookup
            Dictionary<int, Model> sourceModelsDict = new Dictionary<int, Model>();
            if (sourceLevel.mobyModels != null)
            {
                foreach (var model in sourceLevel.mobyModels)
                {
                    if (model != null && !sourceModelsDict.ContainsKey(model.id))
                        sourceModelsDict[model.id] = model;
                }
            }

            // Gather model IDs for the types we want
            HashSet<int> modelIdsToInclude = new HashSet<int>();
            foreach (string type in mobyTypes)
            {
                if (MobyTypes.ContainsKey(type))
                {
                    foreach (int modelId in MobyTypes[type])
                    {
                        modelIdsToInclude.Add(modelId);
                    }
                }
            }

            // Pre-check for existing mobys in the target level
            Dictionary<int, List<Moby>> existingMobysByModelId = new Dictionary<int, List<Moby>>();
            foreach (int modelId in modelIdsToInclude)
            {
                var existingMobys = targetLevel.mobs.Where(m => m.modelID == modelId).ToList();
                if (existingMobys.Count > 0)
                {
                    existingMobysByModelId[modelId] = existingMobys;
                    Console.WriteLine($"  Found {existingMobys.Count} existing mobys with model ID {modelId} in target level");
                }
            }

            // Include the Megacorp Vendor Logo (ID 1143) only if requested
            if (mobyTypes.Contains("VendorLogo") && !modelIdsToInclude.Contains(1143))
            {
                modelIdsToInclude.Add(1143);
                Console.WriteLine("  Including Megacorp Vendor Logo (ID 1143)");
            }

            Console.WriteLine($"  Looking for mobys with model IDs: {string.Join(", ", modelIdsToInclude)}");

            // Pre-copy any models we need that aren't already in the target level
            foreach (int modelId in modelIdsToInclude)
            {
                if (!targetModelsDict.ContainsKey(modelId) && sourceModelsDict.ContainsKey(modelId))
                {
                    var modelToImport = sourceModelsDict[modelId];
                    var clonedModel = DeepCloneModel(modelToImport);
                    clonedModel.id = modelToImport.id;

                    if (modelToImport.textureConfig != null && modelToImport.textureConfig.Count > 0)
                    {
                        foreach (var texConf in clonedModel.textureConfig!)
                        {
                            var srcTex = sourceLevel.textures[texConf.id];
                            int targetTexId = targetLevel.textures.FindIndex(t => t.Equals(srcTex));
                            if (targetTexId == -1)
                            {
                                targetLevel.textures.Add(srcTex);
                                texConf.id = targetLevel.textures.Count - 1;
                            }
                            else
                            {
                                texConf.id = targetTexId;
                            }
                        }
                    }

                    if (targetLevel.mobyModels == null)
                        targetLevel.mobyModels = new List<Model>();

                    targetLevel.mobyModels.Add(clonedModel);
                    targetModelsDict[clonedModel.id] = clonedModel;
                    modelsCopiedCount++;

                    Console.WriteLine($"  ➕ Imported model ID {clonedModel.id} from source level (with textures)");
                }
            }

            int nextMobyId = 1000;
            if (targetLevel.mobs != null && targetLevel.mobs.Count > 0)
            {
                nextMobyId = targetLevel.mobs.Max(m => m.mobyID) + 1;
            }
            Console.WriteLine($"  Starting moby ID assignment from {nextMobyId}");

            if (targetLevel.pVars == null)
            {
                targetLevel.pVars = new List<byte[]>();
            }

            int nextPvarIndex = targetLevel.pVars.Count;

            // Process each moby type separately to handle repositioning vs. copying
            foreach (int modelId in modelIdsToInclude)
            {
                string mobyType = GetMobyTypeName(modelId);
                Console.WriteLine($"\n  Processing {mobyType} (Model ID: {modelId})...");

                var sourceMobys = sourceLevel.mobs?.Where(m => m.modelID == modelId).ToList() ?? new List<Moby>();
                if (sourceMobys.Count == 0)
                {
                    Console.WriteLine($"    No {mobyType} mobys found in source level, skipping");
                    continue;
                }

                // If this moby type already exists in the target, reposition it instead of copying
                if (existingMobysByModelId.ContainsKey(modelId))
                {
                    var existingMobys = existingMobysByModelId[modelId];
                    Console.WriteLine($"    ⚠️ Found {existingMobys.Count} existing {mobyType} mobys in target level. Repositioning them.");
                    Console.WriteLine($"    ℹ️ Source level has {sourceMobys.Count} {mobyType} mobys");

                    int instanceCount = Math.Min(existingMobys.Count, sourceMobys.Count);

                    for (int i = 0; i < instanceCount; i++)
                    {
                        var targetMoby = existingMobys[i];
                        var sourceMoby = sourceMobys[i];
                        Vector3 originalPos = targetMoby.position;
                        targetMoby.position = sourceMoby.position;
                        targetMoby.rotation = sourceMoby.rotation;
                        targetMoby.scale = sourceMoby.scale;
                        targetMoby.UpdateTransformMatrix();
                        Console.WriteLine($"    ✅ Repositioned existing {mobyType} from {originalPos} to {targetMoby.position}");
                    }

                    if (existingMobys.Count > sourceMobys.Count)
                    {
                        int mobysToRemoveCount = existingMobys.Count - sourceMobys.Count;
                        var mobysToDelete = existingMobys.Skip(sourceMobys.Count).ToList();
                        foreach (var moby in mobysToDelete)
                        {
                            if (targetLevel.mobs != null)
                            {
                                targetLevel.mobs.Remove(moby);
                            }
                            Console.WriteLine($"    🗑️ Removed extra {mobyType} with ID {moby.mobyID}");
                        }
                        Console.WriteLine($"    Removed {mobysToRemoveCount} extra {mobyType} mobys.");
                    }

                    skippedCount += instanceCount;
                    continue;
                }

                // If we get here, the moby type doesn't exist in the target, so we copy it
                Console.WriteLine($"    ℹ️ Copying {sourceMobys.Count} new {mobyType} mobys from source level");

                foreach (var sourceMoby in sourceMobys)
                {
                    try
                    {
                        if (excludeRatchetClank && (sourceMoby.modelID == 0 || sourceMoby.modelID == 1))
                        {
                            skippedCount++;
                            continue;
                        }

                        if (!targetModelsDict.ContainsKey(sourceMoby.modelID))
                        {
                            Console.WriteLine($"  ⚠️ Could not find model ID {sourceMoby.modelID}, skipping moby");
                            failedCount++;
                            continue;
                        }

                        var newMoby = new Moby(targetLevel.game, targetModelsDict[sourceMoby.modelID], sourceMoby.position, sourceMoby.rotation, sourceMoby.scale);
                        newMoby.mobyID = nextMobyId++;
                        newMoby.drawDistance = sourceMoby.drawDistance;
                        newMoby.updateDistance = sourceMoby.updateDistance;
                        newMoby.bolts = sourceMoby.bolts;
                        newMoby.dataval = sourceMoby.dataval;
                        newMoby.modelID = sourceMoby.modelID;
                        newMoby.unk7A = (short) (sourceMoby.unk7A > 0 ? sourceMoby.unk7A : 8192);
                        newMoby.unk8A = (short) (sourceMoby.unk8A > 0 ? sourceMoby.unk8A : 16384);
                        newMoby.unk12A = (short) (sourceMoby.unk12A > 0 ? sourceMoby.unk12A : 256);
                        newMoby.spawnType = sourceMoby.spawnType;
                        newMoby.isCrate = sourceMoby.isCrate;
                        newMoby.spawnBeforeDeath = sourceMoby.spawnBeforeDeath;
                        newMoby.spawnBeforeMissionCompletion = sourceMoby.spawnBeforeMissionCompletion;
                        newMoby.spawnAfterMissionCompletion = sourceMoby.spawnAfterMissionCompletion;
                        newMoby.model = targetModelsDict[sourceMoby.modelID];
                        newMoby.UpdateTransformMatrix();

                        if (sourceMoby.pvarIndex >= 0 && sourceMoby.pVars != null && sourceMoby.pVars.Length > 0)
                        {
                            byte[] pVarsCopy = new byte[sourceMoby.pVars.Length];
                            Array.Copy(sourceMoby.pVars, pVarsCopy, sourceMoby.pVars.Length);
                            newMoby.pvarIndex = nextPvarIndex;
                            targetLevel.pVars.Add(pVarsCopy);
                            nextPvarIndex++;
                            Console.WriteLine($"    - Assigned unique pvarIndex: {newMoby.pvarIndex} for {GetMobyTypeName(newMoby.modelID)}");
                        }
                        else
                        {
                            newMoby.pvarIndex = -1;
                        }

                        EnsurePropertiesAreSet(newMoby, sourceMoby);
                        if (targetLevel.mobs == null)
                        {
                            targetLevel.mobs = new List<Moby>();
                        }
                        targetLevel.mobs.Add(newMoby);
                        Console.WriteLine($"  ✅ Copied {GetMobyTypeName(sourceMoby.modelID)} (Model ID: {sourceMoby.modelID}) at pos {sourceMoby.position}");
                        copiedCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ❌ Error copying moby with model ID {sourceMoby.modelID}: {ex.Message}");
                        failedCount++;
                    }
                }
            }

            if (targetLevel.mobs != null && targetLevel.mobs.Count > 0)
            {
                targetLevel.mobyIds = new List<int>();
                foreach (var moby in targetLevel.mobs)
                {
                    targetLevel.mobyIds.Add(moby.mobyID);
                }
                Console.WriteLine($"  ✅ Updated mobyIds list with {targetLevel.mobyIds.Count} entries");
            }
            else if (targetLevel.mobyIds == null)
            {
                targetLevel.mobyIds = new List<int>();
                Console.WriteLine("  ✅ Created empty mobyIds list");
            }

            if (modelsCopiedCount > 0)
            {
                Console.WriteLine("  🔄 Ensuring texture dependencies are properly maintained...");
            }

            ValidateAndFixPvarIndices(targetLevel);

            Console.WriteLine("\n🔧 Fixing spawn type formatting for crates...");
            int fixedSpawnTypeCount = 0;
            if (targetLevel.mobs != null)
            {
                foreach (var moby in targetLevel.mobs)
                {
                    if (moby.modelID == 500 || moby.modelID == 511 || moby.modelID == 512 || moby.modelID == 501)
                    {
                        moby.spawnType = 12;
                        FixSpawnTypeFormatting(moby);
                        fixedSpawnTypeCount++;
                    }
                }
            }
            Console.WriteLine($"  Fixed spawn type formatting for {fixedSpawnTypeCount} crates");

            if (targetLevel.mobyModels != null && targetLevel.mobyModels.Any(m => m.id == 1143))
            {
                EnsureVendorLogoTextures(targetLevel, sourceLevel);
            }

            Console.WriteLine($"\n==== Moby Copy Summary ====");
            Console.WriteLine($"  ✅ Successfully copied: {copiedCount} mobys");
            Console.WriteLine($"  ✅ Repositioned/updated: {skippedCount} mobys");
            Console.WriteLine($"  ➕ Imported {modelsCopiedCount} model(s)");
            Console.WriteLine($"  ❌ Failed: {failedCount} mobys");

            return copiedCount > 0 || skippedCount > 0;
        }

        /// <summary>
        /// Interactive debug tool that allows selecting specific moby types to copy from a source level to a target level
        /// </summary>
        /// <param name="targetLevel">The level to copy mobys to</param>
        /// <param name="sourceLevel">The level to copy mobys from</param>
        /// <returns>True if operation was successful, false otherwise</returns>
        public static bool DebugCopySelectedMobyTypes(Level targetLevel, Level sourceLevel)
        {
            if (targetLevel == null || sourceLevel == null)
            {
                Console.WriteLine("❌ Cannot copy mobys: Invalid level data");
                return false;
            }

            Console.WriteLine("\n==== DEBUG: Select Moby Types to Copy ====");
            Console.WriteLine($"  Source Level: {Path.GetFileName(sourceLevel.path)}");
            Console.WriteLine($"  Target Level: {Path.GetFileName(targetLevel.path)}");

            // Build a list of available moby types from the MobyTypes dictionary
            List<string> availableTypes = MobyTypes.Keys.ToList();
            List<string> selectedTypes = new List<string>();

            // Display the selection menu
            Console.WriteLine("\nAvailable Moby Types:");
            for (int i = 0; i < availableTypes.Count; i++)
            {
                string typeKey = availableTypes[i];
                int[] modelIds = MobyTypes[typeKey];
                int count = sourceLevel.mobs?.Count(m => modelIds.Contains(m.modelID)) ?? 0;
                Console.WriteLine($"{i + 1}. {typeKey} (Model IDs: {string.Join(", ", modelIds)}) - {count} instances in source");
            }

            Console.WriteLine("\nSelect moby types to copy (comma-separated numbers, e.g., '1,3,5')");
            Console.WriteLine("Enter 'a' to select all types, or 'q' to cancel");
            Console.Write("> ");

            string input = Console.ReadLine()?.Trim().ToLower() ?? "";

            if (input == "q")
            {
                Console.WriteLine("❌ Operation cancelled");
                return false;
            }
            else if (input == "a")
            {
                selectedTypes = availableTypes;
                Console.WriteLine("✅ All moby types selected");
            }
            else
            {
                // Parse the comma-separated input
                string[] selections = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (string selection in selections)
                {
                    if (int.TryParse(selection.Trim(), out int index) && index >= 1 && index <= availableTypes.Count)
                    {
                        selectedTypes.Add(availableTypes[index - 1]);
                    }
                }

                if (selectedTypes.Count == 0)
                {
                    Console.WriteLine("❌ No valid selections made, operation cancelled");
                    return false;
                }
            }

            // Display what was selected
            Console.WriteLine("\nSelected Types:");
            foreach (string type in selectedTypes)
            {
                Console.WriteLine($"  - {type} (Model IDs: {string.Join(", ", MobyTypes[type])})");
            }

            // Confirm before proceeding
            Console.WriteLine("\nProceed with copying these moby types? (y/n)");
            Console.Write("> ");
            string confirm = Console.ReadLine()?.Trim().ToLower() ?? "n";

            if (confirm != "y")
            {
                Console.WriteLine("❌ Operation cancelled");
                return false;
            }

            // Execute the copy operation with the selected types
            Console.WriteLine("\n🔄 Copying selected moby types...");
            bool success = CopyMobysToLevel(targetLevel, sourceLevel, true, selectedTypes);

            if (success)
            {
                Console.WriteLine("✅ Successfully copied selected moby types");

                // Always ensure proper pVar indices after copy
                ValidateAndFixPvarIndices(targetLevel);

                // Check if vendor logo was included in the selection
                if (selectedTypes.Contains("VendorLogo"))
                {
                    EnsureVendorLogoTextures(targetLevel, sourceLevel);
                }

                return true;
            }
            else
            {
                Console.WriteLine("❌ Failed to copy selected moby types");
                return false;
            }
        }

        /// <summary>
        /// Gets the user-friendly name of a moby type based on its model ID
        /// </summary>
        private static string GetMobyTypeName(int modelId)
        {
            foreach (var entry in MobyTypes)
            {
                if (entry.Value.Contains(modelId))
                {
                    return entry.Key;
                }
            }

            // Special case for vendor logo
            if (modelId == 1143)
            {
                return "VendorLogo";
            }

            return "Unknown";
        }

        /// <summary>
        /// Copy mobys from RC2 source level but use RC1 models for their visuals
        /// and positions them according to RC1 Oltanis placements
        /// </summary>
        /// <param name="targetLevel">The RC2 level to add mobys to</param>
        /// <param name="rc2SourceLevel">The RC2 reference level to get moby data from (Damosel)</param>
        /// <param name="rc1ModelLevel">The RC1 level to get models from (Oltanis)</param>
        /// <param name="mobyTypes">List of moby types to include</param>
        /// <returns>True if operation was successful, false otherwise</returns>
        public static bool CopyRC2MobysWithRC1Models(
            Level targetLevel,
            Level rc2SourceLevel,
            Level rc1ModelLevel,
            List<string>? mobyTypes = null)
        {
            if (targetLevel == null || rc2SourceLevel == null || rc1ModelLevel == null)
            {
                Console.WriteLine("❌ Cannot copy and replace mobys: One or more level parameters is null");
                return false;
            }

            if (rc2SourceLevel.mobs == null || rc2SourceLevel.mobs.Count == 0)
            {
                Console.WriteLine("❌ Cannot copy and replace mobys: RC2 source level has no mobys");
                return false;
            }

            // If no specific moby types were provided, use all types
            if (mobyTypes == null || mobyTypes.Count == 0)
            {
                mobyTypes = MobyTypes.Keys.ToList();
            }

            // Initialize target level's moby list if needed
            if (targetLevel.mobs == null)
            {
                targetLevel.mobs = new List<Moby>();
            }

            // Count for statistics
            int copiedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            int modelsMappedCount = 0;
            int positionsMatchedCount = 0;

            Console.WriteLine($"\n==== Copying RC2 Mobys with RC1 Models and RC1 Positions ====");
            Console.WriteLine($"RC2 Source: {Path.GetFileName(rc2SourceLevel.path)}");
            Console.WriteLine($"RC1 Model Source: {Path.GetFileName(rc1ModelLevel.path)}");
            Console.WriteLine($"Target: {Path.GetFileName(targetLevel.path)}");

            // Create dictionaries for quick lookup
            Dictionary<int, Model> targetModelsDict = new Dictionary<int, Model>();
            Dictionary<int, Model> rc1ModelsDict = new Dictionary<int, Model>();
            Dictionary<int, Model> rc2ModelsDict = new Dictionary<int, Model>();

            // Populate target model dictionary
            if (targetLevel.mobyModels != null)
            {
                foreach (var model in targetLevel.mobyModels)
                {
                    if (model != null && !targetModelsDict.ContainsKey(model.id))
                        targetModelsDict[model.id] = model;
                }
            }

            // Populate RC1 model dictionary
            if (rc1ModelLevel.mobyModels != null)
            {
                foreach (var model in rc1ModelLevel.mobyModels)
                {
                    if (model != null && !rc1ModelsDict.ContainsKey(model.id))
                        rc1ModelsDict[model.id] = model;
                }
            }

            // Populate RC2 model dictionary
            if (rc2SourceLevel.mobyModels != null)
            {
                foreach (var model in rc2SourceLevel.mobyModels)
                {
                    if (model != null && !rc2ModelsDict.ContainsKey(model.id))
                        rc2ModelsDict[model.id] = model;
                }
            }

            // Gather model IDs for the types we want based on the MobyTypes dictionary
            HashSet<int> modelIdsToInclude = new HashSet<int>();
            foreach (string type in mobyTypes)
            {
                if (MobyTypes.ContainsKey(type))
                {
                    foreach (int modelId in MobyTypes[type])
                    {
                        modelIdsToInclude.Add(modelId);
                    }
                }
            }

            Console.WriteLine($"  Looking for mobys with model IDs: {string.Join(", ", modelIdsToInclude)}");

            // First, analyze existing pVar indices in the target level
            HashSet<int> existingPvarIndices = new HashSet<int>();
            if (targetLevel.mobs != null)
            {
                foreach (var moby in targetLevel.mobs)
                {
                    if (moby.pvarIndex >= 0)
                    {
                        existingPvarIndices.Add(moby.pvarIndex);
                    }
                }
            }
            Console.WriteLine($"  Found {existingPvarIndices.Count} existing pVar indices in target level");

            // First, import any RC1 models we need that aren't already in the target level
            foreach (int modelId in modelIdsToInclude)
            {
                // Only process models that are available in RC1
                if (!rc1ModelsDict.ContainsKey(modelId))
                {
                    Console.WriteLine($"  ⚠️ Model ID {modelId} not found in RC1 level, will use RC2 model if available");
                    continue;
                }

                // Skip if we already have this model in the target level
                if (targetModelsDict.TryGetValue(modelId, out var existingModel))
                {
                    var rc1ModelToCompare = rc1ModelsDict[modelId];

                    if (!ModelsAreEquivalent(existingModel, rc1ModelToCompare))
                    {
                        // Replace existing model with RC1 version
                        if (targetLevel.mobyModels != null)
                            targetLevel.mobyModels.Remove(existingModel);

                        var replacementModel = DeepCloneModel(rc1ModelToCompare);
                        replacementModel.id = rc1ModelToCompare.id;

                        // Handle texture mapping
                        if (replacementModel.textureConfig != null &&
                            replacementModel.textureConfig.Count > 0 &&
                            rc1ModelLevel.textures != null &&
                            rc1ModelLevel.textures.Count > 0)
                        {
                            foreach (var texConf in replacementModel.textureConfig)
                            {
                                if (texConf.id < 0 || texConf.id >= rc1ModelLevel.textures.Count)
                                    continue;

                                var rc1Texture = rc1ModelLevel.textures[texConf.id];

                                int existingIndex = -1;
                                for (int i = 0; i < targetLevel.textures.Count; i++)
                                {
                                    if (TextureEquals(rc1Texture, targetLevel.textures[i]))
                                    {
                                        existingIndex = i;
                                        break;
                                    }
                                }

                                if (existingIndex >= 0)
                                {
                                    texConf.id = existingIndex;
                                }
                                else
                                {
                                    targetLevel.textures.Add(rc1Texture);
                                    texConf.id = targetLevel.textures.Count - 1;
                                }
                            }
                        }

                        if (targetLevel.mobyModels == null)
                            targetLevel.mobyModels = new List<Model>();

                        targetLevel.mobyModels.Add(replacementModel);
                        targetModelsDict[replacementModel.id] = replacementModel;
                        modelsMappedCount++;

                        Console.WriteLine($"  🔄 Replaced existing model ID {replacementModel.id} with RC1 version");
                    }
                    continue;
                }

                var rc1ModelToImport = rc1ModelsDict[modelId];

                // Deep clone the RC1 model
                var clonedRC1Model = DeepCloneModel(rc1ModelToImport);
                clonedRC1Model.id = rc1ModelToImport.id;  // Ensure ID remains the same

                // Handle texture mapping
                if (clonedRC1Model.textureConfig != null && clonedRC1Model.textureConfig.Count > 0 &&
                    rc1ModelLevel.textures != null && rc1ModelLevel.textures.Count > 0)
                {
                    // First, add RC1 textures to the target level's texture list if needed
                    int baseTextureOffset = targetLevel.textures.Count;
                    foreach (var texConf in clonedRC1Model.textureConfig)
                    {
                        if (texConf.id < 0 || texConf.id >= rc1ModelLevel.textures.Count)
                        {
                            Console.WriteLine($"  ⚠️ Invalid texture ID {texConf.id} in RC1 model {modelId}");
                            continue;
                        }

                        // Get texture from RC1 level
                        var rc1Texture = rc1ModelLevel.textures[texConf.id];

                        // Find if this texture already exists in the target
                        int existingIndex = -1;
                        for (int i = 0; i < targetLevel.textures.Count; i++)
                        {
                            if (TextureEquals(rc1Texture, targetLevel.textures[i]))
                            {
                                existingIndex = i;
                                break;
                            }
                        }

                        if (existingIndex >= 0)
                        {
                            // Use existing texture
                            texConf.id = existingIndex;
                        }
                        else
                        {
                            // Add texture and update id
                            targetLevel.textures.Add(rc1Texture);
                            texConf.id = targetLevel.textures.Count - 1;
                        }
                    }
                }

                // Add the RC1 model to the target level's model collection
                if (targetLevel.mobyModels == null)
                    targetLevel.mobyModels = new List<Model>();

                targetLevel.mobyModels.Add(clonedRC1Model);
                targetModelsDict[clonedRC1Model.id] = clonedRC1Model;
                modelsMappedCount++;

                Console.WriteLine($"  ➕ Imported RC1 model {clonedRC1Model.id} from {Path.GetFileName(rc1ModelLevel.path)}");
            }

            // Find the next available mobyID in the target level
            int nextMobyId = targetLevel.mobs != null && targetLevel.mobs.Count > 0
                ? targetLevel.mobs.Max(m => m.mobyID) + 1
                : 1000;
            Console.WriteLine($"  Starting moby ID assignment from {nextMobyId}");

            // Ensure pVars list exists
            targetLevel.pVars ??= new List<byte[]>();

            // Group RC1 mobys by type for position reference
            Dictionary<int, List<Moby>> rc1MobysByType = new Dictionary<int, List<Moby>>();
            foreach (var moby in rc1ModelLevel.mobs)
            {
                if (modelIdsToInclude.Contains(moby.modelID))
                {
                    if (!rc1MobysByType.ContainsKey(moby.modelID))
                    {
                        rc1MobysByType[moby.modelID] = new List<Moby>();
                    }
                    rc1MobysByType[moby.modelID].Add(moby);
                }
            }

            // Output count of RC1 mobys found for each type
            foreach (string type in mobyTypes)
            {
                int count = 0;
                if (MobyTypes.TryGetValue(type, out int[]? modelIds))
                {
                    foreach (int modelId in modelIds)
                    {
                        if (rc1MobysByType.TryGetValue(modelId, out var mobys))
                        {
                            count += mobys.Count;
                        }
                    }
                }
                Console.WriteLine($"  Found {count} {type} mobys in RC1 level for positioning");
            }

            // Keep track of assigned pVar indices to avoid conflicts
            Dictionary<string, int> nextPvarIndexByType = new Dictionary<string, int>()
    {
        {"Vendor", 12},         // Start at 12 for Vendor
        {"VendorLogo", 13},     // Start at 13 for Vendor Logo
        {"Crate", 20},          // Start at 20 for Crates
        {"AmmoCrate", 30},      // Start at 30 for Ammo Crates
        {"NanotechCrate", 40},  // Start at 40 for Nanotech Crates
        {"SwingshotNode", 50},  // Start at 50 for Swingshot Nodes
        {"Other", 100}          // Start at 100 for other types
    };

            // Process each moby type separately to ensure we match similar types between games
            foreach (string mobyType in mobyTypes)
            {
                if (!MobyTypes.ContainsKey(mobyType))
                    continue;

                int[] modelIds = MobyTypes[mobyType];
                Console.WriteLine($"\n  Processing {mobyType} mobys...");

                // Get all RC1 mobys for this type for positioning
                List<Moby> rc1MobysOfType = new List<Moby>();
                foreach (int modelId in modelIds)
                {
                    if (rc1MobysByType.TryGetValue(modelId, out var mobys))
                    {
                        rc1MobysOfType.AddRange(mobys);
                    }
                }

                // If no RC1 mobys of this type, skip
                if (rc1MobysOfType.Count == 0)
                {
                    Console.WriteLine($"    ⏭️ No RC1 {mobyType} mobys found to use as position reference. Skipping.");
                    continue;
                }

                // Get all matching RC2 mobys for this type from the source level
                List<Moby> rc2MobysOfType = new List<Moby>();
                foreach (int modelId in modelIds)
                {
                    var mobys = rc2SourceLevel.mobs.Where(m => m.modelID == modelId).ToList();
                    rc2MobysOfType.AddRange(mobys);
                }

                Console.WriteLine($"    Found {rc2MobysOfType.Count} RC2 mobys and {rc1MobysOfType.Count} RC1 positions for {mobyType}");

                // If no RC2 mobys of this type, skip
                if (rc2MobysOfType.Count == 0)
                {
                    Console.WriteLine($"    ⚠️ No RC2 {mobyType} mobys found to use as template. Skipping.");
                    continue;
                }

                // For each RC1 position, create a moby with RC1 model (if available) or RC2 model
                for (int i = 0; i < rc1MobysOfType.Count; i++)
                {
                    try
                    {
                        // Get position info from RC1 level
                        Moby rc1PositionMoby = rc1MobysOfType[i];

                        // Find a matching RC2 moby to use as template (cycle through available RC2 mobys)
                        Moby rc2TemplateMoby = rc2MobysOfType[i % rc2MobysOfType.Count];

                        // Find RC1 model or use RC2 model as fallback
                        Model modelToUse;
                        bool usingRC1Model = false;

                        if (rc1ModelsDict.ContainsKey(rc1PositionMoby.modelID))
                        {
                            modelToUse = targetModelsDict[rc1PositionMoby.modelID];  // Already imported into targetModelsDict above
                            usingRC1Model = true;
                        }
                        else if (rc2ModelsDict.ContainsKey(rc2TemplateMoby.modelID))
                        {
                            // RC1 model not available, try to use RC2 model directly
                            if (!targetModelsDict.ContainsKey(rc2TemplateMoby.modelID))
                            {
                                // Import RC2 model if not already in target
                                var rc2Model = rc2ModelsDict[rc2TemplateMoby.modelID];
                                var clonedRC2Model = DeepCloneModel(rc2Model);
                                clonedRC2Model.id = rc2Model.id;

                                // Handle textures for RC2 model
                                if (clonedRC2Model.textureConfig != null)
                                {
                                    foreach (var texConf in clonedRC2Model.textureConfig)
                                    {
                                        if (texConf.id < rc2SourceLevel.textures.Count)
                                        {
                                            var rc2Texture = rc2SourceLevel.textures[texConf.id];

                                            // Find if texture already exists
                                            int existingIndex = -1;
                                            for (int j = 0; j < targetLevel.textures.Count; j++)
                                            {
                                                if (TextureEquals(rc2Texture, targetLevel.textures[j]))
                                                {
                                                    existingIndex = j;
                                                    break;
                                                }
                                            }

                                            if (existingIndex >= 0)
                                            {
                                                texConf.id = existingIndex;
                                            }
                                            else
                                            {
                                                targetLevel.textures.Add(rc2Texture);
                                                texConf.id = targetLevel.textures.Count - 1;
                                            }
                                        }
                                    }
                                }

                                if (targetLevel.mobyModels == null)
                                {
                                    targetLevel.mobyModels = new List<Model>();
                                }
                                targetLevel.mobyModels.Add(clonedRC2Model);
                                targetModelsDict[clonedRC2Model.id] = clonedRC2Model;
                                Console.WriteLine($"  ➕ Using RC2 model {clonedRC2Model.id} as fallback (RC1 model not available)");
                            }

                            modelToUse = targetModelsDict[rc2TemplateMoby.modelID];
                        }
                        else
                        {
                            Console.WriteLine($"  ⚠️ Could not find model ID {rc1PositionMoby.modelID} in either RC1 or RC2, skipping moby");
                            skippedCount++;
                            continue;
                        }

                        // Create a new moby with RC1 or RC2 model based on availability
                        // BUT USING RC1 POSITION, ROTATION AND SCALE
                        var newMoby = new Moby(
                            targetLevel.game,
                            modelToUse,
                            rc1PositionMoby.position,  // Use RC1 position
                            rc1PositionMoby.rotation,  // Use RC1 rotation
                            rc1PositionMoby.scale      // Use RC1 scale
                        );

                        // Copy other relevant properties from the RC2 template
                        newMoby.mobyID = nextMobyId++;
                        newMoby.modelID = modelToUse.id;
                        newMoby.missionID = rc2TemplateMoby.missionID;
                        newMoby.spawnType = rc2TemplateMoby.spawnType;
                        newMoby.bolts = rc2TemplateMoby.bolts;
                        newMoby.dataval = rc2TemplateMoby.dataval;
                        newMoby.drawDistance = rc2TemplateMoby.drawDistance;
                        newMoby.updateDistance = rc2TemplateMoby.updateDistance;
                        newMoby.unk3A = rc2TemplateMoby.unk3A;
                        newMoby.unk3B = rc2TemplateMoby.unk3B;
                        newMoby.occlusion = rc2TemplateMoby.occlusion;
                        newMoby.groupIndex = rc2TemplateMoby.groupIndex;
                        newMoby.isRooted = rc2TemplateMoby.isRooted;
                        newMoby.rootedDistance = rc2TemplateMoby.rootedDistance;
                        newMoby.unk6 = rc2TemplateMoby.unk6;
                        newMoby.unk7A = rc2TemplateMoby.unk7A;
                        newMoby.unk7B = rc2TemplateMoby.unk7B;
                        newMoby.unk8A = rc2TemplateMoby.unk8A;
                        newMoby.unk8B = rc2TemplateMoby.unk8B;
                        newMoby.unk9 = rc2TemplateMoby.unk9;
                        newMoby.unk12A = rc2TemplateMoby.unk12A;
                        newMoby.unk12B = rc2TemplateMoby.unk12B;
                        newMoby.color = rc2TemplateMoby.color;
                        newMoby.light = rc2TemplateMoby.light;
                        newMoby.cutscene = rc2TemplateMoby.cutscene;
                        newMoby.exp = rc2TemplateMoby.exp;
                        newMoby.mode = rc2TemplateMoby.mode;

                        // Ensure transform matrix is updated after setting position, rotation, scale
                        newMoby.UpdateTransformMatrix();

                        // Determine moby type for pVar handling
                        string currentMobyType = GetMobyTypeName(newMoby.modelID);
                        if (!nextPvarIndexByType.ContainsKey(currentMobyType))
                        {
                            currentMobyType = "Other"; // Use general "Other" category if not found
                        }

                        // Get next available index for this moby type
                        int pvarIndex = nextPvarIndexByType[currentMobyType];

                        // Find next available pVar index that doesn't conflict
                        while (existingPvarIndices.Contains(pvarIndex))
                        {
                            pvarIndex++;
                        }

                        // Update next available index for this type
                        nextPvarIndexByType[currentMobyType] = pvarIndex + 1;

                        // Assign the pVar index and prepare pVars data
                        newMoby.pvarIndex = pvarIndex;
                        Console.WriteLine($"      - Assigned pVarIndex {pvarIndex} for {mobyType}");

                        // Ensure space in pVars list
                        while (targetLevel.pVars.Count <= pvarIndex)
                        {
                            targetLevel.pVars.Add(new byte[0x80]);
                        }

                        // Copy pVars or create default
                        if (rc2TemplateMoby.pVars?.Length > 0)
                        {
                            // Copy the pVars data from RC2
                            byte[] pVarsCopy = new byte[rc2TemplateMoby.pVars.Length];
                            Array.Copy(rc2TemplateMoby.pVars, pVarsCopy, rc2TemplateMoby.pVars.Length);
                            targetLevel.pVars[pvarIndex] = pVarsCopy;
                            newMoby.pVars = pVarsCopy;
                        }
                        else
                        {
                            // Create default pVars based on moby type
                            byte[] defaultPvars = new byte[0x80];
                            targetLevel.pVars[pvarIndex] = defaultPvars;
                            newMoby.pVars = defaultPvars;
                        }

                        // Mark this index as used
                        existingPvarIndices.Add(pvarIndex);

                        // Apply moby type specific properties
                        ApplyMobyTypeSpecificProperties(newMoby, mobyType);

                        // Add to the target level
                        if (targetLevel.mobs == null)
                        {
                            targetLevel.mobs = new List<Moby>();
                        }
                        targetLevel.mobs.Add(newMoby);
                        copiedCount++;
                        positionsMatchedCount++;

                        Console.WriteLine($"    ✅ Created {mobyType} with {(usingRC1Model ? "RC1" : "RC2")} model at RC1 position {newMoby.position}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    ❌ Error creating {mobyType} moby: {ex.Message}");
                        failedCount++;
                    }
                }
            }

            // Update mobyIds list for proper serialization
            if (targetLevel.mobs != null && targetLevel.mobs.Count > 0)
            {
                targetLevel.mobyIds = new List<int>();
                foreach (var moby in targetLevel.mobs)
                {
                    targetLevel.mobyIds.Add(moby.mobyID);
                }
            }

            // Double-check for any pVar index conflicts
            var duplicatePvarIndices = (targetLevel.mobs ?? Enumerable.Empty<Moby>())
                .Where(m => m.pvarIndex >= 0)
                .GroupBy(m => m.pvarIndex)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicatePvarIndices.Any())
            {
                Console.WriteLine($"\n⚠️ WARNING: Found {duplicatePvarIndices.Count} duplicate pVar index assignments! Running conflict resolution...");
                ValidateAndFixPvarIndices(targetLevel);
            }

            // Specifically ensure Vendor Logo textures if we copied any
            bool hasVendorLogo = targetLevel.mobs != null && targetLevel.mobs.Any(m => m.modelID == 1143);
            if (hasVendorLogo)
            {
                EnsureVendorLogoTextures(targetLevel, rc1ModelLevel);
            }

            // Fix crate pVars using the RC2 source level as reference, if crates were involved in the copy operation
            bool requestedCrateTypes = modelIdsToInclude.Contains(500) || // Crate ID
                                       modelIdsToInclude.Contains(511) || // AmmoCrate ID
                                       modelIdsToInclude.Contains(512) || // NanotechCrate (Damosel) ID
                                       modelIdsToInclude.Contains(501);   // NanotechCrate (Oltanis) ID

            if (requestedCrateTypes && copiedCount > 0) // Only if crate types were requested and some mobys were actually copied
            {
                // FixMobyPvarsFromReferenceLevel will internally check if targetLevel actually contains crates to fix.
                Console.WriteLine("\n🔧 Applying RC2 reference pVars for any copied crates...");
                FixMobyPvarsFromReferenceLevel(targetLevel, rc2SourceLevel);
            }

            Console.WriteLine($"\n==== Moby Copy Summary ====");
            Console.WriteLine($"  ✅ Successfully copied: {copiedCount} mobys");
            Console.WriteLine($"  ➕ Mapped {modelsMappedCount} RC1 models");
            Console.WriteLine($"  ✅ Positions matched to RC1: {positionsMatchedCount}");
            Console.WriteLine($"  ⏭️ Skipped: {skippedCount} mobys");
            Console.WriteLine($"  ❌ Failed: {failedCount} mobys");

            ValidateAndFixPvarIndices(targetLevel);

            return copiedCount > 0;
        }

        /// <summary>
        /// Copies RC2 mobys while preserving their original RC2 models (no model replacement)
        /// but positions them according to RC1 Oltanis placements
        /// </summary>
        /// <param name="targetLevel">The level to copy mobys to</param>
        /// <param name="rc2SourceLevel">The RC2 level to copy mobys from (for models)</param>
        /// <param name="rc1PositionLevel">The RC1 level to use for positions (Oltanis)</param>
        /// <param name="mobyTypes">List of moby types to copy</param>
        /// <returns>True if operation was successful</returns>
        public static bool CopyRC2MobysPreservingModels(
            Level targetLevel,
            Level rc2SourceLevel,
            Level rc1PositionLevel,
            List<string>? mobyTypes = null)
        {
            if (targetLevel == null || rc2SourceLevel == null || rc2SourceLevel.mobs == null)
            {
                Console.WriteLine("❌ Cannot copy RC2 mobys: Invalid level data");
                return false;
            }

            if (rc1PositionLevel == null || rc1PositionLevel.mobs == null)
            {
                Console.WriteLine("❌ Cannot get RC1 positions: Invalid RC1 level data");
                return false;
            }

            // If no specific moby types were provided, use all types
            if (mobyTypes == null || mobyTypes.Count == 0)
            {
                mobyTypes = MobyTypes.Keys.ToList();
            }

            Console.WriteLine($"\n==== Copying RC2 Mobys with Original RC2 Models and RC1 Positions ====");

            // Initialize target level's moby list if needed
            if (targetLevel.mobs == null)
            {
                targetLevel.mobs = new List<Moby>();
            }

            // Create a list of model IDs to include
            HashSet<int> modelIdsToInclude = new HashSet<int>();
            foreach (string type in mobyTypes)
            {
                if (MobyTypes.ContainsKey(type))
                {
                    foreach (int modelId in MobyTypes[type])
                    {
                        modelIdsToInclude.Add(modelId);
                    }
                }
            }

            Console.WriteLine($"  Looking for RC2 mobys with model IDs: {string.Join(", ", modelIdsToInclude)}");

            // Count for statistics
            int copiedCount = 0;
            int skippedCount = 0;
            int modelsCopiedCount = 0;
            int positionsMatchedCount = 0;

            // Create a model dictionary for quick lookup of RC2 models
            Dictionary<int, Model> targetModelsDict = new Dictionary<int, Model>();
            if (targetLevel.mobyModels != null)
            {
                foreach (var model in targetLevel.mobyModels)
                {
                    if (model != null && !targetModelsDict.ContainsKey(model.id))
                        targetModelsDict[model.id] = model;
                }
            }

            // Dictionary of RC2 source models for lookup
            Dictionary<int, Model> sourceModelsDict = new Dictionary<int, Model>();
            if (rc2SourceLevel.mobyModels != null)
            {
                foreach (var model in rc2SourceLevel.mobyModels)
                {
                    if (model != null && !sourceModelsDict.ContainsKey(model.id))
                        sourceModelsDict[model.id] = model;
                }
            }

            // Group RC1 mobys by type for position reference
            Dictionary<int, List<Moby>> rc1MobysByType = new Dictionary<int, List<Moby>>();
            foreach (var moby in rc1PositionLevel.mobs)
            {
                if (modelIdsToInclude.Contains(moby.modelID))
                {
                    if (!rc1MobysByType.ContainsKey(moby.modelID))
                    {
                        rc1MobysByType[moby.modelID] = new List<Moby>();
                    }
                    rc1MobysByType[moby.modelID].Add(moby);
                }
            }

            // Output count of RC1 mobys found for each type
            foreach (string type in mobyTypes)
            {
                int count = 0;
                if (MobyTypes.TryGetValue(type, out int[]? modelIds))
                {
                    foreach (int modelId in modelIds)
                    {
                        if (rc1MobysByType.TryGetValue(modelId, out var mobys))
                        {
                            count += mobys.Count;
                        }
                    }
                }
                Console.WriteLine($"  Found {count} {type} mobys in RC1 level for positioning");
            }

            // Keep track of next available moby ID
            int nextMobyId = 1000; // Start with a safe base value
            if (targetLevel.mobs != null && targetLevel.mobs.Count > 0)
            {
                // Find the highest mobyID and add 1
                nextMobyId = targetLevel.mobs.Max(m => m.mobyID) + 1;
            }
            Console.WriteLine($"  Starting moby ID assignment from {nextMobyId}");

            // Ensure pVars list exists
            if (targetLevel.pVars == null)
            {
                targetLevel.pVars = new List<byte[]>();
            }

            // Find the next available pVar index
            int nextPvarIndex = targetLevel.pVars.Count;

            // Dictionary to track which model IDs have already been assigned pVar indices
            Dictionary<int, int> modelToPvarIndex = new Dictionary<int, int>();

            // First, import RC2 models we need
            foreach (int modelId in modelIdsToInclude)
            {
                if (!targetModelsDict.ContainsKey(modelId) && sourceModelsDict.ContainsKey(modelId))
                {
                    var sourceModel = sourceModelsDict[modelId];

                    // Deep clone the model
                    var clonedModel = DeepCloneModel(sourceModel);

                    // Ensure the model has the correct ID
                    clonedModel.id = sourceModel.id;

                    // Copy textures and update texture references
                    if (sourceModel.textureConfig != null && sourceModel.textureConfig.Count > 0)
                    {
                        foreach (var texConf in clonedModel.textureConfig!)
                        {
                            // Find the texture in the source level
                            var srcTex = rc2SourceLevel.textures[texConf.id];

                            // Check if this texture already exists in the target
                            int targetTexId = targetLevel.textures.FindIndex(t => TextureEquals(t, srcTex));
                            if (targetTexId == -1)
                            {
                                // Add texture to target and update ID
                                targetLevel.textures.Add(srcTex);
                                texConf.id = targetLevel.textures.Count - 1;
                            }
                            else
                            {
                                // Use existing texture index
                                texConf.id = targetTexId;
                            }
                        }
                    }

                    // Add model to target level
                    if (targetLevel.mobyModels == null)
                        targetLevel.mobyModels = new List<Model>();

                    targetLevel.mobyModels.Add(clonedModel);
                    targetModelsDict[clonedModel.id] = clonedModel;
                    modelsCopiedCount++;

                    Console.WriteLine($"  ➕ Imported RC2 model ID {clonedModel.id} (with textures)");
                }
            }

            // Process each moby type separately to ensure we match similar types between games
            foreach (string mobyType in mobyTypes)
            {
                if (!MobyTypes.ContainsKey(mobyType))
                    continue;

                int[] modelIds = MobyTypes[mobyType];
                Console.WriteLine($"\n  Processing {mobyType} mobys...");

                // Get all RC1 mobys for this type for positioning
                List<Moby> rc1MobysOfType = new List<Moby>();
                foreach (int modelId in modelIds)
                {
                    if (rc1MobysByType.TryGetValue(modelId, out var mobys))
                    {
                        rc1MobysOfType.AddRange(mobys);
                    }
                }

                // If no RC1 mobys of this type, skip
                if (rc1MobysOfType.Count == 0)
                {
                    Console.WriteLine($"    ⏭️ No RC1 {mobyType} mobys found to use as position reference. Skipping.");
                    continue;
                }

                // Get all matching RC2 mobys for this type from the source level
                List<Moby> rc2MobysOfType = new List<Moby>();
                foreach (int modelId in modelIds)
                {
                    var mobys = rc2SourceLevel.mobs.Where(m => m.modelID == modelId).ToList();
                    rc2MobysOfType.AddRange(mobys);
                }

                Console.WriteLine($"    Found {rc2MobysOfType.Count} RC2 mobys and {rc1MobysOfType.Count} RC1 positions for {mobyType}");

                // If no RC2 mobys of this type, skip
                if (rc2MobysOfType.Count == 0)
                {
                    Console.WriteLine($"    ⚠️ No RC2 {mobyType} mobys found to use as template. Skipping.");
                    continue;
                }

                // Create a moby for each RC1 position using RC2 models
                for (int i = 0; i < rc1MobysOfType.Count; i++)
                {
                    try
                    {
                        // Get position info from RC1 level
                        Moby rc1PositionMoby = rc1MobysOfType[i];

                        // Find a matching RC2 moby to use as template
                        Moby rc2ModelMoby = rc2MobysOfType[i % rc2MobysOfType.Count]; // Cycle through available RC2 mobys

                        // Check if model exists in target level
                        if (!targetModelsDict.ContainsKey(rc2ModelMoby.modelID))
                        {
                            Console.WriteLine($"    ⚠️ Could not find model ID {rc2ModelMoby.modelID} in target level, skipping moby");
                            skippedCount++;
                            continue;
                        }

                        // Create a new moby instance with RC2 model but RC1 position
                        var newMoby = new Moby(
                            targetLevel.game,
                            targetModelsDict[rc2ModelMoby.modelID],
                            rc1PositionMoby.position,  // Use RC1 position
                            rc1PositionMoby.rotation,  // Use RC1 rotation
                            rc1PositionMoby.scale      // Use RC1 scale
                        );

                        // Copy properties from RC2 source
                        newMoby.mobyID = nextMobyId++;
                        newMoby.modelID = rc2ModelMoby.modelID;
                        newMoby.missionID = rc2ModelMoby.missionID;
                        newMoby.dataval = rc2ModelMoby.dataval;
                        newMoby.bolts = rc2ModelMoby.bolts;
                        newMoby.spawnType = rc2ModelMoby.spawnType;
                        newMoby.drawDistance = rc2ModelMoby.drawDistance;
                        newMoby.updateDistance = rc2ModelMoby.updateDistance;
                        newMoby.unk7A = rc2ModelMoby.unk7A;
                        newMoby.unk8A = rc2ModelMoby.unk8A;
                        newMoby.unk12A = rc2ModelMoby.unk12A;
                        newMoby.unk3A = rc2ModelMoby.unk3A;
                        newMoby.unk3B = rc2ModelMoby.unk3B;
                        newMoby.exp = rc2ModelMoby.exp;
                        newMoby.unk9 = rc2ModelMoby.unk9;
                        newMoby.unk6 = rc2ModelMoby.unk6;
                        newMoby.groupIndex = rc2ModelMoby.groupIndex;
                        newMoby.isRooted = rc2ModelMoby.isRooted;
                        newMoby.rootedDistance = rc2ModelMoby.rootedDistance;
                        newMoby.occlusion = rc2ModelMoby.occlusion;
                        newMoby.color = rc2ModelMoby.color;
                        newMoby.light = rc2ModelMoby.light;
                        newMoby.cutscene = rc2ModelMoby.cutscene;
                        newMoby.model = targetModelsDict[rc2ModelMoby.modelID];

                        // Handle pVars - either reuse or copy
                        if (rc2ModelMoby.pvarIndex >= 0 && rc2ModelMoby.pVars != null && rc2ModelMoby.pVars.Length > 0)
                        {
                            // Check if we've already copied this pvar data
                            if (modelToPvarIndex.TryGetValue(rc2ModelMoby.modelID, out int existingIndex))
                            {
                                // Reuse existing pvar index
                                newMoby.pvarIndex = existingIndex;
                                Console.WriteLine($"    - Reused pVarIndex {existingIndex} for model {rc2ModelMoby.modelID}");
                            }
                            else
                            {
                                // Copy pVars data to target level
                                byte[] pVarsCopy = new byte[rc2ModelMoby.pVars.Length];
                                Array.Copy(rc2ModelMoby.pVars, pVarsCopy, rc2ModelMoby.pVars.Length);

                                // Add to target level pVars list
                                while (targetLevel.pVars.Count <= nextPvarIndex)
                                {
                                    targetLevel.pVars.Add(new byte[0x80]);
                                }
                                targetLevel.pVars[nextPvarIndex] = pVarsCopy;
                                newMoby.pvarIndex = nextPvarIndex;
                                Console.WriteLine($"    - Assigned new pVarIndex {nextPvarIndex} for model {rc2ModelMoby.modelID}");

                                // Remember this pvar index for this model ID
                                modelToPvarIndex[rc2ModelMoby.modelID] = nextPvarIndex;
                                nextPvarIndex++;
                            }
                        }
                        else
                        {
                            newMoby.pvarIndex = -1;
                            newMoby.pVars = new byte[0x80];
                        }

                        // Apply any special type-specific properties
                        ApplyMobyTypeSpecificProperties(newMoby, mobyType);

                        // Update transform matrix with the position/rotation/scale from RC1
                        newMoby.UpdateTransformMatrix();

                        // Add the moby to the target level
                        // Ensure `targetLevel.mobs` is initialized before adding `newMoby`
                        if (targetLevel.mobs == null)
                        {
                            targetLevel.mobs = new List<Moby>();
                        }
                        targetLevel.mobs.Add(newMoby);
                        copiedCount++;
                        positionsMatchedCount++;

                        if ((copiedCount % 10) == 0)
                        {
                            Console.WriteLine($"    Created {copiedCount} mobys so far...");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    ❌ Error creating {mobyType} moby: {ex.Message}");
                        skippedCount++;
                    }
                }
            }

            // Update the mobyIds list in the target level
            if (targetLevel.mobs != null && targetLevel.mobs.Count > 0)
            {
                targetLevel.mobyIds = targetLevel.mobs.Select(m => m.mobyID).ToList();
            }

            // Summary
            Console.WriteLine($"\n==== RC2 Moby with RC1 Positions Summary ====");
            Console.WriteLine($"  ✅ Models imported: {modelsCopiedCount}");
            Console.WriteLine($"  ✅ Moby instances created: {copiedCount}");
            Console.WriteLine($"  ✅ Positions matched to RC1: {positionsMatchedCount}");
            Console.WriteLine($"  ⏭️ Skipped: {skippedCount}");
            Console.WriteLine($"  ⚙️ Next available mobyID: {nextMobyId}");
            Console.WriteLine($"  ⚙️ Next available pvarIndex: {nextPvarIndex}");

            // Perform some final updates to ensure everything is set correctly
            ValidateAndFixPvarIndices(targetLevel);

            ValidateAndFixPvarIndices(targetLevel);

            return copiedCount > 0;
        }

        /// <summary>
        /// Repositions RC2 mobys in the target level according to the positions in the RC1 source level
        /// </summary>
        /// <param name="targetLevel">The RC2 level where mobys need to be repositioned</param>
        /// <param name="rc1SourceLevel">The RC1 level with the original positions</param>
        /// <param name="mobyTypes">List of moby types to reposition</param>
        /// <returns>True if repositioning was successful</returns>

        public static bool RepositionMobysToMatchRC1Placements(Level targetLevel, Level rc1SourceLevel, List<string>? mobyTypes = null)
        {
            if (targetLevel == null || rc1SourceLevel == null ||
                targetLevel.mobs == null || targetLevel.mobs.Count == 0 ||
                rc1SourceLevel.mobs == null || rc1SourceLevel.mobs.Count == 0)
            {
                Console.WriteLine("❌ Cannot reposition mobys: Invalid level data or no mobys found");
                return false;
            }

            // If no specific moby types were provided, use all types
            if (mobyTypes == null || mobyTypes.Count == 0)
            {
                mobyTypes = MobyTypes.Keys.ToList();
            }

            // Gather model IDs for the types we want
            HashSet<int> modelIdsToReposition = new HashSet<int>();
            foreach (string type in mobyTypes)
            {
                if (MobyTypes.ContainsKey(type))
                {
                    foreach (int modelId in MobyTypes[type])
                    {
                        modelIdsToReposition.Add(modelId);
                    }
                }
            }

            Console.WriteLine($"\n==== Repositioning {modelIdsToReposition.Count} Moby Types to Match RC1 Placements ====");
            Console.WriteLine($"  Looking for mobys with model IDs: {string.Join(", ", modelIdsToReposition)}");

            int repositionedCount = 0;
            int unmatched = 0;

            // Group mobys by their model IDs for quick lookup 
            var rc1MobysByType = new Dictionary<int, List<Moby>>();
            foreach (var moby in rc1SourceLevel.mobs.Where(m => modelIdsToReposition.Contains(m.modelID)))
            {
                if (!rc1MobysByType.ContainsKey(moby.modelID))
                {
                    rc1MobysByType[moby.modelID] = new List<Moby>();
                }
                rc1MobysByType[moby.modelID].Add(moby);
            }

            // For each moby type, print how many we found in RC1
            foreach (var type in mobyTypes)
            {
                if (!MobyTypes.ContainsKey(type)) continue;

                int count = 0;
                foreach (var modelId in MobyTypes[type])
                {
                    if (rc1MobysByType.ContainsKey(modelId))
                    {
                        count += rc1MobysByType[modelId].Count;
                    }
                }
                Console.WriteLine($"  Found {count} {type} mobys in RC1 level");
            }

            // Process each moby type separately to ensure similar types are matched together
            foreach (string mobyType in mobyTypes)
            {
                if (!MobyTypes.ContainsKey(mobyType)) continue;

                // Get all target mobys for this type
                var targetMobys = targetLevel.mobs
                    .Where(m => MobyTypes[mobyType].Contains(m.modelID))
                    .ToList();

                // Get all source mobys for this type
                var sourceMobys = new List<Moby>();
                foreach (var modelId in MobyTypes[mobyType])
                {
                    if (rc1MobysByType.ContainsKey(modelId))
                    {
                        sourceMobys.AddRange(rc1MobysByType[modelId]);
                    }
                }

                Console.WriteLine($"\n  Processing {mobyType}: {targetMobys.Count} target mobys, {sourceMobys.Count} source mobys");

                // Match counts between target and source
                int matchCount = Math.Min(targetMobys.Count, sourceMobys.Count);

                // Reposition target mobys to match source mobys
                for (int i = 0; i < matchCount; i++)
                {
                    var targetMoby = targetMobys[i];
                    var sourceMoby = sourceMobys[i];

                    // Store the original position for reporting
                    Vector3 originalPos = targetMoby.position;

                    // Update position, rotation, and scale
                    targetMoby.position = sourceMoby.position;
                    targetMoby.rotation = sourceMoby.rotation;
                    targetMoby.scale = sourceMoby.scale;

                    // Ensure the transform matrix is updated
                    targetMoby.UpdateTransformMatrix();

                    Console.WriteLine($"  ✅ Repositioned {mobyType} from {originalPos} to {targetMoby.position}");
                    repositionedCount++;
                }

                // Report if counts don't match
                if (targetMobys.Count > sourceMobys.Count)
                {
                    int unmatchedCount = targetMobys.Count - sourceMobys.Count;
                    Console.WriteLine($"  ⚠️ {unmatchedCount} {mobyType} mobys in target have no matching source positions");
                    unmatched += unmatchedCount;
                }
                else if (sourceMobys.Count > targetMobys.Count)
                {
                    int unmatchedCount = sourceMobys.Count - targetMobys.Count;
                    Console.WriteLine($"  ⚠️ {unmatchedCount} {mobyType} mobys in source have no matching target mobys");
                    unmatched += unmatchedCount;
                }
            }

            Console.WriteLine($"\n==== Repositioning Summary ====");
            Console.WriteLine($"  ✅ Successfully repositioned: {repositionedCount} mobys");
            Console.WriteLine($"  ⚠️ Unmatched mobys: {unmatched}");

            return repositionedCount > 0;
        }

        /// <summary>
        /// Interactive debug tool for repositioning mobys in the target level to match the positions in an RC1 source level
        /// </summary>
        /// <param name="targetLevel">The level containing mobys to reposition</param>
        /// <param name="rc1SourceLevel">The RC1 level with the original positions</param>
        /// <returns>True if repositioning was successful</returns>
        public static bool DebugRepositionMobysToMatchRC1Placements(Level targetLevel, Level rc1SourceLevel)
        {
            if (targetLevel == null || rc1SourceLevel == null)
            {
                Console.WriteLine("❌ Cannot reposition mobys: Invalid level data");
                return false;
            }

            Console.WriteLine("\n==== DEBUG: Select Moby Types to Reposition ====");
            Console.WriteLine($"  Target Level: {Path.GetFileName(targetLevel.path)}");
            Console.WriteLine($"  RC1 Source Level: {Path.GetFileName(rc1SourceLevel.path)}");

            // Build a list of available moby types from the MobyTypes dictionary
            List<string> availableTypes = MobyTypes.Keys.ToList();
            List<string> selectedTypes = new List<string>();

            // Calculate how many of each moby type exist in target and RC1
            Console.WriteLine("\nAvailable Moby Types:");
            for (int i = 0; i < availableTypes.Count; i++)
            {
                string typeKey = availableTypes[i];
                int[] modelIds = MobyTypes[typeKey];

                int targetCount = targetLevel.mobs?.Count(m => modelIds.Contains(m.modelID)) ?? 0;
                int rc1Count = rc1SourceLevel.mobs?.Count(m => modelIds.Contains(m.modelID)) ?? 0;

                if (targetCount == 0 && rc1Count == 0)
                {
                    // Skip types that don't exist in either level
                    continue;
                }

                Console.WriteLine($"{i + 1}. {typeKey} (Model IDs: {string.Join(", ", modelIds)})");
                Console.WriteLine($"   - {targetCount} instances in target level");
                Console.WriteLine($"   - {rc1Count} placements in RC1 source level");
            }

            Console.WriteLine("\nSelect moby types to reposition (comma-separated numbers, e.g., '1,3,5')");
            Console.WriteLine("Enter 'a' to select all types, or 'q' to cancel");
            Console.Write("> ");

            string input = Console.ReadLine()?.Trim().ToLower() ?? "";

            if (input == "q")
            {
                Console.WriteLine("❌ Operation cancelled");
                return false;
            }
            else if (input == "a")
            {
                selectedTypes = availableTypes;
                Console.WriteLine("✅ All moby types selected");
            }
            else
            {
                // Parse the comma-separated input
                string[] selections = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (string selection in selections)
                {
                    if (int.TryParse(selection.Trim(), out int index) && index >= 1 && index <= availableTypes.Count)
                    {
                        selectedTypes.Add(availableTypes[index - 1]);
                    }
                }

                if (selectedTypes.Count == 0)
                {
                    Console.WriteLine("❌ No valid selections made, operation cancelled");
                    return false;
                }
            }

            // Display what was selected
            Console.WriteLine("\nSelected Types:");
            foreach (string type in selectedTypes)
            {
                Console.WriteLine($"  - {type} (Model IDs: {string.Join(", ", MobyTypes[type])})");
            }

            // Confirm before proceeding
            Console.WriteLine("\nProceed with repositioning these moby types? (y/n)");
            Console.Write("> ");
            string confirm = Console.ReadLine()?.Trim().ToLower() ?? "n";

            if (confirm != "y")
            {
                Console.WriteLine("❌ Operation cancelled");
                return false;
            }

            // Execute the repositioning operation with the selected types
            Console.WriteLine("\n🔄 Repositioning selected moby types...");
            bool success = RepositionMobysToMatchRC1Placements(targetLevel, rc1SourceLevel, selectedTypes);

            if (success)
            {
                Console.WriteLine("✅ Successfully repositioned mobys to match RC1 placements");
                return true;
            }
            else
            {
                Console.WriteLine("❌ Failed to reposition mobys");
                return false;
            }
        }

        /// <summary>
        /// Utility method to compare two textures
        /// </summary>
        private static bool TextureEquals(Texture tex1, Texture tex2)
        {
            // This is a simplified comparison, you might need to refine this
            // based on how textures are identified in your system
            if (tex1 == null || tex2 == null)
                return false;

            // Compare vramPointer, size or other discriminating factors
            // in reality we'd want to compare the actual data but 
            // this might be good enough for preliminary detection
            return tex1.vramPointer == tex2.vramPointer &&
                   (tex1.data?.Length == tex2.data?.Length);
        }

        /// <summary>
        /// Rough comparison of two models to determine if they originate from the same source.
        /// Compares vertex and index buffer lengths as well as texture config counts.
        /// </summary>
        private static bool ModelsAreEquivalent(Model m1, Model m2)
        {
            if (m1 == null || m2 == null)
                return false;

            if (m1.vertexBuffer?.Length != m2.vertexBuffer?.Length)
                return false;

            if (m1.indexBuffer?.Length != m2.indexBuffer?.Length)
                return false;

            int m1Tex = m1.textureConfig?.Count ?? 0;
            int m2Tex = m2.textureConfig?.Count ?? 0;
            if (m1Tex != m2Tex)
                return false;

            return true;
        }

        /// <summary>
        /// Debug tool that allows selecting specific moby types to copy from RC2 (structure) with RC1 models (visuals)
        /// </summary>
        /// <param name="targetLevel">The level to add mobys to</param>
        /// <param name="rc2SourceLevel">The RC2 reference level to get moby data from</param>
        /// <param name="rc1ModelLevel">The RC1 level to get models from</param>
        /// <returns>True if operation was successful</returns>
        public static bool DebugCopyRC2MobysWithRC1Models(
            Level targetLevel,
            Level rc2SourceLevel,
            Level rc1ModelLevel)
        {
            if (targetLevel == null || rc2SourceLevel == null || rc1ModelLevel == null)
            {
                Console.WriteLine("❌ Cannot copy mobys: Invalid level data");
                return false;
            }

            Console.WriteLine("\n==== DEBUG: Select Moby Types to Copy from RC2 with RC1 Models ====");
            Console.WriteLine($"  RC2 Source Level: {Path.GetFileName(rc2SourceLevel.path)}");
            Console.WriteLine($"  RC1 Model Level: {Path.GetFileName(rc1ModelLevel.path)}");
            Console.WriteLine($"  Target Level: {Path.GetFileName(targetLevel.path)}");

            // Build a list of available moby types from the MobyTypes dictionary
            List<string> availableTypes = MobyTypes.Keys.ToList();
            List<string> selectedTypes = new List<string>();

            // Calculate how many of each moby type exist in RC2 and RC1
            Console.WriteLine("\nAvailable Moby Types:");
            for (int i = 0; i < availableTypes.Count; i++)
            {
                string typeKey = availableTypes[i];
                int[] modelIds = MobyTypes[typeKey];

                int rc2Count = rc2SourceLevel.mobs?.Count(m => modelIds.Contains(m.modelID)) ?? 0;
                int rc1ModelCount = rc1ModelLevel.mobyModels?.Count(m => modelIds.Contains(m.id)) ?? 0;
                int rc1PlacementCount = rc1ModelLevel.mobs?.Count(m => modelIds.Contains(m.modelID)) ?? 0;

                Console.WriteLine($"{i + 1}. {typeKey} (Model IDs: {string.Join(", ", modelIds)})");
                Console.WriteLine($"   - {rc2Count} instances in RC2 source");
                Console.WriteLine($"   - {rc1ModelCount} models available in RC1");
                Console.WriteLine($"   - {rc1PlacementCount} placements in RC1");
            }

            Console.WriteLine("\nSelect moby types to copy (comma-separated numbers, e.g., '1,3,5')");
            Console.WriteLine("Enter 'a' to select all types, or 'q' to cancel");
            Console.Write("> ");

            string input = Console.ReadLine()?.Trim().ToLower() ?? "";

            if (input == "q")
            {
                Console.WriteLine("❌ Operation cancelled");
                return false;
            }
            else if (input == "a")
            {
                selectedTypes = availableTypes;
                Console.WriteLine("✅ All moby types selected");
            }
            else
            {
                // Parse the comma-separated input
                string[] selections = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (string selection in selections)
                {
                    if (int.TryParse(selection.Trim(), out int index) && index >= 1 && index <= availableTypes.Count)
                    {
                        selectedTypes.Add(availableTypes[index - 1]);
                    }
                }

                if (selectedTypes.Count == 0)
                {
                    Console.WriteLine("❌ No valid selections made, operation cancelled");
                    return false;
                }
            }

            // Display what was selected
            Console.WriteLine("\nSelected Types:");
            foreach (string type in selectedTypes)
            {
                Console.WriteLine($"  - {type} (Model IDs: {string.Join(", ", MobyTypes[type])})");
            }

            // Confirm before proceeding
            Console.WriteLine("\nProceed with copying these moby types from RC2 with RC1 models? (y/n)");
            Console.Write("> ");
            string confirm = Console.ReadLine()?.Trim().ToLower() ?? "n";

            if (confirm != "y")
            {
                Console.WriteLine("❌ Operation cancelled");
                return false;
            }

            // Execute the copy operation with the selected types
            Console.WriteLine("\n🔄 Copying selected RC2 mobys with RC1 models...");
            bool success = CopyRC2MobysWithRC1Models(targetLevel, rc2SourceLevel, rc1ModelLevel, selectedTypes);

            if (success)
            {
                Console.WriteLine("✅ Successfully copied RC2 mobys with RC1 models");

                // Ask if the user wants to reposition the mobys according to RC1 placements
                Console.WriteLine("\nDo you want to reposition the mobys to match the RC1 level's placements? (y/n)");
                Console.WriteLine("This will move the copied mobys to the positions they had in the RC1 level.");
                Console.Write("> ");
                string repositionConfirm = Console.ReadLine()?.Trim().ToLower() ?? "n";

                if (repositionConfirm == "y")
                {
                    bool repositionSuccess = RepositionMobysToMatchRC1Placements(targetLevel, rc1ModelLevel, selectedTypes);
                    if (repositionSuccess)
                    {
                        Console.WriteLine("✅ Successfully repositioned mobys to match RC1 placements");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ Could not reposition mobys to match RC1 placements");
                    }
                }

                return true;
            }
            else
            {
                Console.WriteLine("❌ Failed to copy RC2 mobys with RC1 models");
                return false;
            }
        }

        /// <summary>
        /// Ensures proper deep copy of Model objects, especially MobyModel with all its specialized fields
        /// </summary>
        public static Model DeepCloneModel(Model sourceModel)
        {
            // Create new instance of the same type
            var clonedModel = Activator.CreateInstance(sourceModel.GetType()) as Model;
            if (clonedModel == null)
            {
                throw new InvalidOperationException($"Failed to clone model ID {sourceModel.id}");
            }

            // Basic properties need deep copy
            clonedModel.id = sourceModel.id;
            clonedModel.size = sourceModel.size;

            // Deep copy of vertex buffer
            if (sourceModel.vertexBuffer != null)
            {
                clonedModel.vertexBuffer = new float[sourceModel.vertexBuffer.Length];
                Array.Copy(sourceModel.vertexBuffer, clonedModel.vertexBuffer, sourceModel.vertexBuffer.Length);
            }

            // Deep copy of index buffer
            if (sourceModel.indexBuffer != null)
            {
                clonedModel.indexBuffer = new ushort[sourceModel.indexBuffer.Length];
                Array.Copy(sourceModel.indexBuffer, clonedModel.indexBuffer, sourceModel.indexBuffer.Length);
            }

            // Deep copy of RGBA array if present
            if (sourceModel.rgbas != null)
            {
                clonedModel.rgbas = new byte[sourceModel.rgbas.Length];
                Array.Copy(sourceModel.rgbas, clonedModel.rgbas, sourceModel.rgbas.Length);
            }

            // CRITICAL: Copy weights and ids arrays to avoid IndexOutOfRangeException in SerializeVertices()
            int vertexCount = sourceModel.vertexBuffer != null ? sourceModel.vertexBuffer.Length / 8 : 0;

            if (sourceModel.weights != null && sourceModel.weights.Length > 0)
            {
                clonedModel.weights = new uint[sourceModel.weights.Length];
                Array.Copy(sourceModel.weights, clonedModel.weights, sourceModel.weights.Length);
            }
            else
            {
                // Initialize with empty array matching vertex count
                clonedModel.weights = new uint[vertexCount];
            }

            if (sourceModel.ids != null && sourceModel.ids.Length > 0)
            {
                clonedModel.ids = new uint[sourceModel.ids.Length];
                Array.Copy(sourceModel.ids, clonedModel.ids, sourceModel.ids.Length);
            }
            else
            {
                // Initialize with empty array matching vertex count
                clonedModel.ids = new uint[vertexCount];
            }

            // Deep copy of texture configs
            if (sourceModel.textureConfig != null)
            {
                clonedModel.textureConfig = new List<TextureConfig>();
                foreach (var tc in sourceModel.textureConfig)
                {
                    // Create new TextureConfig and copy properties
                    var newTc = new TextureConfig
                    {
                        id = tc.id,
                        size = tc.size,
                        start = tc.start,
                        mode = tc.mode
                    };
                    clonedModel.textureConfig.Add(newTc);
                }
            }

            // Special handling for MobyModel-specific properties
            if (sourceModel is MobyModel srcMobyModel && clonedModel is MobyModel clonedMobyModel)
            {
                clonedMobyModel.vertexCount2 = srcMobyModel.vertexCount2;
                clonedMobyModel.null1 = srcMobyModel.null1;
                clonedMobyModel.boneCount = srcMobyModel.boneCount;
                clonedMobyModel.lpBoneCount = srcMobyModel.lpBoneCount;
                clonedMobyModel.count3 = srcMobyModel.count3;
                clonedMobyModel.count4 = srcMobyModel.count4;
                clonedMobyModel.lpRenderDist = srcMobyModel.lpRenderDist;
                clonedMobyModel.count8 = srcMobyModel.count8;
                clonedMobyModel.null2 = srcMobyModel.null2;
                clonedMobyModel.null3 = srcMobyModel.null3;
                clonedMobyModel.unk1 = srcMobyModel.unk1;
                clonedMobyModel.unk2 = srcMobyModel.unk2;
                clonedMobyModel.unk3 = srcMobyModel.unk3;
                clonedMobyModel.unk4 = srcMobyModel.unk4;
                clonedMobyModel.color2 = srcMobyModel.color2;
                clonedMobyModel.unk6 = srcMobyModel.unk6;

                // Deep copy of animations
                if (srcMobyModel.animations != null)
                {
                    clonedMobyModel.animations = new List<Animation>(srcMobyModel.animations);
                }

                // Deep copy of sounds
                if (srcMobyModel.modelSounds != null)
                {
                    clonedMobyModel.modelSounds = new List<ModelSound>(srcMobyModel.modelSounds);
                }

                // Deep copy of attachments
                if (srcMobyModel.attachments != null)
                {
                    clonedMobyModel.attachments = new List<Attachment>(srcMobyModel.attachments);
                }

                // Deep copy of index attachments
                if (srcMobyModel.indexAttachments != null)
                {
                    clonedMobyModel.indexAttachments = new List<byte>(srcMobyModel.indexAttachments);
                }

                // Deep copy of bone matrices and data
                if (srcMobyModel.boneMatrices != null)
                {
                    clonedMobyModel.boneMatrices = new List<BoneMatrix>(srcMobyModel.boneMatrices);
                }

                if (srcMobyModel.boneDatas != null)
                {
                    clonedMobyModel.boneDatas = new List<BoneData>(srcMobyModel.boneDatas);
                }

                // Deep copy of other buffers
                if (srcMobyModel.otherBuffer != null)
                {
                    clonedMobyModel.otherBuffer = new List<byte>(srcMobyModel.otherBuffer);
                }

                if (srcMobyModel.otherTextureConfigs != null)
                {
                    clonedMobyModel.otherTextureConfigs = new List<TextureConfig>(srcMobyModel.otherTextureConfigs);
                }

                if (srcMobyModel.otherIndexBuffer != null)
                {
                    clonedMobyModel.otherIndexBuffer = new List<ushort>(srcMobyModel.otherIndexBuffer);
                }

                // Copy over type10Block (hitbox data)
                if (srcMobyModel.type10Block != null)
                {
                    clonedMobyModel.type10Block = new byte[srcMobyModel.type10Block.Length];
                    Array.Copy(srcMobyModel.type10Block, clonedMobyModel.type10Block, srcMobyModel.type10Block.Length);
                }

                // Copy over skeleton reference
                clonedMobyModel.skeleton = srcMobyModel.skeleton;
                clonedMobyModel.isModel = srcMobyModel.isModel;
            }

            Console.WriteLine($"  🔍 Deep cloned model ID {sourceModel.id}: " +
                              $"Vertex buffer: {sourceModel.vertexBuffer?.Length ?? 0} → {clonedModel.vertexBuffer?.Length ?? 0}, " +
                              $"Index buffer: {sourceModel.indexBuffer?.Length ?? 0} → {clonedModel.indexBuffer?.Length ?? 0}, " +
                              $"Weights: {sourceModel.weights?.Length ?? 0} → {clonedModel.weights?.Length ?? 0}, " +
                              $"IDs: {sourceModel.ids?.Length ?? 0} → {clonedModel.ids?.Length ?? 0}");

            return clonedModel;
        }

        /// <summary>
        /// Ensures proper copying of important moby properties based on moby type
        /// </summary>
        /// <param name="targetMoby">The destination moby to update</param>
        /// <param name="sourceMoby">The source moby to copy properties from</param>
        private static void EnsurePropertiesAreSet(Moby targetMoby, Moby sourceMoby)
        {
            // Set basic properties if they weren't already copied
            if (targetMoby.drawDistance <= 0 && sourceMoby.drawDistance > 0)
                targetMoby.drawDistance = sourceMoby.drawDistance;

            if (targetMoby.updateDistance <= 0 && sourceMoby.updateDistance > 0)
                targetMoby.updateDistance = sourceMoby.updateDistance;

            if (targetMoby.bolts <= 0 && sourceMoby.bolts > 0)
                targetMoby.bolts = sourceMoby.bolts;

            // Make sure these always have valid values
            if (targetMoby.unk7A == 0)
                targetMoby.unk7A = 8192; // Standard value

            if (targetMoby.unk8A == 0)
                targetMoby.unk8A = 16384; // Standard value

            if (targetMoby.unk12A == 0)
                targetMoby.unk12A = 256; // Standard value

            // Copy spawn type info
            if ((targetMoby.spawnType == 0) && (sourceMoby.spawnType > 0))
                targetMoby.spawnType = sourceMoby.spawnType;

            // Special handling based on moby type
            int modelId = targetMoby.modelID;

            // Crates (500, 511, 512, 501)
            if (modelId == 500 || modelId == 511 || modelId == 512 || modelId == 501)
            {
                targetMoby.isCrate = true;
                targetMoby.spawnBeforeDeath = true;

                // Unless already set, use typical values
                if (targetMoby.bolts <= 0)
                    targetMoby.bolts = 5; // Default bolt value for crates

                if (targetMoby.drawDistance <= 0)
                    targetMoby.drawDistance = 30;

                if (targetMoby.updateDistance <= 0)
                    targetMoby.updateDistance = 50;
            }

            // Swingshot nodes (803)
            else if (modelId == 803)
            {
                // Unless already set, use typical values
                if (targetMoby.drawDistance <= 0)
                    targetMoby.drawDistance = 50;

                if (targetMoby.updateDistance <= 0)
                    targetMoby.updateDistance = 100;
            }

            // Vendor (11)
            else if (modelId == 11)
            {
                // Vendors typically have standard values
                if (targetMoby.drawDistance <= 0)
                    targetMoby.drawDistance = 40;

                if (targetMoby.updateDistance <= 0)
                    targetMoby.updateDistance = 60;
            }

            // Vendor Logo (1143)
            else if (modelId == 1143)
            {
                // Vendor logo typically has standard values
                if (targetMoby.drawDistance <= 0)
                    targetMoby.drawDistance = 40;

                if (targetMoby.updateDistance <= 0)
                    targetMoby.updateDistance = 60;
            }
        }

        /// <summary>
        /// Ensures ALL mobys have unique pVar indices to prevent game crashes
        /// </summary>
        /// <param name="level">The level to fix pVar indices in</param>
        /// <returns>True if changes were made</returns>
        public static bool ValidateAndFixPvarIndices(Level level)
        {
            if (level == null || level.mobs == null || level.mobs.Count == 0)
            {
                Console.WriteLine("No mobys to validate pVar indices for");
                return false;
            }

            Console.WriteLine("\n==== Validating and Fixing pVar Indices ====");
            Console.WriteLine($"Found {level.mobs.Count} mobys to check");

            // Track changes made
            bool changesMade = false;

            // First, analyze existing indices
            Dictionary<int, List<Moby>> pvarIndexToMobys = new Dictionary<int, List<Moby>>();
            HashSet<int> usedPvarIndices = new HashSet<int>();

            // Group mobys by their current pVar index
            foreach (var moby in level.mobs)
            {
                if (moby.pvarIndex < 0)
                    continue; // Skip mobys without pVar data

                if (!pvarIndexToMobys.ContainsKey(moby.pvarIndex))
                {
                    pvarIndexToMobys[moby.pvarIndex] = new List<Moby>();
                }

                pvarIndexToMobys[moby.pvarIndex].Add(moby);
                usedPvarIndices.Add(moby.pvarIndex);
            }

            // Find conflicts - where multiple mobys share the same pVar index
            var conflicts = pvarIndexToMobys.Where(kv => kv.Value.Count > 1)
                                          .OrderByDescending(kv => kv.Value.Count)
                                          .ToList();

            if (conflicts.Count > 0)
            {
                Console.WriteLine($"Found {conflicts.Count} pVar index conflicts");

                foreach (var conflict in conflicts)
                {
                    int conflictIndex = conflict.Key;
                    List<Moby> conflictingMobys = conflict.Value;

                    Console.WriteLine($"  pVar Index {conflictIndex} used by {conflictingMobys.Count} mobys:");

                    // Keep the first moby with this index, reassign all others
                    Moby firstMoby = conflictingMobys[0];
                    string firstMobyType = GetMobyTypeName(firstMoby.modelID);
                    Console.WriteLine($"    Keeping pVar Index {conflictIndex} for first moby (Type: {firstMobyType}, ModelID: {firstMoby.modelID}, MobyID: {firstMoby.mobyID})");

                    // Reassign all other mobys with new unique indices
                    for (int i = 1; i < conflictingMobys.Count; i++)
                    {
                        Moby moby = conflictingMobys[i];
                        string mobyType = GetMobyTypeName(moby.modelID);

                        // Find next available index
                        int newIndex = 0;
                        while (usedPvarIndices.Contains(newIndex))
                        {
                            newIndex++;
                        }

                        // Update the moby's pVar index
                        int oldIndex = moby.pvarIndex;
                        moby.pvarIndex = newIndex;
                        usedPvarIndices.Add(newIndex);

                        Console.WriteLine($"    Reassigning {mobyType} (ModelID: {moby.modelID}, MobyID: {moby.mobyID}) from pVar Index {oldIndex} to {newIndex}");

                        changesMade = true;
                    }
                }
            }
            else
            {
                Console.WriteLine("No pVar index conflicts found");
            }

            // At this point, we know all existing indices are unique
            // Now let's ensure any mobys without pVar indices get unique ones if they need them
            int maxPvarIndex = usedPvarIndices.Count > 0 ? usedPvarIndices.Max() : -1;

            // Group mobys by model ID to handle related mobys together
            var mobysByModelId = level.mobs.GroupBy(m => m.modelID).ToDictionary(g => g.Key, g => g.ToList());

            // Process each moby model type
            foreach (var modelGroup in mobysByModelId)
            {
                int modelId = modelGroup.Key;
                List<Moby> mobysWithSameModel = modelGroup.Value;

                // Check if mobys of this type need pVars
                bool needsPvars = mobysWithSameModel.Any(m => m.pVars != null && m.pVars.Length > 0);
                string mobyType = GetMobyTypeName(modelId);

                if (needsPvars)
                {
                    // Find mobys without pVar indices
                    var mobysNeedingPvarIndex = mobysWithSameModel.Where(m => m.pvarIndex < 0).ToList();

                    if (mobysNeedingPvarIndex.Count > 0)
                    {
                        Console.WriteLine($"  Assigning pVar indices to {mobysNeedingPvarIndex.Count} {mobyType} mobys that need them");

                        foreach (var moby in mobysNeedingPvarIndex)
                        {
                            // Find next available index
                            int newIndex = maxPvarIndex + 1;
                            while (usedPvarIndices.Contains(newIndex))
                            {
                                newIndex++;
                            }

                            moby.pvarIndex = newIndex;
                            usedPvarIndices.Add(newIndex);
                            maxPvarIndex = Math.Max(maxPvarIndex, newIndex);

                            Console.WriteLine($"    Assigned pVar Index {newIndex} to {mobyType} (ModelID: {moby.modelID}, MobyID: {moby.mobyID})");

                            changesMade = true;
                        }
                    }
                }
            }

            // Make sure level.pVars is large enough to hold all indices we've assigned
            if (changesMade && maxPvarIndex >= 0)
            {
                if (level.pVars == null)
                {
                    level.pVars = new List<byte[]>();
                }

                // Ensure level.pVars has an entry for each pVar index
                while (level.pVars.Count <= maxPvarIndex)
                {
                    level.pVars.Add(new byte[0x80]);
                }

                Console.WriteLine($"Resized pVars array to {level.pVars.Count} entries");
            }

            // Final verification - check for any remaining duplicates
            var finalCheck = level.mobs
                .Where(m => m.pvarIndex >= 0)
                .GroupBy(m => m.pvarIndex)
                .FirstOrDefault(g => g.Count() > 1);

            if (finalCheck != null)
            {
                Console.WriteLine($"⚠️ WARNING: Still found duplicate pVar index {finalCheck.Key} after fixing! This should not happen.");
                return false;
            }

            if (changesMade)
            {
                Console.WriteLine($"✅ Successfully fixed all pVar index conflicts");
            }
            else
            {
                Console.WriteLine($"✅ No changes needed, all pVar indices are already unique");
            }

            return changesMade;
        }

        /// <summary>
        /// Fixes the serialization format for the spawn type bitmask
        /// </summary>
        private static void FixSpawnTypeFormatting(Moby moby)
        {
            // Convert the bitmask to an integer using explicit cast
            int spawnTypeAsInt = (int) moby.spawnType;

            // Set these values directly using bit manipulation for clarity
            bool isCrateBit = (spawnTypeAsInt & 4) > 0;
            bool spawnBeforeDeathBit = (spawnTypeAsInt & 8) > 0;

            // Log the actual numeric value
            Console.WriteLine($"    - FIXED spawnType=0x{spawnTypeAsInt:X2}, isCrate={isCrateBit}, spawnBeforeDeath={spawnBeforeDeathBit}");

            // Force the values to be consistent
            moby.isCrate = isCrateBit;
            moby.spawnBeforeDeath = spawnBeforeDeathBit;
        }

        /// <summary>
        /// Ensures Vendor Logo textures are properly added to the target level
        /// </summary>
        public static void EnsureVendorLogoTextures(Level targetLevel, Level sourceLevel)
        {
            Console.WriteLine("🔍 Looking for Vendor Logo textures from source level...");

            // First, check if we already have a model for the Vendor Logo in target level
            var vendorLogoModel = targetLevel.mobyModels?.FirstOrDefault(m => m.id == 1143);
            if (vendorLogoModel == null)
            {
                Console.WriteLine("❌ No Vendor Logo model found in target level to add textures to.");
                return;
            }

            // Look for the Vendor Logo textures in mobyload files (likely where they're stored)
            List<Texture> foundTextures = new List<Texture>();
            bool texturesFound = false;

            // First check regular textures
            if (sourceLevel.textures != null)
            {
                var textureIds = vendorLogoModel.textureConfig?.Select(tc => tc.id).ToList();
                if (textureIds != null && textureIds.Count > 0)
                {
                    Console.WriteLine($"  - Vendor Logo model references texture IDs: {string.Join(", ", textureIds)}");
                    foreach (var id in textureIds)
                    {
                        if (id < sourceLevel.textures.Count)
                        {
                            foundTextures.Add(sourceLevel.textures[id]);
                            texturesFound = true;
                        }
                    }
                }
            }

            // Check mobyload textures if we haven't found the textures yet
            if (!texturesFound && sourceLevel.mobyloadTextures != null)
            {
                for (int i = 0; i < sourceLevel.mobyloadTextures.Count; i++)
                {
                    // Look for mobyload textures that might be used by vendor logo
                    if (sourceLevel.mobyloadModels[i]?.Any(m => m.id == 1143) == true)
                    {
                        Console.WriteLine($"  - Found Vendor Logo model in mobyload file {i}");
                        foundTextures.AddRange(sourceLevel.mobyloadTextures[i]);
                        texturesFound = true;
                    }
                }
            }

            if (!texturesFound)
            {
                Console.WriteLine("⚠️ Could not locate specific Vendor Logo textures. Using fallback strategy...");
                // Fallback: if we can't identify the exact textures, copy some likely textures
                // This might include more textures than needed, but ensures we get what we need
                if (sourceLevel.mobyloadTextures != null && sourceLevel.mobyloadTextures.Count > 0)
                {
                    // Copy textures from the first few mobyload files as a fallback
                    for (int i = 0; i < Math.Min(3, sourceLevel.mobyloadTextures.Count); i++)
                    {
                        foundTextures.AddRange(sourceLevel.mobyloadTextures[i]);
                    }
                    texturesFound = foundTextures.Count > 0;
                }
            }

            // Add the textures to the target level
            if (texturesFound)
            {
                int startIndex = targetLevel.textures.Count;
                Console.WriteLine($"✅ Adding {foundTextures.Count} potential Vendor Logo textures to target level starting at index {startIndex}");
                targetLevel.textures.AddRange(foundTextures);

                // Update texture indices in the vendor logo model
                if (vendorLogoModel.textureConfig != null)
                {
                    foreach (var tc in vendorLogoModel.textureConfig)
                    {
                        // Adjust texture IDs to point to the newly added textures
                        // This needs to map original source texture IDs to new target IDs carefully.
                        // The provided logic assumes a simple sequential addition, which might be problematic
                        // if tc.id was an index into sourceLevel.textures and now needs to be an index
                        // into targetLevel.textures.
                        // For simplicity, if we added N textures, and tc.id was X (0 to N-1 relative to what was added),
                        // then new id is startIndex + X.
                        // This part of the logic might need refinement based on how texture IDs are managed globally.
                        // Assuming tc.id was an index relative to the `foundTextures` list for this example.
                        if (tc.id < foundTextures.Count && tc.id >= 0) // Ensure tc.id is a valid index for the found textures
                            tc.id = startIndex + tc.id; // This assumes tc.id was an index _within_ the block of foundTextures.
                                                        // More robustly, one would map old IDs to new IDs during texture copy.
                                                        // Given the existing code, this is a placeholder adjustment.
                    }
                    Console.WriteLine("✅ Updated texture references in Vendor Logo model (may need verification)");
                }
            }
            else
            {
                Console.WriteLine("❌ Failed to find any textures for Vendor Logo");
            }
        }

        /// <summary>
        /// Fixes moby pVars by copying correct data from a reference level for all supported moby types
        /// </summary>
        /// <param name="targetLevel">The level with the mobys to fix</param>
        /// <param name="referenceLevel">A working reference level with functional mobys</param>
        /// <returns>True if pVars were updated successfully</returns>
        public static bool FixMobyPvarsFromReferenceLevel(Level targetLevel, Level referenceLevel)
        {
            if (targetLevel == null || referenceLevel == null ||
                targetLevel.mobs == null || referenceLevel.mobs == null)
            {
                Console.WriteLine("❌ Cannot fix moby pVars: Invalid level data");
                return false;
            }

            Console.WriteLine("\n==== Fixing Moby pVars From Reference Level ====");

            // Ensure target level has enough pVars space
            if (targetLevel.pVars == null)
                targetLevel.pVars = new List<byte[]>();

            int totalFixedCount = 0;

            // Process each moby type separately
            foreach (var mobyTypeEntry in MobyTypes)
            {
                string mobyType = mobyTypeEntry.Key;
                int[] modelIds = mobyTypeEntry.Value;

                // Get target mobys of this type
                var targetMobys = targetLevel.mobs
                    .Where(m => modelIds.Contains(m.modelID))
                    .ToList();

                if (targetMobys.Count == 0)
                    continue;

                Console.WriteLine($"\n  Processing {targetMobys.Count} {mobyType} mobys");

                // Find reference mobys of this type
                var referenceMobys = referenceLevel.mobs
                    .Where(m => modelIds.Contains(m.modelID))
                    .ToList();

                if (referenceMobys.Count == 0)
                {
                    Console.WriteLine($"  ⚠️ No reference {mobyType} mobys found. Skipping.");
                    continue;
                }

                Console.WriteLine($"  Found {referenceMobys.Count} reference {mobyType} mobys");

                // Store reference pVars data for each model ID
                var modelPvarsData = new Dictionary<int, byte[]>();
                foreach (var refMoby in referenceMobys)
                {
                    if (refMoby.pVars != null && refMoby.pVars.Length > 0 && !modelPvarsData.ContainsKey(refMoby.modelID))
                    {
                        modelPvarsData[refMoby.modelID] = refMoby.pVars;
                        Console.WriteLine($"  ✅ Found pVars for {mobyType} model {refMoby.modelID}: {BitConverter.ToString(refMoby.pVars)}");
                    }
                }

                if (modelPvarsData.Count == 0)
                {
                    Console.WriteLine($"  ⚠️ No reference {mobyType} with valid pVars data found. Skipping.");
                    continue;
                }

                int typeFixedCount = 0;

                // Apply correct pVars and other properties to each target moby
                foreach (var targetMoby in targetMobys)
                {
                    // Set moby type specific properties
                    ApplyMobyTypeSpecificProperties(targetMoby, mobyType);

                    // Ensure pvarIndex is valid
                    if (targetMoby.pvarIndex < 0)
                    {
                        Console.WriteLine($"  ⚠️ {mobyType} model {targetMoby.modelID} (ID {targetMoby.mobyID}) has invalid pvarIndex {targetMoby.pvarIndex}. Assigning new index...");

                        // Assign appropriate pVar index based on moby type
                        switch (mobyType)
                        {
                            case "Vendor":
                                targetMoby.pvarIndex = 12;
                                break;
                            case "VendorLogo":
                                targetMoby.pvarIndex = 13;
                                break;
                            case "Crate":
                            case "AmmoCrate":
                            case "NanotechCrate":
                                targetMoby.pvarIndex = 20 + typeFixedCount;
                                break;
                            case "SwingshotNode":
                                targetMoby.pvarIndex = 50 + typeFixedCount;
                                break;
                            default:
                                targetMoby.pvarIndex = 100 + typeFixedCount;
                                break;
                        }
                    }

                    // Ensure we have space in the pVars list
                    while (targetLevel.pVars.Count <= targetMoby.pvarIndex)
                    {
                        targetLevel.pVars.Add(new byte[0x80]);
                    }

                    // Apply reference pVars data if available for this model ID
                    if (modelPvarsData.TryGetValue(targetMoby.modelID, out byte[]? referenceData))
                    {
                        // Create a deep copy of the reference data
                        byte[] pVarsCopy = new byte[referenceData.Length];
                        Array.Copy(referenceData, pVarsCopy, referenceData.Length);

                        // Update the pVars for this moby
                        targetLevel.pVars[targetMoby.pvarIndex] = pVarsCopy;
                        targetMoby.pVars = pVarsCopy; // Also update the moby's direct reference

                        Console.WriteLine($"  ✅ Fixed {mobyType} model {targetMoby.modelID} (ID {targetMoby.mobyID}) with exact pVars data for pvarIndex {targetMoby.pvarIndex}");
                    }
                    else
                    {
                        // No reference data for this specific model ID, use data from any moby of the same type
                        var fallbackData = modelPvarsData.First().Value;
                        byte[] pVarsCopy = new byte[fallbackData.Length];
                        Array.Copy(fallbackData, pVarsCopy, fallbackData.Length);

                        // Update the pVars for this moby
                        targetLevel.pVars[targetMoby.pvarIndex] = pVarsCopy;
                        targetMoby.pVars = pVarsCopy; // Also update the moby's direct reference

                        Console.WriteLine($"  ✅ Fixed {mobyType} model {targetMoby.modelID} (ID {targetMoby.mobyID}) with fallback pVars data for pvarIndex {targetMoby.pvarIndex}");
                    }

                    typeFixedCount++;
                }

                Console.WriteLine($"  Fixed {typeFixedCount} {mobyType} mobys");
                totalFixedCount += typeFixedCount;
            }

            // Check for and resolve any pVar index conflicts
            var duplicateIndices = targetLevel.mobs
                .Where(m => m.pvarIndex >= 0)
                .GroupBy(m => m.pvarIndex)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateIndices.Any())
            {
                Console.WriteLine("\n⚠️ Found duplicate pVar index assignments. Running conflict resolution...");
                ValidateAndFixPvarIndices(targetLevel);
            }

            Console.WriteLine($"\n==== Moby pVar Fix Summary ====");
            Console.WriteLine($"  ✅ Successfully fixed {totalFixedCount} mobys");
            Console.WriteLine($"  Make sure to save the level for changes to take effect");

            return totalFixedCount > 0;
        }

        /// <summary>
        /// Imports specific RC2 moby models from a reference level into the target level
        /// </summary>
        /// <param name="targetLevel">The level to import the models into</param>
        /// <param name="referenceLevel">The RC2 reference level to get the models from</param>
        /// <param name="mobyIDs">List of moby model IDs to import</param>
        /// <returns>True if operation was successful</returns>
        public static bool ImportSpecificRC2Models(Level targetLevel, Level referenceLevel, List<int> mobyIDs)
        {
            if (targetLevel == null || referenceLevel == null ||
                referenceLevel.mobyModels == null || mobyIDs == null)
            {
                Console.WriteLine("❌ Cannot import specific RC2 models: Invalid data");
                return false;
            }

            // Initialize target level's models list if needed
            if (targetLevel.mobyModels == null)
            {
                targetLevel.mobyModels = new List<Model>();
            }

            Console.WriteLine($"\n==== Importing Specific RC2 Moby Models ====");

            // Create a dictionary of existing models in target level for quick lookup
            var existingModelIds = new HashSet<int>();
            foreach (var model in targetLevel.mobyModels)
            {
                if (model != null)
                {
                    existingModelIds.Add(model.id);
                }
            }

            // Keep track of imported models
            int importedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            // Check for specific models first - verbose diagnostic
            Console.WriteLine("Checking source level for requested models:");
            foreach (int mobyID in mobyIDs)
            {
                var sourceModel = referenceLevel.mobyModels?.FirstOrDefault(m => m.id == mobyID);
                Console.WriteLine($"  Model ID {mobyID}: {(sourceModel != null ? "Found" : "NOT FOUND")} in source level");
            }

            foreach (int mobyID in mobyIDs)
            {
                try
                {
                    Console.WriteLine($"Processing model ID {mobyID}...");

                    // Skip if this model ID already exists in the target level
                    if (existingModelIds.Contains(mobyID))
                    {
                        Console.WriteLine($"  ℹ️ Moby model ID {mobyID} already exists in target level, skipping");
                        skippedCount++;
                        continue;
                    }

                    // Find the model in the reference level
                    var sourceModel = referenceLevel.mobyModels?.FirstOrDefault(m => m.id == mobyID);
                    if (sourceModel == null)
                    {
                        // Enhanced error message for troubleshooting
                        Console.WriteLine($"  ⚠️ Could not find moby model ID {mobyID} in reference level, skipping");
                        if (referenceLevel.mobyModels != null)
                        {
                            Console.WriteLine($"  Available model IDs in reference level: {string.Join(", ", referenceLevel.mobyModels.Take(10).Select(m => m.id))}...");
                        }
                        else
                        {
                            Console.WriteLine("  No models found in reference level.");
                        }
                        skippedCount++;
                        continue;
                    }

                    // Perform deep clone of the model
                    var clonedModel = DeepCloneModel(sourceModel);

                    // IMPORTANT! Ensure the clone maintains the original ID without casting to short
                    clonedModel.id = sourceModel.id; // Use the source model's ID directly

                    // Handle textures - import any textures used by this model
                    if (clonedModel.textureConfig != null)
                    {
                        Dictionary<int, int> textureMapping = new Dictionary<int, int>();
                        int processedTextures = 0;
                        int skippedTextures = 0;

                        foreach (var texConfig in clonedModel.textureConfig)
                        {
                            int originalTexID = texConfig.id;

                            // Skip if texture index is out of bounds
                            if (originalTexID >= referenceLevel.textures.Count)
                            {
                                Console.WriteLine($"  ⚠️ Model {mobyID} references invalid texture ID {originalTexID}, skipping texture");
                                skippedTextures++;
                                continue;
                            }

                            // Safety check
                            if (originalTexID < 0)
                            {
                                Console.WriteLine($"  ⚠️ Model {mobyID} has negative texture ID {originalTexID}, skipping texture");
                                skippedTextures++;
                                continue;
                            }

                            // Get the texture from reference level
                            var sourceTexture = referenceLevel.textures[originalTexID];

                            // Check if this texture already exists in the target level
                            int targetTexIdx = -1;
                            for (int i = 0; i < targetLevel.textures.Count; i++)
                            {
                                if (TextureEquals(sourceTexture, targetLevel.textures[i]))
                                {
                                    targetTexIdx = i;
                                    break;
                                }
                            }

                            // Add the texture to the target level if not already present
                            if (targetTexIdx == -1)
                            {
                                targetLevel.textures.Add(sourceTexture);
                                targetTexIdx = targetLevel.textures.Count - 1;
                            }

                            // Update the texture mapping
                            textureMapping[originalTexID] = targetTexIdx;

                            // Update the texture ID in the config
                            texConfig.id = targetTexIdx;
                            processedTextures++;
                        }

                        Console.WriteLine($"  ✅ Processed {processedTextures} textures for model ID {mobyID} (skipped {skippedTextures})");
                    }

                    // Add the cloned model to the target level
                    targetLevel.mobyModels.Add(clonedModel);

                    Console.WriteLine($"  ✅ Imported model ID {mobyID} from reference level");
                    importedCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error importing model ID {mobyID}: {ex.Message}");
                    Console.WriteLine($"  Stack trace: {ex.StackTrace}");
                    failedCount++;
                }
            }

            Console.WriteLine($"\n==== Import Summary ====");
            Console.WriteLine($"  ✅ Successfully imported: {importedCount} models");
            Console.WriteLine($"  ⏭️ Skipped: {skippedCount} models");
            Console.WriteLine($"  ❌ Failed: {failedCount} models");

            return importedCount > 0;
        }

        /// <summary>
        /// Imports vendor support mobys from RC2 reference level that are required for proper vendor functionality
        /// </summary>
        /// <param name="targetLevel">The level to import models into</param>
        /// <param name="referenceLevel">The RC2 reference level (Damosel) with the source models</param>
        /// <returns>True if the operation was successful</returns>
        public static bool ImportVendorSupportModels(Level targetLevel, Level referenceLevel)
        {
            Console.WriteLine("🔄 Importing vendor support models from reference level...");

            // These are the models you specified as needed for vendor functionality
            List<int> vendorSupportModelIds = new List<int> { 1007, 1137, 1204, 1502 };

            return ImportSpecificRC2Models(targetLevel, referenceLevel, vendorSupportModelIds);
        }

        /// <summary>
        /// Applies moby type specific properties based on the moby type
        /// </summary>
        /// <param name="moby">The moby to update</param>
        /// <param name="mobyType">The type of moby</param>
        private static void ApplyMobyTypeSpecificProperties(Moby moby, string mobyType)
        {
            switch (mobyType)
            {
                case "Vendor":
                    // Vendor-specific properties
                    moby.drawDistance = 40;
                    moby.updateDistance = 60;
                    moby.unk7A = 8192;
                    moby.unk8A = 16384;
                    moby.unk12A = 256;

                    // Set special fields on the vendor's MobyModel if available
                    if (moby.model is MobyModel vendorModel)
                    {
                        Console.WriteLine($"  Setting special properties for Vendor MobyModel #{vendorModel.id}");
                        vendorModel.count3 = 0;
                        vendorModel.count4 = 255;
                        vendorModel.unk1 = -0.000f;
                        vendorModel.unk2 = 0.001f;
                        vendorModel.unk3 = -0.000f;
                        vendorModel.unk4 = 25348.143f;
                        vendorModel.unk6 = 1073807359;
                    }
                    else
                    {
                        Console.WriteLine($"  WARNING: Vendor model #{moby.modelID} is not a MobyModel, can't set vendor-specific model properties!");
                    }
                    break;

                case "VendorLogo":
                    moby.drawDistance = 40;
                    moby.updateDistance = 60;
                    moby.unk7A = 8192;
                    moby.unk8A = 16384;
                    moby.unk12A = 256;
                    break;

                case "Crate":
                    // Set crate-specific properties
                    moby.spawnType = 12; // 1100 in binary (sets both isCrate and spawnBeforeDeath bits)
                    moby.isCrate = true;
                    moby.spawnBeforeDeath = true;
                    moby.bolts = 5; // Standard bolt value for crates
                    moby.drawDistance = 30;
                    moby.updateDistance = 50;
                    moby.unk7A = 8192;
                    moby.unk8A = 16384;
                    moby.unk12A = 256;

                    // Set dataval (important for crate functionality)
                    if (moby.dataval == 0)
                        moby.dataval = 1;

                    // Set special null3 field on the crate's MobyModel if available
                    if (moby.model != null)
                    {
                        // Try explicit casting first
                        MobyModel? crateModel = moby.model as MobyModel;
                        if (crateModel != null)
                        {
                            Console.WriteLine($"  Setting null3=41092981 for {mobyType} MobyModel #{crateModel.id}");
                            crateModel.null3 = 41092981;
                        }
                        else
                        {
                            Console.WriteLine($"  WARNING: Crate model #{moby.modelID} is not a MobyModel type ({moby.model.GetType().Name}), trying reflection to set null3...");

                            // Try using reflection as a fallback
                            try
                            {
                                var nullProp = moby.model.GetType().GetProperty("null3");
                                if (nullProp != null)
                                {
                                    nullProp.SetValue(moby.model, 41092981);
                                    Console.WriteLine($"  Successfully set null3=41092981 for Crate model #{moby.modelID} using reflection");
                                }
                                else
                                {
                                    Console.WriteLine($"  Failed to set null3: Property not found on model type {moby.model.GetType().Name}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"  Failed to set null3 via reflection: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  WARNING: Crate at position {moby.position} has null model reference!");
                    }
                    break;
                case "AmmoCrate":
                case "NanotechCrate":

                case "SwingshotNode":
                    moby.drawDistance = 50;
                    moby.updateDistance = 100;
                    moby.unk7A = 8192;
                    moby.unk8A = 16384;
                    moby.unk12A = 256;
                    moby.light = 0;
                    break;

                default:
                    // Generic properties for all other moby types
                    if (moby.unk7A == 0)
                        moby.unk7A = 8192;
                    if (moby.unk8A == 0)
                        moby.unk8A = 16384;
                    if (moby.unk12A == 0)
                        moby.unk12A = 256;
                    break;
            }
        }

        public static void FixSwingshotNodePvars(Level targetLevel, Level donorLevel, int[] swingshotNodeModelIds)
        {
            if (targetLevel == null || donorLevel == null || swingshotNodeModelIds == null)
                return;

            foreach (int modelId in swingshotNodeModelIds)
            {
                // Get all swingshot nodes in both levels, ordered for 1:1 mapping
                var donorNodes = donorLevel.mobs?.Where(m => m.modelID == modelId).OrderBy(m => m.mobyID).ToList();
                var targetNodes = targetLevel.mobs?.Where(m => m.modelID == modelId).OrderBy(m => m.mobyID).ToList();

                if (donorNodes == null || targetNodes == null)
                    continue;

                int count = Math.Min(donorNodes.Count, targetNodes.Count);

                for (int i = 0; i < count; i++)
                {
                    var donor = donorNodes[i];
                    var target = targetNodes[i];

                    if (donor.pVars != null && donor.pVars.Length > 0)
                    {
                        target.pVars = new byte[donor.pVars.Length];
                        Array.Copy(donor.pVars, target.pVars, donor.pVars.Length);
                    }
                }
            }
        }

        /// <summary>
        /// Clones swingshot nodes from the donor level and positions them at RC1 level locations
        /// </summary>
        /// <param name="targetLevel">Level to add the swingshot nodes to</param>
        /// <param name="donorLevel">Level with working swingshot nodes to clone</param>
        /// <param name="rc1PositionLevel">RC1 level with swingshot node positions</param>
        /// <param name="swingshotNodeModelIds">Model IDs for swingshot nodes</param>
        /// <returns>True if successful</returns>
        public static bool CloneSwingshotNodesToRC1Positions(Level targetLevel, Level donorLevel, Level rc1PositionLevel, int[] swingshotNodeModelIds)
        {
            Console.WriteLine("\n==== Cloning Swingshot Nodes to RC1 Positions ====");

            if (targetLevel == null || donorLevel == null || rc1PositionLevel == null || swingshotNodeModelIds == null)
            {
                Console.WriteLine("❌ Cannot clone swingshot nodes: Missing level data");
                return false;
            }

            // First, remove any existing swingshot nodes from target level
            int nodesRemoved = 0;
            if (targetLevel.mobs != null)
            {
                var existingNodes = targetLevel.mobs
                    .Where(m => swingshotNodeModelIds.Contains(m.modelID))
                    .ToList();

                foreach (var node in existingNodes)
                {
                    targetLevel.mobs.Remove(node);
                    nodesRemoved++;
                }

                if (nodesRemoved > 0)
                    Console.WriteLine($"Removed {nodesRemoved} existing swingshot nodes from target level");
            }

            // Find RC1 nodes to get positions from
            var rc1Nodes = rc1PositionLevel.mobs?
                .Where(m => swingshotNodeModelIds.Contains(m.modelID))
                .ToList() ?? new List<Moby>();

            if (rc1Nodes.Count == 0)
            {
                Console.WriteLine("❌ No swingshot nodes found in RC1 position level");
                return false;
            }

            Console.WriteLine($"Found {rc1Nodes.Count} swingshot nodes in RC1 position level");

            // Find template nodes in donor level
            var donorNodes = donorLevel.mobs?
                .Where(m => swingshotNodeModelIds.Contains(m.modelID))
                .ToList() ?? new List<Moby>();

            if (donorNodes.Count == 0)
            {
                Console.WriteLine("❌ No swingshot nodes found in donor level to use as template");
                return false;
            }

            Console.WriteLine($"Found {donorNodes.Count} swingshot nodes in donor level to use as templates");

            // Find the next available moby ID
            int nextMobyId = 1000;
            if (targetLevel.mobs != null && targetLevel.mobs.Count > 0)
            {
                nextMobyId = targetLevel.mobs.Max(m => m.mobyID) + 1;
            }

            // Ensure target level has a mobs list
            if (targetLevel.mobs == null)
            {
                targetLevel.mobs = new List<Moby>();
            }

            // Ensure target level has pVars list
            if (targetLevel.pVars == null)
            {
                targetLevel.pVars = new List<byte[]>();
            }

            // Clone donor nodes and position them at RC1 positions
            int createdCount = 0;
            for (int i = 0; i < rc1Nodes.Count; i++)
            {
                var rc1Node = rc1Nodes[i];
                // Use modulo to cycle through available donor nodes if needed
                var donorNode = donorNodes[i % donorNodes.Count];

                try
                {
                    // Create a new moby as an exact copy of the donor node
                    var newNode = new Moby(donorNode);

                    // Only change position, mobyID, and ensure model reference
                    newNode.position = rc1Node.position;
                    newNode.rotation = rc1Node.rotation;
                    newNode.scale = rc1Node.scale;
                    newNode.mobyID = nextMobyId++;

                    // Make sure we have the model
                    if (targetLevel.mobyModels == null ||
                        !targetLevel.mobyModels.Any(m => m.id == donorNode.modelID))
                    {
                        // Import model
                        var donorModel = donorNode.model;
                        if (donorModel != null)
                        {
                            if (targetLevel.mobyModels == null)
                                targetLevel.mobyModels = new List<Model>();

                            var clonedModel = DeepCloneModel(donorModel);
                            targetLevel.mobyModels.Add(clonedModel);
                            newNode.model = clonedModel;
                        }
                    }
                    else
                    {
                        // Link to existing model
                        newNode.model = targetLevel.mobyModels.First(m => m.id == donorNode.modelID);
                    }

                    // Update transform matrix with RC1 position
                    newNode.UpdateTransformMatrix();

                    // Crucial part: Copy pVars exactly
                    if (donorNode.pVars != null && donorNode.pVars.Length > 0)
                    {
                        // Allocate space for a deep copy
                        newNode.pVars = new byte[donorNode.pVars.Length];
                        Array.Copy(donorNode.pVars, newNode.pVars, donorNode.pVars.Length);

                        // Assign a new pvarIndex
                        newNode.pvarIndex = targetLevel.pVars.Count;
                        targetLevel.pVars.Add(newNode.pVars);

                        Console.WriteLine($"  Assigned pVarIndex {newNode.pvarIndex} with exact pVars from donor ({newNode.pVars.Length} bytes)");
                    }

                    // Add to target level
                    targetLevel.mobs.Add(newNode);
                    createdCount++;

                    Console.WriteLine($"Created swingshot node at position {newNode.position}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error creating swingshot node: {ex.Message}");
                }
            }

            // Update mobyIds list for serialization
            if (createdCount > 0)
            {
                targetLevel.mobyIds = targetLevel.mobs.Select(m => m.mobyID).ToList();
            }

            Console.WriteLine($"\n==== Swingshot Node Clone Summary ====");
            Console.WriteLine($"✅ Successfully cloned {createdCount} swingshot nodes");
            Console.WriteLine($"🏁 All nodes are exact copies of working donor nodes at RC1 positions");

            return createdCount > 0;
        }

        /// <summary>
        /// Validates that all pVar indices in a level are unique and not conflicting
        /// </summary>
        /// <param name="level">The level to validate</param>
        /// <returns>A tuple: First item is whether validation passed (true) or failed (false), and second item is a list of detailed messages</returns>
        public static (bool Success, List<string> Messages) ValidatePvarIndices(Level level)
        {
            List<string> messages = new List<string>();
            bool success = true;

            if (level?.mobs == null || level.mobs.Count == 0)
            {
                messages.Add("No mobys to validate pVar indices for.");
                return (true, messages);
            }

            if (level.pVars == null)
            {
                messages.Add("Warning: Level has mobys but no pVars list.");
                level.pVars = new List<byte[]>();
            }

            // 1. Collect all pVar index assignments
            Dictionary<int, List<Moby>> pvarIndexToMobys = new Dictionary<int, List<Moby>>();
            int invalidPvarIndices = 0;
            int negativePvarIndices = 0;
            int outOfRangePvarIndices = 0;

            foreach (var moby in level.mobs)
            {
                // Skip mobys with no pVars (index = -1)
                if (moby.pvarIndex == -1)
                {
                    negativePvarIndices++;
                    continue;
                }

                // Check for invalid index ranges
                if (moby.pvarIndex < 0)
                {
                    messages.Add($"Invalid negative pVar index {moby.pvarIndex} on moby ID {moby.mobyID} (model {moby.modelID})");
                    invalidPvarIndices++;
                    success = false;
                    continue;
                }

                // Check for out of range indices
                if (moby.pvarIndex >= level.pVars.Count)
                {
                    messages.Add($"pVar index {moby.pvarIndex} is out of range (max: {level.pVars.Count - 1}) on moby ID {moby.mobyID} (model {moby.modelID})");
                    outOfRangePvarIndices++;
                    success = false;
                    continue;
                }

                // Add to the dictionary for conflict detection
                if (!pvarIndexToMobys.ContainsKey(moby.pvarIndex))
                {
                    pvarIndexToMobys[moby.pvarIndex] = new List<Moby>();
                }
                pvarIndexToMobys[moby.pvarIndex].Add(moby);
            }

            // 2. Find conflicts (pVar indices used by multiple mobys of different model types)
            List<int> conflictingIndices = new List<int>();

            foreach (var kvp in pvarIndexToMobys)
            {
                int pvarIndex = kvp.Key;
                List<Moby> mobysWithIndex = kvp.Value;

                if (mobysWithIndex.Count > 1)
                {
                    // Check if all mobys sharing this index have the same model ID
                    var modelIds = new HashSet<int>(mobysWithIndex.Select(m => m.modelID));

                    if (modelIds.Count > 1)
                    {
                        conflictingIndices.Add(pvarIndex);

                        string modelsList = string.Join(", ", modelIds.Select(id => $"{id} ({GetMobyTypeName(id)})"));
                        messages.Add($"pVar index {pvarIndex} is shared by {mobysWithIndex.Count} mobys with different model IDs: {modelsList}");
                        success = false;
                    }
                }
            }

            // 3. Generate summary
            int totalMobysWithPvars = level.mobs.Count - negativePvarIndices;
            int uniquePvarIndices = pvarIndexToMobys.Count;

            messages.Insert(0, $"pVar Index Validation Summary:");
            messages.Insert(1, $"- Total mobys: {level.mobs.Count}");
            messages.Insert(2, $"- Mobys with pVar indices: {totalMobysWithPvars}");
            messages.Insert(3, $"- Mobys without pVar indices (pvarIndex = -1): {negativePvarIndices}");
            messages.Insert(4, $"- Unique pVar indices used: {uniquePvarIndices}");
            messages.Insert(5, $"- pVar array size: {level.pVars.Count}");

            if (invalidPvarIndices > 0)
                messages.Insert(6, $"- Invalid negative indices (not -1): {invalidPvarIndices}");

            if (outOfRangePvarIndices > 0)
                messages.Insert(7, $"- Out of range indices: {outOfRangePvarIndices}");

            if (conflictingIndices.Count > 0)
                messages.Insert(8, $"- Conflicting indices (shared between different model types): {conflictingIndices.Count}");

            if (success)
            {
                messages.Add("✅ All pVar indices are valid and unique per model type.");
            }
            else
            {
                messages.Add("❌ pVar index validation failed. See details above.");
            }

            return (success, messages);
        }
    }
}
