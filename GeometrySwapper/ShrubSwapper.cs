using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibReplanetizer;
using LibReplanetizer.Headers;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using OpenTK.Mathematics;
using static LibReplanetizer.DataFunctions;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles the transfer of Shrub models and instances from RC1 levels to RC2 levels
    /// </summary>
    public class ShrubSwapper
    {
        /// <summary>
        /// Options for controlling Shrub swapping behavior
        /// </summary>
        [Flags]
        public enum ShrubSwapOptions
        {
            None = 0,
            UseRC1Placements = 1,
            UseRC1Models = 2,
            MapTextures = 4,

            PlacementsOnly = UseRC1Placements,
            PlacementsAndModels = UseRC1Placements | UseRC1Models,
            FullSwap = UseRC1Placements | UseRC1Models | MapTextures,
            Default = PlacementsAndModels
        }

        /// <summary>
        /// Swaps Shrub objects from an RC1 level to an RC2 level
        /// </summary>
        /// <param name="targetLevel">The RC2 level where Shrubs will be replaced</param>
        /// <param name="rc1SourceLevel">The RC1 level containing the source Shrubs</param>
        /// <param name="options">Options to control the swap behavior</param>
        /// <returns>True if the operation was successful</returns>
        public static bool SwapShrubsWithRC1Oltanis(Level targetLevel, Level rc1SourceLevel, ShrubSwapOptions options = ShrubSwapOptions.Default)
        {
            if (targetLevel == null || rc1SourceLevel == null)
            {
                Console.WriteLine("  ❌ Error: One of the levels is null");
                return false;
            }

            try
            {
                Console.WriteLine("\n==== Swapping Shrubs to match RC1 Oltanis ====");
                Console.WriteLine($"Options: Placements={options.HasFlag(ShrubSwapOptions.UseRC1Placements)}, " +
                                 $"Models={options.HasFlag(ShrubSwapOptions.UseRC1Models)}, " +
                                 $"MapTextures={options.HasFlag(ShrubSwapOptions.MapTextures)}");

                // Step 1: Remove all existing Shrubs from the target level
                RemoveAllShrubs(targetLevel);

                // Step 2: Import Shrub models from RC1 if needed
                Dictionary<int, int> shrubModelIdMapping = new Dictionary<int, int>();
                if (options.HasFlag(ShrubSwapOptions.UseRC1Models))
                {
                    shrubModelIdMapping = ImportRC1ShrubModelsToRC2Level(targetLevel, rc1SourceLevel);
                }

                // Step 3: Map textures FIRST (before transferring instances)
                if (options.HasFlag(ShrubSwapOptions.MapTextures))
                {
                    MapShrubTextures(targetLevel, rc1SourceLevel);
                }

                // Step 4: Import Shrubs from RC1 to RC2 (now that textures are available)
                TransferShrubInstances(targetLevel, rc1SourceLevel, shrubModelIdMapping);

                // Step 5: Validate texture references after transfer
                if (options.HasFlag(ShrubSwapOptions.MapTextures))
                {
                    ValidateShrubTextureReferencesAfterMapping(targetLevel);
                }

                // Step 6: Resolve any ID conflicts
                ResolveMobyShrubIdConflicts(targetLevel);

                // Step 7: Update light references to use Light 0
                UpdateShrubLightReferences(targetLevel);

                // Step 8: Update the shrubIds list to match the models
                UpdateShrubIds(targetLevel);

                // Step 9: Create advanced occlusion data for performance
                CreateShrubOcclusionData(targetLevel);

                // Step 10: Optimize group data for vegetation rendering
                OptimizeShrubGroupData(targetLevel);

                // Validate the final state
                Console.WriteLine("\n--- POST-SWAP VALIDATION ---");
                Console.WriteLine($"Target level now has {targetLevel.shrubs.Count} shrubs");
                Console.WriteLine($"Target level now has {targetLevel.shrubModels.Count} shrub models");

                // Make sure shrubIds has entries for each shrub instance
                if (targetLevel.shrubIds.Count != targetLevel.shrubs.Count)
                {
                    Console.WriteLine($"⚠️ Warning: shrubIds count ({targetLevel.shrubIds.Count}) doesn't match shrub count ({targetLevel.shrubs.Count})");
                    targetLevel.shrubIds.Clear();
                    foreach (var shrub in targetLevel.shrubs)
                    {
                        targetLevel.shrubIds.Add(shrub.modelID);
                    }
                    Console.WriteLine($"✅ Updated shrubIds list with {targetLevel.shrubIds.Count} entries");
                }
                else
                {
                    Console.WriteLine($"✅ shrubIds list correctly contains {targetLevel.shrubIds.Count} entries");
                }

                // Validate that all shrubs reference valid models
                bool allValid = true;
                foreach (var shrub in targetLevel.shrubs)
                {
                    if (shrub.model == null || targetLevel.shrubModels.All(m => m.id != shrub.modelID))
                    {
                        Console.WriteLine($"⚠️ Shrub references invalid model ID: {shrub.modelID}");
                        allValid = false;
                    }
                }

                if (allValid)
                {
                    Console.WriteLine("✅ All shrubs reference valid models");
                }

                Console.WriteLine("✅ Successfully transferred shrubs from RC1 to RC2 level");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during shrub transfer: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Removes all shrubs from the target level
        /// </summary>
        private static void RemoveAllShrubs(Level targetLevel)
        {
            int count = targetLevel.shrubs.Count;
            targetLevel.shrubs.Clear();
            Console.WriteLine($"  ✅ Removed {count} existing shrubs from target level");
        }

        /// <summary>
        /// Maps shrub textures from RC1 to RC2 level using an append-based approach
        /// This is safer and more reliable than the current find-or-replace method
        /// </summary>
        /// <param name="targetLevel">The RC2 target level</param>
        /// <param name="rc1SourceLevel">The RC1 source level</param>
        private static void MapShrubTextures(Level targetLevel, Level rc1SourceLevel)
        {
            Console.WriteLine("  🔄 Mapping textures for RC1 Shrub models using append-based approach...");

            if (rc1SourceLevel.textures == null || rc1SourceLevel.textures.Count == 0)
            {
                Console.WriteLine("  ⚠️ No textures found in RC1 source level");
                return;
            }

            // Make sure target level has a texture list
            if (targetLevel.textures == null)
            {
                targetLevel.textures = new List<Texture>();
            }

            // Find all texture IDs used by Shrub models in RC1
            HashSet<int> usedTextureIdsInRC1 = new HashSet<int>();
            foreach (var model in rc1SourceLevel.shrubModels?.OfType<ShrubModel>() ?? Enumerable.Empty<ShrubModel>())
            {
                if (model.textureConfig != null)
                {
                    foreach (var texConfig in model.textureConfig)
                    {
                        usedTextureIdsInRC1.Add(texConfig.id);
                    }
                }
            }

            Console.WriteLine($"  Found {usedTextureIdsInRC1.Count} unique texture IDs used by RC1 Shrub models");

            if (usedTextureIdsInRC1.Count == 0)
            {
                Console.WriteLine("  No textures to import");
                return;
            }

            // Create mapping from RC1 texture IDs to new target texture IDs
            Dictionary<int, int> textureIdMapping = new Dictionary<int, int>();
            
            // Remember the starting index where we'll append RC1 textures
            int appendStartIndex = targetLevel.textures.Count;
            
            // Sort the texture IDs for consistent processing
            var sortedTextureIds = usedTextureIdsInRC1.OrderBy(id => id).ToList();
            
            Console.WriteLine($"  Appending RC1 textures starting at index {appendStartIndex}");

            int texturesImported = 0;
            int duplicatesSkipped = 0;
            
            foreach (int rc1TextureId in sortedTextureIds)
            {
                // Skip invalid texture IDs
                if (rc1TextureId < 0 || rc1TextureId >= rc1SourceLevel.textures.Count)
                {
                    Console.WriteLine($"  ⚠️ Texture ID {rc1TextureId} is out of range for RC1 source level");
                    continue;
                }

                var rc1Texture = rc1SourceLevel.textures[rc1TextureId];
                if (rc1Texture == null || rc1Texture.data == null || rc1Texture.data.Length == 0)
                {
                    Console.WriteLine($"  ⚠️ Texture ID {rc1TextureId} has no data, skipping");
                    continue;
                }

                // Check if we already have an identical texture in the target level
                // This prevents unnecessary duplication
                int existingTextureId = FindExistingShrubTexture(targetLevel, rc1Texture);
                
                if (existingTextureId != -1)
                {
                    // Reuse existing texture
                    textureIdMapping[rc1TextureId] = existingTextureId;
                    duplicatesSkipped++;
                    Console.WriteLine($"  📝 Reusing existing texture at index {existingTextureId} for RC1 texture {rc1TextureId}");
                }
                else
                {
                    // Append the texture to the end of the list
                    Texture newTexture = DeepCloneTexture(rc1Texture);
                    
                    // Update the texture's ID to reflect its new position
                    newTexture.id = targetLevel.textures.Count;
                    
                    targetLevel.textures.Add(newTexture);
                    int newTextureId = targetLevel.textures.Count - 1;
                    
                    textureIdMapping[rc1TextureId] = newTextureId;
                    texturesImported++;
                    
                    Console.WriteLine($"  ✅ Appended RC1 texture {rc1TextureId} as {newTextureId} ({newTexture.width}x{newTexture.height})");
                }
            }

            // Now update texture references in Shrub models that we imported
            if (targetLevel.shrubModels != null)
            {
                int modelsUpdated = 0;
                int textureReferencesUpdated = 0;
                
                foreach (var model in targetLevel.shrubModels)
                {
                    if (model.textureConfig == null || model.textureConfig.Count == 0)
                        continue;

                    bool modelUpdated = false;
                    foreach (var texConfig in model.textureConfig)
                    {
                        // Only update texture references that have mappings
                        // (i.e., they came from RC1 Shrub models)
                        if (textureIdMapping.TryGetValue(texConfig.id, out int newId))
                        {
                            int oldId = texConfig.id;
                            texConfig.id = newId;
                            modelUpdated = true;
                            textureReferencesUpdated++;
                            
                            if (textureReferencesUpdated <= 10) // Limit logging
                            {
                                Console.WriteLine($"    Updated texture ref in model {model.id}: {oldId} → {newId}");
                            }
                        }
                    }

                    if (modelUpdated)
                        modelsUpdated++;
                }

                Console.WriteLine($"  ✅ Updated texture references in {modelsUpdated} Shrub models ({textureReferencesUpdated} total references)");
            }

            Console.WriteLine($"  ✅ Imported {texturesImported} new textures, reused {duplicatesSkipped} existing textures");
            Console.WriteLine($"  📊 Target level now has {targetLevel.textures.Count} total textures");
        }

        /// <summary>
        /// Finds an existing texture in the target level that matches the source texture
        /// </summary>
        /// <param name="targetLevel">Level to search in</param>
        /// <param name="sourceTexture">Texture to find a match for</param>
        /// <returns>Index of matching texture, or -1 if not found</returns>
        private static int FindExistingShrubTexture(Level targetLevel, Texture sourceTexture)
        {
            if (sourceTexture == null || targetLevel.textures == null)
                return -1;

            for (int i = 0; i < targetLevel.textures.Count; i++)
            {
                if (TextureEquals(sourceTexture, targetLevel.textures[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Enhanced texture comparison that includes content hash for better duplicate detection
        /// </summary>
        /// <param name="tex1">First texture to compare</param>
        /// <param name="tex2">Second texture to compare</param>
        /// <returns>True if textures are equivalent, false otherwise</returns>
        private static bool TextureEquals(Texture tex1, Texture tex2)
        {
            if (tex1 == null || tex2 == null)
                return false;

            // Quick property comparison first
            if (tex1.width != tex2.width || 
                tex1.height != tex2.height || 
                tex1.data?.Length != tex2.data?.Length)
                return false;

            // If basic properties match, compare actual data
            if (tex1.data != null && tex2.data != null)
            {
                // For performance, we can compare just a hash or checksum
                // For now, let's do a simple byte comparison
                return tex1.data.SequenceEqual(tex2.data);
            }

            return tex1.data == tex2.data; // Both null
        }

        /// <summary>
        /// Creates a deep clone of a texture
        /// </summary>
        private static Texture DeepCloneTexture(Texture sourceTexture)
        {
            byte[] newData = new byte[0];
            if (sourceTexture.data != null)
            {
                newData = new byte[sourceTexture.data.Length];
                Array.Copy(sourceTexture.data, newData, sourceTexture.data.Length);
            }

            Texture newTexture = new Texture(
                sourceTexture.id,
                sourceTexture.width,
                sourceTexture.height,
                newData
            );

            newTexture.mipMapCount = sourceTexture.mipMapCount;
            newTexture.off06 = sourceTexture.off06;
            newTexture.off08 = sourceTexture.off08;
            newTexture.off0C = sourceTexture.off0C;
            newTexture.off10 = sourceTexture.off10;
            newTexture.off14 = sourceTexture.off14;
            newTexture.off1C = sourceTexture.off1C;
            newTexture.off20 = sourceTexture.off20;
            newTexture.vramPointer = sourceTexture.vramPointer;

            return newTexture;
        }

        /// <summary>
        /// Imports RC1 shrub models into the RC2 level
        /// </summary>
        public static Dictionary<int, int> ImportRC1ShrubModelsToRC2Level(Level targetLevel, Level rc1SourceLevel)
        {
            var modelIdMapping = new Dictionary<int, int>();
            int initialModelCount = targetLevel.shrubModels.Count;

            // Build a list of existing IDs
            HashSet<short> existingIds = new HashSet<short>();
            foreach (var model in targetLevel.shrubModels)
            {
                existingIds.Add(model.id);
            }

            // Track the next available ID to use
            short nextId = 0;
            while (existingIds.Contains(nextId)) nextId++;

            // Process each RC1 shrub model
            foreach (var sourceModel in rc1SourceLevel.shrubModels)
            {
                // Clone the RC1 model
                ShrubModel newModel = CloneShrubModel((ShrubModel) sourceModel);

                // Assign a new ID if there's a conflict
                short originalId = newModel.id;
                if (existingIds.Contains(newModel.id))
                {
                    newModel.id = nextId++;
                    while (existingIds.Contains(nextId)) nextId++;
                }

                existingIds.Add(newModel.id);
                targetLevel.shrubModels.Add(newModel);
                modelIdMapping[originalId] = newModel.id;
            }

            int addedCount = targetLevel.shrubModels.Count - initialModelCount;
            Console.WriteLine($"  ✅ Added {addedCount} shrub models");
            return modelIdMapping;
        }

        /// <summary>
        /// Creates a complete clone of a ShrubModel
        /// </summary>
        public static ShrubModel CloneShrubModel(ShrubModel sourceModel)
        {
            if (sourceModel == null)
                throw new ArgumentNullException(nameof(sourceModel), "Source model cannot be null");

            Console.WriteLine($"  Cloning shrub model {sourceModel.id}");
            
            // Create a temporary byte array with the minimum required data
            byte[] shrubBlock = new byte[0x40]; // Standard size for shrub model header
            
            // Write key values into the shrub block
            WriteShort(shrubBlock, 0x30, sourceModel.id);
            WriteFloat(shrubBlock, 0x00, sourceModel.cullingX);
            WriteFloat(shrubBlock, 0x04, sourceModel.cullingY);
            WriteFloat(shrubBlock, 0x08, sourceModel.cullingZ);
            WriteFloat(shrubBlock, 0x0C, sourceModel.cullingRadius);
            WriteUint(shrubBlock, 0x20, sourceModel.off20);
            WriteShort(shrubBlock, 0x2A, sourceModel.off2A);
            WriteUint(shrubBlock, 0x2C, sourceModel.off2C);
            WriteUint(shrubBlock, 0x34, sourceModel.off34);
            WriteUint(shrubBlock, 0x38, sourceModel.off38);
            WriteUint(shrubBlock, 0x3C, sourceModel.off3C);
            
            // Create a new instance using the minimal data
            // Note: This approach means the constructor will initialize with empty
            // vertex/index buffers and texture configs, which we'll overwrite next
            ShrubModel newModel = new ShrubModel(null, shrubBlock, 0);
            
            // Clone vertex buffer
            if (sourceModel.vertexBuffer != null && sourceModel.vertexBuffer.Length > 0)
            {
                newModel.vertexBuffer = new float[sourceModel.vertexBuffer.Length];
                Array.Copy(sourceModel.vertexBuffer, newModel.vertexBuffer, sourceModel.vertexBuffer.Length);
                Console.WriteLine($"    Copied {sourceModel.vertexBuffer.Length / 8} vertices");
            }
            else
            {
                Console.WriteLine("    Warning: Source model has no vertex data");
                newModel.vertexBuffer = new float[0];
            }

            // Clone index buffer
            if (sourceModel.indexBuffer != null && sourceModel.indexBuffer.Length > 0)
            {
                newModel.indexBuffer = new ushort[sourceModel.indexBuffer.Length];
                Array.Copy(sourceModel.indexBuffer, newModel.indexBuffer, sourceModel.indexBuffer.Length);
                Console.WriteLine($"    Copied {sourceModel.indexBuffer.Length / 3} triangles");
            }
            else
            {
                Console.WriteLine("    Warning: Source model has no index data");
                newModel.indexBuffer = new ushort[0];
            }

            // Clone texture configuration
            newModel.textureConfig = new List<TextureConfig>();
            if (sourceModel.textureConfig != null)
            {
                foreach (var texConfig in sourceModel.textureConfig)
                {
                    TextureConfig newConfig = new TextureConfig
                    {
                        id = texConfig.id,
                        start = texConfig.start,
                        size = texConfig.size,
                        mode = texConfig.mode,
                        wrapModeS = texConfig.wrapModeS,
                        wrapModeT = texConfig.wrapModeT
                    };
                    newModel.textureConfig.Add(newConfig);
                }
                Console.WriteLine($"    Copied {sourceModel.textureConfig.Count} texture configurations");
            }
            else
            {
                Console.WriteLine("    Warning: Source model has no texture configs");
            }

            Console.WriteLine($"  ✅ Successfully cloned shrub model {sourceModel.id}");
            return newModel;
        }

        /// <summary>
        /// Transfers shrub instances from RC1 to RC2 level
        /// </summary>
        public static bool TransferShrubInstances(Level targetLevel, Level rc1SourceLevel, Dictionary<int, int> modelIdMapping)
        {
            int count = 0;

            // Process each shrub from RC1
            foreach (var rc1Shrub in rc1SourceLevel.shrubs)
            {
                // Create a new shrub instance based on the RC1 shrub
                Shrub newShrub = new Shrub(rc1Shrub);

                // Map to the new model ID if needed
                if (modelIdMapping.ContainsKey(newShrub.modelID))
                {
                    newShrub.modelID = modelIdMapping[newShrub.modelID];
                }

                // Find the corresponding model
                newShrub.model = targetLevel.shrubModels.Find(m => m.id == newShrub.modelID);

                // Only add if we found a model
                if (newShrub.model != null)
                {
                    targetLevel.shrubs.Add(newShrub);
                    count++;
                }
                else
                {
                    Console.WriteLine($"  ⚠️ Could not find model ID {newShrub.modelID} for shrub");
                }
            }

            Console.WriteLine($"  ✅ Transferred {count} shrubs from RC1 level");
            return count > 0;
        }

        /// <summary>
        /// Resolves conflicts between Moby and Shrub model IDs
        /// </summary>
        public static int ResolveMobyShrubIdConflicts(Level level)
        {
            int changedCount = 0;
            HashSet<short> mobyIds = new HashSet<short>();

            // Collect all moby IDs
            foreach (var model in level.mobyModels)
            {
                mobyIds.Add(model.id);
            }

            // Find shrub models with conflicting IDs
            var conflictIds = level.shrubModels
                .Where(model => mobyIds.Contains(model.id))
                .ToList();

            if (conflictIds.Count == 0)
            {
                Console.WriteLine("  ✅ No ID conflicts detected between shrub and moby models");
                return 0;
            }

            // Find the highest existing ID
            short maxId = level.shrubModels.Max(model => model.id);
            short nextId = (short) (maxId + 1);

            // Resolve conflicts by assigning new IDs
            foreach (var model in conflictIds)
            {
                short oldId = model.id;
                model.id = nextId++;
                changedCount++;
                Console.WriteLine($"  ✅ Changed shrub model ID from {oldId} to {model.id} to resolve conflict");

                // Update any shrub instances using this model
                foreach (var shrub in level.shrubs.Where(s => s.modelID == oldId))
                {
                    shrub.modelID = model.id;
                    shrub.model = model;
                }
            }

            Console.WriteLine($"  ✅ Resolved {changedCount} ID conflicts between shrub and moby models");
            return changedCount;
        }

        /// <summary>
        /// Updates all shrub light references to use Light 0
        /// </summary>
        private static void UpdateShrubLightReferences(Level level)
        {
            int count = 0;
            if (level.shrubs != null)
            {
                foreach (var shrub in level.shrubs)
                {
                    if (shrub.light != 0)
                    {
                        shrub.light = 0;
                        count++;
                    }
                }
            }
            Console.WriteLine($"  ✅ Updated {count} shrubs to use Light 0");
        }

        /// <summary>
        /// Interactive wrapper for shrub swapping function
        /// </summary>
        /// <returns>True if the operation was successful</returns>
        public static bool SwapShrubsWithRC1OltanisInteractive()
        {
            Console.WriteLine("\n==== Swap RC2 Shrubs with RC1 Oltanis Shrubs ====");

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
                targetLevel = new Level(targetPath);
                rc1OltanisLevel = new Level(rc1OltanisPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading levels: {ex.Message}");
                return false;
            }

            // Option selection
            Console.WriteLine("\nSelect swap options:");
            Console.WriteLine("1. Full replacement (RC1 models and positions)");
            Console.WriteLine("2. Placements only (keep RC2 models but use RC1 positions)");
            Console.WriteLine("3. Custom options");
            Console.Write("> ");
            string choice = Console.ReadLine()?.Trim() ?? "1";
            ShrubSwapOptions options;
            switch (choice)
            {
                case "2":
                    options = ShrubSwapOptions.PlacementsOnly;
                    break;
                case "3":
                    options = GetCustomOptions();
                    break;
                case "1":
                default:
                    options = ShrubSwapOptions.FullSwap;
                    break;
            }

            bool success = SwapShrubsWithRC1Oltanis(targetLevel, rc1OltanisLevel, options);

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
        private static ShrubSwapOptions GetCustomOptions()
        {
            ShrubSwapOptions options = ShrubSwapOptions.None;

            Console.WriteLine("\nCustomize swap options:");

            if (GetYesNoInput("Use RC1 shrub placements? (y/n): "))
                options |= ShrubSwapOptions.UseRC1Placements;

            if (GetYesNoInput("Use RC1 shrub models? (y/n): "))
                options |= ShrubSwapOptions.UseRC1Models;

            if (GetYesNoInput("Map RC1 textures to RC2 level? (y/n): "))
                options |= ShrubSwapOptions.MapTextures;

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
        /// Updates the level's shrubIds list to include both model IDs and instance data needed for serialization
        /// </summary>
        /// <param name="level">The level to update</param>
        private static void UpdateShrubIds(Level level)
        {
            if (level.shrubModels == null)
                return;
            
            // First, ensure we have a shrubIds list
            if (level.shrubIds == null)
            {
                level.shrubIds = new List<int>();
            }
            else
            {
                level.shrubIds.Clear();
            }
            
            // Add the actual shrub instance IDs
            // This is crucial - the serializer uses this list during save
            if (level.shrubs != null)
            {
                foreach (var shrub in level.shrubs)
                {
                    level.shrubIds.Add(shrub.modelID);
                }
            }
            
            Console.WriteLine($"  ✅ Updated shrubIds list with {level.shrubIds.Count} entries");
            
            // Ensure the shrub data byte array is recreated to match the correct shrub count
            if (level.shrubs != null)
            {
                // Create or update the shrub data byte array
                level.shrubData = new byte[level.shrubs.Count * Shrub.ELEMENTSIZE];
                int offset = 0;
                
                // Write each shrub's data into the byte array
                foreach (var shrub in level.shrubs)
                {
                    byte[] shrubBytes = shrub.ToByteArray();
                    Array.Copy(shrubBytes, 0, level.shrubData, offset, shrubBytes.Length);
                    offset += shrubBytes.Length;
                }
                
                Console.WriteLine($"  ✅ Created shrub data array with {level.shrubs.Count} entries");
            }
            
            // Also initialize shrubGroupData if it doesn't exist
            if (level.shrubGroupData == null || level.shrubGroupData.Length == 0)
            {
                // Create minimal valid shrub group data
                level.shrubGroupData = new byte[8];
                Console.WriteLine("  ✅ Created minimal shrub group data");
            }
        }

        /// <summary>
        /// Validates that all Shrub models have valid texture references after texture mapping
        /// </summary>
        /// <param name="level">The level to validate</param>
        private static void ValidateShrubTextureReferencesAfterMapping(Level level)
        {
            if (level.shrubModels == null || level.textures == null)
                return;

            Console.WriteLine("\n🔍 Validating Shrub texture references after mapping...");

            int modelsChecked = 0;
            int invalidReferences = 0;
            int referencesFixed = 0;

            foreach (var model in level.shrubModels.OfType<ShrubModel>())
            {
                if (model.textureConfig == null || model.textureConfig.Count == 0)
                    continue;

                modelsChecked++;
                
                for (int i = 0; i < model.textureConfig.Count; i++)
                {
                    var texConfig = model.textureConfig[i];
                    
                    if (texConfig.id < 0 || texConfig.id >= level.textures.Count)
                    {
                        invalidReferences++;
                        
                        // Try to fix by clamping to valid range
                        int oldId = texConfig.id;
                        texConfig.id = Math.Clamp(texConfig.id, 0, level.textures.Count - 1);
                        referencesFixed++;
                        
                        Console.WriteLine($"  ⚠️ Fixed invalid texture ID in model {model.id}: {oldId} → {texConfig.id}");
                    }
                }
            }

            if (invalidReferences == 0)
            {
                Console.WriteLine($"  ✅ All {modelsChecked} Shrub models have valid texture references");
            }
            else
            {
                Console.WriteLine($"  ⚠️ Fixed {referencesFixed} invalid texture references in {modelsChecked} models");
            }
        }

        /// <summary>
        /// Creates advanced occlusion data for Shrubs to improve rendering performance
        /// </summary>
        /// <param name="level">The level to update with occlusion data</param>
        private static void CreateShrubOcclusionData(Level level)
        {
            if (level.shrubs == null || level.shrubs.Count == 0)
                return;

            Console.WriteLine($"🌳 Creating advanced Shrub occlusion data for {level.shrubs.Count} instances...");

            // Initialize occlusion data if it doesn't exist
            if (level.occlusionData == null)
            {
                InitializeOcclusionData(level);
            }

            // Clear existing Shrub occlusion data
            level.occlusionData.shrubData.Clear();

            // Create spatial groups for better culling
            var spatialGroups = CreateShrubSpatialGroups(level.shrubs);
            Console.WriteLine($"  🌿 Created {spatialGroups.Count} spatial groups for Shrub culling");

            // Generate occlusion data for each Shrub
            for (int i = 0; i < level.shrubs.Count; i++)
            {
                var shrub = level.shrubs[i];
                
                // Calculate spatial hash for this Shrub's position
                int spatialHash = CalculateSpatialHash(shrub.position);
                
                // Calculate visibility flags based on various criteria
                int visibilityFlags = CalculateShrubVisibilityFlags(shrub, spatialGroups, i);
                
                // Add to occlusion data
                level.occlusionData.shrubData.Add(new KeyValuePair<int, int>(spatialHash, visibilityFlags));
            }

            Console.WriteLine($"  ✅ Generated occlusion data for {level.shrubs.Count} Shrubs with spatial culling");
        }

        /// <summary>
        /// Creates spatial groups for Shrubs optimized for vegetation rendering
        /// </summary>
        private static List<List<Shrub>> CreateShrubSpatialGroups(List<Shrub> shrubs)
        {
            var groups = new List<List<Shrub>>();
            var spatialGrid = new Dictionary<Vector3i, List<Shrub>>();
            
            const float GRID_SIZE = 60.0f; // Smaller grid for vegetation (denser distribution)
            
            // Group Shrubs by spatial grid
            foreach (var shrub in shrubs)
            {
                var gridPos = new Vector3i(
                    (int)(shrub.position.X / GRID_SIZE),
                    (int)(shrub.position.Y / GRID_SIZE),
                    (int)(shrub.position.Z / GRID_SIZE)
                );
                
                if (!spatialGrid.ContainsKey(gridPos))
                {
                    spatialGrid[gridPos] = new List<Shrub>();
                }
                
                spatialGrid[gridPos].Add(shrub);
            }
            
            // Process each spatial cell
            foreach (var gridGroup in spatialGrid.Values)
            {
                // Group by model for better batching
                var modelGroups = gridGroup.GroupBy(s => s.modelID).ToList();

                foreach (var modelGroup in modelGroups)
                {
                    var shrubList = modelGroup.ToList(); // ← Changed name to avoid conflict

                    // Shrubs can handle larger batches than TIEs (vegetation is typically simpler)
                    const int MAX_GROUP_SIZE = 32;
                    for (int i = 0; i < shrubList.Count; i += MAX_GROUP_SIZE)
                    {
                        var subGroup = shrubList.Skip(i).Take(MAX_GROUP_SIZE).ToList();
                        groups.Add(subGroup);
                    }
                }
            }
            
            return groups;
        }

        /// <summary>
        /// Calculates visibility flags for a Shrub optimized for vegetation rendering
        /// </summary>
        private static int CalculateShrubVisibilityFlags(Shrub shrub, List<List<Shrub>> spatialGroups, int shrubIndex)
        {
            int flags = 0x00000001; // Base visibility flag
            
            // Distance-based culling (more aggressive than TIEs since shrubs are usually decorative)
            float distanceFromOrigin = shrub.position.Length;
            if (distanceFromOrigin > 800.0f)
            {
                flags |= 0x00000010; // Far distance - very aggressive culling
            }
            else if (distanceFromOrigin > 400.0f)
            {
                flags |= 0x00000008; // Medium distance - aggressive culling
            }
            else if (distanceFromOrigin > 150.0f)
            {
                flags |= 0x00000004; // Near distance - moderate culling
            }
            
            // Height-based culling
            if (Math.Abs(shrub.position.Y) > 200.0f)
            {
                flags |= 0x00000020; // High altitude culling
            }
            
            // Draw distance optimization
            if (shrub.drawDistance > 0)
            {
                if (shrub.drawDistance < 50.0f)
                {
                    flags |= 0x00000040; // Short draw distance - early culling
                }
                else if (shrub.drawDistance > 200.0f)
                {
                    flags |= 0x00000002; // Long draw distance - preserve visibility
                }
            }
            
            // Model complexity (vegetation can be quite complex)
            if (shrub.model != null)
            {
                int vertexCount = shrub.model.vertexBuffer?.Length / 8 ?? 0;
                if (vertexCount > 800) // High-poly vegetation
                {
                    flags |= 0x00000080; // Complex vegetation - more aggressive culling
                }
            }
            
            // Spatial group index
            for (int i = 0; i < spatialGroups.Count; i++)
            {
                if (spatialGroups[i].Contains(shrub))
                {
                    flags |= (i & 0xFF) << 16; // Store group index in upper bits
                    break;
                }
            }
            
            // Instance priority
            int priority = Math.Max(0, 255 - shrubIndex / 8); // More gradual priority decrease
            flags |= (priority & 0xFF) << 8;
            
            return flags;
        }

        /// <summary>
        /// Optimizes Shrub group data for better rendering performance
        /// </summary>
        private static void OptimizeShrubGroupData(Level level)
        {
            if (level.shrubs == null || level.shrubModels == null)
                return;

            Console.WriteLine("🌿 Optimizing Shrub group data for vegetation rendering...");

            // Group shrubs by model for optimal rendering
            var shrubsByModel = level.shrubs.GroupBy(s => s.modelID).ToDictionary(g => g.Key, g => g.ToList());
            
            // Calculate optimal group data size
            int baseSize = level.shrubModels.Count * 12; // 12 bytes per model group (optimized for shrubs)
            int alignedSize = ((baseSize + 0x3F) / 0x40) * 0x40; // Align to 0x40 boundary (smaller than TIEs)
            
            byte[] optimizedGroupData = new byte[alignedSize];
            
            // Fill group data with vegetation-specific optimizations
            for (int i = 0; i < level.shrubModels.Count; i++)
            {
                var model = level.shrubModels[i];
                int groupOffset = i * 12;
                
                if (shrubsByModel.TryGetValue(model.id, out var shrubs))
                {
                    // Calculate density and distribution for this model's instances
                    if (shrubs.Count > 0)
                    {
                        // Calculate bounding box for distribution analysis
                        var minPos = shrubs[0].position;
                        var maxPos = shrubs[0].position;
                        foreach (var shrub in shrubs)
                        {
                            minPos = Vector3.ComponentMin(minPos, shrub.position);
                            maxPos = Vector3.ComponentMax(maxPos, shrub.position);
                        }
                        
                        float distributionArea = (maxPos - minPos).Length;
                        float density = shrubs.Count / Math.Max(distributionArea, 1.0f);
                        
                        // Store vegetation-specific information
                        WriteInt(optimizedGroupData, groupOffset + 0, i * 0x70); // Data offset
                        WriteInt(optimizedGroupData, groupOffset + 4, shrubs.Count); // Instance count
                        WriteFloat(optimizedGroupData, groupOffset + 8, density); // Vegetation density
                    }
                }
                else
                {
                    // No instances for this model
                    WriteInt(optimizedGroupData, groupOffset + 0, 0);
                    WriteInt(optimizedGroupData, groupOffset + 4, 0);
                    WriteFloat(optimizedGroupData, groupOffset + 8, 0.0f);
                }
            }
            
            level.shrubGroupData = optimizedGroupData;
            Console.WriteLine($"  ✅ Created optimized Shrub group data: {alignedSize} bytes with vegetation density indexing");
        }

        /// <summary>
        /// Initializes occlusion data structure if it doesn't exist
        /// </summary>
        private static void InitializeOcclusionData(Level level)
        {
            if (level.occlusionData != null)
                return;

            // Create proper header with current counts
            byte[] headerBlock = new byte[16];
            WriteInt(headerBlock, 0x00, level.mobs?.Count ?? 0);
            WriteInt(headerBlock, 0x04, level.ties?.Count ?? 0);
            WriteInt(headerBlock, 0x08, level.shrubs?.Count ?? 0);
            WriteInt(headerBlock, 0x0C, 0); // Reserved

            var header = new OcclusionDataHeader(headerBlock);

            // Create empty occlusion block
            byte[] emptyBlock = new byte[0];
            level.occlusionData = new OcclusionData(emptyBlock, header);

            // Initialize the lists
            level.occlusionData.mobyData = new List<KeyValuePair<int, int>>();
            level.occlusionData.tieData = new List<KeyValuePair<int, int>>();
            level.occlusionData.shrubData = new List<KeyValuePair<int, int>>();

            Console.WriteLine("  🔧 Initialized occlusion data structure");
        }

        /// <summary>
        /// Calculates spatial hash for position-based culling
        /// </summary>
        private static int CalculateSpatialHash(Vector3 position)
        {
            // Optimized spatial hash for RC2's culling system
            int x = (int)(position.X / 40.0f);
            int y = (int)(position.Y / 40.0f);
            int z = (int)(position.Z / 40.0f);

            // FNV-1a hash variation optimized for spatial distribution
            return ((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) & 0x7FFFFFFF;
        }

        // Helper struct for spatial grid
        public struct Vector3i
        {
            public int X, Y, Z;

            public Vector3i(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public override bool Equals(object obj)
            {
                if (obj is Vector3i other)
                {
                    return X == other.X && Y == other.Y && Z == other.Z;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(X, Y, Z);
            }
        }
    }
}
