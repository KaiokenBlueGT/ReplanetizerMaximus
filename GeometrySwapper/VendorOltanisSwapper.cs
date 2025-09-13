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
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GeometrySwapper
{
    /// <summary>
    /// Specializes in handling the Vendor Moby replacement with RC1 or RC3 Vendor models
    /// </summary>
    public static class VendorOltanisSwapper
    {
        /// <summary>
        /// Enum to specify which vendor model to use
        /// </summary>
        public enum VendorModelSource
        {
            RC1,
            RC3,
            KeepOriginal
        }

        /// <summary>
        /// Flags enum to control swap options
        /// </summary>
        [Flags]
        public enum VendorSwapOptions
        {
            None = 0,
            UseSourcePosition = 1,
            UseSourceModel = 2,

            // Common combinations
            PositionOnly = UseSourcePosition,
            FullReplacement = UseSourcePosition | UseSourceModel,
            Default = FullReplacement
        }

        /// <summary>
        /// Swaps the target level's Vendor with a source vendor model, preserving target properties for compatibility
        /// REWRITTEN: Now brings in complete source model, then applies only specific donor properties
        /// UPDATED: Creates vendor moby instance if none exists
        /// </summary>
        /// <param name="targetLevel">Target level to modify</param>
        /// <param name="sourceLevel">Source level to get the vendor model from</param>
        /// <param name="modelSource">Which model to use (RC1 or RC3)</param>
        /// <param name="options">Options to control the swap behavior</param>
        /// <returns>True if operation was successful</returns>
        public static bool SwapVendorModel(Level targetLevel, Level sourceLevel, VendorModelSource modelSource, VendorSwapOptions options = VendorSwapOptions.Default)
        {
            if (targetLevel == null || sourceLevel == null)
            {
                Console.WriteLine("❌ Cannot swap vendor: Invalid level data");
                return false;
            }

            if (modelSource == VendorModelSource.KeepOriginal)
            {
                Console.WriteLine("Keeping original vendor model as requested");
                return true;
            }

            // Vendor model ID
            const int vendorModelId = 11;

            Console.WriteLine($"\n==== Swapping Vendor with {modelSource} Model ====");
            Console.WriteLine($"Options: Position={options.HasFlag(VendorSwapOptions.UseSourcePosition)}, " +
                              $"Model={options.HasFlag(VendorSwapOptions.UseSourceModel)}");

            // Find models in both levels
            var sourceVendorModel = sourceLevel.mobyModels?.FirstOrDefault(m => m.id == vendorModelId) as MobyModel;
            var targetVendorModel = targetLevel.mobyModels?.FirstOrDefault(m => m.id == vendorModelId) as MobyModel;

            if (sourceVendorModel == null)
            {
                Console.WriteLine($"❌ Could not find vendor model in {modelSource} level");
                return false;
            }

            // 🆕 Handle case where target level doesn't have vendor model
            if (targetVendorModel == null)
            {
                Console.WriteLine("⚠️ No vendor model found in target level - will add source vendor model");
                
                // Add the vendor model to target level first
                var clonedVendorModel = (MobyModel) MobySwapper.DeepCloneModel(sourceVendorModel);
                clonedVendorModel.id = vendorModelId;
                
                if (targetLevel.mobyModels == null)
                    targetLevel.mobyModels = new List<Model>();
                    
                targetLevel.mobyModels.Add(clonedVendorModel);
                targetVendorModel = clonedVendorModel;
                
                Console.WriteLine("✅ Added vendor model to target level");
            }

            // Step 1: Find vendor mobys in target level
            var targetVendorMobys = targetLevel.mobs?.Where(m => m.modelID == vendorModelId).ToList();
            if (targetVendorMobys == null || targetVendorMobys.Count == 0)
            {
                Console.WriteLine("⚠️ No vendor mobys found in target level - will create a new one");
                
                // 🆕 Create a new vendor moby if none exists
                var newVendorMoby = CreateVendorMobyFromSource(targetLevel, sourceLevel, vendorModelId);
                if (newVendorMoby != null)
                {
                    // Initialize the list if it's null
                    if (targetLevel.mobs == null)
                        targetLevel.mobs = new List<Moby>();
                        
                    targetLevel.mobs.Add(newVendorMoby);
                    targetVendorMobys = new List<Moby> { newVendorMoby };
                    Console.WriteLine("✅ Created new vendor moby instance");
                }
                else
                {
                    Console.WriteLine("❌ Failed to create new vendor moby instance");
                    return false;
                }
            }

            Console.WriteLine($"Found {targetVendorMobys.Count} vendor mobys in target level");

            // Step 2: Reposition vendor if UseSourcePosition is specified
            if (options.HasFlag(VendorSwapOptions.UseSourcePosition))
            {
                var sourceVendorMobys = sourceLevel.mobs?.Where(m => m.modelID == vendorModelId).ToList();
                if (sourceVendorMobys == null || sourceVendorMobys.Count == 0)
                {
                    Console.WriteLine($"⚠️ Could not find vendor moby in {modelSource} level - will not reposition");
                }
                else
                {
                    Console.WriteLine($"Found {sourceVendorMobys.Count} vendor mobys in {modelSource} level");

                    // Get the position of the first source vendor
                    var sourceVendor = sourceVendorMobys[0];

                    // 🔧 FIX: Only reposition ONE target vendor instead of all when multiple exist
                    if (targetVendorMobys.Count > 1)
                    {
                        Console.WriteLine($"⚠️ Found {targetVendorMobys.Count} target vendors - repositioning only the first one to avoid stacking");
                        var targetVendor = targetVendorMobys[0];
                        Console.WriteLine($"Repositioning vendor from {targetVendor.position} to {sourceVendor.position}");
                        targetVendor.position = sourceVendor.position;
                        targetVendor.rotation = sourceVendor.rotation;
                        targetVendor.scale = sourceVendor.scale;
                        targetVendor.UpdateTransformMatrix();

                        Console.WriteLine($"✅ Repositioned 1 of {targetVendorMobys.Count} vendors (others kept in original positions)");
                    }
                    else
                    {
                        // Single vendor - reposition as before
                        var targetVendor = targetVendorMobys[0];
                        Console.WriteLine($"Repositioning vendor from {targetVendor.position} to {sourceVendor.position}");
                        targetVendor.position = sourceVendor.position;
                        targetVendor.rotation = sourceVendor.rotation;
                        targetVendor.scale = sourceVendor.scale;
                        targetVendor.UpdateTransformMatrix();
                    }
                }
            }
            else
            {
                Console.WriteLine("Keeping original vendor position (source positioning disabled)");
            }

            // Step 3: Replace vendor model if UseSourceModel is specified
            if (options.HasFlag(VendorSwapOptions.UseSourceModel))
            {
                Console.WriteLine("🔄 Complete source model import with selective donor property application...");

                // 🆕 NEW APPROACH: Capture ONLY the compatibility properties from target before anything else
                var donorCompatibilityProperties = CaptureCompatibilityProperties(targetVendorModel);

                Console.WriteLine($"\n🔍 Source {modelSource} vendor model (will be imported complete):");
                Console.WriteLine($"  📏 Size: {sourceVendorModel.size} (will be kept)");
                Console.WriteLine($"  🎬 Animations: {sourceVendorModel.animations?.Count ?? 0} (will be kept)");
                Console.WriteLine($"  🎨 Texture configs: {sourceVendorModel.textureConfig?.Count ?? 0} (will be kept)");
                Console.WriteLine($"  📐 Vertices: {sourceVendorModel.vertexBuffer?.Length / 8 ?? 0} (will be kept)");

                Console.WriteLine($"\n🔧 Donor compatibility properties (will be applied to source model):");
                foreach (var prop in donorCompatibilityProperties)
                {
                    Console.WriteLine($"  🔧 {prop.Key}: {prop.Value}");
                }

                // 🆕 STEP 1: Complete deep clone of source model (preserving everything)
                var completeSourceModel = (MobyModel) MobySwapper.DeepCloneModel(sourceVendorModel);
                completeSourceModel.id = vendorModelId; // Ensure ID is maintained as 11

                // 🆕 STEP 2: Import source textures
                if (completeSourceModel.textureConfig != null && completeSourceModel.textureConfig.Count > 0)
                {
                    Console.WriteLine($"Importing {completeSourceModel.textureConfig.Count} textures for {modelSource} vendor model...");
                    ImportModelTextures(targetLevel, sourceLevel, completeSourceModel);
                }

                // 🆕 STEP 3: Apply ONLY the compatibility properties from donor level
                ApplyCompatibilityProperties(completeSourceModel, donorCompatibilityProperties);

                // 🆕 STEP 4: Replace model in target level
                int modelIndex = targetLevel.mobyModels.IndexOf(targetVendorModel);
                if (modelIndex >= 0)
                {
                    targetLevel.mobyModels[modelIndex] = completeSourceModel;
                }
                else
                {
                    targetLevel.mobyModels.Remove(targetVendorModel);
                    targetLevel.mobyModels.Add(completeSourceModel);
                }

                // 🆕 STEP 5: Update all vendor moby references
                foreach (var moby in targetVendorMobys)
                {
                    moby.model = completeSourceModel;
                    moby.modelID = vendorModelId; // Ensure modelID matches
                }

                // 🆕 FINAL VERIFICATION
                Console.WriteLine($"\n🔍 Final model verification:");
                Console.WriteLine($"  📏 Size: {completeSourceModel.size} (from source - preserved)");
                Console.WriteLine($"  🎬 Animations: {completeSourceModel.animations?.Count ?? 0} (from source - preserved)");
                Console.WriteLine($"  🔧 Compatibility properties: Applied from donor level");

                Console.WriteLine($"✅ Complete {modelSource} vendor model imported with donor compatibility properties");
            }
            else
            {
                Console.WriteLine("Keeping original vendor model (source model replacement disabled)");
            }

            // Step 4: Set light value to 0 for all vendor mobys (standard for RC1 compatibility)
            foreach (var vendor in targetVendorMobys)
            {
                vendor.light = 0;
            }
            Console.WriteLine("✅ Set vendor light value to 0 for compatibility");

            // Step 5: Validate and fix pVar indices
            MobySwapper.ValidateAndFixPvarIndices(targetLevel);

            // Step 6: Ensure vendor logo has correct properties if it exists
            EnsureVendorLogoProperties(targetLevel);

            // Summary
            Console.WriteLine("\n==== Vendor Swap Summary ====");
            if (options.HasFlag(VendorSwapOptions.UseSourcePosition))
                Console.WriteLine($"✅ Repositioned vendor to match {modelSource} level");

            if (options.HasFlag(VendorSwapOptions.UseSourceModel))
            {
                Console.WriteLine($"✅ Imported complete {modelSource} vendor model");
                Console.WriteLine($"  📏 Size preserved from source: {sourceVendorModel.size}");
                Console.WriteLine($"  🎬 Animations preserved from source: {sourceVendorModel.animations?.Count ?? 0}");
                Console.WriteLine($"  🔧 Compatibility properties applied from donor");
            }
            else
                Console.WriteLine("✅ Kept original vendor model");

            Console.WriteLine("✅ Set vendor light value to 0 for compatibility");
            Console.WriteLine("✅ Fixed up all pVar indices");

            return true;
        }

        /// <summary>
        /// Legacy method for backwards compatibility - delegates to SwapVendorModel
        /// </summary>
        /// <param name="targetLevel">RC2 level to modify</param>
        /// <param name="rc1OltanisLevel">RC1 Oltanis level to get the vendor model from</param>
        /// <param name="options">Options to control the swap behavior</param>
        /// <returns>True if operation was successful</returns>
        public static bool SwapVendorWithRC1Oltanis(Level targetLevel, Level rc1OltanisLevel, VendorSwapOptions options = VendorSwapOptions.Default)
        {
            return SwapVendorModel(targetLevel, rc1OltanisLevel, VendorModelSource.RC1, options);
        }

        /// <summary>
        /// Non-interactive version that works with already loaded levels
        /// </summary>
        /// <param name="targetLevel">Already loaded target level</param>
        /// <param name="sourceLevel">Already loaded source level</param>
        /// <param name="modelSource">Which model to use (RC1 or RC3)</param>
        /// <param name="options">Options for the swap</param>
        /// <returns>True if operation was successful</returns>
        public static bool SwapVendorWithLoadedLevels(Level targetLevel, Level sourceLevel, VendorModelSource modelSource = VendorModelSource.RC1, VendorSwapOptions options = VendorSwapOptions.Default)
        {
            if (targetLevel == null || sourceLevel == null)
            {
                Console.WriteLine("❌ Cannot swap vendor: Invalid level data");
                return false;
            }

            // For your specific use case: RC1 model + position, but keep target properties
            Console.WriteLine("Select vendor model source:");
            Console.WriteLine("1. RC1 model (Blue Gadgetron vendor) [default]");
            Console.WriteLine("2. RC3 model (Orange Gadgetron vendor)");
            Console.WriteLine("3. Keep original model");
            Console.Write("> ");

            string modelChoice = Console.ReadLine()?.Trim() ?? "1";
            switch (modelChoice)
            {
                case "2":
                    modelSource = VendorModelSource.RC3;
                    break;
                case "3":
                    modelSource = VendorModelSource.KeepOriginal;
                    break;
                case "1":
                default:
                    modelSource = VendorModelSource.RC1;
                    break;
            }

            if (modelSource == VendorModelSource.KeepOriginal)
            {
                Console.WriteLine("✅ Keeping original model - no changes needed");
                return true;
            }

            // Ask for swap options
            Console.WriteLine("\nSelect swap options:");
            Console.WriteLine("1. RC1 model + RC1 position (preserving target properties) [recommended]");
            Console.WriteLine("2. RC1 position only (keep target model)");
            Console.WriteLine("3. RC1 model only (keep target position)");
            Console.WriteLine("4. Custom options");
            Console.Write("> ");

            string choice = Console.ReadLine()?.Trim() ?? "1";
            switch (choice)
            {
                case "1":
                    options = VendorSwapOptions.FullReplacement; // This will preserve target properties
                    break;
                case "2":
                    options = VendorSwapOptions.UseSourcePosition;
                    break;
                case "3":
                    options = VendorSwapOptions.UseSourceModel;
                    break;
                case "4":
                    options = GetCustomOptions();
                    break;
                default:
                    options = VendorSwapOptions.FullReplacement;
                    break;
            }

            // Perform the actual swap
            return SwapVendorModel(targetLevel, sourceLevel, modelSource, options);
        }

        /// <summary>
        /// 🆕 NEW: Captures ONLY the compatibility properties that need to be preserved from donor level
        /// </summary>
        /// <param name="donorModel">The donor model to capture compatibility properties from</param>
        /// <returns>Dictionary of only the specific properties needed for compatibility</returns>
        private static Dictionary<string, object?> CaptureCompatibilityProperties(MobyModel donorModel)
        {
            var compatibilityProperties = new Dictionary<string, object?>();

            // Define exactly which properties are needed for compatibility
            var compatibilityPropertyNames = new[]
            {
                "null1", "null2", "null3", // null fields for compatibility
                // NOTE: bone-related fields must come from the source model for animations to work
                // Overriding these from the donor (target) model caused blank animations
                // "boneCount", "lpBoneCount", // <-- intentionally skipped
                "count3", "count4", "lpRenderDist", "count8", // other counts required for compatibility
                "unk1", "unk2", "unk3", "unk4", // unknown fields that affect compatibility
                "color2", "unk6", // color and compatibility fields
                "vertexCount2", // vertex count field
                // Note: Deliberately excluding size, animations, geometry, textures
            };

            Console.WriteLine("🔍 Capturing compatibility properties from donor model:");

            foreach (string propName in compatibilityPropertyNames)
            {
                try
                {
                    var property = typeof(MobyModel).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (property != null && property.CanRead)
                    {
                        var value = property.GetValue(donorModel);
                        compatibilityProperties[propName] = value;
                        Console.WriteLine($"  🔧 {propName}: {value}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️ Could not capture {propName}: {ex.Message}");
                }
            }

            Console.WriteLine($"✅ Captured {compatibilityProperties.Count} compatibility properties");
            return compatibilityProperties;
        }

        /// <summary>
        /// 🆕 NEW: Applies ONLY the compatibility properties to the complete source model
        /// </summary>
        /// <param name="sourceModel">The complete source model to apply compatibility properties to</param>
        /// <param name="compatibilityProperties">Dictionary of compatibility properties to apply</param>
        private static void ApplyCompatibilityProperties(MobyModel sourceModel, Dictionary<string, object?> compatibilityProperties)
        {
            Console.WriteLine("🔧 Applying donor compatibility properties to complete source model:");
            Console.WriteLine($"  📏 Source size will be preserved: {sourceModel.size}");
            Console.WriteLine($"  🎬 Source animations will be preserved: {sourceModel.animations?.Count ?? 0}");
            Console.WriteLine($"  🎨 Source textures will be preserved: {sourceModel.textureConfig?.Count ?? 0}");

            foreach (var kvp in compatibilityProperties)
            {
                try
                {
                    var property = typeof(MobyModel).GetProperty(kvp.Key, BindingFlags.Public | BindingFlags.Instance);
                    if (property != null && property.CanWrite)
                    {
                        property.SetValue(sourceModel, kvp.Value);
                        Console.WriteLine($"  ✅ Applied {kvp.Key}: {kvp.Value}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️ Could not apply {kvp.Key}: {ex.Message}");
                }
            }

            // Ensure arrays are properly sized (this shouldn't change since we kept the source geometry)
            EnsureModelArraysAreProperlySized(sourceModel);

            Console.WriteLine("✅ Compatibility properties applied to complete source model");
            Console.WriteLine($"  📏 Final size: {sourceModel.size} (preserved from source)");
            Console.WriteLine($"  🎬 Final animations: {sourceModel.animations?.Count ?? 0} (preserved from source)");
        }

        /// <summary>
        /// Ensures that the weights and ids arrays are properly sized to match the vertex count
        /// </summary>
        /// <param name="model">The model to fix</param>
        private static void EnsureModelArraysAreProperlySized(MobyModel model)
        {
            if (model.vertexBuffer == null || model.vertexBuffer.Length == 0)
            {
                Console.WriteLine("  🔧 Model has no vertex buffer, setting empty arrays");
                model.weights = new uint[0];
                model.ids = new uint[0];
                return;
            }

            int vertexCount = model.vertexBuffer.Length / 8; // Standard vertex stride is 8 floats

            Console.WriteLine($"  🔧 Model has {vertexCount} vertices");

            // Check if weights array needs to be resized
            if (model.weights == null || model.weights.Length != vertexCount)
            {
                Console.WriteLine($"  🔧 Resizing weights array from {model.weights?.Length ?? 0} to {vertexCount}");
                var newWeights = new uint[vertexCount];

                // Copy existing data if any
                if (model.weights != null)
                {
                    int copyCount = Math.Min(model.weights.Length, vertexCount);
                    Array.Copy(model.weights, newWeights, copyCount);
                }

                model.weights = newWeights;
            }

            // Check if ids array needs to be resized
            if (model.ids == null || model.ids.Length != vertexCount)
            {
                Console.WriteLine($"  🔧 Resizing ids array from {model.ids?.Length ?? 0} to {vertexCount}");
                var newIds = new uint[vertexCount];

                // Copy existing data if any
                if (model.ids != null)
                {
                    int copyCount = Math.Min(model.ids.Length, vertexCount);
                    Array.Copy(model.ids, newIds, copyCount);
                }

                model.ids = newIds;
            }

            Console.WriteLine($"  ✅ Arrays properly sized: weights[{model.weights.Length}], ids[{model.ids.Length}]");
        }

        /// <summary>
        /// Interactive wrapper for vendor swapping function with model selection
        /// </summary>
        /// <returns>True if the operation was successful</returns>
        public static bool SwapVendorInteractive()
        {
            Console.WriteLine("\n==== Vendor Model Swapper ====");

            // Get target level path
            Console.WriteLine("\nEnter path to the target level engine.ps3 file:");
            Console.Write("> ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                Console.WriteLine("❌ Invalid target level path");
                return false;
            }

            // Get source level path
            Console.WriteLine("\nEnter path to the source level engine.ps3 file (RC1 or RC3):");
            Console.Write("> ");
            string sourcePath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                Console.WriteLine("❌ Invalid source level path");
                return false;
            }

            // Load target level
            Console.WriteLine($"\nLoading target level: {Path.GetFileName(targetPath)}...");
            Level targetLevel;

            try
            {
                targetLevel = new Level(targetPath);
                Console.WriteLine($"✅ Successfully loaded target level");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading target level: {ex.Message}");
                return false;
            }

            // Load source level
            Console.WriteLine($"\nLoading source level: {Path.GetFileName(sourcePath)}...");
            Level sourceLevel;

            try
            {
                sourceLevel = new Level(sourcePath);
                Console.WriteLine($"✅ Successfully loaded source level");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading source level: {ex.Message}");
                return false;
            }

            // Select vendor model source
            Console.WriteLine("\nSelect vendor model to use:");
            Console.WriteLine("1. RC1 model (Gadgetron vendor)");
            Console.WriteLine("2. RC3 model (Megacorp vendor)");
            Console.WriteLine("3. Keep original model");
            Console.Write("> ");

            string modelChoice = Console.ReadLine()?.Trim() ?? "1";
            VendorModelSource modelSource;

            switch (modelChoice)
            {
                case "2":
                    modelSource = VendorModelSource.RC3;
                    break;
                case "3":
                    modelSource = VendorModelSource.KeepOriginal;
                    break;
                case "1":
                default:
                    modelSource = VendorModelSource.RC1;
                    break;
            }

            if (modelSource == VendorModelSource.KeepOriginal)
            {
                Console.WriteLine("✅ Keeping original model - no changes needed");
                return true;
            }

            // Add option selection before performing the swap
            Console.WriteLine("\nSelect swap options:");
            Console.WriteLine("1. Full replacement (source model and position)");
            Console.WriteLine("2. Position only (keep target model but use source position)");
            Console.WriteLine("3. Model only (use source model but keep target position)");
            Console.WriteLine("4. Custom options");
            Console.Write("> ");

            string choice = Console.ReadLine()?.Trim() ?? "1";
            VendorSwapOptions options;

            switch (choice)
            {
                case "2":
                    options = VendorSwapOptions.UseSourcePosition;
                    break;
                case "3":
                    options = VendorSwapOptions.UseSourceModel;
                    break;
                case "4":
                    options = GetCustomOptions();
                    break;
                case "1":
                default:
                    options = VendorSwapOptions.FullReplacement;
                    break;
            }

            // Perform vendor swap with selected options
            bool success = SwapVendorModel(targetLevel, sourceLevel, modelSource, options);

            if (success)
            {
                // Ask if the user wants to save
                Console.Write("\nSave changes to the target level? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLower() == "y")
                {
                    Console.WriteLine("Saving target level...");

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
        /// Legacy interactive method for backwards compatibility
        /// </summary>
        /// <returns>True if the operation was successful</returns>
        public static bool SwapVendorWithRC1OltanisInteractive()
        {
            Console.WriteLine("Note: This method has been deprecated. Please use SwapVendorInteractive() for full functionality.");
            return SwapVendorInteractive();
        }

        /// <summary>
        /// Helper method to get custom options
        /// </summary>
        private static VendorSwapOptions GetCustomOptions()
        {
            VendorSwapOptions options = VendorSwapOptions.None;

            Console.WriteLine("\nCustomize swap options:");

            if (GetYesNoInput("Use source position? (y/n): "))
                options |= VendorSwapOptions.UseSourcePosition;

            if (GetYesNoInput("Use source model? (y/n): "))
                options |= VendorSwapOptions.UseSourceModel;

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
        /// Helper method to compare two textures for similarity
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

        /// <summary>
        /// Helper method to create a deep copy of a texture
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
        /// Helper method to import textures for a model
        /// </summary>
        public static void ImportModelTextures(Level targetLevel, Level sourceLevel, MobyModel model)
        {
            if (model == null || model.textureConfig == null || model.textureConfig.Count == 0)
                return;

            Console.WriteLine($"Importing textures for model ID {model.id}...");
            Dictionary<int, int> textureMapping = new Dictionary<int, int>();

            // Process primary texture configs
            foreach (var texConfig in model.textureConfig)
            {
                int originalTexId = texConfig.id;

                // Skip if already processed
                if (textureMapping.TryGetValue(originalTexId, out int existingTexId))
                {
                    texConfig.id = existingTexId;
                    continue;
                }

                // Validate texture index
                if (originalTexId < 0 || originalTexId >= sourceLevel.textures.Count)
                {
                    Console.WriteLine($"  ⚠️ Texture ID {originalTexId} is out of range for source textures");
                    continue;
                }

                var sourceTexture = sourceLevel.textures[originalTexId];

                // Check if this texture already exists in the target level
                int targetTexId = -1;
                for (int i = 0; i < targetLevel.textures.Count; i++)
                {
                    if (TextureEquals(sourceTexture, targetLevel.textures[i]))
                    {
                        targetTexId = i;
                        break;
                    }
                }

                // If not found, add the texture to the target level
                if (targetTexId == -1)
                {
                    // Deep copy the texture
                    var clonedTexture = DeepCloneTexture(sourceTexture);
                    targetLevel.textures.Add(clonedTexture);
                    targetTexId = targetLevel.textures.Count - 1;
                    Console.WriteLine($"  Added new texture at index {targetTexId}");
                }
                else
                {
                    Console.WriteLine($"  Found matching texture at index {targetTexId}");
                }

                // Update the mapping and texture config
                textureMapping[originalTexId] = targetTexId;
                texConfig.id = targetTexId;
            }

            // Handle other texture configs if present
            if (model.otherTextureConfigs != null && model.otherTextureConfigs.Count > 0)
            {
                foreach (var texConfig in model.otherTextureConfigs)
                {
                    int originalTexId = texConfig.id;

                    // If we've already mapped this texture, reuse the mapping
                    if (textureMapping.TryGetValue(originalTexId, out int mappedId))
                    {
                        texConfig.id = mappedId;
                    }
                    else if (originalTexId >= 0 && originalTexId < sourceLevel.textures.Count)
                    {
                        // Map it like we did above
                        var sourceTexture = sourceLevel.textures[originalTexId];

                        // Find or add the texture
                        int targetTexId = -1;
                        for (int i = 0; i < targetLevel.textures.Count; i++)
                        {
                            if (TextureEquals(sourceTexture, targetLevel.textures[i]))
                            {
                                targetTexId = i;
                                break;
                            }
                        }

                        if (targetTexId == -1)
                        {
                            var clonedTexture = DeepCloneTexture(sourceTexture);
                            targetLevel.textures.Add(clonedTexture);
                            targetTexId = targetLevel.textures.Count - 1;
                            Console.WriteLine($"  Added new texture at index {targetTexId}");
                        }

                        textureMapping[originalTexId] = targetTexId;
                        texConfig.id = targetTexId;
                    }
                }
            }

            Console.WriteLine($"✅ Successfully processed {textureMapping.Count} textures for model ID {model.id}");
        }

        /// <summary>
        /// Ensures the Vendor Logo model (ID 1143) has the correct property values
        /// </summary>
        /// <param name="level">The level containing the vendor logo model</param>
        /// <returns>True if the vendor logo was found and updated, otherwise false</returns>
        public static bool EnsureVendorLogoProperties(Level level)
        {
            // Vendor Logo model ID
            const int vendorLogoId = 1143;

            // Find vendor logo model in the level
            var vendorLogoModel = level.mobyModels?.FirstOrDefault(m => m.id == vendorLogoId) as MobyModel;

            if (vendorLogoModel == null)
            {
                Console.WriteLine("⚠️ Could not find vendor logo model in the level (ID: 1143)");
                return false;
            }

            Console.WriteLine("Setting correct properties for Vendor Logo model (ID: 1143)");

            // Set the required property values
            vendorLogoModel.count3 = 0;
            vendorLogoModel.count4 = 0;
            vendorLogoModel.unk1 = 0.002f;
            vendorLogoModel.unk2 = 0.002f;
            vendorLogoModel.unk3 = -3318.602f;
            vendorLogoModel.unk4 = 25448.279f;
            vendorLogoModel.unk6 = 1073807359;

            Console.WriteLine("✅ Updated vendor logo properties:");
            Console.WriteLine($"  count3: {vendorLogoModel.count3}");
            Console.WriteLine($"  count4: {vendorLogoModel.count4}");
            Console.WriteLine($"  unk1: {vendorLogoModel.unk1}");
            Console.WriteLine($"  unk2: {vendorLogoModel.unk2}");
            Console.WriteLine($"  unk3: {vendorLogoModel.unk3}");
            Console.WriteLine($"  unk4: {vendorLogoModel.unk4}");
            Console.WriteLine($"  unk6: {vendorLogoModel.unk6}");

            return true;
        }

        /// <summary>
        /// Ensure the vendor logo (Megacorp/Gadgetron sign) exists and has proper textures
        /// </summary>
        /// <param name="targetLevel">The level to check/fix</param>
        /// <param name="referenceLevel">A reference level that may have the vendor logo</param>
        /// <returns>True if successful</returns>
        public static bool EnsureVendorLogoModel(Level targetLevel, Level referenceLevel)
        {
            const int vendorLogoId = 1143;

            Console.WriteLine("\n==== Ensuring Vendor Logo Model Exists ====");

            // Check if target already has the model
            var targetLogoModel = targetLevel.mobyModels?.FirstOrDefault(m => m.id == vendorLogoId) as MobyModel;

            // If target has the model, ensure properties
            if (targetLogoModel != null)
            {
                Console.WriteLine("✅ Found vendor logo model in target level");
                EnsureVendorLogoProperties(targetLevel);

                // Check for texture issues - if reference has model with textures, use them
                var refLogoModel = referenceLevel?.mobyModels?.FirstOrDefault(m => m.id == vendorLogoId) as MobyModel;
                if (refLogoModel != null && refLogoModel.textureConfig != null && refLogoModel.textureConfig.Count > 0)
                {
                    Console.WriteLine("Updating vendor logo textures from reference level...");
                    ImportModelTextures(targetLevel, referenceLevel, targetLogoModel);
                    Console.WriteLine("✅ Updated vendor logo textures");
                }

                return true;
            }

            // Target doesn't have model - try to copy from reference
            var referenceLogoModel = referenceLevel?.mobyModels?.FirstOrDefault(m => m.id == vendorLogoId) as MobyModel;
            if (referenceLogoModel != null)
            {
                Console.WriteLine("Copying vendor logo model from reference level...");
                var clonedModel = (MobyModel) MobySwapper.DeepCloneModel(referenceLogoModel);
                clonedModel.id = vendorLogoId; // Ensure ID is correct

                // Set proper properties
                clonedModel.count3 = 0;
                clonedModel.count4 = 0;
                clonedModel.unk1 = 0.002f;
                clonedModel.unk2 = 0.002f;
                clonedModel.unk3 = -3318.602f;
                clonedModel.unk4 = 25448.28f;
                clonedModel.unk6 = 1073807359;

                // Add to target level
                if (targetLevel.mobyModels == null)
                    targetLevel.mobyModels = new List<Model>();

                targetLevel.mobyModels.Add(clonedModel);

                // Import textures
                ImportModelTextures(targetLevel, referenceLevel, clonedModel);

                Console.WriteLine("✅ Successfully added vendor logo model from reference level");
                return true;
            }

            Console.WriteLine("❌ Could not find vendor logo model in any available level");
            return false;
        }
        
        /// <summary>
        /// 🆕 NEW: Creates a new vendor moby instance based on source level vendor
        /// </summary>
        /// <param name="targetLevel">The target level to create the moby for</param>
        /// <param name="sourceLevel">The source level to get vendor positioning from</param>
        /// <param name="vendorModelId">The vendor model ID (should be 11)</param>
        /// <returns>New vendor moby instance, or null if creation failed</returns>
        private static Moby? CreateVendorMobyFromSource(Level targetLevel, Level sourceLevel, int vendorModelId)
        {
            Console.WriteLine("🔨 Creating new vendor moby instance...");
            
            // Find a source vendor moby to use as reference for positioning and properties
            var sourceVendorMobys = sourceLevel.mobs?.Where(m => m.modelID == vendorModelId).ToList();
            if (sourceVendorMobys == null || sourceVendorMobys.Count == 0)
            {
                Console.WriteLine("⚠️ No source vendor moby found for positioning reference");
                Console.WriteLine("  Using default position (0, 0, 0)");
                
                // Create with default position if no source vendor found
                var defaultVendorMoby = new Moby(targetLevel.game, null, Vector3.Zero, Quaternion.Identity, Vector3.One)
                {
                    modelID = vendorModelId,
                    light = 0,
                    pvarIndex = -1,
                    spawnType = 0,
                    missionID = 0,
                    dataval = 0,
                    bolts = 0,
                    drawDistance = 1000,
                    updateDistance = 1000,
                    unk7A = 8192,
                    unk8A = 16384,
                    unk12A = 256,
                    // Set other typical vendor properties
                    groupIndex = 0,
                    isRooted = 0,
                    rootedDistance = 0.0f,
                    cutscene = 0,
                    exp = 0,
                    mode = 0
                };
                
                Console.WriteLine("✅ Created vendor moby with default properties");
                return defaultVendorMoby;
            }
            
            // Use the first source vendor as a reference
            var sourceVendor = sourceVendorMobys[0];
            
            Console.WriteLine($"📍 Using source vendor position: {sourceVendor.position}");
            Console.WriteLine($"🔄 Using source vendor rotation: {sourceVendor.rotation}");
            Console.WriteLine($"📏 Using source vendor scale: {sourceVendor.scale}");
            
            // Create a new moby based on the source vendor
            var newVendorMoby = new Moby(sourceVendor)
            {
                // Override the game type to match target level
                // Note: This uses reflection since game field is private
            };
            
            // Set the game type using reflection since it's private
            try
            {
                var gameField = typeof(Moby).GetField("game", BindingFlags.NonPublic | BindingFlags.Instance);
                gameField?.SetValue(newVendorMoby, targetLevel.game);
                Console.WriteLine($"  🎮 Set game type to: {targetLevel.game.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️ Could not set game type: {ex.Message}");
            }
            
            // Ensure critical vendor properties
            newVendorMoby.modelID = vendorModelId;
            newVendorMoby.light = 0; // Standard for RC1 compatibility
            newVendorMoby.model = null; // Will be set later when model is replaced
            
            Console.WriteLine($"✅ Created new vendor moby (ID: {newVendorMoby.mobyID})");
            Console.WriteLine($"  📍 Position: {newVendorMoby.position}");
            Console.WriteLine($"  🔄 Rotation: {newVendorMoby.rotation}");
            Console.WriteLine($"  📏 Scale: {newVendorMoby.scale}");
            Console.WriteLine($"  💡 Light: {newVendorMoby.light}");
            Console.WriteLine($"  🎮 Game: {targetLevel.game.Name}");
            
            return newVendorMoby;
        }
    }
}
