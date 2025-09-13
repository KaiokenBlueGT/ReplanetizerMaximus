using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;

namespace GeometrySwapper
{
    /// <summary>
    /// Diagnostic tool for analyzing TIE objects and identifying potential issues
    /// </summary>
    public static class TieDiagnostics
    {
        /// <summary>
        /// Analyzes TIE data in a level and exports diagnostic information, including a crash risk assessment.
        /// </summary>
        /// <param name="level">The level to analyze</param>
        /// <param name="outputPath">Path where to save the diagnostic file</param>
        /// <param name="label">Label to identify this level in the report</param>
        /// <param name="includeVertexData">"Whether to include detailed vertex data in the report</param>
        public static void AnalyzeTies(Level level, string outputPath, string label, bool includeVertexData = false)
        {
            try
            {
                Console.WriteLine($"⚙️ Running TIE diagnostics for {label}...");

                // Ensure the output directory exists
                Directory.CreateDirectory(outputPath);

                // Run all diagnostics
                CheckTieVertexBufferStrides(level, outputPath);
                CheckTieTextureReferences(level, outputPath);
                CheckDuplicateTieIds(level, outputPath);
                DumpTieModelInfo(level, outputPath, includeVertexData);
                ValidateTieModelReferences(level, outputPath);
                CheckTieColorBytesAlignment(level, outputPath);

                // Now create the summary report
                string reportFile = Path.Combine(outputPath, $"{label}_tie_analysis.txt");

                if (level == null)
                {
                    Console.WriteLine("❌ Cannot analyze TIEs: Level is null");
                    return;
                }

                Console.WriteLine($"Analyzing TIEs in level {label}...");

                int expectedOff54 = level.game.num == 1 ? 720 : 4000;

                using (var writer = new StreamWriter(reportFile))
                {
                    writer.WriteLine($"======== TIE DIAGNOSTIC REPORT - {label} ========");
                    writer.WriteLine($"Generated on: {DateTime.Now}");
                    writer.WriteLine("===============================================");

                    // Basic counts
                    writer.WriteLine("\n1. BASIC STATISTICS:");
                    writer.WriteLine($"TIE Instances: {level.ties?.Count ?? 0}");
                    writer.WriteLine($"TIE Models: {level.tieModels?.Count ?? 0}");
                    writer.WriteLine($"Texture Count: {level.textures?.Count ?? 0}");
                    writer.WriteLine($"TieIds in header: {level.tieIds?.Count ?? 0}");
                    writer.WriteLine($"TieData size: {level.tieData?.Length ?? 0} bytes");
                    writer.WriteLine($"TieGroupData size: {level.tieGroupData?.Length ?? 0} bytes");

                    // CRITICAL CHECK: Validate tieIds matches models, not instances
                    if (level.tieIds != null && level.tieModels != null)
                    {
                        bool correctTieIdsCount = level.tieIds.Count == level.tieModels.Count;
                        writer.WriteLine($"\nTieIds count matches model count: {correctTieIdsCount}");

                        if (!correctTieIdsCount)
                        {
                            if (level.tieIds.Count == level.ties?.Count)
                            {
                                writer.WriteLine("❌ CRITICAL ERROR: tieIds matches instance count, should match model count!");
                                writer.WriteLine("This will cause memory access violations and crashes");
                            }
                            else
                            {
                                writer.WriteLine($"⚠️ Unexpected tieIds count: {level.tieIds.Count} (models: {level.tieModels.Count}, instances: {level.ties?.Count ?? 0})");
                            }
                        }
                    }

                    // CRITICAL CHECK: TieGroupData alignment to 0x80 boundaries 
                    if (level.tieGroupData != null)
                    {
                        bool properAlignment = level.tieGroupData.Length % 0x80 == 0;
                        writer.WriteLine($"\nTieGroupData properly aligned to 0x80 boundaries: {properAlignment}");

                        if (!properAlignment)
                        {
                            writer.WriteLine($"  ❌ TieGroupData size is {level.tieGroupData.Length} bytes");
                            writer.WriteLine($"  Should be padded to {((level.tieGroupData.Length + 0x7F) / 0x80) * 0x80} bytes");
                            writer.WriteLine("  This will cause memory alignment issues and crashes");
                        }

                        // Size sanity check
                        if (level.tieGroupData.Length > 10_000_000) // Over 10MB is definitely wrong
                        {
                            writer.WriteLine($"  ❌ TieGroupData size is suspiciously large: {level.tieGroupData.Length} bytes");
                            writer.WriteLine("  This is almost certainly incorrect and will cause crashes");
                            writer.WriteLine("  Expected size should be much smaller, typically under 1MB");
                        }
                    }

                    // Check for off54 values (should be 720 for RC1, 4000 for RC2/3)
                    if (level.ties != null)
                    {
                        var badOff54 = level.ties.Where(t => t.off54 != expectedOff54).ToList();
                        writer.WriteLine($"\nTIEs with non-standard off54 value (should be {expectedOff54}): {badOff54.Count}");
                        foreach (var tie in badOff54.Take(10))
                        {
                            writer.WriteLine($"  - TIE modelID {tie.modelID} has off54 = {tie.off54}");
                        }
                        if (badOff54.Count > 10)
                            writer.WriteLine($"  - ... and {badOff54.Count - 10} more");
                    }

                    // Add full crash risk assessment
                    writer.WriteLine("\n============= CRASH RISK ASSESSMENT =============");
                    int riskScore = 0;

                    // Add points for each critical issue
                    if (level.tieIds != null && level.tieModels != null && level.tieIds.Count != level.tieModels.Count)
                        riskScore += 40;

                    if (level.tieGroupData != null && level.tieGroupData.Length % 0x80 != 0)
                        riskScore += 30;

                    if (level.tieGroupData != null && level.tieGroupData.Length > 10_000_000)
                        riskScore += 50;

                    // Invalid model references
                    if (level.ties != null && level.tieModels != null)
                    {
                        var modelIds = new HashSet<int>(level.tieModels.Select(m => (int) m.id));
                        int invalidRefs = level.ties.Count(t => !modelIds.Contains(t.modelID));
                        if (invalidRefs > 0)
                            riskScore += Math.Min(invalidRefs, 50);
                    }

                    string riskLevel;
                    if (riskScore >= 70)
                        riskLevel = "CRITICAL - Will absolutely crash";
                    else if (riskScore >= 40)
                        riskLevel = "HIGH - Very likely to crash";
                    else if (riskScore >= 20)
                        riskLevel = "MODERATE - May crash under certain conditions";
                    else if (riskScore > 0)
                        riskLevel = "LOW - Minor issues detected";
                    else
                        riskLevel = "MINIMAL - No obvious issues detected";

                    writer.WriteLine($"Risk Level: {riskLevel} ({riskScore}/100)");

                    if (riskScore > 0)
                    {
                        writer.WriteLine("\nRECOMMENDED ACTIONS:");

                        if (level.tieIds != null && level.tieModels != null && level.tieIds.Count != level.tieModels.Count)
                        {
                            writer.WriteLine("1. Fix tieIds list to match model count, not instance count:");
                            writer.WriteLine("   level.tieIds = level.tieModels.Select(m => (int)m.id).ToList();");
                        }

                        if (level.tieGroupData != null && (level.tieGroupData.Length % 0x80 != 0 || level.tieGroupData.Length > 10_000_000))
                        {
                            writer.WriteLine("2. Regenerate TIE serialization data with proper alignment:");
                            writer.WriteLine("   TieSwapper.CreateTieSerializationData(level);");
                        }
                    }
                }

                Console.WriteLine($"TIE diagnostics for {label} written to {reportFile}");
                Console.WriteLine("✅ TIE diagnostics completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ERROR: Failed to run TIE diagnostics: {ex.Message}");
            }
        }

        public static void CompareTies(Level sourceLevel, Level targetLevel, string outputPath,
                                      string sourceLabel, string targetLabel)
        {
            if (sourceLevel == null || targetLevel == null)
            {
                Console.WriteLine("❌ Cannot compare TIEs: One or both levels are null");
                return;
            }

            string? directoryPath = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            else
            {
                Console.WriteLine($"❌ ERROR: Unable to determine directory path for output: {outputPath}");
            }

            Console.WriteLine($"Comparing TIEs between {sourceLabel} and {targetLabel}...");

            using (var writer = new StreamWriter(outputPath))
            {
                writer.WriteLine($"======== TIE COMPARISON REPORT ========");
                writer.WriteLine($"{sourceLabel} vs. {targetLabel}");
                writer.WriteLine($"Generated on: {DateTime.Now}");
                writer.WriteLine("======================================");

                // Basic statistics comparison
                writer.WriteLine("\n1. BASIC STATISTICS COMPARISON:");
                CompareAndWrite(writer, "TIE Instances", sourceLevel.ties?.Count ?? 0, targetLevel.ties?.Count ?? 0);
                CompareAndWrite(writer, "TIE Models", sourceLevel.tieModels?.Count ?? 0, targetLevel.tieModels?.Count ?? 0);
                CompareAndWrite(writer, "TieIds in header", sourceLevel.tieIds?.Count ?? 0, targetLevel.tieIds?.Count ?? 0);
                CompareAndWrite(writer, "Textures", sourceLevel.textures?.Count ?? 0, targetLevel.textures?.Count ?? 0);

                // Model ID analysis
                writer.WriteLine("\n2. MODEL ID ANALYSIS:");

                // Get sets of model IDs from both levels
                var sourceModelIds = sourceLevel.tieModels?.Select(m => (int)m.id).ToHashSet() ?? new HashSet<int>();
                var targetModelIds = targetLevel.tieModels?.Select(m => (int)m.id).ToHashSet() ?? new HashSet<int>();

                // Find IDs that exist in source but not in target, and vice versa
                var missingInTarget = sourceModelIds.Where(id => !targetModelIds.Contains(id)).ToList();
                var newInTarget = targetModelIds.Where(id => !sourceModelIds.Contains(id)).ToList();

                writer.WriteLine($"\nSource model IDs missing in target: {missingInTarget.Count}");
                if (missingInTarget.Count > 0)
                {
                    writer.WriteLine($"  Sample missing IDs: {string.Join(", ", missingInTarget.Take(10))}");
                    if (missingInTarget.Count > 10)
                        writer.WriteLine($"  ... and {missingInTarget.Count - 10} more");
                }

                writer.WriteLine($"\nNew model IDs in target: {newInTarget.Count}");
                if (newInTarget.Count > 0)
                {
                    writer.WriteLine($"  Sample new IDs: {string.Join(", ", newInTarget.Take(10))}");
                    if (newInTarget.Count > 10)
                        writer.WriteLine($"  ... and {newInTarget.Count - 10} more");
                }

                // Texture reference check
                writer.WriteLine("\n3. TEXTURE REFERENCE ANALYSIS:");
                var sourceTexMaxId = (sourceLevel.textures?.Count ?? 0) - 1;
                var targetTexMaxId = (targetLevel.textures?.Count ?? 0) - 1;
                writer.WriteLine($"Source texture ID range: 0-{sourceTexMaxId}");
                writer.WriteLine($"Target texture ID range: 0-{targetTexMaxId}");

                // Check if target models reference textures outside of range
                var problematicModels = new List<TieModel>();
                if (targetLevel.tieModels != null)
                {
                    foreach (var model in targetLevel.tieModels.OfType<TieModel>())
                    {
                        if (model.textureConfig != null)
                        {
                            foreach (var texConfig in model.textureConfig)
                            {
                                if (texConfig.id < 0 || (targetLevel.textures != null && texConfig.id >= targetLevel.textures.Count))
                                {
                                    problematicModels.Add(model);
                                    break;
                                }
                            }
                        }
                    }
                }

                writer.WriteLine($"\nTarget models with out-of-bounds texture references: {problematicModels.Count}");
                foreach (var model in problematicModels.Take(10))
                {
                    writer.WriteLine($"  Model ID {model.id} references textures: {string.Join(", ", model.textureConfig?.Select(tc => tc.id) ?? new int[0])}");
                }
                if (problematicModels.Count > 10)
                    writer.WriteLine($"  ... and {problematicModels.Count - 10} more");

                // Analyze TIE instance model references
                writer.WriteLine("\n4. TIE INSTANCE MODEL REFERENCES:");
                var targetInvalidRefs = 0;

                if (targetLevel.ties != null && targetLevel.tieModels != null)
                {
                    var validModelIds = new HashSet<int>(targetLevel.tieModels.Select(m => (int)m.id));
                    targetInvalidRefs = targetLevel.ties.Count(t => !validModelIds.Contains(t.modelID));

                    writer.WriteLine($"Target TIEs with invalid model references: {targetInvalidRefs}");
                    if (targetInvalidRefs > 0)
                    {
                        writer.WriteLine("\nThis is a CRITICAL issue that will crash the game!");
                        writer.WriteLine("Each TIE must reference a valid model ID that exists in the level.");
                    }
                }

                // ColorBytes analysis
                writer.WriteLine("\n5. COLORBYTES ALIGNMENT ANALYSIS:");
                int targetMisalignedColorBytes = 0;

                if (targetLevel.ties != null)
                {
                    foreach (var tie in targetLevel.ties)
                    {
                        if (tie.model?.vertexBuffer != null && tie.model.vertexBuffer.Length > 0)
                        {
                            int expectedBytes = tie.model.vertexBuffer.Length / 8 * 4;
                            if (tie.colorBytes == null || tie.colorBytes.Length != expectedBytes)
                            {
                                targetMisalignedColorBytes++;
                            }
                        }
                    }
                }

                writer.WriteLine($"Target TIEs with misaligned colorBytes: {targetMisalignedColorBytes}");
                if (targetMisalignedColorBytes > 0)
                {
                    writer.WriteLine("\nThis is an issue that could cause visual glitches or crashes.");
                    writer.WriteLine("Each TIE's colorBytes must match the vertex count of its model (4 bytes per vertex).");
                }

                // Generate crash risk assessment
                writer.WriteLine("\n6. CRASH RISK ASSESSMENT:");
                int riskScore = 0;

                // Add to risk score based on various factors
                if (targetInvalidRefs > 0) riskScore += 100; // Critical issue
                if (targetMisalignedColorBytes > 0) riskScore += 50;
                if (problematicModels.Count > 0) riskScore += 70;
                if ((targetLevel.tieGroupData?.Length ?? 0) % 0x80 != 0) riskScore += 40;
                if ((targetLevel.tieIds?.Count ?? 0) != (targetLevel.tieModels?.Count ?? 0)) riskScore += 30;

                string riskLevel;
                if (riskScore >= 100)
                    riskLevel = "HIGH - Game will likely crash";
                else if (riskScore >= 50)
                    riskLevel = "MEDIUM - Game may crash or have visual issues";
                else if (riskScore > 0)
                    riskLevel = "LOW - Minor issues detected, game might run with visual glitches";
                else
                    riskLevel = "MINIMAL - No major issues detected";

                writer.WriteLine($"Crash risk assessment: {riskLevel}");
                writer.WriteLine($"Risk score: {riskScore}/100");

                // Recommendations
                writer.WriteLine("\n7. RECOMMENDATIONS:");

                if (targetInvalidRefs > 0)
                {
                    writer.WriteLine("• CRITICAL: Fix invalid model references in TIE instances");
                    writer.WriteLine("  - Each TIE must reference a valid model ID that exists in the level");
                    writer.WriteLine("  - Run TieSwapper.ValidateTieModelReferences() to identify specific issues");
                }

                if (problematicModels.Count > 0)
                {
                    writer.WriteLine("• IMPORTANT: Fix out-of-bounds texture references in TIE models");
                    writer.WriteLine("  - Use TieSwapper.MapTieTextures() to properly map textures from source to target level");
                }

                if (targetMisalignedColorBytes > 0)
                {
                    writer.WriteLine("• Fix colorBytes alignment issues in TIE instances");
                    writer.WriteLine("  - Each TIE's colorBytes array should be 4 bytes per vertex in its model");
                }

                if ((targetLevel.tieGroupData?.Length ?? 0) % 0x80 != 0)
                {
                    writer.WriteLine("• Fix tieGroupData alignment");
                    writer.WriteLine("  - tieGroupData size should be a multiple of 0x80 bytes");
                    writer.WriteLine("  - Run TieSwapper.CreateTieSerializationData() to regenerate properly aligned data");
                }

                if ((targetLevel.tieIds?.Count ?? 0) != (targetLevel.tieModels?.Count ?? 0))
                {
                    writer.WriteLine("• Ensure tieIds list matches all model IDs in the level");
                    writer.WriteLine("  - Update targetLevel.tieIds = targetLevel.tieModels.Select(m => (int)m.id).ToList();");
                }
            }

            Console.WriteLine($"TIE comparison written to {outputPath}");
        }

        /// <summary>
        /// Performs deep memory analysis of TIE data for crash investigation
        /// </summary>
        /// <param name="level">The level to analyze for memory issues</param>
        /// <param name="outputPath">Path where to save the diagnostic report</param>
        /// <param name="label">Label to identify this level in the report</param>
        public static void AnalyzeTieMemoryStructures(Level level, string outputPath, string label)
        {
            if (level == null)
            {
                Console.WriteLine("❌ Cannot analyze TIE memory: Level is null");
                return;
            }

            string? directoryPath = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            else
            {
                Console.WriteLine($"❌ ERROR: Unable to determine directory path for output: {outputPath}");
            }
            string reportFile = outputPath;

            Console.WriteLine($"🔍 Performing deep memory analysis of TIEs in level {label}...");

            using (var writer = new StreamWriter(reportFile))
            {
                writer.WriteLine($"======== TIE MEMORY STRUCTURE DIAGNOSTIC REPORT - {label} ========");
                writer.WriteLine($"Generated on: {DateTime.Now}");
                writer.WriteLine("===============================================================");

                // Check if texture indices are properly aligned
                writer.WriteLine("\n1. TEXTURE INDEX VALIDATION:");
                int invalidTextureIndices = 0;

                if (level.tieModels != null)
                {
                    foreach (var model in level.tieModels.OfType<TieModel>())
                    {
                        if (model.textureConfig == null) continue;

                        foreach (var texConfig in model.textureConfig)
                        {
                            if (texConfig.id < 0 || (level.textures != null && texConfig.id >= level.textures.Count))
                            {
                                invalidTextureIndices++;
                                writer.WriteLine($"  ❌ Model ID {model.id} references invalid texture index {texConfig.id}");
                                writer.WriteLine($"     - Valid range is 0 to {(level.textures?.Count - 1 ?? -1)}");
                                writer.WriteLine($"     - This will likely cause ACCESS VIOLATION when the game tries to read texture data");
                            }
                        }
                    }
                }

                writer.WriteLine($"\nFound {invalidTextureIndices} invalid texture references that may cause crashes");

                // Check for memory alignment issues in TIE structures
                writer.WriteLine("\n2. MEMORY ALIGNMENT ANALYSIS:");

                // Check TIE data format
                if (level.tieData != null)
                {
                    writer.WriteLine($"TIE data size: {level.tieData.Length} bytes");
                    if (level.tieData.Length % 0x70 != 0)
                    {
                        writer.WriteLine($"  ❌ WARNING: TIE data size is not a multiple of 0x70 bytes (expected for proper tie structure)");
                        writer.WriteLine($"     This may cause memory misalignment and crashes");
                    }
                    else
                    {
                        writer.WriteLine($"  ✅ TIE data has proper structure alignment (multiple of 0x70 bytes)");
                    }

                    writer.WriteLine($"\nAnalyzing TIE data memory patterns...");
                    AnalyzeMemoryPatterns(writer, level.tieData, "TIE data block");
                }

                // Check TIE group data
                if (level.tieGroupData != null)
                {
                    writer.WriteLine($"\nTIE group data size: {level.tieGroupData.Length} bytes");
                    if (level.tieGroupData.Length % 0x80 != 0)
                    {
                        writer.WriteLine($"  ❌ WARNING: TIE group data is not aligned to 0x80-byte boundaries");
                        writer.WriteLine($"     Expected size: {((level.tieGroupData.Length + 0x7F) / 0x80) * 0x80} bytes");
                        writer.WriteLine($"     This may cause memory access issues during streaming");
                    }
                    else
                    {
                        writer.WriteLine($"  ✅ TIE group data has proper 0x80-byte alignment");
                    }

                    writer.WriteLine($"\nAnalyzing TIE group data memory patterns...");
                    AnalyzeMemoryPatterns(writer, level.tieGroupData, "TIE group data block");
                }

                // Check for inconsistent vertex buffer strides
                writer.WriteLine("\n3. VERTEX BUFFER CONSISTENCY CHECK:");
                if (level.tieModels != null)
                {
                    var nonStandard = level.tieModels.Where(m => m.vertexBuffer != null && m.vertexBuffer.Length % 8 != 0).ToList();
                    if (nonStandard.Count > 0)
                    {
                        writer.WriteLine($"  ❌ WARNING: Found {nonStandard.Count} TIE models with vertex buffers not aligned to 8-float stride");
                        foreach (var model in nonStandard.Take(5))
                        {
                            writer.WriteLine($"     - Model ID {model.id}: Buffer length {model.vertexBuffer.Length} floats (not divisible by 8)");
                            writer.WriteLine($"       This will cause memory access violations when rendering vertex attributes");
                        }
                        if (nonStandard.Count > 5)
                            writer.WriteLine($"       ... and {nonStandard.Count - 5} more");
                    }
                    else
                    {
                        writer.WriteLine($"  ✅ All TIE models have proper 8-float vertex stride alignment");
                    }

                    // Check for potential buffer overflows in colorBytes
                    writer.WriteLine("\n4. COLOR BUFFER OVERFLOW ANALYSIS:");
                    int colorBufferRisks = 0;

                    if (level.ties != null)
                    {
                        foreach (var tie in level.ties)
                        {
                            if (tie.model?.vertexBuffer == null) continue;

                            int expectedColorBytes = tie.model.vertexBuffer.Length / 8 * 4; // 4 bytes per vertex

                            if (tie.colorBytes == null)
                            {
                                colorBufferRisks++;
                                writer.WriteLine($"  ❌ TIE with model ID {tie.modelID} has null colorBytes but expected {expectedColorBytes} bytes");
                            }
                            else if (tie.colorBytes.Length != expectedColorBytes)
                            {
                                colorBufferRisks++;
                                writer.WriteLine($"  ❌ TIE with model ID {tie.modelID} has mismatched colorBytes: has {tie.colorBytes.Length}, expected {expectedColorBytes}");
                                writer.WriteLine($"     This will likely cause buffer overflow and memory corruption");
                            }
                        }
                    }

                    if (colorBufferRisks == 0)
                    {
                        writer.WriteLine("  ✅ All TIE instances have properly sized color buffers");
                    }
                    else
                    {
                        writer.WriteLine($"\n  Found {colorBufferRisks} TIEs with potential color buffer overflow issues");
                        writer.WriteLine("  This could cause memory access violations during colorBytes processing");
                    }
                }

                // Deep pointer analysis
                writer.WriteLine("\n5. TIE POINTER CHAIN ANALYSIS:");
                AnalyzeTiePointerChains(level, writer);

                // Check for NULL pointers or invalid offsets in TIE structures
                writer.WriteLine("\n6. NULL POINTER / ZERO OFFSET ANALYSIS:");
                if (level.ties != null)
                {
                    int suspiciousOffsets = 0;
                    foreach (var tie in level.ties)
                    {
                        // Check for suspicious values that might indicate bad pointers
                        if (tie.modelID <= 0)
                        {
                            suspiciousOffsets++;
                            writer.WriteLine($"  ❌ TIE has suspicious modelID: {tie.modelID}");
                        }
                    }

                    writer.WriteLine($"\nFound {suspiciousOffsets} TIEs with potentially dangerous pointer values");
                }

                // Generate memory access risk assessment for TIEs
                writer.WriteLine("\n7. TIE MEMORY ACCESS RISK ASSESSMENT:");
                CalculateMemoryRiskScore(level, writer);
            }

            Console.WriteLine($"✅ TIE memory structure analysis completed. Report saved to {reportFile}");
        }

        /// <summary>
        /// Analyzes memory patterns in a byte array looking for potential issues
        /// </summary>
        private static void AnalyzeMemoryPatterns(StreamWriter writer, byte[] data, string name)
        {
            if (data == null || data.Length == 0) return;

            // Check for potential null pointer patterns (consecutive zeros)
            int nullPointerPatterns = 0;
            int suspiciousAddressPatterns = 0;

            // Check for sequences that might indicate null pointers or suspicious addresses
            for (int i = 0; i < data.Length - 4; i += 4)
            {
                uint value = BitConverter.ToUInt32(data, i);

                // Detect null pointers
                if (value == 0)
                {
                    nullPointerPatterns++;
                }

                // Detect suspiciously high or invalid-looking memory addresses
                // These patterns have been known to cause crashes in RC games
                if ((value >= 0x40000000 && value < 0x50000000) || // Common area for access violations
                    (value >= 0x80000000 && value < 0x90000000))   // Another problematic range
                {
                    suspiciousAddressPatterns++;
                    writer.WriteLine($"  ⚠️ Found suspicious memory address 0x{value:X8} at offset 0x{i:X} in {name}");

                    // Only show the first few to avoid flooding the report
                    if (suspiciousAddressPatterns >= 5)
                    {
                        writer.WriteLine("  (Additional suspicious addresses omitted)");
                        break;
                    }
                }
            }

            writer.WriteLine($"  Found {nullPointerPatterns} potential null pointers in {name}");
            writer.WriteLine($"  Found {suspiciousAddressPatterns} suspicious address patterns in {name}");
        }

        /// <summary>
        /// Analyzes TIE pointer chains for potential issues
        /// </summary>
        private static void AnalyzeTiePointerChains(Level level, StreamWriter writer)
        {
            if (level.ties == null || level.ties.Count == 0)
            {
                writer.WriteLine("  No TIEs to analyze pointer chains for");
                return;
            }

            int brokenChains = 0;
            int completeChains = 0;

            foreach (var tie in level.ties)
            {
                bool isChainComplete = true;
                List<string> issues = new List<string>();

                // Check TIE -> Model chain
                if (tie.model == null)
                {
                    isChainComplete = false;
                    issues.Add($"TIE -> Model: BROKEN (modelID {tie.modelID} not linked to actual model)");
                }
                else
                {
                    issues.Add($"TIE -> Model: OK (modelID {tie.modelID})");

                    // Check Model -> Vertex Buffer chain
                    if (tie.model.vertexBuffer == null || tie.model.vertexBuffer.Length == 0)
                    {
                        isChainComplete = false;
                        issues.Add("Model -> VertexBuffer: BROKEN (null or empty)");
                    }
                    else
                    {
                        issues.Add($"Model -> VertexBuffer: OK ({tie.model.vertexBuffer.Length / 8} vertices)");
                    }

                    // Check Model -> Index Buffer chain
                    if (tie.model.indexBuffer == null || tie.model.indexBuffer.Length == 0)
                    {
                        isChainComplete = false;
                        issues.Add("Model -> IndexBuffer: BROKEN (null or empty)");
                    }
                    else
                    {
                        issues.Add($"Model -> IndexBuffer: OK ({tie.model.indexBuffer.Length / 3} triangles)");
                    }

                    // Check Model -> Texture chain
                    if (tie.model.textureConfig == null || tie.model.textureConfig.Count == 0)
                    {
                        isChainComplete = false;
                        issues.Add("Model -> TextureConfig: BROKEN (null or empty)");
                    }
                    else
                    {
                        bool allTextureRefsValid = true;
                        foreach (var texConfig in tie.model.textureConfig)
                        {
                            if (texConfig.id < 0 || (level.textures != null && texConfig.id >= level.textures.Count))
                            {
                                allTextureRefsValid = false;
                                issues.Add($"TextureConfig -> Texture: BROKEN (invalid index {texConfig.id})");
                                break;
                            }
                        }

                        if (allTextureRefsValid)
                        {
                            issues.Add($"Model -> TextureConfig -> Texture: OK ({tie.model.textureConfig.Count} configs)");
                        }
                        else
                        {
                            isChainComplete = false;
                        }
                    }

                    // Check TIE -> ColorBytes chain
                    int expectedColorBytes = tie.model.vertexBuffer.Length / 8 * 4;
                    if (tie.colorBytes == null)
                    {
                        isChainComplete = false;
                        issues.Add("TIE -> ColorBytes: BROKEN (null)");
                    }
                    else if (tie.colorBytes.Length != expectedColorBytes)
                    {
                        isChainComplete = false;
                        issues.Add($"TIE -> ColorBytes: BROKEN (size mismatch: {tie.colorBytes.Length} vs expected {expectedColorBytes})");
                    }
                    else
                    {
                        issues.Add($"TIE -> ColorBytes: OK ({tie.colorBytes.Length} bytes)");
                    }
                }

                if (isChainComplete)
                {
                    completeChains++;
                }
                else
                {
                    brokenChains++;
                    writer.WriteLine($"\n  ⚠️ TIE at position {tie.position} has broken pointer chain:");
                    foreach (var issue in issues)
                    {
                        writer.WriteLine($"    - {issue}");
                    }
                }
            }

            writer.WriteLine($"\n  Complete pointer chains: {completeChains}");
            writer.WriteLine($"  Broken pointer chains: {brokenChains}");

            if (brokenChains > 0)
            {
                writer.WriteLine("  ❌ Broken pointer chains WILL cause memory access violations!");
                writer.WriteLine("     These must be fixed before the level will run properly.");
            }
        }

        /// <summary>
        /// Calculates a numeric risk score for memory access issues
        /// </summary>
        private static void CalculateMemoryRiskScore(Level level, StreamWriter writer)
        {
            int riskScore = 0;
            List<string> riskFactors = new List<string>();

            // Check if tie data alignment is compromised
            if (level.tieData != null && level.tieData.Length % 0x70 != 0)
            {
                riskScore += 25;
                riskFactors.Add("TIE data has improper alignment (not multiple of 0x70 bytes)");
            }

            // Check if tie group data alignment is compromised
            if (level.tieGroupData != null && level.tieGroupData.Length % 0x80 != 0)
            {
                riskScore += 20;
                riskFactors.Add("TIE group data is not aligned to 0x80 boundaries");
            }

            // Check for missing models
            if (level.ties != null && level.tieModels != null)
            {
                var modelIds = new HashSet<int>(level.tieModels.Select(m => (int)m.id));
                int missingModels = level.ties.Count(t => !modelIds.Contains(t.modelID));

                if (missingModels > 0)
                {
                    riskScore += 40;
                    riskFactors.Add($"{missingModels} TIEs reference non-existent models");
                }
            }

            // Check for invalid texture references
            int invalidTextureRefs = 0;
            if (level.tieModels != null && level.textures != null)
            {
                foreach (var model in level.tieModels.OfType<TieModel>())
                {
                    if (model.textureConfig != null)
                    {
                        foreach (var texConfig in model.textureConfig)
                        {
                            if (texConfig.id < 0 || texConfig.id >= level.textures.Count)
                            {
                                invalidTextureRefs++;
                            }
                        }
                    }
                }

                if (invalidTextureRefs > 0)
                {
                    riskScore += 35;
                    riskFactors.Add($"{invalidTextureRefs} invalid texture references");
                }
            }

            // Check for colorBytes misalignment
            int colorBytesIssues = 0;
            if (level.ties != null)
            {
                foreach (var tie in level.ties)
                {
                    if (tie.model?.vertexBuffer != null)
                    {
                        int expected = tie.model.vertexBuffer.Length / 8 * 4;
                        if (tie.colorBytes == null || tie.colorBytes.Length != expected)
                        {
                            colorBytesIssues++;
                        }
                    }
                }

                if (colorBytesIssues > 0)
                {
                    riskScore += 30;
                    riskFactors.Add($"{colorBytesIssues} TIEs have misaligned colorBytes");
                }
            }

            // Check for vertex buffer stride issues
            if (level.tieModels != null)
            {
                var badStride = level.tieModels.Count(m => m.vertexBuffer != null && m.vertexBuffer.Length % 8 != 0);
                if (badStride > 0)
                {
                    riskScore += 25;
                    riskFactors.Add($"{badStride} TIE models have incorrect vertex buffer stride");
                }
            }

            // Calculate crash risk level
            string riskLevel;
            string remediation;

            if (riskScore >= 70)
            {
                riskLevel = "CRITICAL (Game will crash)";
                remediation = "This level cannot run until critical memory issues are fixed";
            }
            else if (riskScore >= 40)
            {
                riskLevel = "HIGH (Likely to crash)";
                remediation = "Multiple serious memory access issues need to be addressed";
            }
            else if (riskScore >= 20)
            {
                riskLevel = "MODERATE (May crash under certain conditions)";
                remediation = "Some memory issues should be fixed for stability";
            }
            else if (riskScore > 0)
            {
                riskLevel = "LOW (Potential instability)";
                remediation = "Minor issues may cause instability in edge cases";
            }
            else
            {
                riskLevel = "MINIMAL (No obvious issues)";
                remediation = "No memory access issues detected";
            }

            writer.WriteLine($"Memory Access Risk Score: {riskScore}/100");
            writer.WriteLine($"Risk Level: {riskLevel}");
            writer.WriteLine($"Remediation: {remediation}");

            if (riskFactors.Count > 0)
            {
                writer.WriteLine("\nRisk factors identified:");
                foreach (var factor in riskFactors)
                {
                    writer.WriteLine($"  - {factor}");
                }
            }
        }

        /// <summary>
        /// Analyzes potential TIE crash causes and provides detailed debugging information
        /// </summary>
        /// <param name="level">The level to analyze</param>
        /// <param name="outputPath">Path where to save the diagnostic report</param>
        /// <param name="addrHint">Optional memory address from crash report (e.g. 0x4A300000)</param>
        public static void AnalyzeTieCrashCauses(Level level, string outputPath, uint? addrHint = null)
        {
            string? directoryPath = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            else
            {
                Console.WriteLine($"❌ ERROR: Unable to determine directory path for output: {outputPath}");
            }
            string reportFile = outputPath;

            Console.WriteLine($"🔍 Analyzing potential TIE crash causes...");

            int expectedOff54 = level.game.num == 1 ? 720 : 4000;

            using (var writer = new StreamWriter(reportFile))
            {
                writer.WriteLine("======== TIE CRASH ANALYSIS REPORT ========");
                writer.WriteLine($"Generated on: {DateTime.Now}");

                if (addrHint.HasValue)
                {
                    writer.WriteLine($"Analyzing with crash address hint: 0x{addrHint.Value:X8}");
                }

                writer.WriteLine("===========================================\n");

                writer.WriteLine("1. CRITICAL ISSUES THAT COULD CAUSE CRASHES:");
                writer.WriteLine("-------------------------------------------");

                // Check 1: Missing models
                writer.WriteLine("\nA. MODEL REFERENCE VALIDATION:");

                if (level.ties != null && level.tieModels != null)
                {
                    var modelIds = new HashSet<int>(level.tieModels.Select(m => (int)m.id));
                    var badRefs = level.ties.Where(t => !modelIds.Contains(t.modelID)).ToList();

                    if (badRefs.Count > 0)
                    {
                        writer.WriteLine($"  ❌ CRITICAL: Found {badRefs.Count} TIEs with invalid model references");
                        writer.WriteLine("     This will cause crashes when the game tries to render these TIEs");

                        foreach (var tie in badRefs.Take(5))
                        {
                            writer.WriteLine($"     - TIE at {tie.position} references model ID {tie.modelID} which doesn't exist");
                        }

                        if (badRefs.Count > 5)
                        {
                            writer.WriteLine($"     - ... and {badRefs.Count - 5} more");
                        }
                    }
                    else
                    {
                        writer.WriteLine("  ✅ All TIEs reference valid models");
                    }
                }

                // Check 2: Invalid texture references
                writer.WriteLine("\nB. TEXTURE REFERENCE VALIDATION:");

                if (level.tieModels != null && level.textures != null)
                {
                    var modelsWithBadTextures = new List<TieModel>();

                    foreach (var model in level.tieModels.OfType<TieModel>())
                    {
                        if (model.textureConfig != null)
                        {
                            bool hasBadTexture = false;
                            foreach (var texConfig in model.textureConfig)
                            {
                                if (texConfig.id < 0 || texConfig.id >= level.textures.Count)
                                {
                                    hasBadTexture = true;
                                    break;
                                }
                            }

                            if (hasBadTexture)
                            {
                                modelsWithBadTextures.Add(model);
                            }
                        }
                    }

                    if (modelsWithBadTextures.Count > 0)
                    {
                        writer.WriteLine($"  ❌ CRITICAL: Found {modelsWithBadTextures.Count} models with invalid texture references");
                        writer.WriteLine("     This will cause crashes when the game tries to load these textures");

                        foreach (var model in modelsWithBadTextures.Take(5))
                        {
                            writer.Write($"     - Model ID {model.id} has invalid texture IDs: ");

                            if (model.textureConfig != null)
                            {
                                var badIds = model.textureConfig
                                    .Where(tc => tc.id < 0 || tc.id >= level.textures.Count)
                                    .Select(tc => tc.id.ToString());

                                writer.WriteLine(string.Join(", ", badIds));
                            }
                        }

                        if (modelsWithBadTextures.Count > 5)
                        {
                            writer.WriteLine($"     - ... and {modelsWithBadTextures.Count - 5} more");
                        }

                        // If we have an address hint from a crash, check if it could be texture-related
                        if (addrHint.HasValue && addrHint.Value >= 0x40000000 && addrHint.Value < 0x50000000)
                        {
                            writer.WriteLine($"\n  ⚠️ The crash address 0x{addrHint.Value:X8} is in a range commonly associated");
                            writer.WriteLine("     with texture or model data access. Invalid texture references are the");
                            writer.WriteLine("     most likely cause of this crash.");
                        }
                    }
                    else
                    {
                        writer.WriteLine("  ✅ All texture references are valid");
                    }
                }

                // Check 3: Buffer alignment issues
                writer.WriteLine("\nC. BUFFER ALIGNMENT ISSUES:");

                if (level.ties != null)
                {
                    var tiesWithBadColorBytes = new List<Tie>();

                    foreach (var tie in level.ties)
                    {
                        if (tie.model?.vertexBuffer != null)
                        {
                            int expected = tie.model.vertexBuffer.Length / 8 * 4;
                            if (tie.colorBytes == null || tie.colorBytes.Length != expected)
                            {
                                tiesWithBadColorBytes.Add(tie);
                            }
                        }
                    }

                    if (tiesWithBadColorBytes.Count > 0)
                    {
                        writer.WriteLine($"  ❌ CRITICAL: Found {tiesWithBadColorBytes.Count} TIEs with misaligned colorBytes");
                        writer.WriteLine("     This will cause buffer overruns and memory corruption");

                        foreach (var tie in tiesWithBadColorBytes.Take(5))
                        {
                            int expected = tie.model?.vertexBuffer?.Length / 8 * 4 ?? 0;
                            writer.WriteLine($"     - TIE at {tie.position} has {tie.colorBytes?.Length ?? 0} colorBytes, expected {expected}");
                        }

                        if (tiesWithBadColorBytes.Count > 5)
                        {
                            writer.WriteLine($"     - ... and {tiesWithBadColorBytes.Count - 5} more");
                        }
                    }
                    else
                    {
                        writer.WriteLine("  ✅ All TIE color buffers have proper alignment");
                    }
                }

                // Check 4: Memory strides
                writer.WriteLine("\nD. VERTEX BUFFER STRIDE ISSUES:");

                if (level.tieModels != null)
                {
                    var badStrides = level.tieModels
                        .Where(m => m.vertexBuffer != null && m.vertexBuffer.Length % 8 != 0)
                        .ToList();

                    if (badStrides.Count > 0)
                    {
                        writer.WriteLine($"  ❌ CRITICAL: Found {badStrides.Count} models with incorrect vertex buffer stride");
                        writer.WriteLine("     This will cause vertex attribute fetching to fail and crash the game");

                        foreach (var model in badStrides.Take(5))
                        {
                            writer.WriteLine($"     - Model ID {model.id} has vertex buffer length {model.vertexBuffer.Length} (not divisible by 8)");
                        }

                        if (badStrides.Count > 5)
                        {
                            writer.WriteLine($"     - ... and {badStrides.Count - 5} more");
                        }
                    }
                    else
                    {
                        writer.WriteLine("  ✅ All vertex buffers have correct stride values");
                    }
                }

                // Check 5: Common culprit: RC1 vs RC2 format differences
                writer.WriteLine("\n2. RC1/RC2 FORMAT COMPATIBILITY ISSUES:");
                writer.WriteLine("------------------------------------");

                if (level.tieModels != null)
                {
                    int nonStandardRC2Models = 0;

                    foreach (var model in level.tieModels)
                    {
                        // Instead of checking model.off54 (which doesn't exist in TieModel),
                        // we need to check if any Tie instances using this model have non-standard off54 values
                        if (level.ties != null)
                        {
                            var tiesUsingThisModel = level.ties.Where(t => t.modelID == model.id).ToList();
                            var nonStandardTies = tiesUsingThisModel.Where(t => t.off54 != expectedOff54).ToList();

                            if (nonStandardTies.Count > 0)
                            {
                                nonStandardRC2Models++;
                                writer.WriteLine($"  ⚠️ Model ID {model.id} is used by TIEs with non-standard values (off54 != {expectedOff54})");
                            }
                        }
                    }

                    if (nonStandardRC2Models > 0)
                    {
                        writer.WriteLine($"\n  Found {nonStandardRC2Models} models with potential RC1/RC2 compatibility issues");
                        writer.WriteLine("  This could cause rendering or memory access problems");
                    }
                    else
                    {
                        writer.WriteLine("  ✅ No obvious RC1/RC2 compatibility issues detected");
                    }
                }

                // Provide actionable solutions
                writer.WriteLine("\n3. RECOMMENDED FIXES:");
                writer.WriteLine("---------------------");

                writer.WriteLine("If your level is crashing with TIEs, try these fixes in order:");

                writer.WriteLine("\n1. Fix Invalid Model References");
                writer.WriteLine("   - Ensure every TIE references an existing model ID");
                writer.WriteLine("   - Add code to validate model IDs before saving: level.ties.RemoveAll(t => !modelIdSet.Contains(t.modelID));");

                writer.WriteLine("\n2. Fix Texture References");
                writer.WriteLine("   - Map texture IDs from source to target level");
                writer.WriteLine("   - Add code to import required textures before using them");
                writer.WriteLine("   - Fix TextureConfig entries to use only valid texture indices");

                writer.WriteLine("\n3. Fix ColorBytes Buffer Alignment");
                writer.WriteLine("   - Regenerate colorBytes for each TIE based on its actual model");
                writer.WriteLine("   - Example: tie.colorBytes = new byte[tie.model.vertexBuffer.Length / 8 * 4];");

                writer.WriteLine("\n4. Fix TIE Serialization Data");
                writer.WriteLine("   - Regenerate tieData and tieGroupData with proper alignment");
                writer.WriteLine("   - Ensure 0x70-byte TIE structures and 0x80-aligned group data");

                // Special case if the address hint is provided
                if (addrHint.HasValue)
                {
                    writer.WriteLine("\n4. CRASH ADDRESS ANALYSIS:");
                    writer.WriteLine("-------------------------");

                    if (addrHint.Value >= 0x40000000 && addrHint.Value < 0x50000000)
                    {
                        writer.WriteLine($"The crash address 0x{addrHint.Value:X8} is in a memory region typically used for:");
                        writer.WriteLine("- Texture data");
                        writer.WriteLine("- Model vertex/index buffers");
                        writer.WriteLine("- Rendering resources");
                        writer.WriteLine("\nThis strongly suggests the crash is related to:");
                        writer.WriteLine("1. Invalid texture references in TIE models");
                        writer.WriteLine("2. Corrupted vertex or index buffer");
                        writer.WriteLine("3. Misaligned colorBytes buffer");
                    }
                    else if (addrHint.Value >= 0x00000000 && addrHint.Value < 0x10000000)
                    {
                        writer.WriteLine($"The crash address 0x{addrHint.Value:X8} is very low, suggesting:");
                        writer.WriteLine("- Null or near-null pointer dereference");
                        writer.WriteLine("- Likely a missing model or completely corrupted pointer");
                    }
                    else
                    {
                        writer.WriteLine($"The crash address 0x{addrHint.Value:X8} doesn't match common pattern");
                        writer.WriteLine("This may be a more complex issue or corruption in game memory");
                    }
                }

                // Add summary of TIE validation status
                writer.WriteLine("\n5. TIE VALIDATION SUMMARY:");
                writer.WriteLine("--------------------------");

                bool hasCriticalIssues = false;

                if (level.ties != null && level.tieModels != null)
                {
                    // Calculate statistics again to provide a summary
                    var modelIds = new HashSet<int>(level.tieModels.Select(m => (int)m.id));
                    int invalidModelRefs = level.ties.Count(t => !modelIds.Contains(t.modelID));

                    if (invalidModelRefs > 0)
                    {
                        hasCriticalIssues = true;
                        writer.WriteLine($"❌ CRITICAL: {invalidModelRefs} TIEs reference invalid models");
                    }
                    else
                    {
                        writer.WriteLine("✅ All TIEs reference valid models");
                    }

                    // Check texture references again for summary
                    int modelsWithBadTextures = 0;
                    if (level.textures != null)
                    {
                        foreach (var model in level.tieModels.OfType<TieModel>())
                        {
                            if (model.textureConfig != null)
                            {
                                foreach (var texConfig in model.textureConfig)
                                {
                                    if (texConfig.id < 0 || texConfig.id >= level.textures.Count)
                                    {
                                        modelsWithBadTextures++;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (modelsWithBadTextures > 0)
                    {
                        hasCriticalIssues = true;
                        writer.WriteLine($"❌ CRITICAL: {modelsWithBadTextures} models have invalid texture references");
                    }
                    else
                    {
                        writer.WriteLine("✅ All texture references are valid");
                    }

                    // ColorBytes alignment check
                    int tiesWithBadColorBytes = 0;
                    foreach (var tie in level.ties)
                    {
                        if (tie.model?.vertexBuffer != null)
                        {
                            int expected = tie.model.vertexBuffer.Length / 8 * 4;
                            if (tie.colorBytes == null || tie.colorBytes.Length != expected)
                            {
                                tiesWithBadColorBytes++;
                            }
                        }
                    }

                    if (tiesWithBadColorBytes > 0)
                    {
                        hasCriticalIssues = true;
                        writer.WriteLine($"❌ CRITICAL: {tiesWithBadColorBytes} TIEs have misaligned colorBytes");
                    }
                    else
                    {
                        writer.WriteLine("✅ All colorBytes buffers are properly sized");
                    }

                    // Final verdict
                    if (hasCriticalIssues)
                    {
                        writer.WriteLine("\nVERDICT: This level WILL crash due to the critical issues identified above.");
                        writer.WriteLine("         Fix these issues before attempting to run the level.");
                    }
                    else
                    {
                        writer.WriteLine("\nVERDICT: No critical issues were identified that would definitely cause a crash.");
                        writer.WriteLine("         If the level is still crashing, there may be more subtle issues.");
                    }
                }
            }

            Console.WriteLine($"✅ TIE crash analysis completed. Report saved to {reportFile}");
        }

        /// <summary>
        /// Interactive function to investigate a specific TIE crash address
        /// </summary>
        public static void InvestigateTieCrashAddress(Level level, string outputPath, string crashAddress)
        {
            try
            {
                // Parse the crash address as a hexadecimal number
                uint address = 0;

                // Handle different input formats like "0x4A300000" or just "4A300000"
                if (crashAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    crashAddress = crashAddress.Substring(2);
                }

                if (uint.TryParse(crashAddress, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out address))
                {
                    Console.WriteLine($"Investigating crash at address 0x{address:X8}...");
                    AnalyzeTieCrashCauses(level, outputPath, address);
                }
                else
                {
                    Console.WriteLine($"❌ Invalid memory address format: {crashAddress}");
                    Console.WriteLine("Expected format: 0x4A300000 or 4A300000");
                    AnalyzeTieCrashCauses(level, outputPath, null);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during crash investigation: {ex.Message}");
            }
        }

        /// <summary>
        /// Interactive wrapper for TIE diagnostics
        /// </summary>
        public static void RunTieDiagnosticsInteractive()
        {
            Console.WriteLine("\n===== TIE Diagnostic Tools =====");
            Console.WriteLine("1. Analyze single level");
            Console.WriteLine("2. Compare two levels");
            Console.WriteLine("3. Analyze TIE Memory Structures");
            Console.WriteLine("4. Investigate Specific Crash Address");
            Console.Write("Select an option (1-4): ");

            string choice = Console.ReadLine()?.Trim() ?? "1";

            try
            {
                switch (choice)
                {
                    case "1":
                        AnalyzeSingleLevelInteractive();
                        break;
                    case "2":
                        CompareLevelsInteractive();
                        break;
                    case "3":
                        RunTieMemoryAnalysisInteractive();
                        break;
                    case "4":
                        RunTieCrashInvestigationInteractive();
                        break;
                    default:
                        Console.WriteLine("Invalid option. Please select 1, 2, 3, or 4.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during diagnostic: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private static void AnalyzeSingleLevelInteractive()
        {
            Console.WriteLine("\nEnter path to the level engine.ps3 file:");
            Console.Write("> ");
            string levelPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(levelPath))
            {
                Console.WriteLine("❌ File not found");
                return;
            }

            Console.WriteLine("\nEnter label for this level (e.g. 'RC2_Oltanis'):");
            Console.Write("> ");
            string label = Console.ReadLine()?.Trim() ?? "Level";

            Console.WriteLine("\nEnter output path for the diagnostic report:");
            Console.Write("> ");
            string outputPath = Console.ReadLine()?.Trim() ?? Path.Combine(Environment.CurrentDirectory, $"{label}_tie_diagnostics.txt");

            try
            {
                Console.WriteLine($"Loading level from {levelPath}...");
                Level level = new Level(levelPath);

                AnalyzeTies(level, outputPath, label);
                Console.WriteLine($"Analysis complete. Report written to {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error analyzing level: {ex.Message}");
            }
        }

        private static void CompareLevelsInteractive()
        {
            Console.WriteLine("\nEnter path to the SOURCE level engine.ps3 file (e.g. RC1 level):");
            Console.Write("> ");
            string sourcePath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(sourcePath))
            {
                Console.WriteLine("❌ Source file not found");
                return;
            }

            Console.WriteLine("\nEnter label for the source level (e.g. 'RC1_Oltanis'):");
            Console.Write("> ");
            string sourceLabel = Console.ReadLine()?.Trim() ?? "Source";

            Console.WriteLine("\nEnter path to the TARGET level engine.ps3 file (e.g. modified RC2 level):");
            Console.Write("> ");
            string targetPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(targetPath))
            {
                Console.WriteLine("❌ Target file not found");
                return;
            }

            Console.WriteLine("\nEnter label for the target level (e.g. 'RC2_Oltanis_Modified'):");
            Console.Write("> ");
            string targetLabel = Console.ReadLine()?.Trim() ?? "Target";

            Console.WriteLine("\nEnter output path for the comparison report:");
            Console.Write("> ");
            string outputPath = Console.ReadLine()?.Trim() ?? Path.Combine(Environment.CurrentDirectory, $"{sourceLabel}_vs_{targetLabel}_tie_comparison.txt");

            try
            {
                Console.WriteLine($"Loading source level from {sourcePath}...");
                Level sourceLevel = new Level(sourcePath);

                Console.WriteLine($"Loading target level from {targetPath}...");
                Level targetLevel = new Level(targetPath);

                CompareTies(sourceLevel, targetLevel, outputPath, sourceLabel, targetLabel);
                Console.WriteLine($"Comparison complete. Report written to {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error comparing levels: {ex.Message}");
            }
        }

        private static void RunTieMemoryAnalysisInteractive()
        {
            Console.WriteLine("\nEnter path to the level engine.ps3 file:");
            Console.Write("> ");
            string levelPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(levelPath))
            {
                Console.WriteLine("❌ File not found");
                return;
            }

            Console.WriteLine("\nEnter label for this level (e.g. 'RC2_Oltanis'):");
            Console.Write("> ");
            string label = Console.ReadLine()?.Trim() ?? "Level";

            Console.WriteLine("\nEnter output path for the memory analysis report:");
            Console.Write("> ");
            string outputPath = Console.ReadLine()?.Trim() ?? Path.Combine(Environment.CurrentDirectory, $"{label}_tie_memory_analysis.txt");

            try
            {
                Console.WriteLine($"Loading level from {levelPath}...");
                Level level = new Level(levelPath);

                AnalyzeTieMemoryStructures(level, outputPath, label);
                Console.WriteLine($"Analysis complete. Report written to {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error analyzing level: {ex.Message}");
            }
        }

        private static void RunTieCrashInvestigationInteractive()
        {
            Console.WriteLine("\nEnter path to the level engine.ps3 file:");
            Console.Write("> ");
            string levelPath = Console.ReadLine()?.Trim() ?? "";

            if (!File.Exists(levelPath))
            {
                Console.WriteLine("❌ File not found");
                return;
            }

            Console.WriteLine("\nEnter the crash memory address (e.g. '0x4A300000'):");
            Console.Write("> ");
            string crashAddress = Console.ReadLine()?.Trim() ?? "";

            Console.WriteLine("\nEnter output path for the crash analysis report:");
            Console.Write("> ");
            string outputPath = Console.ReadLine()?.Trim() ?? Path.Combine(Environment.CurrentDirectory, $"tie_crash_analysis.txt");

            try
            {
                Console.WriteLine($"Loading level from {levelPath}...");
                Level level = new Level(levelPath);

                InvestigateTieCrashAddress(level, outputPath, crashAddress);
                Console.WriteLine($"Analysis complete. Report written to {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error analyzing level: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to compare values and write formatted output
        /// </summary>
        private static void CompareAndWrite<T>(StreamWriter writer, string propertyName, T value1, T value2)
        {
            bool matches = EqualityComparer<T>.Default.Equals(value1, value2);
            string matchIndicator = matches ? "✅" : "❌";

            writer.WriteLine($"{matchIndicator} {propertyName}: {value1} vs {value2}");
        }

        /// <summary>
        /// Checks for and reports model ID conflicts between Mobys and TIEs
        /// </summary>
        /// <param name="level">The level to check</param>
        /// <param name="outputPath">Path to write the report</param>
        public static void CheckMobyTieIdConflicts(Level level, string outputPath)
        {
            if (level == null)
            {
                Console.WriteLine("❌ Cannot check ID conflicts: Level is null");
                return;
            }

            using (var writer = new StreamWriter(Path.Combine(outputPath, "id_conflicts.txt")))
            {
                writer.WriteLine($"=== MOBY-TIE ID CONFLICT ANALYSIS ===");
                writer.WriteLine($"Level: {Path.GetFileName(level.path)}");
                writer.WriteLine($"Date: {DateTime.Now}");
                writer.WriteLine();

                // Collect moby model IDs
                HashSet<int> mobyIds = new HashSet<int>();
                if (level.mobyModels != null)
                {
                    foreach (var model in level.mobyModels)
                    {
                        if (model != null)
                        {
                            mobyIds.Add(model.id);
                        }
                    }
                }
                writer.WriteLine($"Found {mobyIds.Count} unique moby model IDs");

                // Collect tie model IDs
                HashSet<int> tieIds = new HashSet<int>();
                if (level.tieModels != null)
                {
                    foreach (var model in level.tieModels)
                    {
                        if (model != null)
                        {
                            tieIds.Add(model.id);
                        }
                    }
                }
                writer.WriteLine($"Found {tieIds.Count} unique tie model IDs");

                // Find conflicts
                var conflicts = mobyIds.Intersect(tieIds).ToList();
                writer.WriteLine();

                if (conflicts.Count > 0)
                {
                    writer.WriteLine($"⚠️ FOUND {conflicts.Count} ID CONFLICTS ⚠️");
                    writer.WriteLine();
                    writer.WriteLine("Conflicting IDs:");
                    foreach (var id in conflicts)
                    {
                        writer.WriteLine($"- ID {id}");
                    }

                    // Check if any mobys use these conflicting IDs
                    var usedByMobys = level.mobs?.Where(m => conflicts.Contains(m.modelID)).ToList();
                    if (usedByMobys != null && usedByMobys.Count > 0)
                    {
                        writer.WriteLine();
                        writer.WriteLine($"{usedByMobys.Count} moby instances use conflicting IDs:");
                        foreach (var moby in usedByMobys)
                        {
                            writer.WriteLine($"- Moby at position {moby.position} uses model ID {moby.modelID}");
                        }
                    }

                    // Check if any ties use these conflicting IDs
                    var usedByTies = level.ties?.Where(t => conflicts.Contains(t.modelID)).ToList();
                    if (usedByTies != null && usedByTies.Count > 0)
                    {
                        writer.WriteLine();
                        writer.WriteLine($"{usedByTies.Count} tie instances use conflicting IDs:");
                        foreach (var tie in usedByTies)
                        {
                            writer.WriteLine($"- Tie at position {tie.position} uses model ID {tie.modelID}");
                        }
                    }
                }
                else
                {
                    writer.WriteLine("✅ No ID conflicts found between Moby models and TIE models");
                }
            }

            Console.WriteLine($"ID conflict analysis completed. Report written to {Path.Combine(outputPath, "id_conflicts.txt")}");
        }
        public static void CheckTieVertexBufferStrides(Level level, string outputPath)
        {
            Console.WriteLine("🔍 Checking TIE vertex buffer strides...");

            using (var writer = new StreamWriter(Path.Combine(outputPath, "tie_stride_issues.log")))
            {
                writer.WriteLine("TIE Vertex Buffer Stride Analysis");
                writer.WriteLine("================================");
                writer.WriteLine($"Date: {DateTime.Now}");
                writer.WriteLine();

                int errorCount = 0;
                int crashRisk = 0;

                if (level.tieModels == null || level.tieModels.Count == 0)
                {
                    writer.WriteLine("No TIE models found in level.");
                    Console.WriteLine("  No TIE models to analyze.");
                    return;
                }

                // Check models directly
                foreach (var model in level.tieModels)
                {
                    if (model.vertexBuffer == null) continue;

                    int bufferLength = model.vertexBuffer.Length;
                    const int stride = 8; // RC2 standard = 8 floats

                    if (bufferLength % stride != 0)
                    {
                        string error = $"❌ TIE model ID {model.id} has incorrect vertex stride. " +
                                       $"Buffer length {bufferLength} is not divisible by {stride} (RC2 stride)";
                        writer.WriteLine(error);
                        Console.WriteLine($"  {error}");
                        errorCount++;
                        crashRisk++;
                    }
                }

                // Check ties using the models
                if (level.ties != null)
                {
                    foreach (var tie in level.ties)
                    {
                        if (tie.model == null)
                        {
                            // Don't double count, we'll catch these in the model reference check
                            continue;
                        }

                        int bufferLength = tie.model.vertexBuffer?.Length ?? 0;
                        if (bufferLength > 0 && bufferLength % 8 != 0)
                        {
                            string error = $"❌ TIE with ID {tie.off58} uses model {tie.modelID} with incorrect vertex stride " +
                                           $"(buffer length {bufferLength} not divisible by 8)";
                            writer.WriteLine(error);
                            Console.WriteLine($"  {error}");
                            errorCount++;
                        }
                    }
                }

                if (errorCount == 0)
                {
                    writer.WriteLine("✅ All TIE vertex buffers have correct stride (divisible by 8).");
                    Console.WriteLine("  ✅ All TIE vertex buffers have correct stride.");
                }
                else
                {
                    writer.WriteLine($"\nFound {errorCount} TIE models with incorrect vertex buffer strides.");
                    writer.WriteLine($"⚠️ This will cause memory access violations in the rendering engine!");
                    Console.WriteLine($"  ❌ Found {errorCount} TIEs with incorrect vertex buffer strides - WILL CRASH GAME!");
                }

                if (crashRisk > 0)
                {
                    writer.WriteLine("\n🔴 This level has TIEs that will CRASH THE GAME.");
                    writer.WriteLine("Consider stripping these models or fixing their vertex buffers.");
                }
            }
        }

        public static void CheckTieTextureReferences(Level level, string outputPath)
        {
            Console.WriteLine("🔍 Checking TIE texture references...");

            using (var writer = new StreamWriter(Path.Combine(outputPath, "tie_texture_issues.log")))
            {
                writer.WriteLine("TIE Texture Reference Analysis");
                writer.WriteLine("============================");
                writer.WriteLine($"Date: {DateTime.Now}");
                writer.WriteLine();

                int warningCount = 0;
                int crashRisk = 0;

                if (level.textures == null)
                {
                    writer.WriteLine("❌ CRITICAL: Level has no textures array!");
                    Console.WriteLine("  ❌ CRITICAL: Level has no textures array!");
                    return;
                }

                int maxValidTextureIndex = level.textures.Count - 1;

                // First check all TIE models directly
                if (level.tieModels != null)
                {
                    foreach (var model in level.tieModels)
                    {
                        if (model.textureConfig == null) continue;

                        bool hasInvalidTexture = false;
                        List<int> invalidIndices = new List<int>();

                        foreach (var texConfig in model.textureConfig)
                        {
                            if (texConfig.id < 0 || texConfig.id > maxValidTextureIndex)
                            {
                                hasInvalidTexture = true;
                                invalidIndices.Add(texConfig.id);
                            }
                        }

                        if (hasInvalidTexture)
                        {
                            string warning = $"❌ TIE model ID {model.id} references invalid texture indices: " +
                                             $"{string.Join(", ", invalidIndices)}. Valid range is [0, {maxValidTextureIndex}].";
                            writer.WriteLine(warning);
                            Console.WriteLine($"  {warning}");
                            warningCount++;
                            crashRisk++;
                        }
                    }
                }

                // Then check all TIE instances
                if (level.ties != null)
                {
                    foreach (var tie in level.ties)
                    {
                        if (tie.model == null || tie.model.textureConfig == null) continue;

                        bool hasInvalidTexture = false;
                        List<int> invalidIndices = new List<int>();

                        foreach (var texConfig in tie.model.textureConfig)
                        {
                            if (texConfig.id < 0 || texConfig.id > maxValidTextureIndex)
                            {
                                hasInvalidTexture = true;
                                invalidIndices.Add(texConfig.id);
                            }
                        }

                        if (hasInvalidTexture)
                        {
                            string warning = $"❌ TIE with ID {tie.off58} (model ID {tie.modelID}) references invalid texture indices: " +
                                             $"{string.Join(", ", invalidIndices)}. Valid range is [0, {maxValidTextureIndex}].";
                            writer.WriteLine(warning);
                            Console.WriteLine($"  {warning}");
                            warningCount++;
                        }
                    }
                }

                if (warningCount == 0)
                {
                    writer.WriteLine("✅ All TIE texture references are valid.");
                    Console.WriteLine("  ✅ All TIE texture references are valid.");
                }
                else
                {
                    writer.WriteLine($"\nFound {warningCount} TIEs with invalid texture references.");
                    writer.WriteLine($"⚠️ The game WILL CRASH when trying to load these textures!");
                    Console.WriteLine($"  ❌ Found {warningCount} TIEs with invalid texture references - WILL CRASH GAME!");
                }

                if (crashRisk > 0)
                {
                    writer.WriteLine("\n🔴 This level has TIEs with invalid texture references that will CRASH THE GAME.");
                    writer.WriteLine("Consider fixing texture references or removing these models.");
                }
            }
        }

        public static void CheckDuplicateTieIds(Level level, string outputPath)
        {
            Console.WriteLine("🔍 Checking for duplicate TIE IDs (off58)...");

            using (var writer = new StreamWriter(Path.Combine(outputPath, "tie_duplicate_ids.log")))
            {
                writer.WriteLine("TIE Duplicate ID Analysis");
                writer.WriteLine("=======================");
                writer.WriteLine($"Date: {DateTime.Now}");
                writer.WriteLine();

                if (level.ties == null || level.ties.Count == 0)
                {
                    writer.WriteLine("No TIEs found in level.");
                    Console.WriteLine("  No TIEs to analyze.");
                    return;
                }

                var tieIdCounts = new Dictionary<uint, List<int>>();

                for (int i = 0; i < level.ties.Count; i++)
                {
                    var tie = level.ties[i];
                    uint tieId = tie.off58;

                    if (!tieIdCounts.ContainsKey(tieId))
                    {
                        tieIdCounts[tieId] = new List<int>();
                    }

                    tieIdCounts[tieId].Add(i);
                }

                int duplicateIdSets = 0;

                foreach (var kvp in tieIdCounts)
                {
                    if (kvp.Value.Count > 1)
                    {
                        duplicateIdSets++;
                        string duplicateMsg = $"⚠️ Found duplicate TIE ID (off58): {kvp.Key}, used by {kvp.Value.Count} TIEs at indices: " +
                                              $"{string.Join(", ", kvp.Value)}";
                        writer.WriteLine(duplicateMsg);
                        Console.WriteLine($"  {duplicateMsg}");

                        // Write details about each tie with this duplicate ID
                        foreach (int tieIdx in kvp.Value)
                        {
                            var tie = level.ties[tieIdx];
                            writer.WriteLine($"    TIE[{tieIdx}]: Model ID={tie.modelID}, Position={tie.position}, " +
                                            $"off54={tie.off54}, off5C={tie.off5C}");
                        }
                        writer.WriteLine();
                    }
                }

                if (duplicateIdSets == 0)
                {
                    writer.WriteLine("✅ All TIE IDs (off58) are unique.");
                    Console.WriteLine("  ✅ All TIE IDs (off58) are unique.");
                }
                else
                {
                    writer.WriteLine($"\nFound {duplicateIdSets} sets of duplicate TIE IDs.");
                    Console.WriteLine($"  ⚠️ Found {duplicateIdSets} sets of duplicate TIE IDs.");
                }
            }
        }

        public static void DumpTieModelInfo(Level level, string outputPath, bool includeVertexData = false)
        {
            Console.WriteLine("📊 Generating TIE model info dump...");

            using (var writer = new StreamWriter(Path.Combine(outputPath, "tie_model_info.csv")))
            {
                writer.WriteLine("TIE_Index,Model_ID,Vertex_Count,Buffer_Length,Stride,Texture_Count,Texture_IDs,Off54,Off58,Off5C,Off64,Light,Position");

                if (level.ties == null || level.ties.Count == 0)
                {
                    Console.WriteLine("  No TIEs to analyze.");
                    return;
                }

                for (int i = 0; i < level.ties.Count; i++)
                {
                    var tie = level.ties[i];
                    int vertexCount = 0;
                    int bufferLength = 0;
                    int stride = 0;
                    int textureCount = 0;
                    string textureIds = "none";

                    if (tie.model != null)
                    {
                        bufferLength = tie.model.vertexBuffer.Length;
                        stride = tie.model.vertexStride;
                        vertexCount = bufferLength / stride;

                        if (tie.model.textureConfig != null && tie.model.textureConfig.Count > 0)
                        {
                            textureCount = tie.model.textureConfig.Count;
                            textureIds = string.Join(";", tie.model.textureConfig.Select(tc => tc.id));
                        }
                    }

                    writer.WriteLine($"{i},{tie.modelID},{vertexCount},{bufferLength},{stride},{textureCount},{textureIds}," +
                                    $"{tie.off54},{tie.off58},{tie.off5C},{tie.off64},{tie.light}," +
                                    $"\"{tie.position.X},{tie.position.Y},{tie.position.Z}\"");
                }
            }

            // Optionally dump detailed vertex data
            if (includeVertexData)
            {
                using (var writer = new StreamWriter(Path.Combine(outputPath, "tie_vertex_data.csv")))
                {
                    writer.WriteLine("TIE_Index,Model_ID,Vertex_Index,X,Y,Z,NX,NY,NZ,U,V,Color_RGBA");

                    for (int i = 0; i < level.ties.Count; i++)
                    {
                        var tie = level.ties[i];

                        if (tie.model == null || tie.model.vertexBuffer == null || tie.model.vertexBuffer.Length == 0)
                            continue;

                        int stride = tie.model.vertexStride;
                        int vertexCount = tie.model.vertexBuffer.Length / stride;

                        for (int v = 0; v < vertexCount; v++)
                        {
                            float x = 0, y = 0, z = 0, nx = 0, ny = 0, nz = 0, u = 0, v_tex = 0;
                            string color = "N/A";

                            if (v * stride + 7 < tie.model.vertexBuffer.Length)
                            {
                                x = tie.model.vertexBuffer[v * stride + 0];
                                y = tie.model.vertexBuffer[v * stride + 1];
                                z = tie.model.vertexBuffer[v * stride + 2];
                                nx = tie.model.vertexBuffer[v * stride + 3];
                                ny = tie.model.vertexBuffer[v * stride + 4];
                                nz = tie.model.vertexBuffer[v * stride + 5];
                                u = tie.model.vertexBuffer[v * stride + 6];
                                v_tex = tie.model.vertexBuffer[v * stride + 7];

                                if (tie.colorBytes != null && v * 4 + 3 < tie.colorBytes.Length)
                                {
                                    byte r = tie.colorBytes[v * 4 + 0];
                                    byte g = tie.colorBytes[v * 4 + 1];
                                    byte b = tie.colorBytes[v * 4 + 2];
                                    byte a = tie.colorBytes[v * 4 + 3];
                                    color = $"{r},{g},{b},{a}";
                                }
                            }

                            writer.WriteLine($"{i},{tie.modelID},{v},{x},{y},{z},{nx},{ny},{nz},{u},{v_tex},{color}");
                        }
                    }
                }
            }

            Console.WriteLine($"  ✅ TIE model info exported to {outputPath}");
        }

        public static void ValidateTieModelReferences(Level level, string outputPath)
        {
            Console.WriteLine("🔍 Validating TIE model references...");

            using (var writer = new StreamWriter(Path.Combine(outputPath, "tie_model_references.log")))
            {
                writer.WriteLine("TIE Model Reference Validation");
                writer.WriteLine("============================");
                writer.WriteLine($"Date: {DateTime.Now}");
                writer.WriteLine();

                int errorCount = 0;
                int crashRisk = 0;

                if (level.ties == null || level.ties.Count == 0)
                {
                    writer.WriteLine("No TIEs found in level.");
                    Console.WriteLine("  No TIEs to analyze.");
                    return;
                }

                // Create a set of all valid model IDs
                HashSet<int> validModelIds = new HashSet<int>();
                if (level.tieModels != null)
                {
                    foreach (var model in level.tieModels)
                    {
                        validModelIds.Add(model.id);
                    }
                }

                foreach (var tie in level.ties)
                {
                    // Check if the model ID exists in the level's model list
                    if (!validModelIds.Contains(tie.modelID))
                    {
                        string error = $"❌ TIE with ID {tie.off58} references missing model ID {tie.modelID}!";
                        writer.WriteLine(error);
                        Console.WriteLine($"  {error}");
                        errorCount++;
                        crashRisk++;
                    }

                    // Check if the tie has a null model reference
                    if (tie.model == null)
                    {
                        string error = $"❌ TIE with ID {tie.off58} has null model reference - colorBytes will be empty, which will cause rendering crash.";
                        writer.WriteLine(error);
                        Console.WriteLine($"  {error}");
                        errorCount++;
                        crashRisk++;
                    }
                }

                if (errorCount == 0)
                {
                    writer.WriteLine("✅ All TIEs reference valid models.");
                    Console.WriteLine("  ✅ All TIEs reference valid models.");
                }
                else
                {
                    writer.WriteLine($"\nFound {errorCount} TIEs with invalid model references.");
                    writer.WriteLine($"❌ CRITICAL: The game WILL CRASH when trying to render these TIEs!");
                    Console.WriteLine($"  ❌ Found {errorCount} TIEs with invalid model references - WILL CRASH GAME!");
                }

                if (crashRisk > 0)
                {
                    writer.WriteLine("\n🔴 This level has TIEs that will CRASH THE GAME.");
                    writer.WriteLine("Consider removing these TIEs from the level or fixing their model references.");
                }
            }
        }

        public static void CheckTieColorBytesAlignment(Level level, string outputPath)
        {
            Console.WriteLine("🔍 Checking TIE colorBytes alignment...");

            using (var writer = new StreamWriter(Path.Combine(outputPath, "tie_colorbytes_issues.log")))
            {
                writer.WriteLine("TIE ColorBytes Alignment Analysis");
                writer.WriteLine("===============================");
                writer.WriteLine($"Date: {DateTime.Now}");
                writer.WriteLine();

                int errorCount = 0;
                int crashRisk = 0;

                if (level.ties == null || level.ties.Count == 0)
                {
                    writer.WriteLine("No TIEs found in level.");
                    Console.WriteLine("  No TIEs to analyze.");
                    return;
                }

                foreach (var tie in level.ties)
                {
                    // Skip ties without models
                    if (tie.model == null) continue;

                    // Calculate expected colorBytes size
                    int vertexCount = tie.model.vertexBuffer != null ? tie.model.vertexBuffer.Length / 8 : 0;
                    int expectedColorBytesSize = vertexCount * 4; // 4 bytes (RGBA) per vertex

                    if (tie.colorBytes == null)
                    {
                        string error = $"❌ TIE with ID {tie.off58} (model ID {tie.modelID}) has null colorBytes array!";
                        writer.WriteLine(error);
                        Console.WriteLine($"  {error}");
                        errorCount++;
                        crashRisk++;
                    }
                    else if (tie.colorBytes.Length != expectedColorBytesSize)
                    {
                        string error = $"❌ TIE with ID {tie.off58} (model ID {tie.modelID}) has misaligned colorBytes: " +
                                       $"has {tie.colorBytes.Length} bytes, expected {expectedColorBytesSize}";
                        writer.WriteLine(error);
                        Console.WriteLine($"  {error}");
                        errorCount++;
                        crashRisk++;
                    }
                }

                if (errorCount == 0)
                {
                    writer.WriteLine("✅ All TIE colorBytes arrays are properly aligned.");
                    Console.WriteLine("  ✅ All TIE colorBytes arrays are properly aligned.");
                }
                else
                {
                    writer.WriteLine($"\nFound {errorCount} TIEs with colorBytes alignment issues.");
                    writer.WriteLine($"⚠️ These will likely cause buffer overflows and memory corruption!");
                    Console.WriteLine($"  ❌ Found {errorCount} TIEs with colorBytes alignment issues!");
                }

                if (crashRisk > 0)
                {
                    writer.WriteLine("\n🔴 This level has TIEs with colorBytes issues that may CRASH THE GAME.");
                    writer.WriteLine("Consider fixing the colorBytes arrays for these TIEs.");
                    writer.WriteLine("Hint: colorBytes length should be equal to (vertexBuffer.Length / 8 * 4)");
                }
            }
        }
    }
}
