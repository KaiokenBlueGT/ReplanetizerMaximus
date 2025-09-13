using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using OpenTK.Mathematics;
using static LibReplanetizer.DataFunctions;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles the swapping of grind paths between levels, specifically from RC1 to RC2
    /// </summary>
    public class GrindPathSwapper
    {
        // Hard-coded flag to control adding new paths
        // Set to false to only update existing paths, true to add new paths as well
        private const bool ALLOW_ADDING_NEW_PATHS = true;

        /// <summary>
        /// Flags enum to control grind path swap options
        /// </summary>
        [Flags]
        public enum GrindPathSwapOptions
        {
            None = 0,
            UseRC1Positions = 1,
            CopyMissingPaths = 2,

            // Common combinations
            PositionOnly = UseRC1Positions,
            FullReplacement = UseRC1Positions | CopyMissingPaths,
            Default = PositionOnly
        }

        /// <summary>
        /// Swaps RC2 grind paths with RC1 Oltanis grind paths
        /// </summary>
        /// <param name="targetLevel">RC2 level to modify</param>
        /// <param name="rc1OltanisLevel">RC1 Oltanis level to get the grind paths from</param>
        /// <param name="options">Options to control the swap behavior</param>
        /// <returns>True if operation was successful</returns>
        public static bool SwapGrindPathsWithRC1Oltanis(Level targetLevel, Level rc1OltanisLevel, GrindPathSwapOptions options = GrindPathSwapOptions.Default)
        {
            if (targetLevel == null || rc1OltanisLevel == null)
            {
                Console.WriteLine("❌ Cannot swap grind paths: Invalid level data");
                return false;
            }

            try
            {
                Console.WriteLine("\n==== Swapping RC2 Grind Paths with RC1 Oltanis Grind Paths ====");

                // Check the hard-coded flag and modify options accordingly
                if (!ALLOW_ADDING_NEW_PATHS && options.HasFlag(GrindPathSwapOptions.CopyMissingPaths))
                {
                    options &= ~GrindPathSwapOptions.CopyMissingPaths;
                    Console.WriteLine("⚠️ Adding new paths is disabled by ALLOW_ADDING_NEW_PATHS flag");
                }

                Console.WriteLine($"Options: UseRC1Positions={options.HasFlag(GrindPathSwapOptions.UseRC1Positions)}, " +
                                  $"CopyMissingPaths={options.HasFlag(GrindPathSwapOptions.CopyMissingPaths)}");

                // Check if levels have grind paths
                if (rc1OltanisLevel.grindPaths == null || rc1OltanisLevel.grindPaths.Count == 0)
                {
                    Console.WriteLine("❌ No grind paths found in RC1 Oltanis level");
                    return false;
                }

                if (targetLevel.grindPaths == null)
                {
                    Console.WriteLine("❌ Target level has no grind path list initialized");
                    // Initialize the list if it doesn't exist
                    targetLevel.grindPaths = new List<GrindPath>();
                }

                // Initialize splines list if needed
                if (targetLevel.splines == null)
                {
                    targetLevel.splines = new List<Spline>();
                }

                Console.WriteLine($"Found {rc1OltanisLevel.grindPaths.Count} grind paths in RC1 Oltanis");
                Console.WriteLine($"Found {targetLevel.grindPaths.Count} grind paths in target level");

                // Find the highest existing spline ID to avoid conflicts
                int highestSplineId = -1;
                if (targetLevel.splines.Count > 0)
                {
                    highestSplineId = targetLevel.splines.Max(s => s.id);
                    Console.WriteLine($"Highest spline ID found: {highestSplineId}");
                }

                // We'll set this AFTER we've finished fixing path 0
                GrindPath templatePath = null;

                // Step 1: Reposition existing grind paths if UseRC1Positions is specified
                if (options.HasFlag(GrindPathSwapOptions.UseRC1Positions))
                {
                    int pathsRepositioned = 0;

                    for (int i = 0; i < targetLevel.grindPaths.Count && i < rc1OltanisLevel.grindPaths.Count; i++)
                    {
                        try
                        {
                            var rc1Path = rc1OltanisLevel.grindPaths[i];
                            var targetPath = targetLevel.grindPaths[i];

                            Console.WriteLine($"Repositioning grind path ID {targetPath.id} to match RC1 path ID {rc1Path.id}");

                            targetPath.position = rc1Path.position;
                            targetPath.radius = rc1Path.radius;

                            if (rc1Path.spline != null)
                            {
                                var oldSpline = targetPath.spline;

                                // Use a high ID space for grind path splines to avoid conflicts.
                                // This helps prevent ID collisions with other types of splines like camera splines
                                highestSplineId = Math.Max(highestSplineId, 100);
                                int newSplineId = ++highestSplineId;

                                if (rc1Path.spline.GetVertexCount() == 0)
                                {
                                    Console.WriteLine($"⚠️ Warning: Source spline has no vertices, skipping this spline!");
                                    continue;
                                }

                                int sourceVertexCount = rc1Path.spline.GetVertexCount();
                                int sourceWValCount = rc1Path.spline.wVals.Length;

                                if (sourceVertexCount != sourceWValCount)
                                {
                                    Console.WriteLine($"⚠️ Warning: Source spline has mismatched vertex/W value counts, will be fixed");

                                    // Fix mismatched vertex counts and w-values
                                    float[] fixedWVals;
                                    if (sourceWValCount < sourceVertexCount)
                                    {
                                        fixedWVals = new float[sourceVertexCount];
                                        Array.Copy(rc1Path.spline.wVals, fixedWVals, sourceWValCount);
                                        for (int w = sourceWValCount; w < sourceVertexCount; w++)
                                        {
                                            fixedWVals[w] = w > 0 ? fixedWVals[w - 1] + 0.1f : 0f;
                                        }
                                    }
                                    else
                                    {
                                        fixedWVals = new float[sourceVertexCount];
                                        Array.Copy(rc1Path.spline.wVals, fixedWVals, sourceVertexCount);
                                    }

                                    float[] newVertexBuffer = new float[rc1Path.spline.vertexBuffer.Length];
                                    Array.Copy(rc1Path.spline.vertexBuffer, newVertexBuffer, rc1Path.spline.vertexBuffer.Length);

                                    Spline newSpline = new Spline(newSplineId, newVertexBuffer)
                                    {
                                        wVals = fixedWVals,
                                        position = rc1Path.spline.position,
                                        rotation = rc1Path.spline.rotation,
                                        scale = rc1Path.spline.scale,
                                        reflection = rc1Path.spline.reflection
                                    };
                                    newSpline.UpdateTransformMatrix();

                                    targetLevel.splines.Add(newSpline);
                                    targetPath.spline = newSpline;

                                    Console.WriteLine($"✅ Created new spline with ID {newSplineId} with {newSpline.GetVertexCount()} vertices and matching W values");
                                }
                                else
                                {
                                    Spline newSpline = CloneSpline(rc1Path.spline, newSplineId);
                                    targetLevel.splines.Add(newSpline);
                                    targetPath.spline = newSpline;
                                }

                                // Safely handle old spline reference
                                if (oldSpline != null)
                                {
                                    // Save the newly assigned spline to ensure we don't delete it
                                    Spline newSpline = targetPath.spline;

                                    // Temporarily set targetPath.spline back to null to avoid counting it when checking references
                                    targetPath.spline = null;

                                    // Now check if the old spline is used by any OTHER path
                                    bool oldSplineStillReferenced = targetLevel.grindPaths.Any(p => p != null && p.spline == oldSpline);

                                    // Restore targetPath.spline to the new spline
                                    targetPath.spline = newSpline;

                                    // Only remove old spline if it's not referenced elsewhere
                                    if (!oldSplineStillReferenced)
                                    {
                                        // Safe to remove the old spline
                                        targetLevel.splines.Remove(oldSpline);
                                        Console.WriteLine($"  Removed unreferenced old spline ID {oldSpline.id}");
                                    }
                                }
                            }

                            targetPath.UpdateTransformMatrix();
                            pathsRepositioned++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Error repositioning path at index {i}: {ex.Message}");
                        }
                    }
                    Console.WriteLine($"✅ Repositioned {pathsRepositioned} grind paths to match RC1 Oltanis");
                }

                // Find a suitable template path for adding new paths
                if (targetLevel.grindPaths.Count > 0)
                {
                    // Find the first path with a valid spline for use as a template
                    templatePath = targetLevel.grindPaths.FirstOrDefault(p => p.spline != null &&
                                                                       targetLevel.splines.Contains(p.spline));

                    if (templatePath != null)
                    {
                        Console.WriteLine($"✅ Template path {templatePath.id} uses spline {templatePath.spline.id} " +
                                          $"({templatePath.spline.GetVertexCount()} verts)");
                      }
                      else
                      {
                        Console.WriteLine("⚠️ Cannot find a valid template path with proper spline reference");
                        Console.WriteLine("    No new paths will be added.");
                      }
                }

                // Step 2: Copy missing paths
                if (options.HasFlag(GrindPathSwapOptions.CopyMissingPaths))
                {
                    int pathsAdded = 0;
                    int highestId = targetLevel.grindPaths.Count > 0 ? targetLevel.grindPaths.Max(p => p.id) : -1;

                    int pathsToAdd = rc1OltanisLevel.grindPaths.Count - targetLevel.grindPaths.Count;

                    if (pathsToAdd > 0)
                    {
                        Console.WriteLine($"Adding {pathsToAdd} new grind paths from RC1 Oltanis...");
                        for (int i = targetLevel.grindPaths.Count; i < rc1OltanisLevel.grindPaths.Count; i++)
                        {
                            try
                            {
                                var rc1Path = rc1OltanisLevel.grindPaths[i];
                                highestId++;

                                Spline newSpline = null;
                                if (rc1Path.spline != null)
                                {
                                    // Use a high ID space for grind path splines
                                    highestSplineId = Math.Max(highestSplineId, 100);
                                    int newSplineId = ++highestSplineId;

                                    if (rc1Path.spline.GetVertexCount() == 0)
                                    {
                                        Console.WriteLine($"⚠️ Warning: Source spline for path {i} has no vertices, skipping!");
                                        continue;
                                    }

                                    // Create a new spline from the RC1 path's spline
                                    newSpline = CloneSpline(rc1Path.spline, newSplineId);
                                    targetLevel.splines.Add(newSpline);
                                }

                                GrindPath newPath;
                                
                                // If we have a template path, use it for structure consistency
                                if (templatePath != null)
                                {
                                    newPath = DuplicateGrindPath(templatePath, newSpline);
                                    newPath.position = rc1Path.position;
                                    newPath.radius = rc1Path.radius;
                                    newPath.id = highestId;
                                }
                                else
                                {
                                    // No template available - create grind path directly from RC1 data
                                    newPath = CreateGrindPathFromRC1Data(rc1Path, newSpline, highestId);
                                }

                                targetLevel.grindPaths.Add(newPath);
                                pathsAdded++;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Error adding path at index {i}: {ex.Message}");
                            }
                        }
                    }
                    Console.WriteLine($"✅ Added {pathsAdded} new grind paths from RC1 Oltanis");
                }

                // Validate all grind path references to ensure everything is properly linked
                ValidateGrindPathReferences(targetLevel);

                // Add a final verification to check grind path and spline counts
                Console.WriteLine("\n=== Final Grind Path and Spline Verification ===");
                Console.WriteLine($"GrindPath count: {targetLevel.grindPaths.Count}");
                Console.WriteLine($"Distinct spline IDs used: {targetLevel.grindPaths.Where(p => p.spline != null).Select(p => p.spline.id).Distinct().Count()}");
                Console.WriteLine($"Total splines in level: {targetLevel.splines.Count}");

                // Check if any duplicates exist in spline IDs
                var duplicateSplineIds = targetLevel.splines
                    .GroupBy(s => s.id)
                    .Where(g => g.Count() > 1)
                    .ToList();

                if (duplicateSplineIds.Any())
                {
                    Console.WriteLine("⚠️ CRITICAL WARNING: Duplicate spline IDs detected");
                    Console.WriteLine("Fixing duplicate spline IDs to prevent memory corruption on save...");

                    foreach (var group in duplicateSplineIds)
                    {
                        Console.WriteLine($"  Fixing duplicate ID {group.Key} ({group.Count()} occurrences)");

                        // Keep the first one with this ID, change all others
                        bool isFirst = true;
                        foreach (var spline in targetLevel.splines.Where(s => s.id == group.Key).ToList())
                        {
                            if (isFirst)
                            {
                                isFirst = false;
                                continue;
                            }

                            int oldId = spline.id;
                            spline.id = ++highestSplineId;

                            Console.WriteLine($"    Changed spline ID from {oldId} to {spline.id}");
                        }
                    }
                }

                // Check for inconsistent vertex buffer/w-value lengths
                var inconsistentSplines = targetLevel.splines
                    .Where(s => s.GetVertexCount() != s.wVals.Length)
                    .ToList();

                if (inconsistentSplines.Any())
                {
                    Console.WriteLine("⚠️ CRITICAL WARNING: Splines with inconsistent vertex buffer and w-value lengths");
                    Console.WriteLine("Fixing inconsistent splines to prevent crashes...");

                    foreach (var spline in inconsistentSplines)
                    {
                        int vertexCount = spline.GetVertexCount();
                        int wValCount = spline.wVals.Length;

                        Console.WriteLine($"  Fixing spline ID {spline.id} ({vertexCount} vertices, {wValCount} w-values)");

                        // Fix the w-values array to match vertex count
                        float[] newWVals = new float[vertexCount];

                        if (wValCount < vertexCount)
                        {
                            // Copy existing values and extrapolate the rest
                            Array.Copy(spline.wVals, newWVals, Math.Min(wValCount, vertexCount));
                            for (int w = wValCount; w < vertexCount; w++)
                            {
                                newWVals[w] = w > 0 ? newWVals[w - 1] + 0.1f : 0f;
                            }
                        }
                        else
                        {
                            // Just copy what we need
                            Array.Copy(spline.wVals, newWVals, vertexCount);
                        }

                        spline.wVals = newWVals;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Critical error during grind path swapping: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Validates all grind path references in a level and reports any issues
        /// </summary>
        /// <param name="level">Level to validate</param>
        /// <returns>True if validation passes with no errors</returns>
        public static bool ValidateGrindPathReferences(Level level)
        {
            if (level == null || level.grindPaths == null || level.splines == null)
            {
                Console.WriteLine("❌ Cannot validate level: Missing grind paths or splines collections");
                return false;
            }

            Console.WriteLine("\n=== Validating Grind Path References ===");
            bool isValid = true;

            // Check 1: Null spline references
            var pathsWithNullSplines = level.grindPaths.Where(p => p.spline == null).ToList();
            if (pathsWithNullSplines.Count > 0)
            {
                Console.WriteLine($"⚠️ Found {pathsWithNullSplines.Count} grind path(s) with null spline references");
                foreach (var path in pathsWithNullSplines)
                {
                    Console.WriteLine($"  Grind Path ID {path.id} has no associated spline");
                }
            }

            // Check 2: Dangling Spline References - ensure the actual spline object exists in the level's collection
            var pathsWithDanglingSplines = level.grindPaths
                .Where(p => p.spline != null && !level.splines.Contains(p.spline))
                .ToList();

            if (pathsWithDanglingSplines.Count > 0)
            {
                isValid = false;
                Console.WriteLine($"❌ Found {pathsWithDanglingSplines.Count} grind path(s) referencing splines not in the level collection.");
                foreach (var path in pathsWithDanglingSplines)
                {
                    Console.WriteLine($"  Grind Path ID {path.id} references a spline with ID {path.spline.id} that has been removed or was never added to level.splines.");
                }
            }

            // Check 3: Duplicate spline IDs (critical to prevent save-time corruption)
            var duplicateSplineIds = level.splines
                .GroupBy(s => s.id)
                .Where(g => g.Count() > 1)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToList();

            if (duplicateSplineIds.Count > 0)
            {
                isValid = false;
                Console.WriteLine($"❌ Found {duplicateSplineIds.Count} duplicate spline ID(s)");
                foreach (var dup in duplicateSplineIds)
                {
                    Console.WriteLine($"  Spline ID {dup.Id} appears {dup.Count} times");
                }
            }

            // Check 4: Inconsistent vertex buffer/w-value lengths (crucial for preventing crashes)
            var splinesWithMismatchedBuffers = level.splines
                .Where(s => s.GetVertexCount() != s.wVals.Length && s.GetVertexCount() > 0)
                .ToList();

            if (splinesWithMismatchedBuffers.Count > 0)
            {
                isValid = false;
                Console.WriteLine($"❌ Found {splinesWithMismatchedBuffers.Count} spline(s) with mismatched buffer lengths");
                foreach (var spline in splinesWithMismatchedBuffers)
                {
                    Console.WriteLine($"  Spline ID {spline.id} has {spline.GetVertexCount()} vertices but {spline.wVals.Length} w-values");
                }
            }

            if (isValid)
            {
                Console.WriteLine("✅ All grind path references appear valid");
            }

            return isValid;
        }

        /// <summary>
        /// Creates a duplicate of an existing grind path with a different spline
        /// </summary>
        private static GrindPath DuplicateGrindPath(GrindPath sourcePath, Spline newSpline)
        {
            // Create a byte array from the source path
            byte[] sourceBlock = CreateGrindPathBlock(sourcePath);

            // Create a new grind path from the byte array with the new spline
            GrindPath newPath = new GrindPath(sourceBlock, 0, newSpline);

            // Make sure to copy all the properties from the source path
            newPath.wrap = sourcePath.wrap;
            newPath.inactive = sourcePath.inactive;
            newPath.unk0x10 = sourcePath.unk0x10;

            // Update the transform matrix
            newPath.UpdateTransformMatrix();

            return newPath;
        }

        /// <summary>
        /// Interactive wrapper for grind path swapping
        /// </summary>
        /// <returns>True if the operation was successful</returns>
        public static bool SwapGrindPathsWithRC1OltanisInteractive()
        {
            Console.WriteLine("\n==== Swap RC2 Grind Paths with RC1 Oltanis Grind Paths ====");

            if (!ALLOW_ADDING_NEW_PATHS)
            {
                Console.WriteLine("⚠️ NOTE: Adding new paths is disabled by ALLOW_ADDING_NEW_PATHS flag");
            }

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
                Console.WriteLine($"\nLoading target level: {Path.GetFileName(targetPath)}...");
                targetLevel = new Level(targetPath);
                Console.WriteLine($"✅ Successfully loaded target level");

                Console.WriteLine($"\nLoading RC1 Oltanis level: {Path.GetFileName(rc1OltanisPath)}...");
                rc1OltanisLevel = new Level(rc1OltanisPath);
                Console.WriteLine($"✅ Successfully loaded RC1 Oltanis level");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading levels: {ex.Message}");
                return false;
            }

            // Option selection
            Console.WriteLine("\nSelect swap options:");
            Console.WriteLine("1. Reposition existing grind paths only (default)");

            if (ALLOW_ADDING_NEW_PATHS)
            {
                Console.WriteLine("2. Full replacement (reposition existing + add missing paths)");
                Console.WriteLine("3. Custom options");
            }
            else
            {
                Console.WriteLine("2. [DISABLED] Full replacement");
                Console.WriteLine("3. [DISABLED] Custom options");
            }

            Console.Write("> ");
            string choice = Console.ReadLine()?.Trim() ?? "1";
            GrindPathSwapOptions options;

            if (ALLOW_ADDING_NEW_PATHS)
            {
                switch (choice)
                {
                    case "2":
                        options = GrindPathSwapOptions.FullReplacement;
                        break;
                    case "3":
                        options = GetCustomOptions();
                        break;
                    case "1":
                    default:
                        options = GrindPathSwapOptions.PositionOnly;
                        break;
                }
            }
            else
            {
                // When adding paths is disabled, always use PositionOnly
                options = GrindPathSwapOptions.PositionOnly;
                Console.WriteLine("Using 'Position only' option as adding paths is disabled");
            }

            bool success = SwapGrindPathsWithRC1Oltanis(targetLevel, rc1OltanisLevel, options);

            if (success)
            {
                // Run validation before saving
                Console.WriteLine("\nValidating grind path references...");
                bool validationResult = ValidateGrindPathReferences(targetLevel);

                if (!validationResult)
                {
                    Console.WriteLine("\n⚠️ Validation detected issues with grind path references.");
                    Console.Write("Would you still like to save the changes? (y/n): ");
                    if (Console.ReadLine()?.Trim().ToLower() != "y")
                    {
                        Console.WriteLine("Changes not saved.");
                        return false;
                    }
                }

                // Ask if the user wants to save
                Console.Write("\nSave changes to the target level? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLower() == "y")
                {
                    try
                    {
                        // Ask if they want to skip validation for testing purposes
                        bool skipValidation = false;
                        Console.Write("\nSkip validation for testing purposes? (Only use if you know what you're doing) (y/n): ");
                        if (Console.ReadLine()?.Trim().ToLower() == "y")
                        {
                            skipValidation = true;
                            Console.WriteLine("⚠️ WARNING: Validation will be skipped. This may cause save errors but is useful for testing.");
                        }

                        // Final verification of spline references
                        Console.WriteLine("\n=== Final Grind Path Validation ===");
                        foreach (var path in targetLevel.grindPaths)
                        {
                            bool splineExists = path.spline != null && targetLevel.splines.Contains(path.spline);
                            int vertexCount = path.spline?.GetVertexCount() ?? -1;
                            Console.WriteLine($"Path {path.id} → Spline ID {path.spline?.id ?? -1} → Exists: {splineExists} | Vertices: {vertexCount}");
                        }

                        // Use the grind path-specific save method that handles alignment properly
                        SaveLevelWithProperGrindPathAlignment(targetLevel, targetPath, skipValidation);
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
        /// Helper method to ensure proper 0x80 alignment when saving grind paths and splines
        /// </summary>
        /// <param name="level">The level to save</param>
        /// <param name="outputPath">Path to save to</param>
        /// <param name="skipValidation">Optional parameter to skip validation and cleanup</param>
        private static void SaveLevelWithProperGrindPathAlignment(Level level, string outputPath, bool skipValidation = false)
        {
            try
            {
                Console.WriteLine("Saving level with proper grind path alignment...");

                // Verify all grind path splines exist before saving
                Console.WriteLine("\n=== PRE-SAVE VERIFICATION ===");

                if (skipValidation)
                {
                    Console.WriteLine("⚠️ VALIDATION SKIPPED: Preserving all grind paths regardless of validity");
                    Console.WriteLine("This may cause issues during save, but can be useful for testing");
                }
                else
                {
                    int validPaths = 0, invalidPaths = 0;

                    // Fix: Remove any grind paths with null spline references before saving
                    bool needsCleanup = false;
                    foreach (var path in level.grindPaths.ToList())
                    {
                        if (path.spline == null)
                        {
                            Console.WriteLine($"⚠️ Found grind path ID {path.id} with null spline reference - will remove before saving");
                            level.grindPaths.Remove(path);
                            needsCleanup = true;
                            invalidPaths++;
                            continue;
                        }

                        if (!level.splines.Contains(path.spline))
                        {
                            Console.WriteLine($"⚠️ Found grind path ID {path.id} referencing a spline not in the level - will remove before saving");
                            level.grindPaths.Remove(path);
                            needsCleanup = true;
                            invalidPaths++;
                            continue;
                        }

                        // Also verify the vertex buffer and w-values are consistent
                        if (path.spline.GetVertexCount() != path.spline.wVals.Length)
                        {
                            Console.WriteLine($"⚠️ Fixing mismatched vertex/W values in spline {path.spline.id}");

                            int vertexCount = path.spline.GetVertexCount();
                            float[] newWVals = new float[vertexCount];

                            if (path.spline.wVals.Length < vertexCount)
                            {
                                // Copy existing values
                                Array.Copy(path.spline.wVals, newWVals, path.spline.wVals.Length);
                                for (int w = path.spline.wVals.Length; w < vertexCount; w++)
                                {
                                    newWVals[w] = w > 0 ? newWVals[w - 1] + 0.1f : 0f;
                                }
                            }
                            else
                            {
                                // Copy just what we need
                                Array.Copy(path.spline.wVals, newWVals, vertexCount);
                            }

                            path.spline.wVals = newWVals;
                        }

                        validPaths++;
                    }

                    if (needsCleanup)
                    {
                        Console.WriteLine($"⚠️ Removed {invalidPaths} invalid grind paths to prevent save errors");
                    }

                    Console.WriteLine($"Valid path-spline connections: {validPaths}");
                    Console.WriteLine($"Invalid path-spline connections removed: {invalidPaths}");

                    // Check for duplicate spline IDs and fix them
                    var duplicateIds = level.splines
                        .GroupBy(s => s.id)
                        .Where(g => g.Count() > 1)
                        .ToList();

                    if (duplicateIds.Any())
                    {
                        Console.WriteLine($"⚠️ Found {duplicateIds.Count} splines with duplicate IDs. Fixing...");

                        int highestId = level.splines.Max(s => s.id);

                        foreach (var group in duplicateIds)
                        {
                            bool first = true;
                            foreach (var spline in level.splines.Where(s => s.id == group.Key).ToList())
                            {
                                if (first)
                                {
                                    first = false;
                                    continue; // Skip the first one
                                }

                                // Assign a new unique ID
                                int oldId = spline.id;
                                spline.id = ++highestId;
                                Console.WriteLine($"  Changed spline ID {oldId} to {spline.id}");
                            }
                        }
                    }
                }

                // Now save the level
                level.Save(outputPath);

                Console.WriteLine("✅ Level saved with proper grind path alignment");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in SaveLevelWithProperGrindPathAlignment: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                throw;  // Re-throw to handle at the higher level
            }
        }

        /// <summary>
        /// Helper method to get custom options
        /// </summary>
        private static GrindPathSwapOptions GetCustomOptions()
        {
            GrindPathSwapOptions options = GrindPathSwapOptions.None;

            Console.WriteLine("\nCustomize swap options:");

            if (GetYesNoInput("Reposition existing grind paths to match RC1 positions? (y/n): "))
                options |= GrindPathSwapOptions.UseRC1Positions;

            if (ALLOW_ADDING_NEW_PATHS && GetYesNoInput("Add missing grind paths from RC1 Oltanis? (y/n): "))
                options |= GrindPathSwapOptions.CopyMissingPaths;

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
        /// Creates a complete clone of a spline with a new ID
        /// </summary>
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
            Spline newSpline = new Spline(newId, newVertexBuffer);
            newSpline.wVals = newWVals;

            // Copy position, rotation and scale
            newSpline.position = sourceSpline.position;
            newSpline.rotation = sourceSpline.rotation;
            newSpline.scale = sourceSpline.scale;
            newSpline.reflection = sourceSpline.reflection;

            newSpline.UpdateTransformMatrix();

            Console.WriteLine($"✅ Created new spline with ID {newId} with {newSpline.GetVertexCount()} vertices");

            return newSpline;
        }

        /// <summary>
        /// Creates a byte array for a single grind path in the format expected by the GrindPath constructor
        /// </summary>
        private static byte[] CreateGrindPathBlock(GrindPath sourcePath)
        {
            // Create a MemoryStream to build the complete block
            using (var block = new MemoryStream())
            {
                // Get the source path data
                byte[] sourceData = sourcePath.ToByteArray();

                // Write the data to the block
                block.Write(sourceData, 0, sourceData.Length);

                // Ensure the block is aligned to GrindPath.ELEMENTSIZE
                while (block.Length < GrindPath.ELEMENTSIZE)
                {
                    block.WriteByte(0);
                }

                // Align to 0x80 for proper RC2 format compatibility
                while (block.Length % 0x80 != 0)
                {
                    block.WriteByte(0);
                }

                // Return the full block as a byte array
                return block.ToArray();
            }
        }

        /// <summary>
        /// Creates a new grind path directly from RC1 data when no template is available
        /// </summary>
        private static GrindPath CreateGrindPathFromRC1Data(GrindPath rc1Path, Spline newSpline, int newId)
        {
            // Create a minimal byte block for the new grind path
            byte[] pathBlock = new byte[GrindPath.ELEMENTSIZE];
            
            // Write the RC1 path data to the block
            WriteFloat(pathBlock, 0x00, rc1Path.position.X);
            WriteFloat(pathBlock, 0x04, rc1Path.position.Y);
            WriteFloat(pathBlock, 0x08, rc1Path.position.Z);
            WriteFloat(pathBlock, 0x0C, rc1Path.radius);
            WriteInt(pathBlock, 0x10, rc1Path.unk0x10);
            WriteInt(pathBlock, 0x14, rc1Path.wrap);
            WriteInt(pathBlock, 0x18, rc1Path.inactive);
            
            // Create the new grind path
            GrindPath newPath = new GrindPath(pathBlock, 0, newSpline);
            newPath.id = newId;
            
            // Update the transform matrix
            newPath.UpdateTransformMatrix();
            
            Console.WriteLine($"✅ Created grind path {newId} directly from RC1 data at {FormatVector3(newPath.position)}");
            
            return newPath;
        }

        /// <summary>
        /// Helper method to format Vector3 for console output
        /// </summary>
        private static string FormatVector3(Vector3 vec)
        {
            return $"({vec.X:F2}, {vec.Y:F2}, {vec.Z:F2})";
        }
    }
}
