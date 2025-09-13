using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeometrySwapper
{
    /// <summary>
    /// Provides robust methods for saving level data.
    /// </summary>
    public static class SaveHelper
    {
        /// <summary>
        /// Prepares the level for saving by fixing inconsistencies and logs diagnostics.
        /// </summary>
        private static void PrepareLevelForSave(Level level)
        {
            Console.WriteLine("\n=== Preparing Level For Save ===");

            // Diagnostics: Print summary of key collections
            Console.WriteLine($"  Mobys: {level.mobs?.Count ?? 0}");
            Console.WriteLine($"  Ties: {level.ties?.Count ?? 0}");
            Console.WriteLine($"  Shrubs: {level.shrubs?.Count ?? 0}");
            Console.WriteLine($"  GrindPaths: {level.grindPaths?.Count ?? 0}");
            Console.WriteLine($"  Cuboids: {level.cuboids?.Count ?? 0}");
            Console.WriteLine($"  Splines: {level.splines?.Count ?? 0}");
            Console.WriteLine($"  Cameras: {level.gameCameras?.Count ?? 0}");
            Console.WriteLine($"  TerrainChunks: {level.terrainChunks?.Count ?? 0}");
            Console.WriteLine($"  CollisionChunks: {level.collisionChunks?.Count ?? 0}");
            Console.WriteLine($"  Textures: {level.textures?.Count ?? 0}");

            // Warn if any critical collection is empty
            if (level.mobs == null || level.mobs.Count == 0) Console.WriteLine("  ⚠️ WARNING: Mobys collection is empty!");
            if (level.ties == null || level.ties.Count == 0) Console.WriteLine("  ⚠️ WARNING: Ties collection is empty!");
            if (level.shrubs == null || level.shrubs.Count == 0) Console.WriteLine("  ⚠️ WARNING: Shrubs collection is empty!");
            if (level.grindPaths == null || level.grindPaths.Count == 0) Console.WriteLine("  ⚠️ WARNING: GrindPaths collection is empty!");
            if (level.cuboids == null || level.cuboids.Count == 0) Console.WriteLine("  ⚠️ WARNING: Cuboids collection is empty!");
            if (level.splines == null || level.splines.Count == 0) Console.WriteLine("  ⚠️ WARNING: Splines collection is empty!");
            if (level.gameCameras == null || level.gameCameras.Count == 0) Console.WriteLine("  ⚠️ WARNING: Cameras collection is empty!");
            if (level.textures == null || level.textures.Count == 0) Console.WriteLine("  ⚠️ WARNING: Textures collection is empty!");

            // Check for duplicate moby IDs
            if (level.mobs != null)
            {
                var mobyIdGroups = level.mobs.GroupBy(m => m.mobyID).Where(g => g.Count() > 1).ToList();
                if (mobyIdGroups.Count > 0)
                {
                    Console.WriteLine($"  ⚠️ Duplicate mobyIDs detected: {string.Join(", ", mobyIdGroups.Select(g => g.Key))}");
                }
            }
            // Check for duplicate cuboid IDs
            if (level.cuboids != null)
            {
                var cuboidIdGroups = level.cuboids.GroupBy(c => c.id).Where(g => g.Count() > 1).ToList();
                if (cuboidIdGroups.Count > 0)
                {
                    Console.WriteLine($"  ⚠️ Duplicate cuboid IDs detected: {string.Join(", ", cuboidIdGroups.Select(g => g.Key))}");
                }
            }
            // Note: Tie does not have a unique ID property accessible here, so skip duplicate check for ties.

            // 1. Make sure mobyIds are properly synced
            if (level.mobs != null)
            {
                level.mobyIds = level.mobs.Select(m => m.mobyID).ToList();
            }

            // 2. Restore and validate grind paths from the original file to prevent data loss.
            // This is the critical step to prevent grind paths from being deleted.
            GrindPathSwapper.ValidateGrindPathReferences(level);

            // 3. Ensure other critical collections are not null
            if (level.pVars == null) level.pVars = new List<byte[]>();

            // 4. Clear chunk data to prevent saving them
            level.terrainChunks = new List<Terrain>();
            level.collisionChunks = new List<Collision>();
            level.collBytesChunks = new List<byte[]>();
            if (level.levelVariables != null)
            {
                level.levelVariables.chunkCount = 0;
            }

            // 5. Update transform matrices for all mobys
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
        /// Safely saves the level using a robust method, with diagnostics.
        /// </summary>
        /// <param name="level">The level to save</param>
        /// <param name="outputPath">Path where the level should be saved (should be the engine.ps3 file)</param>
        /// <returns>True if save was successful</returns>
        public static bool SaveLevelSafely(Level level, string outputPath)
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

                // Save the level using the standard Level.Save method
                Console.WriteLine($"Saving level to {directory}...");
                level.Save(directory);

                // Diagnostics: Post-save verification
                string engineFile = Path.Combine(directory, "engine.ps3");
                if (File.Exists(engineFile))
                {
                    var fileInfo = new FileInfo(engineFile);
                    Console.WriteLine($"  [DIAG] engine.ps3 exists, size: {fileInfo.Length} bytes");
                }
                else
                {
                    Console.WriteLine("  ❌ [DIAG] engine.ps3 file not found after save!");
                }
                string gameplayFile = Path.Combine(directory, "gameplay_ntsc");
                if (File.Exists(gameplayFile))
                {
                    var fileInfo = new FileInfo(gameplayFile);
                    Console.WriteLine($"  [DIAG] gameplay_ntsc exists, size: {fileInfo.Length} bytes");
                }
                else
                {
                    Console.WriteLine("  ❌ [DIAG] gameplay_ntsc file not found after save!");
                }

                // Patch the engine header with required values for each supported game
                if (level != null)
                {
                    string outputEngineFile = Path.Combine(directory, "engine.ps3");
                    Console.WriteLine("Patching engine.ps3 header values...");
                    try
                    {
                        using (var fs = File.Open(outputEngineFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            bool patched = true;
                            switch (level.game.num)
                            {
                                case 1:
                                    fs.Seek(0x08, SeekOrigin.Begin); WriteUintBigEndian(fs, 0x00010003);
                                    fs.Seek(0x0C, SeekOrigin.Begin); WriteUintBigEndian(fs, 0x00000000);
                                    fs.Seek(0xA0, SeekOrigin.Begin); WriteUintBigEndian(fs, 0x00000001);
                                    break;
                                case 2:
                                    fs.Seek(0x08, SeekOrigin.Begin); WriteUintBigEndian(fs, 0x00020003);
                                    fs.Seek(0x0C, SeekOrigin.Begin); WriteUintBigEndian(fs, 0x00000000);
                                    fs.Seek(0xA0, SeekOrigin.Begin); WriteUintBigEndian(fs, 0xEAA90001);
                                    break;
                                case 3:
                                    fs.Seek(0x08, SeekOrigin.Begin); WriteUintBigEndian(fs, 0x00030003);
                                    fs.Seek(0x0C, SeekOrigin.Begin); WriteUintBigEndian(fs, 0x00000000);
                                    fs.Seek(0xA0, SeekOrigin.Begin); WriteUintBigEndian(fs, 0xEAA60001);
                                    break;
                                default:
                                    Console.WriteLine($"⚠️ Unsupported game type {level.game.num}; engine.ps3 not patched.");
                                    patched = false;
                                    break;
                            }
                            if (patched)
                                Console.WriteLine("✅ engine.ps3 patched successfully.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error while patching engine.ps3: {ex.Message}");
                    }
                }

                Console.WriteLine("✅ Level saved successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during safe save: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                // Diagnostics: Print summary of level state on error
                Console.WriteLine($"  [DIAG] Mobys: {level.mobs?.Count ?? 0}");
                Console.WriteLine($"  [DIAG] Ties: {level.ties?.Count ?? 0}");
                Console.WriteLine($"  [DIAG] Shrubs: {level.shrubs?.Count ?? 0}");
                Console.WriteLine($"  [DIAG] GrindPaths: {level.grindPaths?.Count ?? 0}");
                Console.WriteLine($"  [DIAG] Cuboids: {level.cuboids?.Count ?? 0}");
                Console.WriteLine($"  [DIAG] Splines: {level.splines?.Count ?? 0}");
                Console.WriteLine($"  [DIAG] Cameras: {level.gameCameras?.Count ?? 0}");
                Console.WriteLine($"  [DIAG] TerrainChunks: {level.terrainChunks?.Count ?? 0}");
                Console.WriteLine($"  [DIAG] CollisionChunks: {level.collisionChunks?.Count ?? 0}");
                Console.WriteLine($"  [DIAG] Textures: {level.textures?.Count ?? 0}");
                return false;
            }
        }
    }
}
