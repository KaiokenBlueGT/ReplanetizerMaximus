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
using System.Security.Cryptography; // For SHA256
using System.Text.Json;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using LibReplanetizer.Parsers;
using OpenTK.Mathematics;
using LibTexture = LibReplanetizer.Texture;
using LibVector3 = OpenTK.Mathematics.Vector3;
using LibQuaternion = OpenTK.Mathematics.Quaternion;
using static LibReplanetizer.DataFunctions;

namespace GeometrySwapper
{
    [Flags]
    public enum SwapOptions
    {
        None = 0,
        Terrain = 1 << 0,
        Collision = 1 << 1,
        Ties = 1 << 2,
        Shrubs = 1 << 3,
        Skybox = 1 << 4,
        GrindPaths = 1 << 5,
        PointLights = 1 << 6,
        SoundInstances = 1 << 7,
        Mobys = 1 << 8,
        All = Terrain | Collision | Ties | Shrubs | Skybox | GrindPaths | PointLights | SoundInstances,
        RC2SelfTest = 1 << 9,
        SwapLevelVariables = 1 << 10,
        TransferRatchetPosition = 1 << 11,
        RegisterPlanetInMap = 1 << 12,
        SwapVendorWithOltanis = 1 << 13,
        SwapCratesWithOltanis = 1 << 14,
        SwapSwingshots = 1 << 15,
        RunGrindPathDiagnostics = 1 << 16,
        UseMobyConverter = 1 << 17,
        CreateMobyInstances = 1 << 18,
        SwapShipExitAnim = 1 << 19,
        CopySpecialMobysOnly = 1 << 20,
        ImportCuboids = 1 << 21,
        RunShipCameraDiagnostics = 1 << 22,
        ImportSplines = 1 << 23,
        RestoreAnimations = 1 << 24,
        FutureToUyaConversion = 1 << 25,
        RunOcclusionDiagnostics = 1 << 26,
    }

    class Program
    {
        // Make these static fields at the class level so they're accessible from all methods
        private static string globalRc3Dir = @"D:\Projects\R&C1_to_R&C2_Planet_Format\Up_Your_Arsenal_PSARC\rc3\ps3data\global\";
        private static string globalRc2Dir = @"D:\Projects\R&C1_to_R&C2_Planet_Format\Going_Commando_PSARC\rc2\ps3data\global\";
        private static string globalRc1Dir = @"D:\Projects\R&C1_to_R&C2_Planet_Format\Ratchet_1\rc1\ps3data\global\";
        private static string defaultSourceLevelDir = @"C:\Users\Ryan_\Downloads\temp\Oltanis_RaC1\";
        private static string defaultRc2SourceLevelDir = @"C:\Users\Ryan_\Downloads\temp\GC_LEVEL\";
        private static string sourceLevelDir = defaultSourceLevelDir;
        private static string rc3DonorLevelDir = @"C:\Users\Ryan_\Downloads\temp\UYA_DONOR\"; // Using an RaC3 level as donor
        private static string rc2DonorLevelDir = @"C:\Users\Ryan_\Downloads\temp\Insomniac_Museum-Smolg\"; // Using Smolg as donor
        private static string rc1DonorLevelDir = @"C:\Users\Ryan_\Downloads\temp\Oltanis_RaC1\"; // Using an RaC1 level as donor
        private static string referenceUyaPlanetDir = @"C:\Users\Ryan_\Downloads\temp\UYA_DONOR\"; // Reference RaC3 level (should be unused from my RaC3 converter... hopefully.)
        private static string referenceGcPlanetDir = @"C:\Users\Ryan_\Downloads\temp\Damosel\";
        private static string referenceRc1PlanetDir = @"C:\Users\Ryan_\Downloads\temp\Oltanis_RaC1\";
        private static string outputDir = @"C:\Users\Ryan_\Downloads\temp\FutureonDaxxBase\"; // Changed output name
        private const string ConfigFileName = "swap_config.json";

        private class SwapConfig
        {
            public int SourceGame { get; set; } = 1;
            public int TargetGame { get; set; } = 3;
            public string SourceLevelDir { get; set; } = sourceLevelDir;
            public string DonorLevelDir { get; set; } = rc3DonorLevelDir;
            public string ReferencePlanetDir { get; set; } = referenceUyaPlanetDir;
            public string GlobalDir { get; set; } = globalRc3Dir;
            public string OutputDir { get; set; } = outputDir;
            public SwapOptions Options { get; set; } = SwapOptions.All;
        }

        private static SwapConfig? LoadConfig()
        {
            if (!File.Exists(ConfigFileName)) return null;
            try
            {
                string json = File.ReadAllText(ConfigFileName);
                return JsonSerializer.Deserialize<SwapConfig>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void SaveConfig(SwapConfig config)
        {
            try
            {
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFileName, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error saving config: {ex.Message}");
            }
        }

        private static int GetSourceGameFromUser()
        {
            Console.Write("Select source game (1=RaC1, 2=RaC2) [default 1]: ");
            string? input = Console.ReadLine();
            return input?.Trim() == "2" ? 2 : 1;
        }

        private static int GetTargetGameFromUser()
        {
            Console.Write("Select target game (1=RaC1, 2=RaC2, 3=RaC3) [default 3]: ");
            string? input = Console.ReadLine();
            return input?.Trim() == "1" ? 1 : input?.Trim() == "2" ? 2 : 3;
        }

        static void WriteUintBigEndian(FileStream fs, uint value)
        {
            byte[] b = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            fs.Write(b, 0, 4);
        }

        static string GetTextureSignature(LibTexture tex)
        {
            if (tex.data == null || tex.data.Length == 0)
            {
                return $"NO_DATA_texture_{tex.width}x{tex.height}_{Guid.NewGuid()}";
            }
            using (var sha256 = SHA256.Create())
            {
                return Convert.ToBase64String(sha256.ComputeHash(tex.data));
            }
        }

        /// <summary>
        /// Remaps texture IDs in a model based on a texture ID mapping dictionary
        /// </summary>
        static void ReindexModelTextures(Model modelToUpdate, Dictionary<int, int> textureMapping, string modelContext)
        {
            if (modelToUpdate?.textureConfig == null) return;

            for (int i = 0; i < modelToUpdate.textureConfig.Count; i++)
            {
                var texConfig = modelToUpdate.textureConfig[i];
                int oldId = texConfig.id;

                // Try to map the texture ID
                if (textureMapping.TryGetValue(oldId, out int newId))
                {
                    texConfig.id = newId;
                    Console.WriteLine($"  Reindexed texture in {modelContext} (ID {modelToUpdate.id}): {oldId} → {newId}");
                }
                else
                {
                    Console.WriteLine($"  Warning: No mapping for texture ID {oldId} in model {modelContext} (ID {modelToUpdate.id})");
                }
            }
        }

        static void DumpTerrainInfo(Terrain? terrain, string label)
        {
            if (terrain == null)
            {
                Console.WriteLine($"\n===== TERRAIN DUMP for {label} =====");
                Console.WriteLine("Terrain is null");
                Console.WriteLine("=============================\n");
                return;
            }

            Console.WriteLine($"\n===== TERRAIN DUMP for {label} =====");
            Console.WriteLine($"Level Number: {terrain.levelNumber}");
            Console.WriteLine($"Fragment Count: {terrain.fragments.Count}");

            // Dump first few fragments for analysis
            int count = Math.Min(5, terrain.fragments.Count);
            for (int i = 0; i < count; i++)
            {
                var frag = terrain.fragments[i];
                Console.WriteLine($"\nFragment {i}:");
                Console.WriteLine($"  Culling Center: {frag.cullingCenter}");
                Console.WriteLine($"  Culling Size: {frag.cullingSize}");
                Console.WriteLine($"  Model ID: {frag.modelID}");
                Console.WriteLine($"  off1C: 0x{frag.off1C:X4}");
                Console.WriteLine($"  off1E (Fragment ID): 0x{frag.off1E:X4}");
                Console.WriteLine($"  off20: 0x{frag.off20:X4}");
                Console.WriteLine($"  off24: 0x{frag.off24:X8}");
                Console.WriteLine($"  off28: 0x{frag.off28:X8}");
                Console.WriteLine($"  off2C: 0x{frag.off2C:X8}");

                // Dump some model info if available
                if (frag.model != null)
                {
                    var model = frag.model;
                    Console.WriteLine($"  Model:");
                    Console.WriteLine($"    ID: {model.id}");
                    Console.WriteLine($"    Vertex Count: {model.vertexBuffer.Length / (model is SkyboxModel ? 6 : 8)}");
                    Console.WriteLine($"    Face Count: {model.indexBuffer.Length / 3}");
                    Console.WriteLine($"    Texture Configs: {model.textureConfig?.Count ?? 0}");
                }
            }
            Console.WriteLine("=============================\n");
        }

        static void LoadGlobalRCAssets(
            string globalRcDir,
            out List<Model> globalArmorModels,
            out List<List<LibTexture>> globalArmorTextures,
            out List<Model> globalGadgetModels,
            out List<LibTexture> globalGadgetTextures)
        {
            Console.WriteLine($"Loading global RC assets from {globalRcDir}...");

            // Initialize output parameters with empty collections
            globalArmorModels = new List<Model>();
            globalArmorTextures = new List<List<LibTexture>>();  // Fix this line
            globalGadgetModels = new List<Model>();
            globalGadgetTextures = new List<LibTexture>();        // Fix this line

            try
            {
                // Check if directory exists
                if (!Directory.Exists(globalRcDir))
                {
                    Console.WriteLine($"❌ Global RC directory not found: {globalRcDir}");
                    return;
                }

                // TODO: Load armor assets
                string armorDir = Path.Combine(globalRcDir, "armor");
                if (Directory.Exists(armorDir))
                {
                    // Load armor models and textures logic
                    // This would typically involve loading .ps3 files for each armor set
                    Console.WriteLine($"  Loading armor assets from {armorDir}...");
                    // Example implementation would parse files in this directory
                }
                else
                {
                    Console.WriteLine($"  ⚠️ Armor directory not found: {armorDir}");
                }

                // TODO: Load gadget assets
                string gadgetDir = Path.Combine(globalRcDir, "gadget");
                if (Directory.Exists(gadgetDir))
                {
                    // Load gadget models and textures logic
                    Console.WriteLine($"  Loading gadget assets from {gadgetDir}...");
                    // Example implementation would parse files in this directory
                }
                else
                {
                    Console.WriteLine($"  ⚠️ Gadget directory not found: {gadgetDir}");
                }

            Console.WriteLine($"✅ Global RC assets loaded: {globalArmorModels.Count} armor models, {globalGadgetModels.Count} gadget models");
        }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading global RC3/UYA assets: {ex.Message}");
            }
        }

        static void RunFutureToUyaConverter()
        {
            Console.WriteLine("\n==== Future Series to UYA Converter ====");

            // Get paths from user with better guidance
            Console.Write("Enter ToD/ACiT level path (folder containing main.dat or assetlookup.dat): ");
            string futurePath = Console.ReadLine()?.Trim() ?? "";

            // If user entered a file path, get the directory
            if (Path.HasExtension(futurePath))
            {
                futurePath = Path.GetDirectoryName(futurePath) ?? futurePath;
                Console.WriteLine($"Using directory: {futurePath}");
            }

            Console.Write($"Enter UYA donor level path (default: {rc3DonorLevelDir}): ");
            string uyaInput = Console.ReadLine()?.Trim() ?? "";
            string uyaPath = string.IsNullOrEmpty(uyaInput) ? rc3DonorLevelDir : uyaInput;

            Console.Write("Enter output path: ");
            string outputPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = Path.Combine(Path.GetDirectoryName(futurePath) ?? "", "UYA_Converted");
                Console.WriteLine($"Using default output: {outputPath}");
            }

            // Replace this block in RunFutureToUyaConverter()
            /*
            FutureToUyaConverter.ConvertFutureLevelToUya(
                futurePath,
                uyaPath,
                outputPath,
                FutureToUyaConverter.FutureSwapOptions.UFrags |
                FutureToUyaConverter.FutureSwapOptions.Textures |
                FutureToUyaConverter.FutureSwapOptions.Collision
            );
            */
            // TODO: Implement FutureToUyaConverter.ConvertFutureLevelToUya or add the required reference.
            Console.WriteLine("FutureToUyaConverter is not implemented or referenced. Please add the implementation.");
        }
        static void FinalizeLevelProcessing(
             Level targetLevel, // This is the rc2Level that's being modified
             Level sourceLevel,
             Level referenceRc3PlanetLevel, // For Moby templates
             List<Model> globalArmorModels,
             List<List<LibTexture>> globalArmorTextures,  // Change this from Texture to LibTexture
             List<Model> globalGadgetModels,
             List<LibTexture> globalGadgetTextures,       // Change this from Texture to LibTexture
             SwapOptions options,
             Level rc3DonorLevel, // Added parameter to access rc3DonorLevel
             int targetGameSelected) // 🆕 NEW: Add target game parameter

        {
            // 🆕 DEBUG: Show which options are enabled
            Console.WriteLine($"🔍 DEBUG: FinalizeLevelProcessing called with options: {options}");
            Console.WriteLine($"🔍 DEBUG: RestoreAnimations flag set: {(options & SwapOptions.RestoreAnimations) != 0}");
            Console.WriteLine("🧪 TEXTURE SYSTEM CHECKPOINT MODE 🧪");
            Console.WriteLine("Testing terrain swap with original texture IDs preserved");

            // ----- 1. BASIC SETUP - ALWAYS NEEDED -----
            Console.WriteLine("Setting up level variables...");
            if (rc3DonorLevel.levelVariables != null && targetLevel.levelVariables != null)
            {
                // Store original values we want to preserve
                float originalDeathPlaneZ = sourceLevel.levelVariables.deathPlaneZ;

                // Ensure correct ByteSize - critically important
                targetLevel.levelVariables.ByteSize = 0x8C;

                // Keep all values from donor except death plane Z
                targetLevel.levelVariables.deathPlaneZ = originalDeathPlaneZ;

                Console.WriteLine($"  LevelVariables preserved from donor, with death plane Z = {originalDeathPlaneZ}");

                // Check if we should swap level variables
                if ((options & SwapOptions.SwapLevelVariables) != 0 && sourceLevel.levelVariables != null)
                {
                    Console.WriteLine("  Swapping selected level variables from RC1 source level...");

                    // Swap background color
                    targetLevel.levelVariables.backgroundColor = sourceLevel.levelVariables.backgroundColor;
                    Console.WriteLine($"    Background Color swapped from RC1: R={sourceLevel.levelVariables.backgroundColor.R}, G={sourceLevel.levelVariables.backgroundColor.G}, B={sourceLevel.levelVariables.backgroundColor.B}");

                    // Swap fog color
                    targetLevel.levelVariables.fogColor = sourceLevel.levelVariables.fogColor;
                    Console.WriteLine($"    Fog Color swapped from RC1: R={sourceLevel.levelVariables.fogColor.R}, G={sourceLevel.levelVariables.fogColor.G}, B={sourceLevel.levelVariables.fogColor.B}");

                    // Swap ship position
                    targetLevel.levelVariables.shipPosition = sourceLevel.levelVariables.shipPosition;
                    Console.WriteLine($"    Ship Position swapped from RC1: X={sourceLevel.levelVariables.shipPosition.X}, Y={sourceLevel.levelVariables.shipPosition.Y}, Z={sourceLevel.levelVariables.shipPosition.Z}");

                    // Swap ship rotation
                    targetLevel.levelVariables.shipRotation = sourceLevel.levelVariables.shipRotation;
                    Console.WriteLine($"    Ship Rotation swapped from RC1: {sourceLevel.levelVariables.shipRotation}");

                    Console.WriteLine("  ✅ Level variables swapped successfully");
                }
            }

            // Check if we should transfer Ratchet's position
            if ((options & SwapOptions.TransferRatchetPosition) != 0)
            {
                Console.WriteLine("  Transferring Ratchet moby position and rotation from RC1 source level...");

                // Find Ratchet in RC1 source level (typically mobyID = 0)
                Moby? rc1Ratchet = sourceLevel.mobs?.FirstOrDefault(m => m.modelID == 0);

                // Find Ratchet in target level
                Moby? targetRatchet = targetLevel.mobs?.FirstOrDefault(m => m.modelID == 0);

                if (rc1Ratchet != null && targetRatchet != null)
                {
                    // Store original values for logging
                    LibVector3 originalPosition = targetRatchet.position;
                    LibQuaternion originalRotation = targetRatchet.rotation;

                    // Transfer position from RC1 Ratchet to target Ratchet
                    targetRatchet.position = rc1Ratchet.position;

                    // Transfer rotation from RC1 Ratchet to target Ratchet
                    targetRatchet.rotation = rc1Ratchet.rotation;

                    // Make sure the transform matrix is updated with the new position and rotation
                    targetRatchet.UpdateTransformMatrix();

                    Console.WriteLine($"    Ratchet position transferred from RC1: {rc1Ratchet.position}");
                    Console.WriteLine($"    Original position was: {originalPosition}");
                    Console.WriteLine($"    Ratchet rotation transferred from RC1: {rc1Ratchet.rotation}");
                    Console.WriteLine($"    Original rotation was: {originalRotation}");
                    Console.WriteLine("  ✅ Ratchet position and rotation transferred successfully");
                }
                else
                {
                    Console.WriteLine("  ❌ Could not transfer Ratchet position and rotation:");
                    Console.WriteLine($"    RC1 Ratchet found: {rc1Ratchet != null}");
                    Console.WriteLine($"    Target Ratchet found: {targetRatchet != null}");

                    if (rc1Ratchet != null)
                    {
                        Console.WriteLine($"    RC1 Ratchet position was: {rc1Ratchet.position}");
                        Console.WriteLine($"    RC1 Ratchet rotation was: {rc1Ratchet.rotation}");
                    }
                }
            }

            // ----- 2. TERRAIN SWAP WITHOUT TEXTURE REINDEXING -----
            if ((options & SwapOptions.Terrain) != 0 && sourceLevel.terrainEngine != null)
            {
                Console.WriteLine("  Starting terrain transfer (preserving original texture IDs)...");

                // Make a deep copy of the RC1 terrain engine to avoid messing with the original
                Terrain terrainToTransfer = sourceLevel.terrainEngine;

                // Assign the RC1 terrain to the RC3/UYA level without modification
                targetLevel.terrainEngine = terrainToTransfer;

                // RC3/UYA-specific terrain fragment properties that need to be set
                Console.WriteLine("  Setting RC3/UYA-specific values on terrain fragments...");
                if (targetLevel.terrainEngine?.fragments != null)
                {
                    for (int i = 0; i < targetLevel.terrainEngine.fragments.Count; i++)
                    {
                        var frag = targetLevel.terrainEngine.fragments[i];

                        // Get the fragment's byte array to modify offset 0x22 (chunk index)
                        byte[] fragBytes = frag.ToByteArray();

                        // Set chunk index (offset 0x22) to 0 since we're dealing with a single-chunk level
                        WriteShort(fragBytes, 0x22, 0);

                        // RC3/UYA standard values (based on TerrainDiagnostics recommendations)
                        frag.off1C = 0xFFFF;  // Common RC3/UYA value
                        frag.off20 = 0xFF00;  // Common RC3/UYA value
                        frag.off24 = 0;       // Common RC3/UYA value
                        frag.off28 = 0;       // Common RC3/UYA value
                        frag.off2C = 0;       // Common RC3/UYA value

                        // 🆕 CRITICAL FIX: Assign sequential Fragment IDs for RC2/RC3 compatibility
                        frag.off1E = (ushort) i;
                        Console.WriteLine($"  Assigned Fragment ID {i} to terrain fragment (was {frag.off1E})");

                        // IMPORTANT: Do NOT change any texture IDs
                        // DO NOT reindex the fragment's model texture config

                        // Make sure transformation matrix is updated after these changes
                        frag.UpdateTransformMatrix();
                    }

                    Console.WriteLine($"  ✅ Assigned sequential Fragment IDs to {targetLevel.terrainEngine.fragments.Count} terrain fragments");
                }

                if (targetLevel.terrainEngine?.fragments != null && targetLevel.terrainEngine.fragments.Count > 0)
                {
                    Console.WriteLine($"  Terrain swapped with {targetLevel.terrainEngine.fragments.Count} fragments (original texture IDs preserved).");
                }
                else
                {
                    Console.WriteLine("  Terrain swap skipped due to null terrain engine or fragments.");
                }

                // 🔧 CRITICAL FIX: Import RC1 textures IMMEDIATELY after terrain swap
                // This must happen BEFORE TIE/Shrub swapping to avoid texture ID conflicts
                Console.WriteLine("  Importing RC1 textures immediately after terrain swap...");
                ImportRC1TexturesToRC2Level(targetLevel, sourceLevel);

                // Optional: Dump the first few fragments to check their texture IDs
                if (targetLevel.terrainEngine?.fragments != null && targetLevel.terrainEngine.fragments.Count > 0)
                {
                    Console.WriteLine("\n  Texture ID check on first 5 terrain fragments:");
                    for (int i = 0; Math.Min(5, targetLevel.terrainEngine.fragments.Count) > i; i++)
                    {
                        var frag = targetLevel.terrainEngine.fragments[i];
                        if (frag.model?.textureConfig != null)
                        {
                            Console.WriteLine($"    Fragment {i}: {frag.model.textureConfig.Count} texture configs");
                            for (int j = 0; j < frag.model.textureConfig.Count; j++)
                            {
                                Console.WriteLine($"      TextureConfig {j} references texture ID: {frag.model.textureConfig[j].id}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"    Fragment {i}: No texture configs");
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("  Terrain swap skipped.");
            }

            // ----- Add Collision Support -----
            if ((options & SwapOptions.Collision) != 0)
            {
                Console.WriteLine("📦 Copying RC1 collision data into RC3/UYA level...");

                if (sourceLevel.collisionEngine != null)
                {
                    // Copy the collision model from RC1 to RC3/UYA
                    Console.WriteLine("  Copying main collision model...");
                    targetLevel.collisionEngine = sourceLevel.collisionEngine;

                    // Copy the collision bytes (raw data needed for serialization)
                    if (sourceLevel.collBytesEngine != null && sourceLevel.collBytesEngine.Length > 0)
                    {
                        targetLevel.collBytesEngine = sourceLevel.collBytesEngine;
                        Console.WriteLine($"  Copied collision bytes: {targetLevel.collBytesEngine.Length} bytes");
                    }
                    else
                    {
                        Console.WriteLine("  Warning: No collision bytes found in RC1 source level");
                    }

                    Console.WriteLine("✅ Main collision model copied from RC1 to RC3/UYA");
                }
                else
                {
                    Console.WriteLine("❌ No collision model found in RC1 source level");
                }

                // Handle collision chunks if any exist
                if (sourceLevel.collisionChunks != null && sourceLevel.collisionChunks.Count > 0)
                {
                    Console.WriteLine($"  Found {sourceLevel.collisionChunks.Count} collision chunks in RC1 source");

                    // Initialize collision chunks in target level if needed
                    if (targetLevel.collisionChunks == null)
                    {
                        targetLevel.collisionChunks = new List<Collision>();
                    }
                    else
                    {
                        targetLevel.collisionChunks.Clear();
                    }

                    // Initialize collision bytes chunks in target level if needed
                    if (targetLevel.collBytesChunks == null)
                    {
                        targetLevel.collBytesChunks = new List<byte[]>();
                    }
                    else
                    {
                        targetLevel.collBytesChunks.Clear();
                    }

                    // Copy each chunk's collision data
                    for (int i = 0; i < sourceLevel.collisionChunks.Count; i++)
                    {
                        targetLevel.collisionChunks.Add(sourceLevel.collisionChunks[i]);

                        if (sourceLevel.collBytesChunks != null && i < sourceLevel.collBytesChunks.Count)
                        {
                            targetLevel.collBytesChunks.Add(sourceLevel.collBytesChunks[i]);
                        }

                        Console.WriteLine($"  Copied collision chunk {i}");
                    }
                }

                Console.WriteLine("✅ Collision data transfer complete");
            }
            else
            {
                Console.WriteLine("  Collision swap skipped.");
            }

            // ----- Handle Tie swapping -----
            if ((options & SwapOptions.Ties) != 0 && sourceLevel.ties != null && sourceLevel.ties.Count > 0)
            {
                Console.WriteLine("\n==== Swapping TIEs with RC1 Oltanis TIEs ====");

                // Configure options for TIE swapping
                TieSwapper.TieSwapOptions tieOptions = TieSwapper.TieSwapOptions.FullSwap;

                // Ask the user if they want to configure TIE swapping options
                if (GetYesNoInput("Would you like to configure TIE swapping options? (Y/N): "))
                {
                    Console.WriteLine("\nConfiguring TIE swap options:");

                    Console.WriteLine("1. Full swap (placements, models, and textures)");
                    Console.WriteLine("2. Placements and models only (no texture mapping)");
                    Console.WriteLine("3. Placements only (use RC3/UYA models)");
                    Console.WriteLine("4. Custom options");
                    Console.Write("> ");

                    string choice = Console.ReadLine()?.Trim() ?? "1";

                    switch (choice)
                    {
                        case "1":
                            tieOptions = TieSwapper.TieSwapOptions.FullSwap;
                            break;
                        case "2":
                            tieOptions = TieSwapper.TieSwapOptions.PlacementsAndModels;
                            break;
                        case "3":
                            tieOptions = TieSwapper.TieSwapOptions.PlacementsOnly;
                            break;
                        case "4":
                            tieOptions = TieSwapper.TieSwapOptions.None;

                            if (GetYesNoInput("Use RC1 TIE placements? (Y/N): "))
                                tieOptions |= TieSwapper.TieSwapOptions.UseRC1Placements;

                            if (GetYesNoInput("Use RC1 TIE models? (Y/N): "))
                                tieOptions |= TieSwapper.TieSwapOptions.UseRC1Models;

                            if (GetYesNoInput("Map RC1 textures to RC3/UYA? (Y/N): "))
                                tieOptions |= TieSwapper.TieSwapOptions.MapTextures;
                            break;
                        default:
                            tieOptions = TieSwapper.TieSwapOptions.FullSwap;
                            break;
                    }
                }

                // Perform the TIE swap using our new TieSwapper class
                bool success = TieSwapper.SwapTiesWithRC1Oltanis(targetLevel, sourceLevel, tieOptions);

                if (success)
                {
                    Console.WriteLine($"✅ Successfully swapped TIEs from RC1 Oltanis to RC3/UYA level");
                    if (targetLevel.ties != null)
                    {
                        Console.WriteLine($"   Total TIEs in target level: {targetLevel.ties.Count}");
                    }

                    // 🔧 MOVED EARLIER: Rebuild occlusion data immediately after successful TIE swap
                    Console.WriteLine("🔧 Rebuilding TIE occlusion data immediately after swap...");
                    TieCullingDataFixer.DirectCopyTieOcclusion(targetLevel, sourceLevel);
                }
                else
                {
                    Console.WriteLine("❌ Failed to swap TIEs");
                }

                // Apply TIE culling data protection after occlusion rebuild
                if (success && (options & SwapOptions.Ties) != 0)
                {
                    Console.WriteLine("🔧 Applying TIE culling data protection...");

                    // REMOVED: Coordinate conversion - not needed with direct copy approach
                    // The direct copy preserves the original working occlusion patterns
                    // Converting coordinates would recalculate and potentially corrupt this data
                    /*
                    if (targetLevel.game.num == 3 && sourceLevel.game.num != targetLevel.game.num)
                    {
                        TieCullingDataFixer.ApplyCoordinateConversionToTieData(
                            targetLevel,
                            sourceLevel.game.num,
                            targetLevel.game.num
                        );
                    }
                    */

                    Console.WriteLine("✅ TIE culling data protection applied (direct copy, no conversion)");
                }

                // Add TIE diagnostics after TIE swap is complete
                Console.WriteLine("\n==== Running TIE diagnostics after swap ====");

                // Create a diagnostics directory if it doesn't exist
                string tieDiagnosticsDir = Path.Combine(outputDir, "diagnostics");
                Directory.CreateDirectory(tieDiagnosticsDir);

                // Run analysis on the modified target level
                string targetLevelDiagFile = Path.Combine(tieDiagnosticsDir, "target_level_ties_analysis.txt");
                TieDiagnostics.AnalyzeTies(targetLevel, targetLevelDiagFile, "Target_Level_After_Swap");

                // Compare with the RC1 source level
                string comparisonFile = Path.Combine(tieDiagnosticsDir, "rc1_vs_target_ties_comparison.txt");
                TieDiagnostics.CompareTies(sourceLevel, targetLevel, comparisonFile, "RC1_Source", "Target_Level");

                Console.WriteLine($"✅ TIE diagnostics complete. Reports saved to {tieDiagnosticsDir}");

                // Ask if user wants to view the diagnostics summary
                if (GetYesNoInput("\nWould you like to see a brief TIE diagnostics summary? (Y/N): "))
                {
                    try
                    {
                        // Display a brief summary from the comparison report
                        using (var reader = new StreamReader(comparisonFile))
                        {
                            // Skip the header lines
                            for (int i = 0; i < 5; i++) reader.ReadLine();

                            // Display basic statistics and risk assessment
                            Console.WriteLine("\n--- TIE DIAGNOSTICS SUMMARY ---");

                            // Read and display the basic statistics section
                            string line;
                            bool foundRiskSection = false;

                            while ((line = reader.ReadLine()) != null)
                            {
                                // Display basic statistics
                                if (line.Contains("BASIC STATISTICS"))
                                {
                                    Console.WriteLine("\n" + line);
                                    // Print next few lines of statistics
                                    for (int i = 0; i < 5 && (line = reader.ReadLine()) != null; i++)
                                    {
                                        if (!string.IsNullOrWhiteSpace(line))
                                            Console.WriteLine(line);
                                    }
                                }

                                // Display risk assessment when we reach it
                                if (line.Contains("CRASH RISK ASSESSMENT"))
                                {
                                    foundRiskSection = true;
                                    Console.WriteLine("\n" + line);
                                    // Print risk level and recommendations
                                    for (int i = 0; i < 3 && (line = reader.ReadLine()) != null; i++)
                                    {
                                        if (!string.IsNullOrWhiteSpace(line))
                                            Console.WriteLine(line);
                                    }
                                }

                                // Display most important recommendations
                                if (line.Contains("RECOMMENDATIONS"))
                                {
                                    Console.WriteLine("\n" + line);
                                    // Print first few recommendations
                                    for (int i = 0; i < 5 && (line = reader.ReadLine()) != null; i++)
                                    {
                                        if (line.StartsWith("•"))
                                            Console.WriteLine(line);
                                    }
                                }
                            }

                            if (!foundRiskSection)
                            {
                                Console.WriteLine("\nNo crash risk assessment found in the diagnostics report.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error displaying diagnostics summary: {ex.Message}");
                    }

                    Console.WriteLine("\nFull diagnostic reports are available in the diagnostics directory.");
                }
            }
            else
            {
                Console.WriteLine("  TIE swap skipped.");
            }

            // Add shrub swapping here
            SwapShrubsIfRequested(targetLevel, sourceLevel, options);

            // Add skybox swapping here
            SwapSkyboxIfRequested(targetLevel, sourceLevel, rc3DonorLevel, options);

            // Add point lights swapping here
            SwapPointLightsIfRequested(targetLevel, sourceLevel, options);

            // Add sound instance swapping here
            SwapSoundInstancesIfRequested(targetLevel, sourceLevel, options);

            // Add grind path swapping here
            SwapGrindPathsIfRequested(targetLevel, sourceLevel, options);

            // Add moby swapping here
            SwapMobysIfRequested(targetLevel, sourceLevel, referenceRc3PlanetLevel, options);

            // Add Swingshot swapping here
            SwapSwingshotsIfRequested(targetLevel, sourceLevel, options);

            // Add vendor swapping here
            if ((options & SwapOptions.SwapVendorWithOltanis) != 0)
            {
                Console.WriteLine("\n==== Swapping Vendor Model ====");

                // Call the method that will give you all the interactive options
                bool result = VendorOltanisSwapper.SwapVendorWithLoadedLevels(
                    targetLevel,
                    sourceLevel,
                    VendorOltanisSwapper.VendorModelSource.RC1,
                    VendorOltanisSwapper.VendorSwapOptions.Default);

                Console.WriteLine(result ? "✅ Vendor swap complete" : "❌ Vendor swap failed");
            }

            if ((options & SwapOptions.SwapCratesWithOltanis) != 0)
            {
                Console.WriteLine("\n==== Swapping Crates with RC1 Oltanis Crates ====");
                bool result = CratesOltanisSwapper.SwapCratesWithRC1Oltanis(targetLevel, sourceLevel);
                Console.WriteLine(result ? "✅ Crate swap complete" : "❌ Crate swap failed");
            }

            // ----- 3. IMPORT RC1 TEXTURES TO RC3/UYA LEVEL -----
            // 🔧 REMOVED: This is now done immediately after terrain swap
            // if ((options & SwapOptions.Terrain) != 0)
            // {
            //     Console.WriteLine("  Importing RC1 textures to preserve original visual appearance...");
            //     ImportRC1TexturesToRC2Level(targetLevel, sourceLevel);
            // }
            // else
            // {
            //     Console.WriteLine("  Texture import skipped (terrain swap not enabled).");
            // }

            // Add ship exit animation swapping
            if ((options & SwapOptions.SwapShipExitAnim) != 0)
            {
                Console.WriteLine("\n==== Swapping Ship Exit Animation ====");
                bool result = SwapShipExitAnim.SwapAnimation(targetLevel, sourceLevel);
                Console.WriteLine(result ? "✅ Ship exit animation swap complete" : "❌ Ship exit animation swap failed");

                // 🆕 ADD THIS NEW STEP
                Console.WriteLine("\n==== Fixing Ship Camera Matrix Issues ====");
                SwapShipExitAnim.FixAndVerifyShipCameraRotations(outputDir);
            }

            // ----- 4. NO CHANGES TO MODEL LISTS OR TEXTURE INDICES -----
            Console.WriteLine("  Preserving original texture list and model lists from donor level.");

            // ----- 5. NO EMPLACE COMMON DATA -----
            Console.WriteLine("  Skipping EmplaceCommonData() to avoid texture reindexing.");

            // ----- 6. REBUILD ID LISTS FOR HEADERS -----
            // This is needed for serialization to work properly
            if (targetLevel.mobs != null) targetLevel.mobyIds = targetLevel.mobs.Select(m => m.mobyID).ToList();
            if (targetLevel.ties != null) targetLevel.tieIds = targetLevel.ties.Select(t => t.modelID).ToList();
            if (targetLevel.shrubs != null) targetLevel.shrubIds = targetLevel.shrubs.Select(s => s.modelID).ToList();

            Console.WriteLine("🧪 TERRAIN SWAP COMPLETE - Original texture references preserved");

            // ----- 6. REBUILD ID LISTS FOR HEADERS AND ENSURE PROPER PADDING -----
            Console.WriteLine("\n🔄 Finalizing header tables and ensuring proper alignment...");

            // Update the tieIds collection in the header (references from ties to models)
            if (targetLevel.ties != null)
            {
                targetLevel.tieIds = targetLevel.ties.Select(t => t.modelID).ToList();
                Console.WriteLine($"  ✅ Updated tieIds header list with {targetLevel.tieIds.Count} entries");
            }

            // Make sure the tieGroupData ends on an 0x80 boundary for proper alignment
            if (targetLevel.tieGroupData != null && targetLevel.tieGroupData.Length > 0)
            {
                int colorOffset = targetLevel.tieGroupData.Length;
                if (colorOffset % 0x80 != 0)
                {
                    int newSize = ((colorOffset / 0x80) + 1) * 0x80;
                    Array.Resize(ref targetLevel.tieGroupData, newSize);
                    Console.WriteLine($"  ✅ Padded tieGroupData from {colorOffset} to {newSize} bytes for proper alignment");
                }
            }

            Console.WriteLine("✅ Header tables updated and proper alignment ensured");

            // Add planet registration if requested
            if ((options & SwapOptions.RegisterPlanetInMap) != 0)
            {
                Console.WriteLine("\n=== REGISTERING PLANET IN GALACTIC MAP ===");
                
                // Ask for planet details
                Console.Write("Enter planet name to display on the map: ");
                string planetName = Console.ReadLine() ?? "Oltanis";
                
                Console.Write("Enter city name to display on the map: ");
                string cityName = Console.ReadLine() ?? "Gorda City Ruins";
                
                Console.Write("Enter planet/level ID (numeric): ");
                if (!int.TryParse(Console.ReadLine() ?? "27", out int planetId))
                {
                    planetId = 27; // Default to 27 if invalid input
                }
                
                bool isAvailable = GetYesNoInput("Mark planet as available? (Y/N): ");
                
                // Ask if the user wants to update the all_text file
                bool patchAllText = GetYesNoInput("Update the all_text file with planet name? (Y/N): ");
                
                string? gamePath = null;
                if (patchAllText)
                {
                    Console.Write("Enter path to the game's ps3data directory: ");
                    gamePath = Console.ReadLine() ?? globalRc3Dir;
                    
                    // Make sure we have a valid path
                    if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
                    {
                        Console.WriteLine("⚠️ Warning: The provided ps3data path does not exist. Using globalRc2Dir as fallback.");
                        gamePath = globalRc3Dir;
                    }
                }
                
                // Call the planet registration method with the additional parameters
                bool success = GalacticMapManager.AddPlanetToGalacticMap(
                    targetLevel, 
                    planetName, 
                    cityName, 
                    planetId, 
                    isAvailable, 
                    patchAllText,
                    gamePath ?? globalRc3Dir  // Make sure to provide a non-null value
                );
                
                if (success)
                {
                    Console.WriteLine($"✅ Planet '{planetName}' with city '{cityName}' registered successfully with ID {planetId}");
                    Console.WriteLine("\n==== Final pVar Index Validation ====");
                    MobySwapper.ValidateAndFixPvarIndices(targetLevel);
                    if ((options & SwapOptions.CreateMobyInstances) != 0)
                    {
                        Console.WriteLine("\n==== Creating Moby Instances with RC1 Oltanis Positions ====");

                        // Ask for model IDs
                        Console.WriteLine("Enter the model IDs you want to create instances for (comma-separated, e.g. '122,345'):");
                        Console.Write("> ");
                        string input = Console.ReadLine()?.Trim() ?? "";

                        // Parse model IDs
                        List<int> selectedModelIds = new List<int>();
                        foreach (string part in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (int.TryParse(part.Trim(), out int modelId))
                            {
                                selectedModelIds.Add(modelId);
                            }
                        }

                        if (selectedModelIds.Count > 0)
                        {
                            // Ask for options before creating instances
                            Console.WriteLine("\nSelect options for Moby Instancer:");
                            Console.WriteLine("1. Default (light=0, use RC3/UYA template) [default]");
                            Console.WriteLine("2. Custom options");
                            Console.Write("> ");
                            string choice = Console.ReadLine()?.Trim() ?? "1";

                            MobyOltanisInstancer.InstancerOptions instancerOptions;
                            if (choice == "2")
                            {
                                instancerOptions = MobyOltanisInstancer.InstancerOptions.None;

                                if (GetYesNoInput("Set light value to 0? (Y/N): "))
                                    instancerOptions |= MobyOltanisInstancer.InstancerOptions.SetLightToZero;

                                if (GetYesNoInput("Use existing moby as template for properties? (Y/N): "))
                                    instancerOptions |= MobyOltanisInstancer.InstancerOptions.UseRC2Template;

                                if (GetYesNoInput("Copy pVars from source level mobys? (Y/N): "))
                                    instancerOptions |= MobyOltanisInstancer.InstancerOptions.CopyPvars;
                            }
                            else
                            {
                                instancerOptions = MobyOltanisInstancer.InstancerOptions.Default;
                            }

                            bool result = MobyOltanisInstancer.CreateMobyInstancesFromLevel(
                                targetLevel,
                                sourceLevel,
                                selectedModelIds.ToArray(),
                                instancerOptions);

                            Console.WriteLine(result ? "✅ Moby instances created successfully" : "❌ Failed to create moby instances");
                        }
                        else
                        {
                            Console.WriteLine("❌ No valid model IDs provided");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Failed to register planet '{planetName}' with city '{cityName}'");
                }
            }

            // Add cuboid import handling
            if ((options & SwapOptions.ImportCuboids) != 0)
            {
                Console.WriteLine("\n==== Importing RC1 Cuboids ====");
                bool success = CuboidImporter.ImportCuboidsFromRC1(targetLevel, sourceLevel, CuboidImporter.CuboidImportOptions.Default);
                Console.WriteLine(success ? "✅ Cuboid import complete" : "❌ Cuboid import failed");
            }

            // Add spline import handling - ADD THIS AFTER THE CUBOID IMPORT SECTION
            if ((options & SwapOptions.ImportSplines) != 0)
            {
                Console.WriteLine("\n==== Importing RC1 Splines ====");
                bool success = SplineImporter.ImportSplinesFromRC1(targetLevel, sourceLevel, SplineImporter.SplineImportOptions.Default);
                Console.WriteLine(success ? "✅ Spline import complete" : "❌ Spline import failed");
            }

            // 🆕 NEW: Animation Restoration handling
            if ((options & SwapOptions.RestoreAnimations) != 0)
            {
                Console.WriteLine("\n==== Restoring Animations ====");

                // 🆕 QoL IMPROVEMENT 1: Automatically determine animation file paths based on global directory
                // 🔧 FIX: Use the correct target game parameter instead of targetLevel.game.num
                string animationSourcePath = DetermineAnimationSourcePath(
                    targetGameSelected == 1 ? globalRc1Dir :
                    targetGameSelected == 2 ? globalRc2Dir : globalRc3Dir,
                    targetGameSelected);

                if (!string.IsNullOrEmpty(animationSourcePath) && File.Exists(animationSourcePath))
                {
                    Console.WriteLine($"🎯 Auto-detected animation source: {animationSourcePath}");

                    try
                    {
                        // Load the animation source level
                        Level animationSourceLevel = new Level(animationSourcePath);
                        Console.WriteLine($"✅ Animation source level loaded: {animationSourceLevel.game.Name}");

                        // 🆕 QoL IMPROVEMENT 2: Default to restore all missing animations for custom option
                        var customAnimationOptions = AnimationRestoreOptions.AllMobyAnimations |
                                                   AnimationRestoreOptions.PlayerAnimations |
                                                   AnimationRestoreOptions.RatchetModelAnimations |
                                                   AnimationRestoreOptions.PreserveTargetAnimations;

                        bool success = AnimationRestorer.RestoreAnimationsFromLoadedLevels(
                            targetLevel,
                            sourceLevel,
                            animationSourceLevel,
                            customAnimationOptions);

                        Console.WriteLine(success ? "✅ Animation restoration complete" : "❌ Animation restoration failed");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error loading animation source level: {ex.Message}");
                        Console.WriteLine("Falling back to standard animation restoration...");

                        // Fall back to the original method
                        bool success = GeometrySwapper.AnimationRestorer.RestoreAnimationsFromLoadedLevels(targetLevel, sourceLevel, rc3DonorLevel);
                        Console.WriteLine(success ? "✅ Animation restoration complete" : "❌ Animation restoration failed");
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ Could not auto-detect animation source, using donor level...");

                    // Fall back to the original method
                    bool success = GeometrySwapper.AnimationRestorer.RestoreAnimationsFromLoadedLevels(targetLevel, sourceLevel, rc3DonorLevel);
                    Console.WriteLine(success ? "✅ Animation restoration complete" : "❌ Animation restoration failed");
                }
            }

            // 🆕 NEW: Fix Ship Path Index by spline geometry matching - AFTER EVERYTHING IS DONE BUT BEFORE SAVING
            Console.WriteLine("\n==== Final Ship Path Index Geometry Fix ====");
            bool shipPathIndexFix = SwapShipExitAnim.FixShipPathIndexByGeometry(targetLevel, sourceLevel);
            Console.WriteLine(shipPathIndexFix ? "✅ Final ship path index geometry fix complete" : "❌ Ship path index geometry fix failed (this is normal if no ship animation was swapped)");
        }

        static void SwapMobysIfRequested(Level targetLevel, Level sourceLevel, Level referenceRc3PlanetLevel, SwapOptions options)
        {
            if ((options & SwapOptions.Mobys) != 0)
            {
                Console.WriteLine("\n==== Copying Special Mobys ====");

                if (referenceRc3PlanetLevel == null)
                {
                    Console.WriteLine("❌ Reference RC3/UYA planet (Damosel) is required but not loaded");
                    return;
                }

                if (sourceLevel == null)
                {
                    Console.WriteLine("❌ RC1 source level (Oltanis) is required but not loaded");
                    return;
                }

                // Ask user which approach they want to use
                Console.WriteLine("\nSelect moby transfer method:");
                Console.WriteLine("1. Copy RC1 mobys (traditional - may crash in-game)");
                Console.WriteLine("2. Copy RC3/UYA mobys with RC1 models (all selected types)");
                Console.WriteLine("3. Copy RC3/UYA mobys with selective model replacement (recommended)");
                Console.WriteLine("4. Cancel");
                Console.Write("> ");

                string choice = Console.ReadLine()?.Trim() ?? "";

                if (choice == "1")
                {
                    Console.WriteLine("\nCopying specific mobys from RC1 level...");

                    // Ask user which moby types to copy
                    List<string> mobyTypes = GetSelectedMobyTypes();

                    if (mobyTypes.Count > 0)
                    {
                        bool result = MobySwapper.CopyMobysToLevel(targetLevel, sourceLevel, true, mobyTypes);
                        Console.WriteLine(result ? "✅ Mobys copied successfully" : "❌ Failed to copy mobys");
                    }
                    else
                    {
                        Console.WriteLine("❌ No moby types selected, skipping");
                    }
                }
                else if (choice == "2")
                {
                    Console.WriteLine("\nCopying RC3/UYA mobys with RC1 models...");

                    // Ask user which moby types to replace with RC1 models
                    List<string> mobyTypes = GetSelectedMobyTypes();

                    if (mobyTypes.Count > 0)
                    {
                        bool result = MobySwapper.CopyRC2MobysWithRC1Models(
                            targetLevel,
                            referenceRc3PlanetLevel,
                            sourceLevel,
                            mobyTypes);

                        Console.WriteLine(result ? "✅ Mobys copied successfully with RC1 models" : "❌ Failed to copy mobys");
                    }
                    else
                    {
                        Console.WriteLine("❌ No moby types selected, skipping");
                    }
                }
                else if (choice == "3")
                {
                    Console.WriteLine("\nCopying RC3/UYA mobys with selective model replacement...");

                    // First, ask which moby types to include
                    List<string> selectedMobyTypes = GetSelectedMobyTypes();

                    if (selectedMobyTypes.Count == 0)
                    {
                        Console.WriteLine("❌ No moby types selected, skipping");
                        return;
                    }

                    // Dictionary to track which moby types should use RC1 models
                    Dictionary<string, bool> modelSourcePreference = new Dictionary<string, bool>();

                    Console.WriteLine("\nFor each selected moby type, choose whether to use RC1 or RC3/UYA models:");

                    foreach (string mobyType in selectedMobyTypes)
                    {
                        // Display moby info
                        int[] modelIds = MobySwapper.MobyTypes[mobyType];
                        Console.WriteLine($"\n{mobyType} (Model IDs: {string.Join(", ", modelIds)})");

                        Console.Write("Use RC1 model for this moby type? (Y/N, default: Y): ");
                        string useRc1 = Console.ReadLine()?.Trim().ToUpper() ?? "Y";

                        // If Y or empty, use RC1 model; otherwise use RC3/UYA model
                        modelSourcePreference[mobyType] = (useRc1 == "Y" || useRc1 == "");

                        Console.WriteLine($"Selected: {(modelSourcePreference[mobyType] ? "RC1 model" : "RC3/UYA model")} for {mobyType}");
                    }

                    // Get only the moby types that should use RC1 models
                    List<string> rc1ModelTypes = modelSourcePreference
                        .Where(kvp => kvp.Value)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    // Get only the moby types that should use RC3/UYA models
                    List<string> rc2ModelTypes = modelSourcePreference
                        .Where(kvp => !kvp.Value)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    Console.WriteLine("\nProcessing moby copy with selective model replacement...");

                    // Copy RC3/UYA mobys with RC1 models for the selected types
                    if (rc1ModelTypes.Count > 0)
                    {
                        Console.WriteLine($"\nCopying {rc1ModelTypes.Count} moby types with RC1 models: {string.Join(", ", rc1ModelTypes)}");
                        bool rc1Result = MobySwapper.CopyRC2MobysWithRC1Models(
                            targetLevel,
                            referenceRc3PlanetLevel,
                            sourceLevel,
                            rc1ModelTypes);

                        Console.WriteLine(rc1Result ? "✅ Mobys with RC1 models copied successfully" : "❌ Failed to copy mobys with RC1 models");
                    }

                    // Copy RC3/UYA mobys with their original RC3/UYA models for the remaining types
                    if (rc2ModelTypes.Count > 0)
                    {
                        Console.WriteLine($"\nCopying {rc2ModelTypes.Count} moby types with original RC3/UYA models: {string.Join(", ", rc2ModelTypes)}");

                        // Pass sourceLevel (Oltanis) as the position reference
                        bool rc2Result = MobySwapper.CopyRC2MobysPreservingModels(
                            targetLevel,
                            referenceRc3PlanetLevel,
                            sourceLevel,  // Pass RC1 level for position data
                            rc2ModelTypes);

                        Console.WriteLine(rc2Result ? "✅ Mobys with RC3/UYA models copied successfully" : "❌ Failed to copy mobys with RC3/UYA models");
                    }

                    Console.WriteLine("\n✅ Selective model replacement completed");
                }
                else
                {
                    Console.WriteLine("⏭️ Moby copying skipped");
                }

                // Always ensure vendor support models are available, regardless of the option chosen above
                Console.WriteLine("\nEnsuring required vendor support models are available...");
                MobySwapper.ImportVendorSupportModels(targetLevel, referenceRc3PlanetLevel);
                Console.WriteLine("✅ Vendor support models checked");
            }
            else
            {
                Console.WriteLine("⏭️ Moby operations skipped (not enabled in options)");
            }

            Console.WriteLine("\n==== Fixing Swingshot Node pVars ====");
            MobySwapper.FixSwingshotNodePvars(targetLevel, sourceLevel, MobySwapper.MobyTypes["SwingshotNode"]);

            Console.WriteLine("\n==== Validating pVar Indices ====");
            MobySwapper.ValidateAndFixPvarIndices(targetLevel);
        }

        static void SwapSwingshotsIfRequested(Level targetLevel, Level sourceLevel, SwapOptions options)
        {
            if (options.HasFlag(SwapOptions.SwapSwingshots))
            {
                Console.WriteLine("\n=== Swapping Swingshots ===");

                // Use default options for the swingshot swapper
                bool success = SwingshotOltanisSwapper.SwapSwingshotsWithRC1Oltanis(
                    targetLevel,
                    sourceLevel,
                    SwingshotOltanisSwapper.SwingshotSwapOptions.Default);

                if (success)
                    Console.WriteLine("✅ Successfully swapped swingshots");
                else
                    Console.WriteLine("❌ Failed to swap swingshots");
            }
        }

        static void SwapShrubsIfRequested(Level targetLevel, Level sourceLevel, SwapOptions options)
        {
            if ((options & SwapOptions.Shrubs) != 0)
            {
                Console.WriteLine("\n==== Swapping Shrubs with RC1 Oltanis Shrubs ====");

                try
                {
                    // Ask for the specific options to use
                    Console.WriteLine("\nSelect shrub swap method:");
                    Console.WriteLine("1. Full replacement (RC1 models, positions, AND textures)");
                    Console.WriteLine("2. RC1 models and positions (no texture mapping)");
                    Console.WriteLine("3. Positions only (keep RC3/UYA models but use RC1 positions)");
                    Console.WriteLine("4. Custom options");
                    Console.Write("> ");

                    string choice = Console.ReadLine()?.Trim() ?? "1";
                    ShrubSwapper.ShrubSwapOptions shrubOptions;

                    switch (choice)
                    {
                        case "1":
                            shrubOptions = ShrubSwapper.ShrubSwapOptions.FullSwap;
                            break;
                        case "2":
                            shrubOptions = ShrubSwapper.ShrubSwapOptions.PlacementsAndModels;
                            break;
                        case "3":
                            shrubOptions = ShrubSwapper.ShrubSwapOptions.PlacementsOnly;
                            break;
                        case "4":
                            shrubOptions = ShrubSwapper.ShrubSwapOptions.None;

                            if (GetYesNoInput("Use RC1 shrub placements? (Y/N): "))
                                shrubOptions |= ShrubSwapper.ShrubSwapOptions.UseRC1Placements;

                            if (GetYesNoInput("Use RC1 shrub models? (Y/N): "))
                                shrubOptions |= ShrubSwapper.ShrubSwapOptions.UseRC1Models;

                            if (GetYesNoInput("Map RC1 textures to RC3/UYA? (Y/N): "))
                                shrubOptions |= ShrubSwapper.ShrubSwapOptions.MapTextures;
                            break;
                        default:
                            shrubOptions = ShrubSwapper.ShrubSwapOptions.FullSwap;
                            break;
                    }

                    // Pre-swap diagnostics
                    Console.WriteLine("\n--- PRE-SWAP DIAGNOSTICS ---");
                    Console.WriteLine($"Target level has {targetLevel.shrubs?.Count ?? 0} shrubs");
                    Console.WriteLine($"RC1 source level has {sourceLevel.shrubs?.Count ?? 0} shrubs");
                    Console.WriteLine($"Target level has {targetLevel.shrubModels?.Count ?? 0} shrub models");

                    // Ensure collections are initialized
                    if (targetLevel.shrubs == null)
                        targetLevel.shrubs = new List<Shrub>();

                    if (targetLevel.shrubModels == null)
                        targetLevel.shrubModels = new List<Model>();

                    // Call the non-interactive version directly with already-loaded levels
                    bool success = ShrubSwapper.SwapShrubsWithRC1Oltanis(
                        targetLevel,
                        sourceLevel,
                        shrubOptions
                    );

                    // Additional checks after the swap
                    if (success)
                    {
                        // Final validation
                        Console.WriteLine("\n--- POST-SWAP VALIDATION ---");
                        Console.WriteLine($"Target level now has {targetLevel.shrubs.Count} shrubs");
                        Console.WriteLine($"Target level now has {targetLevel.shrubModels.Count} shrub models");

                        // Update shrub IDs list
                        if (targetLevel.shrubs != null)
                        {
                            targetLevel.shrubIds = targetLevel.shrubs.Select(s => s.modelID).ToList();
                            Console.WriteLine($"✅ Updated shrubIds list with {targetLevel.shrubIds.Count} entries");
                        }

                        Console.WriteLine("✅ Successfully transferred shrubs from RC1 to RC3/UYA level");
                    }
                    else
                    {
                        Console.WriteLine("❌ Failed to transfer shrubs");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Critical error in shrub swapping: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        static List<string> GetSelectedMobyTypes()
        {
            List<string> allMobyTypes = MobySwapper.MobyTypes.Keys.ToList();
            List<string> selectedTypes = new List<string>();

            Console.WriteLine("\nAvailable moby types:");

            for (int i = 0; i < allMobyTypes.Count; i++)
            {
                string mobyType = allMobyTypes[i];
                int[] modelIds = MobySwapper.MobyTypes[mobyType];
                Console.WriteLine($"{i + 1}. {mobyType} (Model IDs: {string.Join(", ", modelIds)})");
            }

            Console.WriteLine($"{allMobyTypes.Count + 1}. All types");
            Console.WriteLine($"{allMobyTypes.Count + 2}. None");

            Console.Write("\nEnter numbers of moby types to include (comma-separated, e.g. '1,3,5'): ");
            string input = Console.ReadLine()?.Trim() ?? "";

            if (input == (allMobyTypes.Count + 1).ToString())
            {
                // User selected "All types"
                return allMobyTypes;
            }
            else if (input == (allMobyTypes.Count + 2).ToString() || string.IsNullOrWhiteSpace(input))
            {
                // User selected "None" or gave empty input
                return new List<string>();
            }

            // Parse the comma-separated list of numbers
            string[] parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (string part in parts)
            {
                if (int.TryParse(part, out int index) && index >= 1 && index <= allMobyTypes.Count)
                {
                    selectedTypes.Add(allMobyTypes[index - 1]);
                }
            }

            Console.WriteLine($"\nSelected moby types: {string.Join(", ", selectedTypes)}");
            return selectedTypes;
        }

        static void SwapSkyboxIfRequested(Level targetLevel, Level sourceLevel, Level rc3DonorLevel, SwapOptions options)
        {
            if ((options & SwapOptions.Skybox) != 0)
            {
                Console.WriteLine("\n==== Swapping Skybox ====");
                
                if (sourceLevel.skybox == null)
                {
                    Console.WriteLine("❌ The RC1 source level doesn't have a skybox to transfer");
                    return;
                }
                
                if (targetLevel.skybox == null && rc3DonorLevel.skybox == null)
                {
                    Console.WriteLine("❌ Neither the target level nor the RC3/UYA donor has a skybox structure to use as reference");
                    return;
                }
                
                bool success = SkyboxSwapper.SwapSkybox(targetLevel, sourceLevel, rc3DonorLevel);
                
                if (success)
                {
                    Console.WriteLine("✅ Successfully swapped skybox from RC1 source to RC3/UYA level");
                    
                    // If we have vertex data in the skybox, display some information about it
                    if (targetLevel.skybox != null && targetLevel.skybox.vertexBuffer != null)
                    {
                        int vertexCount = targetLevel.skybox.vertexBuffer.Length / SkyboxModel.VERTELEMSIZE;
                        Console.WriteLine($"  Skybox has {vertexCount} vertices and {targetLevel.skybox.textureConfig?.Count ?? 0} texture configs");
                    }
                }
                else
                {
                    Console.WriteLine("❌ Failed to swap skybox");
                }
            }
        }

        static void RunCollisionDiagnosticsInteractive()
        {
            Console.WriteLine("\n==== Collision Comparison Diagnostics ====");
            Console.WriteLine("Enter path to first RCC file:");
            Console.Write("> ");
            string rccAPath = Console.ReadLine()?.Trim() ?? "";

            Console.WriteLine("Enter path to second RCC file:");
            Console.Write("> ");
            string rccBPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(rccAPath) || !File.Exists(rccBPath))
            {
                Console.WriteLine("❌ One or both RCC files not found. Please check the paths.");
                return;
            }

            Console.WriteLine("Enter output directory for diagnostics report:");
            Console.Write("> ");
            string outputDir = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(outputDir))
                outputDir = Path.Combine(Path.GetDirectoryName(rccAPath) ?? "", "diagnostics");

            Directory.CreateDirectory(outputDir);

            Console.WriteLine("Enter label for first RCC file:");
            Console.Write("> ");
            string labelA = Console.ReadLine()?.Trim() ?? "RCC_A";

            Console.WriteLine("Enter label for second RCC file:");
            Console.Write("> ");
            string labelB = Console.ReadLine()?.Trim() ?? "RCC_B";

            Console.WriteLine("Running collision comparison diagnostics...");
            GeometrySwapper.CollisionDiagnostics.CompareRccFiles(rccAPath, rccBPath, outputDir, labelA, labelB);
            Console.WriteLine($"✅ Diagnostics report generated at: {outputDir}");
        }

        static void RunRccToObjConversionInteractive()
        {
            Console.WriteLine("\n==== RCC to OBJ Conversion ====");
            Console.WriteLine("Enter path to RCC file:");
            Console.Write("> ");
            string rccPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(rccPath))
            {
                Console.WriteLine("❌ RCC file not found. Please check the path.");
                return;
            }

            Console.WriteLine("Enter destination OBJ path (leave blank for same name):");
            Console.Write("> ");
            string objPath = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(objPath))
                objPath = Path.ChangeExtension(rccPath, ".obj");

            GeometrySwapper.CollisionDiagnostics.ConvertRccToObj(rccPath, objPath);
        }

        static void RunObjToRccConversionInteractive()
        {
            Console.WriteLine("\n==== OBJ to RCC Conversion ====");
            Console.WriteLine("Enter path to OBJ file:");
            Console.Write("> ");
            string objPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(objPath))
            {
                Console.WriteLine("❌ OBJ file not found. Please check the path.");
                return;
            }

            Console.WriteLine("Enter destination RCC path (leave blank for same name):");
            Console.Write("> ");
            string rccPath = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(rccPath))
                rccPath = Path.ChangeExtension(objPath, ".rcc");

            GeometrySwapper.CollisionDiagnostics.ConvertObjToRcc(objPath, rccPath);
        }

        static void SwapGrindPathsIfRequested(Level targetLevel, Level sourceLevel, SwapOptions options)
        {
            if ((options & SwapOptions.GrindPaths) != 0)
            {
                Console.WriteLine("\n==== Swapping Grind Paths ====");

                try
                {
                    // Ask for the specific options to use
                    Console.WriteLine("\nSelect grind path swap method:");
                    Console.WriteLine("1. Reposition existing paths only");
                    Console.WriteLine("2. Full replacement (reposition + add missing paths)");
                    Console.Write("> ");

                    string choice = Console.ReadLine()?.Trim() ?? "1";
                    GrindPathSwapper.GrindPathSwapOptions pathOptions;

                    if (choice == "1")
                        pathOptions = GrindPathSwapper.GrindPathSwapOptions.UseRC1Positions;
                    else
                        pathOptions = GrindPathSwapper.GrindPathSwapOptions.FullReplacement;

                    // Pre-swap diagnostics
                    Console.WriteLine("\n--- PRE-SWAP DIAGNOSTICS ---");
                    Console.WriteLine($"Target level has {targetLevel.grindPaths?.Count ?? 0} grind paths");
                    Console.WriteLine($"RC1 source level has {sourceLevel.grindPaths?.Count ?? 0} grind paths");
                    Console.WriteLine($"Target level has {targetLevel.splines?.Count ?? 0} splines");

                    // Ensure collections are initialized
                    if (targetLevel.grindPaths == null)
                        targetLevel.grindPaths = new List<GrindPath>();

                    if (targetLevel.splines == null)
                        targetLevel.splines = new List<Spline>();

                    // Call the non-interactive version directly with already-loaded levels
                    bool success = GrindPathSwapper.SwapGrindPathsWithRC1Oltanis(
                        targetLevel,
                        sourceLevel,
                        pathOptions
                    );

                    // Additional checks after the swap
                    if (success)
                    {
                        // Final validation
                        Console.WriteLine("\n--- POST-SWAP VALIDATION ---");
                        Console.WriteLine($"Target level now has {targetLevel.grindPaths.Count} grind paths");
                        Console.WriteLine($"Target level now has {targetLevel.splines.Count} splines");

                        // Check for duplicate spline IDs
                        var duplicateIds = targetLevel.splines
                            .GroupBy(s => s.id)
                            .Where(g => g.Count() > 1)
                            .ToList();

                        if (duplicateIds.Any())
                        {
                            Console.WriteLine($"⚠️ Found {duplicateIds.Count} duplicate spline ID groups. These will be fixed during save.");
                        }

                        Console.WriteLine($"✅ Successfully transferred grind paths from RC1 to RC3/UYA level");

                        // Report on a few paths if available
                        if (targetLevel.grindPaths.Count > 0)
                        {
                            Console.WriteLine("   Sample of paths in target level:");
                            int sampleSize = Math.Min(3, targetLevel.grindPaths.Count);
                            for (int i = 0; i < sampleSize; i++)
                            {
                                var path = targetLevel.grindPaths[i];
                                Console.WriteLine($"     Path {i}: Position={path.position}, Radius={path.radius}, " +
                                                $"Spline points={(path.spline?.GetVertexCount() ?? 0)}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("❌ Failed to transfer grind paths");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Critical error in grind path swapping: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        static void SwapSoundInstancesIfRequested(Level targetLevel, Level sourceLevel, SwapOptions options)
        {
            if ((options & SwapOptions.SoundInstances) != 0)
            {
                Console.WriteLine("\n==== Swapping Sound Instances ====");
                
                if (sourceLevel.soundInstances == null || sourceLevel.soundInstances.Count == 0)
                {
                    Console.WriteLine("❌ The RC1 source level doesn't have any sound instances to transfer");
                    return;
                }
                
                bool success = SoundInstanceSwapper.SwapSoundInstances(targetLevel, sourceLevel);
                
                if (success)
                {
                    Console.WriteLine($"✅ Successfully transferred {sourceLevel.soundInstances.Count} sound instances from RC1 to RC3/UYA level");
                }
                else
                {
                    Console.WriteLine("❌ Failed to transfer sound instances");
                }
            }
        }

        static void SwapPointLightsIfRequested(Level targetLevel, Level sourceLevel, SwapOptions options)
        {
            if ((options & SwapOptions.PointLights) != 0)
            {
                Console.WriteLine("\n==== Swapping Point Lights with RC1 Oltanis ====");
                bool success = PointLightsSwapper.SwapPointLights(targetLevel, sourceLevel);
                Console.WriteLine(success ? "✅ Point lights swap complete" : "❌ Point lights swap failed");

                // Additionally, update Ratchet's light value to 0 to match RC1 Oltanis
                UpdateRatchetLightValue(targetLevel);
            }
        }

        static void UpdateRatchetLightValue(Level targetLevel)
        {
            // Find Ratchet moby - typically has mobyID 179 in RC3/UYA
            var ratchet = targetLevel.mobs?.FirstOrDefault(m => m.mobyID == 179);

            if (ratchet != null)
            {
                // Store original light value for logging
                int originalLight = ratchet.light;

                // Set light to 0 to match RC1 Oltanis
                ratchet.light = 0;

                Console.WriteLine($"✅ Updated Ratchet's light value from {originalLight} to 0");
            }
            else
            {
                Console.WriteLine("⚠️ Could not find Ratchet moby (ID 179) to update light value");
            }
        }

        static SwapOptions DisplayMenuAndGetOptions()
        {
            Console.WriteLine("\n=== R&C1 to R&C3/UYA Geometry Swap Options ===");
            Console.WriteLine("1. Swap Terrain/Geometry ONLY");
            Console.WriteLine("2. Swap Collision ONLY");
            Console.WriteLine("3. Swap TIEs ONLY");
            Console.WriteLine("4. Swap Shrubs ONLY");
            Console.WriteLine("5. Swap Skybox ONLY");
            Console.WriteLine("6. Swap Grind Paths ONLY");
            Console.WriteLine("7. Swap Point Lights ONLY");
            Console.WriteLine("8. Swap Sound Instances ONLY");
            Console.WriteLine("9. Copy Special Mobys ONLY");
            Console.WriteLine("10. Import Common Mobys from Reference RC3/UYA Levels");
            Console.WriteLine("11. ALL of the above (except 10)");
            Console.WriteLine("12. Swap Level Variables ONLY (Background/Fog Color, Ship Position/Rotation)");
            Console.WriteLine("13. Transfer Ratchet Position ONLY");
            Console.WriteLine("14. Register Planet in Galactic Map ONLY");
            Console.WriteLine("15. Swap Vendor with RC1 Oltanis Vendor ONLY");
            Console.WriteLine("16. Swap Crates with RC1 Oltanis Crates ONLY");
            Console.WriteLine("17. Run TIE Diagnostics ONLY");
            Console.WriteLine("18. Run Grind Path Diagnostics ONLY");
            Console.WriteLine("19. Swap Swingshots with RC1 Oltanis ONLY");
            Console.WriteLine("20. Moby Import Tool");
            Console.WriteLine("21. Moby Export Tool");
            Console.WriteLine("22. Convert RC1 Mobys to RC3/UYA Mobys!");
            Console.WriteLine("23. Create Moby Instances with RC1 Positions");
            Console.WriteLine("24. Swap Ship Exit Animation ONLY");
            Console.WriteLine("25. Import RC1 Cuboids ONLY");
            Console.WriteLine("26. Import RC1 Splines ONLY");
            Console.WriteLine("27. Run Ship Camera Diagnostics ONLY");
            Console.WriteLine("28. Animation Restoration Tool");
            Console.WriteLine("29. Custom Selection");
            Console.WriteLine("30. Convert ToD/ACiT Level to UYA (EXPERIMENTAL)");
            Console.WriteLine("31. Run Terrain Comparison Diagnostics (Compare two levels)");
            Console.WriteLine("32. Run Collision Comparison Diagnostics (Compare two .rcc files)");
            Console.WriteLine("33. Convert RCC Collision to OBJ");
            Console.WriteLine("34. Convert OBJ Collision to RCC");
            Console.WriteLine("35. Run Occlusion Diagnostics ONLY");
            Console.Write("\nEnter your choice (1-35): ");

            string input = Console.ReadLine() ?? "11"; // Default to ALL if empty
            if (!int.TryParse(input.Trim(), out int choice))
                choice = 11; // Default to ALL if invalid input

            SwapOptions options;
            switch (choice)
            {
                case 1: options = SwapOptions.Terrain; break;
                case 2: options = SwapOptions.Collision; break;
                case 3: options = SwapOptions.Ties; break;
                case 4: options = SwapOptions.Shrubs; break;
                case 5: options = SwapOptions.Skybox; break;
                case 6: options = SwapOptions.GrindPaths; break;
                case 7: options = SwapOptions.PointLights; break;
                case 8: options = SwapOptions.SoundInstances; break;
                case 9: options = SwapOptions.Mobys; break;
                case 10: // Import Common Mobys - This is a special case
                    ImportCommonMobysFromUser();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 11: options = SwapOptions.All; break;
                case 12: options = SwapOptions.SwapLevelVariables; break;
                case 13: options = SwapOptions.TransferRatchetPosition; break;
                case 14: options = SwapOptions.RegisterPlanetInMap; break;
                case 15: options = SwapOptions.SwapVendorWithOltanis; break;
                case 16: options = SwapOptions.SwapCratesWithOltanis; break;
                case 17: // Run TIE Diagnostics
                    RunTieDiagnosticsOnly();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 18: // Run Grind Path Diagnostics - Added this case
                    RunGrindPathDiagnosticsOnly();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 19:
                    options = SwapOptions.SwapSwingshots; break;
                case 20:
                    GeometrySwapper.MobyExporter.ImportMobysInteractive();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 21:
                    GeometrySwapper.MobyExporter.ExportMobysInteractive();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 22: // Convert RC1 Mobys to RC3/UYA Mobys
                    RunMobyConverter();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 23: // Create Moby Instances - This is the new option
                    Console.WriteLine("MobyOltanisInstancer.CreateMobyInstancesInteractiveEnhanced is not implemented.");
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 24: options = SwapOptions.SwapShipExitAnim; break;
                case 25: // Import RC1 Cuboids - Add this new case
                    CuboidImporter.ImportCuboidsInteractive();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 26:
                    SplineImporter.ImportSplinesInteractive();
                    return DisplayMenuAndGetOptions();
                case 27: // Run Ship Camera Diagnostics - Add this new case
                    ShipCameraDiagnostics.RunShipCameraDiagnosticsInteractive();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 28: // 🆕 NEW: Animation Restoration Tool
                    AnimationRestorer.AnalyzeAndRestoreAnimationsInteractive();
                    return DisplayMenuAndGetOptions();
                case 29: options = GetCustomOptions(); break;
                case 30:
                    RunFutureToUyaConverter();
                    return DisplayMenuAndGetOptions();
                case 31:
                    RunTerrainDiagnosticsInteractive();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 32:
                    RunCollisionDiagnosticsInteractive();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 33:
                    RunRccToObjConversionInteractive();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 34:
                    RunObjToRccConversionInteractive();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                case 35: // Run Occlusion Diagnostics
                    RunOcclusionDiagnosticsOnly();
                    return DisplayMenuAndGetOptions(); // Return to menu after completion
                default: options = SwapOptions.All; break;
            }

            return options;
        }

        static void RunTerrainDiagnosticsInteractive()
        {
            Console.WriteLine("\n==== Terrain Comparison Diagnostics ====");
            Console.WriteLine("Enter path to first level (e.g., output level):");
            Console.Write("> ");
            string levelAPath = Console.ReadLine()?.Trim() ?? "";

            Console.WriteLine("Enter path to second level (e.g., reference or donor level):");
            Console.Write("> ");
            string levelBPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(levelAPath) || !File.Exists(levelBPath))
            {
                Console.WriteLine("❌ One or both level files not found. Please check the paths.");
                return;
            }

            Level levelA, levelB;
            try
            {
                levelA = new Level(levelAPath);
                levelB = new Level(levelBPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading levels: {ex.Message}");
                return;
            }

            Console.WriteLine("Enter output directory for diagnostics report:");
            Console.Write("> ");
            string outputDir = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(outputDir))
                outputDir = Path.Combine(Path.GetDirectoryName(levelAPath) ?? "", "diagnostics");

            Directory.CreateDirectory(outputDir);

            Console.WriteLine("Enter label for first level:");
            Console.Write("> ");
            string labelA = Console.ReadLine()?.Trim() ?? "LevelA";

            Console.WriteLine("Enter label for second level:");
            Console.Write("> ");
            string labelB = Console.ReadLine()?.Trim() ?? "LevelB";

            Console.WriteLine("Running terrain comparison diagnostics...");
            TerrainDiagnostics.CompareLevels(levelA, levelB, outputDir, labelA, labelB);
            Console.WriteLine($"✅ Diagnostics report generated at: {outputDir}");
        }

        static void RunMobyConverter()
        {
            Console.WriteLine("\n==== RC1 to RC3/UYA Moby Converter ====");

            // Get input file path
            Console.WriteLine("Enter path to the RC1 level engine.ps3 file:");
            Console.Write("> ");
            string enginePath = Console.ReadLine()?.Trim() ?? "";

            // Get output directory
            Console.WriteLine("\nEnter output directory for converted mobys:");
            Console.Write("> ");
            string outputDir = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.Combine(Path.GetDirectoryName(enginePath) ?? "", "converted_mobys");
            }

            // Ensure output directory exists
            Directory.CreateDirectory(outputDir);

            Console.WriteLine($"\nConverting mobys from {enginePath} to {outputDir}...");

            try
            {
                // Call the MobyConverter's Main method with the arguments
                string[] converterArgs = { enginePath, outputDir };
                MobyConverter.Program.RunMobyConverter(converterArgs);

                Console.WriteLine($"\n✅ Moby conversion complete. Converted mobys saved to {outputDir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during moby conversion: {ex.Message}");
            }

            Console.WriteLine("\nPress Enter to return to main menu...");
            Console.ReadLine();
        }

        static void ImportCommonMobysFromUser()
        {
            Console.WriteLine("\n==== Import Common Mobys from Reference RC3/UYA Levels ====");

            // Ask for target level path (default to existing RC3/UYA donor level if available)
            Console.WriteLine("\nEnter path to the target level engine.ps3 file (or leave empty to use current RC3/UYA donor level):");
            Console.Write("> ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            // If no path provided, use the RC3/UYA donor level path
            if (string.IsNullOrEmpty(targetPath))
            {
                targetPath = Path.Combine(rc3DonorLevelDir, "engine.ps3");
                Console.WriteLine($"Using RC3/UYA donor level: {targetPath}");
            }

            if (!File.Exists(targetPath))
            {
                Console.WriteLine("❌ Invalid target level path. Returning to main menu.");
                return;
            }

            // Get reference level paths
            List<string> referenceEnginePaths = new List<string>();

            Console.WriteLine("\nHow many reference RC3/UYA levels do you want to analyze? (2-5)");
            Console.Write("> ");

            if (!int.TryParse(Console.ReadLine()?.Trim() ?? "2", out int referenceCount))
            {
                referenceCount = 2;
            }

            referenceCount = Math.Max(2, Math.Min(5, referenceCount)); // Clamp between 2 and 5

            Console.WriteLine($"\nEnter paths to {referenceCount} reference RC3/UYA engine.ps3 files:");

            // Default to standard RC3/UYA level and the reference GC planet as first two options
            string defaultReferenceLevel = Path.Combine(referenceUyaPlanetDir, "engine.ps3");

            for (int i = 0; i < referenceCount; i++)
            {
                string defaultPrompt = "";
                if (i == 0 && File.Exists(defaultReferenceLevel))
                {
                    defaultPrompt = $" (or leave empty to use {Path.GetFileName(defaultReferenceLevel)})";
                }

                Console.Write($"Reference #{i + 1}{defaultPrompt}> ");
                string path = Console.ReadLine()?.Trim() ?? "";

                if (string.IsNullOrEmpty(path) && i == 0 && File.Exists(defaultReferenceLevel))
                {
                    path = defaultReferenceLevel;
                    Console.WriteLine($"Using default reference: {path}");
                }

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    referenceEnginePaths.Add(path);
                }
                else
                {
                    Console.WriteLine("❌ Invalid path, skipping");
                }
            }

            if (referenceEnginePaths.Count < 2)
            {
                Console.WriteLine("❌ Need at least 2 valid reference paths to continue. Returning to main menu.");
                return;
            }

            // Ask about overwriting existing mobys
            Console.Write("\nOverwrite existing mobys in the target level? (y/n): ");
            bool allowOverwrite = Console.ReadLine()?.Trim().ToLower() == "y";

            // Load target level
            Console.WriteLine($"\nLoading target level: {Path.GetFileName(targetPath)}...");
            Level targetLevel;

            try { targetLevel = new Level(targetPath); }
            catch (Exception ex) { Console.WriteLine($"❌ Error loading target level: {ex.Message}"); return; }

            // Let the user choose which mobys to import
            Console.WriteLine("\nSelect which mobys to import:");
            Console.WriteLine("  1) All mobys");
            Console.WriteLine("  2) Common mobys (appear in multiple reference levels)");
            Console.WriteLine("  3) Uncommon mobys (appear in only one reference level)");
            Console.WriteLine("  4) Manually specify model IDs");
            Console.Write("> ");
            string modeInput = Console.ReadLine()?.Trim() ?? "1";

            MobyImporter.ImportSelectionMode selectionMode = MobyImporter.ImportSelectionMode.All;
            List<int>? manualModelIds = null;

            switch (modeInput)
            {
                case "2":
                    selectionMode = MobyImporter.ImportSelectionMode.MultipleVersions;
                    break;
                case "3":
                    selectionMode = MobyImporter.ImportSelectionMode.Unique;
                    break;
                case "4":
                    selectionMode = MobyImporter.ImportSelectionMode.Manual;
                    Console.WriteLine("Enter comma-separated model IDs to import:");
                    Console.Write("> ");
                    string? ids = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(ids))
                    {
                        manualModelIds = ids.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => int.TryParse(s, out int id) ? id : -1)
                            .Where(id => id >= 0)
                            .ToList();
                    }
                    break;
                default:
                    selectionMode = MobyImporter.ImportSelectionMode.All;
                    break;
            }

            // Import mobys using the selected mode
            bool success = MobyImporter.ImportMobysFromMixedReferenceLevels(
                targetLevel,
                referenceEnginePaths,
                allowOverwrite,
                true, // preserveGrindPaths = true
                selectionMode,
                manualModelIds
            );

            // Save the target level if successful
            if (success)
            {
                Console.Write("\nSave changes to the target level? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLower() == "y")
                {
                    Console.WriteLine("Saving target level...");

                    try
                    {
                        PrepareLevelForSave(targetLevel);
                        Console.WriteLine($"Saving level to {targetPath}...");
                        targetLevel.Save(targetPath);
                        Console.WriteLine("✅ Target level saved successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error saving target level: {ex.Message}");
                    }
                }
            }

            Console.WriteLine("\nPress Enter to return to main menu...");
            Console.ReadLine();
        }

        static SwapOptions GetCustomOptions()
        {
            SwapOptions options = SwapOptions.None;

            Console.WriteLine("\n=== Custom Selection ===");
            Console.WriteLine("Enter Y/N for each option:");

            if (GetYesNoInput("Swap Terrain/Geometry? (Y/N): "))
                options |= SwapOptions.Terrain;

            if (GetYesNoInput("Swap Collision? (Y/N): "))
                options |= SwapOptions.Collision;

            if (GetYesNoInput("Swap TIEs? (Y/N): "))
                options |= SwapOptions.Ties;

            if (GetYesNoInput("Swap Shrubs? (Y/N): "))
                options |= SwapOptions.Shrubs;

            if (GetYesNoInput("Swap Skybox? (Y/N): "))
                options |= SwapOptions.Skybox;

            if (GetYesNoInput("Swap Grind Paths? (Y/N): "))
                options |= SwapOptions.GrindPaths;

            if (GetYesNoInput("Swap Point Lights? (Y/N): "))
                options |= SwapOptions.PointLights;

            if (GetYesNoInput("Swap Sound Instances? (Y/N): "))
                options |= SwapOptions.SoundInstances;

            if (GetYesNoInput("Copy Special Mobys (Vendors, Crates)? (Y/N): "))
                options |= SwapOptions.Mobys;

            if (GetYesNoInput("Import Common Mobys from reference RC3/UYA levels? (Y/N): "))
            {
                ImportCommonMobysFromUser();
            }

            if (GetYesNoInput("Swap Level Variables (Background/Fog Color, Ship Position/Rotation)? (Y/N): "))
                options |= SwapOptions.SwapLevelVariables;

            if (GetYesNoInput("Transfer Ratchet position from RC1 Oltanis? (Y/N): "))
                options |= SwapOptions.TransferRatchetPosition;

            if (GetYesNoInput("Swap Vendor with RC1 Oltanis Vendor? (Y/N): "))
                options |= SwapOptions.SwapVendorWithOltanis;

            if (GetYesNoInput("Swap Crates with RC1 Oltanis Crates? (Y/N): "))
                options |= SwapOptions.SwapCratesWithOltanis;

            if (GetYesNoInput("Swap swingshots from RC1 to RC3/UYA? (y/n): "))
                options |= SwapOptions.SwapSwingshots;

            if (GetYesNoInput("Swap ship exit animation from RC1 to RC3/UYA? (Y/N): "))
                options |= SwapOptions.SwapShipExitAnim;

            if (GetYesNoInput("Import RC1 Cuboids (excluding ship animation)? (Y/N): "))
                options |= SwapOptions.ImportCuboids;

            if (GetYesNoInput("Import RC1 Splines (excluding ship animation)? (Y/N): "))
                options |= SwapOptions.ImportSplines;

            if (GetYesNoInput("Run occlusion diagnostics after swap? (Y/N): "))
                options |= SwapOptions.RunOcclusionDiagnostics;

            if (GetYesNoInput("Run ship camera diagnostics? (Y/N): "))
            {
                ShipCameraDiagnostics.RunShipCameraDiagnosticsInteractive();
            }

            if (GetYesNoInput("Create moby instances with RC1 Oltanis positions? (Y/N): "))
                options |= SwapOptions.CreateMobyInstances;

            if (GetYesNoInput("Register planet in the Galactic Map? (Y/N): "))
                options |= SwapOptions.RegisterPlanetInMap;

            if (GetYesNoInput("Run Grind Path Diagnostics? (Y/N): "))
            {
                RunGrindPathDiagnosticsOnly();
            }

            if (GetYesNoInput("Restore specific animations? (Y/N): "))
                options |= SwapOptions.RestoreAnimations;

            return options;
        }

        static bool GetYesNoInput(string prompt)
        {
            Console.Write(prompt);
            string input = Console.ReadLine() ?? "n";
            return input.Trim().ToUpper().StartsWith("Y");
        }

        // Add these methods after the existing texture-related methods

        /// <summary>
        /// Analyzes texture usage in RC1 and RC3/UYA levels to find potential correspondences
        /// </summary>
        static void AnalyzeAndMapTextures(Level sourceLevel, Level rc2Level, string outputDir)
        {
            Console.WriteLine("🔍 TEXTURE CORRESPONDENCE ANALYSIS 🔍");
            Console.WriteLine("Analyzing RC1 and RC3/UYA textures to find potential matches...");

            // Create output directory for texture analysis
            string textureAnalysisDir = Path.Combine(outputDir, "texture_analysis");
            Directory.CreateDirectory(textureAnalysisDir);

            // Extract the RC1 texture usage by terrain models
            var rc1TextureUsage = new Dictionary<int, List<int>>();
            var rc1TextureInfo = new Dictionary<int, (int width, int height)>();

            if (sourceLevel.terrainEngine?.fragments != null && sourceLevel.textures != null)
            {
                foreach (var fragment in sourceLevel.terrainEngine.fragments)
                {
                    if (fragment.model?.textureConfig != null)
                    {
                        foreach (var texConfig in fragment.model.textureConfig)
                        {
                            int texId = texConfig.id;
                            if (!rc1TextureUsage.ContainsKey(texId))
                            {
                                rc1TextureUsage[texId] = new List<int>();
                                // Record texture dimensions if available
                                if (texId >= 0 && texId < sourceLevel.textures.Count)
                                {
                                    var tex = sourceLevel.textures[texId];
                                    rc1TextureInfo[texId] = (tex.width, tex.height);
                                }
                            }

                            if (!rc1TextureUsage[texId].Contains(fragment.model.id))
                                rc1TextureUsage[texId].Add(fragment.model.id);
                        }
                    }
                }
            }

            // Extract the RC3/UYA texture usage by terrain models
            var rc2TextureUsage = new Dictionary<int, List<int>>();
            var rc2TextureInfo = new Dictionary<int, (int width, int height)>();

            if (rc2Level.terrainEngine?.fragments != null && rc2Level.textures != null)
            {
                foreach (var fragment in rc2Level.terrainEngine.fragments)
                {
                    if (fragment.model?.textureConfig != null)
                    {
                        foreach (var texConfig in fragment.model.textureConfig)
                        {
                            int texId = texConfig.id;
                            if (!rc2TextureUsage.ContainsKey(texId))
                            {
                                rc2TextureUsage[texId] = new List<int>();
                                // Record texture dimensions if available
                                if (texId >= 0 && texId < rc2Level.textures.Count)
                                {
                                    var tex = rc2Level.textures[texId];
                                    rc2TextureInfo[texId] = (tex.width, tex.height);
                                }
                            }

                            if (!rc2TextureUsage[texId].Contains(fragment.model.id))
                                rc2TextureUsage[texId].Add(fragment.model.id);
                        }
                    }
                }
            }

            // Generate texture correspondence report
            using (var writer = new StreamWriter(Path.Combine(textureAnalysisDir, "texture_correspondence.txt")))
            {
                writer.WriteLine("=== TEXTURE CORRESPONDENCE ANALYSIS ===");
                writer.WriteLine($"RC1 Level: {sourceLevel.path}");
                writer.WriteLine($"RC3/UYA Level: {rc2Level.path}");
                writer.WriteLine($"Total RC1 Textures: {sourceLevel.textures?.Count ?? 0}");
                writer.WriteLine($"Total RC3/UYA Textures: {rc2Level.textures?.Count ?? 0}");
                writer.WriteLine("\n=== RC1 TEXTURES USED BY TERRAIN ===");

                foreach (var kvp in rc1TextureUsage.OrderBy(k => k.Key))
                {
                    string dimensions = rc1TextureInfo.ContainsKey(kvp.Key)
                        ? $"{rc1TextureInfo[kvp.Key].width}x{rc1TextureInfo[kvp.Key].height}"
                        : "unknown";

                    writer.WriteLine($"Texture {kvp.Key} ({dimensions}): Used by {kvp.Value.Count} models: {string.Join(", ", kvp.Value)}");
                }

                writer.WriteLine("\n=== RC3/UYA TEXTURES USED BY TERRAIN ===");
                foreach (var kvp in rc2TextureUsage.OrderBy(k => k.Key))
                {
                    string dimensions = rc2TextureInfo.ContainsKey(kvp.Key)
                        ? $"{rc2TextureInfo[kvp.Key].width}x{rc2TextureInfo[kvp.Key].height}"
                        : "unknown";

                    writer.WriteLine($"Texture {kvp.Key} ({dimensions}): Used by {kvp.Value.Count} models: {string.Join(", ", kvp.Value)}");
                }

                // Attempt to match textures based on dimensions and usage patterns
                writer.WriteLine("\n=== POTENTIAL TEXTURE MATCHES (DIMENSION-BASED) ===");

                var potentialMatches = new Dictionary<int, List<(int id, float score)>>();

                foreach (var rc1Tex in rc1TextureInfo)
                {
                    var matches = new List<(int id, float score)>();

                    foreach (var rc2Tex in rc2TextureInfo)
                    {
                        float score = 0;

                        // Score based on dimension match
                        if (rc1Tex.Value.width == rc2Tex.Value.width && rc1Tex.Value.height == rc2Tex.Value.height)
                        {
                            score += 10;  // Strong match for same dimensions
                        }
                        else
                        {
                            // Partial match for similar dimensions (within 25%)
                            float widthRatio = (float) rc1Tex.Value.width / rc2Tex.Value.width;
                            float heightRatio = (float) rc1Tex.Value.height / rc2Tex.Value.height;

                            if (widthRatio >= 0.75f && widthRatio <= 1.25f &&
                                heightRatio >= 0.75f && heightRatio <= 1.25f)
                            {
                                score += 5;  // Partial match for similar dimensions
                            }
                        }

                        // Only include matches with some score
                        if (score > 0)
                        {
                            matches.Add((rc2Tex.Key, score));
                        }
                    }

                    if (matches.Count > 0)
                    {
                        potentialMatches[rc1Tex.Key] = matches.OrderByDescending(m => m.score).ToList();
                    }
                }

                // Write potential matches
                foreach (var kvp in potentialMatches)
                {
                    writer.WriteLine($"RC1 Texture {kvp.Key} ({rc1TextureInfo[kvp.Key].width}x{rc1TextureInfo[kvp.Key].height}):");

                    foreach (var match in kvp.Value)
                    {
                        writer.WriteLine($"  -> RC3/UYA Texture {match.id} ({rc2TextureInfo[match.id].width}x{rc2TextureInfo[match.id].height}), Score: {match.score}");
                    }
                }

                // Generate texture mapping suggestions
                writer.WriteLine("\n=== RECOMMENDED TEXTURE MAPPING (RC1 -> RC3/UYA) ===");
                writer.WriteLine("// Use this as a starting point for your texture mapping");
                writer.WriteLine("var textureMapping = new Dictionary<int, int>() {");

                foreach (var kvp in potentialMatches)
                {
                    if (kvp.Value.Count > 0)
                    {
                        writer.WriteLine($"    {{ {kvp.Key}, {kvp.Value[0].id} }}, // RC1 -> RC3/UYA");
                    }
                }

                writer.WriteLine("};");
            }

            Console.WriteLine($"✅ Texture analysis complete. Results saved to {textureAnalysisDir}");
            Console.WriteLine("Review the texture_correspondence.txt file for mapping suggestions");
        }

        /// <summary>
        /// Tests texture ID remapping on a limited number of fragments
        /// </summary>
        static void TestTextureReindexing(Level targetLevel, Dictionary<int, int> textureMapping, int fragmentCount = 5)
        {
            Console.WriteLine($"🧪 TESTING TEXTURE REINDEXING ON {fragmentCount} FRAGMENTS 🧪");

            if (targetLevel.terrainEngine?.fragments == null || targetLevel.terrainEngine.fragments.Count == 0)
            {
                Console.WriteLine("❌ No terrain fragments to reindex");
                return;
            }

            // Limit to requested fragment count
            int count = Math.Min(fragmentCount, targetLevel.terrainEngine.fragments.Count);
            Console.WriteLine($"Reindexing texture IDs for first {count} fragments...");

            // Keep track of statistics
            int totalConfigs = 0;
            int mappedConfigs = 0;
            int unmappedConfigs = 0;

            // Process the fragments
            for (int i = 0; i < count; i++)
            {
                var fragment = targetLevel.terrainEngine.fragments[i];
                if (fragment.model?.textureConfig != null)
                {
                    Console.WriteLine($"  Fragment {i} (Model ID {fragment.model.id}):");

                    for (int j = 0; j < fragment.model.textureConfig.Count; j++)
                    {
                        var texConfig = fragment.model.textureConfig[j];
                        int oldTextureId = texConfig.id;
                        totalConfigs++;

                        Console.WriteLine($"    TextureConfig {j}: ID {oldTextureId}");

                        if (textureMapping.TryGetValue(oldTextureId, out int newTextureId))
                        {
                            // Remap texture ID
                            texConfig.id = newTextureId;
                            mappedConfigs++;
                            Console.WriteLine($"      → Mapped to RC3/UYA texture ID {newTextureId}");
                        }
                        else
                        {
                            unmappedConfigs++;
                            Console.WriteLine($"      → No mapping found, keeping original ID");
                        }
                    }
                }
            }

            Console.WriteLine("\nReindexing Statistics:");
            Console.WriteLine($"  Total TextureConfigs processed: {totalConfigs}");
            Console.WriteLine($"  Successfully mapped: {mappedConfigs} ({(totalConfigs > 0 ? (float) mappedConfigs / totalConfigs * 100 : 0):F1}%)");
            Console.WriteLine($"  No mapping found: {unmappedConfigs} ({(totalConfigs > 0 ? (float) unmappedConfigs / totalConfigs * 100 : 0):F1}%)");

            Console.WriteLine("\n⚠️ WARNING: This is a test run only. The texture IDs have been changed but will not be saved unless you explicitly save the level.");
        }

        /// <summary>
        /// 🆕 QoL IMPROVEMENT: Automatically determines the animation source path based on global directory
        /// </summary>
        /// <param name="globalDir">The global directory path (e.g., globalRc3Dir, globalRc2Dir, or globalRc1Dir)</param>
        /// <param name="targetGame">Target game (1 for RC1, 2 for RC2, 3 for RC3)</param>
        /// <returns>Path to the level containing all animations, or empty string if not found</returns>
        private static string DetermineAnimationSourcePath(string globalDir, int targetGame)
        {
            try
            {
                // Navigate back from global directory to ps3data, then to the appropriate level
                // Example: D:\...\rc3\ps3data\global\ -> D:\...\rc3\ps3data\level1\engine.ps3

                var globalDirInfo = new DirectoryInfo(globalDir);
                var ps3DataDir = globalDirInfo.Parent; // Go back to ps3data

                if (ps3DataDir == null || !ps3DataDir.Exists)
                {
                    Console.WriteLine($"⚠️ Could not find ps3data directory from global path: {globalDir}");
                    return "";
                }

                string animationLevelFolder;
                if (targetGame == 1)
                {
                    // RC1: level1 contains all animations
                    animationLevelFolder = "level0";
                }
                else if (targetGame == 2)
                {
                    // RC2: level21 contains all animations
                    animationLevelFolder = "level21";
                }
                else
                {
                    // RC3: level1 contains all animations
                    animationLevelFolder = "level1";
                }

                var animationLevelDir = Path.Combine(ps3DataDir.FullName, animationLevelFolder);
                var animationEnginePath = Path.Combine(animationLevelDir, "engine.ps3");

                if (Directory.Exists(animationLevelDir) && File.Exists(animationEnginePath))
                {
                    Console.WriteLine($"✅ Found animation source level: {animationLevelFolder}");
                    return animationEnginePath;
                }
                else
                {
                    Console.WriteLine($"⚠️ Animation level not found at: {animationLevelDir}");
                    return "";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error determining animation source path: {ex.Message}");
                return "";
            }
        }



        /// <summary>
        /// Imports textures from the RC1 level into the target RC3/UYA level using append-based approach
        /// This is safer and more reliable than the current find-or-replace method
        /// </summary>
        static void ImportRC1TexturesToRC2Level(Level targetLevel, Level sourceLevel)
        {
            Console.WriteLine("📥 Importing RC1 textures to RC3/UYA level using append-based approach...");

            if (targetLevel == null || sourceLevel == null)
            {
                Console.WriteLine("❌ Cannot import textures: Invalid level data");
                return;
            }

            if (sourceLevel.textures == null || sourceLevel.textures.Count == 0)
            {
                Console.WriteLine("❌ Source level has no textures to import");
                return;
            }

            // Ensure target texture list exists
            if (targetLevel.textures == null)
            {
                targetLevel.textures = new List<LibTexture>();  // Fix this line
            }

            // 🔧 CRITICAL FIX: Find all texture IDs used by terrain models in the TARGET level
            // (since the target level now contains RC1 terrain with RC1 texture IDs after terrain swap)
            HashSet<int> usedTextureIdsInTarget = new HashSet<int>();
            if (targetLevel.terrainEngine?.fragments != null)
            {
                foreach (var fragment in targetLevel.terrainEngine.fragments)
                {
                    if (fragment?.model?.textureConfig != null)
                    {
                        foreach (var texConfig in fragment.model.textureConfig)
                        {
                            usedTextureIdsInTarget.Add(texConfig.id);
                        }
                    }
                }
            }

            Console.WriteLine($"  Found {usedTextureIdsInTarget.Count} unique texture IDs used by target level terrain models");
            Console.WriteLine($"  🔍 DEBUG: Texture IDs being used: {string.Join(", ", usedTextureIdsInTarget.OrderBy(x => x).Take(10))}{(usedTextureIdsInTarget.Count > 10 ? "..." : "")}");

            if (usedTextureIdsInTarget.Count == 0)
            {
                Console.WriteLine("  No textures to import");
                return;
            }

            // Create mapping from RC1 texture IDs to new target texture IDs
            Dictionary<int, int> textureIdMapping = new Dictionary<int, int>();

            // Remember the starting index where we'll append RC1 textures
            int appendStartIndex = targetLevel.textures.Count;

            // Sort the texture IDs for consistent processing
            var sortedTextureIds = usedTextureIdsInTarget.OrderBy(id => id).ToList();

            Console.WriteLine($"  Appending RC1 textures starting at index {appendStartIndex}");
            Console.WriteLine($"  🔍 DEBUG: Target level currently has {targetLevel.textures.Count} textures before import");

            int texturesImported = 0;
            int duplicatesSkipped = 0;
            int skippedOutOfRange = 0;
            int skippedNoData = 0;

            foreach (int rc1TextureId in sortedTextureIds)
            {
                // Skip invalid texture IDs
                if (rc1TextureId < 0 || rc1TextureId >= sourceLevel.textures.Count)
                {
                    Console.WriteLine($"  ⚠️ Texture ID {rc1TextureId} is out of range for RC1 source level");
                    skippedOutOfRange++;
                    continue;
                }

                var rc1Texture = sourceLevel.textures[rc1TextureId];
                if (rc1Texture == null || rc1Texture.data == null || rc1Texture.data.Length == 0)
                {
                    Console.WriteLine($"  ⚠️ Texture ID {rc1TextureId} has no data, skipping");
                    skippedNoData++;
                    continue;
                }

                // 🔧 TEMPORARY FIX: Disable duplicate detection for now to see if that's the issue
                // Check if we already have an identical texture in the target level
                // This prevents unnecessary duplication
                int existingTextureId = -1; // FindExistingTerrainTexture(targetLevel, rc1Texture);

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
                    LibTexture newTexture = DeepCloneTerrainTexture(rc1Texture);

                    // Update the texture's ID to reflect its new position
                    newTexture.id = targetLevel.textures.Count;

                    targetLevel.textures.Add(newTexture);
                    int newTextureId = targetLevel.textures.Count - 1;

                    textureIdMapping[rc1TextureId] = newTextureId;
                    texturesImported++;

                    Console.WriteLine($"  ✅ Appended RC1 texture {rc1TextureId} as {newTextureId} ({newTexture.width}x{newTexture.height})");
                }
            }

            // 🔧 CRITICAL FIX: Now update texture references in the TARGET level's terrain models 
            // (which are now RC1 terrain fragments that need their texture IDs updated)
            if (targetLevel.terrainEngine?.fragments != null)
            {
                int modelsUpdated = 0;
                int textureReferencesUpdated = 0;

                // 🔍 DEBUG: Track what texture IDs we're actually updating
                Dictionary<int, List<int>> textureIdUpdates = new Dictionary<int, List<int>>();

                foreach (var fragment in targetLevel.terrainEngine.fragments)
                {
                    if (fragment?.model?.textureConfig == null || fragment.model.textureConfig.Count == 0)
                        continue;

                    bool modelUpdated = false;
                    foreach (var texConfig in fragment.model.textureConfig)
                    {
                        // Update ALL texture references using the mapping we just created
                        if (textureIdMapping.TryGetValue(texConfig.id, out int newId))
                        {
                            int oldId = texConfig.id;
                            texConfig.id = newId;
                            modelUpdated = true;
                            textureReferencesUpdated++;

                            // Track this update for debugging
                            if (!textureIdUpdates.ContainsKey(oldId))
                                textureIdUpdates[oldId] = new List<int>();
                            textureIdUpdates[oldId].Add(newId);

                            if (textureReferencesUpdated <= 10) // Limit logging
                            {
                                Console.WriteLine($"    Updated texture ref in terrain fragment {fragment.off1E}: {oldId} → {newId}");
                            }
                        }
                        else
                        {
                            // This shouldn't happen if our logic is correct
                            Console.WriteLine($"    ⚠️ WARNING: No mapping found for texture ID {texConfig.id} in fragment {fragment.off1E}");
                        }
                    }

                    if (modelUpdated)
                        modelsUpdated++;
                }

                Console.WriteLine($"  ✅ Updated texture references in {modelsUpdated} terrain fragments ({textureReferencesUpdated} total references)");

                // 🔍 DEBUG: Show what unique texture IDs fragments are now using
                var finalTextureIds = new HashSet<int>();
                foreach (var fragment in targetLevel.terrainEngine.fragments)
                {
                    if (fragment?.model?.textureConfig != null)
                    {
                        foreach (var texConfig in fragment.model.textureConfig)
                        {
                            finalTextureIds.Add(texConfig.id);
                        }
                    }
                }
                Console.WriteLine($"  🔍 DEBUG: After update, terrain fragments now use {finalTextureIds.Count} unique texture IDs: {string.Join(", ", finalTextureIds.OrderBy(x => x).Take(10))}{(finalTextureIds.Count > 10 ? "..." : "")}");

                // 🔍 DEBUG: Show the actual mapping that was applied
                Console.WriteLine($"  🔍 DEBUG: Texture ID mappings applied:");
                foreach (var kvp in textureIdMapping.OrderBy(x => x.Key).Take(10))
                {
                    Console.WriteLine($"    RC1 Texture {kvp.Key} → New Texture {kvp.Value}");
                }
                if (textureIdMapping.Count > 10)
                    Console.WriteLine($"    ... and {textureIdMapping.Count - 10} more mappings");
            }

            Console.WriteLine($"  ✅ Imported {texturesImported} new textures, reused {duplicatesSkipped} existing textures");
            if (skippedOutOfRange > 0 || skippedNoData > 0)
            {
                Console.WriteLine($"  ⚠️ Skipped {skippedOutOfRange} out-of-range IDs and {skippedNoData} textures with no data");
            }
            Console.WriteLine($"  📊 Target level now has {targetLevel.textures.Count} total textures");
        }

        /// <summary>
        /// Creates a deep copy of a texture with all properties preserved
        /// </summary>
        /// <param name="sourceTexture">The source texture to clone</param>
        /// <returns>A new completely independent texture instance</returns>
private static LibTexture DeepCloneTerrainTexture(LibTexture sourceTexture)
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

            LibTexture newTexture = new LibTexture(
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
        /// Finds an existing texture in the target level that matches the source texture
        /// </summary>
        /// <param name="targetLevel">Level to search in</param>
        /// <param name="sourceTexture">Texture to find a match for</param>
        /// <returns>Index of matching texture, or -1 if not found</returns>
        private static int FindExistingTerrainTexture(Level targetLevel, LibTexture sourceTexture)
        {
            if (sourceTexture == null || targetLevel.textures == null)
                return -1;

            for (int i = 0; i < targetLevel.textures.Count; i++)
            {
                if (TextureEqualsForTerrain(sourceTexture, targetLevel.textures[i]))
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
        private static bool TextureEqualsForTerrain(LibTexture tex1, LibTexture tex2)
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
        /// Verifies that all textures used by terrain fragments exist in the texture list
        /// </summary>
        static void VerifyTexturesForTerrainFragments(Level level, string levelName)
        {
            Console.WriteLine($"\n=== TEXTURE VERIFICATION FOR {levelName} ===");

            if (level.terrainEngine?.fragments == null || level.terrainEngine.fragments.Count == 0)
            {
                Console.WriteLine("No terrain fragments to verify");
                return;
            }

            if (level.textures == null || level.textures.Count == 0)
            {
                Console.WriteLine("No textures in level");
                return;
            }

            var usedTextureIds = new HashSet<int>();
            var missingTextureIds = new HashSet<int>();
            var emptyTextureIds = new HashSet<int>();

            // Check texture references in fragments
            foreach (var fragment in level.terrainEngine.fragments)
            {
                if (fragment.model?.textureConfig != null)
                {
                    foreach (var texConfig in fragment.model.textureConfig)
                    {
                        int texId = texConfig.id;
                        usedTextureIds.Add(texId);

                        // Check if the ID is within range
                        if (texId < 0 || texId >= level.textures.Count)
                        {
                            missingTextureIds.Add(texId);
                            continue;
                        }

                        // Check if texture has data
                        if (level.textures[texId].data == null || level.textures[texId].data.Length == 0)
                        {
                            emptyTextureIds.Add(texId);
                        }
                    }
                }
            }

            // Report findings
            Console.WriteLine($"Total unique texture IDs used by terrain: {usedTextureIds.Count}");
            Console.WriteLine($"Texture list size: {level.textures.Count}");

            if (missingTextureIds.Count > 0)
            {
                Console.WriteLine($"⚠️ {missingTextureIds.Count} texture IDs are out of range: {string.Join(", ", missingTextureIds.Take(10))}{(missingTextureIds.Count > 10 ? "..." : "")}");
            }
            else
            {
                Console.WriteLine("✅ All texture IDs are within range");
            }

            if (emptyTextureIds.Count > 0)
            {
                Console.WriteLine($"⚠️ {emptyTextureIds.Count} textures have no data: {string.Join(", ", emptyTextureIds.Take(10))}{(emptyTextureIds.Count > 10 ? "..." : "")}");
            }
            else
            {
                Console.WriteLine("✅ All referenced textures have data");
            }

            // Sample some textures
            Console.WriteLine("\nSample of textures used by terrain:");
            foreach (var texId in usedTextureIds.OrderBy(id => id).Take(5))
            {
                if (texId >= 0 && texId < level.textures.Count)
                {
                    var tex = level.textures[texId];
                    Console.WriteLine($"  Texture {texId}: {tex.width}x{tex.height}, {(tex.data?.Length ?? 0)} bytes");
                }
                else
                {
                    Console.WriteLine($"  Texture {texId}: Out of range");
                }
            }
        }

        /// <summary>
        /// Checks if a tie object is likely to be garbage/unused
        /// Simplified to only check for truly invalid data, not aggressive culling
        /// </summary>
        static bool IsLikelyGarbageTie(Tie tie)
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

            // Keep all other TIEs - let the game's culling system handle visibility
            return false;
        }

        /// <summary>
        /// Imports RC1 tie models to RC3/UYA level, ensuring proper model compatibility with unique IDs
        /// </summary>
        static Dictionary<int, int> ImportRC1TieModelsToRC2Level(Level targetLevel, Level sourceLevel)
        {
            Console.WriteLine($"🔄 Importing RC1 tie models to RC3/UYA level...");

            var modelIdMapping = new Dictionary<int, int>();

            if (sourceLevel.tieModels == null || sourceLevel.tieModels.Count == 0)
            {
                Console.WriteLine("  ⚠️ No tie models found in RC1 source level");
                return modelIdMapping;
            }

            // Ensure the target has a tie model list
            if (targetLevel.tieModels == null)
            {
                targetLevel.tieModels = new List<Model>();
            }

            // Find the highest existing model ID to avoid conflicts
            short nextModelId = 1000; // Start with a safe base value
            foreach (var existingModel in targetLevel.tieModels)
            {
                if (existingModel != null && existingModel.id >= nextModelId)
                {
                    nextModelId = (short) (existingModel.id + 1);
                }
            }

            Console.WriteLine($"  Starting model ID assignment from {nextModelId}");

            // Log all RC1 model IDs for diagnostic purposes
            var rc1ModelIds = new HashSet<int>();
            if (sourceLevel.tieModels != null)
            {
                foreach (var model in sourceLevel.tieModels.Where(m => m != null))
                {
                    rc1ModelIds.Add(model.id);
                }
            }
            Console.WriteLine($"  Found {rc1ModelIds.Count} unique model IDs in RC1 source");

            // Create a deep copy of each RC1 model to avoid reference issues
            foreach (var origModel in sourceLevel.tieModels?.Where(m => m != null) ?? Enumerable.Empty<Model>())
            {
                // Skip if not actually a TieModel (shouldn't happen but just to be safe)
                if (!(origModel is TieModel rc1Model))
                {
                    Console.WriteLine($"  ⚠️ Model {origModel.id} is not a TieModel, skipping");
                    continue;
                }

                // Store the original RC1 model ID
                int originalId = rc1Model.id;

                // Create a clone of the model
                try
                {
                    TieModel newModel = TieModel(rc1Model);

                    // Assign a new unique ID to avoid conflicts with existing RC3/UYA models
                    newModel.id = nextModelId++;

                    // Add the model with its new ID to the RC3/UYA level
                    targetLevel.tieModels.Add(newModel);

                    // Record the mapping from original RC1 ID to new assigned RC3/UYA ID
                    modelIdMapping[originalId] = newModel.id;

                    Console.WriteLine($"  Added RC1 tie model {originalId} as RC3/UYA model {newModel.id}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error copying RC1 tie model {originalId}: {ex.Message}");
                }
            }

            // Identify any RC1 tie model IDs that weren't imported
            var usedModelIds = new HashSet<int>();
            if (sourceLevel.ties != null)
            {
                foreach (var tie in sourceLevel.ties)
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

            // CRITICAL FIX: Update both tieIds AND tieModelIds collections in the header
            if (targetLevel.tieModels != null)
            {
                // Update the mapping of model IDs used by the engine header
                targetLevel.tieIds = targetLevel.tieModels.Select(m => (int) m.id).ToList();

                Console.WriteLine($"  ✅ Updated tieIds header list with {targetLevel.tieIds.Count} model IDs");
            }

            Console.WriteLine($"  ✅ Imported {modelIdMapping.Count} RC1 tie models to RC3/UYA level with unique IDs");
            return modelIdMapping;
        }

        /// <summary>
        /// Creates a copy of a TieModel with new ID assignment
        /// </summary>
        static TieModel TieModel(TieModel sourceModel)
        {
            // Create a minimal byte array that we can pass to construct a new TieModel
            byte[] dummyBlock = new byte[0x40];

            // Copy basic properties that would be read from the byte array
            WriteFloat(dummyBlock, 0x00, sourceModel.cullingX);
            WriteFloat(dummyBlock, 0x04, sourceModel.cullingY);
            WriteFloat(dummyBlock, 0x08, sourceModel.cullingZ);
            WriteFloat(dummyBlock, 0x0C, sourceModel.cullingRadius);
            WriteUint(dummyBlock, 0x20, sourceModel.off20);
            WriteShort(dummyBlock, 0x2A, sourceModel.wiggleMode);
            WriteFloat(dummyBlock, 0x2C, sourceModel.off2C);
            WriteShort(dummyBlock, 0x30, (short) 0); // We'll set the ID later
            WriteUint(dummyBlock, 0x34, sourceModel.off34);
            WriteUint(dummyBlock, 0x38, sourceModel.off38);
            WriteUint(dummyBlock, 0x3C, sourceModel.off3C);

            // Since the constructor requires a FileStream, we need to create one from our dummy block
            // We'll create a temporary file for this purpose
            string tempFilePath = Path.GetTempFileName();
            try
            {
                // Write the dummy block to the temp file
                File.WriteAllBytes(tempFilePath, dummyBlock);

                // Open the temp file as a FileStream
                using (FileStream fs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
                {
                    // Create a new TieModel using the dummy data
                    TieModel newModel = new TieModel(fs, dummyBlock, 0);

                    // Now manually copy over the remaining important data
                    newModel.size = sourceModel.size;

                    // Deep copy vertex and index buffers
                    newModel.vertexBuffer = sourceModel.vertexBuffer != null
                        ? (float[]) sourceModel.vertexBuffer.Clone()
                        : new float[0];

                    newModel.indexBuffer = sourceModel.indexBuffer != null
                        ? (ushort[]) sourceModel.indexBuffer.Clone()
                        : new ushort[0];

                    // Deep copy texture configs to avoid shared references
                    newModel.textureConfig = new List<TextureConfig>();
                    if (sourceModel.textureConfig != null)
                    {
                        foreach (var texConfig in sourceModel.textureConfig)
                        {
                            TextureConfig newConfig = new TextureConfig();
                            newConfig.id = texConfig.id;
                            newConfig.start = texConfig.start;
                            newConfig.size = texConfig.size;
                            newConfig.mode = texConfig.mode;
                            newModel.textureConfig.Add(newConfig);
                        }
                    }

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

        static void RunTieDiagnosticsOnly()
        {
            Console.WriteLine("\n==== Running TIE Diagnostics Tool ====");
            TieDiagnostics.RunTieDiagnosticsInteractive();
        }

        static void RunGrindPathDiagnosticsOnly()
        {
            Console.WriteLine("\n==== Running Grind Path Diagnostics Tool ====");
            GrindPathDiagnostics.RunGrindPathDiagnosticsInteractive();
        }

        static void RunOcclusionDiagnosticsOnly()
        {
            Console.WriteLine("\n==== Running Occlusion Diagnostics Tool ====");

            Console.WriteLine("Enter path to source level:");
            Console.Write("> ");
            string sourcePath = Console.ReadLine()?.Trim() ?? "";

            Console.WriteLine("Enter path to pristine donor level:");
            Console.Write("> ");
            string donorPath = Console.ReadLine()?.Trim() ?? "";

            Console.WriteLine("Enter path to target level:");
            Console.Write("> ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(sourcePath) || !File.Exists(donorPath) || !File.Exists(targetPath))
            {
                Console.WriteLine("❌ One or more level files not found. Please check the paths.");
                return;
            }

            Level sourceLevel, donorLevel, targetLevel;
            try
            {
                sourceLevel = new Level(sourcePath);
                donorLevel = new Level(donorPath);
                targetLevel = new Level(targetPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading levels: {ex.Message}");
                return;
            }

            Console.WriteLine("Enter output directory for diagnostics report:");
            Console.Write("> ");
            string outputDir = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(outputDir))
                outputDir = Path.Combine(Path.GetDirectoryName(targetPath) ?? "", "diagnostics");

            Directory.CreateDirectory(outputDir);

            Console.WriteLine("Running occlusion comparison diagnostics...");
            OcclusionDiagnostics.CompareOcclusionData(sourceLevel, donorLevel, targetLevel, outputDir);
            Console.WriteLine($"✅ Diagnostics report generated at: {outputDir}");
        }

        static void PrepareLevelForSave(Level level)
        {
            Console.WriteLine("\n=== Preparing Level For Save ===");

            // Add grind path validation
            if (level.grindPaths != null && level.grindPaths.Count > 0)
            {
                Console.WriteLine("Validating grind paths...");
                bool validPaths = GrindPathSwapper.ValidateGrindPathReferences(level);

                if (!validPaths)
                {
                    Console.WriteLine("⚠️ WARNING: Invalid grind path references detected. Attempting to fix...");

                    // Fix dangling references
                    foreach (var path in level.grindPaths.ToList())
                    {
                        if (path.spline != null && !level.splines.Contains(path.spline))
                        {
                            Console.WriteLine($"  Fixing invalid reference in path {path.id}");
                            path.spline = null;
                        }
                    }

                    // Fix duplicate spline IDs
                    var duplicateSplineIds = level.splines
                        .GroupBy(s => s.id)
                        .Where(g => g.Count() > 1)
                        .ToList();

                    if (duplicateSplineIds.Any())
                    {
                        Console.WriteLine($"  Fixing {duplicateSplineIds.Count} duplicate spline ID groups");
                        int highestId = level.splines.Max(s => s.id);

                        foreach (var group in duplicateSplineIds)
                        {
                            bool first = true;
                            foreach (var spline in level.splines.Where(s => s.id == group.Key).ToList())
                            {
                                if (first)
                                {
                                    first = false;
                                    continue;
                                }

                                // Assign new ID
                                int oldId = spline.id;
                                spline.id = ++highestId;
                                Console.WriteLine($"    Changed spline ID {oldId} to {spline.id}");
                            }
                        }
                    }
                }
            }

            // Validate TIE data
            if (level.tieModels != null)
            {
                Console.WriteLine($"• Level has {level.tieModels.Count} TIE models");
            }
            
            if (level.ties != null)
            {
                Console.WriteLine($"• Level has {level.ties.Count} TIE instances");
            }
            
            if (level.tieIds != null)
            {
                Console.WriteLine($"• Level has {level.tieIds.Count} TIE IDs in header list");
                
                // Critical check: tieIds must match model count, not instance count
                if (level.tieModels != null && level.tieIds.Count != level.tieModels.Count)
                {
                    Console.WriteLine($"⚠️ FIXING CRITICAL ISSUE: tieIds count ({level.tieIds.Count}) doesn't match model count ({level.tieModels.Count})");
                    level.tieIds = level.tieModels.Select(m => (int)m.id).ToList();
                    Console.WriteLine($"✅ Updated tieIds list with {level.tieIds.Count} entries");
                }
            }
            
            // Check tieGroupData alignment
            if (level.tieGroupData != null)
            {
                Console.WriteLine($"• Level has {level.tieGroupData.Length} bytes of tieGroupData");
                
                // Check alignment
                if (level.tieGroupData.Length % 0x80 != 0)
                {
                    Console.WriteLine($"⚠️ FIXING CRITICAL ISSUE: tieGroupData size ({level.tieGroupData.Length}) is not aligned to 0x80 boundaries");
                    TieSwapper.CreateTieSerializationData(level);
                    Console.WriteLine($"✅ Recreated tieGroupData, now {level.tieGroupData.Length} bytes (aligned to 0x80)");
                }
            }
            
            // Check tieData size
            if (level.tieData != null && level.ties != null)
            {
                Console.WriteLine($"• Level has {level.tieData.Length} bytes of tieData");
                
                int expectedSize = level.ties.Count * 0x70;
                if (level.tieData.Length != expectedSize)
                {
                    Console.WriteLine($"⚠️ FIXING CRITICAL ISSUE: tieData size mismatch: {level.tieData.Length} bytes (expected {expectedSize})");
                    TieSwapper.CreateTieSerializationData(level);
                    Console.WriteLine($"✅ Recreated tieData, now {level.tieData.Length} bytes");
                }
            }
            
            Console.WriteLine($"Saving level with {level.tieIds?.Count ?? 0} tieIds and tieGroupData size {level.tieGroupData?.Length ?? 0} bytes");
        }

        public static void Main(string[] args)
        {
            Console.WriteLine(">>> R&C Geometry Swapper <<<");

            SwapConfig? savedConfig = LoadConfig();
            SwapConfig config;

            if (savedConfig != null && GetYesNoInput("Would you like to use the same settings as before? (Y/N): "))
            {
                config = savedConfig;
            }
            else
            {
                int sourceGame = GetSourceGameFromUser();
                int targetGame = GetTargetGameFromUser();
                config = new SwapConfig
                {
                    SourceGame = sourceGame,
                    TargetGame = targetGame,
                    SourceLevelDir = sourceGame == 2 ? defaultRc2SourceLevelDir : defaultSourceLevelDir,
                    DonorLevelDir = targetGame == 1 ? rc1DonorLevelDir :
                                    targetGame == 2 ? rc2DonorLevelDir : rc3DonorLevelDir,
                    ReferencePlanetDir = targetGame == 1 ? referenceRc1PlanetDir :
                                        targetGame == 2 ? referenceGcPlanetDir : referenceUyaPlanetDir,
                    GlobalDir = targetGame == 1 ? globalRc1Dir :
                               targetGame == 2 ? globalRc2Dir : globalRc3Dir,
                    OutputDir = outputDir,
                    Options = DisplayMenuAndGetOptions()
                };
            }

            int sourceGameSelected = config.SourceGame;
            int targetGameSelected = config.TargetGame;
            sourceLevelDir = config.SourceLevelDir;
            rc3DonorLevelDir = config.DonorLevelDir;
            referenceUyaPlanetDir = config.ReferencePlanetDir;
            globalRc3Dir = config.GlobalDir;
            outputDir = config.OutputDir;
            SwapOptions options = config.Options;

            Console.WriteLine($"\nSelected options: {options}");

            Directory.CreateDirectory(outputDir);

            string sourceEnginePath = Path.Combine(sourceLevelDir, "engine.ps3");
            string donorEnginePath = Path.Combine(rc3DonorLevelDir, "engine.ps3");
            string referenceEnginePath = Path.Combine(referenceUyaPlanetDir, "engine.ps3");

            Console.WriteLine($"📂 Loading R&C{sourceGameSelected} Source Level (for geometry): {sourceEnginePath}");
            Level? sourceLevel = null;
            try { sourceLevel = new Level(sourceEnginePath); }
            catch (Exception ex) { Console.WriteLine($"❌ Error loading R&C{sourceGameSelected} source: {ex.Message}"); return; }

            Level? rc3DonorLevel = null; // Renamed for clarity
            try { rc3DonorLevel = new Level(donorEnginePath); }
            catch (Exception ex) { Console.WriteLine($"❌ Error loading R&C3/UYA donor: {ex.Message}"); return; }

            Level? referenceRc3PlanetLevel = null;
            if (File.Exists(referenceEnginePath))
            {
                try
                {
                    referenceRc3PlanetLevel = new Level(referenceEnginePath);
                    Console.WriteLine("  ✅ Reference R&C3/UYA planet loaded.");
                }
                catch (Exception ex) { Console.WriteLine($"  ❌ Error loading reference R&C3/UYA planet: {ex.Message}"); }
            }
            else { Console.WriteLine($"  Warning: Reference R&C3/UYA planet not found at {referenceEnginePath}."); }

            LoadGlobalRCAssets(globalRc3Dir, out var globalArmorModels, out var globalArmorTextures, out var globalGadgetModels, out var globalGadgetTextures);

            if (sourceLevel == null || rc3DonorLevel == null)
            {
                Console.WriteLine("Critical error: Source or Donor level failed to load. Exiting.");
                return;
            }

            rc3DonorLevel.game = targetGameSelected switch
            {
                1 => GameType.RaC1,
                2 => GameType.RaC2,
                _ => GameType.RaC3
            }; // Explicitly set target game type


            // Add diagnostic info about donor level before attempting to modify it
            Console.WriteLine("\n=== RC3/UYA DONOR LEVEL DIAGNOSTIC INFO ===");
            Console.WriteLine($"Game Type: {rc3DonorLevel.game.num}");
            Console.WriteLine($"Total textures: {rc3DonorLevel.textures?.Count ?? 0}");
            Console.WriteLine($"Total mobys: {rc3DonorLevel.mobs?.Count ?? 0}");
            Console.WriteLine($"Total moby models: {rc3DonorLevel.mobyModels?.Count ?? 0}");
            Console.WriteLine($"Level Variables ByteSize: 0x{rc3DonorLevel.levelVariables?.ByteSize.ToString("X") ?? "??"}");
            Console.WriteLine($"Terrain fragments: {rc3DonorLevel.terrainEngine?.fragments?.Count ?? 0}");

            // Perform terrain analysis
            Console.WriteLine("\n=== ANALYZING TERRAIN STRUCTURES ===");
            DumpTerrainInfo(sourceLevel.terrainEngine, "RC1 Source Level");
            DumpTerrainInfo(rc3DonorLevel.terrainEngine, "RC3/UYA Donor Level");

            if (referenceRc3PlanetLevel != null)
            {
                DumpTerrainInfo(referenceRc3PlanetLevel.terrainEngine, "RC3/UYA Reference Level");
            }

            // Ask if user wants to continue after analysis
            Console.WriteLine("\nAnalysis complete. Continue with geometry swap? (Y/N):");
            if (!GetYesNoInput(""))
            {
                Console.WriteLine("Operation cancelled by user.");
                return;
            }

            // 🆕 QUICK TEST - Add this before calling FinalizeLevelProcessing
            Console.WriteLine("🧪 Testing AnimationRestorer accessibility...");
            try
            {
                // This should compile and run if the class is accessible
                Console.WriteLine($"AnimationRestorer class found: {typeof(AnimationRestorer).Name}");
                Console.WriteLine("✅ AnimationRestorer is accessible");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ AnimationRestorer not accessible: {ex.Message}");
            }

            // --- Call the main processing function with user-selected options ---
            FinalizeLevelProcessing(rc3DonorLevel, sourceLevel, referenceRc3PlanetLevel!,
                      globalArmorModels, globalArmorTextures,    // These need to match the expected types
                      globalGadgetModels, globalGadgetTextures,
                      options,
                      rc3DonorLevel,
                      targetGameSelected); // 🆕 NEW: Pass the target game selection

            // Analyze texture correspondence between RC1 and RC3/UYA
            Console.WriteLine("\n=== TEXTURE MAPPING ANALYSIS ===");
            AnalyzeAndMapTextures(sourceLevel, rc3DonorLevel, outputDir);

            // After the analysis is complete, you can define a manual mapping:
            // This is just a placeholder - you should replace it with values from the analysis
            var textureMapping = new Dictionary<int, int>()
            {
                { 0, 0 },  // Just placeholders for now
                { 1, 1 },
                // Add more mappings based on the analysis results
            };

            // Test how texture reindexing would work on a small sample
            Console.WriteLine("\n=== TEXTURE REINDEXING TEST ===");
            if (GetYesNoInput("Would you like to test texture reindexing on 5 fragments? (Y/N): "))
            {
                // Check if terrain swapping was enabled
                if ((options & SwapOptions.Terrain) != 0)
                {
                    Console.WriteLine("⚠️ Texture reindexing test is not compatible with terrain swapping.");
                    Console.WriteLine("   The append-based texture import has already updated texture references.");
                    Console.WriteLine("   Skipping test to avoid conflicts.");
                }
                else
                {
                    TestTextureReindexing(sourceLevel, textureMapping, 5);
                }
            }

            // 🔧 REMOVE THIS ENTIRE SECTION - IT'S DUPLICATING THE TEXTURE IMPORT
            // Import RC1 textures into RC3/UYA level
            // Console.WriteLine("\n=== IMPORTING RC1 TEXTURES INTO RC3/UYA LEVEL ===");
            // ImportRC1TexturesToRC2Level(rc3DonorLevel, sourceLevel);

            // Verify textures for terrain fragments
            Console.WriteLine("\n=== VERIFYING TEXTURES FOR TERRAIN FRAGMENTS ===");
            VerifyTexturesForTerrainFragments(rc3DonorLevel, "RC3/UYA Donor Level");

            // --- Save, Patch, Verify ---
            Console.WriteLine($"\n💾 Saving geometry-swapped level to: {outputDir}");

            // Ensure ByteSize is correct before saving
            if (rc3DonorLevel.levelVariables != null)
            {
                if (rc3DonorLevel.levelVariables.ByteSize != 0x8C)
                {
                    Console.WriteLine($"⚠️ Correcting ByteSize from 0x{rc3DonorLevel.levelVariables.ByteSize:X} to 0x8C");
                    rc3DonorLevel.levelVariables.ByteSize = 0x8C;
                }
                else
                {
                    Console.WriteLine("✅ ByteSize correctly set to 0x8C for RaC3 compatibility");
                }
            }

            // --- Save, Patch, Verify ---
            Console.WriteLine($"\n💾 Saving geometry-swapped level to: {outputDir}");

            // Ensure ByteSize is correct before saving
            if (rc3DonorLevel.levelVariables != null)
            {
                if (rc3DonorLevel.levelVariables.ByteSize != 0x8C)
                {
                    Console.WriteLine($"⚠️ Correcting ByteSize from 0x{rc3DonorLevel.levelVariables.ByteSize:X} to 0x8C");
                    rc3DonorLevel.levelVariables.ByteSize = 0x8C;
                }
                else
                {
                    Console.WriteLine("✅ ByteSize correctly set to 0x8C for RaC3 compatibility");
                }
            }

            PrepareLevelForSave(rc3DonorLevel);

            //TieCullingDataFixer.RebuildTieOcclusion(rc3DonorLevel, sourceLevel);

            // Preserve TIE culling data before saving to prevent corruption
            Console.WriteLine("🔧 Applying TIE culling data protection before save...");
            var tieCullingProtection = TieCullingDataFixer.PreserveTieCullingData(rc3DonorLevel);

            // Apply coordinate conversion when importing TIEs into an RC3 level
            /* DISABLED - This overwrites the direct copy with calculated values
            if ((options & SwapOptions.Ties) != 0 && rc3DonorLevel.game.num == 3 && sourceLevel.game.num != rc3DonorLevel.game.num)
            {
                Console.WriteLine("🔄 Applying coordinate system conversion to TIE culling data...");
                TieCullingDataFixer.ApplyCoordinateConversionToTieData(
                    rc3DonorLevel,
                    sourceLevel.game.num,
                    rc3DonorLevel.game.num
                );

                // Capture converted occlusion data
                TieCullingDataFixer.PreserveTieCullingData(rc3DonorLevel, tieCullingProtection);
            }
            */
            Console.WriteLine("🔧 Skipping coordinate conversion - direct copy approach preserves original patterns");

            Console.WriteLine($"Saving level with TIE culling protection to {outputDir}...");
            rc3DonorLevel.Save(outputDir);

            // Restore preserved TIE culling data after save
            Console.WriteLine("🔓 Restoring TIE culling data after save...");
            TieCullingDataFixer.RestoreTieCullingData(rc3DonorLevel, tieCullingProtection);

            string outputEngineFile = Path.Combine(outputDir, "engine.ps3");
            Console.WriteLine("Patching engine.ps3 header values...");
            try
            {
                using (var fs = File.Open(outputEngineFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    uint version;
                    uint magic;
                    switch (rc3DonorLevel.game.num)
                    {
                        case 1:
                            version = 0x00010003u;
                            magic = 0x00000001u;
                            break;
                        case 2:
                            version = 0x00020003u;
                            magic = 0xEAA90001u;
                            break;
                        case 3:
                            version = 0x00030003u;
                            magic = 0xEAA60001u;
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported game number {rc3DonorLevel.game.num}");
                    }
                    fs.Seek(0x08, SeekOrigin.Begin); WriteUintBigEndian(fs, version);
                    fs.Seek(0x0C, SeekOrigin.Begin); WriteUintBigEndian(fs, 0x00000000);
                    fs.Seek(0xA0, SeekOrigin.Begin); WriteUintBigEndian(fs, magic);
                }
                Console.WriteLine("✅ engine.ps3 patched successfully with TIE culling protection applied.");
            }
            catch (Exception ex) { Console.WriteLine($"❌ Error while patching engine.ps3: {ex.Message}"); }

            // Declare verificationLevel so it's accessible later
            Level? verificationLevel = null;

            Console.WriteLine("\n=== Post-Processing Verification ===");
            try
            {
                verificationLevel = new Level(outputEngineFile);
                Console.WriteLine($"GameType reported by loaded level: {verificationLevel.game.num}");
                Console.WriteLine($"Terrain fragments: {verificationLevel.terrainEngine?.fragments?.Count ?? 0}");
                Console.WriteLine($"Total mobys: {verificationLevel.mobs?.Count ?? 0}");
                Console.WriteLine($"Total ties: {verificationLevel.ties?.Count ?? 0}");
                Console.WriteLine($"Total shrubs: {verificationLevel.shrubs?.Count ?? 0}");

                // Fragment verification
                Console.WriteLine("\n=== Terrain Fragment ID Verification ===");
                var fragments = verificationLevel.terrainEngine?.fragments;
                if (fragments != null && fragments.Count > 0)
                {
                    var fragmentIds = fragments.Select(f => f.off1E).ToList();
                    var uniqueIds = new HashSet<ushort>(fragmentIds);
                    Console.WriteLine($"Fragment count: {fragmentIds.Count}");
                    Console.WriteLine($"Unique IDs: {uniqueIds.Count}");

                    bool properSequence = true;
                    for (int i = 0; i < fragmentIds.Count; i++)
                    {
                        if (fragmentIds[i] != i)
                        {
                            properSequence = false;
                            Console.WriteLine($"Sequence break: Fragment at index {i} has ID {fragmentIds[i]}");
                        }
                    }

                    if (properSequence)
                        Console.WriteLine("✅ Fragment IDs are in perfect sequential order");
                    else
                        Console.WriteLine("⚠️ Fragment IDs are not in sequential order");

                    Console.WriteLine("\nSample of first few fragments:");
                    for (int i = 0; i < Math.Min(5, fragments.Count); i++)
                    {
                        var frag = fragments[i];
                        Console.WriteLine($"  Fragment {i}: ID={frag.off1E}, ModelID={frag.modelID}, HasVertices={frag.model?.vertexBuffer?.Length > 0}");
                    }
                }
                else
                {
                    Console.WriteLine("No terrain fragments found in verification level");
                }

                var ratchet = verificationLevel.mobs?.FirstOrDefault(m => m.mobyID == 0);
                if (ratchet != null)
                {
                    Console.WriteLine($"Ratchet found (oClass {ratchet.mobyID}) - pos: {ratchet.position}, modelID: {ratchet.modelID}");
                }
                else
                {
                    Console.WriteLine("Warning: Ratchet (ID 0) not found in verification load!");
                }

                // Verify textures in the saved level
                VerifyTexturesForTerrainFragments(verificationLevel, "Verification Level");
            }
            catch (Exception ex) { Console.WriteLine($"❌ Error during verification: {ex.Message}"); }

            // Save result examination
            Console.WriteLine("\n=== SAVE RESULT EXAMINATION ===");
            Console.WriteLine($"Output directory exists: {Directory.Exists(outputDir)}");
            Console.WriteLine($"Engine file exists: {File.Exists(outputEngineFile)}");
            Console.WriteLine($"Engine file size: {(File.Exists(outputEngineFile) ? new FileInfo(outputEngineFile).Length : 0)} bytes");
            Console.WriteLine($"Gameplay file exists: {File.Exists(Path.Combine(outputDir, "gameplay_ntsc"))}");
            Console.WriteLine($"Gameplay file size: {(File.Exists(Path.Combine(outputDir, "gameplay_ntsc")) ? new FileInfo(Path.Combine(outputDir, "gameplay_ntsc")).Length : 0)} bytes");

            // Try to open the files directly to see if there's a basic format issue
            try
            {
                using (var fs = File.OpenRead(outputEngineFile))
                {
                    byte[] header = new byte[16];
                    fs.Read(header, 0, 16);
                    Console.WriteLine($"  Engine header first 16 bytes: {BitConverter.ToString(header)}");
                }

                string gameplayPath = Path.Combine(outputDir, "gameplay_ntsc");
                if (File.Exists(gameplayPath))
                {
                    using (var fs = File.OpenRead(gameplayPath))
                    {
                        byte[] header = new byte[16];
                        fs.Read(header, 0, 16);
                        Console.WriteLine($"  Gameplay header first 16 bytes: {BitConverter.ToString(header)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error examining saved files: {ex.Message}");
            }

            // === Diagnostics ===
            string diagnosticsDir = Path.Combine(outputDir, "diagnostics");
            Directory.CreateDirectory(diagnosticsDir);
            Console.WriteLine("\n=== Running Detailed Terrain Diagnostics ===");

            if (sourceLevel != null)
                TerrainDiagnostics.AnalyzeLevel(sourceLevel, diagnosticsDir, "RC1_Source");

            if (rc3DonorLevel != null)
                TerrainDiagnostics.AnalyzeLevel(rc3DonorLevel, diagnosticsDir, "RC2_Donor");

            if (referenceRc3PlanetLevel != null)
            {
                TerrainDiagnostics.AnalyzeLevel(referenceRc3PlanetLevel, diagnosticsDir, "RC2_Reference");
            }

            if (verificationLevel != null)
            {
                TerrainDiagnostics.AnalyzeLevel(verificationLevel, diagnosticsDir, "Output_Level");

                if (rc3DonorLevel != null)
                    TerrainDiagnostics.CompareLevels(rc3DonorLevel, verificationLevel, diagnosticsDir, "RC2_Donor", "Output_Level");

                if (sourceLevel != null)
                    TerrainDiagnostics.CompareLevels(sourceLevel, verificationLevel, diagnosticsDir, "RC1_Source", "Output_Level");
            }
            else
            {
                Console.WriteLine("⚠️ Skipping verification level analysis as the level could not be loaded");
            }

            // Ask if user wants to analyze all RC3/UYA levels
            if (GetYesNoInput("\nWould you like to analyze all Up Your Arsenal levels for comparison? (Y/N): "))
            {
                string? rc3DataPath = Path.GetDirectoryName(Path.GetDirectoryName(globalRc3Dir)); // Should point to ps3data
                string allLevelsOutputDir = Path.Combine(outputDir, "all_rc3_levels_analysis");

                Console.WriteLine("\n=== Running Analysis on All Up Your Arsenal Levels ===");
                Console.WriteLine($"Looking in: {rc3DataPath ?? "Unknown path"}");

                // Check if the directory exists and has levels
                if (string.IsNullOrEmpty(rc3DataPath) || !Directory.Exists(rc3DataPath))
                {
                    Console.WriteLine($"❌ RC3/UYA data directory not found: {rc3DataPath ?? "Unknown path"}");
                }
                else
                {
                    // Generate a comprehensive analysis of patterns across all RC3/UYA levels
                    TerrainDiagnostics.GenerateRC2PatternsReport(rc3DataPath, allLevelsOutputDir);

                    // Analyze individual levels if user wants detailed reports
                    if (GetYesNoInput("Would you also like detailed reports for each individual level? (Y/N): "))
                    {
                        int maxLevels = 0;
                        Console.Write("Enter maximum number of levels to analyze (0 for all): ");
                        int.TryParse(Console.ReadLine() ?? "0", out maxLevels);

                        TerrainDiagnostics.AnalyzeAllRC2Levels(rc3DataPath, allLevelsOutputDir, maxLevels);
                    }

                    Console.WriteLine($"✅ All RC3/UYA levels analysis complete. Results in {allLevelsOutputDir}");
                }
            }

            // Run comprehensive rotation test if ship animation was swapped
            if ((options & SwapOptions.SwapShipExitAnim) != 0)
            {
                Console.WriteLine("\n🧪 Running Comprehensive Rotation Test...");
                SwapShipExitAnim.ComprehensiveRotationTest(outputDir);

                // 🆕 ADD THIS NEW ANALYSIS
                Console.WriteLine("\n🔍 ANALYZING UYA DONOR LEVEL FOR COMPARISON...");

                string uyaDonorEnginePath = Path.Combine(rc3DonorLevelDir, "engine.ps3");
                if (File.Exists(uyaDonorEnginePath))
                {
                    Console.WriteLine($"Loading UYA donor level: {uyaDonorEnginePath}");
                    try
                    {
                        var uyaDonorLevel = new Level(uyaDonorEnginePath);
                        Console.WriteLine($"UYA Donor Level loaded successfully:");
                        Console.WriteLine($"  Game Type: {uyaDonorLevel.game?.Name ?? "Unknown"}");
                        Console.WriteLine($"  Total Cuboids: {uyaDonorLevel.cuboids?.Count ?? 0}");

                        // Run the comprehensive test on the donor level
                        SwapShipExitAnim.TestMatrixStorageFormats(uyaDonorLevel, "UYA Donor Level");
                        SwapShipExitAnim.ValidateRC3CuboidFormat(uyaDonorLevel);

                        // Analyze ship camera data if it exists
                        SwapShipExitAnim.AnalyzeShipCameraData(uyaDonorLevel, "UYA_Donor");

                        Console.WriteLine("\n=== UYA DONOR vs SAVED LEVEL COMPARISON ===");
                        SwapShipExitAnim.CompareUyaDonorVsSavedLevel(uyaDonorLevel, outputDir);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error loading UYA donor level: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"⚠️ UYA donor level not found at: {uyaDonorEnginePath}");
                }
            }

            SaveConfig(config);
            Console.WriteLine("\n✅ Geometry Swapper Done! Test the output in RPCS3.");
        }
    }
}
