using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibReplanetizer;
using LibReplanetizer.Headers;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using OpenTK.Mathematics;
using static LibReplanetizer.DataFunctions;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles the transfer of TIE models and instances from RC1 levels to RC2 levels
    /// </summary>
    public class TieSwapper
    {
        /// <summary>
        /// Options for controlling TIE swapping behavior
        /// </summary>
        [Flags]
        public enum TieSwapOptions
        {
            None = 0,
            UseRC1Placements = 1,
            UseRC1Models = 2,
            MapTextures = 4,
            CopyOcclusionData = 8,

            PlacementsOnly = UseRC1Placements,
            PlacementsAndModels = UseRC1Placements | UseRC1Models,
            FullSwap = UseRC1Placements | UseRC1Models | MapTextures | CopyOcclusionData,
            Default = PlacementsAndModels
        }

        /// <summary>
        /// Swaps TIE objects from an RC1 level to an RC2 level
        /// </summary>
        /// <param name="targetLevel">The RC2 level where TIEs will be replaced</param>
        /// <param name="rc1SourceLevel">The RC1 level containing the source TIEs</param>
        /// <param name="options">Options to control the swap behavior</param>
        /// <returns>True if the operation was successful</returns>
        public static bool SwapTiesWithRC1Oltanis(Level targetLevel, Level rc1SourceLevel, TieSwapOptions options = TieSwapOptions.Default)
        {
            if (targetLevel == null || rc1SourceLevel == null)
            {
                Console.WriteLine("  ❌ Error: One of the levels is null");
                return false;
            }

            try
            {
                // Step 1: Remove all existing TIEs from the target level
                RemoveAllTies(targetLevel);

                // Step 2: Import TIE models from RC1 if needed
                Dictionary<int, int> tieModelIdMapping = new Dictionary<int, int>();
                if (options.HasFlag(TieSwapOptions.UseRC1Models))
                {
                    tieModelIdMapping = ImportRC1TieModelsToRC2Level(targetLevel, rc1SourceLevel);
                }

                // Step 3: Map textures FIRST (before transferring instances)
                if (options.HasFlag(TieSwapOptions.MapTextures))
                {
                    MapTieTextures(targetLevel, rc1SourceLevel);
                }

                // Step 4: Import TIEs from RC1 to RC2 (now that textures are available)
                TransferTieInstances(targetLevel, rc1SourceLevel, tieModelIdMapping);

                // Step 5: Validate texture references after transfer
                if (options.HasFlag(TieSwapOptions.MapTextures))
                {
                    ValidateTextureReferences(targetLevel);
                }

                // Step 6: Resolve any ID conflicts with Moby models
                ResolveMobyTieIdConflicts(targetLevel);

                // Step 7: Clean up by removing unused TIE models
                RemoveUnusedTieModels(targetLevel);

                // Step 8: Validate all texture references in TIE models
                ValidateTextureReferences(targetLevel);

                // Step 9: Validate and fix TIE model references
                ValidateTieModelReferences(targetLevel);

                // Step 10: CRITICAL - Validate and fix TIE data alignment
                ValidateTieDataAlignment(targetLevel);

                // Step 11: Update the level's tie IDs collection to match models (not instances)
                if (targetLevel.tieModels != null)
                {
                    targetLevel.tieIds = targetLevel.tieModels.Select(t => (int)t.id).ToList();
                    Console.WriteLine($"  ✅ Updated tieIds list to contain {targetLevel.tieIds.Count} model IDs (one per model)");
                }

                // Step 12: Create proper TIE serialization data with correct alignment
                CreateTieSerializationData(targetLevel);

                // Step 13: Create or copy TIE occlusion data as requested
                if (options.HasFlag(TieSwapOptions.CopyOcclusionData))
                    TieCullingDataFixer.DirectCopyTieOcclusion(targetLevel, rc1SourceLevel);
                else
                    CreateTieOcclusionData(targetLevel);

                // Step 14: Optimize group data for rendering performance  
                OptimizeTieGroupData(targetLevel);

                Console.WriteLine("\n==== TIE Swap Summary ====");
                if (targetLevel.ties != null)
                {
                    Console.WriteLine($"✅ Successfully added {targetLevel.ties.Count} TIEs from RC1 Oltanis to RC2 level");
                }
                else
                {
                    Console.WriteLine("⚠️ No TIEs were added to the target level");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Error during TIE swap: {ex.Message}");
                Console.WriteLine($"  Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Removes all TIEs from the target level
        /// </summary>
        /// <param name="targetLevel">The level to clear TIEs from</param>
        private static void RemoveAllTies(Level targetLevel)
        {
            if (targetLevel.ties == null)
            {
                targetLevel.ties = new List<Tie>();
                Console.WriteLine("  🔄 Created new empty TIE list");
            }
            else
            {
                int tieCount = targetLevel.ties.Count;
                targetLevel.ties.Clear();
                Console.WriteLine($"  🧹 Removed {tieCount} existing TIEs from target level");
            }

            // Clear the tie group data as well
            targetLevel.tieGroupData = new byte[0];

            // Update tie IDs list to be empty
            targetLevel.tieIds = new List<int>();
        }

        /// <summary>
        /// Maps textures from RC1 to RC2 for TIE models using an append-based approach
        /// This is safer and more reliable than the current find-or-replace method
        /// </summary>
        /// <param name="targetLevel">The RC2 target level</param>
        /// <param name="rc1SourceLevel">The RC1 source level</param>
        private static void MapTieTextures(Level targetLevel, Level rc1SourceLevel)
        {
            Console.WriteLine("  🔄 Mapping textures for RC1 TIE models using append-based approach...");

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

            // Find all texture IDs used by TIE models in RC1
            HashSet<int> usedTextureIdsInRC1 = new HashSet<int>();
            foreach (var model in rc1SourceLevel.tieModels?.OfType<TieModel>() ?? Enumerable.Empty<TieModel>())
            {
                if (model.textureConfig != null)
                {
                    foreach (var texConfig in model.textureConfig)
                    {
                        usedTextureIdsInRC1.Add(texConfig.id);
                    }
                }
            }

            Console.WriteLine($"  Found {usedTextureIdsInRC1.Count} unique texture IDs used by RC1 TIE models");

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
                int existingTextureId = FindExistingTexture(targetLevel, rc1Texture);
                
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

            // Now update texture references in TIE models that we imported
            if (targetLevel.tieModels != null)
            {
                int modelsUpdated = 0;
                int textureReferencesUpdated = 0;
                
                foreach (var model in targetLevel.tieModels)
                {
                    if (model.textureConfig == null || model.textureConfig.Count == 0)
                        continue;

                    bool modelUpdated = false;
                    foreach (var texConfig in model.textureConfig)
                    {
                        // Only update texture references that have mappings
                        // (i.e., they came from RC1 TIE models)
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

                Console.WriteLine($"  ✅ Updated texture references in {modelsUpdated} TIE models ({textureReferencesUpdated} total references)");
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
        private static int FindExistingTexture(Level targetLevel, Texture sourceTexture)
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
        /// Creates a deep copy of a texture with all properties preserved
        /// </summary>
        /// <param name="sourceTexture">The source texture to clone</param>
        /// <returns>A new completely independent texture instance</returns>
        private static Texture DeepCloneTexture(Texture sourceTexture)
        {
            if (sourceTexture == null)
                throw new ArgumentNullException(nameof(sourceTexture), "Source texture cannot be null.");

            // Create a new texture with the same properties
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

            // Copy remaining properties
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
        /// Imports RC1 tie models to RC2 level, ensuring proper model compatibility with unique IDs
        /// </summary>
        /// <param name="targetLevel">The RC2 level where models will be imported</param>
        /// <param name="rc1SourceLevel">The RC1 level containing source models</param>
        /// <returns>Mapping from RC1 model IDs to newly assigned RC2 model IDs</returns>
        public static Dictionary<int, int> ImportRC1TieModelsToRC2Level(Level targetLevel, Level rc1SourceLevel)
        {
            Console.WriteLine($"🔄 Importing RC1 tie models to RC2 level...");

            var modelIdMapping = new Dictionary<int, int>();

            if (rc1SourceLevel.tieModels == null || rc1SourceLevel.tieModels.Count == 0)
            {
                Console.WriteLine("  ⚠️ No tie models found in RC1 source level");
                return modelIdMapping;
            }

            // Ensure the target has a tie model list
            if (targetLevel.tieModels == null)
            {
                targetLevel.tieModels = new List<Model>();
            }

            // Find existing models in target level to avoid ID conflicts
            var existingModelIds = new HashSet<int>();
            foreach (var existingModel in targetLevel.tieModels)
            {
                if (existingModel != null)
                {
                    existingModelIds.Add(existingModel.id);
                }
            }

            // Create a deep copy of each RC1 model to avoid reference issues
            foreach (var origModel in rc1SourceLevel.tieModels?.Where(m => m != null) ?? Enumerable.Empty<Model>())
            {
                // Skip if not actually a TieModel
                if (!(origModel is TieModel rc1Model))
                {
                    Console.WriteLine($"  ⚠️ Model {origModel.id} is not a TieModel, skipping");
                    continue;
                }

                // Store the original RC1 model ID
                int originalId = rc1Model.id;

                // Try to preserve original ID if possible
                short newModelId = (short)originalId;

                // If ID conflicts with existing model, find next available ID
                if (existingModelIds.Contains(newModelId))
                {
                    // Find a clear ID range
                    short nextModelId = 1000;
                    while (existingModelIds.Contains(nextModelId))
                    {
                        nextModelId++;
                    }
                    newModelId = nextModelId;
                    Console.WriteLine($"  ⚠️ Model ID {originalId} conflicts with existing model, using {newModelId} instead");
                }

                // Create a clone of the model
                try
                {
                    TieModel newModel = CloneTieModel(rc1Model);

                    // Assign the determined ID
                    newModel.id = newModelId;
                    existingModelIds.Add(newModelId); // Mark as used

                    // Add the model with its ID to the RC2 level
                    targetLevel.tieModels.Add(newModel);

                    // Record the mapping from original RC1 ID to assigned RC2 ID
                    modelIdMapping[originalId] = newModelId;

                    string idStatus = (newModelId == originalId) ? "preserved" : "remapped to";
                    Console.WriteLine($"  Added RC1 tie model {originalId} ({idStatus} {newModelId})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error copying RC1 tie model {originalId}: {ex.Message}");
                }
            }

            // Identify any RC1 tie model IDs that weren't imported
            var usedModelIds = new HashSet<int>();
            if (rc1SourceLevel.ties != null)
            {
                foreach (var tie in rc1SourceLevel.ties)
                {
                    usedModelIds.Add(tie.modelID);
                }
            }

            // Find model IDs that are referenced by ties but weren't imported
            var missingModelIds = usedModelIds.Where(id => !modelIdMapping.ContainsKey(id)).ToList();
            if (missingModelIds.Count > 0)
            {
                Console.WriteLine($"  ⚠️ Found {missingModelIds.Count} model IDs referenced by RC1 ties but not imported:");
                foreach (var id in missingModelIds.Take(10))
                {
                    Console.WriteLine($"     - Model ID {id}");
                }
                if (missingModelIds.Count > 10)
                {
                    Console.WriteLine($"     - ... and {missingModelIds.Count - 10} more");
                }
            }

            // Update the tieIds to contain ALL model IDs, whether they're used or not
            if (targetLevel.tieModels != null)
            {
                targetLevel.tieIds = targetLevel.tieModels.Select(m => (int)m.id).ToList();
                Console.WriteLine($"  ✅ Updated tieIds header list with {targetLevel.tieIds.Count} model IDs");
            }

            // Count how many IDs were preserved
            int preservedIds = modelIdMapping.Count(kvp => kvp.Key == kvp.Value);
            Console.WriteLine($"  ✅ Imported {modelIdMapping.Count} RC1 tie models ({preservedIds} with original IDs preserved)");

            return modelIdMapping;
        }

        /// <summary>
        /// Creates a deep copy of a TieModel with new ID assignment and proper RC2 vertex layout
        /// </summary>
        /// <param name="sourceModel">The source model to clone</param>
        /// <returns>A new TieModel instance with RC2-compatible vertex layout</returns>
        public static TieModel CloneTieModel(TieModel sourceModel)
        {
            // Create a minimal byte array for the TieModel constructor
            byte[] dummyBlock = new byte[0x40];

            // Copy basic properties from the source model
            WriteFloat(dummyBlock, 0x00, sourceModel.cullingX);
            WriteFloat(dummyBlock, 0x04, sourceModel.cullingY);
            WriteFloat(dummyBlock, 0x08, sourceModel.cullingZ);
            WriteFloat(dummyBlock, 0x0C, sourceModel.cullingRadius);
            WriteUint(dummyBlock, 0x20, sourceModel.off20);
            WriteShort(dummyBlock, 0x2A, sourceModel.wiggleMode);
            WriteFloat(dummyBlock, 0x2C, sourceModel.off2C);
            WriteShort(dummyBlock, 0x30, sourceModel.id);
            WriteUint(dummyBlock, 0x34, sourceModel.off34);
            WriteUint(dummyBlock, 0x38, sourceModel.off38);
            WriteUint(dummyBlock, 0x3C, sourceModel.off3C);

            // Create a temporary file for the required FileStream
            string tempFilePath = Path.GetTempFileName();
            try
            {
                // Write the dummy block to the temp file
                File.WriteAllBytes(tempFilePath, dummyBlock);

                // Create a FileStream which is required by the constructor
                using (FileStream fs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
                {
                    // Create a new TieModel using the constructor that requires FileStream
                    TieModel newModel = new TieModel(fs, dummyBlock, 0);

                    // Set the size property
                    newModel.size = sourceModel.size;

                    // ALWAYS convert vertex buffer to RC2 format regardless of source stride
                    // This ensures we have consistent 8-float stride in all models
                    float[] sourceVertexBuffer = sourceModel.vertexBuffer;
                    int sourceStride = sourceModel.vertexStride;
                    float[] newVertexBuffer = ConvertVertexBufferToRC2Format(sourceVertexBuffer, sourceStride);
                    newModel.vertexBuffer = newVertexBuffer;

                    // Deep copy index buffer
                    if (sourceModel.indexBuffer != null)
                    {
                        newModel.indexBuffer = new ushort[sourceModel.indexBuffer.Length];
                        Array.Copy(sourceModel.indexBuffer, newModel.indexBuffer, sourceModel.indexBuffer.Length);
                    }

                    // Deep copy any RGBA data
                    if (sourceModel.rgbas != null)
                    {
                        newModel.rgbas = new byte[sourceModel.rgbas.Length];
                        Array.Copy(sourceModel.rgbas, newModel.rgbas, sourceModel.rgbas.Length);
                    }

                    // Deep copy weights and IDs arrays if present
                    if (sourceModel.weights != null)
                    {
                        newModel.weights = new uint[sourceModel.weights.Length];
                        Array.Copy(sourceModel.weights, newModel.weights, sourceModel.weights.Length);
                    }

                    if (sourceModel.ids != null)
                    {
                        newModel.ids = new uint[sourceModel.ids.Length];
                        Array.Copy(sourceModel.ids, newModel.ids, sourceModel.ids.Length);
                    }

                    // Deep copy texture configurations and validate texture IDs
                    if (sourceModel.textureConfig != null)
                    {
                        newModel.textureConfig = new List<TextureConfig>();
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
                    }

                    // Set the vertex stride via reflection if still needed
                    var vertexStrideProperty = typeof(Model).GetProperty("vertexStride",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

                    if (vertexStrideProperty != null)
                    {
                        vertexStrideProperty.SetValue(newModel, 8); // Always set to RC2 standard
                    }

                    newModel.ValidateCullingData();

                    return newModel;
                }
            }
            finally
            {
                // Clean up the temporary file
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); }
                    catch { /* Ignore delete errors */ }
                }
            }
        }

        /// <summary>
        /// Converts a vertex buffer from RC1 format to RC2-compatible format
        /// </summary>
        /// <param name="sourceBuffer">Source vertex buffer (RC1 format)</param>
        /// <param name="sourceStride">Stride of the source buffer</param>
        /// <returns>New vertex buffer with RC2 layout</returns>
        private static float[] ConvertVertexBufferToRC2Format(float[] sourceBuffer, int sourceStride)
        {
            // RC1 often uses different formats, but we need to convert to RC2 standard:
            // RC2 TIEs expect: position(3) | normal(3) | uv(2)
            // with UVs at offset 0x18 (indices 6,7) in each vertex

            int vertexCount = sourceBuffer.Length / sourceStride;
            float[] destBuffer = new float[vertexCount * 8]; // RC2 standard stride is 8 floats

            Console.WriteLine($"  🔄 Converting vertex buffer from stride {sourceStride} to RC2 standard format (stride 8)");

            for (int i = 0; i < vertexCount; i++)
            {
                int srcOffset = i * sourceStride;
                int destOffset = i * 8;

                // Position (always first 3 floats)
                destBuffer[destOffset + 0] = sourceBuffer[srcOffset + 0]; // X
                destBuffer[destOffset + 1] = sourceBuffer[srcOffset + 1]; // Y  
                destBuffer[destOffset + 2] = sourceBuffer[srcOffset + 2]; // Z

                // Normal (next 3 floats in both formats)
                if (sourceStride >= 6)
                {
                    destBuffer[destOffset + 3] = sourceBuffer[srcOffset + 3]; // Normal X
                    destBuffer[destOffset + 4] = sourceBuffer[srcOffset + 4]; // Normal Y
                    destBuffer[destOffset + 5] = sourceBuffer[srcOffset + 5]; // Normal Z
                }
                else
                {
                    // Default normal if not available
                    destBuffer[destOffset + 3] = 0;
                    destBuffer[destOffset + 4] = 1;
                    destBuffer[destOffset + 5] = 0;
                }

                // UV coordinates (may be at different locations or missing)
                if (sourceStride >= 8)
                {
                    // Standard RC2 layout, copy UVs from expected positions
                    destBuffer[destOffset + 6] = sourceBuffer[srcOffset + 6]; // U
                    destBuffer[destOffset + 7] = sourceBuffer[srcOffset + 7]; // V
                }
                else if (sourceStride == 7)
                {
                    // Some RC1 models have UVs at the end with stride 7
                    destBuffer[destOffset + 6] = sourceBuffer[srcOffset + 5]; // U
                    destBuffer[destOffset + 7] = sourceBuffer[srcOffset + 6]; // V
                }
                else
                {
                    // If no UVs found, set to defaults (center of texture)
                    destBuffer[destOffset + 6] = 0.5f;
                    destBuffer[destOffset + 7] = 0.5f;
                }
            }

            return destBuffer;
        }

        /// <summary>
        /// Transfers TIE instances from RC1 level to RC2 level, using the provided model ID mappings
        /// ENHANCED: Now preserves TIE IDs, lighting, and rotation data from source
        /// </summary>
        /// <param name="targetLevel">The RC2 level where TIE instances will be created</param>
        /// <param name="rc1SourceLevel">The RC1 level containing source TIEs</param>
        /// <param name="modelIdMapping">Mapping from RC1 model IDs to RC2 model IDs</param>
        /// <returns>True if transfer was successful</returns>
        public static bool TransferTieInstances(Level targetLevel, Level rc1SourceLevel, Dictionary<int, int> modelIdMapping)
        {
            Console.WriteLine($"🔄 Transferring TIE instances from RC1 to RC2 level...");

            if (rc1SourceLevel.ties == null || rc1SourceLevel.ties.Count == 0)
            {
                Console.WriteLine("  ⚠️ No TIE instances found in RC1 source level");
                return false;
            }

            // Get a reference tie for cloning - if we don't have one, can't continue
            if (targetLevel.ties == null || targetLevel.ties.Count == 0)
            {
                Console.WriteLine("  ⚠️ No reference TIE available in target level. Creating a minimal template.");
                targetLevel.ties = new List<Tie>();

                // Create a minimal template from scratch if needed
                Tie minimalTemplateTie = CreateMinimalTemplateTie();
                targetLevel.ties.Add(minimalTemplateTie);
            }

            Tie templateTie = targetLevel.ties[0];

            // Create a completely new list of ties
            List<Tie> newTies = new List<Tie>();

            // Keep track of statistics for reporting
            int validTiesAdded = 0;
            int invalidTiesSkipped = 0;
            int outOfBoundsSkipped = 0;
            int badScaleSkipped = 0;
            int noModelSkipped = 0;
            int invalidTextureSkipped = 0;
            int preservedTieIds = 0;
            int preservedLighting = 0;
            int preservedRotations = 0;

            // Process each tie from RC1 source
            for (int i = 0; i < rc1SourceLevel.ties.Count; i++)
            {
                var sourceTie = rc1SourceLevel.ties[i];

                try
                {
                    // Skip ties that are likely garbage/unused
                    if (IsLikelyGarbageTie(sourceTie))
                    {
                        invalidTiesSkipped++;
                        if (invalidTiesSkipped <= 5) // Limit logging
                            Console.WriteLine($"  Skipping invalid tie at {sourceTie.position}");
                        continue;
                    }

                    // Get the RC1 model ID and look up the remapped RC2 ID
                    int rc1ModelId = sourceTie.modelID;

                    // Skip ties with non-mapped model IDs
                    if (!modelIdMapping.TryGetValue(rc1ModelId, out int rc2ModelId))
                    {
                        // NEW APPROACH: Try to import the missing model on-demand
                        Console.WriteLine($"  🔄 Model ID {rc1ModelId} not in mapping, attempting to import on-demand...");

                        // Find the source model
                        var sourceModel = rc1SourceLevel.tieModels?.FirstOrDefault(m => m.id == rc1ModelId);
                        if (sourceModel is TieModel sourceTieModel)
                        {
                            try
                            {
                                // Clone and import the missing model
                                TieModel newModel = CloneTieModel(sourceTieModel);

                                // Find a safe ID for the new model
                                var existingIds = new HashSet<int>(targetLevel.tieModels.Select(m => (int) m.id));
                                int newId = rc1ModelId;
                                while (existingIds.Contains(newId))
                                {
                                    newId++;
                                }
                                newModel.id = (short) newId;

                                // Add to target level
                                targetLevel.tieModels.Add(newModel);
                                modelIdMapping[rc1ModelId] = newId;
                                rc2ModelId = newId;

                                Console.WriteLine($"  ✅ Successfully imported missing model {rc1ModelId} as {newId}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"  ❌ Failed to import missing model {rc1ModelId}: {ex.Message}");
                                noModelSkipped++;
                                continue;
                            }
                        }
                        else
                        {
                            noModelSkipped++;
                            if (noModelSkipped <= 10) // Limit logging
                                Console.WriteLine($"  ⚠️ No mapping found for RC1 model ID {rc1ModelId}. Skipping tie at position {sourceTie.position}");
                            continue;
                        }
                    }

                    // Find the actual model using the new remapped ID
                    Model? targetModel = targetLevel.tieModels.FirstOrDefault(m => m.id == rc2ModelId);
                    if (targetModel == null)
                    {
                        invalidTiesSkipped++;
                        Console.WriteLine($"  ⚠️ Model ID {rc2ModelId} exists in mapping but not in target models. Skipping tie.");
                        continue;
                    }

                    // Check texture IDs in the target model to ensure they're valid
                    bool hasInvalidTextures = false;
                    if (targetModel.textureConfig != null && targetLevel.textures != null)
                    {
                        foreach (var texConfig in targetModel.textureConfig)
                        {
                            if (texConfig.id < 0 || texConfig.id >= targetLevel.textures.Count)
                            {
                                hasInvalidTextures = true;
                                Console.WriteLine($"  ⚠️ TIE model {targetModel.id} has invalid texture ID {texConfig.id} (max valid: {targetLevel.textures.Count - 1})");
                                break;
                            }
                        }
                    }

                    if (hasInvalidTextures)
                    {
                        invalidTextureSkipped++;
                        continue;
                    }

                    // Create a new tie by cloning the template
                    Tie newTie = new Tie(templateTie);

                    // **CRITICAL FIX**: Set light value IMMEDIATELY after cloning, before any other operations
                    // This prevents the template's light=0 value from being used
                    newTie.light = sourceTie.light;
                    preservedLighting++;

                    // **PERFORMANCE FIX 1**: Preserve original position, rotation, scale, and reflection
                    newTie.modelMatrix = sourceTie.modelMatrix;
                    newTie.position = sourceTie.position;
                    newTie.rotation = sourceTie.rotation;  // ← FIXED: Don't ignore source rotation
                    newTie.scale = sourceTie.scale;
                    newTie.reflection = sourceTie.reflection; // ← FIXED: Preserve reflection matrix
                    preservedRotations++;

                    // Set the model to the remapped RC2 model ID
                    newTie.modelID = rc2ModelId;
                    newTie.model = targetModel;

                    // **PERFORMANCE FIX 2**: Preserve original TIE ID (off58) without enforcing uniqueness
                    newTie.off58 = sourceTie.off58;
                    if (sourceTie.off58 != 0)
                    {
                        preservedTieIds++;
                    }

                    // Preserve other RC1 properties that make sense in RC2
                    // Culling distance differs between games:
                    switch (targetLevel.game.num)
                    {
                        case 1:
                            newTie.off54 = 720; // RC1: default value sourced from RC1 levels
                            break;
                        case 2:
                        case 3:
                            newTie.off54 = 4000; // RC2/RC3: default value sourced from RC2/RC3 levels
                            break;
                        default:
                            newTie.off54 = 4000; // Fallback to RC2/RC3 value
                            break;
                    }
                    newTie.off5C = sourceTie.off5C; // ← FIXED: Preserve this value
                    newTie.off64 = sourceTie.off64; // ← FIXED: Preserve this value
                    newTie.off6C = sourceTie.off6C; // ← FIXED: Preserve this value

                    // Create fresh color bytes for each tie based on the model
                    if (targetModel.vertexBuffer != null && targetModel.vertexBuffer.Length > 0)
                    {
                        int vertexCount = targetModel.vertexBuffer.Length / 8;
                        newTie.colorBytes = new byte[vertexCount * 4];

                        // **PERFORMANCE FIX 4**: Try to preserve original color data
                        if (sourceTie.colorBytes != null &&
                            sourceTie.model?.vertexBuffer != null &&
                            sourceTie.model.vertexBuffer.Length / sourceTie.model.vertexStride == vertexCount &&
                            sourceTie.colorBytes.Length == vertexCount * 4)
                        {
                            // Perfect match - copy original colors
                            Buffer.BlockCopy(sourceTie.colorBytes, 0, newTie.colorBytes, 0, newTie.colorBytes.Length);
                        }
                        else
                        {
                            // Fallback to white color (255 for all channels)
                            for (int j = 0; j < newTie.colorBytes.Length; j++)
                            {
                                newTie.colorBytes[j] = 0xFF;
                            }
                        }
                    }
                    else
                    {
                        newTie.colorBytes = new byte[0];
                    }

                    // Add to our list
                    newTies.Add(newTie);
                    validTiesAdded++;

                    // Log progress periodically
                    if (validTiesAdded % 100 == 0)
                    {
                        Console.WriteLine($"  Added {validTiesAdded} ties...");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error creating RC2 tie with RC1 model: {ex.Message}");
                    invalidTiesSkipped++;
                }
            }

            // Replace the target level's ties completely
            targetLevel.ties = newTies;

            // Update the tieIds list for header serialization - CRITICALLY IMPORTANT
            // This should contain ALL model IDs used by TIEs, not instance IDs
            targetLevel.tieIds = targetLevel.tieModels.Select(m => (int) m.id).ToList();

            // Create the serialization data for ties
            CreateTieSerializationData(targetLevel);

            // Perform a final validation to ensure all models are properly referenced
            ValidateTieModelReferences(targetLevel);

            // **ENHANCED**: Detailed statistics about preservation vs. generation
            Console.WriteLine($"✅ Successfully created {validTiesAdded} RC2-compatible ties");
            Console.WriteLine($"  📊 TIE ID preservation: {preservedTieIds}/{validTiesAdded} ({(preservedTieIds * 100.0 / Math.Max(validTiesAdded, 1)):F1}%)");
            Console.WriteLine($"  💡 Lighting preservation: {preservedLighting}/{validTiesAdded} ({(preservedLighting * 100.0 / Math.Max(validTiesAdded, 1)):F1}%)");
            Console.WriteLine($"  🔄 Rotation preservation: {preservedRotations}/{validTiesAdded} ({(preservedRotations * 100.0 / Math.Max(validTiesAdded, 1)):F1}%)");
            Console.WriteLine($"  Skipped {invalidTiesSkipped} invalid ties");
            Console.WriteLine($"  - {outOfBoundsSkipped} out-of-bounds positions");
            Console.WriteLine($"  - {badScaleSkipped} with unreasonable scale");
            Console.WriteLine($"  - {noModelSkipped} with missing model mappings");
            Console.WriteLine($"  - {invalidTextureSkipped} with invalid texture references");

            return true;
        }

        /// <summary>
        /// Debug method to analyze why TIEs are being skipped
        /// </summary>
        private static void AnalyzeTieFiltering(Level rc1SourceLevel, Dictionary<int, int> modelIdMapping)
        {
            Console.WriteLine("\n🔍 Analyzing TIE filtering reasons...");
            
            if (rc1SourceLevel.ties == null) return;
            
            int total = rc1SourceLevel.ties.Count;
            int garbageFiltered = 0;
            int missingModels = 0;
            int invalidTextures = 0;
            int validTies = 0;
            
            foreach (var tie in rc1SourceLevel.ties)
            {
                if (IsLikelyGarbageTie(tie))
                {
                    garbageFiltered++;
                    continue;
                }
                
                if (!modelIdMapping.ContainsKey(tie.modelID))
                {
                    missingModels++;
                    continue;
                }
                
                // Check if this would pass texture validation
                // (simplified check)
                validTies++;
            }
            
            Console.WriteLine($"  Total RC1 TIEs: {total}");
            Console.WriteLine($"  Valid TIEs: {validTies}");
            Console.WriteLine($"  Filtered as garbage: {garbageFiltered}");
            Console.WriteLine($"  Missing model mappings: {missingModels}");
            Console.WriteLine($"  Estimated loss: {total - validTies} TIEs ({((total - validTies) * 100.0 / total):F1}%)");
        }

        /// <summary>
        /// Validates texture references in all TIE models to ensure they're within valid range
        /// </summary>
        /// <param name="level">The level to validate</param>
        private static void ValidateTextureReferences(Level level)
        {
            if (level.tieModels == null || level.tieModels.Count == 0 || level.textures == null)
                return;

            Console.WriteLine("\n🔍 Validating texture references in TIE models...");

            int modelsFixed = 0;
            int texturesFixed = 0;

            // Maximum valid texture index
            int maxValidTextureId = level.textures.Count - 1;

            foreach (var model in level.tieModels.OfType<TieModel>())
            {
                if (model.textureConfig == null || model.textureConfig.Count == 0)
                    continue;

                bool modelFixed = false;

                for (int i = 0; i < model.textureConfig.Count; i++)
                {
                    var texConfig = model.textureConfig[i];

                    // Check if texture ID is out of range
                    if (texConfig.id < 0 || texConfig.id > maxValidTextureId)
                    {
                        // Clamp the texture ID to a valid value
                        int oldId = texConfig.id;
                        texConfig.id = Math.Clamp(texConfig.id, 0, maxValidTextureId);

                        Console.WriteLine($"  ⚠️ Fixed invalid texture ID in model {model.id}: {oldId} → {texConfig.id}");
                        texturesFixed++;
                        modelFixed = true;
                    }
                }

                if (modelFixed)
                    modelsFixed++;
            }

            if (modelsFixed > 0)
                Console.WriteLine($"  ✅ Fixed texture references in {modelsFixed} models (corrected {texturesFixed} texture IDs)");
            else
                Console.WriteLine("  ✅ All TIE texture references are valid");
        }

        /// <summary>
        /// Creates proper TIE serialization data necessary for the level to load correctly in RC2
        /// </summary>
        /// <param name="level">The level to update</param>
        public static void CreateTieSerializationData(Level level)
        {
            if (level.ties == null || level.ties.Count == 0)
            {
                Console.WriteLine("  No ties to create serialization data for.");
                return;
            }

            Console.WriteLine("  Creating TIE serialization data...");

            try
            {
                // 1. CRITICAL FIX: Set the tieIds list to contain one ID per MODEL, not per instance
                // This must be done FIRST to ensure nothing else overwrites it later
                if (level.tieModels != null)
                {
                    level.tieIds = level.tieModels.Select(m => (int)m.id).ToList();
                    Console.WriteLine($"  ✅ Created proper tieIds list with {level.tieIds.Count} unique model IDs (one per model)");
                }

                // 2. Generate the tieData array (one entry per TIE instance)
                byte[] tieData = new byte[level.ties.Count * 0x70]; // 0x70 bytes per TIE

                // 3. Create a properly sized TIE group data structure:
                // Must be an array of u32 offset, u32 count pairs (8 bytes per model)
                // Total size: tieModels.Count * 8 bytes, then padded to 0x80 alignment
                int baseGroupDataSize = level.tieModels.Count * 8; // 8 bytes per entry (offset + count)
                int paddedGroupDataSize = ((baseGroupDataSize + 0x7F) / 0x80) * 0x80; // Pad to next 0x80 boundary
                byte[] tieGroupData = new byte[paddedGroupDataSize]; 

                // 4. Calculate the required size for each tie's color data
                Dictionary<int, int> tieColorOffsets = new Dictionary<int, int>();
                Dictionary<int, int> tieModelGroups = new Dictionary<int, int>();
                int colorDataOffset = 0;

                // Group ties by model ID for TieGroupData generation
                var tiesByModel = level.ties.GroupBy(t => t.modelID).ToDictionary(g => g.Key, g => g.ToList());

                // 5. Fill the TIE data array and collect model group information
                for (int i = 0; i < level.ties.Count; i++)
                {
                    var tie = level.ties[i];
                    
                    // Store the first instance of each model's offset in tieData
                    if (!tieModelGroups.ContainsKey(tie.modelID))
                    {
                        tieModelGroups[tie.modelID] = i * 0x70; // Offset is index * record size
                    }

                    // Get or allocate color data for this tie
                    if (tie.colorBytes != null && tie.colorBytes.Length > 0)
                    {
                        tieColorOffsets[i] = colorDataOffset;
                        colorDataOffset += tie.colorBytes.Length;
                    }
                    
                    // Write TIE instance data
                    byte[] tieBytes = tie.ToByteArray(tieColorOffsets.ContainsKey(i) ? tieColorOffsets[i] : 0);
                    Array.Copy(tieBytes, 0, tieData, i * 0x70, 0x70);
                }

                // 6. Fill the TIE group data array - critical for proper rendering
                int groupIdx = 0;
                foreach (var model in level.tieModels)
                {
                    int offset = 0;
                    int count = 0;
                    
                    // Find the offset to the first instance of this model in tieData
                    if (tieModelGroups.TryGetValue(model.id, out int dataOffset))
                    {
                        offset = dataOffset;
                        // Count how many ties use this model
                        count = tiesByModel.TryGetValue(model.id, out var instances) ? instances.Count : 0;
                    }

                    // Write the group entry (offset, count) pair at the correct position in tieGroupData
                    int groupEntryOffset = groupIdx * 8; // 8 bytes per entry
                    if (groupEntryOffset + 8 <= tieGroupData.Length)
                    {
                        WriteInt(tieGroupData, groupEntryOffset, offset);     // Offset to first instance
                        WriteInt(tieGroupData, groupEntryOffset + 4, count);  // Number of instances
                    }
                    groupIdx++;
                }

                // 7. Set the serialized data
                level.tieData = tieData;
                level.tieGroupData = tieGroupData;

                Console.WriteLine($"  Created tieData: {level.tieData.Length} bytes");
                Console.WriteLine($"  Created tieGroupData: {level.tieGroupData.Length} bytes (for {level.tieModels?.Count ?? 0} models)");
                Console.WriteLine($"  ✅ TIE group data properly padded to 0x80 alignment");
                
                // 8. Verify our fixes
                if (level.tieIds != null && level.tieModels != null)
                {
                    if (level.tieIds.Count != level.tieModels.Count)
                    {
                        Console.WriteLine($"  ❌ ERROR: tieIds count ({level.tieIds.Count}) still doesn't match model count ({level.tieModels.Count})");
                        // Fix it one more time to be absolutely sure
                        level.tieIds = level.tieModels.Select(m => (int)m.id).ToList();
                        Console.WriteLine($"  ✅ Re-fixed tieIds list: {level.tieIds.Count} entries");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Error creating TIE serialization data: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Creates advanced occlusion data for TIEs to improve rendering performance
        /// </summary>
        /// <param name="level">The level to update with occlusion data</param>
        private static void CreateTieOcclusionData(Level level)
        {
            if (level.ties == null || level.ties.Count == 0)
                return;

            // Ensure all models have valid culling information before generating occlusion data
            EnsureTieModelCullingData(level);

            Console.WriteLine($"🔧 Creating advanced TIE occlusion data for {level.ties.Count} instances...");

            // Initialize occlusion data if it doesn't exist
            if (level.occlusionData == null)
            {
                InitializeOcclusionData(level);
            }

            // Clear existing TIE occlusion data
            level.occlusionData.tieData.Clear();

            // Create spatial groups for better culling
            var spatialGroups = CreateTieSpatialGroups(level.ties);
            Console.WriteLine($"  📦 Created {spatialGroups.Count} spatial groups for TIE culling");

            // Generate occlusion data for each TIE
            for (int i = 0; i < level.ties.Count; i++)
            {
                var tie = level.ties[i];
                
                // Calculate spatial hash for this TIE's position
                int spatialHash = CalculateSpatialHash(tie.position);
                
                // Calculate visibility flags based on various criteria
                int visibilityFlags = CalculateTieVisibilityFlags(tie, spatialGroups, i);
                
                // Add to occlusion data
                level.occlusionData.tieData.Add(new KeyValuePair<int, int>(spatialHash, visibilityFlags));
            }

            Console.WriteLine($"  ✅ Generated occlusion data for {level.ties.Count} TIEs with spatial culling");
        }

        /// <summary>
        /// Validates culling bounds for all TIE models in the level, computing a
        /// bounding sphere when the data is missing. Uses TieModel's internal cache
        /// to avoid recomputation across multiple runs.
        /// </summary>
        private static void EnsureTieModelCullingData(Level level)
        {
            if (level.tieModels == null)
                return;

            foreach (var model in level.tieModels.OfType<TieModel>())
            {
                model.ValidateCullingData();
            }
        }

        /// <summary>
        /// Creates spatial groups for TIEs based on position and model similarity
        /// </summary>
        private static List<List<Tie>> CreateTieSpatialGroups(List<Tie> ties)
        {
            var groups = new List<List<Tie>>();
            var spatialGrid = new Dictionary<Vector3i, List<Tie>>();
            
            const float GRID_SIZE = 80.0f; // Optimal for RC2 culling system
            
            // Group TIEs by spatial grid
            foreach (var tie in ties)
            {
                var gridPos = new Vector3i(
                    (int)(tie.position.X / GRID_SIZE),
                    (int)(tie.position.Y / GRID_SIZE),
                    (int)(tie.position.Z / GRID_SIZE)
                );
                
                if (!spatialGrid.ContainsKey(gridPos))
                {
                    spatialGrid[gridPos] = new List<Tie>();
                }
                
                spatialGrid[gridPos].Add(tie);
            }
            
            // Further subdivide large groups by model type
            foreach (var gridGroup in spatialGrid.Values)
            {
                var modelGroups = gridGroup.GroupBy(t => t.modelID).ToList();

                foreach (var modelGroup in modelGroups)
                {
                    var tieList = modelGroup.ToList(); // ← Changed name to avoid conflict

                    // Split very large groups to prevent excessive batch sizes
                    const int MAX_GROUP_SIZE = 24; // Optimal batch size for RC2
                    for (int i = 0; i < tieList.Count; i += MAX_GROUP_SIZE)
                    {
                        var subGroup = tieList.Skip(i).Take(MAX_GROUP_SIZE).ToList();
                        groups.Add(subGroup);
                    }
                }
            }
            
            return groups;
        }

        /// <summary>
        /// Calculates visibility flags for a TIE based on position, model, and spatial context
        /// </summary>
        private static int CalculateTieVisibilityFlags(Tie tie, List<List<Tie>> spatialGroups, int tieIndex)
        {
            int flags = 0x00000001; // Base visibility flag
            
            // Distance-based culling levels
            float distanceFromOrigin = tie.position.Length;
            if (distanceFromOrigin > 1000.0f)
            {
                flags |= 0x00000010; // Far distance - aggressive culling
            }
            else if (distanceFromOrigin > 500.0f)
            {
                flags |= 0x00000008; // Medium distance - moderate culling
            }
            else if (distanceFromOrigin > 200.0f)
            {
                flags |= 0x00000004; // Near-medium distance - light culling
            }
            // else: very close - no distance culling (keeps base flag only)
            
            // Height-based culling
            if (Math.Abs(tie.position.Y) > 300.0f)
            {
                flags |= 0x00000020; // High altitude culling
            }
            
            // Model complexity culling
            if (tie.model != null)
            {
                int vertexCount = tie.model.vertexBuffer?.Length / 8 ?? 0;
                if (vertexCount > 1000) // High-poly models
                {
                    flags |= 0x00000040; // Complex geometry - more aggressive culling
                }
                else if (vertexCount > 500) // Medium-poly models
                {
                    flags |= 0x00000080; // Medium complexity culling
                }
            }
            
            // Spatial group index (for batch rendering)
            for (int i = 0; i < spatialGroups.Count; i++)
            {
                if (spatialGroups[i].Contains(tie))
                {
                    flags |= (i & 0xFF) << 16; // Store group index in upper bits
                    break;
                }
            }
            
            // Instance priority (earlier instances get higher priority)
            int priority = Math.Max(0, 255 - tieIndex / 4); // Gradually decrease priority
            flags |= (priority & 0xFF) << 8;
            
            return flags;
        }

        /// <summary>
        /// Optimizes TIE group data for better rendering performance
        /// </summary>
        private static void OptimizeTieGroupData(Level level)
        {
            if (level.ties == null || level.tieModels == null)
                return;

            Console.WriteLine("🚀 Optimizing TIE group data for rendering performance...");

            // Group TIEs by model for optimal rendering
            var tiesByModel = level.ties.GroupBy(t => t.modelID).ToDictionary(g => g.Key, g => g.ToList());
            
            // Calculate optimal group data size with spatial coherency
            int baseSize = level.tieModels.Count * 16; // 16 bytes per model group (expanded from 8)
            int alignedSize = ((baseSize + 0x7F) / 0x80) * 0x80; // Align to 0x80 boundary
            
            byte[] optimizedGroupData = new byte[alignedSize];
            
            // Fill group data with spatial and performance optimizations
            for (int i = 0; i < level.tieModels.Count; i++)
            {
                var model = level.tieModels[i];
                int groupOffset = i * 16;
                
                if (tiesByModel.TryGetValue(model.id, out var ties))
                {
                    // Calculate spatial center for this model's instances
                    if (ties.Count > 0)
                    {
                        Vector3 center = Vector3.Zero;
                        foreach (var tie in ties)
                        {
                            center += tie.position;
                        }
                        center /= ties.Count;
                        
                        // Store spatial information in group data
                        WriteInt(optimizedGroupData, groupOffset + 0, i * 0x70); // Data offset
                        WriteInt(optimizedGroupData, groupOffset + 4, ties.Count); // Instance count
                        WriteFloat(optimizedGroupData, groupOffset + 8, center.Length); // Distance from origin
                        WriteInt(optimizedGroupData, groupOffset + 12, CalculateSpatialHash(center)); // Spatial hash
                    }
                }
                else
                {
                    // No instances for this model
                    WriteInt(optimizedGroupData, groupOffset + 0, 0);
                    WriteInt(optimizedGroupData, groupOffset + 4, 0);
                    WriteFloat(optimizedGroupData, groupOffset + 8, 0.0f);
                    WriteInt(optimizedGroupData, groupOffset + 12, 0);
                }
            }
            
            level.tieGroupData = optimizedGroupData;
            Console.WriteLine($"  ✅ Created optimized TIE group data: {alignedSize} bytes with spatial indexing");
        }

        /// <summary>
        /// Checks if a tie object is likely to be garbage/unused based on basic safety heuristics only
        /// Removed aggressive distance-based filtering to preserve all valid TIEs
        /// </summary>
        /// <param name="tie">The tie to check</param>
        /// <returns>True if the tie appears to be garbage</returns>
        public static bool IsLikelyGarbageTie(Tie tie)
        {
            // Keep only the most basic safety checks for truly invalid data
            
            // Check for NaN coordinates (definitely invalid)
            if (float.IsNaN(tie.position.X) || float.IsNaN(tie.position.Y) || float.IsNaN(tie.position.Z))
                return true;

            // Check for extremely unreasonable coordinates (likely corrupted data)
            if (Math.Abs(tie.position.X) > 100000 || Math.Abs(tie.position.Y) > 100000 || Math.Abs(tie.position.Z) > 100000)
                return true;

            // Check for extremely unreasonable scale values (likely corrupted data)
            if (tie.scale.X < 0.00001f || tie.scale.Y < 0.00001f || tie.scale.Z < 0.00001f ||
                tie.scale.X > 100000 || tie.scale.Y > 100000 || tie.scale.Z > 100000)
                return true;

            // REMOVED: All distance-based filtering that was causing problems
            // REMOVED: Position Y threshold filtering 
            // REMOVED: Hash-based probabilistic filtering
            
            // Keep all other TIEs - let the game's culling system handle visibility
            return false;
        }

        /// <summary>
        /// Creates a minimal template TIE for cloning
        /// FIXED: Now creates template with light=1 instead of 0 to avoid lighting issues
        /// </summary>
        /// <returns>A new Tie instance with default RC2 values</returns>
        private static Tie CreateMinimalTemplateTie()
        {
            // Create a minimal byte array for the constructor
            byte[] dummyBlock = new byte[0x70];
            WriteMatrix4(dummyBlock, 0x00, Matrix4.Identity);
            WriteInt(dummyBlock, 0x50, 0); // Model ID = 0
            WriteUint(dummyBlock, 0x54, 4000); // RC2 Standard Value
            WriteUint(dummyBlock, 0x58, 0); // TIE ID = 0
            WriteUint(dummyBlock, 0x5C, 0); // Always 0
            WriteInt(dummyBlock, 0x60, 0); // Color offset
            WriteUint(dummyBlock, 0x64, 0); // Always 0
            WriteUshort(dummyBlock, 0x68, 1); // ← FIXED: Set light to 1 instead of 0 as a safer default
            WriteUshort(dummyBlock, 0x6A, 0xFFFF); // Standard value
            WriteUint(dummyBlock, 0x6C, 0); // Always 0

            // Create a temporary file to use with FileStream
            string tempFilePath = Path.GetTempFileName();
            try
            {
                // Create an empty model list since we don't have actual models yet
                var dummyModels = new List<Model>();

                // We need a temporary FileStream for the Tie constructor
                using (FileStream fs = File.Create(tempFilePath))
                {
                    fs.Write(dummyBlock, 0, dummyBlock.Length);
                    fs.Seek(0, SeekOrigin.Begin);

                    // Create a new Tie with default values
                    Tie templateTie = new Tie(dummyBlock, 0, dummyModels, fs);
                    templateTie.colorBytes = new byte[0]; // Empty color bytes

                    return templateTie;
                }
            }
            finally
            {
                // Clean up
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); }
                    catch { /* Ignore delete errors */ }
                }
            }
        }

        /// <summary>
        /// Validates and fixes TIE data alignment issues that would crash RC2
        /// </summary>
        /// <param name="level">The level to validate</param>
        public static void ValidateTieDataAlignment(Level level)
        {
            Console.WriteLine("\n🔍 Validating TIE data alignment...");

            bool needsFixing = false;

            // Check if tieData size is correct
            if (level.ties != null && level.tieData != null)
            {
                int expectedSize = level.ties.Count * 0x70;
                if (level.tieData.Length != expectedSize)
                {
                    Console.WriteLine($"  ⚠️ tieData size mismatch: {level.tieData.Length} bytes (expected {expectedSize})");
                    needsFixing = true;
                }
                else
                {
                    Console.WriteLine($"  ✅ tieData size is correct: {level.tieData.Length} bytes");
                }
            }

            // Check if tieGroupData is aligned to 0x80 boundaries
            if (level.tieGroupData != null)
            {
                if (level.tieGroupData.Length % 0x80 != 0)
                {
                    Console.WriteLine($"  ⚠️ tieGroupData size ({level.tieGroupData.Length}) is not aligned to 0x80 boundaries");
                    needsFixing = true;
                }
                else
                {
                    Console.WriteLine($"  ✅ tieGroupData is properly aligned: {level.tieGroupData.Length} bytes");
                }

                // Check if tieGroupData size is suspiciously large
                if (level.tieGroupData.Length > 1_000_000)  // Over 1MB is suspicious for typical levels
                {
                    Console.WriteLine($"  ⚠️ tieGroupData size is suspiciously large: {level.tieGroupData.Length} bytes");
                    needsFixing = true;
                }
            }

            // Check if tieIds list has correct length
            if (level.tieIds != null && level.tieModels != null)
            {
                if (level.tieIds.Count != level.tieModels.Count)
                {
                    Console.WriteLine($"  ⚠️ tieIds count ({level.tieIds.Count}) doesn't match tieModels count ({level.tieModels.Count})");
                    needsFixing = true;
                }
                else
                {
                    Console.WriteLine($"  ✅ tieIds count matches tieModels count: {level.tieIds.Count}");
                }
            }

            if (needsFixing)
            {
                Console.WriteLine("  ⚠️ TIE data has alignment issues that will be fixed during serialization");
                Console.WriteLine("  Recreating TIE serialization data with proper alignment...");
                CreateTieSerializationData(level);

                // Verify the fix
                if (level.tieGroupData != null && level.tieGroupData.Length % 0x80 == 0)
                {
                    Console.WriteLine("  ✅ TIE data alignment issues fixed successfully");
                }
                else
                {
                    Console.WriteLine("  ❌ Failed to fix TIE data alignment issues");
                }
            }
            else
            {
                Console.WriteLine("  ✅ TIE data alignment looks good");
            }
        }

        /// <summary>
        /// Removes all TIE models that aren't referenced by any TIE instance in the level
        /// </summary>
        /// <param name="level">The level to clean up</param>
        /// <returns>Number of removed models</returns>
        public static int RemoveUnusedTieModels(Level level)
        {
            if (level?.tieModels == null || level.ties == null)
            {
                Console.WriteLine("  Cannot remove unused TIE models: missing model list or TIE instances");
                return 0;
            }

            Console.WriteLine("\n🧹 Cleaning up unused TIE models...");

            // Get all model IDs that are currently in use by TIE instances
            HashSet<int> usedModelIds = new HashSet<int>(level.ties.Select(t => t.modelID));

            // Identify unused models
            var unusedModels = level.tieModels.Where(m => !usedModelIds.Contains(m.id)).ToList();

            if (unusedModels.Count == 0)
            {
                Console.WriteLine("  ✅ No unused TIE models found");
                return 0;
            }

            // Log the unused models we're about to remove
            Console.WriteLine($"  Found {unusedModels.Count} unused TIE models to remove:");
            foreach (var model in unusedModels.Take(10))
            {
                Console.WriteLine($"  - Model ID: {model.id}");
            }
            if (unusedModels.Count > 10)
                Console.WriteLine($"  - ... and {unusedModels.Count - 10} more");

            // Remove all the unused models
            int initialCount = level.tieModels.Count;
            level.tieModels.RemoveAll(m => !usedModelIds.Contains(m.id));
            int removedCount = initialCount - level.tieModels.Count;

            // Update the tieIds list to match the remaining models
            level.tieIds = level.tieModels.Select(m => (int)m.id).ToList();

            Console.WriteLine($"  ✅ Removed {removedCount} unused TIE models");
            Console.WriteLine($"  ✅ Updated tieIds list with {level.tieIds.Count} entries");

            return removedCount;
        }

        /// <summary>
        /// Resolves ID conflicts between Moby models and TIE models by remapping TIE model IDs
        /// </summary>
        /// <param name="level">The level to process</param>
        /// <returns>The number of conflicts resolved</returns>
        public static int ResolveMobyTieIdConflicts(Level level)
        {
            Console.WriteLine("\n🔍 Checking for Moby-TIE model ID conflicts...");

            if (level.mobyModels == null || level.tieModels == null)
            {
                Console.WriteLine("  Cannot check conflicts: Missing model lists");
                return 0;
            }

            // Collect moby model IDs
            HashSet<int> mobyIds = new HashSet<int>();
            foreach (var model in level.mobyModels)
            {
                if (model != null)
                {
                    mobyIds.Add(model.id);
                }
            }
            Console.WriteLine($"  Found {mobyIds.Count} unique moby model IDs");

            // Collect tie model IDs and create a reference lookup
            HashSet<int> tieIds = new HashSet<int>();
            Dictionary<int, TieModel> tieModelsById = new Dictionary<int, TieModel>();
            foreach (var model in level.tieModels)
            {
                if (model != null)
                {
                    tieIds.Add(model.id);
                    if (model is TieModel tieModel)
                    {
                        tieModelsById[(int)model.id] = tieModel;
                    }
                }
            }
            Console.WriteLine($"  Found {tieIds.Count} unique tie model IDs");

            // Find conflicts
            var conflicts = mobyIds.Intersect(tieIds).ToList();

            if (conflicts.Count == 0)
            {
                Console.WriteLine("  ✅ No ID conflicts found between Moby models and TIE models");
                return 0;
            }

            Console.WriteLine($"  ⚠️ Found {conflicts.Count} ID conflicts between Moby models and TIE models");
            foreach (var id in conflicts)
            {
                Console.WriteLine($"    - ID {id}");
            }

            // Find the highest existing model ID to use as a starting point for new IDs
            int highestModelId = Math.Max(
                level.mobyModels.Max(m => m?.id ?? 0),
                level.tieModels.Max(m => (int)(m?.id ?? 0))
            );

            int nextAvailableId = highestModelId + 1000; // Start well beyond existing IDs
            int conflictsResolved = 0;

            // Collect mappings for affected ties
            Dictionary<int, int> idRemapping = new Dictionary<int, int>();

            // First step: Remap the IDs in the tie models
            foreach (int conflictId in conflicts)
            {
                if (tieModelsById.TryGetValue(conflictId, out TieModel? tieModel) && tieModel != null)
                {
                    int newId = nextAvailableId++;

                    // Store the old ID and new ID mapping
                    idRemapping[conflictId] = newId;

                    // Update the model ID
                    tieModel.id = (short)newId;

                    Console.WriteLine($"  🔄 Remapped TIE model {conflictId} to {newId}");
                    conflictsResolved++;
                }
            }

            // Second step: Update all TIE instances to use the new model IDs
            int instancesUpdated = 0;
            if (level.ties != null)
            {
                foreach (var tie in level.ties)
                {
                    if (idRemapping.TryGetValue(tie.modelID, out int newModelId))
                    {
                        tie.modelID = newModelId;
                        instancesUpdated++;
                    }
                }
            }

            // Third step: Update the tieIds list to reflect the new model IDs
            if (level.tieIds != null)
            {
                for (int i = 0; i < level.tieIds.Count; i++)
                {
                    if (idRemapping.TryGetValue(level.tieIds[i], out int newModelId))
                    {
                        level.tieIds[i] = newModelId;
                    }
                }
            }
            else
            {
                level.tieIds = level.tieModels.Select(m => (int)m.id).ToList();
            }

            Console.WriteLine($"  ✅ Resolved {conflictsResolved} model ID conflicts");
            Console.WriteLine($"  ✅ Updated {instancesUpdated} TIE instances to use new model IDs");
            Console.WriteLine($"  ✅ Updated tieIds list with new model IDs");

            return conflictsResolved;
        }

        /// <summary>
        /// Validates that all TIE instances reference valid models
        /// </summary>
        /// <param name="level">The level to validate</param>
        private static void ValidateTieModelReferences(Level level)
        {
            if (level.ties == null || level.tieModels == null)
                return;

            Console.WriteLine("\n🔍 Validating TIE model references...");

            var modelIds = new HashSet<int>(level.tieModels.Select(m => (int)m.id));
            var danglingReferences = 0;

            foreach (var tie in level.ties)
            {
                if (!modelIds.Contains(tie.modelID))
                {
                    danglingReferences++;
                    Console.WriteLine($"  ⚠️ Tie references missing model ID {tie.modelID} - this will crash the game!");
                }
            }

            if (danglingReferences > 0)
            {
                Console.WriteLine($"  ❌ Found {danglingReferences} ties with invalid model references");
                Console.WriteLine("  This will crash the game when loaded! Fix the model references.");
            }
            else
            {
                Console.WriteLine("  ✅ All tie model references are valid");
            }

            // Additional validation for tieIds list
            if (level.tieIds != null)
            {
                // Check if tieIds matches model count (correct) or instance count (wrong)
                if (level.tieIds.Count == level.ties.Count && level.tieIds.Count != level.tieModels.Count)
                {
                    Console.WriteLine($"  ❌ CRITICAL: tieIds list has {level.tieIds.Count} entries (matches instance count)");
                    Console.WriteLine("  This should match the model count instead. Will be fixed during serialization.");
                }
                else if (level.tieIds.Count != level.tieModels.Count)
                {
                    Console.WriteLine($"  ⚠️ tieIds list has {level.tieIds.Count} entries, but there are {level.tieModels.Count} models");
                    Console.WriteLine("  This mismatch may cause issues. Will be fixed during serialization.");
                }
                else
                {
                    Console.WriteLine($"  ✅ tieIds list correctly contains {level.tieIds.Count} entries (matches model count)");
                }

                // Check if all entries in tieIds actually exist as models
                var missingIds = level.tieIds.Where(id => !modelIds.Contains(id)).ToList();
                if (missingIds.Count > 0)
                {
                    Console.WriteLine($"  ⚠️ Found {missingIds.Count} IDs in tieIds that don't match any model");
                    Console.WriteLine("  This may cause crashes. Will be fixed during serialization.");
                }
            }
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
            int x = (int) (position.X / 40.0f);
            int y = (int) (position.Y / 40.0f);
            int z = (int) (position.Z / 40.0f);

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
