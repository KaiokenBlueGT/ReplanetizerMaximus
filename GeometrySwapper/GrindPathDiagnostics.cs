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
using System.Text;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using OpenTK.Mathematics;

namespace GeometrySwapper
{
    /// <summary>
    /// Provides diagnostic tools for analyzing grind paths and splines in Ratchet & Clank levels
    /// </summary>
    public static class GrindPathDiagnostics
    {
        /// <summary>
        /// Analyzes and outputs detailed information about grind paths in the specified level
        /// </summary>
        /// <param name="level">The level to analyze</param>
        /// <param name="outputPath">Path to write the diagnostic report</param>
        /// <param name="label">Label for identification in the report</param>
        /// <param name="includeVertexData">Whether to include detailed spline vertex data</param>
        public static void AnalyzeGrindPaths(Level level, string outputPath, string label, bool includeVertexData = false)
        {
            if (level == null)
            {
                Console.WriteLine("❌ Cannot analyze grind paths: Invalid level data");
                return;
            }

            Console.WriteLine($"Analyzing grind paths in {label}...");

            using (StreamWriter writer = new StreamWriter(outputPath))
            {
                writer.WriteLine($"=== Grind Path Analysis for {label} ===");
                writer.WriteLine($"Game: {level.game.num}");
                writer.WriteLine($"Level Path: {level.path}");
                writer.WriteLine($"Analysis Date: {DateTime.Now}\n");

                if (level.grindPaths == null || level.grindPaths.Count == 0)
                {
                    writer.WriteLine("No grind paths found in this level.");
                    Console.WriteLine("❌ No grind paths found in this level");
                    return;
                }

                writer.WriteLine($"Total Grind Paths: {level.grindPaths.Count}");
                writer.WriteLine($"Total Splines: {level.splines.Count}\n");

                // Analyze each grind path
                writer.WriteLine("=== GRIND PATH DETAILS ===");
                foreach (var path in level.grindPaths)
                {
                    writer.WriteLine($"\n--- Grind Path ID: {path.id} ---");
                    writer.WriteLine($"Position: ({path.position.X}, {path.position.Y}, {path.position.Z})");
                    writer.WriteLine($"Radius: {path.radius}");
                    writer.WriteLine($"Wrap: {path.wrap}");
                    writer.WriteLine($"Inactive: {path.inactive}");
                    writer.WriteLine($"Unknown value at 0x10: 0x{path.unk0x10:X8}");

                    // Spline analysis
                    if (path.spline != null)
                    {
                        writer.WriteLine($"Associated Spline ID: {path.spline.id}");
                        writer.WriteLine($"Spline Vertex Count: {path.spline.GetVertexCount()}");
                        writer.WriteLine($"Spline Position: ({path.spline.position.X}, {path.spline.position.Y}, {path.spline.position.Z})");
                        writer.WriteLine($"Spline Rotation: {path.spline.rotation}");
                        writer.WriteLine($"Spline Scale: ({path.spline.scale.X}, {path.spline.scale.Y}, {path.spline.scale.Z})");

                        if (includeVertexData && path.spline.GetVertexCount() > 0)
                        {
                            writer.WriteLine("\nSpline Vertices:");
                            for (int i = 0; i < path.spline.GetVertexCount(); i++)
                            {
                                if (i % 10 == 0) // Only show every 10th vertex to avoid excessive output
                                {
                                    Vector3 vertex = path.spline.GetVertex(i);
                                    writer.WriteLine($"  Vertex {i}: ({vertex.X}, {vertex.Y}, {vertex.Z}), W={path.spline.wVals[i]}");
                                }
                            }

                            // Always show the first and last vertex
                            if (path.spline.GetVertexCount() > 10)
                            {
                                Vector3 lastVertex = path.spline.GetVertex(path.spline.GetVertexCount() - 1);
                                writer.WriteLine($"  Vertex {path.spline.GetVertexCount() - 1}: ({lastVertex.X}, {lastVertex.Y}, {lastVertex.Z}), W={path.spline.wVals[path.spline.GetVertexCount() - 1]}");
                            }
                        }
                    }
                    else
                    {
                        writer.WriteLine("WARNING: No associated spline for this grind path!");
                    }
                }

                // Analyze memory layout and byte structure
                writer.WriteLine("\n=== MEMORY LAYOUT ANALYSIS ===");
                writer.WriteLine($"Grind Path Element Size: {GrindPath.ELEMENTSIZE} bytes");

                if (level.grindPaths.Count > 0)
                {
                    var firstPath = level.grindPaths[0];
                    byte[] firstPathBytes = firstPath.ToByteArray();
                    writer.WriteLine($"\nFirst Grind Path Byte Structure:");
                    writer.WriteLine($"0x00-0x0C: Position ({BitConverter.ToSingle(firstPathBytes, 0)}, {BitConverter.ToSingle(firstPathBytes, 4)}, {BitConverter.ToSingle(firstPathBytes, 8)})");
                    writer.WriteLine($"0x0C-0x10: Radius {BitConverter.ToSingle(firstPathBytes, 12)}");
                    writer.WriteLine($"0x10-0x14: Unknown Value 0x{BitConverter.ToInt32(firstPathBytes, 16):X8}");
                    writer.WriteLine($"0x14-0x18: Wrap {BitConverter.ToInt32(firstPathBytes, 20)}");
                    writer.WriteLine($"0x18-0x1C: Inactive {BitConverter.ToInt32(firstPathBytes, 24)}");
                }

                // Check for orphaned splines and orphaned grind paths
                writer.WriteLine("\n=== INTEGRITY CHECKS ===");

                // Find orphaned splines (splines not referenced by any grind path)
                var usedSplineIds = level.grindPaths.Where(p => p.spline != null).Select(p => p.spline.id).ToHashSet();
                var orphanedSplines = level.splines.Where(s => !usedSplineIds.Contains(s.id)).ToList();

                writer.WriteLine($"Orphaned Splines (not referenced by any grind path): {orphanedSplines.Count}");
                if (orphanedSplines.Count > 0)
                {
                    writer.WriteLine("Orphaned Spline IDs:");
                    foreach (var spline in orphanedSplines)
                    {
                        writer.WriteLine($"  Spline ID {spline.id} - Vertex Count: {spline.GetVertexCount()}");
                    }
                }

                // Find invalid grind path-spline associations
                var allSplineIds = level.splines.Select(s => s.id).ToHashSet();
                var invalidGrindPaths = level.grindPaths.Where(p => p.spline != null && !allSplineIds.Contains(p.spline.id)).ToList();

                writer.WriteLine($"\nGrind Paths with Invalid Spline References: {invalidGrindPaths.Count}");
                if (invalidGrindPaths.Count > 0)
                {
                    foreach (var path in invalidGrindPaths)
                    {
                        writer.WriteLine($"  Grind Path ID {path.id} references non-existent Spline ID {path.spline.id}");
                    }
                }

                // Check for duplicate grind path IDs
                var grindPathIds = level.grindPaths.Select(p => p.id).ToList();
                var duplicateIds = grindPathIds.GroupBy(id => id)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToList();

                writer.WriteLine($"\nDuplicate Grind Path IDs: {duplicateIds.Count}");
                if (duplicateIds.Count > 0)
                {
                    writer.WriteLine("The following grind path IDs appear multiple times:");
                    foreach (var id in duplicateIds)
                    {
                        writer.WriteLine($"  ID {id} appears {grindPathIds.Count(i => i == id)} times");
                    }
                }

                // Check for extremely large or small splines
                writer.WriteLine("\n=== SPLINE SIZE ANALYSIS ===");
                var splineSizes = level.splines.Select(s => s.GetVertexCount()).ToList();
                if (splineSizes.Count > 0)
                {
                    writer.WriteLine($"Min Spline Vertex Count: {splineSizes.Min()}");
                    writer.WriteLine($"Max Spline Vertex Count: {splineSizes.Max()}");
                    writer.WriteLine($"Average Spline Vertex Count: {splineSizes.Average():F2}");

                    // Identify unusually large splines
                    var largeSplines = level.splines
                        .Where(s => s.GetVertexCount() > 500) // Arbitrarily chosen threshold
                        .OrderByDescending(s => s.GetVertexCount())
                        .ToList();

                    if (largeSplines.Count > 0)
                    {
                        writer.WriteLine("\nUnusually Large Splines:");
                        foreach (var spline in largeSplines)
                        {
                            writer.WriteLine($"  Spline ID {spline.id} - {spline.GetVertexCount()} vertices");
                        }
                    }
                }

                // Analyze wVals patterns
                writer.WriteLine("\n=== SPLINE W VALUES ANALYSIS ===");
                if (level.splines.Count > 0 && level.splines[0].wVals.Length > 0)
                {
                    var firstSpline = level.splines[0];
                    writer.WriteLine($"First Spline (ID {firstSpline.id}) W Values Pattern:");
                    writer.WriteLine($"  First W: {firstSpline.wVals[0]}");
                    writer.WriteLine($"  Last W: {firstSpline.wVals[firstSpline.wVals.Length - 1]}");

                    if (firstSpline.wVals.Length > 1)
                    {
                        float minW = firstSpline.wVals.Min();
                        float maxW = firstSpline.wVals.Max();
                        float avgW = firstSpline.wVals.Average();
                        writer.WriteLine($"  Min W: {minW}");
                        writer.WriteLine($"  Max W: {maxW}");
                        writer.WriteLine($"  Avg W: {avgW:F4}");
                        writer.WriteLine($"  W Range: {maxW - minW:F4}");
                    }
                }
            }

            Console.WriteLine($"✅ Grind path analysis complete - Report saved to {outputPath}");
        }

        /// <summary>
        /// Compares grind paths between two levels to identify differences
        /// </summary>
        /// <param name="sourceLevel">First level to compare</param>
        /// <param name="targetLevel">Second level to compare</param>
        /// <param name="outputPath">Path to save comparison report</param>
        /// <param name="sourceLabel">Label for the first level</param>
        /// <param name="targetLabel">Label for the second level</param>
        public static void CompareGrindPaths(Level sourceLevel, Level targetLevel, string outputPath,
                                          string sourceLabel, string targetLabel)
        {
            if (sourceLevel == null || targetLevel == null)
            {
                Console.WriteLine("❌ Cannot compare grind paths: Invalid level data");
                return;
            }

            Console.WriteLine($"Comparing grind paths between {sourceLabel} and {targetLabel}...");

            using (StreamWriter writer = new StreamWriter(outputPath))
            {
                writer.WriteLine($"=== Grind Path Comparison ===");
                writer.WriteLine($"Source: {sourceLabel} ({sourceLevel.game.num})");
                writer.WriteLine($"Target: {targetLabel} ({targetLevel.game.num})");
                writer.WriteLine($"Comparison Date: {DateTime.Now}\n");

                // Count comparisons
                writer.WriteLine("=== COUNT COMPARISON ===");
                writer.WriteLine($"Source Grind Path Count: {sourceLevel.grindPaths?.Count ?? 0}");
                writer.WriteLine($"Target Grind Path Count: {targetLevel.grindPaths?.Count ?? 0}");
                writer.WriteLine($"Source Spline Count: {sourceLevel.splines?.Count ?? 0}");
                writer.WriteLine($"Target Spline Count: {targetLevel.splines?.Count ?? 0}");

                // If either level has no grind paths, we can't do a detailed comparison
                if ((sourceLevel.grindPaths == null || sourceLevel.grindPaths.Count == 0) ||
                    (targetLevel.grindPaths == null || targetLevel.grindPaths.Count == 0))
                {
                    writer.WriteLine("\nDetailed comparison not possible - one or both levels have no grind paths.");
                    Console.WriteLine("⚠️ Detailed comparison not possible - one or both levels have no grind paths");
                    return;
                }

                // Compare grind paths
                writer.WriteLine("\n=== GRIND PATH PROPERTY COMPARISON ===");

                int commonCount = Math.Min(sourceLevel.grindPaths.Count, targetLevel.grindPaths.Count);
                writer.WriteLine($"\nComparing first {commonCount} grind paths from each level:");

                for (int i = 0; i < commonCount; i++)
                {
                    var sourcePath = sourceLevel.grindPaths[i];
                    var targetPath = targetLevel.grindPaths[i];

                    writer.WriteLine($"\n--- Grind Path Index {i} ---");
                    writer.WriteLine($"  ID: {sourcePath.id} vs {targetPath.id}");

                    // Compare positions
                    bool positionMatches = sourcePath.position == targetPath.position;
                    writer.WriteLine($"  Position: {FormatVector3(sourcePath.position)} vs {FormatVector3(targetPath.position)}{(positionMatches ? " ✓" : " ≠")}");

                    // Compare radius
                    bool radiusMatches = Math.Abs(sourcePath.radius - targetPath.radius) < 0.001f;
                    writer.WriteLine($"  Radius: {sourcePath.radius} vs {targetPath.radius}{(radiusMatches ? " ✓" : " ≠")}");

                    // Compare other properties
                    CompareAndWrite(writer, "  Wrap", sourcePath.wrap, targetPath.wrap);
                    CompareAndWrite(writer, "  Inactive", sourcePath.inactive, targetPath.inactive);
                    CompareAndWrite(writer, "  Unknown 0x10", $"0x{sourcePath.unk0x10:X8}", $"0x{targetPath.unk0x10:X8}");

                    // Compare splines
                    if (sourcePath.spline != null && targetPath.spline != null)
                    {
                        writer.WriteLine("\n  Spline Comparison:");
                        CompareAndWrite(writer, "    ID", sourcePath.spline.id, targetPath.spline.id);
                        CompareAndWrite(writer, "    Vertex Count", sourcePath.spline.GetVertexCount(), targetPath.spline.GetVertexCount());
                        CompareAndWrite(writer, "    Position", FormatVector3(sourcePath.spline.position), FormatVector3(targetPath.spline.position));
                        CompareAndWrite(writer, "    Scale", FormatVector3(sourcePath.spline.scale), FormatVector3(targetPath.spline.scale));

                        // Compare vertex positions (sample only first and last)
                        if (sourcePath.spline.GetVertexCount() > 0 && targetPath.spline.GetVertexCount() > 0)
                        {
                            writer.WriteLine("\n    Vertex Comparison (Sample):");
                            var sourceFirstVertex = sourcePath.spline.GetVertex(0);
                            var targetFirstVertex = targetPath.spline.GetVertex(0);
                            writer.WriteLine($"      First Vertex: {FormatVector3(sourceFirstVertex)} vs {FormatVector3(targetFirstVertex)}");

                            var sourceLastVertex = sourcePath.spline.GetVertex(sourcePath.spline.GetVertexCount() - 1);
                            var targetLastVertex = targetPath.spline.GetVertex(targetPath.spline.GetVertexCount() - 1);
                            writer.WriteLine($"      Last Vertex: {FormatVector3(sourceLastVertex)} vs {FormatVector3(targetLastVertex)}");
                        }
                    }
                    else if (sourcePath.spline == null && targetPath.spline == null)
                    {
                        writer.WriteLine("\n  Both grind paths have no associated spline");
                    }
                    else
                    {
                        writer.WriteLine($"\n  Spline association mismatch: {(sourcePath.spline != null ? "Source has spline" : "Source has no spline")} vs {(targetPath.spline != null ? "Target has spline" : "Target has no spline")}");
                    }
                }

                // Analysis of what would need to be transferred from source to target
                writer.WriteLine("\n=== TRANSFER ANALYSIS ===");
                if (sourceLevel.grindPaths.Count > targetLevel.grindPaths.Count)
                {
                    int pathsToAdd = sourceLevel.grindPaths.Count - targetLevel.grindPaths.Count;
                    writer.WriteLine($"To match source, {pathsToAdd} grind paths would need to be added to target");

                    writer.WriteLine("\nPaths that would need to be added:");
                    for (int i = targetLevel.grindPaths.Count; i < sourceLevel.grindPaths.Count; i++)
                    {
                        var sourcePath = sourceLevel.grindPaths[i];
                        writer.WriteLine($"  Source Path ID {sourcePath.id} at position {FormatVector3(sourcePath.position)}, radius {sourcePath.radius}");
                    }
                }
                else if (targetLevel.grindPaths.Count > sourceLevel.grindPaths.Count)
                {
                    int pathsToRemove = targetLevel.grindPaths.Count - sourceLevel.grindPaths.Count;
                    writer.WriteLine($"To match source, {pathsToRemove} grind paths would need to be removed from target");
                }
                else
                {
                    writer.WriteLine("Both levels have the same number of grind paths");
                }
            }

            Console.WriteLine($"✅ Grind path comparison complete - Report saved to {outputPath}");
        }

        /// <summary>
        /// Performs detailed analysis of spline vertex data
        /// </summary>
        /// <param name="level">The level to analyze</param>
        /// <param name="outputPath">Path to save the detailed spline analysis</param>
        public static void AnalyzeSplineVertexData(Level level, string outputPath)
        {
            if (level == null || level.splines == null || level.splines.Count == 0)
            {
                Console.WriteLine("❌ Cannot analyze spline data: Level has no splines");
                return;
            }

            Console.WriteLine("Analyzing spline vertex data...");

            using (StreamWriter writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("=== Spline Vertex Data Analysis ===");
                writer.WriteLine($"Level: {Path.GetFileName(level.path)}");
                writer.WriteLine($"Game: {level.game.num}");
                writer.WriteLine($"Total Splines: {level.splines.Count}\n");

                // Analyze each spline's vertex data
                foreach (var spline in level.splines)
                {
                    int vertexCount = spline.GetVertexCount();

                    writer.WriteLine($"--- Spline ID {spline.id} ---");
                    writer.WriteLine($"Vertex Count: {vertexCount}");

                    if (vertexCount == 0)
                    {
                        writer.WriteLine("WARNING: Spline has no vertices!");
                        continue;
                    }

                    // Calculate bounding box and other metrics
                    Vector3 min = new Vector3(float.MaxValue);
                    Vector3 max = new Vector3(float.MinValue);
                    float totalLength = 0f;
                    Vector3? previousVertex = null;

                    for (int i = 0; i < vertexCount; i++)
                    {
                        Vector3 vertex = spline.GetVertex(i);

                        // Update min/max
                        min.X = Math.Min(min.X, vertex.X);
                        min.Y = Math.Min(min.Y, vertex.Y);
                        min.Z = Math.Min(min.Z, vertex.Z);

                        max.X = Math.Max(max.X, vertex.X);
                        max.Y = Math.Max(max.Y, vertex.Y);
                        max.Z = Math.Max(max.Z, vertex.Z);

                        // Calculate segment length
                        if (previousVertex.HasValue)
                        {
                            totalLength += (vertex - previousVertex.Value).Length;
                        }
                        previousVertex = vertex;
                    }

                    // Calculate metrics
                    Vector3 size = max - min;
                    Vector3 center = (min + max) / 2;
                    float avgSegmentLength = (vertexCount > 1) ? totalLength / (vertexCount - 1) : 0;

                    writer.WriteLine($"Bounding Box Min: {FormatVector3(min)}");
                    writer.WriteLine($"Bounding Box Max: {FormatVector3(max)}");
                    writer.WriteLine($"Bounding Box Size: {FormatVector3(size)}");
                    writer.WriteLine($"Bounding Box Center: {FormatVector3(center)}");
                    writer.WriteLine($"Total Path Length: {totalLength:F2} units");
                    writer.WriteLine($"Average Segment Length: {avgSegmentLength:F4} units");

                    // Detect if spline appears to be closed (first vertex approximately equals last vertex)
                    if (vertexCount > 1)
                    {
                        Vector3 first = spline.GetVertex(0);
                        Vector3 last = spline.GetVertex(vertexCount - 1);
                        float distanceFirstToLast = (first - last).Length;

                        writer.WriteLine($"First-to-Last Vertex Distance: {distanceFirstToLast:F4} units");

                        if (distanceFirstToLast < avgSegmentLength * 0.5)
                            writer.WriteLine("This appears to be a CLOSED spline (first and last vertices are very close)");
                        else
                            writer.WriteLine("This appears to be an OPEN spline (first and last vertices are far apart)");
                    }

                    // Analyze W values
                    if (spline.wVals.Length > 0)
                    {
                        float minW = spline.wVals.Min();
                        float maxW = spline.wVals.Max();
                        writer.WriteLine($"W Values Range: {minW:F4} to {maxW:F4}");

                        // Check if W values are monotonically increasing
                        bool wIncreasing = true;
                        for (int i = 1; i < spline.wVals.Length; i++)
                        {
                            if (spline.wVals[i] < spline.wVals[i - 1])
                            {
                                wIncreasing = false;
                                break;
                            }
                        }
                        writer.WriteLine($"W Values Monotonically Increasing: {wIncreasing}");
                    }

                    writer.WriteLine();
                }
            }

            Console.WriteLine($"✅ Spline vertex analysis complete - Report saved to {outputPath}");
        }

        /// <summary>
        /// Checks for potential issues that could cause game crashes related to grind paths
        /// </summary>
        /// <param name="level">The level to analyze</param>
        /// <param name="outputPath">Path to save the crash risk analysis</param>
        public static void AnalyzeGrindPathCrashRisks(Level level, string outputPath)
        {
            if (level == null)
            {
                Console.WriteLine("❌ Cannot analyze grind path crash risks: Invalid level data");
                return;
            }

            Console.WriteLine("Analyzing grind path crash risks...");

            using (StreamWriter writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("=== Grind Path Crash Risk Analysis ===");
                writer.WriteLine($"Level: {Path.GetFileName(level.path)}");
                writer.WriteLine($"Game: {level.game.num}");
                writer.WriteLine($"Analysis Date: {DateTime.Now}\n");

                int riskCount = 0;
                List<string> criticalRisks = new List<string>();
                List<string> highRisks = new List<string>();
                List<string> mediumRisks = new List<string>();
                List<string> lowRisks = new List<string>();

                // Check 1: Missing or empty grind paths list
                if (level.grindPaths == null || level.grindPaths.Count == 0)
                {
                    lowRisks.Add("Level has no grind paths. This is not a risk unless the game expects grind paths to be present.");
                }

                // Check 2: Missing or empty splines list
                if (level.splines == null || level.splines.Count == 0)
                {
                    if (level.grindPaths?.Count > 0)
                    {
                        criticalRisks.Add("Level has grind paths but no splines. This will likely cause a crash when the game tries to access spline data.");
                    }
                    else
                    {
                        lowRisks.Add("Level has no splines. This is not a risk since there are also no grind paths.");
                    }
                }

                // If no grind paths or splines, we can't do much more analysis
                if ((level.grindPaths == null || level.grindPaths.Count == 0) ||
                    (level.splines == null || level.splines.Count == 0))
                {
                    if (criticalRisks.Count > 0)
                    {
                        writer.WriteLine("=== CRITICAL RISKS ===");
                        foreach (var risk in criticalRisks)
                        {
                            writer.WriteLine($"- {risk}");
                        }
                    }

                    if (highRisks.Count > 0)
                    {
                        writer.WriteLine("\n=== HIGH RISKS ===");
                        foreach (var risk in highRisks)
                        {
                            writer.WriteLine($"- {risk}");
                        }
                    }

                    if (mediumRisks.Count > 0)
                    {
                        writer.WriteLine("\n=== MEDIUM RISKS ===");
                        foreach (var risk in mediumRisks)
                        {
                            writer.WriteLine($"- {risk}");
                        }
                    }

                    if (lowRisks.Count > 0)
                    {
                        writer.WriteLine("\n=== LOW RISKS ===");
                        foreach (var risk in lowRisks)
                        {
                            writer.WriteLine($"- {risk}");
                        }
                    }

                    Console.WriteLine("✅ Grind path crash risk analysis complete - Report saved to {outputPath}");
                    return;
                }

                // Check 3: Grind paths with null splines
                var pathsWithNullSplines = level.grindPaths.Where(p => p.spline == null).ToList();
                if (pathsWithNullSplines.Count > 0)
                {
                    criticalRisks.Add($"Found {pathsWithNullSplines.Count} grind path(s) with null spline references. These will likely cause crashes when accessed.");
                    writer.WriteLine("\n--- Grind Paths with Null Spline References ---");
                    foreach (var path in pathsWithNullSplines)
                    {
                        writer.WriteLine($"  Grind Path ID {path.id} at position {FormatVector3(path.position)} has a null spline reference");
                    }
                }

                // Check 4: Grind paths with invalid spline references
                var allSplineIds = level.splines.Select(s => s.id).ToHashSet();
                var pathsWithInvalidSplines = level.grindPaths
                    .Where(p => p.spline != null && !allSplineIds.Contains(p.spline.id))
                    .ToList();

                if (pathsWithInvalidSplines.Count > 0)
                {
                    criticalRisks.Add($"Found {pathsWithInvalidSplines.Count} grind path(s) with invalid spline references. These will likely cause crashes when accessed.");
                    writer.WriteLine("\n--- Grind Paths with Invalid Spline References ---");
                    foreach (var path in pathsWithInvalidSplines)
                    {
                        writer.WriteLine($"  Grind Path ID {path.id} references non-existent Spline ID {path.spline.id}");
                    }
                }

                // Check 5: Duplicate grind path IDs
                var grindPathIds = level.grindPaths.Select(p => p.id).ToList();
                var duplicateIds = grindPathIds.GroupBy(id => id)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToList();

                if (duplicateIds.Count > 0)
                {
                    highRisks.Add($"Found {duplicateIds.Count} duplicate grind path ID(s). This may cause unpredictable behavior or crashes.");
                    writer.WriteLine("\n--- Duplicate Grind Path IDs ---");
                    foreach (var id in duplicateIds)
                    {
                        writer.WriteLine($"  ID {id} appears {grindPathIds.Count(i => i == id)} times");
                    }
                }

                // Check 6: Splines with no vertices
                var splinesWithNoVertices = level.splines.Where(s => s.GetVertexCount() == 0).ToList();
                if (splinesWithNoVertices.Count > 0)
                {
                    criticalRisks.Add($"Found {splinesWithNoVertices.Count} spline(s) with no vertices. These will likely cause crashes if used.");
                    writer.WriteLine("\n--- Splines with No Vertices ---");
                    foreach (var spline in splinesWithNoVertices)
                    {
                        writer.WriteLine($"  Spline ID {spline.id} has no vertices");

                        // Check if any grind paths reference this empty spline
                        var affectedPaths = level.grindPaths.Where(p => p.spline != null && p.spline.id == spline.id).ToList();
                        if (affectedPaths.Count > 0)
                        {
                            writer.WriteLine($"    Referenced by {affectedPaths.Count} grind path(s):");
                            foreach (var path in affectedPaths)
                            {
                                writer.WriteLine($"      Grind Path ID {path.id}");
                            }
                        }
                    }
                }

                // Check 7: Extremely large splines (potential memory issues)
                var veryLargeSplines = level.splines.Where(s => s.GetVertexCount() > 2000).ToList();
                if (veryLargeSplines.Count > 0)
                {
                    mediumRisks.Add($"Found {veryLargeSplines.Count} extremely large spline(s) (>2000 vertices). These may cause memory or performance issues.");
                    writer.WriteLine("\n--- Extremely Large Splines ---");
                    foreach (var spline in veryLargeSplines)
                    {
                        writer.WriteLine($"  Spline ID {spline.id} has {spline.GetVertexCount()} vertices");

                        // Check if any grind paths reference this large spline
                        var affectedPaths = level.grindPaths.Where(p => p.spline != null && p.spline.id == spline.id).ToList();
                        if (affectedPaths.Count > 0)
                        {
                            writer.WriteLine($"    Referenced by {affectedPaths.Count} grind path(s):");
                            foreach (var path in affectedPaths)
                            {
                                writer.WriteLine($"      Grind Path ID {path.id}");
                            }
                        }
                    }
                }

                // Check 8: Grind paths with extremely small or zero radius
                var pathsWithSmallRadius = level.grindPaths.Where(p => p.radius < 0.1f).ToList();
                if (pathsWithSmallRadius.Count > 0)
                {
                    mediumRisks.Add($"Found {pathsWithSmallRadius.Count} grind path(s) with extremely small radius (<0.1). This may cause physics or collision issues.");
                    writer.WriteLine("\n--- Grind Paths with Extremely Small Radius ---");
                    foreach (var path in pathsWithSmallRadius)
                    {
                        writer.WriteLine($"  Grind Path ID {path.id} has radius {path.radius}");
                    }
                }

                // Check 9: Splines with vertex buffer/wVals size mismatch
                var splinesWithSizeMismatch = level.splines
                    .Where(s => s.vertexBuffer.Length / 3 != s.wVals.Length && s.vertexBuffer.Length > 0)
                    .ToList();

                if (splinesWithSizeMismatch.Count > 0)
                {
                    criticalRisks.Add($"Found {splinesWithSizeMismatch.Count} spline(s) with vertex buffer and wVals length mismatch. This will likely cause crashes.");
                    writer.WriteLine("\n--- Splines with Buffer Size Mismatch ---");
                    foreach (var spline in splinesWithSizeMismatch)
                    {
                        writer.WriteLine($"  Spline ID {spline.id} has {spline.vertexBuffer.Length / 3} vertices but {spline.wVals.Length} wVals");
                    }
                }

                // Check 10: Very close grind paths (potential duplicate)
                var grindPathsByPosition = new Dictionary<string, List<GrindPath>>();
                foreach (var path in level.grindPaths)
                {
                    // Round position to nearest unit for grouping
                    string posKey = $"{Math.Round(path.position.X)},{Math.Round(path.position.Y)},{Math.Round(path.position.Z)}";
                    if (!grindPathsByPosition.ContainsKey(posKey))
                        grindPathsByPosition[posKey] = new List<GrindPath>();

                    grindPathsByPosition[posKey].Add(path);
                }

                var overlappingPaths = grindPathsByPosition
                    .Where(kvp => kvp.Value.Count > 1)
                    .ToList();

                if (overlappingPaths.Count > 0)
                {
                    lowRisks.Add($"Found {overlappingPaths.Count} position(s) with multiple grind paths. These might be unintentional duplicates.");
                    writer.WriteLine("\n--- Potentially Duplicate Grind Paths ---");
                    foreach (var group in overlappingPaths)
                    {
                        writer.WriteLine($"  Position (approx): {group.Key}");
                        writer.WriteLine($"  Contains {group.Value.Count} grind paths:");
                        foreach (var path in group.Value)
                        {
                            writer.WriteLine($"    ID {path.id}, Exact pos: {FormatVector3(path.position)}, Radius: {path.radius}");
                        }
                    }
                }

                // Summary
                riskCount = criticalRisks.Count + highRisks.Count + mediumRisks.Count + lowRisks.Count;
                writer.WriteLine($"\n=== RISK SUMMARY ===");
                writer.WriteLine($"Total Risks Identified: {riskCount}");
                writer.WriteLine($"  Critical: {criticalRisks.Count}");
                writer.WriteLine($"  High: {highRisks.Count}");
                writer.WriteLine($"  Medium: {mediumRisks.Count}");
                writer.WriteLine($"  Low: {lowRisks.Count}");

                if (riskCount == 0)
                {
                    writer.WriteLine("\nNo significant risks detected. Grind paths appear to be well-formed.");
                }

                // Detailed risk lists
                if (criticalRisks.Count > 0)
                {
                    writer.WriteLine("\n=== CRITICAL RISKS ===");
                    foreach (var risk in criticalRisks)
                    {
                        writer.WriteLine($"- {risk}");
                    }
                }

                if (highRisks.Count > 0)
                {
                    writer.WriteLine("\n=== HIGH RISKS ===");
                    foreach (var risk in highRisks)
                    {
                        writer.WriteLine($"- {risk}");
                    }
                }

                if (mediumRisks.Count > 0)
                {
                    writer.WriteLine("\n=== MEDIUM RISKS ===");
                    foreach (var risk in mediumRisks)
                    {
                        writer.WriteLine($"- {risk}");
                    }
                }

                if (lowRisks.Count > 0)
                {
                    writer.WriteLine("\n=== LOW RISKS ===");
                    foreach (var risk in lowRisks)
                    {
                        writer.WriteLine($"- {risk}");
                    }
                }
            }

            Console.WriteLine($"✅ Grind path crash risk analysis complete - Report saved to {outputPath}");
        }

        /// <summary>
        /// Dumps raw binary data for grind paths and splines for direct comparison
        /// </summary>
        /// <param name="level">The level to analyze</param>
        /// <param name="outputPath">Path to save the binary data report</param>
        public static void DumpGrindPathBinaryData(Level level, string outputPath)
        {
            if (level == null)
            {
                Console.WriteLine("❌ Cannot dump grind path binary data: Invalid level data");
                return;
            }

            Console.WriteLine("Dumping grind path and spline binary data...");

            using (StreamWriter writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("=== Grind Path Binary Data Dump ===");
                writer.WriteLine($"Level: {Path.GetFileName(level.path)}");
                writer.WriteLine($"Game: {level.game.num}");
                writer.WriteLine($"Analysis Date: {DateTime.Now}\n");

                // Dump grind path binary data
                if (level.grindPaths == null || level.grindPaths.Count == 0)
                {
                    writer.WriteLine("No grind paths found in this level.");
                }
                else
                {
                    writer.WriteLine($"=== GRIND PATH BINARY DATA ({level.grindPaths.Count} paths) ===");

                    foreach (var path in level.grindPaths)
                    {
                        writer.WriteLine($"\n--- Grind Path ID {path.id} ---");

                        byte[] bytes = path.ToByteArray();
                        writer.WriteLine($"Raw Byte Data (Length: {bytes.Length}):");
                        writer.WriteLine(FormatByteArray(bytes));

                        // Break down the byte structure
                        writer.WriteLine("\nStructure Breakdown:");
                        writer.WriteLine($"0x00-0x0C: Position ({BitConverter.ToSingle(bytes, 0):F3}, {BitConverter.ToSingle(bytes, 4):F3}, {BitConverter.ToSingle(bytes, 8):F3})");
                        writer.WriteLine($"0x0C-0x10: Radius {BitConverter.ToSingle(bytes, 12):F3}");
                        writer.WriteLine($"0x10-0x14: Unknown Value 0x{BitConverter.ToInt32(bytes, 16):X8} ({BitConverter.ToInt32(bytes, 16)})");
                        writer.WriteLine($"0x14-0x18: Wrap {BitConverter.ToInt32(bytes, 20)}");
                        writer.WriteLine($"0x18-0x1C: Inactive {BitConverter.ToInt32(bytes, 24)}");
                    }
                }

                // Dump spline binary data (sample)
                if (level.splines == null || level.splines.Count == 0)
                {
                    writer.WriteLine("\nNo splines found in this level.");
                }
                else
                {
                    writer.WriteLine($"\n=== SPLINE BINARY DATA (Sample from {level.splines.Count} splines) ===");

                    // Get a representative sample (first spline, and one with many vertices if available)
                    var firstSpline = level.splines[0];
                    var largeSpline = level.splines.OrderByDescending(s => s.GetVertexCount()).FirstOrDefault();

                    // Display first spline
                    writer.WriteLine($"\n--- First Spline (ID {firstSpline.id}) ---");
                    writer.WriteLine($"Vertex Count: {firstSpline.GetVertexCount()}");
                    byte[] firstSplineBytes = firstSpline.ToByteArray();
                    writer.WriteLine($"Binary Length: {firstSplineBytes.Length} bytes");
                    writer.WriteLine("Header Structure:");
                    writer.WriteLine($"0x00-0x04: Vertex Count {BitConverter.ToUInt32(firstSplineBytes, 0)}");

                    if (firstSpline.GetVertexCount() > 0)
                    {
                        writer.WriteLine("\nSample of First Few Vertices:");
                        int samplesToShow = Math.Min(3, firstSpline.GetVertexCount());
                        for (int i = 0; i < samplesToShow; i++)
                        {
                            int offset = 0x10 + (i * 0x10);
                            writer.WriteLine($"Vertex {i} at offset 0x{offset:X4}:");
                            writer.WriteLine($"  X: {BitConverter.ToSingle(firstSplineBytes, offset):F4}");
                            writer.WriteLine($"  Y: {BitConverter.ToSingle(firstSplineBytes, offset + 4):F4}");
                            writer.WriteLine($"  Z: {BitConverter.ToSingle(firstSplineBytes, offset + 8):F4}");
                            writer.WriteLine($"  W: {BitConverter.ToSingle(firstSplineBytes, offset + 12):F4}");
                        }
                    }

                    // If we have a large spline different from the first one, show its info too
                    if (largeSpline != null && largeSpline.id != firstSpline.id)
                    {
                        writer.WriteLine($"\n--- Large Spline (ID {largeSpline.id}) ---");
                        writer.WriteLine($"Vertex Count: {largeSpline.GetVertexCount()}");
                        byte[] largeSplineBytes = largeSpline.ToByteArray();
                        writer.WriteLine($"Binary Length: {largeSplineBytes.Length} bytes");
                        writer.WriteLine($"Bytes per Vertex: {largeSplineBytes.Length / (float) largeSpline.GetVertexCount():F2}");
                    }
                }
            }

            Console.WriteLine($"✅ Grind path binary data dump complete - Report saved to {outputPath}");
        }

        /// <summary>
        /// Interactive console interface for grind path diagnostics
        /// </summary>
        public static void RunGrindPathDiagnosticsInteractive()
        {
            bool exit = false;
            while (!exit)
            {
                Console.Clear();
                Console.WriteLine("=== Grind Path Diagnostics Tool ===\n");
                Console.WriteLine("1. Analyze Single Level's Grind Paths");
                Console.WriteLine("2. Compare Grind Paths Between Two Levels");
                Console.WriteLine("3. Detailed Spline Vertex Analysis");
                Console.WriteLine("4. Analyze Crash Risks");
                Console.WriteLine("5. Dump Binary Grind Path Data");
                Console.WriteLine("0. Exit");

                Console.Write("\nSelect an option: ");
                string choice = Console.ReadLine()?.Trim() ?? "";

                switch (choice)
                {
                    case "1":
                        AnalyzeSingleLevelInteractive();
                        break;
                    case "2":
                        CompareLevelsInteractive();
                        break;
                    case "3":
                        AnalyzeSplineDataInteractive();
                        break;
                    case "4":
                        AnalyzeCrashRisksInteractive();
                        break;
                    case "5":
                        DumpBinaryDataInteractive();
                        break;
                    case "0":
                        exit = true;
                        break;
                    default:
                        Console.WriteLine("Invalid option. Press any key to try again...");
                        Console.ReadKey();
                        break;
                }
            }
        }

        #region Interactive Methods

        private static void AnalyzeSingleLevelInteractive()
        {
            Console.Clear();
            Console.WriteLine("=== Analyze Single Level's Grind Paths ===\n");

            // Get level path
            Console.Write("Enter path to engine.ps3 file: ");
            string levelPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(levelPath))
            {
                Console.WriteLine("❌ Invalid path or file does not exist");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return;
            }

            // Get output path
            string defaultOutputPath = Path.Combine(
                Path.GetDirectoryName(levelPath) ?? "",
                $"{Path.GetFileNameWithoutExtension(levelPath)}_grind_path_analysis.txt");

            Console.Write($"Output file path [{defaultOutputPath}]: ");
            string outputPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = defaultOutputPath;

            Console.Write("Include detailed vertex data? (y/n) [n]: ");
            bool includeVertexData = (Console.ReadLine()?.Trim().ToLower() == "y");

            try
            {
                Console.WriteLine($"\nLoading level from {levelPath}...");
                Level level = new Level(levelPath);
                Console.WriteLine($"✅ Level loaded successfully");

                AnalyzeGrindPaths(level, outputPath, Path.GetFileName(levelPath), includeVertexData);

                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
        }

        private static void CompareLevelsInteractive()
        {
            Console.Clear();
            Console.WriteLine("=== Compare Grind Paths Between Two Levels ===\n");

            // Get source level path
            Console.Write("Enter path to source engine.ps3 file: ");
            string sourcePath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(sourcePath))
            {
                Console.WriteLine("❌ Invalid source path or file does not exist");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return;
            }

            // Get target level path
            Console.Write("Enter path to target engine.ps3 file: ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(targetPath))
            {
                Console.WriteLine("❌ Invalid target path or file does not exist");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return;
            }

            // Get output path
            string defaultOutputPath = Path.Combine(
                Path.GetDirectoryName(targetPath) ?? "",
                $"grind_path_comparison_report.txt");

            Console.Write($"Output file path [{defaultOutputPath}]: ");
            string outputPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = defaultOutputPath;

            try
            {
                Console.WriteLine($"\nLoading source level from {sourcePath}...");
                Level sourceLevel = new Level(sourcePath);
                Console.WriteLine($"✅ Source level loaded successfully");

                Console.WriteLine($"\nLoading target level from {targetPath}...");
                Level targetLevel = new Level(targetPath);
                Console.WriteLine($"✅ Target level loaded successfully");

                string sourceLabel = Path.GetFileName(sourcePath);
                string targetLabel = Path.GetFileName(targetPath);

                CompareGrindPaths(
                    sourceLevel,
                    targetLevel,
                    outputPath,
                    sourceLabel,
                    targetLabel
                );

                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
        }

        private static void AnalyzeSplineDataInteractive()
        {
            Console.Clear();
            Console.WriteLine("=== Detailed Spline Vertex Analysis ===\n");

            // Get level path
            Console.Write("Enter path to engine.ps3 file: ");
            string levelPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(levelPath))
            {
                Console.WriteLine("❌ Invalid path or file does not exist");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return;
            }

            // Get output path
            string defaultOutputPath = Path.Combine(
                Path.GetDirectoryName(levelPath) ?? "",
                $"{Path.GetFileNameWithoutExtension(levelPath)}_spline_analysis.txt");

            Console.Write($"Output file path [{defaultOutputPath}]: ");
            string outputPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = defaultOutputPath;

            try
            {
                Console.WriteLine($"\nLoading level from {levelPath}...");
                Level level = new Level(levelPath);
                Console.WriteLine($"✅ Level loaded successfully");

                AnalyzeSplineVertexData(level, outputPath);

                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
        }

        private static void AnalyzeCrashRisksInteractive()
        {
            Console.Clear();
            Console.WriteLine("=== Analyze Grind Path Crash Risks ===\n");

            // Get level path
            Console.Write("Enter path to engine.ps3 file: ");
            string levelPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(levelPath))
            {
                Console.WriteLine("❌ Invalid path or file does not exist");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return;
            }

            // Get output path
            string defaultOutputPath = Path.Combine(
                Path.GetDirectoryName(levelPath) ?? "",
                $"{Path.GetFileNameWithoutExtension(levelPath)}_grind_path_crash_risks.txt");

            Console.Write($"Output file path [{defaultOutputPath}]: ");
            string outputPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = defaultOutputPath;

            try
            {
                Console.WriteLine($"\nLoading level from {levelPath}...");
                Level level = new Level(levelPath);
                Console.WriteLine($"✅ Level loaded successfully");

                AnalyzeGrindPathCrashRisks(level, outputPath);

                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
        }

        private static void DumpBinaryDataInteractive()
        {
            Console.Clear();
            Console.WriteLine("=== Dump Binary Grind Path Data ===\n");

            // Get level path
            Console.Write("Enter path to engine.ps3 file: ");
            string levelPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(levelPath))
            {
                Console.WriteLine("❌ Invalid path or file does not exist");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return;
            }

            // Get output path
            string defaultOutputPath = Path.Combine(
                Path.GetDirectoryName(levelPath) ?? "",
                $"{Path.GetFileNameWithoutExtension(levelPath)}_grind_path_binary.txt");

            Console.Write($"Output file path [{defaultOutputPath}]: ");
            string outputPath = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = defaultOutputPath;

            try
            {
                Console.WriteLine($"\nLoading level from {levelPath}...");
                Level level = new Level(levelPath);
                Console.WriteLine($"✅ Level loaded successfully");

                DumpGrindPathBinaryData(level, outputPath);

                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }
        }

        #endregion

        #region Helper Methods

        private static void CompareAndWrite<T>(StreamWriter writer, string propertyName, T value1, T value2)
        {
            bool matches = value1?.Equals(value2) ?? (value2 == null);
            writer.WriteLine($"{propertyName}: {value1} vs {value2}{(matches ? " ✓" : " ≠")}");
        }

        private static string FormatVector3(Vector3 vec)
        {
            return $"({vec.X:F3}, {vec.Y:F3}, {vec.Z:F3})";
        }

        private static string FormatByteArray(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                // Add offset at beginning of each line
                if (i % 16 == 0)
                {
                    if (i > 0) sb.AppendLine();
                    sb.Append($"{i:X4}: ");
                }
                else if (i % 8 == 0)
                {
                    sb.Append(" ");  // Add extra space every 8 bytes
                }

                sb.Append($"{bytes[i]:X2} ");
            }
            return sb.ToString();
        }

        #endregion
    }
}
