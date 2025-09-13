using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using LibReplanetizer;
using LibReplanetizer.Models;

namespace GeometrySwapper
{
    public static class TerrainDiagnostics
    {
        public static void CompareLevels(Level levelA, Level levelB, string outputDir, string labelA, string labelB)
        {
            Directory.CreateDirectory(outputDir);
            string reportPath = Path.Combine(outputDir, $"terrain_comparison_{labelA}_vs_{labelB}.txt");

            using (var writer = new StreamWriter(reportPath))
            {
                writer.WriteLine($"=== Terrain Comparison Report: {labelA} vs {labelB} ===");
                writer.WriteLine($"Generated: {DateTime.Now}");
                writer.WriteLine();

                DumpTerrain(writer, levelA, labelA);
                DumpTerrain(writer, levelB, labelB);

                writer.WriteLine();
                writer.WriteLine("=== Differences & Summary ===");

                // Fragment counts
                int fragA = levelA.terrainEngine?.fragments?.Count ?? 0;
                int fragB = levelB.terrainEngine?.fragments?.Count ?? 0;
                writer.WriteLine($"Fragment count: {labelA}={fragA}, {labelB}={fragB}");

                // Texture counts
                int texA = levelA.textures?.Count ?? 0;
                int texB = levelB.textures?.Count ?? 0;
                writer.WriteLine($"Texture count: {labelA}={texA}, {labelB}={texB}");

                // Fragment ID sequences
                writer.WriteLine();
                writer.WriteLine("Fragment ID sequences:");
                writer.WriteLine($"{labelA}: {string.Join(", ", GetFragmentIds(levelA))}");
                writer.WriteLine($"{labelB}: {string.Join(", ", GetFragmentIds(levelB))}");

                // Texture usage
                writer.WriteLine();
                writer.WriteLine("Texture usage by terrain fragments:");
                DumpTextureUsage(writer, levelA, labelA);
                DumpTextureUsage(writer, levelB, labelB);

                // Missing/invalid textures
                writer.WriteLine();
                writer.WriteLine("Missing or invalid texture references:");
                DumpMissingTextures(writer, levelA, labelA);
                DumpMissingTextures(writer, levelB, labelB);

                // Degenerate fragments (too few verts/faces)
                writer.WriteLine();
                writer.WriteLine("Degenerate fragments (few verts/faces):");
                DumpDegenerateFragments(writer, levelA, labelA);
                DumpDegenerateFragments(writer, levelB, labelB);

                // Bounding box summary
                writer.WriteLine();
                writer.WriteLine("Terrain bounding box summary:");
                DumpBoundingBox(writer, levelA, labelA);
                DumpBoundingBox(writer, levelB, labelB);

                // Vertex/index count summary
                writer.WriteLine();
                writer.WriteLine("Vertex/Index count summary:");
                DumpVertexIndexSummary(writer, levelA, labelA);
                DumpVertexIndexSummary(writer, levelB, labelB);

                // Texture dimension summary
                writer.WriteLine();
                writer.WriteLine("Texture dimension summary:");
                DumpTextureDimensions(writer, levelA, labelA);
                DumpTextureDimensions(writer, levelB, labelB);

                // Texture wrap mode summary
                writer.WriteLine();
                writer.WriteLine("Texture wrap mode summary:");
                DumpTextureWrapModes(writer, levelA, labelA);
                DumpTextureWrapModes(writer, levelB, labelB);

                // Final notes
                writer.WriteLine();
                writer.WriteLine("=== End of Report ===");
            }
        }

        private static void DumpTerrain(StreamWriter writer, Level level, string label)
        {
            writer.WriteLine($"--- {label} ---");
            if (level.terrainEngine?.fragments == null)
            {
                writer.WriteLine("No terrain fragments.");
                return;
            }
            writer.WriteLine($"Fragment count: {level.terrainEngine.fragments.Count}");
            for (int i = 0; i < Math.Min(10, level.terrainEngine.fragments.Count); i++)
            {
                var frag = level.terrainEngine.fragments[i];
                writer.WriteLine($"Fragment {i}: ID={frag.off1E}, ModelID={frag.modelID}, Vertices={frag.model?.vertexCount ?? 0}, Indices={frag.model?.indexBuffer?.Length ?? 0}, Textures={frag.model?.textureConfig?.Count ?? 0}");
            }
            writer.WriteLine();
        }

        private static IEnumerable<ushort> GetFragmentIds(Level level)
        {
            if (level.terrainEngine?.fragments == null)
                return Enumerable.Empty<ushort>();
            return level.terrainEngine.fragments.Select(f => f.off1E);
        }

        private static void DumpTextureUsage(StreamWriter writer, Level level, string label)
        {
            if (level.terrainEngine?.fragments == null || level.textures == null)
            {
                writer.WriteLine($"{label}: No terrain or textures.");
                return;
            }
            var usedTextureIds = new HashSet<int>();
            foreach (var frag in level.terrainEngine.fragments)
            {
                if (frag.model?.textureConfig != null)
                {
                    foreach (var texConfig in frag.model.textureConfig)
                        usedTextureIds.Add(texConfig.id);
                }
            }
            writer.WriteLine($"{label}: {usedTextureIds.Count} unique texture IDs used by terrain fragments.");
            writer.WriteLine($"IDs: {string.Join(", ", usedTextureIds.OrderBy(x => x).Take(20))}{(usedTextureIds.Count > 20 ? ", ..." : "")}");
        }

        private static void DumpMissingTextures(StreamWriter writer, Level level, string label)
        {
            if (level.terrainEngine?.fragments == null || level.textures == null)
            {
                writer.WriteLine($"{label}: No terrain or textures.");
                return;
            }
            var missing = new HashSet<int>();
            foreach (var frag in level.terrainEngine.fragments)
            {
                if (frag.model?.textureConfig != null)
                {
                    foreach (var texConfig in frag.model.textureConfig)
                    {
                        if (texConfig.id < 0 || texConfig.id >= level.textures.Count ||
                            level.textures[texConfig.id] == null ||
                            level.textures[texConfig.id].data == null ||
                            level.textures[texConfig.id].data.Length == 0)
                        {
                            missing.Add(texConfig.id);
                        }
                    }
                }
            }
            if (missing.Count > 0)
                writer.WriteLine($"{label}: {missing.Count} missing/invalid texture IDs: {string.Join(", ", missing.OrderBy(x => x))}");
            else
                writer.WriteLine($"{label}: All terrain texture references are valid.");
        }

        private static void DumpDegenerateFragments(StreamWriter writer, Level level, string label)
        {
            if (level.terrainEngine?.fragments == null)
            {
                writer.WriteLine($"{label}: No terrain fragments.");
                return;
            }
            int degenerateCount = 0;
            foreach (var frag in level.terrainEngine.fragments)
            {
                int verts = frag.model?.vertexCount ?? 0;
                int faces = frag.model?.faceCount ?? 0;
                if (verts < 3 || faces < 1)
                {
                    writer.WriteLine($"{label}: Fragment ID={frag.off1E} is degenerate (verts={verts}, faces={faces})");
                    degenerateCount++;
                }
            }
            if (degenerateCount == 0)
                writer.WriteLine($"{label}: No degenerate fragments found.");
        }

        private static void DumpBoundingBox(StreamWriter writer, Level level, string label)
        {
            if (level.terrainEngine?.fragments == null)
            {
                writer.WriteLine($"{label}: No terrain fragments.");
                return;
            }
            var allVerts = new List<OpenTK.Mathematics.Vector3>();
            foreach (var frag in level.terrainEngine.fragments)
            {
                if (frag.model is TerrainModel tm)
                {
                    for (int i = 0; i < tm.vertexCount; i++)
                    {
                        allVerts.Add(new OpenTK.Mathematics.Vector3(
                            tm.vertexBuffer[i * tm.vertexStride + 0],
                            tm.vertexBuffer[i * tm.vertexStride + 1],
                            tm.vertexBuffer[i * tm.vertexStride + 2]
                        ));
                    }
                }
            }
            if (allVerts.Count == 0)
            {
                writer.WriteLine($"{label}: No vertices found.");
                return;
            }
            var min = allVerts.Aggregate((a, b) => OpenTK.Mathematics.Vector3.ComponentMin(a, b));
            var max = allVerts.Aggregate((a, b) => OpenTK.Mathematics.Vector3.ComponentMax(a, b));
            writer.WriteLine($"{label}: Terrain bounding box: min={min}, max={max}, size={max - min}");
        }

        private static void DumpVertexIndexSummary(StreamWriter writer, Level level, string label)
        {
            if (level.terrainEngine?.fragments == null)
            {
                writer.WriteLine($"{label}: No terrain fragments.");
                return;
            }
            int totalVerts = 0, totalIndices = 0, totalFaces = 0;
            foreach (var frag in level.terrainEngine.fragments)
            {
                totalVerts += frag.model?.vertexCount ?? 0;
                totalIndices += frag.model?.indexBuffer?.Length ?? 0;
                totalFaces += frag.model?.faceCount ?? 0;
            }
            writer.WriteLine($"{label}: Total vertices={totalVerts}, indices={totalIndices}, faces={totalFaces}");
        }

        private static void DumpTextureDimensions(StreamWriter writer, Level level, string label)
        {
            if (level.textures == null)
            {
                writer.WriteLine($"{label}: No textures.");
                return;
            }
            var dims = level.textures
                .Where(t => t != null && t.width > 0 && t.height > 0)
                .GroupBy(t => (t.width, t.height))
                .Select(g => new { g.Key.width, g.Key.height, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();
            writer.WriteLine($"{label}: Texture dimensions used:");
            foreach (var d in dims)
            {
                writer.WriteLine($"  {d.width}x{d.height}: {d.Count} textures");
            }
        }

        private static void DumpTextureWrapModes(StreamWriter writer, Level level, string label)
        {
            if (level.terrainEngine?.fragments == null)
            {
                writer.WriteLine($"{label}: No terrain fragments.");
                return;
            }
            int repeatS = 0, clampS = 0, repeatT = 0, clampT = 0, unknownS = 0, unknownT = 0;
            foreach (var frag in level.terrainEngine.fragments)
            {
                if (frag.model?.textureConfig != null)
                {
                    foreach (var texConfig in frag.model.textureConfig)
                    {
                        var s = texConfig.wrapModeS;
                        var t = texConfig.wrapModeT;
                        if (s == TextureConfig.WrapMode.Repeat) repeatS++;
                        else if (s == TextureConfig.WrapMode.ClampEdge) clampS++;
                        else unknownS++;
                        if (t == TextureConfig.WrapMode.Repeat) repeatT++;
                        else if (t == TextureConfig.WrapMode.ClampEdge) clampT++;
                        else unknownT++;
                        writer.WriteLine($"Fragment {frag.off1E} TextureID {texConfig.id}: wrapModeS={s}, wrapModeT={t}, mode=0x{texConfig.mode:X8}");
                    }
                }
            }
            writer.WriteLine($"{label}: wrapModeS: Repeat={repeatS}, ClampEdge={clampS}, Unknown={unknownS}");
            writer.WriteLine($"{label}: wrapModeT: Repeat={repeatT}, ClampEdge={clampT}, Unknown={unknownT}");
        }

        /// <summary>
        /// Performs comprehensive analysis of a level's terrain and exports findings to files
        /// </summary>
        public static void AnalyzeLevel(Level level, string outputPath, string label)
        {
            Directory.CreateDirectory(outputPath);
            string baseFileName = Path.Combine(outputPath, $"{label}_analysis");

            // Export terrain fragment details
            using (var writer = new StreamWriter($"{baseFileName}_fragments.csv"))
            {
                writer.WriteLine("Index,FragmentID,ModelID,ChunkIndex,off1C,off1E,off20,off22,off24,off28,off2C,HasModel,VertexCount,TextureConfigCount,Position");
                
                if (level.terrainEngine?.fragments != null)
                {
                    for (int i = 0; i < level.terrainEngine.fragments.Count; i++)
                    {
                        var frag = level.terrainEngine.fragments[i];
                        var fragBytes = frag.ToByteArray();
                        
                        // Extract chunk index from byte array at offset 0x22
                        short chunkIndex = 0;
                        if (fragBytes.Length >= 0x24)
                        {
                            chunkIndex = BitConverter.ToInt16(fragBytes, 0x22);
                        }
                        
                        // Handle position which might be a Vector3 or individual floats
                        string positionStr;
                        try
                        {
                            // Try to access as Vector3
                            positionStr = $"{frag.position.X},{frag.position.Y},{frag.position.Z}";
                        }
                        catch (Exception)
                        {
                            // Fallback if position is not a Vector3
                            positionStr = $"{frag.position}";
                        }
                        
                        writer.WriteLine(
                            $"{i}," +
                            $"{frag.off1E}," +
                            $"{frag.modelID}," +
                            $"{chunkIndex}," +
                            $"0x{frag.off1C:X4}," +
                            $"0x{frag.off1E:X4}," +
                            $"0x{frag.off20:X4}," +
                            $"0x{chunkIndex:X4}," +
                            $"0x{frag.off24:X8}," +
                            $"0x{frag.off28:X8}," +
                            $"0x{frag.off2C:X8}," +
                            $"{(frag.model != null)}," +
                            $"{(frag.model?.vertexBuffer?.Length ?? 0) / 8}," +
                            $"{frag.model?.textureConfig?.Count ?? 0}," +
                            $"{positionStr}"
                        );
                    }
                }
            }

            // Export terrain model details
            using (var writer = new StreamWriter($"{baseFileName}_models.csv"))
            {
                writer.WriteLine("ModelID,Size,VertexCount,IndexCount,TextureConfigCount,TextureIDs");
                
                var uniqueModels = new HashSet<int>();
                if (level.terrainEngine?.fragments != null)
                {
                    foreach (var frag in level.terrainEngine.fragments)
                    {
                        if (frag.model != null && !uniqueModels.Contains(frag.model.id))
                        {
                            uniqueModels.Add(frag.model.id);
                            
                            // Gather texture IDs used by this model
                            string textureIds = "none";
                            if (frag.model.textureConfig != null && frag.model.textureConfig.Count > 0)
                            {
                                textureIds = string.Join(",", frag.model.textureConfig.Select(tc => tc.id));
                            }
                            
                            writer.WriteLine(
                                $"{frag.model.id}," +
                                $"{frag.model.size}," +
                                $"{frag.model.vertexBuffer.Length / 8}," +
                                $"{frag.model.indexBuffer.Length}," +
                                $"{frag.model.textureConfig?.Count ?? 0}," +
                                $"{textureIds}"
                            );
                        }
                    }
                }
            }

            // Export level variables
            using (var writer = new StreamWriter($"{baseFileName}_levelVars.txt"))
            {
                if (level.levelVariables != null)
                {
                    var lv = level.levelVariables;
                    writer.WriteLine($"Game: {level.game.num}");
                    writer.WriteLine($"ChunkCount: {lv.chunkCount}");
                    writer.WriteLine($"ByteSize: {lv.ByteSize}");
                    writer.WriteLine($"DeathPlaneZ: {lv.deathPlaneZ}");
                    writer.WriteLine($"FogStart: {lv.fogNearDistance}");
                    writer.WriteLine($"FogEnd: {lv.fogFarDistance}");
                    writer.WriteLine($"FogNearIntensity: {lv.fogNearIntensity}");
                    writer.WriteLine($"FogFarIntensity: {lv.fogFarIntensity}");
                    
                    // Handle shipPosition which might be a Vector3 or individual components
                    try
                    {
                        // Try to access as Vector3
                        writer.WriteLine($"ShipPosition: {lv.shipPosition.X}, {lv.shipPosition.Y}, {lv.shipPosition.Z}");
                    }
                    catch (Exception)
                    {
                        // Fallback if not a Vector3
                        writer.WriteLine($"ShipPosition: {lv.shipPosition}");
                    }
                    
                    writer.WriteLine($"ShipRotation: {lv.shipRotation}");
                    
                    writer.WriteLine($"ShipPathID: {lv.shipPathID}");
                    writer.WriteLine($"ShipCameraStartID: {lv.shipCameraStartID}");
                    writer.WriteLine($"ShipCameraEndID: {lv.shipCameraEndID}");
                    writer.WriteLine($"off58: 0x{lv.off58:X8}");
                    writer.WriteLine($"off68: 0x{lv.off68:X8}");
                    writer.WriteLine($"off6C: 0x{lv.off6C:X8}");
                    writer.WriteLine($"off78: 0x{lv.off78:X8}");
                    writer.WriteLine($"off7C: 0x{lv.off7C:X8}");
                }
            }

            // Export texture usage analysis
            using (var writer = new StreamWriter($"{baseFileName}_textures.csv"))
            {
                writer.WriteLine("TextureID,Width,Height,UsedByModels");
                
                if (level.textures != null)
                {
                    // Build a map of which textures are used by which models
                    var textureToModelMap = new Dictionary<int, List<int>>();
                    
                    if (level.terrainEngine?.fragments != null)
                    {
                        foreach (var frag in level.terrainEngine.fragments)
                        {
                            if (frag.model?.textureConfig != null)
                            {
                                foreach (var texConfig in frag.model.textureConfig)
                                {
                                    if (!textureToModelMap.ContainsKey(texConfig.id))
                                    {
                                        textureToModelMap[texConfig.id] = new List<int>();
                                    }
                                    
                                    if (!textureToModelMap[texConfig.id].Contains(frag.model.id))
                                    {
                                        textureToModelMap[texConfig.id].Add(frag.model.id);
                                    }
                                }
                            }
                        }
                    }
                    
                    for (int i = 0; i < level.textures.Count; i++)
                    {
                        var tex = level.textures[i];
                        var usedBy = textureToModelMap.ContainsKey(i) 
                            ? string.Join(",", textureToModelMap[i]) 
                            : "none";
                        
                        writer.WriteLine($"{i},{tex.width},{tex.height},{usedBy}");
                    }
                }
            }

            // Export chunk data analysis (if any)
            using (var writer = new StreamWriter($"{baseFileName}_chunks.txt"))
            {
                writer.WriteLine($"LevelVariables.chunkCount: {level.levelVariables?.chunkCount ?? 0}");
                writer.WriteLine($"terrainChunks.Count: {level.terrainChunks?.Count ?? 0}");
                writer.WriteLine($"collisionChunks.Count: {level.collisionChunks?.Count ?? 0}");
                writer.WriteLine($"collBytesChunks.Count: {level.collBytesChunks?.Count ?? 0}");
                
                if (level.terrainChunks != null)
                {
                    for (int i = 0; i < level.terrainChunks.Count; i++)
                    {
                        writer.WriteLine($"\nChunk {i}:");
                        writer.WriteLine($"  levelNumber: {level.terrainChunks[i].levelNumber}");
                        writer.WriteLine($"  fragmentCount: {level.terrainChunks[i].fragments?.Count ?? 0}");
                    }
                }
            }

            Console.WriteLine($"✅ Level analysis exported to {outputPath}");
        }

        /// <summary>
        /// Analyze all Going Commando levels in a directory
        /// </summary>
        /// <param name="rc2DataPath">Path to the RC2 ps3data directory</param>
        /// <param name="outputPath">Where to save the analysis files</param>
        /// <param name="maxLevels">Maximum number of levels to analyze (0 for all)</param>
        public static void AnalyzeAllRC2Levels(string rc2DataPath, string outputPath, int maxLevels = 0)
        {
            Console.WriteLine($"Analyzing RC2 levels in {rc2DataPath}...");
            
            Directory.CreateDirectory(outputPath);
            
            // Summary file for quick reference
            using (var summaryWriter = new StreamWriter(Path.Combine(outputPath, "rc2_levels_summary.csv")))
            {
                summaryWriter.WriteLine("LevelNumber,LevelName,GameType,FragmentCount,TerrainChunks,CollisionChunks,ChunkCountInVars,SequentialFragmentIDs,UniqueModels,TextureCount");
                
                // Look for level directories (level0 through level26)
                List<string> levelPaths = new List<string>();
                for (int i = 0; i <= 26; i++)
                {
                    string levelDir = Path.Combine(rc2DataPath, $"level{i}");
                    if (Directory.Exists(levelDir))
                    {
                        levelPaths.Add(levelDir);
                    }
                }
                
                // Apply limit if specified
                if (maxLevels > 0 && maxLevels < levelPaths.Count)
                {
                    levelPaths = levelPaths.Take(maxLevels).ToList();
                }
                
                Console.WriteLine($"Found {levelPaths.Count} level directories to analyze");
                
                // Now process each level
                foreach (string levelDir in levelPaths)
                {
                    string levelName = Path.GetFileName(levelDir);
                    string enginePath = Path.Combine(levelDir, "engine.ps3");
                    
                    if (!File.Exists(enginePath))
                    {
                        Console.WriteLine($"⚠️ No engine.ps3 found in {levelDir} - skipping");
                        continue;
                    }
                    
                    try
                    {
                        Console.WriteLine($"Loading {levelName}...");
                        Level level = new Level(enginePath);
                        
                        // Create level-specific output directory
                        string levelOutputDir = Path.Combine(outputPath, levelName);
                        Directory.CreateDirectory(levelOutputDir);
                        
                        // Analyze this level
                        AnalyzeLevel(level, levelOutputDir, levelName);
                        
                        // Add to summary
                        int uniqueModels = level.terrainEngine?.fragments?.Select(f => f.model?.id ?? -1)
                            .Where(id => id >= 0).Distinct().Count() ?? 0;
                        
                        bool sequentialIds = CheckSequentialFragmentIds(level, out _);
                        
                        summaryWriter.WriteLine(
                            $"{level.terrainEngine?.levelNumber ?? -1}," +
                            $"{levelName}," +
                            $"{level.game.num}," +
                            $"{level.terrainEngine?.fragments?.Count ?? 0}," +
                            $"{level.terrainChunks?.Count ?? 0}," +
                            $"{level.collisionChunks?.Count ?? 0}," +
                            $"{level.levelVariables?.chunkCount ?? 0}," +
                            $"{sequentialIds}," +
                            $"{uniqueModels}," +
                            $"{level.textures?.Count ?? 0}"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error analyzing {levelName}: {ex.Message}");
                    }
                }
            }
            
            Console.WriteLine($"✅ Analysis of all RC2 levels complete. Results saved to {outputPath}");
        }
        
        /// <summary>
        /// Perform a comparative analysis across all RC2 levels to identify patterns and best practices
        /// </summary>
        public static void GenerateRC2PatternsReport(string rc2DataPath, string outputPath)
        {
            Console.WriteLine("Generating RC2 terrain patterns report...");
            
            Directory.CreateDirectory(outputPath);
            string reportPath = Path.Combine(outputPath, "rc2_terrain_patterns_report.txt");
            
            // Load all available RC2 levels first
            List<Tuple<string, Level>> levels = new List<Tuple<string, Level>>();
            
            for (int i = 0; i <= 26; i++)
            {
                string levelDir = Path.Combine(rc2DataPath, $"level{i}");
                string enginePath = Path.Combine(levelDir, "engine.ps3");
                
                if (Directory.Exists(levelDir) && File.Exists(enginePath))
                {
                    try
                    {
                        Level level = new Level(enginePath);
                        levels.Add(new Tuple<string, Level>($"level{i}", level));
                        Console.WriteLine($"Loaded {levelDir}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Could not load {levelDir}: {ex.Message}");
                    }
                }
            }
            
            using (var writer = new StreamWriter(reportPath))
            {
                writer.WriteLine("Ratchet & Clank: Going Commando (RC2) Terrain Structure Analysis");
                writer.WriteLine("=================================================================");
                writer.WriteLine($"Date: {DateTime.Now}");
                writer.WriteLine($"Total levels analyzed: {levels.Count}");
                writer.WriteLine();
                
                // 1. Fragment ID Sequencing Pattern
                writer.WriteLine("1. FRAGMENT ID SEQUENCING PATTERN");
                writer.WriteLine("------------------------------");
                int sequentialCount = 0;
                foreach (var (name, level) in levels)
                {
                    bool isSequential = CheckSequentialFragmentIds(level, out _);
                    if (isSequential) sequentialCount++;
                    writer.WriteLine($"{name}: {(isSequential ? "Sequential" : "Non-sequential")}");
                }
                writer.WriteLine($"Summary: {sequentialCount} out of {levels.Count} levels have sequential fragment IDs");
                writer.WriteLine($"Pattern: {(sequentialCount == levels.Count ? "ALL RC2 levels use sequential fragment IDs" : "Sequential fragment IDs are common but not universal")}");
                writer.WriteLine();
                
                // 2. Chunk Count Pattern
                writer.WriteLine("2. CHUNK USAGE PATTERN");
                writer.WriteLine("----------------------");
                var chunkCounts = levels.Select(l => l.Item2.terrainChunks?.Count ?? 0).GroupBy(c => c)
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Key} chunks: {g.Count()} levels")
                    .ToList();
                
                foreach (var countGroup in chunkCounts)
                {
                    writer.WriteLine(countGroup);
                }
                
                writer.WriteLine("\nChunk count in LevelVariables vs actual terrain chunks:");
                foreach (var (name, level) in levels)
                {
                    int varsCount = level.levelVariables?.chunkCount ?? 0;
                    int actualCount = level.terrainChunks?.Count ?? 0;
                    writer.WriteLine($"{name}: LevelVars.chunkCount={varsCount}, actual chunks={actualCount}");
                }
                writer.WriteLine();
                
                // 3. Fragment to Chunk Index Distribution
                writer.WriteLine("3. FRAGMENT TO CHUNK INDEX MAPPING");
                writer.WriteLine("----------------------------------");
                writer.WriteLine("Analysis of how off22 (chunk index) values are distributed in fragments:");
                
                foreach (var (name, level) in levels.Where(l => l.Item2.terrainEngine?.fragments?.Count > 0))
                {
                    writer.WriteLine($"\n{name}:");
                    AnalyzeChunkIndices(level, name, writer);
                }
                
                // 4. Common Values for Important Fields
                writer.WriteLine("\n4. COMMON FRAGMENT FIELD VALUES");
                writer.WriteLine("------------------------------");
                writer.WriteLine("Analysis of common values for off1C, off20, off24, off28, off2C:");
                
                var off1CValues = new Dictionary<ushort, int>();
                var off20Values = new Dictionary<ushort, int>();
                var off24Values = new Dictionary<uint, int>();
                var off28Values = new Dictionary<uint, int>();
                var off2CValues = new Dictionary<uint, int>();
                
                foreach (var (_, level) in levels)
                {
                    if (level.terrainEngine?.fragments == null) continue;
                    
                    foreach (var frag in level.terrainEngine.fragments)
                    {
                        // off1C
                        if (!off1CValues.ContainsKey(frag.off1C))
                            off1CValues[frag.off1C] = 0;
                        off1CValues[frag.off1C]++;
                        
                        // off20
                        if (!off20Values.ContainsKey(frag.off20))
                            off20Values[frag.off20] = 0;
                        off20Values[frag.off20]++;
                        
                        // off24
                        if (!off24Values.ContainsKey(frag.off24))
                            off24Values[frag.off24] = 0;
                        off24Values[frag.off24]++;
                        
                        // off28
                        if (!off28Values.ContainsKey(frag.off28))
                            off28Values[frag.off28] = 0;
                        off28Values[frag.off28]++;
                        
                        // off2C
                        if (!off2CValues.ContainsKey(frag.off2C))
                            off2CValues[frag.off2C] = 0;
                        off2CValues[frag.off2C]++;
                    }
                }
                
                writer.WriteLine("\nMost common values for off1C:");
                foreach (var kvp in off1CValues.OrderByDescending(kv => kv.Value).Take(5))
                {
                    writer.WriteLine($"  0x{kvp.Key:X4}: {kvp.Value} occurrences ({(double)kvp.Value / levels.Sum(l => l.Item2.terrainEngine?.fragments?.Count ?? 0) * 100:F1}%)");
                }
                
                writer.WriteLine("\nMost common values for off20:");
                foreach (var kvp in off20Values.OrderByDescending(kv => kv.Value).Take(5))
                {
                    writer.WriteLine($"  0x{kvp.Key:X4}: {kvp.Value} occurrences ({(double)kvp.Value / levels.Sum(l => l.Item2.terrainEngine?.fragments?.Count ?? 0) * 100:F1}%)");
                }
                
                writer.WriteLine("\nMost common values for off24:");
                foreach (var kvp in off24Values.OrderByDescending(kv => kv.Value).Take(5))
                {
                    writer.WriteLine($"  0x{kvp.Key:X8}: {kvp.Value} occurrences ({(double)kvp.Value / levels.Sum(l => l.Item2.terrainEngine?.fragments?.Count ?? 0) * 100:F1}%)");
                }
                
                // 5. Recommendations for Oltanis port
                writer.WriteLine("\n5. RECOMMENDATIONS FOR RC1 TO RC2 TERRAIN PORTING");
                writer.WriteLine("----------------------------------------------");
                writer.WriteLine("Based on the analysis of RC2 levels, here are recommendations for porting RC1 terrain:");
                writer.WriteLine("1. Ensure fragment IDs (off1E) are sequential (0, 1, 2, 3...)");
                writer.WriteLine("2. Set all fragments' chunk index (off22) to 0 for single-chunk levels");
                writer.WriteLine("3. Use standard values for other offsets:");
                writer.WriteLine($"   - off1C: 0x{off1CValues.OrderByDescending(kv => kv.Value).First().Key:X4}");
                writer.WriteLine($"   - off20: 0x{off20Values.OrderByDescending(kv => kv.Value).First().Key:X4}");
                writer.WriteLine($"   - off24: 0x{off24Values.OrderByDescending(kv => kv.Value).First().Key:X8}");
                writer.WriteLine($"   - off28: 0x{off28Values.OrderByDescending(kv => kv.Value).First().Key:X8}");
                writer.WriteLine($"   - off2C: 0x{off2CValues.OrderByDescending(kv => kv.Value).First().Key:X8}");
                writer.WriteLine("4. Set LevelVariables.chunkCount to match the number of actual chunks (1 for single-chunk levels)");
                writer.WriteLine("5. Ensure proper texture index remapping to avoid visual glitches");
            }
            
            Console.WriteLine($"✅ RC2 terrain patterns report generated at {reportPath}");
        }

        private static bool CheckSequentialFragmentIds(Level level, out string breakDetails)
        {
            var fragments = level.terrainEngine?.fragments;
            var sb = new System.Text.StringBuilder();
            bool isSequential = true;
            
            if (fragments != null && fragments.Count > 0)
            {
                for (int i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i].off1E != i)
                    {
                        isSequential = false;
                        sb.AppendLine($"Fragment at index {i} has ID {fragments[i].off1E}");
                    }
                }
            }
            
            breakDetails = sb.ToString();
            return isSequential;
        }

        private static void AnalyzeChunkIndices(Level level, string label, System.IO.StreamWriter writer)
        {
            if (level.terrainEngine?.fragments == null || level.terrainEngine.fragments.Count == 0)
            {
                writer.WriteLine($"{label}: No fragments to analyze");
                return;
            }
            
            var chunkIndices = new Dictionary<short, int>();
            
            foreach (var frag in level.terrainEngine.fragments)
            {
                var fragBytes = frag.ToByteArray();
                short chunkIndex = 0;
                
                if (fragBytes.Length >= 0x24)
                {
                    chunkIndex = BitConverter.ToInt16(fragBytes, 0x22);
                }
                
                if (!chunkIndices.ContainsKey(chunkIndex))
                {
                    chunkIndices[chunkIndex] = 0;
                }
                
                chunkIndices[chunkIndex]++;
            }
            
            writer.WriteLine($"{label} chunk indices distribution:");
            foreach (var kvp in chunkIndices.OrderBy(k => k.Key))
            {
                writer.WriteLine($"  Chunk {kvp.Key}: {kvp.Value} fragments");
            }
        }
    }
}
