using LibReplanetizer;
using LibReplanetizer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GeometrySwapper
{
    /// <summary>
    /// Diagnostic tool for analyzing Moby model import issues
    /// </summary>
    public static class MobyDiagnostics
    {
        /// <summary>
        /// Performs a detailed analysis of moby models and textures in source and target levels
        /// </summary>
        /// <param name="targetLevel">The target level to analyze</param>
        /// <param name="sourceLevel">The source level to analyze</param>
        /// <param name="outputPath">Where to save the analysis file</param>
        /// <param name="modelIdsToInclude">Optional list of specific model IDs to focus on</param>
        public static void AnalyzeMobyImport(Level targetLevel, Level sourceLevel, string outputPath, HashSet<int>? modelIdsToInclude = null)
        {
            var directoryPath = Path.GetDirectoryName(outputPath);
            if (directoryPath != null)
            {
                Directory.CreateDirectory(directoryPath);
            }
            else
            {
                throw new ArgumentException("The outputPath does not contain a valid directory path.", nameof(outputPath));
            }
            
            using (var writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("========== MOBY MODEL IMPORT DIAGNOSTIC REPORT ==========");
                writer.WriteLine($"Generated on: {DateTime.Now}");
                writer.WriteLine($"Source level: {Path.GetFileName(sourceLevel.path)}");
                writer.WriteLine($"Target level: {Path.GetFileName(targetLevel.path)}");
                writer.WriteLine("=========================================================\n");
                
                // SOURCE LEVEL ANALYSIS
                writer.WriteLine("SOURCE LEVEL ANALYSIS:");
                writer.WriteLine("----------------------");
                
                if (sourceLevel.mobyModels == null)
                {
                    writer.WriteLine("⚠️ Source level has no moby models!");
                }
                else
                {
                    writer.WriteLine($"Total moby models in source: {sourceLevel.mobyModels.Count}");
                    
                    // Focus on the models we're interested in
                    var relevantModels = modelIdsToInclude != null 
                        ? sourceLevel.mobyModels.Where(m => modelIdsToInclude.Contains(m.id)).ToList()
                        : sourceLevel.mobyModels;
                    
                    writer.WriteLine($"Relevant models count: {relevantModels.Count}");
                    
                    foreach (var model in relevantModels)
                    {
                        writer.WriteLine($"\nModel ID: {model.id}");
                        writer.WriteLine($"  Type: {model.GetType().Name}");
                        writer.WriteLine($"  Vertex count: {model.vertexBuffer?.Length / 8 ?? 0}");
                        
                        if (model.textureConfig != null && model.textureConfig.Count > 0)
                        {
                            writer.WriteLine($"  Texture configs: {model.textureConfig.Count}");
                            foreach (var tc in model.textureConfig)
                            {
                                writer.WriteLine($"    TextureID: {tc.id}, Start: {tc.start}, Size: {tc.size}, Mode: {tc.mode}");
                                
                                // Check if the texture ID is valid
                                bool validTexture = tc.id >= 0 && tc.id < sourceLevel.textures.Count;
                                writer.WriteLine($"    Valid texture reference: {validTexture}");
                                
                                if (validTexture)
                                {
                                    var tex = sourceLevel.textures[tc.id];
                                    writer.WriteLine($"    Texture dimensions: {tex.width}x{tex.height}");
                                }
                            }
                        }
                        else
                        {
                            writer.WriteLine("  ⚠️ No texture configs found for model");
                        }
                    }
                }
                
                // Check the moby instances that reference models we care about
                writer.WriteLine("\nMOBY INSTANCES IN SOURCE:");
                writer.WriteLine("-----------------------");
                
                if (sourceLevel.mobs == null)
                {
                    writer.WriteLine("⚠️ No mobys found in source level!");
                }
                else
                {
                    var relevantMobys = modelIdsToInclude != null
                        ? sourceLevel.mobs.Where(m => modelIdsToInclude.Contains(m.modelID)).ToList()
                        : sourceLevel.mobs;
                    
                    writer.WriteLine($"Total relevant moby instances: {relevantMobys.Count}");
                    
                    foreach (var moby in relevantMobys.Take(10)) // Limit output for readability
                    {
                        writer.WriteLine($"\nMoby ID: {moby.mobyID}, ModelID: {moby.modelID}");
                        writer.WriteLine($"  Position: {moby.position}");
                        writer.WriteLine($"  Model reference exists: {moby.model != null}");
                        
                        if (moby.model != null)
                        {
                            writer.WriteLine($"  Referenced model ID matches: {moby.model.id == moby.modelID}");
                            if (moby.model.id != moby.modelID)
                            {
                                writer.WriteLine($"  ⚠️ Mismatch - Referenced model has ID: {moby.model.id}");
                            }
                        }
                    }
                    
                    if (relevantMobys.Count > 10)
                    {
                        writer.WriteLine($"\n  ... and {relevantMobys.Count - 10} more moby instances");
                    }
                }
                
                // TARGET LEVEL ANALYSIS
                writer.WriteLine("\n\nTARGET LEVEL ANALYSIS:");
                writer.WriteLine("----------------------");
                
                if (targetLevel.mobyModels == null)
                {
                    writer.WriteLine("⚠️ Target level has no moby models!");
                }
                else
                {
                    writer.WriteLine($"Total moby models in target: {targetLevel.mobyModels.Count}");
                    
                    // Look for our relevant models in target
                    if (modelIdsToInclude != null)
                    {
                        var foundModels = targetLevel.mobyModels.Where(m => modelIdsToInclude.Contains(m.id)).ToList();
                        writer.WriteLine($"Found {foundModels.Count} out of {modelIdsToInclude.Count} relevant models in target");
                        
                        foreach (var modelId in modelIdsToInclude)
                        {
                            bool exists = targetLevel.mobyModels.Any(m => m.id == modelId);
                            writer.WriteLine($"  Model ID {modelId}: {(exists ? "✅ Found" : "❌ Not found")}");
                        }
                    }
                }
                
                // TEXTURE ANALYSIS
                writer.WriteLine("\n\nTEXTURE ANALYSIS:");
                writer.WriteLine("----------------");
                writer.WriteLine($"Source textures count: {sourceLevel.textures?.Count ?? 0}");
                writer.WriteLine($"Target textures count: {targetLevel.textures?.Count ?? 0}");
                
                // Check if required textures exist in target level
                if (modelIdsToInclude != null && sourceLevel.mobyModels != null && targetLevel.textures != null)
                {
                    var relevantModels = sourceLevel.mobyModels.Where(m => modelIdsToInclude.Contains(m.id)).ToList();
                    HashSet<int> requiredTextureIds = new HashSet<int>();
                    
                    foreach (var model in relevantModels)
                    {
                        if (model.textureConfig != null)
                        {
                            foreach (var tc in model.textureConfig)
                            {
                                requiredTextureIds.Add(tc.id);
                            }
                        }
                    }
                    
                    writer.WriteLine($"\nRequired textures from source: {requiredTextureIds.Count}");
                    writer.WriteLine("Texture ID ranges:");
                    writer.WriteLine($"  Source: 0-{sourceLevel.textures?.Count - 1 ?? 0}");
                    writer.WriteLine($"  Target: 0-{targetLevel.textures.Count - 1}");
                    
                    // Check if all required texture IDs would be valid in target
                    int invalidTextureCount = requiredTextureIds.Count(id => id >= targetLevel.textures.Count);
                    writer.WriteLine($"Textures that would be out of range in target: {invalidTextureCount}");
                    
                    if (invalidTextureCount > 0)
                    {
                        writer.WriteLine("\nOut of range texture IDs:");
                        foreach (int id in requiredTextureIds.Where(id => id >= targetLevel.textures.Count))
                        {
                            writer.WriteLine($"  Texture ID: {id}");
                        }
                    }
                }
                
                // DIAGNOSTIC RECOMMENDATIONS
                writer.WriteLine("\n\nDIAGNOSTIC RECOMMENDATIONS:");
                writer.WriteLine("---------------------------");
                
                if (targetLevel.mobyModels == null || targetLevel.mobyModels.Count == 0)
                {
                    writer.WriteLine("❌ CRITICAL ISSUE: No moby models found in target level");
                    writer.WriteLine("   - Check if models are being added to targetLevel.mobyModels");
                    writer.WriteLine("   - Verify that the MobySwapper.CopyMobysToLevel() function is working correctly");
                }
                
                bool potentialTextureIssue = false;
                
                if (modelIdsToInclude != null && sourceLevel.mobyModels != null && targetLevel.textures != null)
                {
                    var relevantModels = sourceLevel.mobyModels.Where(m => modelIdsToInclude.Contains(m.id)).ToList();
                    foreach (var model in relevantModels)
                    {
                        if (model.textureConfig != null)
                        {
                            foreach (var tc in model.textureConfig)
                            {
                                if (tc.id >= targetLevel.textures.Count)
                                {
                                    potentialTextureIssue = true;
                                    break;
                                }
                            }
                        }
                        if (potentialTextureIssue) break;
                    }
                }
                
                if (potentialTextureIssue)
                {
                    writer.WriteLine("❌ CRITICAL ISSUE: Some texture IDs in source models exceed bounds of target texture list");
                    writer.WriteLine("   - Texture IDs need to be remapped when copying models to avoid out-of-bounds issues");
                    writer.WriteLine("   - Required textures should be copied from source to target level");
                }
                
                // Deep copy check
                writer.WriteLine("\nDeep copy verification:");
                if (sourceLevel.mobyModels != null && targetLevel.mobyModels != null)
                {
                    bool sharingReferences = false;
                    
                    foreach (var sourceModel in sourceLevel.mobyModels)
                    {
                        foreach (var targetModel in targetLevel.mobyModels)
                        {
                            if (Object.ReferenceEquals(sourceModel, targetModel))
                            {
                                sharingReferences = true;
                                writer.WriteLine($"⚠️ Model ID {sourceModel.id} is the SAME OBJECT in both levels (reference is shared)");
                            }
                        }
                    }
                    
                    if (sharingReferences)
                    {
                        writer.WriteLine("❌ CRITICAL ISSUE: Some models are shared by reference, not proper deep copies");
                        writer.WriteLine("   - Models must be deep-copied to allow proper texture remapping");
                    }
                    else
                    {
                        writer.WriteLine("✅ No shared references detected between source and target models");
                    }
                }
            }
            
            Console.WriteLine($"✅ Moby import diagnostic report generated at {outputPath}");
        }
        
        /// <summary>
        /// Creates a brief report of moby model and texture relationship in a level
        /// </summary>
        public static void GenerateMobyTextureReport(Level level, string outputPath)
        {
            using (var writer = new StreamWriter(outputPath))
            {
                writer.WriteLine($"========== MOBY-TEXTURE RELATIONSHIP REPORT ==========");
                writer.WriteLine($"Level: {Path.GetFileName(level.path)}");
                writer.WriteLine($"Generated on: {DateTime.Now}");
                writer.WriteLine("====================================================\n");
                
                if (level.mobyModels == null || level.mobyModels.Count == 0)
                {
                    writer.WriteLine("❌ No moby models found in level!");
                    return;
                }
                
                writer.WriteLine($"Total moby models: {level.mobyModels.Count}");
                writer.WriteLine($"Total textures: {level.textures?.Count ?? 0}");
                
                // Build texture usage map
                Dictionary<int, List<int>> textureToModelMap = new Dictionary<int, List<int>>();
                Dictionary<int, List<int>> modelToTextureMap = new Dictionary<int, List<int>>();
                
                foreach (var model in level.mobyModels)
                {
                    if (model.textureConfig != null)
                    {
                        List<int> texturesForModel = new List<int>();
                        
                        foreach (var tc in model.textureConfig)
                        {
                            if (!textureToModelMap.ContainsKey(tc.id))
                            {
                                textureToModelMap[tc.id] = new List<int>();
                            }
                            
                            // Add this model ID to the texture's usage list
                            if (!textureToModelMap[tc.id].Contains(model.id))
                            {
                                textureToModelMap[tc.id].Add(model.id);
                            }
                            
                            // Track textures used by this model
                            if (!texturesForModel.Contains(tc.id))
                            {
                                texturesForModel.Add(tc.id);
                            }
                        }
                        
                        modelToTextureMap[model.id] = texturesForModel;
                    }
                    else
                    {
                        modelToTextureMap[model.id] = new List<int>();
                    }
                }
                
                // Report on models and textures
                writer.WriteLine("\nMOBY MODELS ANALYSIS:");
                writer.WriteLine("-------------------");
                
                foreach (var model in level.mobyModels)
                {
                    writer.WriteLine($"\nModel ID: {model.id}");
                    writer.WriteLine($"  Type: {model.GetType().Name}");
                    writer.WriteLine($"  Vertex count: {model.vertexBuffer?.Length / 8 ?? 0}");
                    writer.WriteLine($"  Face count: {model.indexBuffer?.Length / 3 ?? 0}");
                    
                    if (modelToTextureMap.TryGetValue(model.id, out var textures) && textures.Count > 0)
                    {
                        writer.WriteLine($"  Textures used: {string.Join(", ", textures)}");
                        
                        // Check for out-of-bounds texture references
                        foreach (var texId in textures)
                        {
                            if (texId < 0 || (level.textures != null && texId >= level.textures.Count))
                            {
                                writer.WriteLine($"  ❌ INVALID TEXTURE REFERENCE: {texId}");
                            }
                        }
                    }
                    else
                    {
                        writer.WriteLine("  ⚠️ No textures used by this model");
                    }
                    
                    // Check for moby instances using this model
                    int instanceCount = level.mobs?.Count(m => m.modelID == model.id) ?? 0;
                    writer.WriteLine($"  Instances in level: {instanceCount}");
                }
                
                writer.WriteLine("\nTEXTURE USAGE ANALYSIS:");
                writer.WriteLine("---------------------");
                
                // Find unused textures and textures used by many models
                var unusedTextures = new List<int>();
                var highUsageTextures = new List<Tuple<int, int>>();
                
                if (level.textures != null)
                {
                    for (int i = 0; i < level.textures.Count; i++)
                    {
                        if (!textureToModelMap.ContainsKey(i))
                        {
                            unusedTextures.Add(i);
                        }
                        else if (textureToModelMap[i].Count > 3) // 3+ models use this texture
                        {
                            highUsageTextures.Add(new Tuple<int, int>(i, textureToModelMap[i].Count));
                        }
                    }
                }
                
                writer.WriteLine($"Unused textures: {unusedTextures.Count}");
                writer.WriteLine($"Highly used textures (4+ models): {highUsageTextures.Count}");
                
                // Show a sample of highly used textures
                if (highUsageTextures.Count > 0)
                {
                    writer.WriteLine("\nHighly used textures:");
                    foreach (var (texId, useCount) in highUsageTextures.OrderByDescending(t => t.Item2).Take(10))
                    {
                        writer.WriteLine($"  Texture {texId}: Used by {useCount} models");
                    }
                }
            }
            
            Console.WriteLine($"✅ Moby-texture relationship report generated at {outputPath}");
        }
    }
}
