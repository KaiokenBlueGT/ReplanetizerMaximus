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
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Serializers; 
using OpenTK.Mathematics;
using static LibReplanetizer.DataFunctions;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles swapping the ship exit animation data (splines and level variables) from a source to a target level.
    /// </summary>
    public static class SwapShipExitAnim
    {
        /// <summary>
        /// Swaps the ship exit animation from a source level to a target level with improved cuboid handling.
        /// Uses existing target cuboids and updates their position/rotation to match RC1 values.
        /// </summary>
        /// <param name="targetLevel">The RC2 level to modify.</param>
        /// <param name="sourceLevel">The level to copy the ship animation data from.</param>
        /// <param name="rc2ReferenceLevel">Optional RC2 level to use as a template for proper cuboid format (e.g., Damosel)</param>
        /// <returns>True if the swap was successful, otherwise false.</returns>
        public static bool SwapAnimation(Level targetLevel, Level sourceLevel, Level rc2ReferenceLevel = null)
        {
            if (targetLevel?.levelVariables == null || sourceLevel?.levelVariables == null)
            {
                Console.WriteLine("❌ Cannot swap ship exit animation: LevelVariables are missing in the source or target level.");
                return false;
            }

            Console.WriteLine("\n==== Swapping Ship Exit Animation ====");
            Console.WriteLine($"Source Level Path: {sourceLevel.path ?? "Not set"}");
            Console.WriteLine($"Target Level Path: {targetLevel.path ?? "Not set"}");
            
            // Ask user which approach they want to use
            Console.WriteLine("\nSelect ship exit animation swap method:");
            Console.WriteLine("1. Use existing target cuboids and update their properties (recommended) [default]");
            Console.WriteLine("2. Create new cuboids with RC2 template (original method)");
            Console.Write("> ");
            
            string choice = Console.ReadLine()?.Trim() ?? "1";
            
            if (choice == "2")
            {
                // Use the original method with RC2 template approach
                return SwapAnimationWithRC2Template(targetLevel, sourceLevel, rc2ReferenceLevel);
            }
            else
            {
                // Use the new method that updates existing cuboids
                return SwapAnimationUsingExistingCuboids(targetLevel, sourceLevel);
            }
        }

        /// <summary>
        /// Original method renamed for clarity - creates new cuboids with RC2 template
        /// </summary>
        private static bool SwapAnimationWithRC2Template(Level targetLevel, Level sourceLevel, Level rc2ReferenceLevel)
        {
            if (targetLevel?.levelVariables == null || sourceLevel?.levelVariables == null)
            {
                Console.WriteLine("❌ Cannot swap ship exit animation: LevelVariables are missing in the source or target level.");
                return false;
            }

            Console.WriteLine("\n==== Swapping Ship Exit Animation (Using RC2 Template) ====");
            Console.WriteLine($"Source Level Path: {sourceLevel.path ?? "Not set"}");
            Console.WriteLine($"Target Level Path: {targetLevel.path ?? "Not set"}");
            
            // Ask user if they want to provide a manual RC2 reference level
            if (rc2ReferenceLevel == null)
            {
                Console.Write("Do you want to provide an RC2 reference level for improved cuboid compatibility? (y/n): ");
                string response = Console.ReadLine()?.Trim().ToLower();
                
                if (response == "y" || response == "yes")
                {
                    Console.Write("Enter path to RC2 reference level engine.ps3 file (e.g., Damosel): ");
                    string referencePath = Console.ReadLine()?.Trim();
                    
                    if (!string.IsNullOrEmpty(referencePath) && File.Exists(referencePath))
                    {
                        try
                        {
                            Console.WriteLine($"Loading RC2 reference level: {Path.GetFileName(referencePath)}...");
                            rc2ReferenceLevel = new Level(referencePath);
                            Console.WriteLine($"✅ Successfully loaded RC2 reference level");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Failed to load RC2 reference level: {ex.Message}");
                            Console.WriteLine("Continuing without RC2 reference level...");
                        }
                    }
                    else
                    {
                        Console.WriteLine("⚠️ Invalid path provided. Continuing without RC2 reference level...");
                    }
                }
            }
            
            if (rc2ReferenceLevel != null)
            {
                Console.WriteLine($"RC2 Reference Level: {rc2ReferenceLevel.path ?? "Not set"}");
            }
            else
            {
                Console.WriteLine("RC2 Reference Level: Not provided - using traditional cloning");
            }

            try
            {
                // Ensure the target level has the necessary lists
                if (targetLevel.splines == null)
                    targetLevel.splines = new List<Spline>();
                if (targetLevel.cuboids == null)
                    targetLevel.cuboids = new List<Cuboid>();

                // Get the IDs from the source level's variables
                int sourceShipPathId = sourceLevel.levelVariables.shipPathID;
                int sourceCamStartId = sourceLevel.levelVariables.shipCameraStartID;
                int sourceCamEndId = sourceLevel.levelVariables.shipCameraEndID;

                Console.WriteLine($"  Source IDs: ShipPath={sourceShipPathId}, CamStart={sourceCamStartId}, CamEnd={sourceCamEndId}");

                // Check if all IDs are valid
                bool validSourceIds = sourceShipPathId >= 0 && sourceCamStartId >= 0 && sourceCamEndId >= 0;

                if (!validSourceIds)
                {
                    Console.WriteLine("⚠️ Source level has invalid ship exit animation IDs. Using fallback approach.");
                    Console.WriteLine($"  Preserving target level's existing ship exit animation settings.");
                    Console.WriteLine($"  Target settings: ShipPath={targetLevel.levelVariables.shipPathID}, " +
                                      $"CamStart={targetLevel.levelVariables.shipCameraStartID}, " +
                                      $"CamEnd={targetLevel.levelVariables.shipCameraEndID}");
                    return true; // Return success since we're preserving existing settings
                }

                // Find or manually load the corresponding objects
                Spline shipPathSpline = FindOrLoadSplineById(sourceLevel, sourceShipPathId);
                
                // For cameras, we need to find the corresponding cuboids
                Cuboid camStartCuboid = FindCuboidById(sourceLevel, sourceCamStartId);
                Cuboid camEndCuboid = FindCuboidById(sourceLevel, sourceCamEndId);

                bool missingObjects = false;
                if (shipPathSpline == null)
                {
                    Console.WriteLine("⚠️ Ship path spline not found, will use a default path");
                    missingObjects = true;
                }
                if (camStartCuboid == null)
                {
                    Console.WriteLine("⚠️ Camera start cuboid not found, will use a default camera start");
                    missingObjects = true;
                }
                if (camEndCuboid == null)
                {
                    Console.WriteLine("⚠️ Camera end cuboid not found, will use a default camera end");
                    missingObjects = true;
                }

                // If any objects are missing, but we still want to proceed, we can create defaults
                if (missingObjects)
                {
                    Console.WriteLine("Creating default objects for missing animation components...");
                    
                    // Create default objects only for the missing ones
                    if (shipPathSpline == null)
                        shipPathSpline = CreateDefaultSpline();
                    
                    if (camStartCuboid == null)
                        camStartCuboid = CreateDefaultCuboidFromRC2Template(0, rc2ReferenceLevel);
                    
                    if (camEndCuboid == null)
                        camEndCuboid = CreateDefaultCuboidFromRC2Template(1, rc2ReferenceLevel);
                }

                // Copy objects to the target level and update the LevelVariables
                targetLevel.levelVariables.shipPathID = CopySplineToLevel(shipPathSpline, targetLevel);
                targetLevel.levelVariables.shipCameraStartID = CopyCuboidToLevelWithRC2Template(camStartCuboid, targetLevel, rc2ReferenceLevel);
                targetLevel.levelVariables.shipCameraEndID = CopyCuboidToLevelWithRC2Template(camEndCuboid, targetLevel, rc2ReferenceLevel);

                Console.WriteLine("  New Target IDs: " +
                                  $"ShipPath={targetLevel.levelVariables.shipPathID}, " +
                                  $"CamStart={targetLevel.levelVariables.shipCameraStartID}, " +
                                  $"CamEnd={targetLevel.levelVariables.shipCameraEndID}");

                Console.WriteLine("✅ Ship exit animation swapped successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An error occurred during ship animation swap: {ex.Message}");
                Console.WriteLine($"  The ship exit animation will remain unchanged.");
                return false;
            }
        }

        /// <summary>
        /// Enhanced ship exit animation swap that uses existing target cuboids and updates their positions/rotations
        /// </summary>
        /// <param name="targetLevel">The RC2/RC3 level to modify</param>
        /// <param name="sourceLevel">The RC1 level to get animation data from</param>
        /// <returns>True if the swap was successful</returns>
        public static bool SwapAnimationUsingExistingCuboids(Level targetLevel, Level sourceLevel)
        {
            if (targetLevel?.levelVariables == null || sourceLevel?.levelVariables == null)
            {
                Console.WriteLine("❌ Cannot swap ship exit animation: LevelVariables are missing in the source or target level.");
                return false;
            }

            Console.WriteLine("\n==== Swapping Ship Exit Animation (Using Existing Cuboids) ====");
            Console.WriteLine($"Source Level Path: {sourceLevel.path ?? "Not set"}");
            Console.WriteLine($"Target Level Path: {targetLevel.path ?? "Not set"}");

            try
            {
                // Ensure the target level has the necessary lists
                if (targetLevel.splines == null)
                    targetLevel.splines = new List<Spline>();
                if (targetLevel.cuboids == null)
                    targetLevel.cuboids = new List<Cuboid>();

                // Get the IDs from the source level's variables
                int sourceShipPathId = sourceLevel.levelVariables.shipPathID;
                int sourceCamStartId = sourceLevel.levelVariables.shipCameraStartID;
                int sourceCamEndId = sourceLevel.levelVariables.shipCameraEndID;

                Console.WriteLine($"  Source IDs: ShipPath={sourceShipPathId}, CamStart={sourceCamStartId}, CamEnd={sourceCamEndId}");
                Console.WriteLine($"  Target current IDs: ShipPath={targetLevel.levelVariables.shipPathID}, CamStart={targetLevel.levelVariables.shipCameraStartID}, CamEnd={targetLevel.levelVariables.shipCameraEndID}");

                // Check if all source IDs are valid
                bool validSourceIds = sourceShipPathId >= 0 && sourceCamStartId >= 0 && sourceCamEndId >= 0;

                if (!validSourceIds)
                {
                    Console.WriteLine("⚠️ Source level has invalid ship exit animation IDs. Keeping target settings.");
                    return true;
                }

                // Find the source objects
                Spline sourceShipPathSpline = FindOrLoadSplineById(sourceLevel, sourceShipPathId);
                Cuboid sourceCamStartCuboid = FindCuboidById(sourceLevel, sourceCamStartId);
                Cuboid sourceCamEndCuboid = FindCuboidById(sourceLevel, sourceCamEndId);

                // Check what we found
                if (sourceShipPathSpline == null)
                {
                    Console.WriteLine("⚠️ Source ship path spline not found, keeping target spline");
                }
                if (sourceCamStartCuboid == null)
                {
                    Console.WriteLine("⚠️ Source camera start cuboid not found, keeping target cuboid");
                }
                if (sourceCamEndCuboid == null)
                {
                    Console.WriteLine("⚠️ Source camera end cuboid not found, keeping target cuboid");
                }

                // Step 1: Handle ship path spline
                if (sourceShipPathSpline != null)
                {
                    int newShipPathId = CopySplineToLevel(sourceShipPathSpline, targetLevel);
                    targetLevel.levelVariables.shipPathID = newShipPathId;
                    Console.WriteLine($"✅ Updated ship path spline (new ID: {newShipPathId})");
                }

                // Step 2: Handle cuboids with proper ID conflict resolution
                if (targetLevel.cuboids.Count >= 2)
                {
                    Console.WriteLine("Using existing target cuboids and updating their properties...");

                    // Use the first two existing cuboids as camera start and end
                    Cuboid targetCamStartCuboid = targetLevel.cuboids[0];
                    Cuboid targetCamEndCuboid = targetLevel.cuboids[1];

                    // Update camera start cuboid
                    if (sourceCamStartCuboid != null)
                    {
                        Console.WriteLine($"Updating camera start cuboid (ID {targetCamStartCuboid.id}):");
                        Console.WriteLine($"  Position: {targetCamStartCuboid.position} → {sourceCamStartCuboid.position}");
                        Console.WriteLine($"  Rotation: {targetCamStartCuboid.rotation} → {sourceCamStartCuboid.rotation}");

                        targetCamStartCuboid.position = sourceCamStartCuboid.position;
                        targetCamStartCuboid.rotation = sourceCamStartCuboid.rotation;
                        targetCamStartCuboid.scale = sourceCamStartCuboid.scale;
                        
                        // 🔧 USE THE WORKING METHOD: Force coordinate conversion like new cuboids
                        targetCamStartCuboid.ApplyRC1ToRC3CameraRotationConversion();
                        
                        targetLevel.levelVariables.shipCameraStartID = targetCamStartCuboid.id;
                        Console.WriteLine($"✅ Updated camera start cuboid with coordinate conversion");
                    }

                    // Update camera end cuboid
                    if (sourceCamEndCuboid != null)
                    {
                        Console.WriteLine($"Updating camera end cuboid (ID {targetCamEndCuboid.id}):");
                        Console.WriteLine($"  Position: {targetCamEndCuboid.position} → {sourceCamEndCuboid.position}");
                        Console.WriteLine($"  Rotation: {targetCamEndCuboid.rotation} → {sourceCamEndCuboid.rotation}");

                        targetCamEndCuboid.position = sourceCamEndCuboid.position;
                        targetCamEndCuboid.rotation = sourceCamEndCuboid.rotation;
                        targetCamEndCuboid.scale = sourceCamEndCuboid.scale;
                        
                        // 🔧 USE THE WORKING METHOD: Force coordinate conversion like new cuboids
                        targetCamEndCuboid.ApplyRC1ToRC3CameraRotationConversion();
                        
                        targetLevel.levelVariables.shipCameraEndID = targetCamEndCuboid.id;
                        Console.WriteLine($"✅ Updated camera end cuboid with coordinate conversion");
                    }

                    // 🆕 ADD THE TEST HERE - Right after updating cuboids
                    Console.WriteLine("\n🧪 TESTING CUBOID SERIALIZATION AFTER UPDATES");
                    TestCuboidSerializationRoundTrip(targetLevel);
                }
                else if (targetLevel.cuboids.Count == 1)
                {
                    Console.WriteLine("Target level has only 1 cuboid. Creating one additional cuboid...");

                    // Use the existing cuboid for camera start
                    Cuboid targetCamStartCuboid = targetLevel.cuboids[0];
                    if (sourceCamStartCuboid != null)
                    {
                        targetCamStartCuboid.position = sourceCamStartCuboid.position;
                        targetCamStartCuboid.rotation = sourceCamStartCuboid.rotation;
                        targetCamStartCuboid.scale = sourceCamStartCuboid.scale;
                        targetCamStartCuboid.UpdateTransformMatrix();
                        targetLevel.levelVariables.shipCameraStartID = targetCamStartCuboid.id;
                    }

                    // Create a new cuboid for camera end, using the existing one as a template
                    if (sourceCamEndCuboid != null)
                    {
                        Cuboid templateCuboid = targetLevel.cuboids[0];
                        byte[] templateData = templateCuboid.ToByteArray();
                        Cuboid newCamEndCuboid = new Cuboid(templateData, 0);

                        // Find next available ID with proper conflict checking
                        int newId = FindNextAvailableCuboidId(targetLevel);
                        newCamEndCuboid.id = newId;

                        // Apply source camera end properties
                        newCamEndCuboid.position = sourceCamEndCuboid.position;
                        newCamEndCuboid.rotation = sourceCamEndCuboid.rotation;
                        newCamEndCuboid.scale = sourceCamEndCuboid.scale;
                        newCamEndCuboid.UpdateTransformMatrix();

                        targetLevel.cuboids.Add(newCamEndCuboid);
                        targetLevel.levelVariables.shipCameraEndID = newCamEndCuboid.id;

                        Console.WriteLine($"✅ Created camera end cuboid with ID {newId}");
                    }
                }
                else
                {
                    Console.WriteLine("Target level has no existing cuboids. Creating two new ones using RC1 data...");

                    // Get safe IDs for both cuboids
                    int camStartId = FindNextAvailableCuboidId(targetLevel);
                    int camEndId = FindNextAvailableCuboidId(targetLevel, camStartId);

                    Console.WriteLine($"Assigning safe cuboid IDs: Start={camStartId}, End={camEndId}");

                    // Create camera start cuboid
                    if (sourceCamStartCuboid != null)
                    {
                        byte[] defaultData = new byte[Cuboid.ELEMENTSIZE];
                        Cuboid newCamStartCuboid = new Cuboid(defaultData, 0);
                        newCamStartCuboid.id = camStartId;
                        newCamStartCuboid.position = sourceCamStartCuboid.position;
                        newCamStartCuboid.rotation = sourceCamStartCuboid.rotation;
                        newCamStartCuboid.scale = sourceCamStartCuboid.scale;
                        newCamStartCuboid.UpdateTransformMatrix();

                        targetLevel.cuboids.Add(newCamStartCuboid);
                        targetLevel.levelVariables.shipCameraStartID = newCamStartCuboid.id;
                        Console.WriteLine($"✅ Created camera start cuboid with safe ID {camStartId}");
                    }

                    // Create camera end cuboid
                    if (sourceCamEndCuboid != null)
                    {
                        byte[] defaultData = new byte[Cuboid.ELEMENTSIZE];
                        Cuboid newCamEndCuboid = new Cuboid(defaultData, 0);
                        newCamEndCuboid.id = camEndId;
                        newCamEndCuboid.position = sourceCamEndCuboid.position;
                        newCamEndCuboid.rotation = sourceCamEndCuboid.rotation;
                        newCamEndCuboid.scale = sourceCamEndCuboid.scale;
                        newCamEndCuboid.UpdateTransformMatrix();

                        targetLevel.cuboids.Add(newCamEndCuboid);
                        targetLevel.levelVariables.shipCameraEndID = newCamEndCuboid.id;
                        Console.WriteLine($"✅ Created camera end cuboid with safe ID {camEndId}");
                    }
                }

                Console.WriteLine($"  Final Target IDs: ShipPath={targetLevel.levelVariables.shipPathID}, " +
                          $"CamStart={targetLevel.levelVariables.shipCameraStartID}, " +
                          $"CamEnd={targetLevel.levelVariables.shipCameraEndID}");

                Console.WriteLine("✅ Ship exit animation swapped successfully using existing cuboids.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An error occurred during ship animation swap: {ex.Message}");
                Console.WriteLine($"  The ship exit animation will remain unchanged.");
                return false;
            }
        }

        /// <summary>
        /// Finds a cuboid by its ID in the level's list.
        /// </summary>
        /// <param name="level">The level containing the cuboid</param>
        /// <param name="cuboidId">The ID of the cuboid to find</param>
        /// <returns>The found cuboid, or null if it couldn't be found</returns>
        private static Cuboid FindCuboidById(Level level, int cuboidId)
        {
            // First, try to find the cuboid in the already loaded list.
            Cuboid existingCuboid = level.cuboids.FirstOrDefault(c => c.id == cuboidId);
            if (existingCuboid != null)
            {
                Console.WriteLine($"  Found cuboid with ID {cuboidId}.");
                return existingCuboid;
            }

            Console.WriteLine($"  ❌ Cuboid ID {cuboidId} not found in the level.");
            return null;
        }

        /// <summary>
        /// Finds a spline by its ID in the level's list, or manually loads it from the level file if not found.
        /// </summary>
        /// <param name="level">The level containing the spline</param>
        /// <param name="splineId">The ID of the spline to find or load</param>
        /// <returns>The found or loaded spline, or null if it couldn't be found or loaded</returns>
        private static Spline FindOrLoadSplineById(Level level, int splineId)
        {
            // First, try to find the spline in the already loaded list.
            Spline existingSpline = level.splines.FirstOrDefault(s => s.id == splineId);
            if (existingSpline != null)
            {
                Console.WriteLine($"  Found pre-loaded spline with ID {splineId}.");
                return existingSpline;
            }

            // If not found, manually search and load it from the level file.
            Console.WriteLine($"  Spline ID {splineId} not pre-loaded. Attempting manual load...");

            if (string.IsNullOrEmpty(level.path))
            {
                Console.WriteLine("  ❌ Cannot manually load spline: Level directory path is not set.");
                return null;
            }

            string gameplayFilePath = Path.Combine(level.path, "gameplay_ntsc");
            if (!File.Exists(gameplayFilePath))
            {
                Console.WriteLine($"  ❌ Cannot manually load spline: Gameplay file not found at '{gameplayFilePath}'.");
                return null;
            }

            try
            {
                using (var fs = new FileStream(gameplayFilePath, FileMode.Open, FileAccess.Read))
                {
                    // Get spline table info from level header using the new public properties
                    if (level.SplinePointer == 0 || level.SplineCount == 0)
                    {
                        Console.WriteLine($"  ❌ Cannot manually load spline: Spline table info is missing in level (Pointer: {level.SplinePointer}, Count: {level.SplineCount}).");
                        return null;
                    }

                    // Read the spline pointer table
                    byte[] splineTable = ReadBlock(fs, level.SplinePointer, level.SplineCount * 4);

                    // Search through the spline pointers to find the one matching our ID
                    for (int i = 0; i < level.SplineCount; i++)
                    {
                        int splineOffset = ReadInt(splineTable, i * 4);
                        if (splineOffset == 0) continue; // Skip null pointers

                        // Read the spline's ID to check if it matches what we're looking for
                        fs.Position = splineOffset;
                        int currentId = fs.ReadByte();
                        if (currentId == splineId)
                        {
                            Console.WriteLine($"    Found spline data for ID {splineId} at file offset 0x{splineOffset:X8}.");

                            // Go back to the start of the spline data and read the entire block
                            fs.Position = splineOffset;
                            byte[] splineData = new byte[0x1000]; // Allocate a large buffer to ensure we read the entire spline
                            int bytesRead = fs.Read(splineData, 0, splineData.Length);

                            // Create a new spline from the data
                            Spline newSpline = (Spline) Spline.CreateFromByteArray(splineData, 0);

                            // Add it to the level's list to avoid loading it again
                            level.splines.Add(newSpline);
                            Console.WriteLine($"    ✅ Successfully loaded and cached spline ID {splineId}.");
                            return newSpline;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Error during manual spline load for ID {splineId}: {ex.Message}");
                return null;
            }

            Console.WriteLine($"  ❌ Manual load failed: Could not find data for spline ID {splineId} in the file.");
            return null;
        }

        /// <summary>
        /// Clones a cuboid, adds it to the target level with a specific ID, and returns the ID.
        /// </summary>
        /// <param name="sourceCuboid">The cuboid to copy</param>
        /// <param name="targetLevel">The level to add the cuboid to</param>
        /// <returns>The ID of the new cuboid in the target level</returns>
        private static int CopyCuboidToLevel(Cuboid sourceCuboid, Level targetLevel)
        {
            if (sourceCuboid == null) return -1;

            // Find the first available cuboid ID to keep the ID low.
            int newId = 0;
            var existingIds = new HashSet<int>(targetLevel.cuboids.Select(c => c.id));
            while (existingIds.Contains(newId))
            {
                newId++;
            }

            try
            {
                // To ensure a perfect, bit-for-bit copy of the matrix data,
                // we serialize the source and deserialize it into the new object.
                byte[] cuboidData = sourceCuboid.ToByteArray();
                Cuboid newCuboid = new Cuboid(cuboidData, 0); // Initially created with a dummy ID

                // Manually assign the new unique ID. The other properties (matrices) are
                // already perfectly copied by the constructor.
                newCuboid.id = newId;

                // Add the fully constructed cuboid to the target level.
                targetLevel.cuboids.Add(newCuboid);

                Console.WriteLine($"  Copied cuboid with original ID {sourceCuboid.id} to new ID {newCuboid.id}.");
                return newCuboid.id;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An error occurred during cuboid copy: {ex.Message}");
                // If this still fails, the issue is deeper than construction.
                // Re-throwing allows the top-level try-catch to handle it.
                throw;
            }
        }

        /// <summary>
        /// Clones a spline, adds it to the target level with a new unique ID, and returns the new ID.
        /// </summary>
        /// <param name="sourceSpline">The spline to copy</param>
        /// <param name="targetLevel">The level to add the spline to</param>
        /// <returns>The ID of the new spline in the target level</returns>
        private static int CopySplineToLevel(Spline sourceSpline, Level targetLevel)
        {
            if (sourceSpline == null) return -1;

            // Important: Use a high ID space for ship path splines to avoid conflicts
            // Similar to what we do in grind path swapper
            int newId = 100; // Start high to avoid conflicts with other splines
            var existingIds = new HashSet<int>(targetLevel.splines.Select(s => s.id));
            
            // Find the first available ID starting from 100
            while (existingIds.Contains(newId))
            {
                newId++;
            }

            // Clone the spline and assign the new unique ID
            Spline newSpline = CloneSpline(sourceSpline, newId);

            // Add the new spline to the target level
            targetLevel.splines.Add(newSpline);

            Console.WriteLine($"  Copied spline with original ID {sourceSpline.id} to new ID {newSpline.id}.");
            Console.WriteLine($"  Spline vertex count: {newSpline.GetVertexCount()}, W-values: {newSpline.wVals.Length}");

            // Validate the spline was added correctly
            var verifySpline = targetLevel.splines.FirstOrDefault(s => s.id == newSpline.id);
            if (verifySpline != null)
            {
                Console.WriteLine($"  ✅ Spline ID {newSpline.id} successfully added and verified in target level");
            }
            else
            {
                Console.WriteLine($"  ❌ Warning: Spline ID {newSpline.id} was not found after adding to target level");
            }

            return newSpline.id;
        }

        /// <summary>
        /// Creates a complete clone of a spline with a new ID.
        /// </summary>
        /// <param name="sourceSpline">The spline to clone</param>
        /// <param name="newId">The ID to assign to the new spline</param>
        /// <returns>A new spline instance that is a copy of the source spline</returns>
        private static Spline CloneSpline(Spline sourceSpline, int newId)
        {
            // Create a new vertex buffer with the same data
            float[] newVertexBuffer = new float[sourceSpline.vertexBuffer.Length];
            Array.Copy(sourceSpline.vertexBuffer, newVertexBuffer, sourceSpline.vertexBuffer.Length);

            // Create new w values array, ensuring it matches the vertex count
            int vertexCount = sourceSpline.GetVertexCount();
            float[] newWVals = new float[vertexCount];

            // Handle the case where wVals might be a different length than the vertex count
            if (sourceSpline.wVals.Length == vertexCount)
            {
                Array.Copy(sourceSpline.wVals, newWVals, sourceSpline.wVals.Length);
            }
            else if (sourceSpline.wVals.Length < vertexCount)
            {
                // Copy what we have and extrapolate the rest
                Array.Copy(sourceSpline.wVals, newWVals, sourceSpline.wVals.Length);
                for (int i = sourceSpline.wVals.Length; i < vertexCount; i++)
                {
                    newWVals[i] = i > 0 ? newWVals[i - 1] + 0.1f : 0f;
                }
            }
            else
            {
                // Just copy what we need
                Array.Copy(sourceSpline.wVals, newWVals, vertexCount);
            }

            // Create a new spline with the copied data
            var newSpline = new Spline(newId, newVertexBuffer)
            {
                wVals = newWVals,
                position = sourceSpline.position,
                rotation = sourceSpline.rotation,
                scale = sourceSpline.scale,
                reflection = sourceSpline.reflection
            };

            newSpline.UpdateTransformMatrix();
            return newSpline;
        }

        /// <summary>
        /// Add these new helper methods to create default objects when needed
        /// </summary>
        private static Spline CreateDefaultSpline()
        {
            // Create a simple default spline with just two points
            float[] vertexBuffer = new float[] {
                0, 0, 30,      // Start point: 30 units up
                0, 30, 50      // End point: 30 units forward, 50 units up
            };
            
            // Use a temporary ID - CopySplineToLevel will assign the proper new ID
            Spline spline = new Spline(-1, vertexBuffer);  // Use -1 as temporary ID
            spline.wVals = new float[] { 0.0f, 1.0f };
            
            Console.WriteLine("  Created default ship path spline with 2 points");
            return spline;
        }

        /// <summary>
        /// Enhanced cuboid copy method that uses RC2 reference cuboids as templates
        /// </summary>
        private static int CopyCuboidToLevelWithRC2Template(Cuboid sourceCuboid, Level targetLevel, Level rc2ReferenceLevel)
        {
            if (sourceCuboid == null) return -1;

            // Find the first available cuboid ID to keep the ID low.
            int newId = 0;
            var existingIds = new HashSet<int>(targetLevel.cuboids.Select(c => c.id));
            while (existingIds.Contains(newId))
            {
                newId++;
            }

            try
            {
                Cuboid newCuboid;

                // If we have an RC2 reference level, use one of its cuboids as a template
                if (rc2ReferenceLevel?.cuboids != null && rc2ReferenceLevel.cuboids.Count > 0)
                {
                    Console.WriteLine($"  Using RC2 reference cuboid as template for improved compatibility");
                    
                    // Use the first available cuboid from the RC2 reference level as a template
                    Cuboid rc2Template = rc2ReferenceLevel.cuboids.FirstOrDefault();
                    if (rc2Template != null)
                    {
                        // Clone the RC2 template to get proper RC2 format
                        byte[] templateData = rc2Template.ToByteArray();
                        newCuboid = new Cuboid(templateData, 0);
                        
                        // Now apply the RC1 cuboid's position and rotation to the RC2 template
                        newCuboid.position = sourceCuboid.position;
                        newCuboid.rotation = sourceCuboid.rotation;
                        newCuboid.scale = sourceCuboid.scale; // Also copy scale for completeness
                        
                        // Force update the matrix with the new values
                        newCuboid.UpdateTransformMatrix();
                        
                        Console.WriteLine($"  Enhanced cuboid: RC2 template + RC1 values");
                        Console.WriteLine($"    Position: ({newCuboid.position.X:F3}, {newCuboid.position.Y:F3}, {newCuboid.position.Z:F3})");
                        Console.WriteLine($"    Rotation: ({newCuboid.rotation.X:F3}, {newCuboid.rotation.Y:F3}, {newCuboid.rotation.Z:F3}, {newCuboid.rotation.W:F3})");
                    }
                    else
                    {
                        // Fallback to traditional cloning if no template found
                        byte[] cuboidData = sourceCuboid.ToByteArray();
                        newCuboid = new Cuboid(cuboidData, 0);
                        Console.WriteLine($"  Fallback: No RC2 template available, using traditional cloning");
                    }
                }
                else
                {
                    // Traditional approach when no RC2 reference is available
                    byte[] cuboidData = sourceCuboid.ToByteArray();
                    newCuboid = new Cuboid(cuboidData, 0);
                    Console.WriteLine($"  Traditional: RC2 reference level not provided, using direct cloning");
                }

                // Assign the new unique ID and add to target level
                newCuboid.id = newId;
                targetLevel.cuboids.Add(newCuboid);

                Console.WriteLine($"  Copied cuboid with original ID {sourceCuboid.id} to new ID {newCuboid.id}.");
                return newCuboid.id;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An error occurred during enhanced cuboid copy: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Creates a default cuboid using RC2 template if available
        /// </summary>
        private static Cuboid CreateDefaultCuboidFromRC2Template(int id, Level rc2ReferenceLevel)
        {
            Cuboid cuboid;
            
            // Try to use RC2 template first
            if (rc2ReferenceLevel?.cuboids != null && rc2ReferenceLevel.cuboids.Count > 0)
            {
                Console.WriteLine($"  Creating default cuboid {id} using RC2 template");
                
                Cuboid template = rc2ReferenceLevel.cuboids.FirstOrDefault();
                byte[] templateData = template.ToByteArray();
                cuboid = new Cuboid(templateData, 0);
                
                // Set reasonable default values for camera positions
                if (id == 0) // Start camera
                {
                    cuboid.position = new Vector3(0, 0, 30);
                    cuboid.rotation = Quaternion.Identity;
                    cuboid.scale = new Vector3(1, 1, 1);
                }
                else // End camera
                {
                    cuboid.position = new Vector3(0, 30, 50);
                    cuboid.rotation = Quaternion.Identity; // No special rotation
                    cuboid.scale = new Vector3(1, 1, 1);
                }
                
                cuboid.UpdateTransformMatrix();
            }
            else
            {
                // Fallback to original method
                Console.WriteLine($"  Creating default cuboid {id} using fallback method");
                cuboid = CreateDefaultCuboid(id);
            }
            
            cuboid.id = id;
            Console.WriteLine($"  Created enhanced default camera cuboid with ID {id}");
            return cuboid;
        }

        /// <summary>
        /// Creates a default cuboid using basic fallback method
        /// </summary>
        private static Cuboid CreateDefaultCuboid(int id)
        {
            // Create a default byte array for a cuboid
            byte[] defaultData = new byte[Cuboid.ELEMENTSIZE]; // Use the constant size
    
            // Fill with zeros
            for (int i = 0; i < defaultData.Length; i++)
            {
                defaultData[i] = 0;
            }
    
            Cuboid cuboid = new Cuboid(defaultData, 0);
            cuboid.id = id;
    
            // Set reasonable default values for camera positions
            if (id == 0) // Start camera
            {
                cuboid.position = new Vector3(0, 0, 30);
                cuboid.rotation = Quaternion.Identity;
                cuboid.scale = new Vector3(1, 1, 1);
            }
            else // End camera
            {
                cuboid.position = new Vector3(0, 30, 50);
                cuboid.rotation = Quaternion.Identity; // No special rotation
                cuboid.scale = new Vector3(1, 1, 1);
            }
            
            cuboid.UpdateTransformMatrix();
    
            Console.WriteLine($"  Created fallback default camera cuboid with ID {id}");
            return cuboid;
        }

        /// <summary>
        /// Finds the next available cuboid ID that doesn't conflict with existing cuboids
        /// </summary>
        /// <param name="level">The level to check for existing cuboid IDs</param>
        /// <param name="excludeId">Optional ID to exclude from consideration (for when assigning multiple IDs)</param>
        /// <returns>The next available cuboid ID</returns>
        private static int FindNextAvailableCuboidId(Level level, int excludeId = -1)
        {
            if (level.cuboids == null || level.cuboids.Count == 0)
            {
                // No existing cuboids, start from 0 unless excluded
                return (excludeId == 0) ? 1 : 0;
            }

            // Get all existing IDs and add the exclude ID if specified
            var existingIds = new HashSet<int>(level.cuboids.Select(c => c.id));
            if (excludeId >= 0)
            {
                existingIds.Add(excludeId);
            }

            // Find the first available ID starting from 0
            int candidateId = 0;
            while (existingIds.Contains(candidateId))
            {
                candidateId++;
            }

            Console.WriteLine($"  Found next available cuboid ID: {candidateId}");
            return candidateId;
        }

        /// <summary>
        /// Investigates the save serialization issue by comparing original vs re-saved cuboids
        /// </summary>
        /// <param name="originalLevelPath">Path to original level</param>
        /// <param name="testOutputPath">Path to save the test level</param>
        public static void InvestigateSaveSerializationIssue(string originalLevelPath, string testOutputPath)
        {
            Console.WriteLine("\n==== Investigating Save Serialization Issue ====");
            
            if (!File.Exists(originalLevelPath))
            {
                Console.WriteLine($"❌ Original level not found: {originalLevelPath}");
                return;
            }
            
            try
            {
                // Load the original level
                Console.WriteLine($"Loading original level: {originalLevelPath}");
                var originalLevel = new Level(originalLevelPath);
                
                // Analyze original level
                Console.WriteLine("\n=== ORIGINAL LEVEL ANALYSIS ===");
                AnalyzeShipCameraData(originalLevel, "Original");
                
                // Capture original cuboid data
                var originalCuboidData = CaptureCuboidData(originalLevel);
                
                // Save the level (which should theoretically not change anything)
                Console.WriteLine($"\nSaving level to: {testOutputPath}");
                Directory.CreateDirectory(Path.GetDirectoryName(testOutputPath));
                originalLevel.Save(testOutputPath);
                
                // Load the re-saved level
                Console.WriteLine($"Reloading saved level: {testOutputPath}");
                var resavedLevel = new Level(testOutputPath);
                
                // Analyze re-saved level
                Console.WriteLine("\n=== RE-SAVED LEVEL ANALYSIS ===");
                AnalyzeShipCameraData(resavedLevel, "Re-saved");
                
                // Capture re-saved cuboid data
                var resavedCuboidData = CaptureCuboidData(resavedLevel);

                // Compare the data
                Console.WriteLine("\n=== COMPARISON ANALYSIS ===");
                CompareCuboidData(originalCuboidData, resavedCuboidData);

                // Raw byte comparison of specific cuboids
                CompareShipCameraCuboidBytes(originalLevel, resavedLevel); // ✅ Use correct variable names

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during investigation: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Captures detailed cuboid data for comparison
        /// </summary>
        private static List<CuboidSnapshot> CaptureCuboidData(Level level)
        {
            var snapshots = new List<CuboidSnapshot>();
            
            if (level.cuboids == null) return snapshots;
            
            foreach (var cuboid in level.cuboids)
            {
                snapshots.Add(new CuboidSnapshot
                {
                    Id = cuboid.id,
                    Position = cuboid.position,
                    Rotation = cuboid.rotation,
                    Scale = cuboid.scale,
                    ModelMatrix = cuboid.modelMatrix,
                    RawBytes = cuboid.ToByteArray()
                });
            }
            
            return snapshots;
        }

        /// <summary>
        /// Compares cuboid data between original and re-saved
        /// </summary>
        private static void CompareCuboidData(List<CuboidSnapshot> original, List<CuboidSnapshot> resaved)
        {
            Console.WriteLine($"Original cuboids: {original.Count}, Re-saved cuboids: {resaved.Count}");
            
            if (original.Count != resaved.Count)
            {
                Console.WriteLine("⚠️ Cuboid count mismatch!");
                return;
            }
            
            for (int i = 0; i < original.Count; i++)
            {
                var orig = original[i];
                var saved = resaved[i];
                
                if (orig.Id != saved.Id)
                {
                    Console.WriteLine($"⚠️ Cuboid {i}: ID mismatch ({orig.Id} vs {saved.Id})");
                    continue;
                }
                
                bool positionChanged = !VectorsNearlyEqual(orig.Position, saved.Position, 0.001f);
                bool rotationChanged = !QuaternionsNearlyEqual(orig.Rotation, saved.Rotation, 0.001f);
                bool scaleChanged = !VectorsNearlyEqual(orig.Scale, saved.Scale, 0.001f);
                bool bytesChanged = !orig.RawBytes.SequenceEqual(saved.RawBytes);
                
                if (positionChanged || rotationChanged || scaleChanged || bytesChanged)
                {
                    Console.WriteLine($"\n🔍 Cuboid {orig.Id} CHANGED:");
                    
                    if (positionChanged)
                        Console.WriteLine($"  Position: {orig.Position} → {saved.Position}");
                    
                    if (rotationChanged)
                        Console.WriteLine($"  Rotation: {orig.Rotation} → {saved.Rotation}");
                        
                    if (scaleChanged)
                        Console.WriteLine($"  Scale: {orig.Scale} → {saved.Scale}");
                    
                    if (bytesChanged)
                    {
                        Console.WriteLine($"  Raw bytes changed! Length: {orig.RawBytes.Length} → {saved.RawBytes.Length}");
                        
                        // Find first differing byte
                        int maxLen = Math.Min(orig.RawBytes.Length, saved.RawBytes.Length);
                        for (int b = 0; b < maxLen; b++)
                        {
                            if (orig.RawBytes[b] != saved.RawBytes[b])
                            {
                                Console.WriteLine($"  First difference at byte {b}: 0x{orig.RawBytes[b]:X2} → 0x{saved.RawBytes[b]:X2}");
                                break;
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"✅ Cuboid {orig.Id}: No changes detected");
                }
            }
        }

        /// <summary>
        /// Helper method to compare Vector3 values with tolerance
        /// </summary>
        private static bool VectorsNearlyEqual(Vector3 a, Vector3 b, float tolerance)
        {
            return Math.Abs(a.X - b.X) <= tolerance &&
                   Math.Abs(a.Y - b.Y) <= tolerance &&
                   Math.Abs(a.Z - b.Z) <= tolerance;
        }

        /// <summary>
        /// Helper method to compare Quaternion values with tolerance
        /// </summary>
        private static bool QuaternionsNearlyEqual(Quaternion a, Quaternion b, float tolerance)
        {
            return Math.Abs(a.X - b.X) <= tolerance &&
                   Math.Abs(a.Y - b.Y) <= tolerance &&
                   Math.Abs(a.Z - b.Z) <= tolerance &&
                   Math.Abs(a.W - b.W) <= tolerance;
        }

        /// <summary>
        /// Specifically compares the ship camera cuboids' raw bytes
        /// </summary>
        private static void CompareShipCameraCuboidBytes(Level original, Level resaved)
        {
            if (original.levelVariables == null || resaved.levelVariables == null) return;
            
            Console.WriteLine("\n=== SHIP CAMERA CUBOID BYTE COMPARISON ===");
            
            var originalCameraIds = new[] { original.levelVariables.shipCameraStartID, original.levelVariables.shipCameraEndID };
            var resavedCameraIds = new[] { resaved.levelVariables.shipCameraStartID, resaved.levelVariables.shipCameraEndID };
            
            for (int i = 0; i < 2; i++)
            {
                string cameraName = i == 0 ? "Start" : "End";
                int origId = originalCameraIds[i];
                int savedId = resavedCameraIds[i];
                
                if (origId != savedId)
                {
                    Console.WriteLine($"⚠️ {cameraName} Camera ID changed: {origId} → {savedId}");
                    continue;
                }
                
                var origCuboid = original.cuboids?.FirstOrDefault(c => c.id == origId);
                var savedCuboid = resaved.cuboids?.FirstOrDefault(c => c.id == savedId);
                
                if (origCuboid == null || savedCuboid == null)
                {
                    Console.WriteLine($"⚠️ {cameraName} Camera cuboid not found");
                    continue;
                }
                
                var origBytes = origCuboid.ToByteArray();
                var savedBytes = savedCuboid.ToByteArray();
                
                Console.WriteLine($"\n{cameraName} Camera Cuboid (ID {origId}):");
                Console.WriteLine($"  Original bytes length: {origBytes.Length}");
                Console.WriteLine($"  Re-saved bytes length: {savedBytes.Length}");
                
                if (origBytes.SequenceEqual(savedBytes))
                {
                    Console.WriteLine($"  ✅ Raw bytes are identical");
                }
                else
                {
                    Console.WriteLine($"  ❌ Raw bytes differ!");
                    
                    // Show hex dump of differences
                    Console.WriteLine($"  Hex comparison (first 128 bytes):");
                    int maxBytes = Math.Min(128, Math.Max(origBytes.Length, savedBytes.Length));
                    
                    for (int b = 0; b < maxBytes; b += 16)
                    {
                        Console.Write($"  {b:X4}: ");
                        
                        // Original bytes
                        for (int j = 0; j < 16 && b + j < origBytes.Length; j++)
                        {
                            if (b + j < origBytes.Length)
                                Console.Write($"{origBytes[b + j]:X2} ");
                            else
                                Console.Write("   ");
                        }
                        
                        Console.Write(" | ");
                        
                        // Re-saved bytes
                        for (int j = 0; j < 16 && b + j < savedBytes.Length; j++)
                        {
                            if (b + j < savedBytes.Length)
                            {
                                if (b + j < origBytes.Length && origBytes[b + j] != savedBytes[b + j])
                                    Console.Write($"[{savedBytes[b + j]:X2}]");
                                else
                                    Console.Write($"{savedBytes[b + j]:X2} ");
                            }
                            else
                                Console.Write("   ");
                        }
                        
                        Console.WriteLine();
                    }
                }
            }
        }

        /// <summary>
        /// Helper class to capture cuboid state
        /// </summary>
        private class CuboidSnapshot
        {
            public int Id { get; set; }
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; }
            public Vector3 Scale { get; set; }
            public Matrix4 ModelMatrix { get; set; }
            public byte[] RawBytes { get; set; }
        }

        /// <summary>
        /// Diagnostic method to analyze existing ship exit animation setup
        /// </summary>
        /// <param name="level">Level to analyze</param>
        /// <param name="levelName">Name for logging</param>
        public static void AnalyzeShipCameraData(Level level, string levelName)
        {
            Console.WriteLine($"\n=== Ship Camera Analysis for {levelName} ===");

            if (level.levelVariables == null)
            {
                Console.WriteLine("❌ No level variables found");
                return;
            }

            Console.WriteLine($"Ship Path ID: {level.levelVariables.shipPathID}");
            Console.WriteLine($"Ship Camera Start ID: {level.levelVariables.shipCameraStartID}");
            Console.WriteLine($"Ship Camera End ID: {level.levelVariables.shipCameraEndID}");

            // Analyze splines
            if (level.splines != null)
            {
                Console.WriteLine($"\nSplines in level: {level.splines.Count}");
                var shipSpline = level.splines.FirstOrDefault(s => s.id == level.levelVariables.shipPathID);
                if (shipSpline != null)
                {
                    Console.WriteLine($"✅ Ship path spline found:");
                    Console.WriteLine($"  Vertex count: {shipSpline.GetVertexCount()}");
                    Console.WriteLine($"  W-values: {shipSpline.wVals.Length}");
                    Console.WriteLine($"  Position: {shipSpline.position}");
                    Console.WriteLine($"  Rotation: {shipSpline.rotation}");
                }
                else
                {
                    Console.WriteLine($"❌ Ship path spline (ID {level.levelVariables.shipPathID}) not found in splines list");
                    Console.WriteLine($"Available spline IDs: {string.Join(", ", level.splines.Select(s => s.id).OrderBy(id => id))}");
                }
            }

            // Analyze cuboids
            if (level.cuboids != null)
            {
                Console.WriteLine($"\nCuboids in level: {level.cuboids.Count}");
                
                var startCuboid = level.cuboids.FirstOrDefault(c => c.id == level.levelVariables.shipCameraStartID);
                if (startCuboid != null)
                {
                    Console.WriteLine($"✅ Ship camera start cuboid found (ID {startCuboid.id}):");
                    Console.WriteLine($"  Position: {startCuboid.position}");
                    Console.WriteLine($"  Rotation: {startCuboid.rotation}");
                    Console.WriteLine($"  Scale: {startCuboid.scale}");
                    
                    // Check the transform matrix
                    var matrix = startCuboid.modelMatrix;
                    Console.WriteLine($"  Transform Matrix:");
                    Console.WriteLine($"    [{matrix.M11:F3}, {matrix.M12:F3}, {matrix.M13:F3}, {matrix.M14:F3}]");
                    Console.WriteLine($"    [{matrix.M21:F3}, {matrix.M22:F3}, {matrix.M23:F3}, {matrix.M24:F3}]");
                    Console.WriteLine($"    [{matrix.M31:F3}, {matrix.M32:F3}, {matrix.M33:F3}, {matrix.M34:F3}]");
                    Console.WriteLine($"    [{matrix.M41:F3}, {matrix.M42:F3}, {matrix.M43:F3}, {matrix.M44:F3}]");
                }
                else
                {
                    Console.WriteLine($"❌ Ship camera start cuboid (ID {level.levelVariables.shipCameraStartID}) not found");
                }

                var endCuboid = level.cuboids.FirstOrDefault(c => c.id == level.levelVariables.shipCameraEndID);
                if (endCuboid != null)
                {
                    Console.WriteLine($"✅ Ship camera end cuboid found (ID {endCuboid.id}):");
                    Console.WriteLine($"  Position: {endCuboid.position}");
                    Console.WriteLine($"  Rotation: {endCuboid.rotation}");
                    Console.WriteLine($"  Scale: {endCuboid.scale}");
                }
                else
                {
                    Console.WriteLine($"❌ Ship camera end cuboid (ID {level.levelVariables.shipCameraEndID}) not found");
                }

                if (level.cuboids.Count > 0)
                {
                    Console.WriteLine($"\nAll cuboid IDs: {string.Join(", ", level.cuboids.Select(c => c.id).OrderBy(id => id))}");
                }
            }
        }

        /// <summary>
        /// Comprehensive test of the entire save/load cycle for cuboid rotation data
        /// </summary>
        public static void ComprehensiveRotationTest(string outputPath)
        {
            Console.WriteLine("\n🧪 COMPREHENSIVE ROTATION TEST");
            
            string enginePath = Path.Combine(outputPath, "engine.ps3");
            if (!File.Exists(enginePath)) return;
            
            // Test 1: Matrix storage formats
            var level = new Level(enginePath);
            TestMatrixStorageFormats(level, "After Load");
            
            // Test 2: Cuboid ID validation
            ValidateShipCameraCuboidIds(level);
            
            // Test 3: RC3/UYA format validation
            ValidateRC3CuboidFormat(level);
            
            // Test 4: Direct byte manipulation test
            TestDirectByteManipulation(level);
        }

        /// <summary>
        /// Test direct byte manipulation to see if the issue is in the Matrix4 class
        /// </summary>
        private static void TestDirectByteManipulation(Level level)
        {
            Console.WriteLine("\n🔬 DIRECT BYTE MANIPULATION TEST");
            
            if (level.cuboids == null || level.cuboids.Count == 0) return;
            
            var cuboid = level.cuboids.First();
            var originalBytes = cuboid.ToByteArray();
            
            // Create a test rotation (45 degrees around Y axis)
            var testRotation = Quaternion.FromEulerAngles(0, MathF.PI / 4, 0);
            var testMatrix = Matrix4.CreateFromQuaternion(testRotation);
            
            // Write the test matrix directly to bytes
            var testBytes = new byte[originalBytes.Length];
            Array.Copy(originalBytes, testBytes, originalBytes.Length);
            
            // Write test matrix at offset 0x00 (transform matrix)
            WriteMatrix4ToBytes(testBytes, 0x00, testMatrix);
            
            // Write inverse at offset 0x40
            WriteMatrix4ToBytes(testBytes, 0x40, testMatrix.Inverted());
            
            // Create new cuboid from test bytes
            var testCuboid = new Cuboid(testBytes, 0);
            
            Console.WriteLine($"Original rotation: {cuboid.rotation}");
            Console.WriteLine($"Test rotation set: {testRotation}");
            Console.WriteLine($"Test cuboid rotation: {testCuboid.rotation}");
            
            var rotationDiff = (testRotation - testCuboid.rotation).Length;
            Console.WriteLine($"Rotation difference: {rotationDiff}");
            
            if (rotationDiff > 0.01f)
            {
                Console.WriteLine("❌ ROTATION DATA LOST IN BYTE CONVERSION!");
            }
            else
            {
                Console.WriteLine("✅ Rotation data preserved in byte conversion");
            }
        }

        /// <summary>
        /// Helper to write Matrix4 directly to byte array
        /// </summary>
        private static void WriteMatrix4ToBytes(byte[] bytes, int offset, Matrix4 matrix)
        {
            // Write matrix using proper float-to-byte conversion (little-endian)
            WriteFloat(bytes, offset + 0, matrix.M11);
            WriteFloat(bytes, offset + 4, matrix.M12);
            WriteFloat(bytes, offset + 8, matrix.M13);
            WriteFloat(bytes, offset + 12, matrix.M14);
            
            WriteFloat(bytes, offset + 16, matrix.M21);
            WriteFloat(bytes, offset + 20, matrix.M22);
            WriteFloat(bytes, offset + 24, matrix.M23);
            WriteFloat(bytes, offset + 28, matrix.M24);
            
            WriteFloat(bytes, offset + 32, matrix.M31);
            WriteFloat(bytes, offset + 36, matrix.M32);
            WriteFloat(bytes, offset + 40, matrix.M33);
            WriteFloat(bytes, offset + 44, matrix.M34);
            
            WriteFloat(bytes, offset + 48, matrix.M41);
            WriteFloat(bytes, offset + 52, matrix.M42);
            WriteFloat(bytes, offset + 56, matrix.M43);
            WriteFloat(bytes, offset + 60, matrix.M44);
        }

        /// <summary>
        /// Test the matrix storage and interpretation differences between game versions
        /// </summary>
        public static void TestMatrixStorageFormats(Level level, string context)
        {
            Console.WriteLine($"\n🔍 TESTING MATRIX STORAGE FORMATS - {context}");
            
            if (level.cuboids == null || level.cuboids.Count == 0)
            {
                Console.WriteLine("No cuboids to test");
                return;
            }
            
            var cuboid = level.cuboids.First();
            Console.WriteLine($"Testing cuboid ID {cuboid.id}:");
            
            // Test 1: Raw byte examination
            var rawBytes = cuboid.ToByteArray();
            Console.WriteLine($"Raw cuboid bytes (first 64 bytes - transform matrix):");
            for (int i = 0; i < Math.Min(64, rawBytes.Length); i += 16)
            {
                Console.Write($"  {i:X4}: ");
                for (int j = 0; j < 16 && i + j < rawBytes.Length; j++)
                {
                    Console.Write($"{rawBytes[i + j]:X2} ");
                }
                Console.WriteLine();
            }
            
            // Test 2: Matrix decomposition
            Console.WriteLine($"\nMatrix decomposition:");
            Console.WriteLine($"  Position: {cuboid.position}");
            Console.WriteLine($"  Rotation: {cuboid.rotation}");
            Console.WriteLine($"  Scale: {cuboid.scale}");
            
            // Test 3: Manual matrix reconstruction
            var manualMatrix = Matrix4.CreateScale(cuboid.scale) * 
                              Matrix4.CreateFromQuaternion(cuboid.rotation) * 
                              Matrix4.CreateTranslation(cuboid.position);
            
            Console.WriteLine($"\nManual matrix reconstruction:");
            Console.WriteLine($"  Original: {cuboid.modelMatrix.M11:F3}, {cuboid.modelMatrix.M12:F3}, {cuboid.modelMatrix.M13:F3}, {cuboid.modelMatrix.M14:F3}");
            Console.WriteLine($"  Manual:   {manualMatrix.M11:F3}, {manualMatrix.M12:F3}, {manualMatrix.M13:F3}, {manualMatrix.M14:F3}");
            
            var matrixDiff = (cuboid.modelMatrix - manualMatrix).Row0.Length;
            Console.WriteLine($"  Matrix difference magnitude: {matrixDiff}");
            
            // Test 4: Game-specific checks
            Console.WriteLine($"\nGame version: {level.game?.Name ?? "Unknown"}"); // ✅ Change 'name' to 'Name'
            Console.WriteLine($"Level variables ByteSize: 0x{level.levelVariables?.ByteSize:X}");
        }

        /// <summary>
        /// Validates that ship camera cuboid IDs don't conflict with game-specific reserved ranges
        /// </summary>
        private static void ValidateShipCameraCuboidIds(Level level)
        {
            Console.WriteLine("\n🔍 VALIDATING SHIP CAMERA CUBOID IDS");
            
            if (level.levelVariables == null) return;
            
            int startId = level.levelVariables.shipCameraStartID;
            int endId = level.levelVariables.shipCameraEndID;
            
            Console.WriteLine($"Ship camera IDs: Start={startId}, End={endId}");
            
            // RC3/UYA might have reserved ID ranges for specific purposes
            var reservedRanges = new[]
            {
                (0, 10, "System Reserved"),
                (100, 110, "Animation System"),
                (200, 210, "Cutscene System"),
                (1000, 1100, "Mission System")
            };
            
            foreach (var (min, max, purpose) in reservedRanges)
            {
                if ((startId >= min && startId <= max) || (endId >= min && endId <= max))
                {
                    Console.WriteLine($"⚠️ WARNING: Ship camera IDs conflict with {purpose} range ({min}-{max})");
                    Console.WriteLine($"  Consider using IDs outside reserved ranges");
                }
            }
            
            // Check if IDs are too high for RC3/UYA
            if (startId > 500 || endId > 500)
            {
                Console.WriteLine($"⚠️ WARNING: Ship camera IDs are very high ({startId}, {endId})");
                Console.WriteLine($"  RC3/UYA might have limits on cuboid ID ranges");
            }
        }

        /// <summary>
        /// Validates RC3/UYA specific cuboid format requirements
        /// </summary>
        public static void ValidateRC3CuboidFormat(Level level)
        {
            Console.WriteLine("\n🔍 VALIDATING RC3/UYA CUBOID FORMAT");
            
            if (level.cuboids == null) return;
            
            foreach (var cuboid in level.cuboids)
            {
                // Check if matrix determinant is reasonable
                var det = cuboid.modelMatrix.Determinant;
                if (Math.Abs(det) < 0.001f || Math.Abs(det) > 1000f)
                {
                    Console.WriteLine($"⚠️ Cuboid {cuboid.id}: suspicious matrix determinant {det}");
                }
                
                // Check if rotation quaternion is normalized
                var rotLength = Math.Sqrt(cuboid.rotation.X * cuboid.rotation.X + 
                                     cuboid.rotation.Y * cuboid.rotation.Y + 
                                     cuboid.rotation.Z * cuboid.rotation.Z + 
                                     cuboid.rotation.W * cuboid.rotation.W);
                if (Math.Abs(rotLength - 1.0f) > 0.01f)
                {
                    Console.WriteLine($"⚠️ Cuboid {cuboid.id}: quaternion not normalized (length={rotLength})");
                }
                
                // Check scale values
                if (cuboid.scale.X <= 0 || cuboid.scale.Y <= 0 || cuboid.scale.Z <= 0)
                {
                    Console.WriteLine($"⚠️ Cuboid {cuboid.id}: invalid scale {cuboid.scale}");
                }
            }
        }

        /// <summary>
        /// Verifies ship camera cuboid rotations after save/reload cycle
        /// </summary>
        public static void VerifyShipCameraRotationsAfterSave(string outputPath)
        {
            Console.WriteLine("\n🔍 VERIFYING SHIP CAMERA ROTATIONS AFTER SAVE/RELOAD");
            
            string enginePath = Path.Combine(outputPath, "engine.ps3");
            if (!File.Exists(enginePath))
            {
                Console.WriteLine($"❌ Engine file not found: {enginePath}");
                return;
            }
            
            try
            {
                // Load the saved level
                Console.WriteLine($"Loading saved level from: {enginePath}");
                var savedLevel = new Level(enginePath);
                
                if (savedLevel.levelVariables == null)
                {
                    Console.WriteLine("❌ No level variables found in saved level");
                    return;
                }
                
                int startId = savedLevel.levelVariables.shipCameraStartID;
                int endId = savedLevel.levelVariables.shipCameraEndID;
                
                Console.WriteLine($"Ship camera cuboid IDs in saved level: Start={startId}, End={endId}");
                
                if (savedLevel.cuboids == null)
                {
                    Console.WriteLine("❌ No cuboids found in saved level");
                    return;
                }
                
                var startCuboid = savedLevel.cuboids.FirstOrDefault(c => c.id == startId);
                var endCuboid = savedLevel.cuboids.FirstOrDefault(c => c.id == endId);
                
                if (startCuboid != null)
                {
                    Console.WriteLine($"🔍 SAVED Camera Start Cuboid (ID {startId}):");
                    Console.WriteLine($"    Position: {startCuboid.position}");
                    Console.WriteLine($"    Rotation: {startCuboid.rotation}");
                    Console.WriteLine($"    Scale: {startCuboid.scale}");
                    LogRotationAsEuler("SAVED Start", startCuboid.rotation);
                    
                    // Check if rotation is identity (which would indicate the problem)
                    if (IsIdentityRotation(startCuboid.rotation))
                    {
                        Console.WriteLine("❌ WARNING: Start cuboid rotation is identity - rotation data was lost!");
                    }
                    else
                    {
                        Console.WriteLine("✅ Start cuboid has non-identity rotation");
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Start cuboid (ID {startId}) not found in saved level");
                }
                
                if (endCuboid != null)
                {
                    Console.WriteLine($"🔍 SAVED Camera End Cuboid (ID {endId}):");
                    Console.WriteLine($"    Position: {endCuboid.position}");
                    Console.WriteLine($"    Rotation: {endCuboid.rotation}");
                    Console.WriteLine($"    Scale: {endCuboid.scale}");
                    LogRotationAsEuler("SAVED End", endCuboid.rotation);
                    
                    // Check if rotation is identity (which would indicate the problem)
                    if (IsIdentityRotation(endCuboid.rotation))
                    {
                        Console.WriteLine("❌ WARNING: End cuboid rotation is identity - rotation data was lost!");
                    }
                    else
                    {
                        Console.WriteLine("✅ End cuboid has non-identity rotation");
                    }
                }
                else
                {
                    Console.WriteLine($"❌ End cuboid (ID {endId}) not found in saved level");
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during verification: {ex.Message}");
            }
        }

        /// <summary>
        /// Enhanced ship camera rotation verification with matrix determinant fixing
        /// </summary>
        public static void FixAndVerifyShipCameraRotations(string outputPath)
        {
            Console.WriteLine("\n🔧 FIXING AND VERIFYING SHIP CAMERA ROTATIONS");
            
            string enginePath = Path.Combine(outputPath, "engine.ps3");
            if (!File.Exists(enginePath))
            {
                Console.WriteLine($"❌ Engine file not found: {enginePath}");
                return;
            }
            
            try
            {
                // Load the level
                Console.WriteLine($"Loading level from: {enginePath}");
                var level = new Level(enginePath);
                
                if (level.levelVariables == null || level.cuboids == null)
                {
                    Console.WriteLine("❌ Missing level variables or cuboids");
                    return;
                }
                
                int startId = level.levelVariables.shipCameraStartID;
                int endId = level.levelVariables.shipCameraEndID;
                
                Console.WriteLine($"Ship camera cuboid IDs: Start={startId}, End={endId}");
                
                bool madeChanges = false;
                
                // Fix ship camera cuboids specifically
                var shipCameraCuboids = level.cuboids.Where(c => c.id == startId || c.id == endId).ToList();
                
                foreach (var cuboid in shipCameraCuboids)
                {
                    string cameraType = (cuboid.id == startId) ? "Start" : "End";
                    Console.WriteLine($"\n🔍 Analyzing {cameraType} Camera Cuboid (ID {cuboid.id}):");
                    
                    // Check current matrix determinant
                    float originalDet = cuboid.modelMatrix.Determinant;
                    Console.WriteLine($"  Original matrix determinant: {originalDet}");
                    
                    if (Math.Abs(originalDet) < 0.001f || Math.Abs(originalDet) > 1000f)
                    {
                        Console.WriteLine($"  ⚠️ Suspicious determinant detected! Fixing...");
                        
                        // Store original transform data
                        var originalPos = cuboid.position;
                        var originalRot = cuboid.rotation;
                        var originalScale = cuboid.scale;
                        
                        Console.WriteLine($"  Original Position: {originalPos}");
                        Console.WriteLine($"  Original Rotation: {originalRot}");
                        Console.WriteLine($"  Original Scale: {originalScale}");
                        
                        // Ensure scale is reasonable (determinant = scale.X * scale.Y * scale.Z * rotationDet)
                        var fixedScale = originalScale;
                        if (Math.Abs(fixedScale.X) < 0.01f) fixedScale.X = 1.0f;
                        if (Math.Abs(fixedScale.Y) < 0.01f) fixedScale.Y = 1.0f;
                        if (Math.Abs(fixedScale.Z) < 0.01f) fixedScale.Z = 1.0f;
                        if (Math.Abs(fixedScale.X) > 100f) fixedScale.X = 1.0f;
                        if (Math.Abs(fixedScale.Y) > 100f) fixedScale.Y = 1.0f;
                        if (Math.Abs(fixedScale.Z) > 100f) fixedScale.Z = 1.0f;
                        
                        // Ensure rotation is normalized
                        var fixedRotation = originalRot.Normalized();
                        
                        // Rebuild the matrix properly
                        cuboid.position = originalPos;
                        cuboid.rotation = fixedRotation;
                        cuboid.scale = fixedScale;
                        cuboid.UpdateTransformMatrix();
                        
                        float newDet = cuboid.modelMatrix.Determinant;
                        Console.WriteLine($"  Fixed matrix determinant: {newDet}");
                        Console.WriteLine($"  Fixed Position: {cuboid.position}");
                        Console.WriteLine($"  Fixed Rotation: {cuboid.rotation}");
                        Console.WriteLine($"  Fixed Scale: {cuboid.scale}");
                        
                        if (Math.Abs(newDet) >= 0.001f && Math.Abs(newDet) <= 1000f)
                        {
                            Console.WriteLine($"  ✅ Matrix determinant fixed successfully!");
                            madeChanges = true;
                        }
                        else
                        {
                            Console.WriteLine($"  ❌ Matrix determinant still suspicious: {newDet}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  ✅ Matrix determinant looks good");
                    }
                }
                
                if (madeChanges)
                {
                    Console.WriteLine("\n💾 Saving level with fixed ship camera cuboids...");
                    level.Save(outputPath);
                    Console.WriteLine("✅ Level saved successfully");
                    
                    // Verify the fix worked
                    Console.WriteLine("\n🔍 Verifying fixes...");
                    var verifyLevel = new Level(enginePath);
                    var verifyStartCuboid = verifyLevel.cuboids?.FirstOrDefault(c => c.id == startId);
                    var verifyEndCuboid = verifyLevel.cuboids?.FirstOrDefault(c => c.id == endId);
                    
                    if (verifyStartCuboid != null)
                    {
                        float verifyDet = verifyStartCuboid.modelMatrix.Determinant;
                        Console.WriteLine($"  Start camera determinant after save: {verifyDet}");
                        LogRotationAsEuler("VERIFIED Start", verifyStartCuboid.rotation);
                    }
                    
                    if (verifyEndCuboid != null)
                    {
                        float verifyDet = verifyEndCuboid.modelMatrix.Determinant;
                        Console.WriteLine($"  End camera determinant after save: {verifyDet}");
                        LogRotationAsEuler("VERIFIED End", verifyEndCuboid.rotation);
                    }
                }
                else
                {
                    Console.WriteLine("\n✅ No fixes needed for ship camera cuboids");
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during fix and verification: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to log rotation as Euler angles for easier understanding
        /// </summary>
        private static void LogRotationAsEuler(string context, Quaternion rotation)
        {
            var euler = rotation.ToEulerAngles();
            Console.WriteLine($"    {context} Euler: X={euler.X * 180.0f / MathF.PI:F2}°, Y={euler.Y * 180.0f / MathF.PI:F2}°, Z={euler.Z * 180.0f / MathF.PI:F2}°");
        }

        /// <summary>
        /// Checks if a quaternion is approximately identity rotation
        /// </summary>
        private static bool IsIdentityRotation(Quaternion rotation)
        {
            const float tolerance = 0.001f;
            return Math.Abs(rotation.X) <= tolerance &&
                   Math.Abs(rotation.Y) <= tolerance &&
                   Math.Abs(rotation.Z) <= tolerance &&
                   Math.Abs(rotation.W - 1.0f) <= tolerance;
        }

        /// <summary>
        /// Tests cuboid serialization round-trip to identify where rotation data is lost
        /// </summary>
        public static void TestCuboidSerializationRoundTrip(Level level)
        {
            Console.WriteLine("\n🧪 TESTING CUBOID SERIALIZATION ROUND TRIP");
            
            if (level.cuboids == null || level.cuboids.Count == 0)
            {
                Console.WriteLine("❌ No cuboids to test");
                return;
            }
            
            foreach (var cuboid in level.cuboids.Take(3))
            {
                Console.WriteLine($"\n--- Testing Cuboid {cuboid.id} ---");
                
                // Store original data
                var originalPos = cuboid.position;
                var originalRot = cuboid.rotation;
                var originalScale = cuboid.scale;
                var originalMatrix = cuboid.modelMatrix;
                var originalDet = originalMatrix.Determinant;
                
                Console.WriteLine($"Original:");
                Console.WriteLine($"  Position: {originalPos}");
                Console.WriteLine($"  Rotation: {originalRot}");
                Console.WriteLine($"  Scale: {originalScale}");
                Console.WriteLine($"  Matrix Det: {originalDet}");
                
                // Test 1: Direct ToByteArray() round trip
                Console.WriteLine($"\nTest 1: Direct ToByteArray() round trip");
                try
                {
                    byte[] serialized = cuboid.ToByteArray();
                    Console.WriteLine($"  Serialized to {serialized.Length} bytes");
                    
                    // Create new cuboid from serialized data
                    var testCuboid = new Cuboid(serialized, 0);
                    
                    Console.WriteLine($"  Deserialized:");
                    Console.WriteLine($"    Position: {testCuboid.position}");
                    Console.WriteLine($"    Rotation: {testCuboid.rotation}");
                    Console.WriteLine($"    Scale: {testCuboid.scale}");
                    Console.WriteLine($"    Matrix Det: {testCuboid.modelMatrix.Determinant}");
                    
                    // Check differences
                    float posDiff = (originalPos - testCuboid.position).Length;
                    float rotDiff = (originalRot - testCuboid.rotation).Length;
                    float scaleDiff = (originalScale - testCuboid.scale).Length;
                    float detDiff = Math.Abs(originalDet - testCuboid.modelMatrix.Determinant);
                    
                    Console.WriteLine($"  Differences:");
                    Console.WriteLine($"    Position: {posDiff} {(posDiff > 0.01f ? "❌" : "✅")}");
                    Console.WriteLine($"    Rotation: {rotDiff} {(rotDiff > 0.01f ? "❌" : "✅")}");
                    Console.WriteLine($"    Scale: {scaleDiff} {(scaleDiff > 0.01f ? "❌" : "✅")}");
                    Console.WriteLine($"    Matrix Det: {detDiff} {(detDiff > 0.01f ? "❌" : "✅")}");

                    if (rotDiff > 0.01f)
                    {
                        Console.WriteLine($"🚨 ROTATION CORRUPTION DETECTED IN ToByteArray()!");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error in ToByteArray() test: {ex.Message}");
                }
                
                // Test 2: GameplaySerializer.SerializeLevelObjects() simulation
                Console.WriteLine($"\nTest 2: GameplaySerializer simulation");
                try
                {
                    var testList = new List<Cuboid> { cuboid };
                    var gameplaySerializer = new LibReplanetizer.Serializers.GameplaySerializer(); // 🔧 FULLY QUALIFIED NAME
    
                    // Call the SerializeLevelObjects method directly
                    byte[] serializedBlock = gameplaySerializer.SerializeLevelObjects(testList, Cuboid.ELEMENTSIZE);
                    
                    Console.WriteLine($"  SerializeLevelObjects returned {serializedBlock.Length} bytes");
                    
                    // Extract the cuboid data (skip 16-byte header)
                    if (serializedBlock.Length >= 16 + Cuboid.ELEMENTSIZE)
                    {
                        byte[] cuboidData = new byte[Cuboid.ELEMENTSIZE];
                        Array.Copy(serializedBlock, 16, cuboidData, 0, Cuboid.ELEMENTSIZE);
                        
                        var testCuboid2 = new Cuboid(cuboidData, 0);
                        
                        Console.WriteLine($"  After GameplaySerializer:");
                        Console.WriteLine($"    Position: {testCuboid2.position}");
                        Console.WriteLine($"    Rotation: {testCuboid2.rotation}");
                        Console.WriteLine($"    Scale: {testCuboid2.scale}");
                        Console.WriteLine($"    Matrix Det: {testCuboid2.modelMatrix.Determinant}");
                        
                        float rotDiff2 = (originalRot - testCuboid2.rotation).Length;
                        if (rotDiff2 > 0.01f)
                        {
                            Console.WriteLine($"🚨 ROTATION CORRUPTION IN GAMEPLAYSERIALIZER!");
                        }
                        else
                        {
                            Console.WriteLine($"✅ GameplaySerializer preserved rotation");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error in GameplaySerializer test: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Compares UYA donor level with the saved level to identify differences
        /// </summary>
        /// <param name="uyaDonorLevel">The original UYA donor level</param>
        /// <param name="outputDir">Directory containing the saved level</param>
        public static void CompareUyaDonorVsSavedLevel(Level uyaDonorLevel, string outputDir)
        {
            Console.WriteLine("\n🔍 COMPARING UYA DONOR vs SAVED LEVEL");
            
            string savedEnginePath = Path.Combine(outputDir, "engine.ps3");
            if (!File.Exists(savedEnginePath))
            {
                Console.WriteLine($"❌ Saved level not found at: {savedEnginePath}");
                return;
            }
            
            try
            {
                Console.WriteLine($"Loading saved level from: {savedEnginePath}");
                var savedLevel = new Level(savedEnginePath);
                
                Console.WriteLine("\n=== BASIC COMPARISON ===");
                Console.WriteLine($"UYA Donor Cuboids: {uyaDonorLevel.cuboids?.Count ?? 0}");
                Console.WriteLine($"Saved Level Cuboids: {savedLevel.cuboids?.Count ?? 0}");
                
                Console.WriteLine($"UYA Donor Splines: {uyaDonorLevel.splines?.Count ?? 0}");
                Console.WriteLine($"Saved Level Splines: {savedLevel.splines?.Count ?? 0}");
                
                // Compare ship camera settings
                if (uyaDonorLevel.levelVariables != null && savedLevel.levelVariables != null)
                {
                    Console.WriteLine("\n=== SHIP CAMERA SETTINGS COMPARISON ===");
                    Console.WriteLine($"Ship Path ID:");
                    Console.WriteLine($"  UYA Donor: {uyaDonorLevel.levelVariables.shipPathID}");
                    Console.WriteLine($"  Saved:     {savedLevel.levelVariables.shipPathID}");
                    
                    Console.WriteLine($"Ship Camera Start ID:");
                    Console.WriteLine($"  UYA Donor: {uyaDonorLevel.levelVariables.shipCameraStartID}");
                    Console.WriteLine($"  Saved:     {savedLevel.levelVariables.shipCameraStartID}");
                    
                    Console.WriteLine($"Ship Camera End ID:");
                    Console.WriteLine($"  UYA Donor: {uyaDonorLevel.levelVariables.shipCameraEndID}");
                    Console.WriteLine($"  Saved:     {savedLevel.levelVariables.shipCameraEndID}");
                }
                
                // Compare ship camera cuboids if they exist
                if (savedLevel.levelVariables != null)
                {
                    int savedStartId = savedLevel.levelVariables.shipCameraStartID;
                    int savedEndId = savedLevel.levelVariables.shipCameraEndID;
                    
                    Console.WriteLine("\n=== SHIP CAMERA CUBOID COMPARISON ===");
                    
                    // Compare start camera
                    var savedStartCuboid = savedLevel.cuboids?.FirstOrDefault(c => c.id == savedStartId);
                    var uyaStartCuboid = uyaDonorLevel.cuboids?.FirstOrDefault(c => c.id == savedStartId);
                    
                    if (savedStartCuboid != null && uyaStartCuboid != null)
                    {
                        Console.WriteLine($"Start Camera Cuboid (ID {savedStartId}):");
                        CompareCuboidDetails("UYA Donor", uyaStartCuboid, "Saved", savedStartCuboid);
                    }
                    else
                    {
                        Console.WriteLine($"Start Camera Cuboid (ID {savedStartId}):");
                        Console.WriteLine($"  UYA Donor: {(uyaStartCuboid != null ? "Found" : "Not Found")}");
                        Console.WriteLine($"  Saved:     {(savedStartCuboid != null ? "Found" : "Not Found")}");
                    }
                    
                    // Compare end camera
                    var savedEndCuboid = savedLevel.cuboids?.FirstOrDefault(c => c.id == savedEndId);
                    var uyaEndCuboid = uyaDonorLevel.cuboids?.FirstOrDefault(c => c.id == savedEndId);
                    
                    if (savedEndCuboid != null && uyaEndCuboid != null)
                    {
                        Console.WriteLine($"\nEnd Camera Cuboid (ID {savedEndId}):");
                        CompareCuboidDetails("UYA Donor", uyaEndCuboid, "Saved", savedEndCuboid);
                    }
                    else
                    {
                        Console.WriteLine($"\nEnd Camera Cuboid (ID {savedEndId}):");
                        Console.WriteLine($"  UYA Donor: {(uyaEndCuboid != null ? "Found" : "Not Found")}");
                        Console.WriteLine($"  Saved:     {(savedEndCuboid != null ? "Found" : "Not Found")}");
                    }
                }
                
                // Check for any new cuboids in saved level
                if (savedLevel.cuboids != null && uyaDonorLevel.cuboids != null)
                {
                    var uyaDonorIds = new HashSet<int>(uyaDonorLevel.cuboids.Select(c => c.id));
                    var newCuboids = savedLevel.cuboids.Where(c => !uyaDonorIds.Contains(c.id)).ToList();
                    
                    if (newCuboids.Count > 0)
                    {
                        Console.WriteLine($"\n=== NEW CUBOIDS IN SAVED LEVEL ===");
                        Console.WriteLine($"Found {newCuboids.Count} new cuboids:");
                        
                        foreach (var cuboid in newCuboids.Take(5)) // Show first 5
                        {
                            Console.WriteLine($"  Cuboid ID {cuboid.id}:");
                            Console.WriteLine($"    Position: {cuboid.position}");
                            Console.WriteLine($"    Rotation: {cuboid.rotation}");
                            Console.WriteLine($"    Scale: {cuboid.scale}");
                            LogRotationAsEuler($"    ID {cuboid.id}", cuboid.rotation);
                        }
                        
                        if (newCuboids.Count > 5)
                        {
                            Console.WriteLine($"  ... and {newCuboids.Count - 5} more");
                        }
                    }
                    else
                    {
                        Console.WriteLine("\n=== NO NEW CUBOIDS ===");
                        Console.WriteLine("No new cuboids were added to the saved level");
                    }
                }
                
                Console.WriteLine("\n✅ UYA Donor vs Saved Level comparison complete");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error comparing levels: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to compare two cuboids in detail
        /// </summary>
        private static void CompareCuboidDetails(string label1, Cuboid cuboid1, string label2, Cuboid cuboid2)
        {
            Console.WriteLine($"  Position:");
            Console.WriteLine($"    {label1}: {cuboid1.position}");
            Console.WriteLine($"    {label2}: {cuboid2.position}");
            
            Console.WriteLine($"  Rotation:");
            Console.WriteLine($"    {label1}: {cuboid1.rotation}");
            Console.WriteLine($"    {label2}: {cuboid2.rotation}");
            
            Console.WriteLine($"  Scale:");
            Console.WriteLine($"    {label1}: {cuboid1.scale}");
            Console.WriteLine($"    {label2}: {cuboid2.scale}");
            
            Console.WriteLine($"  Matrix Determinant:");
            Console.WriteLine($"    {label1}: {cuboid1.modelMatrix.Determinant}");
            Console.WriteLine($"    {label2}: {cuboid2.modelMatrix.Determinant}");
            
            // Check for significant differences
            float posDiff = (cuboid1.position - cuboid2.position).Length;
            float rotDiff = (cuboid1.rotation - cuboid2.rotation).Length;
            float scaleDiff = (cuboid1.scale - cuboid2.scale).Length;
            float detDiff = Math.Abs(cuboid1.modelMatrix.Determinant - cuboid2.modelMatrix.Determinant);
            
            Console.WriteLine($"  Differences:");
            Console.WriteLine($"    Position: {posDiff:F6} {(posDiff > 0.01f ? "❌ SIGNIFICANT" : "✅")}");
            Console.WriteLine($"    Rotation: {rotDiff:F6} {(rotDiff > 0.01f ? "❌ SIGNIFICANT" : "✅")}");
            Console.WriteLine($"    Scale: {scaleDiff:F6} {(scaleDiff > 0.01f ? "❌ SIGNIFICANT" : "✅")}");
            Console.WriteLine($"    Matrix Det: {detDiff:F6} {(detDiff > 0.01f ? "❌ SIGNIFICANT" : "✅")}");
            
            // Log Euler angles for easier understanding
            LogRotationAsEuler($"  {label1} Euler", cuboid1.rotation);
            LogRotationAsEuler($"  {label2} Euler", cuboid2.rotation);
        }

        /// <summary>
        /// 🆕 ENHANCED: Fixes the Ship Path Index by finding the best spline match after imports
        /// This handles cases where splines were duplicated during different import phases
        /// </summary>
        /// <param name="targetLevel">The target level to fix</param>
        /// <param name="sourceLevel">The source level to get the original ship path spline geometry from</param>
        /// <returns>True if the fix was applied successfully</returns>
        public static bool FixShipPathIndexByGeometry(Level targetLevel, Level sourceLevel)
        {
            Console.WriteLine("\n🔧 ENHANCED SHIP PATH INDEX GEOMETRY MATCHING");
            
            if (targetLevel?.levelVariables == null || sourceLevel?.levelVariables == null)
            {
                Console.WriteLine("❌ Cannot fix ship path index: LevelVariables are missing");
                return false;
            }
            
            if (targetLevel.splines == null || sourceLevel.splines == null)
            {
                Console.WriteLine("❌ Cannot fix ship path index: Splines are missing");
                return false;
            }
            
            int sourceShipPathId = sourceLevel.levelVariables.shipPathID;
            int currentTargetShipPathId = targetLevel.levelVariables.shipPathID;
            
            Console.WriteLine($"SOURCE Level Ship Path Index: {sourceShipPathId}");
            Console.WriteLine($"TARGET Level current Ship Path Index: {currentTargetShipPathId}");
            
            // Step 1: Find the SOURCE spline that matches the SOURCE level's Ship Path Index
            Spline sourceShipPathSpline = FindOrLoadSplineById(sourceLevel, sourceShipPathId);
            if (sourceShipPathSpline == null)
            {
                Console.WriteLine($"❌ Could not find SOURCE spline with ID {sourceShipPathId} in SOURCE level");
                return false;
            }
            
            Console.WriteLine($"SOURCE spline geometry (ID {sourceShipPathId}):");
            Console.WriteLine($"  Vertex count: {sourceShipPathSpline.GetVertexCount()}");
            Console.WriteLine($"  W-values count: {sourceShipPathSpline.wVals.Length}");
            if (sourceShipPathSpline.GetVertexCount() > 0)
            {
                Console.WriteLine($"  First vertex: {sourceShipPathSpline.GetVertex(0)}");
                Console.WriteLine($"  Last vertex: {sourceShipPathSpline.GetVertex(sourceShipPathSpline.GetVertexCount() - 1)}");
            }
            
            // Step 2: Find ALL matching splines in the target level (not just one)
            Console.WriteLine($"Searching TARGET level for ALL splines with matching geometry...");
            Console.WriteLine($"TARGET level has {targetLevel.splines.Count} splines total");
            
            var matchingSplines = FindAllSplinesByGeometry(targetLevel, sourceShipPathSpline);
            
            if (matchingSplines.Count == 0)
            {
                Console.WriteLine($"❌ Could not find any TARGET splines with matching geometry");
                return false;
            }
            
            Console.WriteLine($"✅ Found {matchingSplines.Count} TARGET splines with matching geometry:");
            foreach (var match in matchingSplines)
            {
                Console.WriteLine($"  Spline ID {match.id}: {match.GetVertexCount()} vertices, W-vals: {match.wVals.Length}");
            }
            
            // Step 3: Choose the BEST match based on various criteria
            Spline bestMatch = ChooseBestSplineMatch(matchingSplines, sourceShipPathId);
            
            int correctTargetSplineId = bestMatch.id;
            Console.WriteLine($"🎯 Selected BEST matching TARGET spline:");
            Console.WriteLine($"  TARGET Spline ID: {correctTargetSplineId}");
            Console.WriteLine($"  TARGET Spline Vertices: {bestMatch.GetVertexCount()}");
            Console.WriteLine($"  Selection reason: {GetSelectionReason(bestMatch, matchingSplines, sourceShipPathId)}");
            
            if (currentTargetShipPathId != correctTargetSplineId)
            {
                Console.WriteLine($"🔧 Updating TARGET Level Ship Path Index: {currentTargetShipPathId} → {correctTargetSplineId}");
                targetLevel.levelVariables.shipPathID = correctTargetSplineId;
                Console.WriteLine($"✅ TARGET Level Ship Path Index fixed successfully!");
                return true;
            }
            else
            {
                Console.WriteLine($"✅ TARGET Level Ship Path Index is already correct (ID {correctTargetSplineId})");
                return true;
            }
        }
        
        /// <summary>
        /// Finds ALL splines in the target level that have the same geometry as the source spline
        /// </summary>
        /// <param name="level">Level to search in</param>
        /// <param name="sourceSpline">The source spline to match against</param>
        /// <returns>List of all splines with matching geometry</returns>
        private static List<Spline> FindAllSplinesByGeometry(Level level, Spline sourceSpline)
        {
            var matches = new List<Spline>();
            
            if (level.splines == null) return matches;
            
            Console.WriteLine($"Looking for ALL splines with {sourceSpline.GetVertexCount()} vertices and {sourceSpline.wVals.Length} w-values");
            
            foreach (var targetSpline in level.splines)
            {
                // Quick checks first
                if (targetSpline.GetVertexCount() != sourceSpline.GetVertexCount())
                    continue;
                    
                if (targetSpline.wVals.Length != sourceSpline.wVals.Length)
                    continue;
                
                // Check if vertex buffers match (with small tolerance for floating point)
                if (VertexBuffersMatch(sourceSpline.vertexBuffer, targetSpline.vertexBuffer, 0.001f))
                {
                    // Check if W values match
                    if (WValuesMatch(sourceSpline.wVals, targetSpline.wVals, 0.001f))
                    {
                        Console.WriteLine($"  ✅ Found geometry match: Spline ID {targetSpline.id}");
                        matches.Add(targetSpline);
                    }
                }
            }
            
            Console.WriteLine($"  Total matches found: {matches.Count}");
            return matches;
        }
        
        /// <summary>
        /// Chooses the best spline match from multiple candidates based on various criteria
        /// </summary>
        /// <param name="candidates">List of candidate splines</param>
        /// <param name="originalSourceId">The original source spline ID for reference</param>
        /// <returns>The best matching spline</returns>
        private static Spline ChooseBestSplineMatch(List<Spline> candidates, int originalSourceId)
        {
            if (candidates.Count == 1)
            {
                return candidates[0];
            }
            
            // Scoring criteria (higher score = better match):
            // 1. Prefer splines that preserve original ID (score +100)
            // 2. Prefer splines with higher IDs (likely from import, not ship animation duplication)
            // 3. Prefer splines that are NOT in the 100-120 range (likely ship animation range)
            
            var scored = candidates.Select(spline => new
            {
                Spline = spline,
                Score = CalculateSplineScore(spline, originalSourceId)
            }).ToList();
            
            // Sort by score descending
            var sortedByScore = scored.OrderByDescending(x => x.Score).ToList();
            
            Console.WriteLine($"Spline scoring results:");
            foreach (var item in sortedByScore)
            {
                Console.WriteLine($"  Spline ID {item.Spline.id}: Score {item.Score}");
            }
            
            return sortedByScore.First().Spline;
        }
        
        /// <summary>
        /// Calculates a score for how good a spline match is
        /// </summary>
        /// <param name="spline">The spline to score</param>
        /// <param name="originalSourceId">Original source ID for reference</param>
        /// <returns>Score (higher = better)</returns>
        private static int CalculateSplineScore(Spline spline, int originalSourceId)
        {
            int score = 0;
            
            // Bonus for preserving original ID
            if (spline.id == originalSourceId)
            {
                score += 100;
                Console.WriteLine($"    ID {spline.id}: +100 (preserves original ID {originalSourceId})");
            }
            
            // Penalty for being in the "ship animation duplication" range (100-120)
            if (spline.id >= 100 && spline.id <= 120)
            {
                score -= 50;
                Console.WriteLine($"    ID {spline.id}: -50 (likely ship animation duplicate)");
            }
            
            // Bonus for higher IDs (likely from later import phases)
            if (spline.id > 150)
            {
                score += 25;
                Console.WriteLine($"    ID {spline.id}: +25 (high ID, likely from import)");
            }
            
            // Small bonus based on ID itself (tiebreaker)
            score += spline.id / 10;
            
            return score;
        }
        
        /// <summary>
        /// Gets a human-readable reason for why a spline was selected
        /// </summary>
        /// <param name="selectedSpline">The selected spline</param>
        /// <param name="allCandidates">All candidate splines</param>
        /// <param name="originalSourceId">Original source ID</param>
        /// <returns>Selection reason</returns>
        private static string GetSelectionReason(Spline selectedSpline, List<Spline> allCandidates, int originalSourceId)
        {
            if (allCandidates.Count == 1)
            {
                return "Only match found";
            }
            
            if (selectedSpline.id == originalSourceId)
            {
                return $"Preserves original ID {originalSourceId}";
            }
            
            if (selectedSpline.id >= 100 && selectedSpline.id <= 120)
            {
                return "Selected despite being in ship animation range (best available option)";
            }
            
            if (selectedSpline.id > 150)
            {
                return $"High ID {selectedSpline.id} suggests it's from spline import, not ship animation duplicate";
            }
            
            return $"Highest scoring option among {allCandidates.Count} candidates";
        }

        /// <summary>
        /// Checks if two vertex buffers are nearly identical
        /// </summary>
        /// <param name="buffer1">First vertex buffer</param>
        /// <param name="buffer2">Second vertex buffer</param>
        /// <param name="tolerance">Tolerance for floating point comparison</param>
        /// <returns>True if buffers match within tolerance</returns>
        private static bool VertexBuffersMatch(float[] buffer1, float[] buffer2, float tolerance)
        {
            if (buffer1.Length != buffer2.Length)
                return false;
            
            for (int i = 0; i < buffer1.Length; i++)
            {
                if (Math.Abs(buffer1[i] - buffer2[i]) > tolerance)
                {
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Checks if two w-value arrays are nearly identical
        /// </summary>
        /// <param name="wVals1">First w-value array</param>
        /// <param name="wVals2">Second w-value array</param>
        /// <param name="tolerance">Tolerance for floating point comparison</param>
        /// <returns>True if w-values match within tolerance</returns>
        private static bool WValuesMatch(float[] wVals1, float[] wVals2, float tolerance)
        {
            if (wVals1.Length != wVals2.Length)
                return false;
            
            for (int i = 0; i < wVals1.Length; i++)
            {
                if (Math.Abs(wVals1[i] - wVals2[i]) > tolerance)
                {
                    return false;
                }
            }
            
            return true;
        }
    }
}
