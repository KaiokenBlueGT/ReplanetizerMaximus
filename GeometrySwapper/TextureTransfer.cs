// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using System.Linq;
using LibReplanetizer;
using LibReplanetizer.Models;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles texture transfer between levels, preserving IDs and maintaining proper references
    /// </summary>
    public static class TextureTransfer
    {
        /// <summary>
        /// Imports textures from source level to target level, preserving texture IDs used in models
        /// This ensures visual consistency for transferred models
        /// </summary>
        public static bool ImportTexturesPreservingIds(Level targetLevel, Level sourceLevel, bool verbose = true)
        {
            if (targetLevel == null || sourceLevel == null)
            {
                Console.WriteLine("❌ Cannot import textures: Invalid level data");
                return false;
            }
            
            if (sourceLevel.textures == null || sourceLevel.textures.Count == 0)
            {
                Console.WriteLine("❌ Source level has no textures to import");
                return false;
            }
            
            try
            {
                // 1. Find all texture IDs referenced by models in the source level
                HashSet<int> usedTextureIds = GetUsedTextureIds(sourceLevel);
                
                if (verbose)
                    Console.WriteLine($"\n==== Importing Source Level Textures ====");
                
                // 2. Ensure target textures collection exists
                if (targetLevel.textures == null)
                {
                    targetLevel.textures = new List<Texture>();
                }
                
                // 3. Find highest texture ID needed
                int highestTextureId = usedTextureIds.Count > 0 ? usedTextureIds.Max() : -1;
                
                // 4. If needed, expand the target texture list to accommodate all IDs
                if (highestTextureId >= targetLevel.textures.Count)
                {
                    int originalSize = targetLevel.textures.Count;
                    int newSize = highestTextureId + 1;
                    
                    if (verbose)
                        Console.WriteLine($"Expanding target texture list from {originalSize} to {newSize} entries");
                    
                    // Add empty placeholder textures
                    for (int i = originalSize; i < newSize; i++)
                    {
                        targetLevel.textures.Add(new Texture(i, 0, 0, new byte[0]));
                    }
                }
                
                // 5. Import textures from source to target, preserving IDs
                int importCount = 0;
                int skippedCount = 0;
                
                foreach (int texId in usedTextureIds.OrderBy(id => id))
                {
                    // Check if texture exists in source level and has data
                    if (texId >= 0 && texId < sourceLevel.textures.Count && 
                        sourceLevel.textures[texId] != null && 
                        sourceLevel.textures[texId].data != null && 
                        sourceLevel.textures[texId].data.Length > 0)
                    {
                        // Replace the texture at the same index in the target level
                        targetLevel.textures[texId] = DeepCloneTexture(sourceLevel.textures[texId]);
                        importCount++;
                        
                        // Log occasionally to avoid flooding console
                        if (verbose && (importCount % 20 == 0 || importCount <= 5))
                        {
                            var tex = sourceLevel.textures[texId];
                            Console.WriteLine($"Imported texture {texId} ({tex.width}x{tex.height})");
                        }
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
                
                if (verbose)
                {
                    Console.WriteLine($"✅ Imported {importCount} textures from source to target level");
                    if (skippedCount > 0)
                        Console.WriteLine($"⚠️ Skipped {skippedCount} textures due to missing data or out-of-range IDs");
                }
                
                return importCount > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error importing textures: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Imports only terrain textures from source level to target level
        /// </summary>
        public static void ImportTerrainTexturesOnly(Level targetLevel, Level sourceLevel)
        {
            if (targetLevel == null || sourceLevel == null || sourceLevel.terrainEngine == null)
            {
                Console.WriteLine("Cannot import terrain textures: Invalid level data or missing terrain engine");
                return;
            }

            // Get textures referenced by terrain
            HashSet<int> terrainTextureIds = new HashSet<int>();
            
            // Add terrain texture IDs to the set
            if (sourceLevel.terrainEngine.fragments != null)
            {
                // Extract texture IDs from terrain fragments
                foreach (var fragment in sourceLevel.terrainEngine.fragments)
                {
                    if (fragment?.model?.textureConfig != null)
                    {
                        foreach (var texConfig in fragment.model.textureConfig)
                        {
                            if (texConfig.id >= 0 && texConfig.id < sourceLevel.textures.Count)
                                terrainTextureIds.Add(texConfig.id);
                        }
                    }
                }
            }
            
            Console.WriteLine($"Importing {terrainTextureIds.Count} terrain textures");
            
            // Ensure target textures collection exists
            if (targetLevel.textures == null)
            {
                targetLevel.textures = new List<Texture>();
            }
            
            // Now import only the terrain textures
            int importedCount = 0;
            foreach (int texId in terrainTextureIds)
            {
                if (texId >= 0 && texId < sourceLevel.textures.Count)
                {
                    var sourceTexture = sourceLevel.textures[texId];
                    
                    // Check if this texture already exists in the target level
                    bool exists = false;
                    for (int i = 0; i < targetLevel.textures.Count; i++)
                    {
                        if (i == texId && TextureEquals(sourceTexture, targetLevel.textures[i]))
                        {
                            exists = true;
                            break;
                        }
                    }
                    
                    if (!exists)
                    {
                        // If the target texture list is not long enough, extend it
                        while (texId >= targetLevel.textures.Count)
                        {
                            targetLevel.textures.Add(new Texture(targetLevel.textures.Count, 0, 0, new byte[0]));
                        }
                        
                        // Replace the texture at the same index
                        targetLevel.textures[texId] = DeepCloneTexture(sourceTexture);
                        importedCount++;
                    }
                }
            }
            
            Console.WriteLine($"✅ Imported {importedCount} terrain textures");
        }

        /// <summary>
        /// For debugging: Analyze and report texture usage in a level
        /// </summary>
        public static void AnalyzeTextureUsage(Level level)
        {
            if (level == null || level.textures == null)
                return;
                
            Console.WriteLine($"\n==== TEXTURE USAGE ANALYSIS ====");
            Console.WriteLine($"Total textures in level: {level.textures.Count}");
            
            // Track usage by different model types
            Dictionary<int, List<string>> usageByType = new Dictionary<int, List<string>>();
            
            // Check terrain fragments
            int terrainTextureCount = 0;
            if (level.terrainEngine?.fragments != null)
            {
                foreach (var fragment in level.terrainEngine.fragments)
                {
                    if (fragment?.model?.textureConfig != null)
                    {
                        foreach (var texConfig in fragment.model.textureConfig)
                        {
                            if (!usageByType.ContainsKey(texConfig.id))
                                usageByType[texConfig.id] = new List<string>();
                                
                            usageByType[texConfig.id].Add($"Terrain Fragment {fragment.off1E}");
                            terrainTextureCount++;
                        }
                    }
                }
            }
            
            // Check skybox
            int skyboxTextureCount = 0;
            if (level.skybox?.textureConfig != null)
            {
                foreach (var texConfig in level.skybox.textureConfig)
                {
                    if (!usageByType.ContainsKey(texConfig.id))
                        usageByType[texConfig.id] = new List<string>();
                        
                    usageByType[texConfig.id].Add("Skybox");
                    skyboxTextureCount++;
                }
            }
            
            // Check mobys
            int mobyTextureCount = 0;
            if (level.mobyModels != null)
            {
                foreach (var model in level.mobyModels)
                {
                    if (model?.textureConfig != null)
                    {
                        foreach (var texConfig in model.textureConfig)
                        {
                            if (!usageByType.ContainsKey(texConfig.id))
                                usageByType[texConfig.id] = new List<string>();
                                
                            usageByType[texConfig.id].Add($"Moby Model {model.id}");
                            mobyTextureCount++;
                        }
                    }
                }
            }
            
            // Check ties
            int tieTextureCount = 0;
            if (level.tieModels != null)
            {
                foreach (var model in level.tieModels)
                {
                    if (model?.textureConfig != null)
                    {
                        foreach (var texConfig in model.textureConfig)
                        {
                            if (!usageByType.ContainsKey(texConfig.id))
                                usageByType[texConfig.id] = new List<string>();
                                
                            usageByType[texConfig.id].Add($"TIE Model {model.id}");
                            tieTextureCount++;
                        }
                    }
                }
            }
            
            // Check shrubs
            int shrubTextureCount = 0;
            if (level.shrubModels != null)
            {
                foreach (var model in level.shrubModels)
                {
                    if (model?.textureConfig != null)
                    {
                        foreach (var texConfig in model.textureConfig)
                        {
                            if (!usageByType.ContainsKey(texConfig.id))
                                usageByType[texConfig.id] = new List<string>();
                                
                            usageByType[texConfig.id].Add($"Shrub Model {model.id}");
                            shrubTextureCount++;
                        }
                    }
                }
            }
            
            // Output statistics
            Console.WriteLine($"Textures used by terrain: {terrainTextureCount} references to {level.terrainEngine?.fragments?.Sum(f => f.model?.textureConfig?.Count ?? 0) ?? 0} unique texture IDs");
            Console.WriteLine($"Textures used by skybox: {skyboxTextureCount}");
            Console.WriteLine($"Textures used by mobys: {mobyTextureCount}");
            Console.WriteLine($"Textures used by ties: {tieTextureCount}");
            Console.WriteLine($"Textures used by shrubs: {shrubTextureCount}");
            
            // Output textures with invalid references
            int invalidCount = 0;
            foreach (var entry in usageByType)
            {
                int texId = entry.Key;
                if (texId < 0 || texId >= level.textures.Count || level.textures[texId] == null || level.textures[texId].data == null || level.textures[texId].data.Length == 0)
                {
                    Console.WriteLine($"⚠️ Invalid texture reference: ID {texId} used by {entry.Value.Count} objects");
                    invalidCount++;
                    
                    // List the first few users
                    foreach (var user in entry.Value.Take(3))
                    {
                        Console.WriteLine($"   - {user}");
                    }
                    if (entry.Value.Count > 3)
                        Console.WriteLine($"   - ... and {entry.Value.Count - 3} more");
                }
            }
            
            Console.WriteLine($"Found {invalidCount} invalid texture references");
            
            // Count unused textures
            int unusedCount = 0;
            for (int i = 0; i < level.textures.Count; i++)
            {
                if (!usageByType.ContainsKey(i))
                    unusedCount++;
            }
            
            Console.WriteLine($"Found {unusedCount} unused textures");
        }
        
        /// <summary>
        /// Gets all texture IDs used by models in the level
        /// </summary>
        private static HashSet<int> GetUsedTextureIds(Level level)
        {
            HashSet<int> usedIds = new HashSet<int>();
            
            // Check terrain fragments
            if (level.terrainEngine?.fragments != null)
            {
                foreach (var fragment in level.terrainEngine.fragments)
                {
                    if (fragment?.model?.textureConfig != null)
                    {
                        foreach (var texConfig in fragment.model.textureConfig)
                        {
                            usedIds.Add(texConfig.id);
                        }
                    }
                }
            }
            
            // Check skybox
            if (level.skybox?.textureConfig != null)
            {
                foreach (var texConfig in level.skybox.textureConfig)
                {
                    usedIds.Add(texConfig.id);
                }
            }
            
            // Check moby models
            if (level.mobyModels != null)
            {
                foreach (var model in level.mobyModels)
                {
                    if (model?.textureConfig != null)
                    {
                        foreach (var texConfig in model.textureConfig)
                        {
                            usedIds.Add(texConfig.id);
                        }
                    }
                    
                    // Also check secondary texture configs if present
                    if (model is MobyModel mobyModel && mobyModel.otherTextureConfigs != null)
                    {
                        foreach (var texConfig in mobyModel.otherTextureConfigs)
                        {
                            usedIds.Add(texConfig.id);
                        }
                    }
                }
            }
            
            // Check tie models
            if (level.tieModels != null)
            {
                foreach (var model in level.tieModels)
                {
                    if (model?.textureConfig != null)
                    {
                        foreach (var texConfig in model.textureConfig)
                        {
                            usedIds.Add(texConfig.id);
                        }
                    }
                }
            }
            
            // Check shrub models
            if (level.shrubModels != null)
            {
                foreach (var model in level.shrubModels)
                {
                    if (model?.textureConfig != null)
                    {
                        foreach (var texConfig in model.textureConfig)
                        {
                            usedIds.Add(texConfig.id);
                        }
                    }
                }
            }
            
            return usedIds;
        }
        
        /// <summary>
        /// Creates a deep copy of a texture to avoid reference issues
        /// </summary>
        private static Texture DeepCloneTexture(Texture sourceTexture)
        {
            if (sourceTexture == null) return null;
            
            // Create a new data array if needed
            byte[] newData = null;
            if (sourceTexture.data != null)
            {
                newData = new byte[sourceTexture.data.Length];
                Array.Copy(sourceTexture.data, newData, sourceTexture.data.Length);
            }
            
            // Create the new texture with the same properties
            Texture newTexture = new Texture(
                sourceTexture.id,
                sourceTexture.width,
                sourceTexture.height,
                newData
            );
            
            // Copy additional properties
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
        /// Helper method to compare two textures for equality
        /// </summary>
        private static bool TextureEquals(Texture tex1, Texture tex2)
        {
            if (tex1 == null || tex2 == null)
                return false;
                
            return tex1.width == tex2.width &&
                   tex1.height == tex2.height &&
                   tex1.vramPointer == tex2.vramPointer &&
                   tex1.data?.Length == tex2.data?.Length;
        }
    }
}
