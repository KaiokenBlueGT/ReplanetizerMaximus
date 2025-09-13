using System;
using System.Collections.Generic;
using System.IO;
using LibReplanetizer;
using LibReplanetizer.Headers;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using LibReplanetizer.Parsers;
using LibReplanetizer.Serializers; // Add this namespace
using static LibReplanetizer.DataFunctions;
using System.Numerics;
using System.Linq;
using OpenTK.Mathematics;

namespace MobyConverter
{
    internal class Program
    {
        static List<byte[]> LoadPvars(GameplayHeader header, FileStream fs)
        {
            var pVars = new List<byte[]>();
            if (header.pvarSizePointer == 0 || header.pvarPointer == 0)
                return pVars;
            byte[] pVarSizes = ReadBlock(fs, header.pvarSizePointer, header.pvarPointer - header.pvarSizePointer);
            int pVarSizeBlockSize = ReadInt(pVarSizes, pVarSizes.Length - 0x08) + ReadInt(pVarSizes, pVarSizes.Length - 0x04);
            if (pVarSizeBlockSize == 0)
                pVarSizeBlockSize = ReadInt(pVarSizes, pVarSizes.Length - 0x10) + ReadInt(pVarSizes, pVarSizes.Length - 0x0C);
            int pvarCount = pVarSizes.Length / 0x08;
            byte[] pVarBlock = ReadBlock(fs, header.pvarPointer, pVarSizeBlockSize);
            for (int i = 0; i < pvarCount; i++)
            {
                uint start = ReadUint(pVarSizes, i * 8);
                uint count = ReadUint(pVarSizes, i * 8 + 4);
                pVars.Add(GetBytes(pVarBlock, (int) start, (int) count));
            }
            return pVars;
        }

        static List<Moby> LoadRc1Mobies(GameplayHeader header, FileStream fs, List<Model> models, List<byte[]> pvars)
        {
            var mobies = new List<Moby>();
            if (header.mobyPointer == 0)
                return mobies;
            int count = ReadInt(ReadBlock(fs, header.mobyPointer, 4), 0);
            byte[] block = ReadBlock(fs, header.mobyPointer + 0x10, GameType.RaC1.mobyElemSize * count);
            for (int i = 0; i < count; i++)
            {
                mobies.Add(new Moby(GameType.RaC1, block, i, models, pvars));
            }
            return mobies;
        }

        /// <summary>
        /// Ensures every pVar block is at least 0x80 bytes long.
        /// </summary>
        private static void EnsureFullPvarBlocks(List<byte[]> pvars)
        {
            if (pvars == null) return;
            for (int i = 0; i < pvars.Count; i++)
            {
                var block = pvars[i];
                if (block == null)
                {
                    pvars[i] = new byte[0x80];
                }
                else if (block.Length < 0x80)
                {
                    byte[] expanded = new byte[0x80];
                    Array.Copy(block, expanded, block.Length);
                    pvars[i] = expanded;
                }
            }
        }


        static Moby ConvertToRc2(Moby rc1, List<byte[]> allPvars, Moby? rc3Template, Level targetLevel)
        {
            try
            {
                // If no template is found, or the source moby has no model, return a basic moby to avoid crashes.
                // This moby will be static as it lacks correct mode/pVar data.
                if (rc3Template == null || rc1.model == null)
                {
                    Console.WriteLine($"⚠️ No RC3 template or source model for Moby ID {rc1.modelID}, creating basic instance.");
                    // The constructor expects a non-null model, so if rc1.model is null, we can't create a fallback.
                    // Returning a new empty Moby is safer.
                    if (rc1.model == null)
                    {
                        return new Moby();
                    }
                    var fallbackMoby = new Moby(GameType.RaC3, rc1.model, rc1.position, rc1.rotation, rc1.scale)
                    {
                        mobyID = rc1.mobyID,
                        pvarIndex = -1 // No pVars if no template
                    };
                    return fallbackMoby;
                }

                // Create a new Moby by cloning the RC3 template.
                // This copies all RC3-native properties like mode, spawnType, etc.
                var rc3 = new Moby(rc3Template);

                // Overwrite instance-specific data from the RC1 moby.
                rc3.position = rc1.position;
                rc3.rotation = rc1.rotation;
                rc3.scale = rc1.scale;
                rc3.mobyID = rc1.mobyID; // Preserve the original ID
                rc3.color = rc1.color;
                rc3.light = rc1.light;

                // --- pVar Conversion Logic ---
                // Use the RC3 template's pVars as a base to ensure correct structure.
                byte[] newPVars = (byte[])(rc3Template.pVars?.Clone() ?? Array.Empty<byte>());

                // If the source RC1 moby has pVars, selectively copy compatible fields.
                if (rc1.pvarIndex != -1 && rc1.pvarIndex < allPvars.Count)
                {
                    byte[] rc1PVars = allPvars[rc1.pvarIndex];

                    // EXAMPLE: For a simple crate, the first 4 bytes might be the bolt count.
                    // This is where specific, per-moby-type logic would go.
                    // For now, we'll copy the first 16 bytes as a starting point,
                    // as these often contain common, simple values.
                    int bytesToCopy = Math.Min(16, Math.Min(newPVars.Length, rc1PVars.Length));
                    if (bytesToCopy > 0)
                    {
                        Buffer.BlockCopy(rc1PVars, 0, newPVars, 0, bytesToCopy);
                        Console.WriteLine($"  - Converted pVars for moby {rc1.mobyID} (model {rc1.modelID}) using template, copied {bytesToCopy} bytes.");
                    }
                }
                
                rc3.pVars = newPVars;

                // The pvarIndex will be reassigned later by the validation logic.
                // For now, add the new pVars to the level's collection.
                if (targetLevel.pVars == null) targetLevel.pVars = new List<byte[]>();
                targetLevel.pVars.Add(rc3.pVars);
                rc3.pvarIndex = targetLevel.pVars.Count - 1;

                // Ensure the model reference is correct
                rc3.model = rc1.model;
                rc3.modelID = rc1.modelID;

                rc3.UpdateTransformMatrix();
                return rc3;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting moby: {ex.Message}");
                var fallbackMoby = new Moby(GameType.RaC3, new byte[GameType.RaC3.mobyElemSize], 0, new List<Model>(), new List<byte[]>());
                fallbackMoby.mobyID = rc1.mobyID;
                fallbackMoby.modelID = rc1.modelID;
                fallbackMoby.position = rc1.position;
                fallbackMoby.rotation = rc1.rotation;
                fallbackMoby.scale = rc1.scale;
                fallbackMoby.pvarIndex = -1;
                return fallbackMoby;
            }
        }

        /// <summary>
        /// Safely copies a block from one array to another, checking bounds on both arrays
        /// </summary>
        private static void SafeCopyBlock(byte[] source, int srcOffset, byte[] dest, int destOffset, int length)
        {
            if (source == null || dest == null ||
                srcOffset < 0 || destOffset < 0 || length <= 0 ||
                srcOffset + length > source.Length || destOffset + length > dest.Length)
                return;

            Buffer.BlockCopy(source, srcOffset, dest, destOffset, length);
        }

        /// <summary>
        /// Safely copies a ushort value from one array to another, checking bounds on both arrays
        /// </summary>
        private static void SafeCopyUshort(byte[] source, int srcOffset, byte[] dest, int destOffset)
        {
            if (source == null || dest == null ||
                srcOffset < 0 || destOffset < 0 ||
                srcOffset + 2 > source.Length || destOffset + 2 > dest.Length)
                return;

            WriteUshort(dest, destOffset, ReadUshort(source, srcOffset));
        }

        /// <summary>
        /// Helper to read a ushort value from a specific byte array offset
        /// </summary>
        private static ushort ReadUshort(byte[] buffer, int offset)
        {
            return (ushort) ((buffer[offset + 1] << 8) | buffer[offset]);
        }

        /// <summary>
        /// Helper to write a ushort value to a specific byte array offset
        /// </summary>
        private static void WriteUshort(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte) (value & 0xFF);
            buffer[offset + 1] = (byte) (value >> 8);
        }

        /// <summary>
        /// Helper method to write a signed byte value to a specific offset in a byte array
        /// </summary>
        private static void WriteSbyte(byte[] buffer, int offset, sbyte value)
        {
            buffer[offset] = (byte) value;
        }

        /// <summary>
        /// Helper method to write a byte value to a specific offset in a byte array
        /// </summary>
        private static void WriteByte(byte[] buffer, int offset, byte value)
        {
            buffer[offset] = value;
        }

        /// <summary>
        /// Helper method to write an unsigned long value to a specific offset in a byte array
        /// </summary>
        private static void WriteUlong(byte[] buffer, int offset, ulong value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            Array.Copy(bytes, 0, buffer, offset, 8);
        }

        /// <summary>
        /// Initializes empty collections for the given level.
        /// </summary>
        private static void InitializeEmptyCollections(Level rc2Level)
        {
            rc2Level.english = new List<LanguageData>();
            rc2Level.ukenglish = new List<LanguageData>();
            rc2Level.french = new List<LanguageData>();
            rc2Level.german = new List<LanguageData>();
            rc2Level.spanish = new List<LanguageData>();
            rc2Level.italian = new List<LanguageData>();
            rc2Level.japanese = new List<LanguageData>();
            rc2Level.korean = new List<LanguageData>();
            rc2Level.lights = new List<Light>();
            rc2Level.directionalLights = new List<DirectionalLight>();
            rc2Level.pointLights = new List<PointLight>();
            rc2Level.envSamples = new List<EnvSample>();
            rc2Level.gameCameras = new List<GameCamera>();
            rc2Level.soundInstances = new List<SoundInstance>();
            rc2Level.envTransitions = new List<EnvTransition>();
            rc2Level.cuboids = new List<Cuboid>();
            rc2Level.spheres = new List<Sphere>();
            rc2Level.cylinders = new List<Cylinder>();
            rc2Level.splines = new List<Spline>();
            rc2Level.grindPaths = new List<GrindPath>();
            rc2Level.pVars = new List<byte[]>();
            rc2Level.type50s = new List<KeyValuePair<int, int>>();
            rc2Level.type5Cs = new List<KeyValuePair<int, int>>();
            rc2Level.tieIds = new List<int>();
            rc2Level.shrubIds = new List<int>();
            rc2Level.tieData = new byte[0];
            rc2Level.shrubData = new byte[0];
            rc2Level.unk6 = new byte[0];
            rc2Level.unk7 = new byte[0];
            rc2Level.unk14 = new byte[0];
            rc2Level.unk17 = new byte[0];
            rc2Level.tieGroupData = new byte[0];
            rc2Level.shrubGroupData = new byte[0];
            rc2Level.areasData = new byte[0];
        }

        /// <summary>
        /// Saves a level without generating chunk files, only creating engine.ps3 and gameplay_ntsc files.
        /// This avoids crashes when chunk data might not be properly initialized.
        /// </summary>
        /// <param name="level">The level to save</param>
        /// <param name="outputPath">The directory where the level files should be saved</param>
        private static void SaveWithoutChunks(Level level, string outputPath)
        {
            string? directory;
            if (File.Exists(outputPath) && File.GetAttributes(outputPath).HasFlag(FileAttributes.Directory))
            {
                directory = outputPath;
            }
            else
            {
                directory = Path.GetDirectoryName(outputPath);
            }

            if (directory == null) return;

            // Set chunkCount to 0 to indicate no chunks
            if (level.levelVariables != null)
            {
                level.levelVariables.chunkCount = 0;
            }

            // Empty terrain and collision chunks to ensure none get created
            level.terrainChunks = new List<Terrain>();
            level.collisionChunks = new List<Collision>();
            level.collBytesChunks = new List<byte[]>();

            // Only save engine and gameplay files, skip chunks
            Console.WriteLine($"Saving level to {directory} (engine and gameplay only, no chunks)...");
            EngineSerializer engineSerializer = new EngineSerializer();
            engineSerializer.Save(level, directory);
            GameplaySerializer gameplaySerializer = new GameplaySerializer();
            gameplaySerializer.Save(level, directory);

            Console.WriteLine("Level saved successfully without chunks");
        }

        public static void RunMobyConverter(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: MobyConverter <rc1 engine.ps3> <output directory>");
                return;
            }

            string rc1EnginePath = args[0];
            string outputDir = args[1];
            Directory.CreateDirectory(outputDir);

            Console.WriteLine($"Starting conversion using RC1 level '{rc1EnginePath}'...");
            string donorRc2Path = @"C:\Users\Ryan_\Downloads\temp\Oltanis_RaC1\engine.ps3";

            try
            {
                Console.WriteLine($"Loading RC1 level from '{rc1EnginePath}'...");
                Level rc1Level = new Level(rc1EnginePath);
                EnsureFullPvarBlocks(rc1Level.pVars);
                if (rc1Level.game.num != 1)
                {
                    Console.WriteLine("❌ Error: The provided source level is not a Ratchet & Clank 1 level.");
                    return;
                }
                Console.WriteLine($"✅ Loaded RC1 level with {rc1Level.mobs?.Count ?? 0} mobys and {rc1Level.pVars?.Count ?? 0} pVar blocks.");

                if (!File.Exists(donorRc2Path))
                {
                    Console.WriteLine($"❌ Error: Donor RC3 level not found at '{donorRc2Path}'. This file is required.");
                    return;
                }
                Console.WriteLine($"Loading donor RC3 level from '{donorRc2Path}' for assets and structure...");
                Level donorRc2Level = new Level(donorRc2Path);
                if (donorRc2Level.game.num != 2)
                {
                    Console.WriteLine("❌ Error: The donor level is not a Ratchet & Clank 2 level.");
                    return;
                }
                Console.WriteLine($"✅ Loaded donor RC3 level with {donorRc2Level.mobyModels?.Count ?? 0} moby models.");

                var donorModelMap = donorRc2Level.mobyModels?.ToDictionary(m => m.id, m => m) ?? new Dictionary<short, Model>();
                var donorMobyMap = donorRc2Level.mobs?.GroupBy(m => m.modelID).ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<int, Moby>();

                Console.WriteLine("Converting and refining mobys...");
                var finalRc2Mobies = new List<Moby>();
                int successCount = 0;
                int errorCount = 0;

                if (rc1Level.mobs != null && rc1Level.pVars != null)
                {
                    foreach (var rc1Moby in rc1Level.mobs)
                    {
                        try
                        {
                            // Find a template moby from the donor level
                            donorMobyMap.TryGetValue(rc1Moby.modelID, out Moby? donorTemplate);

                            // Pass the full pVars list and the donor template to the converter
                            Moby convertedMoby = ConvertToRc2(rc1Moby, rc1Level.pVars, donorTemplate, donorRc2Level);

                            if (donorModelMap.TryGetValue((short) convertedMoby.modelID, out Model? donorModel) && donorModel != null)
                            {
                                convertedMoby.model = donorModel;
                            }
                            else
                            {
                                Console.WriteLine($"⚠️ Warning: Model ID {convertedMoby.modelID} for moby oClass {convertedMoby.mobyID} not found in donor level. The model may be missing.");
                            }

                            finalRc2Mobies.Add(convertedMoby);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Error during conversion of moby oClass {rc1Moby.mobyID}: {ex.Message}");
                            errorCount++;
                        }
                    }
                }
                Console.WriteLine($"✅ Conversion process complete. Successfully converted {successCount} mobys with {errorCount} errors.");

                Level outputLevel = donorRc2Level;
                outputLevel.path = rc1EnginePath;
                outputLevel.mobs = finalRc2Mobies;
                outputLevel.mobyIds = finalRc2Mobies.Select(m => m.mobyID).ToList();
                
                // Ensure pVars are handled correctly
                if (rc1Level.pVars != null)
                {
                    outputLevel.pVars = rc1Level.pVars;
                    EnsureFullPvarBlocks(outputLevel.pVars);
                }
                else
                {
                    outputLevel.pVars = new List<byte[]>();
                }


                Console.WriteLine($"\nSaving converted level to '{outputDir}'...");
                SaveWithoutChunks(outputLevel, outputDir);
                Console.WriteLine($"\n✅ Level saved successfully to '{outputDir}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An unexpected error occurred: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
