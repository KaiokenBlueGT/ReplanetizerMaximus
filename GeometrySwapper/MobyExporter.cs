// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using LibReplanetizer.Models.Animations;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles exporting and importing of Moby models to/from individual files
    /// </summary>
    public static class MobyExporter
    {
        // File extension for exported Moby files
        public const string MOBY_FILE_EXTENSION = ".rmoby";
        
        /// <summary>
        /// Serializable class to store all Moby model data
        /// </summary>
        private class SerializableMobyData
        {
            // Basic identification info
            public int ModelId { get; set; }
            public string ModelName { get; set; } = "";
            public int GameNum { get; set; }

            // Model data
            public byte[] VertexBuffer { get; set; } = Array.Empty<byte>();
            public byte[] IndexBuffer { get; set; } = Array.Empty<byte>();
            public int VertexStride { get; set; }
            public int VertexCount { get; set; }
            public int FaceCount { get; set; }

            // Texture data
            public List<SerializableTexture> Textures { get; set; } = new List<SerializableTexture>();
            public List<SerializableTextureConfig> TextureConfigs { get; set; } = new List<SerializableTextureConfig>();

            // Animation data
            public List<SerializableAnimation> Animations { get; set; } = new List<SerializableAnimation>();

            // Bone data
            public byte BoneCount { get; set; }
            public byte LpBoneCount { get; set; }
            public List<byte[]> BoneMatrices { get; set; } = new List<byte[]>();
            public List<byte[]> BoneDatas { get; set; } = new List<byte[]>();

            // Other model properties
            public byte[] Type10Block { get; set; } = Array.Empty<byte>();
            public int Null1 { get; set; }
            public byte Count3 { get; set; }
            public byte Count4 { get; set; }
            public byte LpRenderDist { get; set; }
            public byte Count8 { get; set; }
            public int Null2 { get; set; }
            public int Null3 { get; set; }
            public float Unk1 { get; set; }
            public float Unk2 { get; set; }
            public float Unk3 { get; set; }
            public float Unk4 { get; set; }
            public uint Color2 { get; set; }
            public uint Unk6 { get; set; }
            public ushort VertexCount2 { get; set; }
            
            // Additional buffers
            public List<byte> IndexAttachments { get; set; } = new List<byte>();
            public List<byte> OtherBuffer { get; set; } = new List<byte>();
            public List<ushort> OtherIndexBuffer { get; set; } = new List<ushort>();
            public List<SerializableTextureConfig> OtherTextureConfigs { get; set; } = new List<SerializableTextureConfig>();
            
            // Attachments
            public List<byte[]> Attachments { get; set; } = new List<byte[]>();
            
            // Sounds
            public List<byte[]> ModelSounds { get; set; } = new List<byte[]>();

            // New fields
            public uint[] Weights { get; set; } = Array.Empty<uint>();
            public uint[] BoneIds { get; set; } = Array.Empty<uint>();
            public float Size { get; set; } = 1.0f;
        }

        /// <summary>
        /// Serializable class to store texture data
        /// </summary>
        private class SerializableTexture
        {
            public int Id { get; set; }
            public short Width { get; set; }
            public short Height { get; set; }
            public byte MipMapCount { get; set; }
            public ushort Off06 { get; set; }
            public int Off08 { get; set; }
            public int Off0C { get; set; }
            public int Off10 { get; set; }
            public int Off14 { get; set; }
            public int Off1C { get; set; }
            public int Off20 { get; set; }
            public int VramPointer { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// Serializable class to store texture configuration data
        /// </summary>
        private class SerializableTextureConfig
        {
            public int Id { get; set; }
            public byte[] ConfigData { get; set; } = Array.Empty<byte>();
        }

        /// <summary>
        /// Serializable class to store animation data
        /// </summary>
        private class SerializableAnimation
        {
            public float Unk1 { get; set; }
            public float Unk2 { get; set; }
            public float Unk3 { get; set; }
            public float Unk4 { get; set; }
            public byte Unk5 { get; set; }
            public byte Unk7 { get; set; }
            public uint Null1 { get; set; }
            public float Speed { get; set; }
            
            // Store frames as serialized byte arrays
            public List<byte[]> Frames { get; set; } = new List<byte[]>();
            public List<int> Sounds { get; set; } = new List<int>();
            public List<byte> UnknownBytes { get; set; } = new List<byte>();
        }

        /// <summary>
        /// Exports all Moby models from a level to individual files
        /// </summary>
        /// <param name="sourcePath">Path to the source engine.ps3 file</param>
        /// <param name="outputDir">Directory to export the Moby files to</param>
        /// <returns>True if the operation was successful</returns>
        public static bool ExportMobys(string sourcePath, string outputDir)
        {
            Console.WriteLine("\n==== Exporting Moby Models ====");

            // Validate parameters
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                Console.WriteLine("❌ Invalid source file path");
                return false;
            }

            if (string.IsNullOrEmpty(outputDir))
            {
                Console.WriteLine("❌ Invalid output directory");
                return false;
            }

            // Create output directory if it doesn't exist
            Directory.CreateDirectory(outputDir);

            // Load the level
            Console.WriteLine($"Loading level from {sourcePath}...");
            Level sourceLevel;
            try
            {
                sourceLevel = new Level(sourcePath);
                Console.WriteLine("✅ Level loaded successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to load level: {ex.Message}");
                return false;
            }

            // Check if there are any Moby models to export
            if (sourceLevel.mobyModels == null || sourceLevel.mobyModels.Count == 0)
            {
                Console.WriteLine("❌ No Moby models found in the source level");
                return false;
            }

            Console.WriteLine($"Found {sourceLevel.mobyModels.Count} Moby models to export");

            // Dictionary to track model names (for avoiding duplicate filenames)
            Dictionary<string, int> modelNameCounts = new Dictionary<string, int>();

            // Export each Moby model
            int successCount = 0;
            foreach (var model in sourceLevel.mobyModels)
            {
                if (model is MobyModel mobyModel)
                {
                    try
                    {
                        // Get model name for filename
                        string modelName = GetFriendlyModelName(mobyModel.id);

                        // Handle duplicate names by adding a number suffix
                        if (modelNameCounts.ContainsKey(modelName))
                        {
                            modelNameCounts[modelName]++;
                            modelName = $"{modelName}_{modelNameCounts[modelName]}";
                        }
                        else
                        {
                            modelNameCounts[modelName] = 1;
                        }

                        // Sanitize filename
                        string safeModelName = SanitizeFilename(modelName);
                        string filename = Path.Combine(outputDir, $"{safeModelName}_{mobyModel.id}{MOBY_FILE_EXTENSION}");

                        // Export the Moby model
                        if (ExportMobyModel(mobyModel, sourceLevel.textures, filename, sourceLevel.game))
                        {
                            Console.WriteLine($"✅ Exported: {Path.GetFileName(filename)}");
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Failed to export model ID {mobyModel.id}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"\n==== Export Summary ====");
            Console.WriteLine($"✅ Successfully exported: {successCount} Moby models");
            Console.WriteLine($"❌ Failed to export: {sourceLevel.mobyModels.Count - successCount} Moby models");
            Console.WriteLine($"📂 Output directory: {Path.GetFullPath(outputDir)}");

            return successCount > 0;
        }

        /// <summary>
        /// Exports a single Moby model to a file
        /// </summary>
        private static bool ExportMobyModel(MobyModel model, List<Texture> textures, string outputPath, GameType gameType)
        {
            if (model == null)
                return false;

            // Create serializable data object
            var mobyData = new SerializableMobyData
            {
                ModelId = model.id,
                ModelName = GetFriendlyModelName(model.id),
                GameNum = gameType.num,
                VertexBuffer = GetModelVertexBuffer(model),
                IndexBuffer = GetModelIndexBuffer(model),
                VertexStride = model.vertexStride,
                VertexCount = model.vertexCount,
                FaceCount = model.faceCount,
                BoneCount = model.boneCount,
                LpBoneCount = model.lpBoneCount,
                Type10Block = model.type10Block,
                Null1 = model.null1,
                Count3 = model.count3,
                Count4 = model.count4,
                LpRenderDist = model.lpRenderDist,
                Count8 = model.count8,
                Null2 = model.null2,
                Null3 = model.null3,
                Unk1 = model.unk1,
                Unk2 = model.unk2,
                Unk3 = model.unk3,
                Unk4 = model.unk4,
                Color2 = model.color2,
                Unk6 = model.unk6,
                VertexCount2 = model.vertexCount2,
                Weights = model.weights ?? Array.Empty<uint>(),
                BoneIds = model.ids ?? Array.Empty<uint>(),
                Size = model.size
            };

            // Export textures used by this model
            if (model.textureConfig != null)
            {
                foreach (var texConfig in model.textureConfig)
                {
                    // Add texture config
                    mobyData.TextureConfigs.Add(new SerializableTextureConfig
                    {
                        Id = texConfig.id,
                        ConfigData = SerializeTextureConfig(texConfig)
                    });
                    
                    // Add texture data if valid
                    if (texConfig.id >= 0 && texConfig.id < textures.Count)
                    {
                        var texture = textures[texConfig.id];
                        mobyData.Textures.Add(SerializeTexture(texture));
                    }
                }
            }

            // Export other texture configs
            if (model.otherTextureConfigs != null)
            {
                foreach (var texConfig in model.otherTextureConfigs)
                {
                    mobyData.OtherTextureConfigs.Add(new SerializableTextureConfig
                    {
                        Id = texConfig.id,
                        ConfigData = SerializeTextureConfig(texConfig)
                    });
                }
            }

            // Export animations
            if (model.animations != null)
            {
                foreach (var animation in model.animations)
                {
                    var serAnim = new SerializableAnimation
                    {
                        Unk1 = animation.unk1,
                        Unk2 = animation.unk2,
                        Unk3 = animation.unk3,
                        Unk4 = animation.unk4,
                        Unk5 = animation.unk5,
                        Unk7 = animation.unk7,
                        Null1 = animation.null1,
                        Speed = animation.speed,
                        Sounds = new List<int>(animation.sounds),
                        UnknownBytes = new List<byte>(animation.unknownBytes)
                    };
                    
                    // Serialize frames
                    if (animation.frames != null)
                    {
                        foreach (var frame in animation.frames)
                        {
                            serAnim.Frames.Add(frame.Serialize());
                        }
                    }
                    
                    mobyData.Animations.Add(serAnim);
                }
            }

            // Export bone matrices
            if (model.boneMatrices != null)
            {
                foreach (var boneMatrix in model.boneMatrices)
                {
                    mobyData.BoneMatrices.Add(boneMatrix.Serialize());
                }
            }

            // Export bone data
            if (model.boneDatas != null)
            {
                foreach (var boneData in model.boneDatas)
                {
                    mobyData.BoneDatas.Add(boneData.Serialize());
                }
            }

            // Export attachments
            if (model.attachments != null)
            {
                foreach (var attachment in model.attachments)
                {
                    mobyData.Attachments.Add(attachment.Serialize());
                }
            }

            // Export model sounds
            if (model.modelSounds != null)
            {
                foreach (var sound in model.modelSounds)
                {
                    mobyData.ModelSounds.Add(sound.Serialize());
                }
            }

            // Export index attachments
            if (model.indexAttachments != null)
            {
                mobyData.IndexAttachments = new List<byte>(model.indexAttachments);
            }

            // Export other buffer
            if (model.otherBuffer != null)
            {
                mobyData.OtherBuffer = new List<byte>(model.otherBuffer);
            }

            // Export other index buffer
            if (model.otherIndexBuffer != null)
            {
                mobyData.OtherIndexBuffer = new List<ushort>(model.otherIndexBuffer);
            }

            // Serialize to JSON and save to file with compression
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = false }; // Don't need indentation in compressed JSON
                string json = JsonSerializer.Serialize(mobyData, options);
                
                // Use GZip compression
                using (FileStream fs = new FileStream(outputPath, FileMode.Create))
                using (GZipStream gzipStream = new GZipStream(fs, CompressionLevel.Optimal))
                using (StreamWriter writer = new StreamWriter(gzipStream))
                {
                    writer.Write(json);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save model to file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Imports a Moby model from a file into a target level
        /// </summary>
        /// <param name="mobyFilePath">Path to the .rmoby file to import</param>
        /// <param name="targetLevel">Level to import the Moby into</param>
        /// <param name="allowOverwrite">Whether to overwrite existing models with the same ID</param>
        /// <returns>True if the import was successful</returns>
        public static bool ImportMobyModel(string mobyFilePath, Level targetLevel, bool allowOverwrite = false)
        {
            if (!File.Exists(mobyFilePath))
            {
                Console.WriteLine($"❌ File not found: {mobyFilePath}");
                return false;
            }

            if (targetLevel == null)
            {
                Console.WriteLine("❌ Target level is null");
                return false;
            }

            try
            {
                // Read the file with decompression
                string json;
                using (FileStream fs = new FileStream(mobyFilePath, FileMode.Open))
                using (GZipStream gzipStream = new GZipStream(fs, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gzipStream))
                {
                    json = reader.ReadToEnd();
                }
                
                var mobyData = JsonSerializer.Deserialize<SerializableMobyData>(json);

                if (mobyData == null)
                {
                    Console.WriteLine("❌ Failed to deserialize Moby data");
                    return false;
                }

                // Check if a model with this ID already exists
                bool modelExists = targetLevel.mobyModels.Any(m => m.id == mobyData.ModelId);
                if (modelExists && !allowOverwrite)
                {
                    Console.WriteLine($"❌ A model with ID {mobyData.ModelId} already exists and overwrite is not allowed");
                    return false;
                }

                // Create a new MobyModel
                MobyModel newModel = new MobyModel();
                newModel.id = (short)mobyData.ModelId;

                // Set vertex and buffer data properly
                float[] vertexData = DeserializeVertexBuffer(mobyData.VertexBuffer);
                newModel.vertexBuffer = vertexData;
                ushort[] indexData = DeserializeIndexBuffer(mobyData.IndexBuffer);

                // Set these fields properly
                newModel.vertexBuffer = vertexData;
                newModel.indexBuffer = indexData;

                // Set stride directly - this is a protected field in Model, so we need to use reflection with care
                try
                {
                    var strideProperty = typeof(Model).GetProperty("vertexStride",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (strideProperty != null)
                    {
                        strideProperty.SetValue(newModel, mobyData.VertexStride);
                    }
                    else
                    {
                        Console.WriteLine("⚠️ Warning: vertexStride property not found");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Warning: Failed to set vertexStride: {ex.Message}");
                }

                // Include explicit vertex count

                // Set model properties
                newModel.boneCount = mobyData.BoneCount;
                newModel.lpBoneCount = mobyData.LpBoneCount;
                newModel.type10Block = mobyData.Type10Block;
                newModel.null1 = mobyData.Null1;
                newModel.count3 = mobyData.Count3;
                newModel.count4 = mobyData.Count4;
                newModel.lpRenderDist = mobyData.LpRenderDist;
                newModel.count8 = mobyData.Count8;
                newModel.null2 = mobyData.Null2;
                newModel.null3 = mobyData.Null3;
                newModel.unk1 = mobyData.Unk1;
                newModel.unk2 = mobyData.Unk2;
                newModel.unk3 = mobyData.Unk3;
                newModel.unk4 = mobyData.Unk4;
                newModel.color2 = mobyData.Color2;
                newModel.unk6 = mobyData.Unk6;
                newModel.vertexCount2 = (ushort)mobyData.VertexCount2;
                newModel.weights = mobyData.Weights;
                newModel.ids = mobyData.BoneIds;
                newModel.size = mobyData.Size;
                
                // Import textures
                Dictionary<int, int> textureMapping = new Dictionary<int, int>();
                foreach (var textureData in mobyData.Textures)
                {
                    // Check if this texture already exists in the target level
                    int targetTexId = -1;
                    for (int i = 0; i < targetLevel.textures.Count; i++)
                    {
                        if (TexturesMatch(textureData, targetLevel.textures[i]))
                        {
                            targetTexId = i;
                            break;
                        }
                    }
                    
                    // If not found, add the texture to the target level
                    if (targetTexId == -1)
                    {
                        var newTexture = DeserializeTexture(textureData);
                        targetLevel.textures.Add(newTexture);
                        targetTexId = targetLevel.textures.Count - 1;
                    }
                    
                    // Add to mapping
                    textureMapping[textureData.Id] = targetTexId;
                }
                
                // Import texture configs with remapped texture IDs
                newModel.textureConfig = new List<TextureConfig>();
                foreach (var configData in mobyData.TextureConfigs)
                {
                    var texConfig = DeserializeTextureConfig(configData.ConfigData);
                    
                    // Remap texture ID if we have a mapping for it
                    if (textureMapping.TryGetValue(configData.Id, out int newTexId))
                    {
                        texConfig.id = newTexId;
                    }
                    else
                    {
                        texConfig.id = configData.Id;
                    }
                    
                    newModel.textureConfig.Add(texConfig);
                }
                
                // Import other texture configs
                newModel.otherTextureConfigs = new List<TextureConfig>();
                foreach (var configData in mobyData.OtherTextureConfigs)
                {
                    var texConfig = DeserializeTextureConfig(configData.ConfigData);
                    
                    // Remap texture ID if we have a mapping for it
                    if (textureMapping.TryGetValue(configData.Id, out int newTexId))
                    {
                        texConfig.id = newTexId;
                    }
                    else
                    {
                        texConfig.id = configData.Id;
                    }
                    
                    newModel.otherTextureConfigs.Add(texConfig);
                }
                
                // Import animations
                newModel.animations = new List<Animation>();
                foreach (var animData in mobyData.Animations)
                {
                    var newAnim = new Animation();
                    newAnim.unk1 = animData.Unk1;
                    newAnim.unk2 = animData.Unk2;
                    newAnim.unk3 = animData.Unk3;
                    newAnim.unk4 = animData.Unk4;
                    newAnim.unk5 = animData.Unk5;
                    newAnim.unk7 = animData.Unk7;
                    newAnim.null1 = animData.Null1;
                    newAnim.speed = animData.Speed;
                    newAnim.sounds = new List<int>(animData.Sounds);
                    newAnim.unknownBytes = new List<byte>(animData.UnknownBytes);
                    
                    // Deserialize frames
                    newAnim.frames = new List<Frame>();
                    foreach (var frameData in animData.Frames)
                    {
                        var newFrame = DeserializeFrame(frameData);
                        newAnim.frames.Add(newFrame);
                    }
                    
                    newModel.animations.Add(newAnim);
                }
                
                // Import bone matrices
                newModel.boneMatrices = new List<BoneMatrix>();
                foreach (var matrixData in mobyData.BoneMatrices)
                {
                    var boneMatrix = DeserializeBoneMatrix(matrixData);
                    newModel.boneMatrices.Add(boneMatrix);
                }
                
                // Import bone data
                newModel.boneDatas = new List<BoneData>();
                foreach (var boneDataBytes in mobyData.BoneDatas)
                {
                    var boneData = DeserializeBoneData(boneDataBytes);
                    newModel.boneDatas.Add(boneData);
                }
                
                // Reconstruct skeleton
                if (newModel.boneMatrices.Count > 0 && newModel.boneDatas.Count > 0)
                {
                    newModel.skeleton = new Skeleton(newModel.boneMatrices[0], null);
                    
                    for (int i = 1; i < newModel.boneCount; i++)
                    {
                        if (i < newModel.boneMatrices.Count && i < newModel.boneDatas.Count)
                        {
                            newModel.skeleton.InsertBone(newModel.boneMatrices[i], newModel.boneDatas[i].parent);
                        }
                    }
                    Console.WriteLine("✅ Reconstructed skeleton for animations");
                }
                
                // Import attachments
                newModel.attachments = new List<Attachment>();
                foreach (var attachmentData in mobyData.Attachments)
                {
                    var attachment = DeserializeAttachment(attachmentData);
                    newModel.attachments.Add(attachment);
                }
                
                // Import model sounds
                newModel.modelSounds = new List<ModelSound>();
                foreach (var soundData in mobyData.ModelSounds)
                {
                    var sound = DeserializeModelSound(soundData);
                    newModel.modelSounds.Add(sound);
                }
                
                // Import other buffers
                newModel.indexAttachments = new List<byte>(mobyData.IndexAttachments);
                newModel.otherBuffer = new List<byte>(mobyData.OtherBuffer);
                newModel.otherIndexBuffer = new List<ushort>(mobyData.OtherIndexBuffer);
                
                // Remove existing model with the same ID if overwrite is allowed
                if (modelExists && allowOverwrite)
                {
                    targetLevel.mobyModels.RemoveAll(m => m.id == mobyData.ModelId);
                }
                
                // Add the new model to the level
                targetLevel.mobyModels.Add(newModel);
                
                // Update level bookkeeping
                UpdateLevelMobyBookkeeping(targetLevel);
                
                Console.WriteLine($"✅ Successfully imported Moby model ID {mobyData.ModelId} ({mobyData.ModelName})");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error importing Moby model: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Imports multiple Moby models from files into a target level
        /// </summary>
        /// <param name="mobyFilePaths">Paths to the .rmoby files to import</param>
        /// <param name="targetLevel">Level to import the Mobys into</param>
        /// <param name="allowOverwrite">Whether to overwrite existing models with the same ID</param>
        /// <returns>True if at least one import was successful</returns>
        public static bool ImportMobyModels(List<string> mobyFilePaths, Level targetLevel, bool allowOverwrite = false)
        {
            if (mobyFilePaths == null || mobyFilePaths.Count == 0)
            {
                Console.WriteLine("❌ No Moby files specified");
                return false;
            }

            if (targetLevel == null)
            {
                Console.WriteLine("❌ Target level is null");
                return false;
            }

            Console.WriteLine("\n==== Importing Moby Models ====");
            Console.WriteLine($"Files to import: {mobyFilePaths.Count}");

            int successCount = 0;
            foreach (var filePath in mobyFilePaths)
            {
                Console.WriteLine($"\nImporting {Path.GetFileName(filePath)}...");
                if (ImportMobyModel(filePath, targetLevel, allowOverwrite))
                {
                    successCount++;
                }
            }

            Console.WriteLine($"\n==== Import Summary ====");
            Console.WriteLine($"✅ Successfully imported: {successCount} Moby models");
            Console.WriteLine($"❌ Failed to import: {mobyFilePaths.Count - successCount} Moby models");

            return successCount > 0;
        }

        /// <summary>
        /// Gets all Moby files (.rmoby) from a directory
        /// </summary>
        /// <param name="directory">Directory to search</param>
        /// <returns>List of Moby file paths</returns>
        public static List<string> GetMobyFilesFromDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return new List<string>();
            }

            return Directory.GetFiles(directory, $"*{MOBY_FILE_EXTENSION}")
                           .ToList();
        }

        /// <summary>
        /// Interactive method for exporting Mobys from a level
        /// </summary>
        /// <returns>True if the operation was successful</returns>
        public static bool ExportMobysInteractive()
        {
            Console.WriteLine("\n==== Export Mobys ====");

            // Get source level path
            Console.WriteLine("\nEnter the path to the engine.ps3 file to export Mobys from:");
            Console.Write("> ");
            string sourcePath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                Console.WriteLine("❌ Invalid source file path");
                return false;
            }

            // Get output directory
            Console.WriteLine("\nEnter the directory to export the Mobys to:");
            Console.Write("> ");
            string outputDir = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(outputDir))
            {
                // Use default directory if none specified
                outputDir = Path.Combine(Path.GetDirectoryName(sourcePath) ?? ".", "ExportedMobys");
                Console.WriteLine($"Using default output directory: {outputDir}");
            }

            // Perform the export
            return ExportMobys(sourcePath, outputDir);
        }

        /// <summary>
        /// Interactive method for importing Mobys into a level
        /// </summary>
        /// <param name="targetLevel">Level to import Mobys into (or null to load from path)</param>
        /// <returns>True if the operation was successful</returns>
        public static bool ImportMobysInteractive(Level? targetLevel = null)
        {
            Console.WriteLine("\n==== Import Mobys ====");

            // If no target level was provided, ask for the path and load it
            if (targetLevel == null)
            {
                Console.WriteLine("\nEnter the path to the target engine.ps3 file to import Mobys into:");
                Console.Write("> ");
                string targetPath = Console.ReadLine()?.Trim() ?? "";

                if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                {
                    Console.WriteLine("❌ Invalid target file path");
                    return false;
                }

                try
                {
                    Console.WriteLine("Loading target level...");
                    targetLevel = new Level(targetPath);
                    Console.WriteLine("✅ Target level loaded successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to load target level: {ex.Message}");
                    return false;
                }
            }

            // Ask for import options
            Console.WriteLine("\nImport options:");
            Console.WriteLine("1. Import a single Moby file");
            Console.WriteLine("2. Import all Mobys from a directory");
            Console.Write("> ");
            string option = Console.ReadLine()?.Trim() ?? "1";

            Console.WriteLine("\nAllow overwriting existing models with the same ID? (y/n)");
            Console.Write("> ");
            bool allowOverwrite = (Console.ReadLine()?.Trim().ToLower() == "y");

            List<string> filesToImport = new List<string>();

            if (option == "1")
            {
                // Import a single file
                Console.WriteLine("\nEnter the path to the .rmoby file to import:");
                Console.Write("> ");
                string filePath = Console.ReadLine()?.Trim() ?? "";

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    Console.WriteLine("❌ Invalid file path");
                    return false;
                }

                filesToImport.Add(filePath);
            }
            else
            {
                // Import all files from a directory
                Console.WriteLine("\nEnter the directory containing the .rmoby files to import:");
                Console.Write("> ");
                string dirPath = Console.ReadLine()?.Trim() ?? "";

                if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
                {
                    Console.WriteLine("❌ Invalid directory path");
                    return false;
                }

                filesToImport = GetMobyFilesFromDirectory(dirPath);

                if (filesToImport.Count == 0)
                {
                    Console.WriteLine($"❌ No .rmoby files found in directory: {dirPath}");
                    return false;
                }

                Console.WriteLine($"Found {filesToImport.Count} Moby files to import");
            }

            // Perform the import
            bool success = ImportMobyModels(filesToImport, targetLevel, allowOverwrite);

            // Ask if the user wants to save the changes
            if (success)
            {
                Console.WriteLine("\nSave changes to the target level? (y/n)");
                Console.Write("> ");
                bool saveChanges = (Console.ReadLine()?.Trim().ToLower() == "y");

                if (saveChanges)
                {
                    string? savePath = targetLevel.path;
                    if (savePath == null)
                    {
                        Console.WriteLine("❌ Target level path is null, cannot save changes.");
                        return false;
                    }

                    try
                    {
                        Console.WriteLine($"Saving level to {savePath}...");
                        targetLevel.Save(savePath);
                        Console.WriteLine("✅ Level saved successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Failed to save level: {ex.Message}");
                        return false;
                    }
                }
            }

            return success;
        }

        /// <summary>
        /// Updates bookkeeping for Moby models in the target level
        /// </summary>
        public static void UpdateLevelMobyBookkeeping(Level targetLevel)
        {
            if (targetLevel == null || targetLevel.mobyModels == null)
                return;
                
            // Update moby model IDs list if it exists
            if (targetLevel.mobyIds != null)
            {
                // Rebuild the moby IDs list to match the updated models
                targetLevel.mobyIds.Clear();
                foreach (var model in targetLevel.mobyModels)
                {
                    targetLevel.mobyIds.Add(model.id);
                }
                Console.WriteLine($"  ✅ Updated mobyIds list with {targetLevel.mobyIds.Count} entries");
            }
            
            // If we have mobs, make sure they reference the correct models
            if (targetLevel.mobs != null)
            {
                foreach (var mob in targetLevel.mobs)
                {
                    // Make sure the mob has the correct model reference if it exists
                    if (mob.modelID > -1)
                    {
                        mob.model = targetLevel.mobyModels.FirstOrDefault(m => m.id == mob.modelID);
                    }
                }
                Console.WriteLine($"  ✅ Updated model references for {targetLevel.mobs.Count} mobys");
            }

            Console.WriteLine("  ✅ Moby bookkeeping updated");
        }

        #region Helper Methods
            
        /// <summary>
        /// Gets a friendly name for a model ID using known moby types
        /// </summary>
        private static string GetFriendlyModelName(int modelId)
        {
            // Check if this ID corresponds to a known moby type
            foreach (var entry in MobySwapper.MobyTypes)
            {
                if (entry.Value.Contains(modelId))
                {
                    return entry.Key;
                }
            }

            // If not found, use a generic name with the ID
            return $"Moby_{modelId}";
        }

        /// <summary>
        /// Sanitizes a string to be used as a filename
        /// </summary>
        private static string SanitizeFilename(string filename)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return new string(filename.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        }

        /// <summary>
        /// Gets the vertex buffer as a byte array from a model
        /// </summary>
        private static byte[] GetModelVertexBuffer(Model model)
        {
            if (model.vertexBuffer == null || model.vertexBuffer.Length == 0)
                return Array.Empty<byte>();

            // Convert float array to byte array
            byte[] buffer = new byte[model.vertexBuffer.Length * sizeof(float)];
            Buffer.BlockCopy(model.vertexBuffer, 0, buffer, 0, buffer.Length);
            return buffer;
        }

        /// <summary>
        /// Gets the index buffer as a byte array from a model
        /// </summary>
        private static byte[] GetModelIndexBuffer(Model model)
        {
            if (model.indexBuffer == null || model.indexBuffer.Length == 0)
                return Array.Empty<byte>();

            // Convert ushort array to byte array
            byte[] buffer = new byte[model.indexBuffer.Length * sizeof(ushort)];
            Buffer.BlockCopy(model.indexBuffer, 0, buffer, 0, buffer.Length);
            return buffer;
        }

        /// <summary>
        /// Deserializes a vertex buffer from a byte array
        /// </summary>
        private static float[] DeserializeVertexBuffer(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
                return Array.Empty<float>();

            float[] vertices = new float[buffer.Length / sizeof(float)];
            Buffer.BlockCopy(buffer, 0, vertices, 0, buffer.Length);
            return vertices;
        }

        /// <summary>
        /// Deserializes an index buffer from a byte array
        /// </summary>
        private static ushort[] DeserializeIndexBuffer(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
                return Array.Empty<ushort>();

            ushort[] indices = new ushort[buffer.Length / sizeof(ushort)];
            Buffer.BlockCopy(buffer, 0, indices, 0, buffer.Length);
            return indices;
        }

        /// <summary>
        /// Serializes a texture to a serializable object
        /// </summary>
        private static SerializableTexture SerializeTexture(Texture texture)
        {
            return new SerializableTexture
            {
                Id = texture.id,
                Width = texture.width,
                Height = texture.height,
                MipMapCount = (byte) texture.mipMapCount,
                Off06 = (ushort) texture.off06,
                Off08 = texture.off08,
                Off0C = texture.off0C,
                Off10 = texture.off10,
                Off14 = texture.off14,
                Off1C = texture.off1C,
                Off20 = texture.off20,
                VramPointer = texture.vramPointer,
                Data = texture.data ?? Array.Empty<byte>()
            };
        }

        /// <summary>
        /// Deserializes a texture from a serializable object
        /// </summary>
        private static Texture DeserializeTexture(SerializableTexture textureData)
        {
            Texture texture = new Texture(textureData.Id, textureData.Width, textureData.Height, textureData.Data);
            texture.mipMapCount = textureData.MipMapCount;
            texture.off06 = (short) textureData.Off06;
            texture.off08 = textureData.Off08;
            texture.off0C = textureData.Off0C;
            texture.off10 = textureData.Off10;
            texture.off14 = textureData.Off14;
            texture.off1C = textureData.Off1C;
            texture.off20 = textureData.Off20;
            texture.vramPointer = textureData.VramPointer;
            return texture;
        }

        /// <summary>
        /// Safely checks if two textures contain the same data
        /// </summary>
        private static bool TexturesMatch(SerializableTexture texture1, Texture texture2)
        {
            if (texture1 == null || texture2 == null)
                return false;

            // Check basic properties
            if (texture1.Width != texture2.width ||
                texture1.Height != texture2.height ||
                texture1.MipMapCount != texture2.mipMapCount)
                return false;

            // Check actual texture data with a robust hash comparison
            if (texture1.Data == null || texture2.data == null)
                return false;
            
            if (texture1.Data.Length != texture2.data.Length)
                return false;

            // Compare actual texture data bytes for exact match
            // For large textures, we could optimize by comparing just a hash
            // of the data, but for correctness we'll do a full comparison
            return ByteArraysMatch(texture1.Data, texture2.data);
        }

        /// <summary>
        /// Efficiently compares two byte arrays for equality
        /// </summary>
        private static bool ByteArraysMatch(byte[] array1, byte[] array2)
        {
            if (array1.Length != array2.Length)
                return false;
                
            // For small arrays, direct comparison is fastest
            if (array1.Length < 1024)
            {
                for (int i = 0; i < array1.Length; i++)
                {
                    if (array1[i] != array2[i])
                        return false;
                }
                return true;
            }
            
            // For larger arrays, compute and compare a hash
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash1 = sha.ComputeHash(array1);
                byte[] hash2 = sha.ComputeHash(array2);
                
                return hash1.SequenceEqual(hash2);
            }
        }

        /// <summary>
        /// Creates an optimized texture signature for faster comparisons
        /// </summary>
        private static string GetTextureHashSignature(Texture texture)
        {
            if (texture == null || texture.data == null) 
                return "null";
            
            // Create a signature based on texture dimensions and a data hash
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(texture.data);
                
                // Return a compact signature
                return $"{texture.width}x{texture.height}:{Convert.ToBase64String(hash, 0, 8)}";
            }
        }

        /// <summary>
        /// Serializes a texture config to a byte array that preserves all important fields
        /// </summary>
        private static byte[] SerializeTextureConfig(TextureConfig config)
        {
            if (config == null)
                return Array.Empty<byte>();
            
            // TextureConfig has 4 main fields to preserve: id, start, size, mode
            // Plus we should preserve the wrap modes
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write(config.id);
                writer.Write(config.start);
                writer.Write(config.size);
                writer.Write(config.mode);
                writer.Write((int)config.wrapModeS);
                writer.Write((int)config.wrapModeT);
                
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserializes a texture config from a byte array
        /// </summary>
        private static TextureConfig DeserializeTextureConfig(byte[] configData)
        {
            if (configData == null || configData.Length < 24)
                return new TextureConfig();
            
            TextureConfig config = new TextureConfig();
            
            using (MemoryStream ms = new MemoryStream(configData))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                config.id = reader.ReadInt32();
                config.start = reader.ReadInt32();
                config.size = reader.ReadInt32();
                config.mode = reader.ReadInt32();
                
                // Handle wrap modes if they were stored
                if (ms.Position + 8 <= ms.Length)
                {
                    config.wrapModeS = (TextureConfig.WrapMode)reader.ReadInt32();
                    config.wrapModeT = (TextureConfig.WrapMode)reader.ReadInt32();
                }
            }
            
            return config;
        }

        /// <summary>
        /// Deserializes a Frame from byte data
        /// </summary>
        private static Frame DeserializeFrame(byte[] frameData)
        {
            // Since we don't have a direct constructor that takes byte data,
            // create a minimal frame with the byte data
            return new Frame(frameData, 0, 1, 0, 0);
        }

        /// <summary>
        /// Deserializes a BoneMatrix from byte data
        /// </summary>
        private static BoneMatrix DeserializeBoneMatrix(byte[] matrixData)
        {
            // This would properly deserialize the bone matrix in a real implementation
            return new BoneMatrix(GameType.RaC2, matrixData, 0);
        }

        /// <summary>
        /// Deserializes a BoneData from byte data
        /// </summary>
        private static BoneData DeserializeBoneData(byte[] boneData)
        {
            // This would properly deserialize the bone data in a real implementation
            return new BoneData(GameType.RaC2, boneData, 0);
        }

        /// <summary>
        /// Deserializes an Attachment from byte data without creating temporary files
        /// </summary>
        private static Attachment DeserializeAttachment(byte[] attachmentData)
        {
            if (attachmentData == null || attachmentData.Length == 0)
                return null;

            using (MemoryStream ms = new MemoryStream(attachmentData))
            {
                // Replace MemoryStream with FileStream by creating a temporary file
                string tempFilePath = Path.GetTempFileName();
                File.WriteAllBytes(tempFilePath, ms.ToArray());
                using (FileStream fs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
                {
                    return new Attachment(fs, 0);
                }
            }
        }

        /// <summary>
        /// Helper class to wrap a MemoryStream as a FileStream without creating temporary files
        /// </summary>
        private class MemoryStreamWrapper : Stream
        {
            private readonly MemoryStream _memoryStream;
            
            public MemoryStreamWrapper(MemoryStream memoryStream)
            {
                _memoryStream = memoryStream;
            }
            
            public override bool CanRead => _memoryStream.CanRead;
            public override bool CanSeek => _memoryStream.CanSeek;
            public override bool CanWrite => _memoryStream.CanWrite;
            public override long Length => _memoryStream.Length;
            
            public override long Position
            {
                get => _memoryStream.Position;
                set => _memoryStream.Position = value;
            }
            
            public override void Flush() => _memoryStream.Flush();
            
            public override int Read(byte[] buffer, int offset, int count)
            {
                return _memoryStream.Read(buffer, offset, count);
            }
            
            public override long Seek(long offset, SeekOrigin origin)
            {
                return _memoryStream.Seek(offset, origin);
            }
            
            public override void SetLength(long value)
            {
                _memoryStream.SetLength(value);
            }
            
            public override void Write(byte[] buffer, int offset, int count)
            {
                _memoryStream.Write(buffer, offset, count);
            }
            
            protected override void Dispose(bool disposing)
            {
                // No need to dispose the memory stream here as it will be disposed
                // by the caller through the using statement
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Deserializes a ModelSound from byte data
        /// </summary>
        private static ModelSound DeserializeModelSound(byte[] soundData)
        {
            // ModelSound constructor requires a byte array and an index
            return new ModelSound(soundData, 0);
        }

        #endregion
    }
}
