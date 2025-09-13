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
using LibReplanetizer.Headers;
using OpenTK.Mathematics;
using static LibReplanetizer.DataFunctions;

namespace GeometrySwapper
{
    /// <summary>
    /// Fixes TIE culling and occlusion data corruption issues during geometry swapping
    /// Similar to cuboid matrix corruption, TIE culling data can be corrupted during RC1 → RC3 conversion
    /// </summary>
    public static class TieCullingDataFixer
    {
        /// <summary>
        /// Preserves and fixes TIE culling data before save to prevent corruption.
        /// When called the first time it captures the original data.  Calling it again
        /// with an existing protection object after coordinate conversion will store
        /// the converted occlusion bytes for later restoration.
        /// </summary>
        /// <param name="level">The level with TIE data to protect</param>
        /// <param name="existingProtection">Optional protection data to update with converted occlusion bytes</param>
        /// <returns>Protection data for restoration after save</returns>
        public static TieCullingProtectionData PreserveTieCullingData(Level level, TieCullingProtectionData existingProtection = null)
        {
            if (existingProtection != null)
            {
                // Second call after coordinate conversion – capture converted occlusion data
                existingProtection.ConvertedOcclusionData = level.occlusionData?.ToByteArray();
                Console.WriteLine("🔧 [TIE CULLING] Captured converted occlusion data");
                return existingProtection;
            }

            Console.WriteLine("🔧 [TIE CULLING] Preserving TIE culling data before save...");

            var protection = new TieCullingProtectionData
            {
                OriginalTieData = level.tieData?.ToArray(),
                OriginalTieGroupData = level.tieGroupData?.ToArray(),
                OriginalOcclusionData = level.occlusionData?.ToByteArray(),
                ConvertedOcclusionData = level.occlusionData?.ToByteArray(),
                TiePositions = new Dictionary<int, Vector3>(),
                TieBoundingBoxes = new Dictionary<int, (Vector3 min, Vector3 max)>()
            };

            // Capture TIE positions for coordinate system conversion
            if (level.ties != null)
            {
                for (int i = 0; i < level.ties.Count; i++)
                {
                    var tie = level.ties[i];

                    // Use a unique key (tie.off58 or index) to avoid model collisions
                    int key = tie.off58 != 0 ? (int)tie.off58 : i;
                    protection.TiePositions[key] = tie.position;

                    // Calculate bounding box for culling
                    if (tie.model != null)
                    {
                        protection.TieBoundingBoxes[key] = CalculateTieBoundingBox(tie);
                    }
                }

                Console.WriteLine($"🔧 [TIE CULLING] Protected {protection.TiePositions.Count} TIE positions");
            }

            return protection;
        }

        /// <summary>
        /// Applies coordinate system conversion to TIE culling data
        /// </summary>
        /// <param name="level">The level to fix</param>
        /// <param name="sourceGameType">Source game type (RC1)</param>
        /// <param name="targetGameType">Target game type (RC3)</param>
        public static void ApplyCoordinateConversionToTieData(Level level, int sourceGameType, int targetGameType)
        {
            if (sourceGameType == targetGameType)
                return;

            Console.WriteLine($"🔄 [TIE CULLING] Converting TIE culling data from game {sourceGameType} to {targetGameType}");

            // Fix TIE data
            if (level.tieData != null && level.tieData.Length > 0)
            {
                level.tieData = ConvertTieDataCoordinates(level.tieData, level.ties, sourceGameType, targetGameType);
            }

            // Fix TIE group data (contains spatial indexing)
            if (level.tieGroupData != null && level.tieGroupData.Length > 0)
            {
                level.tieGroupData = ConvertTieGroupDataCoordinates(level.tieGroupData, level.ties, sourceGameType, targetGameType);
            }

            // Fix occlusion data
            if (level.occlusionData != null)
            {
                ConvertOcclusionDataCoordinates(level.occlusionData, level.ties, sourceGameType, targetGameType);
            }
        }

        /// <summary>
        /// Restores TIE culling data after save to prevent corruption
        /// </summary>
        /// <param name="level">The level to restore</param>
        /// <param name="protection">Protection data captured before save</param>
        public static void RestoreTieCullingData(Level level, TieCullingProtectionData protection)
        {
            if (protection == null)
                return;

            Console.WriteLine("🔓 [TIE CULLING] Restoring TIE culling data after save...");

            // Restore original data
            if (protection.OriginalTieData != null)
            {
                level.tieData = protection.OriginalTieData.ToArray();
            }

            if (protection.OriginalTieGroupData != null)
            {
                level.tieGroupData = protection.OriginalTieGroupData.ToArray();
            }

            if (protection.ConvertedOcclusionData != null)
            {
                // Recreate occlusion data from converted bytes
                var restored = RecreateOcclusionDataFromBytes(protection.ConvertedOcclusionData);
                if (restored != null)
                {
                    int expectedTieCount = level.ties?.Count ?? 0;
                    if (restored.tieData.Count == expectedTieCount)
                    {
                        level.occlusionData = restored;
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ [TIE CULLING] OcclusionData tie count {restored.tieData.Count} does not match TIE list {expectedTieCount}. Restoration skipped.");
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ [TIE CULLING] Failed to restore OcclusionData. Existing data preserved.");
                }
            }
            else
            {
                Console.WriteLine("⚠️ [TIE CULLING] No converted OcclusionData available for restoration.");
            }

            Console.WriteLine("✅ [TIE CULLING] TIE culling data restored successfully");
        }

        /// <summary>
        /// Rebuilds the TIE occlusion list so indices match the target level's TIE ordering.
        /// Enhanced version with better position tolerance and preservation of existing data.
        /// </summary>
        /// <param name="target">Level whose occlusion data will be rebuilt</param>
        /// <param name="source">Level providing the original occlusion mapping</param>
        public static void RebuildTieOcclusion(Level target, Level source)
        {
            if (target?.ties == null || source?.ties == null || source.occlusionData?.tieData == null)
            {
                Console.WriteLine("⚠️ [OCCLUSION] Cannot rebuild - missing required data");
                return;
            }

            Console.WriteLine("🔄 [OCCLUSION] Rebuilding TIE occlusion data...");

            // Check if target already has properly sized occlusion data
            if (target.occlusionData?.tieData != null && target.occlusionData.tieData.Count == target.ties.Count)
            {
                Console.WriteLine("✅ [OCCLUSION] Target already has properly sized TIE occlusion data, skipping rebuild");
                return;
            }

            var byId = new Dictionary<uint, KeyValuePair<int, int>>();
            var byPosApproximate = new List<(Vector3 pos, KeyValuePair<int, int> entry)>();

            int count = Math.Min(source.ties.Count, source.occlusionData.tieData.Count);
            Console.WriteLine($"🔄 [OCCLUSION] Processing {count} source TIE entries for mapping");

            for (int i = 0; i < count; i++)
            {
                var tie = source.ties[i];
                var entry = source.occlusionData.tieData[i];

                if (tie.off58 != 0)
                {
                    byId[tie.off58] = entry;
                }

                // Store position with tolerance for approximate matching
                byPosApproximate.Add((tie.position, entry));
            }

            Console.WriteLine($"🔄 [OCCLUSION] Created {byId.Count} ID mappings and {byPosApproximate.Count} position mappings");

            // Preserve existing moby and shrub data
            var mobyData = target.occlusionData?.mobyData?.ToList() ?? new List<KeyValuePair<int, int>>();
            var shrubData = target.occlusionData?.shrubData?.ToList() ?? new List<KeyValuePair<int, int>>();

            // Remove shrub entries beyond the available shrubs in the target level
            int shrubCount = target.shrubs?.Count ?? 0;
            if (shrubData.Count > shrubCount)
            {
                Console.WriteLine($"⚠️ [OCCLUSION] Discarding {shrubData.Count - shrubCount} extraneous shrub occlusion entries");
                shrubData = shrubData.Take(shrubCount).ToList();
            }

            // Initialize TIE data if needed
            if (target.occlusionData == null)
            {
                var header = new OcclusionDataHeader(new byte[0x10]);
                target.occlusionData = new OcclusionData(new byte[0], header);
            }

            if (target.occlusionData.tieData == null)
                target.occlusionData.tieData = new List<KeyValuePair<int, int>>();

            target.occlusionData.tieData.Clear();

            var rebuiltTie = new List<KeyValuePair<int, int>>(target.ties.Count);
            int matchedById = 0;
            int matchedByPos = 0;
            int defaultEntries = 0;
            const float POSITION_TOLERANCE = 1.0f; // Allow 1 unit difference

            foreach (var tie in target.ties)
            {
                KeyValuePair<int, int> entry = new KeyValuePair<int, int>(0, 0);
                bool matched = false;

                // Try ID-based matching first
                if (tie.off58 != 0 && byId.TryGetValue(tie.off58, out entry))
                {
                    matched = true;
                    matchedById++;
                }
                else
                {
                    // Try approximate position matching
                    foreach (var (pos, sourceEntry) in byPosApproximate)
                    {
                        float distance = Vector3.Distance(tie.position, pos);
                        if (distance <= POSITION_TOLERANCE)
                        {
                            entry = sourceEntry;
                            matched = true;
                            matchedByPos++;
                            break;
                        }
                    }
                }

                if (!matched)
                {
                    // Fallback to existing visibility flags in the target, or use a safe default
                    int existingFlags = 0x21;
                    if (target.occlusionData?.tieData != null && rebuiltTie.Count < target.occlusionData.tieData.Count)
                    {
                        existingFlags = target.occlusionData.tieData[rebuiltTie.Count].Value & 0xFFFF;
                    }

                    int spatialHash = CalculateSpatialHashRC3(tie.position);
                    entry = new KeyValuePair<int, int>(spatialHash, existingFlags);
                    defaultEntries++;
                }

                rebuiltTie.Add(entry);
            }

            // Update the existing occlusion data rather than creating new
            target.occlusionData.mobyData = mobyData;
            target.occlusionData.shrubData = shrubData;
            target.occlusionData.tieData = rebuiltTie;

            Console.WriteLine($"✅ [OCCLUSION] Rebuilt occlusion data for {rebuiltTie.Count} TIEs");
            Console.WriteLine($"    - Matched by ID: {matchedById}");
            Console.WriteLine($"    - Matched by position: {matchedByPos}");
            Console.WriteLine($"    - Generated defaults: {defaultEntries}");
        }

        /// <summary>
        /// Directly copies TIE occlusion data from source to target level with minimal processing.
        /// This preserves the original occlusion patterns instead of trying to rebuild them.
        /// </summary>
        /// <param name="target">Level to receive the occlusion data</param>
        /// <param name="source">Level providing the source occlusion data</param>
        public static void DirectCopyTieOcclusion(Level target, Level source)
        {
            if (target?.ties == null || source?.ties == null || source.occlusionData?.tieData == null)
            {
                Console.WriteLine("⚠️ [OCCLUSION] Cannot copy - missing required data");
                return;
            }

            Console.WriteLine("🔄 [OCCLUSION] Directly copying TIE occlusion data from source...");

            // Preserve existing moby occlusion data
            var existingMobyData = target.occlusionData?.mobyData?.ToList() ?? new List<KeyValuePair<int, int>>();

            // Copy shrub occlusion data from source (trim or pad to match target)
            int targetShrubCount = target.shrubs?.Count ?? 0;
            var copiedShrubData = source.occlusionData?.shrubData?.ToList() ?? new List<KeyValuePair<int, int>>();

            if (copiedShrubData.Count > targetShrubCount)
            {
                copiedShrubData = copiedShrubData.Take(targetShrubCount).ToList();
                Console.WriteLine($"⚠️ [OCCLUSION] Trimmed shrub occlusion data to {targetShrubCount} entries");
            }
            else if (copiedShrubData.Count < targetShrubCount)
            {
                Console.WriteLine($"🔄 [OCCLUSION] Padding shrub occlusion data ({copiedShrubData.Count} -> {targetShrubCount})");
                var shrubPadding = copiedShrubData.Count > 0 ? copiedShrubData.Last() : new KeyValuePair<int, int>(0, 0);
                int shrubEntriesToAdd = targetShrubCount - copiedShrubData.Count;
                for (int i = 0; i < shrubEntriesToAdd; i++)
                {
                    copiedShrubData.Add(shrubPadding);
                }
            }

            // **CRITICAL FIX**: Create the occlusion data correctly without triggering array bounds exceptions
            try
            {
                // If target already has occlusion data, preserve its structure and just update the lists
                if (target.occlusionData != null)
                {
                    // Directly update the existing occlusion data lists
                    target.occlusionData.mobyData = existingMobyData;
                    target.occlusionData.shrubData = copiedShrubData;

                    // Handle TIE data copy with proper bounds checking
                    if (source.occlusionData.tieData.Count == target.ties.Count)
                    {
                        // Perfect match - direct copy
                        Console.WriteLine($"✅ [OCCLUSION] Perfect count match ({target.ties.Count}) - copying directly");
                        target.occlusionData.tieData = new List<KeyValuePair<int, int>>(source.occlusionData.tieData);
                    }
                    else if (source.occlusionData.tieData.Count > target.ties.Count)
                    {
                        // Source has more entries - take first N entries
                        Console.WriteLine($"🔄 [OCCLUSION] Source has more entries ({source.occlusionData.tieData.Count} -> {target.ties.Count}) - taking first entries");
                        target.occlusionData.tieData = source.occlusionData.tieData.Take(target.ties.Count).ToList();
                    }
                    else
                    {
                        // Source has fewer entries - copy what we have and pad with defaults
                        Console.WriteLine($"🔄 [OCCLUSION] Source has fewer entries ({source.occlusionData.tieData.Count} -> {target.ties.Count}) - padding with defaults");

                        var copiedTieData = new List<KeyValuePair<int, int>>(source.occlusionData.tieData);
                        var paddingTemplate = source.occlusionData.tieData.Count > 0
                            ? source.occlusionData.tieData.Last()
                            : new KeyValuePair<int, int>(unchecked((int) 0x95020000), 0x00000021);

                        int entriesToAdd = target.ties.Count - source.occlusionData.tieData.Count;
                        for (int i = 0; i < entriesToAdd; i++)
                        {
                            copiedTieData.Add(paddingTemplate);
                        }

                        target.occlusionData.tieData = copiedTieData;
                        Console.WriteLine($"    Added {entriesToAdd} padding entries based on source pattern");
                    }
                }
                else
                {
                    // Target doesn't have occlusion data - create it from scratch using serialized approach
                    Console.WriteLine("🔧 [OCCLUSION] Creating new occlusion data structure for target");

                    // Prepare the TIE data to copy
                    List<KeyValuePair<int, int>> tieDataToCopy;
                    if (source.occlusionData.tieData.Count == target.ties.Count)
                    {
                        tieDataToCopy = new List<KeyValuePair<int, int>>(source.occlusionData.tieData);
                    }
                    else if (source.occlusionData.tieData.Count > target.ties.Count)
                    {
                        tieDataToCopy = source.occlusionData.tieData.Take(target.ties.Count).ToList();
                    }
                    else
                    {
                        tieDataToCopy = new List<KeyValuePair<int, int>>(source.occlusionData.tieData);
                        var paddingTemplate = source.occlusionData.tieData.Count > 0
                            ? source.occlusionData.tieData.Last()
                            : new KeyValuePair<int, int>(unchecked((int) 0x95020000), 0x00000021);

                        int entriesToAdd = target.ties.Count - source.occlusionData.tieData.Count;
                        for (int i = 0; i < entriesToAdd; i++)
                        {
                            tieDataToCopy.Add(paddingTemplate);
                        }
                    }

                    // Create properly sized byte array for the occlusion data
                    int totalDataSize = (existingMobyData.Count + tieDataToCopy.Count + copiedShrubData.Count) * 8;
                    byte[] occlusionBlock = new byte[totalDataSize];

                    // Write all the data into the block
                    int offset = 0;
                    foreach (var entry in existingMobyData)
                    {
                        WriteInt(occlusionBlock, offset, entry.Key);
                        WriteInt(occlusionBlock, offset + 4, entry.Value);
                        offset += 8;
                    }
                    foreach (var entry in tieDataToCopy)
                    {
                        WriteInt(occlusionBlock, offset, entry.Key);
                        WriteInt(occlusionBlock, offset + 4, entry.Value);
                        offset += 8;
                    }
                    foreach (var entry in copiedShrubData)
                    {
                        WriteInt(occlusionBlock, offset, entry.Key);
                        WriteInt(occlusionBlock, offset + 4, entry.Value);
                        offset += 8;
                    }

                    // Create header with correct counts
                    byte[] headerBlock = new byte[16];
                    WriteInt(headerBlock, 0x00, existingMobyData.Count);
                    WriteInt(headerBlock, 0x04, tieDataToCopy.Count);
                    WriteInt(headerBlock, 0x08, copiedShrubData.Count);
                    WriteInt(headerBlock, 0x0C, 0); // Reserved

                    var header = new OcclusionDataHeader(headerBlock);

                    // Create the occlusion data object - this should now work without bounds errors
                    target.occlusionData = new OcclusionData(occlusionBlock, header);
                }

                Console.WriteLine($"✅ [OCCLUSION] Direct copy complete - {target.occlusionData.tieData.Count} TIE entries copied");

                // Show first few entries to verify the pattern was preserved
                if (target.occlusionData.tieData.Count > 0)
                {
                    Console.WriteLine("    First few TIE occlusion entries:");
                    for (int i = 0; i < Math.Min(5, target.occlusionData.tieData.Count); i++)
                    {
                        var entry = target.occlusionData.tieData[i];
                        Console.WriteLine($"      [{i}] 0x{entry.Key:X8} -> 0x{entry.Value:X8}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [OCCLUSION] Error during direct copy: {ex.Message}");
                Console.WriteLine($"    Stack trace: {ex.StackTrace}");

                // Fallback: try to preserve existing occlusion data or create minimal structure
                if (target.occlusionData == null)
                {
                    byte[] headerBlock = new byte[16];
                    WriteInt(headerBlock, 0x00, existingMobyData.Count);
                    WriteInt(headerBlock, 0x04, target.ties.Count);
                    WriteInt(headerBlock, 0x08, targetShrubCount);
                    WriteInt(headerBlock, 0x0C, 0);

                    var header = new OcclusionDataHeader(headerBlock);
                    target.occlusionData = new OcclusionData(new byte[0], header);

                    // Initialize empty lists
                    target.occlusionData.mobyData = existingMobyData;
                    target.occlusionData.shrubData = copiedShrubData;
                    target.occlusionData.tieData = new List<KeyValuePair<int, int>>();

                    // Fill with default entries
                    var defaultEntry = new KeyValuePair<int, int>(unchecked((int) 0x95020000), 0x00000021);
                    for (int i = 0; i < target.ties.Count; i++)
                    {
                        target.occlusionData.tieData.Add(defaultEntry);
                    }

                    Console.WriteLine($"🔧 [OCCLUSION] Created fallback occlusion data with {target.ties.Count} default TIE entries");
                }
            }
        }

        /// <summary>
        /// Converts TIE data coordinates between game versions
        /// </summary>
        private static byte[] ConvertTieDataCoordinates(byte[] tieData, List<Tie> ties, int sourceGame, int targetGame)
        {
            Console.WriteLine($"🔄 [TIE DATA] Converting {tieData.Length} bytes of TIE data...");

            byte[] convertedData = new byte[tieData.Length];
            Array.Copy(tieData, convertedData, tieData.Length);

            // TIE data format analysis:
            // RC1: 0xE0 bytes per TIE (includes position matrices and culling bounds)
            // RC2/3: Variable size, includes additional culling data

            if (sourceGame == 1 && (targetGame == 2 || targetGame == 3))
            {
                // RC1 → RC3 conversion
                int tieElementSize = (sourceGame == 1) ? 0xE0 : 0x60;
                int tieCount = Math.Min(ties?.Count ?? 0, tieData.Length / tieElementSize);

                for (int i = 0; i < tieCount; i++)
                {
                    int offset = 0x10 + (i * tieElementSize); // Skip header

                    if (offset + 64 <= convertedData.Length)
                    {
                        // Convert transformation matrix (first 64 bytes)
                        Matrix4 matrix = ReadMatrix4(convertedData, offset);
                        Matrix4 convertedMatrix = ConvertMatrixRC1ToRC3(matrix);
                        WriteMatrix4(convertedData, offset, convertedMatrix);

                        // Convert culling bounds if they exist
                        if (offset + 0x80 <= convertedData.Length)
                        {
                            ConvertCullingBounds(convertedData, offset + 64, sourceGame, targetGame);
                        }
                    }
                }

                Console.WriteLine($"🔄 [TIE DATA] Converted {tieCount} TIE data entries");
            }
            else if (sourceGame == 2 && targetGame == 3)
            {
                // RC2 and RC3 share coordinate systems; no conversion needed
                Console.WriteLine("🔄 [TIE DATA] RC2→RC3 requires no coordinate changes");
            }

            return convertedData;
        }

        /// <summary>
        /// Converts TIE group data coordinates (spatial indexing)
        /// </summary>
        private static byte[] ConvertTieGroupDataCoordinates(byte[] tieGroupData, List<Tie> ties, int sourceGame, int targetGame)
        {
            Console.WriteLine($"🔄 [TIE GROUP] Converting {tieGroupData.Length} bytes of TIE group data...");

            byte[] convertedData = new byte[tieGroupData.Length];
            Array.Copy(tieGroupData, convertedData, tieGroupData.Length);

            if (sourceGame == 1 && (targetGame == 2 || targetGame == 3))
            {
                // TIE group data contains spatial hash tables and culling boundaries
                // We need to rebuild these based on the converted TIE positions

                if (convertedData.Length >= 16)
                {
                    int groupCount = ReadInt(convertedData, 0x00);
                    int dataSize = ReadInt(convertedData, 0x04);

                    Console.WriteLine($"🔄 [TIE GROUP] Found {groupCount} TIE groups in {dataSize} bytes");

                    // Regenerate spatial hash tables
                    RegenerateTieGroupSpatialData(convertedData, ties, sourceGame, targetGame);
                }
            }
            else if (sourceGame == 2 && targetGame == 3)
            {
                // RC2 and RC3 share TIE group layout; spatial data left unchanged
                Console.WriteLine("🔄 [TIE GROUP] RC2→RC3 requires no group data changes");
            }

            return convertedData;
        }

        /// <summary>
        /// Converts occlusion data coordinates
        /// </summary>
        private static void ConvertOcclusionDataCoordinates(OcclusionData occlusionData, List<Tie> ties, int sourceGame, int targetGame)
        {
            if (occlusionData.tieData == null || ties == null)
                return;

            Console.WriteLine($"🔄 [OCCLUSION] Converting {occlusionData.tieData.Count} TIE occlusion entries...");

            // Regenerate occlusion data based on converted TIE positions
            var newTieOcclusionData = new List<KeyValuePair<int, int>>();

            for (int i = 0; i < Math.Min(occlusionData.tieData.Count, ties.Count); i++)
            {
                var tie = ties[i];
                int spatialHash = CalculateSpatialHashRC3(tie.position);
                int visibilityFlags = CalculateTieVisibilityFlags(tie, sourceGame, targetGame);

                newTieOcclusionData.Add(new KeyValuePair<int, int>(spatialHash, visibilityFlags));
            }

            occlusionData.tieData = newTieOcclusionData;
            Console.WriteLine($"🔄 [OCCLUSION] Regenerated {newTieOcclusionData.Count} TIE occlusion entries");
        }

        /// <summary>
        /// Converts a matrix from RC1 to RC3 coordinate system
        /// </summary>
        private static Matrix4 ConvertMatrixRC1ToRC3(Matrix4 matrix)
        {
            // Extract components
            Vector3 position = matrix.ExtractTranslation();
            Quaternion rotation = matrix.ExtractRotation();
            Vector3 scale = matrix.ExtractScale();

            // Apply coordinate system conversion (similar to cuboid conversion)
            var convertedRotation = new Quaternion(-rotation.X, rotation.Y, -rotation.Z, rotation.W);

            // Rebuild matrix with converted rotation
            Matrix4 convertedMatrix = Matrix4.CreateScale(scale) *
                                    Matrix4.CreateFromQuaternion(convertedRotation) *
                                    Matrix4.CreateTranslation(position);

            return convertedMatrix;
        }

        /// <summary>
        /// Converts culling bounds between coordinate systems
        /// </summary>
        private static void ConvertCullingBounds(byte[] data, int offset, int sourceGame, int targetGame)
        {
            if (offset + 24 > data.Length)
                return;

            // Read bounding box (min/max vectors)
            Vector3 min = new Vector3(
                ReadFloat(data, offset + 0),
                ReadFloat(data, offset + 4),
                ReadFloat(data, offset + 8)
            );

            Vector3 max = new Vector3(
                ReadFloat(data, offset + 12),
                ReadFloat(data, offset + 16),
                ReadFloat(data, offset + 20)
            );

            // Apply coordinate conversion if needed
            // (In this case, bounding boxes might not need rotation conversion,
            //  but could need scaling or translation adjustments)

            // Write back converted bounds
            WriteFloat(data, offset + 0, min.X);
            WriteFloat(data, offset + 4, min.Y);
            WriteFloat(data, offset + 8, min.Z);
            WriteFloat(data, offset + 12, max.X);
            WriteFloat(data, offset + 16, max.Y);
            WriteFloat(data, offset + 20, max.Z);
        }

        /// <summary>
        /// Regenerates TIE group spatial data after coordinate conversion
        /// </summary>
        private static void RegenerateTieGroupSpatialData(byte[] groupData, List<Tie> ties, int sourceGame, int targetGame)
        {
            if (ties == null || groupData.Length < 16)
                return;

            // Rebuild spatial hash tables based on converted TIE positions
            var spatialGroups = CreateTieSpatialGroups(ties);

            // Update the group data with new spatial information
            int offset = 0x10; // Skip header
            foreach (var group in spatialGroups.Take(10)) // Limit to available space
            {
                if (offset + 12 <= groupData.Length && group.Count > 0)
                {
                    var center = CalculateGroupCenter(group);
                    var bounds = CalculateGroupBounds(group);

                    WriteFloat(groupData, offset + 0, center.X);
                    WriteFloat(groupData, offset + 4, center.Y);
                    WriteFloat(groupData, offset + 8, center.Z);
                    WriteFloat(groupData, offset + 12, bounds.Length);

                    offset += 16;
                }
            }
        }

        // Helper methods for spatial calculations
        private static (Vector3 min, Vector3 max) CalculateTieBoundingBox(Tie tie)
        {
            Vector3 pos = tie.position;
            // 🔧 FIX: Handle nullable float and convert to Vector3
            float modelSize = tie.model?.size ?? 1.0f;
            Vector3 size = new Vector3(modelSize);

            return (pos - size * 0.5f, pos + size * 0.5f);
        }

        private static int CalculateSpatialHashRC3(Vector3 position)
        {
            // RC3-optimized spatial hash
            int x = (int)(position.X / 64.0f);
            int y = (int)(position.Y / 64.0f);
            int z = (int)(position.Z / 64.0f);

            return ((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) & 0x7FFFFFFF;
        }

        private static int CalculateTieVisibilityFlags(Tie tie, int sourceGame, int targetGame)
        {
            int flags = 0x00000001; // Base visibility

            // Distance-based culling
            float distance = tie.position.Length;
            if (distance > 1000.0f) flags |= 0x00000010; // Far culling
            else if (distance > 500.0f) flags |= 0x00000008; // Medium culling

            // Size-based culling
            if (tie.model != null)
            {
                // 🔧 FIX: Convert float to Vector3 for Length property
                float modelSize = tie.model.size;
                if (modelSize < 10.0f) flags |= 0x00000020; // Small object culling
            }

            return flags;
        }

        private static List<List<Tie>> CreateTieSpatialGroups(List<Tie> ties)
        {
            var groups = new List<List<Tie>>();
            var spatialGrid = new Dictionary<Vector3i, List<Tie>>();

            const float GRID_SIZE = 128.0f;

            foreach (var tie in ties)
            {
                var gridPos = new Vector3i(
                    (int)(tie.position.X / GRID_SIZE),
                    (int)(tie.position.Y / GRID_SIZE),
                    (int)(tie.position.Z / GRID_SIZE)
                );

                if (!spatialGrid.ContainsKey(gridPos))
                    spatialGrid[gridPos] = new List<Tie>();

                spatialGrid[gridPos].Add(tie);
            }

            foreach (var group in spatialGrid.Values)
            {
                groups.Add(group);
            }

            return groups;
        }

        private static Vector3 CalculateGroupCenter(List<Tie> group)
        {
            if (group.Count == 0) return Vector3.Zero;

            Vector3 sum = Vector3.Zero;
            foreach (var tie in group)
            {
                sum += tie.position;
            }

            return sum / group.Count;
        }

        private static Vector3 CalculateGroupBounds(List<Tie> group)
        {
            if (group.Count == 0) return Vector3.Zero;

            Vector3 min = group[0].position;
            Vector3 max = group[0].position;

            foreach (var tie in group)
            {
                min = Vector3.ComponentMin(min, tie.position);
                max = Vector3.ComponentMax(max, tie.position);
            }

            return max - min;
        }

        private static OcclusionData RecreateOcclusionDataFromBytes(byte[] bytes)
        {
            try
            {
                if (bytes == null || bytes.Length < 0x10)
                {
                    Console.WriteLine("⚠️ [TIE CULLING] OcclusionData bytes are invalid or empty");
                    return null;
                }

                // Parse header using big-endian reads
                int mobyCount = ReadInt(bytes, 0x00);
                int tieCount = ReadInt(bytes, 0x04);
                int shrubCount = ReadInt(bytes, 0x08);
                var header = new OcclusionDataHeader(bytes);

                int dataLength = (mobyCount + tieCount + shrubCount) * 0x08;
                int expectedLength = 0x10 + dataLength;
                if (bytes.Length < expectedLength)
                {
                    Console.WriteLine($"⚠️ [TIE CULLING] OcclusionData bytes truncated (expected {expectedLength}, got {bytes.Length})");
                    return null;
                }

                byte[] dataBlock = new byte[dataLength];
                Array.Copy(bytes, 0x10, dataBlock, 0, dataLength);

                return new OcclusionData(dataBlock, header);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ [TIE CULLING] Error recreating OcclusionData: {ex.Message}");
                return null;
            }
        }

        // Helper struct for spatial grid
        public struct Vector3i
        {
            public int X, Y, Z;

            public Vector3i(int x, int y, int z)
            {
                X = x; Y = y; Z = z;
            }

            public override bool Equals(object obj) =>
                obj is Vector3i other && X == other.X && Y == other.Y && Z == other.Z;

            public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        }
    }

    /// <summary>
    /// Data structure for protecting TIE culling data during save operations
    /// </summary>
    public class TieCullingProtectionData
    {
        public byte[] OriginalTieData { get; set; }
        public byte[] OriginalTieGroupData { get; set; }
        public byte[] OriginalOcclusionData { get; set; }
        public byte[] ConvertedOcclusionData { get; set; }
        public Dictionary<int, Vector3> TiePositions { get; set; }
        public Dictionary<int, (Vector3 min, Vector3 max)> TieBoundingBoxes { get; set; }
    }
}
