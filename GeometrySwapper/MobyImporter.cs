using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using LibReplanetizer.Models.Animations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles importing mobys from reference RC1/RC2/RC3 levels into a target level
    /// Enhanced to support RC1, RC2, and RC3 source levels with grind path preservation
    /// </summary>
    public static class MobyImporter
    {
        /// <summary>
        /// Determines which mobys will be imported when pulling from multiple
        /// reference levels.
        /// </summary>
        public enum ImportSelectionMode
        {
            All,
            RC3Only,
            RC2Only,
            RC1Only,
            MultipleVersions,
            Unique,
            Manual
        }
        // Class to store moby data for comparison across levels
        private class MobyIdentifier
        {
            public int ModelId { get; set; }
            public string ModelName { get; set; }
            public int AnimationCount { get; set; }
            public Level SourceLevel { get; set; }
            public string LevelName => Path.GetFileNameWithoutExtension(SourceLevel.path);
            public MobyModel SourceModel { get; set; }
            public List<Animation> Animations => SourceModel?.animations ?? new List<Animation>();
            public GameType SourceGameType => SourceLevel.game;
            public bool IsRC1Source => SourceGameType.num == 1;
            public bool IsRC2Source => SourceGameType.num == 2;
            public bool IsRC3Source => SourceGameType.num == 3;
            public string GameTypeName => SourceGameType.num switch
            {
                1 => "RC1",
                2 => "RC2", 
                3 => "RC3",
                4 => "Deadlocked",
                _ => "Unknown"
            };

            public bool HasAnimations => AnimationCount > 0;

            public MobyIdentifier(int modelId, MobyModel model, Level level)
            {
                ModelId = modelId;
                ModelName = GetFriendlyName(modelId);
                AnimationCount = model?.animations?.Count ?? 0;
                SourceLevel = level;
                SourceModel = model;
            }

            private string GetFriendlyName(int modelId)
            {
                // Try to find in the MobyTypes dictionary first
                foreach (var entry in MobySwapper.MobyTypes)
                {
                    if (entry.Value.Contains(modelId))
                    {
                        return entry.Key;
                    }
                }

                // If not found, just use the model ID
                return $"Model {modelId}";
            }
        }

        // Represents a moby that appears in multiple reference levels
        private class CommonMoby
        {
            public int ModelId { get; set; }
            public string ModelName { get; set; }
            public List<MobyIdentifier> Instances { get; set; } = new List<MobyIdentifier>();
            public int ReferenceCount => Instances.Count;
            public bool HasAnimations => Instances.Any(i => i.HasAnimations);
            public bool HasRC1Sources => Instances.Any(i => i.IsRC1Source);
            public bool HasRC2Sources => Instances.Any(i => i.IsRC2Source);
            public bool HasRC3Sources => Instances.Any(i => i.IsRC3Source);
            public MobyIdentifier BestInstance => GetBestInstance();

            public CommonMoby(int modelId, string name)
            {
                ModelId = modelId;
                ModelName = name;
            }

            public void AddInstance(MobyIdentifier instance)
            {
                if (!Instances.Any(i => i.SourceLevel == instance.SourceLevel))
                {
                    Instances.Add(instance);
                }
            }

            // Get the best instance (prioritize RC3 with animations, then RC2 with animations, then RC1 with animations, then any RC3, then any RC2, then any RC1)
            private MobyIdentifier GetBestInstance()
            {
                // First preference: RC3 with animations
                var rc3WithAnimations = Instances.FirstOrDefault(i => i.IsRC3Source && i.HasAnimations);
                if (rc3WithAnimations != null)
                    return rc3WithAnimations;

                // Second preference: RC2 with animations
                var rc2WithAnimations = Instances.FirstOrDefault(i => i.IsRC2Source && i.HasAnimations);
                if (rc2WithAnimations != null)
                    return rc2WithAnimations;

                // Third preference: RC1 with animations
                var rc1WithAnimations = Instances.FirstOrDefault(i => i.IsRC1Source && i.HasAnimations);
                if (rc1WithAnimations != null)
                    return rc1WithAnimations;

                // Fourth preference: Any RC3
                var anyRC3 = Instances.FirstOrDefault(i => i.IsRC3Source);
                if (anyRC3 != null)
                    return anyRC3;

                // Fifth preference: Any RC2
                var anyRC2 = Instances.FirstOrDefault(i => i.IsRC2Source);
                if (anyRC2 != null)
                    return anyRC2;

                // Last resort: Any RC1
                return Instances.FirstOrDefault();
            }
        }

        /// <summary>
        /// Import mobys from multiple reference levels (RC1, RC2, or RC3) into a target level
        /// Enhanced with grind path preservation and restoration. RC1 pVar blocks are expanded
        /// to 0x80 bytes for compatibility with later games.
        /// </summary>
        /// <param name="targetLevel">The target level to import mobys into</param>
        /// <param name="referenceEnginePaths">Paths to reference engine.ps3 files (can be mixed RC1/RC2/RC3)</param>
        /// <param name="allowOverwrite">Whether to overwrite existing mobys</param>
        /// <param name="preserveGrindPaths">Whether to preserve and restore grind paths (default: true)</param>
        /// <param name="selectionMode">Selection strategy for which mobys to import</param>
        /// <param name="manualModelIds">Optional list of model IDs to import when using Manual mode</param>
        /// <returns>True if the operation was successful</returns>
        public static bool ImportMobysFromMixedReferenceLevels(
            Level targetLevel,
            List<string> referenceEnginePaths,
            bool allowOverwrite = false,
            bool preserveGrindPaths = true,
            ImportSelectionMode selectionMode = ImportSelectionMode.All,
            List<int>? manualModelIds = null)
        {
            if (targetLevel == null || referenceEnginePaths == null || referenceEnginePaths.Count < 1)
            {
                Console.WriteLine("❌ Cannot import mobys: Invalid parameters");
                return false;
            }

            Console.WriteLine("\n==== Importing Mobys from Mixed Reference Levels (RC1/RC2/RC3) ====");

            // 🔧 STEP 1: Backup grind paths and splines before import
            List<GrindPath> originalGrindPaths = null;
            List<Spline> originalSplines = null;
            Level grindPathSourceLevel = null;

            if (preserveGrindPaths)
            {
                Console.WriteLine("\n🛡️ Backing up existing grind paths and splines...");
                originalGrindPaths = BackupGrindPaths(targetLevel);
                originalSplines = BackupSplines(targetLevel);
                
                // Try to find an RC1 source level for grind path restoration
                grindPathSourceLevel = FindRC1SourceLevel(referenceEnginePaths);
                
                if (grindPathSourceLevel != null)
                {
                    Console.WriteLine($"Found RC1 source level for grind path restoration: {Path.GetFileName(grindPathSourceLevel.path)}");
                }
            }

            // 1. Load all reference levels
            List<Level> referenceLevels = new List<Level>();
            int rc1Count = 0, rc2Count = 0, rc3Count = 0;

            foreach (string enginePath in referenceEnginePaths)
            {
                try
                {
                    Console.WriteLine($"Loading reference level: {Path.GetFileName(enginePath)}...");
                    Level level = new Level(enginePath);

                    // RC1 levels store pVars in shorter blocks; expand them to full 0x80-byte blocks
                    // so that converted mobys have a consistent pVar layout when attached.
                    if (level.game.num == 1)
                    {
                        EnsureFullPvarBlocks(level.pVars);
                    }

                    referenceLevels.Add(level);

                    switch (level.game.num)
                    {
                        case 1:
                            rc1Count++;
                            Console.WriteLine($"✅ Successfully loaded RC1 level with {level.mobyModels?.Count ?? 0} moby models");
                            break;
                        case 2:
                            rc2Count++;
                            Console.WriteLine($"✅ Successfully loaded RC2 level with {level.mobyModels?.Count ?? 0} moby models");
                            break;
                        case 3:
                            rc3Count++;
                            Console.WriteLine($"✅ Successfully loaded RC3 level with {level.mobyModels?.Count ?? 0} moby models");
                            break;
                        default:
                            Console.WriteLine($"✅ Successfully loaded unknown game level with {level.mobyModels?.Count ?? 0} moby models");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error loading reference level {enginePath}: {ex.Message}");
                }
            }

            if (referenceLevels.Count < 1)
            {
                Console.WriteLine("❌ Failed to load any reference levels");
                return false;
            }

            Console.WriteLine($"Loaded {rc1Count} RC1 levels, {rc2Count} RC2 levels, and {rc3Count} RC3 levels");

            // 2. Analyze mobys across all levels to find available ones
            Dictionary<int, CommonMoby> allMobys = new Dictionary<int, CommonMoby>();

            Console.WriteLine("\nAnalyzing mobys across reference levels...");

            foreach (Level level in referenceLevels)
            {
                if (level.mobyModels == null) continue;

                foreach (Model model in level.mobyModels)
                {
                    if (model == null || !(model is MobyModel mobyModel)) continue;

                    int modelId = model.id;
                    var identifier = new MobyIdentifier(modelId, mobyModel, level);

                    if (!allMobys.ContainsKey(modelId))
                    {
                        allMobys[modelId] = new CommonMoby(modelId, identifier.ModelName);
                    }

                    allMobys[modelId].AddInstance(identifier);
                }
            }

            Console.WriteLine($"Found {allMobys.Count} unique moby models across all reference levels");
            
            // Show breakdown by source type
            var rc1OnlyModels = allMobys.Values.Where(m => m.HasRC1Sources && !m.HasRC2Sources && !m.HasRC3Sources).Count();
            var rc2OnlyModels = allMobys.Values.Where(m => !m.HasRC1Sources && m.HasRC2Sources && !m.HasRC3Sources).Count();
            var rc3OnlyModels = allMobys.Values.Where(m => !m.HasRC1Sources && !m.HasRC2Sources && m.HasRC3Sources).Count();
            var mixedModels = allMobys.Values.Where(m => (m.HasRC1Sources ? 1 : 0) + (m.HasRC2Sources ? 1 : 0) + (m.HasRC3Sources ? 1 : 0) > 1).Count();
            
            Console.WriteLine($"  - RC1 only: {rc1OnlyModels} models");
            Console.WriteLine($"  - RC2 only: {rc2OnlyModels} models");
            Console.WriteLine($"  - RC3 only: {rc3OnlyModels} models");
            Console.WriteLine($"  - Available in multiple games: {mixedModels} models");

            // 3. Determine which mobys to import based on selection mode
            List<CommonMoby> mobysToImport = selectionMode switch
            {
                ImportSelectionMode.RC3Only => allMobys.Values.Where(m => m.HasRC3Sources).ToList(),
                ImportSelectionMode.RC2Only => allMobys.Values.Where(m => m.HasRC2Sources).ToList(),
                ImportSelectionMode.RC1Only => allMobys.Values.Where(m => m.HasRC1Sources).ToList(),
                ImportSelectionMode.MultipleVersions => allMobys.Values.Where(m =>
                    (m.HasRC1Sources ? 1 : 0) + (m.HasRC2Sources ? 1 : 0) + (m.HasRC3Sources ? 1 : 0) > 1).ToList(),
                ImportSelectionMode.Unique => allMobys.Values.Where(m => m.ReferenceCount == 1).ToList(),
                ImportSelectionMode.Manual => (manualModelIds != null)
                    ? allMobys.Values.Where(m => manualModelIds.Contains(m.ModelId)).ToList()
                    : new List<CommonMoby>(),
                _ => allMobys.Values.ToList()
            };

            Console.WriteLine($"Selected {mobysToImport.Count} mobys for import using mode {selectionMode}");

            // 4. Check for conflicts with existing mobys in the target level
            HashSet<int> targetModelIds = new HashSet<int>();
            HashSet<int> specialMobyIds = GetSpecialMobyIds();

            if (targetLevel.mobyModels != null)
            {
                foreach (var model in targetLevel.mobyModels)
                {
                    if (model != null)
                    {
                        targetModelIds.Add(model.id);
                    }
                }
            }

            // Filter out special mobys that are managed by MobySwapper to avoid conflicts
            mobysToImport = mobysToImport
                .Where(m => !specialMobyIds.Contains(m.ModelId))
                .ToList();

            if (specialMobyIds.Count > 0)
            {
                Console.WriteLine($"Filtered out {specialMobyIds.Count} special mobys that are managed by the Special Moby Swapper");
            }

            List<CommonMoby> conflictingMobys = mobysToImport
                .Where(m => targetModelIds.Contains(m.ModelId))
                .ToList();

            if (conflictingMobys.Count > 0)
            {
                Console.WriteLine($"\nFound {conflictingMobys.Count} mobys that already exist in the target level:");
                foreach (var moby in conflictingMobys.Take(10))
                {
                    Console.WriteLine($"- {moby.ModelName} (ID: {moby.ModelId})");
                }

                if (conflictingMobys.Count > 10)
                {
                    Console.WriteLine($"- ... and {conflictingMobys.Count - 10} more");
                }

                if (!allowOverwrite)
                {
                    Console.WriteLine("Skipping mobys that already exist in the target level");
                    mobysToImport = mobysToImport
                        .Where(m => !targetModelIds.Contains(m.ModelId))
                        .ToList();
                }
            }

            // 5. Perform the import with RC1/RC2/RC3 compatibility
            bool importSuccess = PerformEnhancedMobyImport(targetLevel, mobysToImport, allowOverwrite);

            // 🔧 STEP 2: Restore grind paths after moby import
            if (preserveGrindPaths && importSuccess)
            {
                Console.WriteLine("\n🛡️ Restoring grind paths after moby import...");
                RestoreGrindPaths(targetLevel, originalGrindPaths, originalSplines, grindPathSourceLevel);
            }

            return importSuccess;
        }

        /// <summary>
        /// Backup existing grind paths from the target level
        /// </summary>
        private static List<GrindPath> BackupGrindPaths(Level level)
        {
            if (level.grindPaths == null || level.grindPaths.Count == 0)
            {
                Console.WriteLine("  No grind paths to backup");
                return new List<GrindPath>();
            }

            Console.WriteLine($"  Backed up {level.grindPaths.Count} grind paths");
            return level.grindPaths.ToList(); // Create a copy
        }

        /// <summary>
        /// Backup existing splines from the target level
        /// </summary>
        private static List<Spline> BackupSplines(Level level)
        {
            if (level.splines == null || level.splines.Count == 0)
            {
                Console.WriteLine("  No splines to backup");
                return new List<Spline>();
            }

            Console.WriteLine($"  Backed up {level.splines.Count} splines");
            return level.splines.ToList(); // Create a copy
        }

        /// <summary>
        /// Find an RC1 source level from the reference levels for grind path restoration
        /// </summary>
        private static Level FindRC1SourceLevel(List<string> referenceEnginePaths)
        {
            foreach (string enginePath in referenceEnginePaths)
            {
                try
                {
                    Level level = new Level(enginePath);
                    if (level.game.num == 1 && level.grindPaths != null && level.grindPaths.Count > 0)
                    {
                        return level;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Error checking {enginePath} for grind paths: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Restore grind paths after moby import using the GrindPathSwapper
        /// FIXED: Properly integrates grind path splines into the main level spline collection
        /// </summary>
        private static void RestoreGrindPaths(Level targetLevel, List<GrindPath> originalGrindPaths, List<Spline> originalSplines, Level grindPathSourceLevel)
        {
            try
            {
                if (originalGrindPaths == null || originalGrindPaths.Count == 0)
                {
                    Console.WriteLine("  No original grind paths to restore");

                    // If we have an RC1 source level, try to create grind paths from it
                    if (grindPathSourceLevel != null)
                    {
                        Console.WriteLine("  Attempting to create grind paths from RC1 source level...");
                        bool created = GrindPathSwapper.SwapGrindPathsWithRC1Oltanis(
                            targetLevel,
                            grindPathSourceLevel,
                            GrindPathSwapper.GrindPathSwapOptions.FullReplacement);

                        if (created)
                        {
                            Console.WriteLine("  ✅ Successfully created grind paths from RC1 source");
                        }
                        else
                        {
                            Console.WriteLine("  ⚠️ Failed to create grind paths from RC1 source");
                        }
                    }
                    return;
                }

                Console.WriteLine($"  Restoring {originalGrindPaths.Count} grind paths and {originalSplines?.Count ?? 0} splines...");

                // 🔧 CRITICAL FIX: Create new splines in the main level collection
                // Clear any existing grind paths
                targetLevel.grindPaths?.Clear();
                if (targetLevel.grindPaths == null) targetLevel.grindPaths = new List<GrindPath>();

                // Initialize splines if null
                if (targetLevel.splines == null) targetLevel.splines = new List<Spline>();

                // Find the highest spline ID to avoid conflicts
                int highestSplineId = targetLevel.splines.Count > 0 ? targetLevel.splines.Max(s => s.id) : 0;

                // Restore grind paths and integrate their splines into the main collection
                for (int i = 0; i < originalGrindPaths.Count; i++)
                {
                    var originalPath = originalGrindPaths[i];

                    if (originalPath.spline != null)
                    {
                        // Create a new spline with a unique ID and add it to the main level collection
                        Spline newSpline = CloneSplineWithNewId(originalPath.spline, ++highestSplineId);
                        targetLevel.splines.Add(newSpline);

                        // Create a new grind path that references the spline in the main collection
                        GrindPath newPath = CloneGrindPathWithNewSpline(originalPath, newSpline);
                        targetLevel.grindPaths.Add(newPath);

                        Console.WriteLine($"    Restored grind path {newPath.id} with spline {newSpline.id} (added to main splines collection)");
                    }
                    else
                    {
                        Console.WriteLine($"    Skipping grind path {originalPath.id} - no spline reference");
                    }
                }

                // Validate the restored grind paths
                Console.WriteLine("  Validating restored grind paths...");
                bool isValid = GrindPathSwapper.ValidateGrindPathReferences(targetLevel);

                if (isValid)
                {
                    Console.WriteLine("  ✅ Grind path restoration successful");
                }
                else
                {
                    Console.WriteLine("  ⚠️ Grind path validation failed after restoration");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Error during grind path restoration: {ex.Message}");

                // Fallback: Try to create new grind paths if we have an RC1 source
                if (grindPathSourceLevel != null)
                {
                    Console.WriteLine("  Attempting fallback grind path creation...");
                    try
                    {
                        GrindPathSwapper.SwapGrindPathsWithRC1Oltanis(
                            targetLevel,
                            grindPathSourceLevel,
                            GrindPathSwapper.GrindPathSwapOptions.FullReplacement);
                    }
                    catch (Exception fallbackEx)
                    {
                        Console.WriteLine($"  ❌ Fallback grind path creation failed: {fallbackEx.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Creates a clone of a spline with a new ID
        /// </summary>
        private static Spline CloneSplineWithNewId(Spline sourceSpline, int newId)
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

            return newSpline;
        }

        /// <summary>
        /// Creates a clone of a grind path with a new spline reference
        /// </summary>
        private static GrindPath CloneGrindPathWithNewSpline(GrindPath sourcePath, Spline newSpline)
        {
            // Create a copy of the grind path data
            byte[] sourceData = sourcePath.ToByteArray();

            // Create new grind path with the new spline
            GrindPath newPath = new GrindPath(sourceData, 0, newSpline);

            // Copy all properties
            newPath.id = sourcePath.id;
            newPath.position = sourcePath.position;
            newPath.radius = sourcePath.radius;
            newPath.wrap = sourcePath.wrap;
            newPath.inactive = sourcePath.inactive;
            newPath.unk0x10 = sourcePath.unk0x10;

            newPath.UpdateTransformMatrix();

            return newPath;
        }

        /// <summary>
        /// Ensures every pVar block is at least 0x80 bytes long by padding with zeros.
        /// RC1 stores shorter pVar blocks which need to be expanded for RC2/RC3 compatibility.
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
                    Array.Resize(ref block, 0x80);
                    pvars[i] = block;
                }
            }
        }

        /// <summary>
        /// Mimics the engine lookup for a pVar pointer.
        /// Returns null if the index is out of range or the level has no pVars.
        /// </summary>
        private static byte[]? sub_B5C054(Level level, int pvarIndex)
        {
            if (level?.pVars == null || pvarIndex < 0 || pvarIndex >= level.pVars.Count)
            {
                return null;
            }
            return level.pVars[pvarIndex];
        }

        /// <summary>
        /// Enhanced import process that handles RC1, RC2, and RC3 source models with grind path preservation
        /// </summary>
        private static bool PerformEnhancedMobyImport(Level targetLevel, List<CommonMoby> mobysToImport, bool allowOverwrite)
        {
            Dictionary<string, int> globalTextureMap = new Dictionary<string, int>();
            int importedCount = 0;
            int skippedCount = 0;
            int texturesImported = 0;
            List<int> successfullyImportedModelIds = new List<int>();

            Console.WriteLine("\nImporting selected mobys to target level...");

            foreach (var moby in mobysToImport)
            {
                try
                {
                    // Get the best instance (preferably RC3 with animations, then RC2, then RC1)
                    MobyIdentifier bestInstance = moby.BestInstance;
                    if (bestInstance == null || bestInstance.SourceModel == null)
                    {
                        Console.WriteLine($"⚠️ No valid instance found for {moby.ModelName} (ID: {moby.ModelId})");
                        skippedCount++;
                        continue;
                    }

                    Console.WriteLine($"\nImporting {moby.ModelName} (ID: {moby.ModelId}) from {bestInstance.GameTypeName} level: {bestInstance.LevelName}");

                    // Import the model with RC1/RC2/RC3 compatibility
                    MobyModel sourceModel = bestInstance.SourceModel;
                    Level sourceLevel = bestInstance.SourceLevel;

                    MobyModel clonedModel;
                    Moby? rc1ConvertedMoby = null;

                    if (bestInstance.IsRC1Source)
                    {
                        // Handle RC1 model import - may need conversion
                        clonedModel = ImportRC1MobyModel(sourceModel, sourceLevel, targetLevel, out rc1ConvertedMoby);
                        Console.WriteLine($"  ✅ Converted RC1 model to target format");

                        // Sanity check: ensure pVar pointer resolves after conversion
                        if (rc1ConvertedMoby != null && sub_B5C054(targetLevel, rc1ConvertedMoby.pvarIndex) == null)
                        {
                            Console.WriteLine($"  ⚠️ Invalid pVarIndex {rc1ConvertedMoby.pvarIndex} after conversion");
                        }
                    }
                    else if (bestInstance.IsRC2Source)
                    {
                        // Handle RC2 model import - standard process for newer formats
                        clonedModel = ImportRC2MobyModel(sourceModel, sourceLevel, targetLevel);
                        Console.WriteLine($"  ✅ Imported RC2 model");
                    }
                    else if (bestInstance.IsRC3Source)
                    {
                        // Handle RC3 model import - newest format, standard process
                        clonedModel = ImportRC3MobyModel(sourceModel, sourceLevel, targetLevel);
                        Console.WriteLine($"  ✅ Imported RC3 model");
                    }
                    else
                    {
                        // Fallback for unknown game types
                        clonedModel = (MobyModel)MobySwapper.DeepCloneModel(sourceModel);
                        Console.WriteLine($"  ✅ Cloned model from {bestInstance.GameTypeName}");
                    }

                    clonedModel.id = sourceModel.id; // Preserve original ID

                    // Handle texture dependencies with multi-game compatibility
                    Dictionary<int, int> textureMapping = new Dictionary<int, int>();
                    var textureImportResult = ImportModelTextures(targetLevel, sourceLevel, clonedModel, globalTextureMap, bestInstance);
                    textureMapping = textureImportResult.mapping;
                    texturesImported += textureImportResult.imported;

                    // Remove any existing model with the same ID if overwriting
                    if (allowOverwrite)
                    {
                        targetLevel.mobyModels.RemoveAll(m => m != null && m.id == moby.ModelId);
                    }

                    // Add the new model to the target level
                    targetLevel.mobyModels.Add(clonedModel);
                    successfullyImportedModelIds.Add(clonedModel.id);

                    Console.WriteLine($"✅ Imported {moby.ModelName} (ID: {moby.ModelId}) successfully");
                    if (moby.HasAnimations)
                    {
                        Console.WriteLine($"  - With {bestInstance.AnimationCount} animations");
                    }

                    importedCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error importing {moby.ModelName} (ID: {moby.ModelId}): {ex.Message}");
                    skippedCount++;
                }
            }

            // Print summary
            Console.WriteLine($"\n==== Import Summary ====");
            Console.WriteLine($"✅ Successfully imported: {importedCount} moby models");
            Console.WriteLine($"🖼️ Textures imported: {texturesImported}");
            Console.WriteLine($"⏭️ Skipped: {skippedCount} moby models");

            if (importedCount > 0)
            {
                // After successful import, validate and fix PVAR indices
                MobySwapper.ValidateAndFixPvarIndices(targetLevel);

                // Validate group ID conflicts
                ValidateAndFixGroupIndexConflicts(targetLevel, false);

                // Ask user if they want to create instances of the imported mobys
                if (successfullyImportedModelIds.Count > 0)
                {
                    Console.WriteLine("\nWould you like to create instances of the newly imported mobys using source level positions? (y/n)");
                    Console.Write("> ");
                    if (Console.ReadLine()?.Trim().ToLower() == "y")
                    {
                        bool instancesCreated = CreateInstancesForImportedMobysEnhanced(targetLevel, successfullyImportedModelIds, mobysToImport);
                        
                        // Note: Grind paths will be restored after this method returns
                        return instancesCreated;
                    }
                }
            }

            return importedCount > 0;
        }

        /// <summary>
        /// Import RC1 moby model and convert it to target format
        /// </summary>
        private static MobyModel ImportRC1MobyModel(MobyModel rc1Model, Level rc1Level, Level targetLevel, out Moby? convertedMoby)
        {
            // The conversion logic has been moved to a dedicated helper so we can
            // reuse it when converting individual moby instances as well.
            return ConvertRc1ModelToRc3(rc1Model, rc1Level, targetLevel, out convertedMoby);
        }

        /// <summary>
        /// Converts an RC1 moby model to an RC3 compatible model by creating a
        /// temporary RC3 moby and copying all relevant fields.
        /// </summary>
        private static MobyModel ConvertRc1ModelToRc3(MobyModel rc1Model, Level rc1Level, Level targetLevel, out Moby? convertedMoby)
        {
            convertedMoby = null;
            if (rc1Model == null)
            {
                throw new ArgumentNullException(nameof(rc1Model));
            }

            // Clone the RC1 model so we don't modify the source level data
            var rc3Model = (MobyModel)MobySwapper.DeepCloneModel(rc1Model);

            // --- Class header conversion ---
            // RC1 models embed the behaviour table pointer in the model header.
            // RC2/RC3 expect the table to live in a separate list and store a
            // pointer in the moby instance (and model).  We create a minimal
            // GC header from the RC1 data and append it to the target level.
            if (targetLevel != null)
            {
                var rc1Header = new Rc1MobyClassHeader
                {
                    sizeVersion = Rc1MobyClassHeader.SIZE_RC1,
                    pUpdate = rc1Model.unk6
                };

                var gcHeader = MobyClassHeaderConverter.ConvertRc1ClassHeaderToGc(rc1Header);
                targetLevel.mobyClassHeaders.Add(gcHeader);

                // Address is index within the class-header table.
                rc3Model.unk6 = (uint)((targetLevel.mobyClassHeaders.Count - 1) * GcMobyClassHeader.SIZE_GC);
            }

            // Find a reference moby instance in the RC1 level that uses this model
            var rc1Moby = rc1Level?.mobs?.FirstOrDefault(m => m.modelID == rc1Model.id);

            if (rc1Moby != null)
            {
                // 🔧 FIXED: Use target level's game type instead of hardcoded RaC3
                GameType targetGameType = targetLevel?.game ?? GameType.RaC3;
                
                // Create a new moby using the target level's game type
                var rc3Moby = new Moby(targetGameType, rc3Model, rc1Moby.position, rc1Moby.rotation, rc1Moby.scale)
                {
                    missionID = rc1Moby.missionID,
                    spawnType = rc1Moby.spawnType,
                    mobyID = rc1Moby.mobyID,
                    bolts = rc1Moby.bolts,
                    dataval = rc1Moby.dataval,
                    drawDistance = rc1Moby.drawDistance,
                    updateDistance = rc1Moby.updateDistance,
                    color = rc1Moby.color,
                    light = rc1Moby.light,
                    groupIndex = rc1Moby.groupIndex,

                    // RC2/RC3 exclusive fields – use safe defaults
                    unk3A = 0,
                    unk3B = 0,
                    exp = 0,
                    unk9 = 0,
                    // Pointer to the converted class header
                    unk6 = (int)rc3Model.unk6
                };

                // Clone pVar block and append to target level
                if (rc1Level?.pVars != null && rc1Moby.pvarIndex >= 0 && rc1Moby.pvarIndex < rc1Level.pVars.Count)
                {
                    byte[] pVarData = (byte[])rc1Level.pVars[rc1Moby.pvarIndex].Clone();
                    if (pVarData.Length < 0x80)
                    {
                        Array.Resize(ref pVarData, 0x80);
                    }
                    if (targetLevel.pVars == null) targetLevel.pVars = new List<byte[]>();
                    targetLevel.pVars.Add(pVarData);
                    rc3Moby.pvarIndex = targetLevel.pVars.Count - 1;
                    rc3Moby.pVars = pVarData;
                }
                else
                {
                    rc3Moby.pvarIndex = -1;
                    rc3Moby.pVars = Array.Empty<byte>();
                }

                // Create a valid RC2/3 moby handle from a template moby if available
                var templateMoby = targetLevel?.mobs?.FirstOrDefault();
                if (templateMoby != null)
                {
                    rc3Moby.unk9 = templateMoby.unk9;
                }

                // Ensure pointer to class header is correct
                rc3Moby.unk6 = (int)rc3Model.unk6;

                // Update transform matrix for completeness
                rc3Moby.UpdateTransformMatrix();

                // Sanity check: pVar pointer should resolve
                if (sub_B5C054(targetLevel, rc3Moby.pvarIndex) == null)
                {
                    Console.WriteLine($"  ⚠️ sub_B5C054 returned null for pVar index {rc3Moby.pvarIndex}");
                }

                convertedMoby = rc3Moby;
            }

            // The cloned model is now associated with a moby (if found)
            // and can be returned to the caller.
            return rc3Model;
        }

        /// <summary>
        /// Import RC2 moby model with enhanced compatibility
        /// </summary>
        private static MobyModel ImportRC2MobyModel(MobyModel rc2Model, Level rc2Level, Level targetLevel)
        {
            // RC2 models are generally compatible with most target formats
            var clonedModel = (MobyModel)MobySwapper.DeepCloneModel(rc2Model);
            
            // Preserve animations and other advanced features
            if (clonedModel.animations != null && clonedModel.animations.Count > 0)
            {
                Console.WriteLine($"  - Preserving {clonedModel.animations.Count} animations from RC2 model");
            }

            return clonedModel;
        }

        /// <summary>
        /// Import RC3 moby model with full feature preservation
        /// </summary>
        private static MobyModel ImportRC3MobyModel(MobyModel rc3Model, Level rc3Level, Level targetLevel)
        {
            // RC3 models have the most advanced features and should be preserved
            var clonedModel = (MobyModel)MobySwapper.DeepCloneModel(rc3Model);
            
            // Preserve all advanced features including animations
            if (clonedModel.animations != null && clonedModel.animations.Count > 0)
            {
                Console.WriteLine($"  - Preserving {clonedModel.animations.Count} animations from RC3 model");
            }

            return clonedModel;
        }

        /// <summary>
        /// Import textures for a model with multi-game compatibility
        /// </summary>
        private static (Dictionary<int, int> mapping, int imported) ImportModelTextures(
            Level targetLevel, Level sourceLevel, MobyModel model, 
            Dictionary<string, int> globalTextureMap, MobyIdentifier sourceInfo)
        {
            var textureMapping = new Dictionary<int, int>();
            int texturesImported = 0;

            // Handle main texture configs
            if (model.textureConfig != null && model.textureConfig.Count > 0)
            {
                Console.WriteLine($"  Processing {model.textureConfig.Count} texture configs...");

                foreach (var texConfig in model.textureConfig)
                {
                    int originalTexId = texConfig.id;

                    // Skip if texture index is out of bounds
                    if (originalTexId >= sourceLevel.textures.Count || originalTexId < 0)
                    {
                        Console.WriteLine($"  ⚠️ Texture ID {originalTexId} is out of bounds for source level");
                        continue;
                    }

                    // Get the texture from source level
                    var sourceTexture = sourceLevel.textures[originalTexId];
                    string textureKey = GetTextureSignature(sourceTexture);

                    // Check if we've already imported this texture
                    int targetTexId = -1;
                    if (globalTextureMap.TryGetValue(textureKey, out targetTexId))
                    {
                        textureMapping[originalTexId] = targetTexId;
                    }
                    else
                    {
                        bool foundMatch = false;

                        // Check if the exact same texture already exists in the target level
                        for (int i = 0; i < targetLevel.textures.Count; i++)
                        {
                            if (CompareTextures(sourceTexture, targetLevel.textures[i]))
                            {
                                targetTexId = i;
                                foundMatch = true;
                                break;
                            }
                        }

                        // If not found, add it
                        if (!foundMatch)
                        {
                            Texture newTexture = DeepCloneTexture(sourceTexture);
                            
                            // Apply any game-specific texture format adjustments
                            if (sourceInfo.IsRC1Source)
                            {
                                // RC1 textures may need some format adjustments
                                // For now, most texture formats should be compatible
                            }
                            else if (sourceInfo.IsRC2Source)
                            {
                                // RC2 textures are generally compatible
                            }
                            else if (sourceInfo.IsRC3Source)
                            {
                                // RC3 textures have the most advanced format
                            }
                            
                            targetLevel.textures.Add(newTexture);
                            targetTexId = targetLevel.textures.Count - 1;
                            texturesImported++;
                        }

                        globalTextureMap[textureKey] = targetTexId;
                        textureMapping[originalTexId] = targetTexId;
                    }

                    // Update the texture ID in the config
                    texConfig.id = targetTexId;
                }
            }

            // Handle other texture configs
            if (model.otherTextureConfigs != null && model.otherTextureConfigs.Count > 0)
            {
                foreach (var texConfig in model.otherTextureConfigs)
                {
                    int originalTexId = texConfig.id;

                    if (originalTexId >= sourceLevel.textures.Count || originalTexId < 0)
                    {
                        continue;
                    }

                    if (textureMapping.TryGetValue(originalTexId, out int mappedId))
                    {
                        texConfig.id = mappedId;
                    }
                    else
                    {
                        // Process it the same way as primary textures
                        var sourceTexture = sourceLevel.textures[originalTexId];
                        string textureKey = GetTextureSignature(sourceTexture);

                        int targetTexId = -1;
                        if (globalTextureMap.TryGetValue(textureKey, out targetTexId))
                        {
                            textureMapping[originalTexId] = targetTexId;
                        }
                        else
                        {
                            bool foundMatch = false;
                            for (int i = 0; i < targetLevel.textures.Count; i++)
                            {
                                if (CompareTextures(sourceTexture, targetLevel.textures[i]))
                                {
                                    targetTexId = i;
                                    foundMatch = true;
                                    break;
                                }
                            }

                            if (!foundMatch)
                            {
                                Texture newTexture = DeepCloneTexture(sourceTexture);
                                targetLevel.textures.Add(newTexture);
                                targetTexId = targetLevel.textures.Count - 1;
                                texturesImported++;
                            }

                            globalTextureMap[textureKey] = targetTexId;
                            textureMapping[originalTexId] = targetTexId;
                        }

                        texConfig.id = targetTexId;
                    }
                }
            }

            return (textureMapping, texturesImported);
        }

        /// <summary>
        /// Enhanced manual selection that shows source game types
        /// </summary>
        private static List<CommonMoby> SelectMobysManuallyEnhanced(List<CommonMoby> allMobys)
        {
            List<CommonMoby> selectedMobys = new List<CommonMoby>();

            var sortedMobys = allMobys
                .OrderByDescending(m => m.ReferenceCount)
                .ThenBy(m => m.ModelId)
                .ToList();

            Console.WriteLine("\nAvailable Mobys:");
            Console.WriteLine("----------------");
            Console.WriteLine("[ID]\t[Name]\t\t\t[Sources]\t[Animations]\t[Game Types]");

            for (int i = 0; i < sortedMobys.Count; i++)
            {
                var moby = sortedMobys[i];
                string sourceLevels = string.Join(", ", moby.Instances.Select(inst => inst.LevelName));
                string animStatus = moby.HasAnimations ? "Yes" : "No";
                string gameTypes = "";
                
                List<string> availableGameTypes = new List<string>();
                if (moby.HasRC1Sources) availableGameTypes.Add("RC1");
                if (moby.HasRC2Sources) availableGameTypes.Add("RC2");
                if (moby.HasRC3Sources) availableGameTypes.Add("RC3");
                gameTypes = string.Join("+", availableGameTypes);

                string paddedName = moby.ModelName.PadRight(20);

                Console.WriteLine($"{i + 1,3}. {moby.ModelId,5} {paddedName} {moby.ReferenceCount,4}x\t{animStatus,-5}\t{gameTypes}");
            }

            Console.WriteLine("\nEnter the numbers of the mobys to import, separated by commas");
            Console.WriteLine("Or enter 'r1' for RC1 only, 'r2' for RC2 only, 'r3' for RC3 only, 'mixed' for multiple versions, 'a' for all");
            Console.Write("> ");

            string input = Console.ReadLine()?.Trim().ToLower() ?? "";

            if (input == "r1")
            {
                return sortedMobys.Where(m => m.HasRC1Sources).ToList();
            }
            else if (input == "r2")
            {
                return sortedMobys.Where(m => m.HasRC2Sources).ToList();
            }
            else if (input == "r3")
            {
                return sortedMobys.Where(m => m.HasRC3Sources).ToList();
            }
            else if (input == "mixed")
            {
                return sortedMobys.Where(m => (m.HasRC1Sources ? 1 : 0) + (m.HasRC2Sources ? 1 : 0) + (m.HasRC3Sources ? 1 : 0) > 1).ToList();
            }
            else if (input == "a")
            {
                return sortedMobys;
            }
            else
            {
                // Parse individual selections
                string[] selections = input.Split(',', StringSplitOptions.RemoveEmptyEntries);

                foreach (string selection in selections)
                {
                    if (int.TryParse(selection.Trim(), out int index) && index >= 1 && index <= sortedMobys.Count)
                    {
                        selectedMobys.Add(sortedMobys[index - 1]);
                    }
                }
            }

            return selectedMobys;
        }

        /// <summary>
        /// Enhanced instance creation that can handle mixed RC1/RC2/RC3 sources
        /// </summary>
        private static bool CreateInstancesForImportedMobysEnhanced(Level targetLevel, List<int> importedModelIds, List<CommonMoby> importedMobys)
        {
            if (targetLevel == null || importedModelIds == null || importedModelIds.Count == 0)
            {
                return false;
            }

            // Show available source levels for instancing
            var sourceLevels = importedMobys
                .SelectMany(m => m.Instances)
                .Select(i => i.SourceLevel)
                .Distinct()
                .ToList();

            Console.WriteLine("\nAvailable source levels for position references:");
            for (int i = 0; i < sourceLevels.Count; i++)
            {
                var level = sourceLevels[i];
                string gameType = level.game.num switch
                {
                    1 => "RC1",
                    2 => "RC2", 
                    3 => "RC3",
                    4 => "Deadlocked",
                    _ => "Unknown"
                };
                Console.WriteLine($"{i + 1}. {Path.GetFileName(level.path)} ({gameType})");
            }

            Console.WriteLine("\nSelect source level for positions (enter number):");
            Console.Write("> ");
            string selection = Console.ReadLine()?.Trim() ?? "";

            if (!int.TryParse(selection, out int selectedIndex) || selectedIndex < 1 || selectedIndex > sourceLevels.Count)
            {
                Console.WriteLine("❌ Invalid selection");
                return false;
            }

            Level selectedSourceLevel = sourceLevels[selectedIndex - 1];
            string sourceGameType = selectedSourceLevel.game.num switch
            {
                1 => "RC1",
                2 => "RC2",
                3 => "RC3", 
                4 => "Deadlocked",
                _ => "Unknown"
            };
            
            Console.WriteLine($"Using {sourceGameType} level: {Path.GetFileName(selectedSourceLevel.path)}");

            // Use the standard instancer with the selected source level
            bool success = MobyOltanisInstancer.CreateMobyInstancesFromLevel(
                targetLevel,
                selectedSourceLevel,
                importedModelIds.ToArray(),
                MobyOltanisInstancer.InstancerOptions.Default);

            return success;
        }

        /// <summary>
        /// Enhanced interactive method that supports mixed RC1/RC2/RC3 sources with grind path preservation
        /// </summary>
        public static bool ImportMobysFromMixedSourcesInteractive()
        {
            Console.WriteLine("\n==== Import Mobys from Mixed RC1/RC2/RC3 Sources (with Grind Path Preservation) ====");

            // 1. Get target level path
            Console.WriteLine("\nEnter path to the target level engine.ps3 file:");
            Console.Write("> ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                Console.WriteLine("❌ Invalid target level path");
                return false;
            }

            // 2. Get reference level paths
            List<string> referenceEnginePaths = new List<string>();

            Console.WriteLine("\nHow many reference levels do you want to include? (1-10)");
            Console.Write("> ");

            if (!int.TryParse(Console.ReadLine()?.Trim() ?? "1", out int referenceCount))
            {
                referenceCount = 1;
            }

            referenceCount = Math.Max(1, Math.Min(10, referenceCount));

            Console.WriteLine($"\nEnter paths to {referenceCount} reference engine.ps3 files (can be RC1, RC2, or RC3):");

            for (int i = 0; i < referenceCount; i++)
            {
                Console.Write($"Reference #{i + 1}> ");
                string path = Console.ReadLine()?.Trim() ?? "";

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    referenceEnginePaths.Add(path);
                }
                else
                {
                    Console.WriteLine("❌ Invalid path, skipping");
                }
            }

            if (referenceEnginePaths.Count < 1)
            {
                Console.WriteLine("❌ Need at least 1 valid reference path to continue");
                return false;
            }

            // 3. Ask about overwriting existing mobys
            Console.Write("\nOverwrite existing mobys in the target level? (y/n): ");
            bool allowOverwrite = Console.ReadLine()?.Trim().ToLower() == "y";

            // 4. Ask about grind path preservation
            Console.Write("\nPreserve and restore grind paths? (y/n) [recommended: y]: ");
            bool preserveGrindPaths = Console.ReadLine()?.Trim().ToLower() != "n"; // Default to true

            // 4. Load target level
            Console.WriteLine($"\nLoading target level: {Path.GetFileName(targetPath)}...");
            Level targetLevel;

            try
            {
                targetLevel = new Level(targetPath);
                string targetGameType = targetLevel.game.num switch
                {
                    1 => "RC1",
                    2 => "RC2",
                    3 => "RC3",
                    4 => "Deadlocked", 
                    _ => "Unknown"
                };
                Console.WriteLine($"✅ Successfully loaded {targetGameType} target level with {targetLevel.mobyModels?.Count ?? 0} moby models");
                
                if (preserveGrindPaths)
                {
                    Console.WriteLine($"  Current grind paths: {targetLevel.grindPaths?.Count ?? 0}");
                    Console.WriteLine($"  Current splines: {targetLevel.splines?.Count ?? 0}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading target level: {ex.Message}");
                return false;
            }

            // 5. Ask user how to select mobys for import
            Console.WriteLine("\nSelect which mobys to import:");
            Console.WriteLine("  1) All mobys");
            Console.WriteLine("  2) Common mobys (appear in multiple reference levels)");
            Console.WriteLine("  3) Uncommon mobys (appear in only one reference level)");
            Console.WriteLine("  4) Manually specify model IDs");
            Console.Write("> ");
            string modeInput = Console.ReadLine()?.Trim() ?? "1";

            ImportSelectionMode selectionMode = ImportSelectionMode.All;
            List<int>? manualModelIds = null;

            switch (modeInput)
            {
                case "2":
                    selectionMode = ImportSelectionMode.MultipleVersions;
                    break;
                case "3":
                    selectionMode = ImportSelectionMode.Unique;
                    break;
                case "4":
                    selectionMode = ImportSelectionMode.Manual;
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
                    selectionMode = ImportSelectionMode.All;
                    break;
            }

            // 6. Perform the import with the chosen selection mode
            bool success = ImportMobysFromMixedReferenceLevels(
                targetLevel,
                referenceEnginePaths,
                allowOverwrite,
                preserveGrindPaths,
                selectionMode,
                manualModelIds);

            // 7. Save the target level if successful
            if (success)
            {
                Console.Write("\nSave changes to the target level? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLower() == "y")
                {
                    try
                    {
                        string? savePath = targetLevel.path;
                        if (savePath != null)
                        {
                            Console.WriteLine($"Saving level to {savePath}...");
                            
                            // 🔧 CRITICAL FIX: Save without any additional processing that might clear grind paths
                            if (preserveGrindPaths && targetLevel.grindPaths?.Count > 0)
                            {
                                Console.WriteLine("Final grind path validation before save...");
                                bool grindPathsValid = GrindPathSwapper.ValidateGrindPathReferences(targetLevel);
                                
                                if (!grindPathsValid)
                                {
                                    Console.WriteLine("⚠️ Grind path validation failed before save - attempting emergency restoration...");
                                    
                                    // Emergency restoration using RC1 source if available
                                    var rc1Source = FindRC1SourceLevel(referenceEnginePaths);
                                    if (rc1Source != null)
                                    {
                                        Console.WriteLine("Using RC1 source for emergency grind path restoration...");
                                        GrindPathSwapper.SwapGrindPathsWithRC1Oltanis(
                                            targetLevel, 
                                            rc1Source, 
                                            GrindPathSwapper.GrindPathSwapOptions.FullReplacement);
                                        
                                        // Validate again
                                        bool repairValid = GrindPathSwapper.ValidateGrindPathReferences(targetLevel);
                                        if (repairValid)
                                        {
                                            Console.WriteLine("✅ Emergency grind path restoration successful");
                                        }
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("✅ Grind paths validated successfully before save");
                                }
                            }
                            
                            // Use custom save method that doesn't interfere with grind paths
                            SaveLevelWithGrindPathProtection(targetLevel, savePath);
                            Console.WriteLine("✅ Target level saved successfully");
                            
                            if (preserveGrindPaths)
                            {
                                Console.WriteLine($"  Final grind paths count: {targetLevel.grindPaths?.Count ?? 0}");
                                Console.WriteLine($"  Final splines count: {targetLevel.splines?.Count ?? 0}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("❌ Target level path is null, cannot save changes");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error saving target level: {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                        return false;
                    }
                }
            }

            return success;
        }

        /// <summary>
        /// Custom save method that protects grind paths from being cleared
        /// </summary>
        private static void SaveLevelWithGrindPathProtection(Level level, string savePath)
        {
            // Backup grind paths and splines before any save operations
            var grindPathBackup = level.grindPaths?.ToList() ?? new List<GrindPath>();
            var splineBackup = level.splines?.ToList() ?? new List<Spline>();
            
            Console.WriteLine($"🛡️ Protecting {grindPathBackup.Count} grind paths and {splineBackup.Count} splines during save...");
            
            try
            {
                // Save the level
                level.Save(savePath);
                
                // Immediately restore grind paths if they were cleared
                if (level.grindPaths == null || level.grindPaths.Count == 0)
                {
                    Console.WriteLine("⚠️ Grind paths were cleared during save - restoring...");
                    level.grindPaths = grindPathBackup;
                }
                
                if (level.splines == null || level.splines.Count < splineBackup.Count)
                {
                    Console.WriteLine("⚠️ Splines were cleared during save - restoring...");
                    level.splines = splineBackup;
                    
                    // Fix spline references in grind paths
                    var splineIdMap = splineBackup.ToDictionary(s => s.id, s => s);
                    foreach (var grindPath in grindPathBackup)
                    {
                        if (grindPath.spline != null && splineIdMap.TryGetValue(grindPath.spline.id, out var correctSpline))
                        {
                            grindPath.spline = correctSpline;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during protected save: {ex.Message}");
                
                // Restore grind paths even if save failed
                Console.WriteLine("Restoring grind paths after save error...");
                level.grindPaths = grindPathBackup;
                level.splines = splineBackup;
                
                throw; // Re-throw the exception
            }
        }

        // Keep all existing helper methods unchanged
        private static string GetFriendlyModelName(int modelId)
        {
            foreach (var entry in MobySwapper.MobyTypes)
            {
                if (entry.Value.Contains(modelId))
                {
                    return entry.Key;
                }
            }
            return $"Model {modelId}";
        }

        private static bool CompareTextures(Texture sourceTexture, Texture targetTexture)
        {
            if (sourceTexture == null || targetTexture == null)
                return false;

            return sourceTexture.width == targetTexture.width &&
                   sourceTexture.height == targetTexture.height &&
                   sourceTexture.vramPointer == targetTexture.vramPointer &&
                   sourceTexture.data?.Length == targetTexture.data?.Length;
        }

        private static string GetTextureSignature(Texture texture)
        {
            if (texture == null) return "null";

            string dataHash = "";
            if (texture.data != null && texture.data.Length > 0)
            {
                int bytesToHash = Math.Min(16, texture.data.Length);
                int hashValue = 0;
                for (int i = 0; i < bytesToHash; i++)
                {
                    hashValue = (hashValue * 31) + texture.data[i];
                }
                dataHash = hashValue.ToString("X8");
            }

            return $"{texture.width}x{texture.height}-{texture.vramPointer}-{dataHash}";
        }

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

        private static HashSet<int> GetSpecialMobyIds()
        {
            HashSet<int> specialIds = new HashSet<int>();
            specialIds.Add(0); // Ratchet
            specialIds.Add(1); // Clank
            specialIds.Add(11); // Vendor
            specialIds.Add(1143); // Vendor logo
            specialIds.Add(1007);
            specialIds.Add(1137);
            specialIds.Add(1204);
            specialIds.Add(1502);

            foreach (var entry in MobySwapper.MobyTypes)
            {
                if (entry.Key.Contains("Vendor") ||
                    entry.Key.Contains("Gadgetron") ||
                    entry.Key.Contains("Ratchet") ||
                    entry.Key.Contains("Clank") ||
                    entry.Key.Contains("Ship") ||
                    entry.Key.Contains("Skid"))
                {
                    foreach (var id in entry.Value)
                    {
                        specialIds.Add(id);
                    }
                }
            }

            return specialIds;
        }

        public static void ValidateAndFixGroupIndexConflicts(Level targetLevel, bool autoFix = false)
        {
            if (targetLevel?.mobs == null || targetLevel.mobs.Count == 0)
            {
                Console.WriteLine("✅ No mobys to check for group ID conflicts.");
                return;
            }

            Console.WriteLine("\n🔍 Checking for Moby Group ID conflicts...");

            var groups = targetLevel.mobs
                .Where(m => m.groupIndex != -1)
                .GroupBy(m => m.groupIndex)
                .ToList();

            var conflictingGroups = new List<IGrouping<int, Moby>>();
            foreach (var group in groups)
            {
                if (group.Select(m => m.modelID).Distinct().Count() > 1)
                {
                    conflictingGroups.Add(group);
                }
            }

            if (conflictingGroups.Count == 0)
            {
                Console.WriteLine("✅ No Group ID conflicts found.");
                return;
            }

            Console.WriteLine($"⚠️ Found {conflictingGroups.Count} Group ID conflicts where different model IDs share the same group ID:");
            foreach (var group in conflictingGroups)
            {
                var modelIdsInGroup = group.Select(m => m.modelID).Distinct();
                var modelIdStrings = modelIdsInGroup.Select(id => $"{GetFriendlyModelName(id)} (ID: {id})");
                Console.WriteLine($"  - Group ID {group.Key} is shared by: {string.Join(", ", modelIdStrings)}");
            }

            bool fixConflicts = autoFix;
            if (!autoFix)
            {
                Console.Write("\nDo you want to automatically fix these conflicts by assigning new group IDs? (y/n): ");
                fixConflicts = Console.ReadLine()?.Trim().ToLower() == "y";
            }

            if (fixConflicts)
            {
                Console.WriteLine("🔄 Fixing Group ID conflicts...");
                int nextGroupId = (targetLevel.mobs.Any() ? targetLevel.mobs.Max(m => m.groupIndex) : 0) + 1;
                
                foreach (var group in conflictingGroups)
                {
                    var originalGroupId = group.Key;
                    var modelIdsToRemap = group.Select(m => m.modelID).Distinct().Skip(1).ToList();

                    foreach (var modelIdToRemap in modelIdsToRemap)
                    {
                        var mobysToUpdate = group.Where(m => m.modelID == modelIdToRemap).ToList();
                        foreach (var moby in mobysToUpdate)
                        {
                            moby.groupIndex = nextGroupId;
                        }
                        Console.WriteLine($"  - Remapped {mobysToUpdate.Count} mobys of Model ID {modelIdToRemap} from Group ID {originalGroupId} to new Group ID {nextGroupId}");
                        nextGroupId++;
                    }
                }
                Console.WriteLine($"✅ Fixed {conflictingGroups.Count} conflicts by remapping model IDs to new group IDs.");
            }
            else
            {
                Console.WriteLine("Conflicts were not fixed. This may cause issues in-game.");
            }
        }
    }
}
