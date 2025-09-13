using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models; // Add this using directive
using OpenTK.Mathematics;

namespace GeometrySwapper
{
    /// <summary>
    /// Provides utilities for comparing occlusion data between levels.
    /// </summary>
    public static class OcclusionDiagnostics
    {
        /// <summary>
        /// Compares occlusion data across three levels and writes a detailed report.
        /// </summary>
        /// <param name="source">The source level (typically RC1).</param>
        /// <param name="donor">The pristine donor level before modifications.</param>
        /// <param name="target">The resulting level after swap.</param>
        /// <param name="outputPath">Directory where the report will be written.</param>
        public static void CompareOcclusionData(Level source, Level donor, Level target, string outputPath)
        {
            Directory.CreateDirectory(outputPath);
            string reportPath = Path.Combine(outputPath, "occlusion_comparison_report.txt");

            using var writer = new StreamWriter(reportPath);
            writer.WriteLine("==== Occlusion Comparison Report ====");
            writer.WriteLine($"Generated on {DateTime.Now}");
            writer.WriteLine();

            DumpLevelInfo(writer, "Source", source);
            DumpLevelInfo(writer, "Pristine Donor", donor);
            DumpLevelInfo(writer, "Target", target);

            writer.WriteLine();
            writer.WriteLine("==== Summary of Differences ====");
            CompareLevels(writer, "Source", source, "Pristine Donor", donor);
            writer.WriteLine();
            CompareLevels(writer, "Source", source, "Target", target);
            writer.WriteLine();
            CompareLevels(writer, "Pristine Donor", donor, "Target", target);

            writer.WriteLine();
            writer.WriteLine("==== Detailed TIE Comparison ====");
            CompareAllTies(writer, source, target);
        }

        /// <summary>
        /// Compares every TIE between source and target levels and reports detailed mismatches.
        /// </summary>
        /// <param name="source">The source level</param>
        /// <param name="target">The target level</param>
        /// <param name="outputPath">Directory where the detailed TIE report will be written</param>
        public static void CompareAllTiesDetailed(Level source, Level target, string outputPath)
        {
            Directory.CreateDirectory(outputPath);
            string reportPath = Path.Combine(outputPath, "detailed_tie_comparison_report.txt");

            using var writer = new StreamWriter(reportPath);
            writer.WriteLine("==== Detailed TIE Comparison Report ====");
            writer.WriteLine($"Generated on {DateTime.Now}");
            writer.WriteLine();

            CompareAllTies(writer, source, target);
        }

        private static void DumpLevelInfo(StreamWriter writer, string label, Level level)
        {
            var occl = level.occlusionData;
            int mobyCount = occl?.mobyData?.Count ?? 0;
            int tieCount = occl?.tieData?.Count ?? 0;
            int shrubCount = occl?.shrubData?.Count ?? 0;
            int byteSize = occl?.ToByteArray().Length ?? 0;

            // Additional level information
            int actualTieCount = level.ties?.Count ?? 0;
            int tieModelCount = level.tieModels?.Count ?? 0;
            int tieIdCount = level.tieIds?.Count ?? 0;

            writer.WriteLine($"-- {label} --");
            writer.WriteLine($"Moby entries     : {mobyCount}");
            writer.WriteLine($"TIE entries      : {tieCount}");
            writer.WriteLine($"Shrub entries    : {shrubCount}");
            writer.WriteLine($"Byte size        : {byteSize}");
            writer.WriteLine($"Actual TIE count : {actualTieCount}");
            writer.WriteLine($"TIE models count : {tieModelCount}");
            writer.WriteLine($"TIE IDs count    : {tieIdCount}");

            writer.WriteLine("First TIE entries:");
            if (occl?.tieData != null)
            {
                int index = 0;
                foreach (var kv in occl.tieData.Take(10))
                {
                    writer.WriteLine($"  [{index++}] 0x{kv.Key:X8} -> 0x{kv.Value:X8}");
                }
            }
            else
            {
                writer.WriteLine("  None");
            }
            writer.WriteLine();
        }

        private static void CompareLevels(StreamWriter writer, string labelA, Level a, string labelB, Level b)
        {
            var occlA = a.occlusionData;
            var occlB = b.occlusionData;

            int mobyCountA = occlA?.mobyData?.Count ?? 0;
            int mobyCountB = occlB?.mobyData?.Count ?? 0;
            int tieCountA = occlA?.tieData?.Count ?? 0;
            int tieCountB = occlB?.tieData?.Count ?? 0;
            int shrubCountA = occlA?.shrubData?.Count ?? 0;
            int shrubCountB = occlB?.shrubData?.Count ?? 0;
            int byteSizeA = occlA?.ToByteArray().Length ?? 0;
            int byteSizeB = occlB?.ToByteArray().Length ?? 0;

            writer.WriteLine($"** {labelA} vs {labelB} **");
            writer.WriteLine($"Moby count diff : {mobyCountA - mobyCountB}");
            writer.WriteLine($"TIE count diff  : {tieCountA - tieCountB}");
            writer.WriteLine($"Shrub count diff: {shrubCountA - shrubCountB}");
            writer.WriteLine($"Byte size diff  : {byteSizeA - byteSizeB}");
            writer.WriteLine($"Moby mismatches : {CountMismatches(occlA?.mobyData, occlB?.mobyData)}");
            writer.WriteLine($"TIE mismatches  : {CountMismatches(occlA?.tieData, occlB?.tieData)}");
            writer.WriteLine($"Shrub mismatches: {CountMismatches(occlA?.shrubData, occlB?.shrubData)}");
        }

        private static void CompareAllTies(StreamWriter writer, Level source, Level target)
        {
            writer.WriteLine($"** Detailed TIE-by-TIE Comparison: Source vs Target **");
            writer.WriteLine();

            var sourceTies = source.ties ?? new List<Tie>();
            var targetTies = target.ties ?? new List<Tie>();
            var sourceOcclusion = source.occlusionData?.tieData ?? new List<KeyValuePair<int, int>>();
            var targetOcclusion = target.occlusionData?.tieData ?? new List<KeyValuePair<int, int>>();

            writer.WriteLine($"Source TIE count: {sourceTies.Count}");
            writer.WriteLine($"Target TIE count: {targetTies.Count}");
            writer.WriteLine($"Source occlusion entries: {sourceOcclusion.Count}");
            writer.WriteLine($"Target occlusion entries: {targetOcclusion.Count}");
            writer.WriteLine();

            // Summary statistics
            int positionMismatches = 0;
            int modelIdMismatches = 0;
            int scaleMismatches = 0;
            int rotationMismatches = 0;
            int occlusionMismatches = 0;
            int tieIdMismatches = 0;
            int lightMismatches = 0;

            int maxComparisons = Math.Max(sourceTies.Count, targetTies.Count);
            int minComparisons = Math.Min(sourceTies.Count, targetTies.Count);

            writer.WriteLine("=== Individual TIE Comparisons ===");
            writer.WriteLine();

            for (int i = 0; i < maxComparisons; i++)
            {
                bool sourceMissing = i >= sourceTies.Count;
                bool targetMissing = i >= targetTies.Count;

                writer.WriteLine($"--- TIE Index {i} ---");

                if (sourceMissing)
                {
                    writer.WriteLine("❌ SOURCE: Missing (target has extra TIE)");
                    var targetTie = targetTies[i];
                    writer.WriteLine($"   TARGET: ModelID={targetTie.modelID}, Pos=({targetTie.position.X:F3}, {targetTie.position.Y:F3}, {targetTie.position.Z:F3}), TieID={targetTie.off58}, Light={targetTie.light}");
                }
                else if (targetMissing)
                {
                    writer.WriteLine("❌ TARGET: Missing (source has extra TIE)");
                    var sourceTie = sourceTies[i];
                    writer.WriteLine($"   SOURCE: ModelID={sourceTie.modelID}, Pos=({sourceTie.position.X:F3}, {sourceTie.position.Y:F3}, {sourceTie.position.Z:F3}), TieID={sourceTie.off58}, Light={sourceTie.light}");
                }
                else
                {
                    // Both TIEs exist, compare them
                    var sourceTie = sourceTies[i];
                    var targetTie = targetTies[i];
                    bool hasMismatch = false;

                    // Compare positions (with tolerance for floating point precision)
                    float positionDistance = Vector3.Distance(sourceTie.position, targetTie.position);
                    if (positionDistance > 0.001f) // 1mm tolerance
                    {
                        writer.WriteLine($"❌ POSITION: Source=({sourceTie.position.X:F6}, {sourceTie.position.Y:F6}, {sourceTie.position.Z:F6}), Target=({targetTie.position.X:F6}, {targetTie.position.Y:F6}, {targetTie.position.Z:F6}), Distance={positionDistance:F6}");
                        positionMismatches++;
                        hasMismatch = true;
                    }

                    // Compare model IDs
                    if (sourceTie.modelID != targetTie.modelID)
                    {
                        writer.WriteLine($"❌ MODEL_ID: Source={sourceTie.modelID}, Target={targetTie.modelID}");
                        modelIdMismatches++;
                        hasMismatch = true;
                    }

                    // Compare scales
                    float scaleDistance = Vector3.Distance(sourceTie.scale, targetTie.scale);
                    if (scaleDistance > 0.001f)
                    {
                        writer.WriteLine($"❌ SCALE: Source=({sourceTie.scale.X:F6}, {sourceTie.scale.Y:F6}, {sourceTie.scale.Z:F6}), Target=({targetTie.scale.X:F6}, {targetTie.scale.Y:F6}, {targetTie.scale.Z:F6}), Distance={scaleDistance:F6}");
                        scaleMismatches++;
                        hasMismatch = true;
                    }

                    // Compare rotations (quaternions)
                    float rotationDistance = Math.Abs(sourceTie.rotation.W - targetTie.rotation.W) +
                                           Math.Abs(sourceTie.rotation.X - targetTie.rotation.X) +
                                           Math.Abs(sourceTie.rotation.Y - targetTie.rotation.Y) +
                                           Math.Abs(sourceTie.rotation.Z - targetTie.rotation.Z);
                    if (rotationDistance > 0.001f)
                    {
                        writer.WriteLine($"❌ ROTATION: Source=({sourceTie.rotation.W:F6}, {sourceTie.rotation.X:F6}, {sourceTie.rotation.Y:F6}, {sourceTie.rotation.Z:F6}), Target=({targetTie.rotation.W:F6}, {targetTie.rotation.X:F6}, {targetTie.rotation.Y:F6}, {targetTie.rotation.Z:F6})");
                        rotationMismatches++;
                        hasMismatch = true;
                    }

                    // Compare TIE IDs (off58)
                    if (sourceTie.off58 != targetTie.off58)
                    {
                        writer.WriteLine($"❌ TIE_ID: Source={sourceTie.off58}, Target={targetTie.off58}");
                        tieIdMismatches++;
                        hasMismatch = true;
                    }

                    // Compare light values
                    if (sourceTie.light != targetTie.light)
                    {
                        writer.WriteLine($"❌ LIGHT: Source={sourceTie.light}, Target={targetTie.light}");
                        lightMismatches++;
                        hasMismatch = true;
                    }

                    // Compare occlusion data if available
                    if (i < sourceOcclusion.Count && i < targetOcclusion.Count)
                    {
                        var sourceOccl = sourceOcclusion[i];
                        var targetOccl = targetOcclusion[i];
                        if (sourceOccl.Key != targetOccl.Key || sourceOccl.Value != targetOccl.Value)
                        {
                            writer.WriteLine($"❌ OCCLUSION: Source=(0x{sourceOccl.Key:X8}, 0x{sourceOccl.Value:X8}), Target=(0x{targetOccl.Key:X8}, 0x{targetOccl.Value:X8})");
                            occlusionMismatches++;
                            hasMismatch = true;
                        }
                    }
                    else if (i < sourceOcclusion.Count || i < targetOcclusion.Count)
                    {
                        writer.WriteLine($"❌ OCCLUSION: Mismatched counts (source has {sourceOcclusion.Count}, target has {targetOcclusion.Count})");
                        occlusionMismatches++;
                        hasMismatch = true;
                    }

                    if (!hasMismatch)
                    {
                        writer.WriteLine("✅ MATCH: All properties match");
                    }
                }

                writer.WriteLine();

                // Limit output for very large levels to avoid huge files
                if (i >= 100 && i < maxComparisons - 10)
                {
                    if (i == 100)
                    {
                        writer.WriteLine($"... (skipping detailed output for TIEs {i + 1} to {maxComparisons - 10} to keep file size manageable) ...");
                        writer.WriteLine();
                    }
                    continue;
                }
            }

            writer.WriteLine("=== SUMMARY STATISTICS ===");
            writer.WriteLine($"Total TIEs compared: {minComparisons}");
            writer.WriteLine($"Position mismatches: {positionMismatches}");
            writer.WriteLine($"Model ID mismatches: {modelIdMismatches}");
            writer.WriteLine($"Scale mismatches: {scaleMismatches}");
            writer.WriteLine($"Rotation mismatches: {rotationMismatches}");
            writer.WriteLine($"TIE ID mismatches: {tieIdMismatches}");
            writer.WriteLine($"Light mismatches: {lightMismatches}");
            writer.WriteLine($"Occlusion mismatches: {occlusionMismatches}");
            writer.WriteLine($"Count mismatch: Source={sourceTies.Count}, Target={targetTies.Count}, Difference={Math.Abs(sourceTies.Count - targetTies.Count)}");

            // Calculate match percentage
            if (minComparisons > 0)
            {
                int totalChecks = minComparisons * 7; // 7 properties checked per TIE
                int totalMismatches = positionMismatches + modelIdMismatches + scaleMismatches + 
                                    rotationMismatches + tieIdMismatches + lightMismatches + occlusionMismatches;
                float matchPercentage = ((float)(totalChecks - totalMismatches) / totalChecks) * 100f;
                writer.WriteLine($"Overall match rate: {matchPercentage:F2}%");
            }

            writer.WriteLine();
            writer.WriteLine("=== TIE MODEL ANALYSIS ===");
            AnalyzeTieModels(writer, source, target);

            writer.WriteLine();
            writer.WriteLine("=== SPATIAL DISTRIBUTION ANALYSIS ===");
            AnalyzeSpatialDistribution(writer, source, target);
        }

        private static void AnalyzeTieModels(StreamWriter writer, Level source, Level target)
        {
            // Fix: Handle potentially null lists properly
            var sourceModels = source.tieModels ?? new List<Model>();
            var targetModels = target.tieModels ?? new List<Model>();

            writer.WriteLine($"Source TIE models: {sourceModels.Count}");
            writer.WriteLine($"Target TIE models: {targetModels.Count}");

            // Create dictionaries for model analysis
            var sourceModelCounts = new Dictionary<int, int>();
            var targetModelCounts = new Dictionary<int, int>();

            // Count usage of each model ID in source
            foreach (var tie in source.ties ?? new List<Tie>())
            {
                sourceModelCounts[tie.modelID] = sourceModelCounts.GetValueOrDefault(tie.modelID, 0) + 1;
            }

            // Count usage of each model ID in target  
            foreach (var tie in target.ties ?? new List<Tie>())
            {
                targetModelCounts[tie.modelID] = targetModelCounts.GetValueOrDefault(tie.modelID, 0) + 1;
            }

            writer.WriteLine();
            writer.WriteLine("Model usage comparison:");
            var allModelIds = sourceModelCounts.Keys.Concat(targetModelCounts.Keys).Distinct().OrderBy(id => id);

            foreach (var modelId in allModelIds)
            {
                int sourceUsage = sourceModelCounts.GetValueOrDefault(modelId, 0);
                int targetUsage = targetModelCounts.GetValueOrDefault(modelId, 0);

                if (sourceUsage != targetUsage)
                {
                    writer.WriteLine($"❌ Model {modelId}: Source uses {sourceUsage} times, Target uses {targetUsage} times (diff: {targetUsage - sourceUsage})");
                }
                else if (sourceUsage > 0)
                {
                    writer.WriteLine($"✅ Model {modelId}: Both use {sourceUsage} times");
                }
            }
        }

        private static void AnalyzeSpatialDistribution(StreamWriter writer, Level source, Level target)
        {
            var sourceTies = source.ties ?? new List<Tie>();
            var targetTies = target.ties ?? new List<Tie>();

            if (sourceTies.Count == 0 && targetTies.Count == 0)
            {
                writer.WriteLine("No TIEs to analyze.");
                return;
            }

            // Calculate bounding boxes
            var sourceBounds = CalculateBounds(sourceTies);
            var targetBounds = CalculateBounds(targetTies);

            writer.WriteLine("Spatial bounds comparison:");
            writer.WriteLine($"Source bounds: Min=({sourceBounds.min.X:F2}, {sourceBounds.min.Y:F2}, {sourceBounds.min.Z:F2}), Max=({sourceBounds.max.X:F2}, {sourceBounds.max.Y:F2}, {sourceBounds.max.Z:F2})");
            writer.WriteLine($"Target bounds: Min=({targetBounds.min.X:F2}, {targetBounds.min.Y:F2}, {targetBounds.min.Z:F2}), Max=({targetBounds.max.X:F2}, {targetBounds.max.Y:F2}, {targetBounds.max.Z:F2})");

            var sourceCentroid = (sourceBounds.min + sourceBounds.max) * 0.5f;
            var targetCentroid = (targetBounds.min + targetBounds.max) * 0.5f;
            float centroidDistance = Vector3.Distance(sourceCentroid, targetCentroid);

            writer.WriteLine($"Centroid shift: {centroidDistance:F3} units");
            if (centroidDistance > 1.0f)
            {
                writer.WriteLine($"❌ Significant centroid shift detected!");
            }
            else
            {
                writer.WriteLine($"✅ Centroid shift is minimal");
            }

            // Analyze density in grid cells
            writer.WriteLine();
            writer.WriteLine("Spatial density analysis (64x64x64 unit grid cells):");
            const float gridSize = 64.0f;
            var sourceGrid = CreateSpatialGrid(sourceTies, gridSize);
            var targetGrid = CreateSpatialGrid(targetTies, gridSize);

            var allCells = sourceGrid.Keys.Concat(targetGrid.Keys).Distinct();
            int significantDifferences = 0;

            foreach (var cell in allCells)
            {
                int sourceCount = sourceGrid.GetValueOrDefault(cell, 0);
                int targetCount = targetGrid.GetValueOrDefault(cell, 0);
                int difference = Math.Abs(sourceCount - targetCount);

                if (difference >= 5) // Only report significant differences
                {
                    writer.WriteLine($"Cell ({cell.X}, {cell.Y}, {cell.Z}): Source={sourceCount}, Target={targetCount}, Diff={targetCount - sourceCount}");
                    significantDifferences++;
                }
            }

            if (significantDifferences == 0)
            {
                writer.WriteLine("✅ No significant spatial density differences detected");
            }
            else
            {
                writer.WriteLine($"❌ {significantDifferences} grid cells have significant density differences (>= 5 TIEs)");
            }
        }

        private static (Vector3 min, Vector3 max) CalculateBounds(List<Tie> ties)
        {
            if (ties.Count == 0)
                return (Vector3.Zero, Vector3.Zero);

            var min = ties[0].position;
            var max = ties[0].position;

            foreach (var tie in ties.Skip(1))
            {
                min = Vector3.ComponentMin(min, tie.position);
                max = Vector3.ComponentMax(max, tie.position);
            }

            return (min, max);
        }

        private static Dictionary<(int X, int Y, int Z), int> CreateSpatialGrid(List<Tie> ties, float gridSize)
        {
            var grid = new Dictionary<(int X, int Y, int Z), int>();

            foreach (var tie in ties)
            {
                var cell = (
                    X: (int)Math.Floor(tie.position.X / gridSize),
                    Y: (int)Math.Floor(tie.position.Y / gridSize),
                    Z: (int)Math.Floor(tie.position.Z / gridSize)
                );

                grid[cell] = grid.GetValueOrDefault(cell, 0) + 1;
            }

            return grid;
        }

        private static int CountMismatches(List<KeyValuePair<int, int>>? a, List<KeyValuePair<int, int>>? b)
        {
            if (a == null || b == null)
                return (a?.Count ?? 0) + (b?.Count ?? 0);

            int mismatches = 0;
            int min = Math.Min(a.Count, b.Count);
            for (int i = 0; i < min; i++)
            {
                if (a[i].Key != b[i].Key || a[i].Value != b[i].Value)
                    mismatches++;
            }
            mismatches += Math.Abs(a.Count - b.Count);
            return mismatches;
        }
    }
}

