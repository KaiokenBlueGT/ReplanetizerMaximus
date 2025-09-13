// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;  // Add this import for Moby class
using LibReplanetizer.Models;
using OpenTK.Mathematics;  // Add this import for Vector3

namespace GeometrySwapper
{
    public static class SwapHelper
    {
        // Delegates for progress reporting
        public delegate void ProgressUpdateHandler(string operation, float progress);
        public delegate void OperationStatusHandler(string operation, bool completed);

        // Add static fields to store the custom options
        private static TieSwapper.TieSwapOptions tieSwapOptions = TieSwapper.TieSwapOptions.Default;
        private static ShrubSwapper.ShrubSwapOptions shrubSwapOptions = ShrubSwapper.ShrubSwapOptions.Default;
        private static SwingshotOltanisSwapper.SwingshotSwapOptions swingshotSwapOptions = SwingshotOltanisSwapper.SwingshotSwapOptions.Default;
        private static VendorOltanisSwapper.VendorSwapOptions vendorSwapOptions = VendorOltanisSwapper.VendorSwapOptions.Default;
        private static CratesOltanisSwapper.CrateSwapOptions crateSwapOptions = CratesOltanisSwapper.CrateSwapOptions.Default;
        private static MobyOltanisInstancer.InstancerOptions mobyInstancerOptions = MobyOltanisInstancer.InstancerOptions.Default;
        private static int specialMobyCopyOptions = 0; // No special mobys by default
        private static VendorOltanisSwapper.VendorModelSource vendorModelSource = VendorOltanisSwapper.VendorModelSource.RC1;
        private static CuboidImporter.CuboidImportOptions cuboidImportOptions = CuboidImporter.CuboidImportOptions.Default;
        private static SplineImporter.SplineImportOptions splineImportOptions = SplineImporter.SplineImportOptions.Default;
        private static AnimationRestoreOptions animationRestoreOptions = AnimationRestoreOptions.Default;

        public static void PerformSwap(
            string rc1SourceLevelDir,
            string rc3DonorLevelDir,        // Change from rc2DonorLevelDir
            string referenceUyaPlanetDir,   // Change from referenceGcPlanetDir
            string globalRc3Dir,            // Change from globalRc2Dir
            string outputDir,
            SwapOptions options,
            ProgressUpdateHandler progressUpdate = null,
            OperationStatusHandler operationStatus = null)
        {
            try
            {
                progressUpdate?.Invoke("Starting Geometry Swap", 0.0f);
                Console.WriteLine("--- Starting Geometry Swap ---");

                // Validate paths
                if (!Directory.Exists(rc1SourceLevelDir) || !Directory.Exists(rc3DonorLevelDir) || !Directory.Exists(referenceUyaPlanetDir) || !Directory.Exists(globalRc3Dir))
                {
                    Console.WriteLine("Error: One or more input directories do not exist.");
                    return;
                }

                Directory.CreateDirectory(outputDir);

                // Load levels
                progressUpdate?.Invoke("Loading levels...", 0.05f);
                Console.WriteLine("Loading levels...");
                var rc1SourceLevel = new Level(Path.Combine(rc1SourceLevelDir, "engine.ps3"));
                var rc3DonorLevel = new Level(Path.Combine(rc3DonorLevelDir, "engine.ps3"));
                // Load a pristine copy of the donor level for later diagnostics
                var pristineDonorLevel = new Level(Path.Combine(rc3DonorLevelDir, "engine.ps3"));
                var referenceUyaPlanet = new Level(Path.Combine(referenceUyaPlanetDir, "engine.ps3"));
                Console.WriteLine("Levels loaded successfully.");

                // Ensure the vendor logo model exists by initializing from reference level
                if (options.HasFlag(SwapOptions.CopySpecialMobysOnly) || 
                    (specialMobyCopyOptions & 1) != 0 || // Vendor Orb flag
                    options.HasFlag(SwapOptions.SwapVendorWithOltanis))
                {
                    Console.WriteLine("Ensuring vendor logo model exists...");
                    VendorOltanisSwapper.EnsureVendorLogoModel(rc3DonorLevel, referenceUyaPlanet);
                }

                float progressStep = 0.7f / 19; // Divide main progress among operations
                float currentProgress = 0.1f;

                // Perform swaps based on selected options
                if (options.HasFlag(SwapOptions.Terrain))
                {
                    progressUpdate?.Invoke("Swapping Terrain...", currentProgress);
                    Console.WriteLine("Swapping Terrain...");
                    rc3DonorLevel.terrainEngine = rc1SourceLevel.terrainEngine;
                    
                    // Only import terrain textures, not all textures
                    Console.WriteLine("Importing terrain textures only...");
                    TextureTransfer.ImportTerrainTexturesOnly(rc3DonorLevel, rc1SourceLevel);
                    
                    operationStatus?.Invoke("Terrain", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Terrain", false);
                }

                if (options.HasFlag(SwapOptions.Collision))
                {
                    progressUpdate?.Invoke("Swapping Collision...", currentProgress);
                    Console.WriteLine("Swapping Collision...");
                    // Copy parsed collision model so the renderer reflects the new data
                    rc3DonorLevel.collisionEngine = rc1SourceLevel.collisionEngine;

                    // Also copy the raw collision bytes used during serialization
                    rc3DonorLevel.collBytesEngine = rc1SourceLevel.collBytesEngine;

                    // If the source level has collision chunks, copy those as well
                    if (rc1SourceLevel.collisionChunks != null && rc1SourceLevel.collisionChunks.Count > 0)
                    {
                        rc3DonorLevel.collisionChunks = new List<Collision>(rc1SourceLevel.collisionChunks);
                        rc3DonorLevel.collBytesChunks = rc1SourceLevel.collBytesChunks != null
                            ? new List<byte[]>(rc1SourceLevel.collBytesChunks)
                            : new List<byte[]>();
                    }
                    operationStatus?.Invoke("Collision", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Collision", false);
                }

                if (options.HasFlag(SwapOptions.Ties))
                {
                    progressUpdate?.Invoke("Swapping Ties...", currentProgress);
                    Console.WriteLine("Swapping Ties...");
                    TieSwapper.SwapTiesWithRC1Oltanis(rc3DonorLevel, rc1SourceLevel, tieSwapOptions);
                    operationStatus?.Invoke("Ties", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Ties", false);
                }

                if (options.HasFlag(SwapOptions.Shrubs))
                {
                    progressUpdate?.Invoke("Swapping Shrubs...", currentProgress);
                    Console.WriteLine("Swapping Shrubs...");
                    ShrubSwapper.SwapShrubsWithRC1Oltanis(rc3DonorLevel, rc1SourceLevel, shrubSwapOptions);
                    operationStatus?.Invoke("Shrubs", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Shrubs", false);
                }

                if (options.HasFlag(SwapOptions.Skybox))
                {
                    progressUpdate?.Invoke("Swapping Skybox...", currentProgress);
                    Console.WriteLine("Swapping Skybox...");

                    // Use proper SkyboxSwapper for correct texture handling
                    SkyboxSwapper.SwapSkybox(rc3DonorLevel, rc1SourceLevel, rc3DonorLevel);

                    operationStatus?.Invoke("Skybox", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Skybox", false);
                }

                if (options.HasFlag(SwapOptions.GrindPaths))
                {
                    progressUpdate?.Invoke("Swapping Grind Paths...", currentProgress);
                    Console.WriteLine("Swapping Grind Paths...");
                    GrindPathSwapper.SwapGrindPathsWithRC1Oltanis(rc3DonorLevel, rc1SourceLevel, GrindPathSwapper.GrindPathSwapOptions.FullReplacement);
                    operationStatus?.Invoke("GrindPaths", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("GrindPaths", false);
                }

                if (options.HasFlag(SwapOptions.PointLights))
                {
                    progressUpdate?.Invoke("Swapping Point Lights...", currentProgress);
                    Console.WriteLine("Swapping Point Lights...");
                    
                    // Use PointLightsSwapper instead of direct assignment
                    PointLightsSwapper.SwapPointLights(rc3DonorLevel, rc1SourceLevel);
                    
                    operationStatus?.Invoke("PointLights", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("PointLights", false);
                }

                if (options.HasFlag(SwapOptions.SoundInstances))
                {
                    progressUpdate?.Invoke("Swapping Sound Instances...", currentProgress);
                    Console.WriteLine("Swapping Sound Instances...");
                    rc3DonorLevel.soundInstances = rc1SourceLevel.soundInstances;
                    operationStatus?.Invoke("SoundInstances", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("SoundInstances", false);
                }

                if (options.HasFlag(SwapOptions.Mobys))
                {
                    progressUpdate?.Invoke("Swapping Mobys...", currentProgress);
                    Console.WriteLine("Swapping Mobys...");
                    // Determine which moby types to copy based on special moby options
                    var mobyTypes = GetMobyTypesFromOptions(specialMobyCopyOptions);
                    if (mobyTypes.Count > 0)
                    {
                        // Use CopyMobysToLevel with filtering
                        MobySwapper.CopyMobysToLevel(rc3DonorLevel, rc1SourceLevel, true, mobyTypes);
                        operationStatus?.Invoke("Mobys", true);
                    }
                    else
                    {
                        Console.WriteLine("No special moby types selected - skipping moby copy.");
                        operationStatus?.Invoke("Mobys", false);
                    }
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Mobys", false);
                }

                if (options.HasFlag(SwapOptions.SwapLevelVariables))
                {
                    progressUpdate?.Invoke("Swapping Level Variables...", currentProgress);
                    Console.WriteLine("Swapping Level Variables...");
                    
                    // Make a safety check
                    if (rc1SourceLevel.levelVariables != null && rc3DonorLevel.levelVariables != null)
                    {
                        // Only copy what's needed to avoid potential serialization issues
                        rc3DonorLevel.levelVariables.CopyFrom(rc1SourceLevel.levelVariables);
                    }
                    else
                    {
                        Console.WriteLine("⚠️ Warning: One or both levels have null level variables, skipping this operation.");
                    }
                    
                    operationStatus?.Invoke("LevelVariables", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("LevelVariables", false);
                }

                if (options.HasFlag(SwapOptions.TransferRatchetPosition))
                {
                    progressUpdate?.Invoke("Transferring Ratchet Position...", currentProgress);
                    Console.WriteLine("Swapping Ratchet's Position...");
                    if (rc1SourceLevel.mobs.Count > 0 && rc3DonorLevel.mobs.Count > 0)
                    {
                        rc3DonorLevel.mobs[0].position = rc1SourceLevel.mobs[0].position;
                    }
                    operationStatus?.Invoke("RatchetPosition", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("RatchetPosition", false);
                }

                if (options.HasFlag(SwapOptions.RegisterPlanetInMap))
                {
                    progressUpdate?.Invoke("Preparing map registration...", currentProgress);
                    // We'll just prepare here - the actual map registration will be handled by the caller
                    // The GalacticMapManager class should be used instead of MapPatcher
                    operationStatus?.Invoke("PlanetMap", false); // Will be marked as true after full processing
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("PlanetMap", false);
                }

                if (options.HasFlag(SwapOptions.SwapVendorWithOltanis))
                {
                    progressUpdate?.Invoke("Swapping Vendor...", currentProgress);
                    Console.WriteLine("Swapping Vendor...");
                    Level vendorSource = vendorModelSource == VendorOltanisSwapper.VendorModelSource.RC1 ? rc1SourceLevel : rc3DonorLevel;
                    bool vendorSuccess = VendorOltanisSwapper.SwapVendorModel(rc3DonorLevel, vendorSource, vendorModelSource, vendorSwapOptions);
                    operationStatus?.Invoke("Vendor", vendorSuccess);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Vendor", false);
                }

                if (options.HasFlag(SwapOptions.SwapCratesWithOltanis))
                {
                    // Always attempt to swap crates if it's selected, regardless of CopySpecialMobysOnly
                    progressUpdate?.Invoke("Swapping Crates...", currentProgress);
                    Console.WriteLine("Swapping Crates with RC1 Oltanis...");
                    
                    // Use the existing method signature with 3 parameters
                    bool crateSuccess = CratesOltanisSwapper.SwapCratesWithRC1Oltanis(
                        rc3DonorLevel, 
                        rc1SourceLevel, 
                        crateSwapOptions
                    );
                    
                    operationStatus?.Invoke("Crates", crateSuccess);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Crates", false);
                }

                if (options.HasFlag(SwapOptions.SwapSwingshots))
                {
                    progressUpdate?.Invoke("Swapping Swingshots...", currentProgress);
                    Console.WriteLine("Swapping Swingshots...");
                    // Use SwingshotOltanisSwapper instead of SwingshotSwapper
                    SwingshotOltanisSwapper.SwapSwingshotsWithRC1Oltanis(rc3DonorLevel, rc1SourceLevel, swingshotSwapOptions);
                    operationStatus?.Invoke("Swingshots", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Swingshots", false);
                }

                if (options.HasFlag(SwapOptions.SwapShipExitAnim))
                {
                    progressUpdate?.Invoke("Swapping Ship Exit Animation...", currentProgress);
                    // Use SwapAnimation method instead of SwapShipExitAnimation
                    SwapShipExitAnim.SwapAnimation(rc3DonorLevel, rc1SourceLevel);
                    operationStatus?.Invoke("ShipExitAnimation", true);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("ShipExitAnimation", false);
                }

                if (options.HasFlag(SwapOptions.ImportCuboids))
                {
                    progressUpdate?.Invoke("Importing Cuboids...", currentProgress);
                    Console.WriteLine("Importing Cuboids...");
                    bool cuboidSuccess = CuboidImporter.ImportCuboidsFromRC1(rc3DonorLevel, rc1SourceLevel, cuboidImportOptions);
                    operationStatus?.Invoke("Cuboids", cuboidSuccess);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Cuboids", false);
                }

                if (options.HasFlag(SwapOptions.ImportSplines))
                {
                    progressUpdate?.Invoke("Importing Splines...", currentProgress);
                    Console.WriteLine("Importing Splines...");
                    bool splineSuccess = SplineImporter.ImportSplinesFromRC1(rc3DonorLevel, rc1SourceLevel, splineImportOptions);
                    operationStatus?.Invoke("Splines", splineSuccess);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Splines", false);
                }

                if (options.HasFlag(SwapOptions.RestoreAnimations))
                {
                    progressUpdate?.Invoke("Restoring Animations...", currentProgress);
                    Console.WriteLine("Restoring Animations...");
                    bool animSuccess = AnimationRestorer.RestoreAnimationsFromLoadedLevels(rc3DonorLevel, rc1SourceLevel, rc3DonorLevel, animationRestoreOptions);
                    operationStatus?.Invoke("Animations", animSuccess);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("Animations", false);
                }

                // Ensure CopySpecialMobys section is called properly when needed:
                if (options.HasFlag(SwapOptions.CopySpecialMobysOnly))
                {
                    progressUpdate?.Invoke("Copying Special Mobys...", currentProgress);
                    Console.WriteLine("Copying Special Mobys...");
                    
                    // Use the specialMobyCopyOptions to control which special mobys are copied
                    bool success = CopySpecialMobys(rc3DonorLevel, rc1SourceLevel, specialMobyCopyOptions);
                    
                    operationStatus?.Invoke("SpecialMobys", success);
                    currentProgress += progressStep;
                }
                else
                {
                    operationStatus?.Invoke("SpecialMobys", false);
                }

                // Save the modified level with TIE culling protection
                progressUpdate?.Invoke("Saving modified level...", 0.9f);
                Console.WriteLine("Saving modified level...");

                TieCullingDataFixer.RebuildTieOcclusion(rc3DonorLevel, rc1SourceLevel);

                // Preserve TIE culling data before saving to prevent corruption
                var tieCullingProtection = TieCullingDataFixer.PreserveTieCullingData(rc3DonorLevel);

                // Apply coordinate conversion if swapping between different game versions
                if (options.HasFlag(SwapOptions.Ties) && rc1SourceLevel.game.num != rc3DonorLevel.game.num)
                {
                    TieCullingDataFixer.ApplyCoordinateConversionToTieData(
                        rc3DonorLevel,
                        rc1SourceLevel.game.num,
                        rc3DonorLevel.game.num);

                    // Capture converted occlusion data for restoration
                    TieCullingDataFixer.PreserveTieCullingData(rc3DonorLevel, tieCullingProtection);
                }

                rc3DonorLevel.Save(outputDir);

                // Restore preserved TIE culling data after save
                TieCullingDataFixer.RestoreTieCullingData(rc3DonorLevel, tieCullingProtection);

                // Load the saved target level and run occlusion diagnostics
                var targetLevel = new Level(Path.Combine(outputDir, "engine.ps3"));
                if (options.HasFlag(SwapOptions.RunOcclusionDiagnostics))
                {
                    OcclusionDiagnostics.CompareOcclusionData(rc1SourceLevel, pristineDonorLevel, targetLevel, outputDir);
                }

                Console.WriteLine($"--- Geometry Swap Finished. Output saved to: {outputDir} ---");
                progressUpdate?.Invoke("Geometry swap completed successfully!", 1.0f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during the swap process: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                progressUpdate?.Invoke($"Error: {ex.Message}", 1.0f);
                throw; // Re-throw to notify the caller
            }
        }

        // Add a method to set the options
        // Update the SetSwapperOptions method to include the specialMobyCopyOptions parameter
        public static void SetSwapperOptions(
            TieSwapper.TieSwapOptions tieOpts,
            ShrubSwapper.ShrubSwapOptions shrubOpts,
            SwingshotOltanisSwapper.SwingshotSwapOptions swingshotOpts,
            VendorOltanisSwapper.VendorSwapOptions vendorOpts,
            CratesOltanisSwapper.CrateSwapOptions crateOpts,
            MobyOltanisInstancer.InstancerOptions mobyOpts,
            int specialMobyCopyOpts,
            VendorOltanisSwapper.VendorModelSource vendorModelSrc,
            CuboidImporter.CuboidImportOptions cuboidOpts,
            SplineImporter.SplineImportOptions splineOpts,
            AnimationRestoreOptions animOpts)
        {
            tieSwapOptions = tieOpts;
            shrubSwapOptions = shrubOpts;
            swingshotSwapOptions = swingshotOpts;
            vendorSwapOptions = vendorOpts;
            crateSwapOptions = crateOpts;
            mobyInstancerOptions = mobyOpts;
            specialMobyCopyOptions = specialMobyCopyOpts;
            vendorModelSource = vendorModelSrc;
            cuboidImportOptions = cuboidOpts;
            splineImportOptions = splineOpts;
            animationRestoreOptions = animOpts;
        }

        /// <summary>
        /// Copies special mobys from the source level to the target level based on options
        /// </summary>
        /// <param name="targetLevel">The level to copy mobys to</param>
        /// <param name="sourceLevel">The level to copy mobys from</param>
        /// <param name="specialMobyCopyOptions">Bit flags controlling which special mobys to copy</param>
        /// <returns>True if at least one moby was successfully copied</returns>
        private static bool CopySpecialMobys(Level targetLevel, Level sourceLevel, int specialMobyCopyOptions)
        {
            Console.WriteLine("\n==== Copying Special Mobys ====");
            
            bool anySuccessful = false;
            bool anyAttempted = false;
            
            try {
                // Copy Vendor Orb if selected (option value 1)
                if ((specialMobyCopyOptions & 1) != 0)
                {
                    anyAttempted = true;
                    Console.WriteLine("Copying Vendor Orb...");
                    bool vendorOrbSuccess = CopyVendorOrb(targetLevel, sourceLevel);
                    anySuccessful |= vendorOrbSuccess;
                }
                
                // Copy Vendor if selected (option value 2)
                if ((specialMobyCopyOptions & 2) != 0)
                {
                    anyAttempted = true;
                    Console.WriteLine("Copying Vendor...");
                    bool vendorSuccess = VendorOltanisSwapper.SwapVendorWithRC1Oltanis(
                        targetLevel, sourceLevel, VendorOltanisSwapper.VendorSwapOptions.FullReplacement);
                    anySuccessful |= vendorSuccess;
                }
                
                // Copy Swingshot Nodes if selected (option value 4)
                if ((specialMobyCopyOptions & 4) != 0)
                {
                    anyAttempted = true;
                    Console.WriteLine("Copying Swingshot Nodes...");
                    bool swingshotSuccess = CopySwingshotNodes(targetLevel, sourceLevel);
                    anySuccessful |= swingshotSuccess;
                }
                
                // Copy Nanotech Crates if selected (option value 8)
                if ((specialMobyCopyOptions & 8) != 0)
                {
                    anyAttempted = true;
                    Console.WriteLine("Copying Nanotech Crates...");
                    bool nanotechSuccess = CopyNanotechCrates(targetLevel, sourceLevel);
                    anySuccessful |= nanotechSuccess;
                }
                
                // Copy Ammo Vendors if selected (option value 16)
                if ((specialMobyCopyOptions & 16) != 0)
                {
                    anyAttempted = true;
                    Console.WriteLine("Copying Ammo Vendors...");
                    bool ammoVendorSuccess = CopyAmmoVendors(targetLevel, sourceLevel);
                    anySuccessful |= ammoVendorSuccess;
                }

                if (!anyAttempted)
                {
                    Console.WriteLine("No special moby types selected for copying.");
                    return true; // Return success if nothing was selected (not a failure case)
                }
                
                // Final summary
                if (anySuccessful)
                {
                    Console.WriteLine("\n==== Special Moby Copy Summary ====");
                    Console.WriteLine("✅ Successfully copied one or more special moby types");
                }
                else
                {
                    Console.WriteLine("\n==== Special Moby Copy Summary ====");
                    Console.WriteLine("⚠️ Failed to copy any special mobys");
                }
                
                return anySuccessful;
            }
            catch (Exception ex) {
                Console.WriteLine($"❌ Error in CopySpecialMobys: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Convert the bitfield of special moby options into a list of MobySwapper type keys.
        /// Returns an empty list when no types are selected so no mobys are copied by default.
        /// </summary>
        private static List<string> GetMobyTypesFromOptions(int specialOptions)
        {
            var types = new List<string>();

            if ((specialOptions & 1) != 0)
                types.Add("VendorLogo");
            if ((specialOptions & 2) != 0)
                types.Add("Vendor");
            if ((specialOptions & 4) != 0)
            {
                types.Add("SwingshotNode");
                types.Add("SwingshotPull");
            }
            if ((specialOptions & 8) != 0)
                types.Add("NanotechCrate");
            if ((specialOptions & 16) != 0)
                types.Add("AmmoCrate");

            return types;
        }

        /// <summary>
        /// Copies the vendor orb (Megacorp/Gadgetron logo) from source level to target level
        /// </summary>
        private static bool CopyVendorOrb(Level targetLevel, Level sourceLevel)
        {
            try
            {
                // Vendor Orb model ID (Megacorp/Gadgetron logo)
                const int vendorOrbModelId = 1143;
                
                Console.WriteLine("\n==== Copying Vendor Logo (ID: 1143) ====");
                
                // IMPORTANT FIX: First verify or create the model before trying to create instances
                bool modelExists = false;
                var targetLogoModel = targetLevel.mobyModels?.FirstOrDefault(m => m.id == vendorOrbModelId) as MobyModel;
                var sourceLogoModel = sourceLevel.mobyModels?.FirstOrDefault(m => m.id == vendorOrbModelId) as MobyModel;
                
                // If target doesn't have the model but source does, copy it over
                if (targetLogoModel == null && sourceLogoModel != null)
                {
                    Console.WriteLine("✅ Found vendor logo model in source level, importing to target level");
                    targetLogoModel = (MobyModel)MobySwapper.DeepCloneModel(sourceLogoModel);
                    
                    if (targetLevel.mobyModels == null)
                        targetLevel.mobyModels = new List<Model>();
                    
                    targetLevel.mobyModels.Add(targetLogoModel);
                    
                    // Import textures for this model using vendor swapper's method
                    VendorOltanisSwapper.ImportModelTextures(targetLevel, sourceLevel, targetLogoModel);
                    modelExists = true;
                    Console.WriteLine("✅ Imported vendor logo model and textures successfully");
                }
                else if (targetLogoModel != null)
                {
                    Console.WriteLine("✅ Found vendor logo model in target level");
                    
                    // If we have a source model, update textures to ensure they're correct
                    if (sourceLogoModel != null)
                    {
                        Console.WriteLine("Updating vendor logo textures from source level...");
                        VendorOltanisSwapper.ImportModelTextures(targetLevel, sourceLevel, targetLogoModel);
                    }
                    
                    modelExists = true;
                }
                else
                {
                    // No model in either level - we'll need to generate one or find a replacement
                    Console.WriteLine("⚠️ Vendor logo model not found in source or target level!");
                    
                    // FALLBACK: Look for a vendor logo texture and create a simple model
                    // (Implementation would depend on how your engine handles model creation)
                    Console.WriteLine("This will require manual model creation or a special import");
                    modelExists = false;
                }
                
                // Find all vendor orbs in source level
                var sourceVendorOrbs = sourceLevel.mobs?.Where(m => m.modelID == vendorOrbModelId).ToList();
                if (sourceVendorOrbs == null || sourceVendorOrbs.Count == 0)
                {
                    Console.WriteLine($"⚠️ No vendor logos found in source level. Checking for existing vendor logos in target level...");
                    
                    // Check if the target level already has a vendor logo
                    var existingVendorLogos = targetLevel.mobs?.Where(m => m.modelID == vendorOrbModelId).ToList();
                    if (existingVendorLogos != null && existingVendorLogos.Count > 0)
                    {
                        Console.WriteLine($"✅ Found {existingVendorLogos.Count} existing vendor logos in target level. Ensuring properties are correct.");
                        
                        // Make sure the vendor logo model has correct properties
                        VendorOltanisSwapper.EnsureVendorLogoProperties(targetLevel);
                        
                        Console.WriteLine($"\n==== Vendor Logo Copy Summary ====");
                        Console.WriteLine($"✅ Preserved existing vendor logo instances and updated their properties");
                        return true;
                    }
                    
                    // No source or target logos, but we have the model (either existing or imported)
                    if (modelExists)
                    {
                        Console.WriteLine($"⚠️ No vendor logos found in source or target level. Creating one at default position.");
                        
                        // Create a new vendor logo moby at a default position in front of the vendor
                        int nextMobyId = targetLevel.mobs?.Max(m => m.mobyID) + 1 ?? 1000;
                        
                        // Find vendor position to place logo near it
                        Vector3 vendorPosition = new Vector3(0, 0, 50); // Default position if no vendor
                        var vendor = targetLevel.mobs?.FirstOrDefault(m => m.modelID == 11); // Vendor ID is 11
                        if (vendor != null)
                        {
                            // Position logo slightly in front of and above vendor
                            vendorPosition = vendor.position + new Vector3(0, 3, 2);
                            Console.WriteLine($"  Found vendor at position {vendor.position}, placing logo at {vendorPosition}");
                        }
                        else
                        {
                            Console.WriteLine("  No vendor found, placing logo at default position");
                        }
                        
                        var newVendorLogo = new Moby
                        {
                            mobyID = nextMobyId,
                            modelID = vendorOrbModelId,
                            model = targetLogoModel,
                            position = vendorPosition,
                            rotation = Quaternion.Identity,
                            scale = new Vector3(1, 1, 1),
                            light = 0,  // Match RC1 Oltanis
                            drawDistance = 150,
                            updateDistance = 200
                        };
                        
                        // Update transform matrix
                        newVendorLogo.UpdateTransformMatrix();
                        
                        // Add to target level
                        if (targetLevel.mobs == null)
                        {
                            targetLevel.mobs = new List<Moby>();
                        }
                        targetLevel.mobs.Add(newVendorLogo);
                        
                        // Update mobyIds list if needed
                        if (targetLevel.mobyIds != null)
                        {
                            targetLevel.mobyIds = targetLevel.mobs.Select(m => m.mobyID).ToList();
                        }
                        
                        // Ensure vendor logo properties are correct
                        VendorOltanisSwapper.EnsureVendorLogoProperties(targetLevel);
                        
                        Console.WriteLine($"\n==== Vendor Logo Copy Summary ====");
                        Console.WriteLine($"✅ Created new vendor logo at position {vendorPosition}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"❌ Cannot create vendor logo: Model not available in source or target level");
                        return false;
                    }
                }
                
                // SOURCE LOGO MODEL FOUND - Copy it with proper texture handling
                Console.WriteLine($"Found {sourceVendorOrbs.Count} vendor logos in source level");
                
                // Now copy the vendor orb instances
                Console.WriteLine("Copying vendor logo instances...");
                
                int nextId = targetLevel.mobs?.Max(m => m.mobyID) + 1 ?? 1000;
                
                // Clear existing vendor logos if they exist
                if (targetLevel.mobs != null)
                {
                    int removed = targetLevel.mobs.RemoveAll(m => m.modelID == vendorOrbModelId);
                    if (removed > 0)
                        Console.WriteLine($"  Removed {removed} existing vendor logo instances to avoid duplicates");
                }
                
                foreach (var sourceOrb in sourceVendorOrbs)
                {
                    // Create a new vendor orb in the target level
                    var newOrb = new Moby
                    {
                        mobyID = nextId++,
                        modelID = vendorOrbModelId,
                        model = targetLevel.mobyModels.FirstOrDefault(m => m.id == vendorOrbModelId),
                        position = sourceOrb.position,
                        rotation = sourceOrb.rotation,
                        scale = sourceOrb.scale,
                        light = 0, // Match RC1 Oltanis
                        drawDistance = sourceOrb.drawDistance > 0 ? sourceOrb.drawDistance : 150,
                        updateDistance = sourceOrb.updateDistance > 0 ? sourceOrb.updateDistance : 200
                    };
                    
                    // Update transform matrix
                    newOrb.UpdateTransformMatrix();
                    
                    // Copy pVars if they exist
                    if (sourceOrb.pVars != null && sourceOrb.pVars.Length > 0)
                    {
                        newOrb.pVars = new byte[sourceOrb.pVars.Length];
                        Array.Copy(sourceOrb.pVars, newOrb.pVars, sourceOrb.pVars.Length);
                    }
                    
                    // Add to target level
                    if (targetLevel.mobs == null)
                        targetLevel.mobs = new List<Moby>();
    
                    targetLevel.mobs.Add(newOrb);
                    Console.WriteLine($"  Copied vendor logo to position {newOrb.position}");
                }
                
                // Update mobyIds list if needed
                if (targetLevel.mobyIds != null)
                {
                    targetLevel.mobyIds = targetLevel.mobs.Select(m => m.mobyID).ToList();
                }
                
                // Ensure vendor logo properties are correct
                VendorOltanisSwapper.EnsureVendorLogoProperties(targetLevel);
                
                // Validate and fix pVar indices to prevent any conflicts
                MobySwapper.ValidateAndFixPvarIndices(targetLevel);
                
                Console.WriteLine($"\n==== Vendor Logo Copy Summary ====");
                Console.WriteLine($"✅ Successfully processed vendor logo model, instances and textures");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error copying vendor logo: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Copies swingshot nodes from source level to target level
        /// </summary>
        /// <param name="targetLevel">The level to copy to</param>
        /// <param name="sourceLevel">The level to copy from</param>
        /// <returns>True if at least one swingshot node was copied successfully</returns>
        private static bool CopySwingshotNodes(Level targetLevel, Level sourceLevel)
        {
            // Use SwingshotOltanisSwapper with options to create missing nodes
            try
            {
                Console.WriteLine("\n==== Copying Swingshot Nodes ====");
                bool success = SwingshotOltanisSwapper.SwapSwingshotsWithRC1Oltanis(
                    targetLevel, 
                    sourceLevel, 
                    SwingshotOltanisSwapper.SwingshotSwapOptions.CreateMissing | SwingshotOltanisSwapper.SwingshotSwapOptions.SetLightToZero
                );
                
                if (success)
                {
                    Console.WriteLine("✅ Successfully copied swingshot nodes from source level");
                }
                else
                {
                    Console.WriteLine("⚠️ No swingshot nodes were copied, possibly due to missing models");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error copying swingshot nodes: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Copies nanotech crates from source level to target level
        /// </summary>
        /// <param name="targetLevel">The level to copy to</param>
        /// <param name="sourceLevel">The level to copy from</param>
        /// <returns>True if at least one nanotech crate was copied successfully</returns>
        private static bool CopyNanotechCrates(Level targetLevel, Level sourceLevel)
        {
            try
            {
                Console.WriteLine("\n==== Copying Nanotech Crates ====");
                
                // Nanotech crate model IDs (based on CratesOltanisSwapper.NanotechCrateIds)
                int[] nanotechCrateIds = { 512, 501 };
                bool anyCopied = false;
                
                foreach (var crateId in nanotechCrateIds)
                {
                    Console.WriteLine($"Looking for nanotech crates with model ID {crateId}...");
                    var sourceCrates = sourceLevel.mobs?.Where(m => m.modelID == crateId).ToList() ?? new List<Moby>();
                    
                    if (sourceCrates.Count == 0)
                    {
                        Console.WriteLine($"  No nanotech crates with model ID {crateId} found in source level");
                        continue;
                    }
                    
                    Console.WriteLine($"  Found {sourceCrates.Count} nanotech crates with model ID {crateId} in source level");
                    
                    // Check if the model exists in target level
                    var crateModel = targetLevel.mobyModels?.FirstOrDefault(m => m.id == crateId);
                    if (crateModel == null)
                    {
                        Console.WriteLine($"  ⚠️ Nanotech crate model ID {crateId} not found in target level");
                        
                        // Try to copy the model from source level
                        var sourceCrateModel = sourceLevel.mobyModels?.FirstOrDefault(m => m.id == crateId);
                        if (sourceCrateModel != null)
                        {
                            crateModel = (MobyModel)MobySwapper.DeepCloneModel(sourceCrateModel);
                            
                            // Add to target level
                            if (targetLevel.mobyModels == null)
                            {
                                targetLevel.mobyModels = new List<LibReplanetizer.Models.Model>();
                            }
                            targetLevel.mobyModels.Add(crateModel);
                            Console.WriteLine($"  ✅ Copied nanotech crate model ID {crateId} to target level");
                        }
                        else
                        {
                            Console.WriteLine($"  ❌ Nanotech crate model ID {crateId} not found in source level either");
                            continue;
                        }
                    }
                    
                    // Get next moby ID for new instances
                    int nextMobyId = 1000;
                    if (targetLevel.mobs != null && targetLevel.mobs.Count > 0)
                    {
                        nextMobyId = targetLevel.mobs.Max(m => m.mobyID) + 1;
                    }
                    
                    // Copy each nanotech crate instance
                    foreach (var sourceCrate in sourceCrates)
                    {
                        var newCrate = new Moby
                        {
                            mobyID = nextMobyId++,
                            modelID = crateId,
                            model = crateModel,
                            position = sourceCrate.position,
                            rotation = sourceCrate.rotation,
                            scale = sourceCrate.scale,
                            light = 0, // Match RC1 Oltanis
                            drawDistance = sourceCrate.drawDistance > 0 ? sourceCrate.drawDistance : 150,
                            updateDistance = sourceCrate.updateDistance > 0 ? sourceCrate.updateDistance : 200
                        };
                        
                        // Update transform matrix
                        newCrate.UpdateTransformMatrix();
                        
                        // Copy pVars if they exist
                        if (sourceCrate.pVars != null && sourceCrate.pVars.Length > 0)
                        {
                            newCrate.pVars = new byte[sourceCrate.pVars.Length];
                            Array.Copy(sourceCrate.pVars, newCrate.pVars, sourceCrate.pVars.Length);
                        }
                        
                        // Add to target level
                        if (targetLevel.mobs == null)
                        {
                            targetLevel.mobs = new List<Moby>();
                        }
                        targetLevel.mobs.Add(newCrate);
                        anyCopied = true;
                        
                        Console.WriteLine($"  ✅ Created nanotech crate with ID {newCrate.mobyID} at position {newCrate.position}");
                    }
                }
                
                if (anyCopied)
                {
                    Console.WriteLine("✅ Successfully copied nanotech crates from source level");
                    
                    // Update mobyIds list if needed
                    if (targetLevel.mobyIds != null)
                    {
                        targetLevel.mobyIds = targetLevel.mobs.Select(m => m.mobyID).ToList();
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ No nanotech crates were copied, possibly due to missing models");
                }
                
                return anyCopied;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error copying nanotech crates: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Copies ammo vendors from source level to target level
        /// </summary>
        /// <param name="targetLevel">The level to copy to</param>
        /// <param name="sourceLevel">The level to copy from</param>
        /// <returns>True if at least one ammo vendor was copied successfully</returns>
        private static bool CopyAmmoVendors(Level targetLevel, Level sourceLevel)
        {
            try
            {
                Console.WriteLine("\n==== Copying Ammo Vendors ====");
                
                // Ammo vendor model ID - this may need to be adjusted
                int[] ammoVendorIds = { 580, 581 }; // Placeholder IDs, adjust based on actual game data
                bool anyCopied = false;
                
                foreach (var vendorId in ammoVendorIds)
                {
                    Console.WriteLine($"Looking for ammo vendors with model ID {vendorId}...");
                    var sourceVendors = sourceLevel.mobs?.Where(m => m.modelID == vendorId).ToList() ?? new List<Moby>();
                    
                    if (sourceVendors.Count == 0)
                    {
                        Console.WriteLine($"  No ammo vendors with model ID {vendorId} found in source level");
                        continue;
                    }
                    
                    Console.WriteLine($"  Found {sourceVendors.Count} ammo vendors with model ID {vendorId} in source level");
                    
                    // Check if the model exists in target level
                    var vendorModel = targetLevel.mobyModels?.FirstOrDefault(m => m.id == vendorId);
                    if (vendorModel == null)
                    {
                        Console.WriteLine($"  ⚠️ Ammo vendor model ID {vendorId} not found in target level");
                        
                        // Try to copy the model from source level
                        var sourceVendorModel = sourceLevel.mobyModels?.FirstOrDefault(m => m.id == vendorId);
                        if (sourceVendorModel != null)
                        {
                            vendorModel = (MobyModel)MobySwapper.DeepCloneModel(sourceVendorModel);
                            
                            // Add to target level
                            if (targetLevel.mobyModels == null)
                            {
                                targetLevel.mobyModels = new List<LibReplanetizer.Models.Model>();
                            }
                            targetLevel.mobyModels.Add(vendorModel);
                            Console.WriteLine($"  ✅ Copied ammo vendor model ID {vendorId} to target level");
                        }
                        else
                        {
                            Console.WriteLine($"  ❌ Ammo vendor model ID {vendorId} not found in source level either");
                            continue;
                        }
                    }
                    
                    // Get next moby ID for new instances
                    int nextMobyId = 1000;
                    if (targetLevel.mobs != null && targetLevel.mobs.Count > 0)
                    {
                        nextMobyId = targetLevel.mobs.Max(m => m.mobyID) + 1;
                    }
                    
                    // Copy each ammo vendor instance
                    foreach (var sourceVendor in sourceVendors)
                    {
                        var newVendor = new Moby
                        {
                            mobyID = nextMobyId++,
                            modelID = vendorId,
                            model = vendorModel,
                            position = sourceVendor.position,
                            rotation = sourceVendor.rotation,
                            scale = sourceVendor.scale,
                            light = 0, // Match RC1 Oltanis
                            drawDistance = sourceVendor.drawDistance > 0 ? sourceVendor.drawDistance : 200,
                            updateDistance = sourceVendor.updateDistance > 0 ? sourceVendor.updateDistance : 250
                        };
                        
                        // Update transform matrix
                        newVendor.UpdateTransformMatrix();
                        
                        // Copy pVars if they exist
                        if (sourceVendor.pVars != null && sourceVendor.pVars.Length > 0)
                        {
                            newVendor.pVars = new byte[sourceVendor.pVars.Length];
                            Array.Copy(sourceVendor.pVars, newVendor.pVars, sourceVendor.pVars.Length);
                        }
                        
                        // Add to target level
                        if (targetLevel.mobs == null)
                        {
                            targetLevel.mobs = new List<Moby>();
                        }
                        targetLevel.mobs.Add(newVendor);
                        anyCopied = true;
                        
                        Console.WriteLine($"  ✅ Created ammo vendor with ID {newVendor.mobyID} at position {newVendor.position}");
                    }
                }
                
                if (anyCopied)
                {
                    Console.WriteLine("✅ Successfully copied ammo vendors from source level");
                    
                    // Update mobyIds list if needed
                    if (targetLevel.mobyIds != null)
                    {
                        targetLevel.mobyIds = targetLevel.mobs.Select(m => m.mobyID).ToList();
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ No ammo vendors were copied, possibly due to missing models");
                }
                
                return anyCopied;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error copying ammo vendors: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Helper method to compare two textures for similarity
        /// </summary>
        private static bool TexturesMatch(Texture tex1, Texture tex2)
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
    }
}
