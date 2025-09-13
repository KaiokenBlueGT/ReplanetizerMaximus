// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles manipulation of point lights and directional lights between RC1 and RC2 levels
    /// </summary>
    public static class PointLightsSwapper
    {
        /// <summary>
        /// Swaps point lights from RC1 level to RC2 level, maintaining the exact count from source
        /// </summary>
        /// <param name="targetLevel">RC2 level to modify</param>
        /// <param name="rc1SourceLevel">RC1 level to get light data from</param>
        /// <returns>True if operation was successful</returns>
        public static bool SwapPointLights(Level targetLevel, Level rc1SourceLevel)
        {
            if (targetLevel == null || rc1SourceLevel == null)
            {
                Console.WriteLine("❌ Cannot swap point lights: Invalid level data");
                return false;
            }

            Console.WriteLine("\n==== Swapping Point Lights to match RC1 level ====");

            // Process the point lights
            ProcessPointLights(targetLevel, rc1SourceLevel);

            // Process directional lights (the ones shown in your screenshot)
            ProcessDirectionalLights(targetLevel, rc1SourceLevel);

            return true;
        }

        /// <summary>
        /// Process the point lights from the levels
        /// </summary>
        private static void ProcessPointLights(Level targetLevel, Level rc1SourceLevel)
        {
            if (targetLevel.pointLights == null)
            {
                targetLevel.pointLights = new List<PointLight>();
                Console.WriteLine("Created new point lights list for target level");
            }

            if (rc1SourceLevel.pointLights == null)
            {
                Console.WriteLine("⚠️ RC1 level has no point lights list");

                // Clear any existing point lights in target level since source has none
                if (targetLevel.pointLights.Count > 0)
                {
                    targetLevel.pointLights.Clear();
                    Console.WriteLine("✅ Removed all point lights from target level to match RC1 source");
                }

                return;
            }

            // Clear existing point lights
            int removedCount = targetLevel.pointLights.Count;
            targetLevel.pointLights.Clear();
            Console.WriteLine($"✅ Removed {removedCount} existing point lights from target level");

            // Copy point lights from RC1 level - exact count matching
            foreach (var rc1Light in rc1SourceLevel.pointLights)
            {
                // Create a new RC2/3 light with proper RC2/3 format data
                // IMPORTANT: Always pass 0 as the num parameter to avoid offset issues
                byte[] rc2LightData = new byte[PointLight.GetElementSize(GameType.RaC3)];
                PointLight newLight = new PointLight(GameType.RaC3, rc2LightData, 0);

                // Set the correct ID after creation
                newLight.id = targetLevel.pointLights.Count;

                // Copy the properties from RC1 light (this bypasses the format conversion issues)
                newLight.position = rc1Light.position;
                newLight.color = rc1Light.color;
                newLight.radius = rc1Light.radius;

                targetLevel.pointLights.Add(newLight);
                Console.WriteLine($"✅ Added point light: Position={newLight.position}, Color={newLight.color}, Radius={newLight.radius}");
            }

            Console.WriteLine($"✅ Added {targetLevel.pointLights.Count} point lights from RC1 level (exact match)");
        }

        /// <summary>
        /// Process directional lights from the levels (the ones shown in the screenshot)
        /// </summary>
        private static void ProcessDirectionalLights(Level targetLevel, Level rc1SourceLevel)
        {
            if (targetLevel.lights == null)
            {
                targetLevel.lights = new List<Light>();
                Console.WriteLine("Created new directional lights list for target level");
            }

            if (rc1SourceLevel.lights == null)
            {
                Console.WriteLine("⚠️ RC1 level has no directional lights list");
                return;
            }

            // IMPORTANT: Match the exact count of lights from RC1 source level
            int rc1LightCount = rc1SourceLevel.lights.Count;
            int targetLightCount = targetLevel.lights.Count;

            Console.WriteLine($"RC1 source has {rc1LightCount} directional light(s)");
            Console.WriteLine($"Target level has {targetLightCount} directional light(s) initially");

            if (targetLightCount > rc1LightCount)
            {
                // Remove excess lights to match RC1 count
                int toRemove = targetLightCount - rc1LightCount;
                targetLevel.lights = targetLevel.lights.Take(rc1LightCount).ToList();
                Console.WriteLine($"✅ Removed {toRemove} excess directional light(s) to match RC1 count");
            }
            else if (targetLightCount < rc1LightCount)
            {
                // Add lights if we don't have enough
                for (int i = targetLightCount; i < rc1LightCount; i++)
                {
                    Light newLight = CreateDefaultLight();
                    targetLevel.lights.Add(newLight);
                    Console.WriteLine($"✅ Added new default directional light at index {i}");
                }
            }

            // Now we have exactly the same number of lights as the RC1 source
            // Copy properties from RC1 lights to target lights
            for (int i = 0; i < rc1LightCount; i++)
            {
                Light rc1Light = rc1SourceLevel.lights[i];
                Light targetLight = targetLevel.lights[i];

                // Update the light properties
                targetLight.color1 = rc1Light.color1;
                targetLight.direction1 = rc1Light.direction1;
                targetLight.color2 = rc1Light.color2;
                targetLight.direction2 = rc1Light.direction2;

                Console.WriteLine($"✅ Updated directional light {i} to match RC1 values:");
                Console.WriteLine($"  Color 1: ({rc1Light.color1.X}, {rc1Light.color1.Y}, {rc1Light.color1.Z}, {rc1Light.color1.W})");
                Console.WriteLine($"  Direction 1: ({rc1Light.direction1.X}, {rc1Light.direction1.Y}, {rc1Light.direction1.Z}, {rc1Light.direction1.W})");
                Console.WriteLine($"  Color 2: ({rc1Light.color2.X}, {rc1Light.color2.Y}, {rc1Light.color2.Z}, {rc1Light.color2.W})");
                Console.WriteLine($"  Direction 2: ({rc1Light.direction2.X}, {rc1Light.direction2.Y}, {rc1Light.direction2.Z}, {rc1Light.direction2.W})");
            }

            // Update all light references in mobys, ties and shrubs to use Light 0
            UpdateLightReferences(targetLevel);
        }

        /// <summary>
        /// Updates all objects in the level to reference Light 0
        /// </summary>
        private static void UpdateLightReferences(Level level)
        {
            int mobyCount = 0;
            int shrubCount = 0;
            int tieCount = 0;

            // Update all mobys to use Light 0
            if (level.mobs != null)
            {
                foreach (var moby in level.mobs)
                {
                    if (moby.light != 0)
                    {
                        moby.light = 0;
                        mobyCount++;
                    }
                }
            }

            // Update all shrubs to use Light 0
            if (level.shrubs != null)
            {
                foreach (var shrub in level.shrubs)
                {
                    if (shrub.light != 0)
                    {
                        shrub.light = 0;
                        shrubCount++;
                    }
                }
            }

            // Update all ties to use Light 0
            if (level.ties != null)
            {
                foreach (var tie in level.ties)
                {
                    if (tie.light != 0)
                    {
                        tie.light = 0;
                        tieCount++;
                    }
                }
            }

            Console.WriteLine($"✅ Updated light references to use Light 0: {mobyCount} mobys, {shrubCount} shrubs, {tieCount} ties");
        }

        /// <summary>
        /// Creates a default light with standard values
        /// </summary>
        private static Light CreateDefaultLight()
        {
            // Create a byte array with default values
            byte[] defaultLightData = new byte[0x40];
            
            // Fill with default values (mostly zeros)
            for (int i = 0; i < defaultLightData.Length; i++)
            {
                defaultLightData[i] = 0;
            }
            
            // Create the light from the default data
            Light light = new Light(defaultLightData, 0);
            
            // Set some reasonable default values
            light.color1 = new Vector4(0.267f, 0.286f, 0.286f, 1.0f);  // Light gray
            light.direction1 = new Vector4(0.677f, 0.556f, -0.482f, 1.0f);  // From top right
            light.color2 = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);  // Black (no secondary light)
            light.direction2 = new Vector4(0.770f, 0.421f, -0.479f, 1.0f);  // Similar direction
            
            return light;
        }

        /// <summary>
        /// Interactive wrapper for point light swapping function
        /// </summary>
        /// <returns>True if the operation was successful</returns>
        public static bool SwapPointLightsInteractive()
        {
            Console.WriteLine("\n==== Swap RC2 Point Lights with RC1 Oltanis Point Lights ====");

            // Get target level path
            Console.WriteLine("\nEnter path to the target RC2 level engine.ps3 file:");
            Console.Write("> ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                Console.WriteLine("❌ Invalid target level path");
                return false;
            }

            // Get RC1 Oltanis level path
            Console.WriteLine("\nEnter path to the RC1 Oltanis level engine.ps3 file:");
            Console.Write("> ");
            string rc1OltanisPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(rc1OltanisPath) || !File.Exists(rc1OltanisPath))
            {
                Console.WriteLine("❌ Invalid RC1 Oltanis level path");
                return false;
            }

            // Load levels
            Level targetLevel, rc1OltanisLevel;
            try
            {
                targetLevel = new Level(targetPath);
                rc1OltanisLevel = new Level(rc1OltanisPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading levels: {ex.Message}");
                return false;
            }

            // Perform point lights swap
            bool success = SwapPointLights(targetLevel, rc1OltanisLevel);

            if (success)
            {
                Console.Write("\nSave changes to the target level? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLower() == "y")
                {
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
    }
}
