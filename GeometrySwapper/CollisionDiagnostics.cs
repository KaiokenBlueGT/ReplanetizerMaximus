using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using LibReplanetizer;
using LibReplanetizer.Models;
using LibReplanetizer.LevelObjects;
using OpenTK.Mathematics;

namespace GeometrySwapper
{
    public static class CollisionDiagnostics
    {
        private static LibReplanetizer.GameType PromptGameType(string fileLabel)
        {
            Console.WriteLine($"Select game type for {fileLabel}:");
            Console.WriteLine("1. Ratchet & Clank (RaC1)");
            Console.WriteLine("2. Going Commando (RaC2)");
            Console.WriteLine("3. Up Your Arsenal (RaC3/UYA)");
            Console.WriteLine("4. Deadlocked (DL)");
            Console.Write("> ");
            string input = Console.ReadLine()?.Trim() ?? "3";
            return input switch
            {
                "1" => LibReplanetizer.GameType.RaC1,
                "2" => LibReplanetizer.GameType.RaC2,
                "3" => LibReplanetizer.GameType.RaC3,
                "4" => LibReplanetizer.GameType.DL,
                _ => LibReplanetizer.GameType.RaC3
            };
        }

        private static Collision? TryLoadCollision(string rccPath, LibReplanetizer.GameType gameType)
        {
            // Print file size and first 16 bytes for diagnostics
            try
            {
                // Print first 64 bytes and interpret as 32-bit big-endian integers
                try
                {
                    var fileInfo = new FileInfo(rccPath);
                    Console.WriteLine($"> File size: {fileInfo.Length} bytes");
                    using (var fs = File.OpenRead(rccPath))
                    {
                        byte[] first64 = new byte[Math.Min(64, fs.Length)];
                        fs.Read(first64, 0, first64.Length);
                        Console.WriteLine($"> First {first64.Length} bytes: {BitConverter.ToString(first64)}");
                        for (int i = 0; i < first64.Length; i += 4)
                        {
                            if (i + 4 <= first64.Length)
                            {
                                int val = (first64[i] << 24) | (first64[i + 1] << 16) | (first64[i + 2] << 8) | (first64[i + 3]);
                                Console.WriteLine($"> Header[{i:D2}-{i+3:D2}]: 0x{val:X8} ({val})");
                            }
                        }
                    }
                }
                catch { /* ignore */ }

                // Try chunk-based loading first
                try
                {
                    var chunkParser = new LibReplanetizer.Parsers.ChunkParser(rccPath, gameType);
                    var collision = chunkParser.GetCollisionModel();
                    chunkParser.Dispose();
                    if (collision != null && collision.vertexBuffer.Length > 0 && collision.indBuff.Length > 0)
                        return collision;
                    Console.WriteLine($"No collision data found in chunk for {rccPath}, trying raw block...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ ChunkParser failed: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }

                // Try raw collision block loading (skip header if present)
                try
                {
                    using (var fs = File.OpenRead(rccPath))
                    {
                        long fileLen = fs.Length;
                        if (fileLen > 12) // Heuristic: skip 8-byte header if present
                        {
                            byte[] header = new byte[8];
                            fs.Read(header, 0, 8);
                            long collisionStart = fs.Position;
                            try
                            {
                                var collision = new LibReplanetizer.Models.Collision(fs, (int)collisionStart);
                                if (collision.vertexBuffer.Length > 0 && collision.indBuff.Length > 0)
                                    return collision;
                            }
                            catch (Exception ex2)
                            {
                                Console.WriteLine($"❌ Raw collision block (skipping header) failed: {ex2.Message}");
                                Console.WriteLine(ex2.StackTrace);
                            }
                            fs.Position = 0; // Try from start as fallback
                        }
                        try
                        {
                            var collision = new LibReplanetizer.Models.Collision(fs, 0);
                            if (collision.vertexBuffer.Length > 0 && collision.indBuff.Length > 0)
                                return collision;
                            Console.WriteLine($"Raw collision block loaded, but no data found in {rccPath}.");
                        }
                        catch (Exception ex3)
                        {
                            Console.WriteLine($"❌ Raw collision block (from start) failed: {ex3.Message}");
                            Console.WriteLine(ex3.StackTrace);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Raw collision block failed: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }

                // Try all nonzero pointers in the header as possible collision blocks
                try
                {
                    using (var fs = File.OpenRead(rccPath))
                    {
                        byte[] header = new byte[64];
                        fs.Read(header, 0, header.Length);
                        List<int> pointers = new List<int>();
                        for (int i = 0; i < header.Length; i += 4)
                        {
                            int ptr = (header[i] << 24) | (header[i + 1] << 16) | (header[i + 2] << 8) | (header[i + 3]);
                            if (ptr > 0 && ptr < fs.Length)
                                pointers.Add(ptr);
                        }
                        foreach (var pointer in pointers)
                        {
                            try
                            {
                                fs.Position = 0; // Reset stream
                                var collision = new LibReplanetizer.Models.Collision(fs, pointer);
                                if (collision.vertexBuffer.Length > 0 && collision.indBuff.Length > 0)
                                {
                                    Console.WriteLine($"Loaded collision from pointer 0x{pointer:X8} ({pointer})");
                                    return collision;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Pointer 0x{pointer:X8} failed: {ex.Message}");
                            }
                        }
                        Console.WriteLine("No valid collision block found in header pointers.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Header pointer scan failed: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error accessing file: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            return null;
        }

        public static void AnalyzeRccFile(string rccPath, string outputDir, string label)
        {
            if (!File.Exists(rccPath))
            {
                Console.WriteLine($"❌ RCC file not found: {rccPath}");
                return;
            }
            Directory.CreateDirectory(outputDir);
            string reportPath = Path.Combine(outputDir, $"collision_analysis_{label}.txt");
            using (var writer = new StreamWriter(reportPath))
            {
                writer.WriteLine($"=== Collision Analysis Report: {label} ===");
                writer.WriteLine($"File: {rccPath}");
                writer.WriteLine($"Generated: {DateTime.Now}");
                writer.WriteLine();
                var gameType = PromptGameType(label);
                Collision collision = TryLoadCollision(rccPath, gameType);
                if (collision == null)
                {
                    writer.WriteLine("Failed to load collision data.");
                    return;
                }
                writer.WriteLine($"Vertex count: {collision.vertexBuffer.Length / 4}");
                writer.WriteLine($"Index count: {collision.indBuff.Length}");
                writer.WriteLine($"Face count (triangles): {collision.indBuff.Length / 3}");
                // Bounding box
                if (collision.vertexBuffer.Length >= 4)
                {
                    var verts = new List<Vector3>();
                    for (int i = 0; i < collision.vertexBuffer.Length; i += 4)
                    {
                        verts.Add(new Vector3(
                            collision.vertexBuffer[i + 0],
                            collision.vertexBuffer[i + 1],
                            collision.vertexBuffer[i + 2]
                        ));
                    }
                    var min = verts.Aggregate((a, b) => Vector3.ComponentMin(a, b));
                    var max = verts.Aggregate((a, b) => Vector3.ComponentMax(a, b));
                    writer.WriteLine($"Bounding box: min={min}, max={max}, size={max - min}");
                }
                else
                {
                    writer.WriteLine("No vertices found.");
                }
                // Sample faces
                writer.WriteLine("\nSample faces (first 5):");
                for (int i = 0; i < Math.Min(5, collision.indBuff.Length / 3); i++)
                {
                    writer.WriteLine($"  Face {i}: {collision.indBuff[i * 3 + 0]}, {collision.indBuff[i * 3 + 1]}, {collision.indBuff[i * 3 + 2]}");
                }
                writer.WriteLine("\n=== End of Report ===");
            }
            Console.WriteLine($"✅ Collision analysis exported to {reportPath}");
        }

        public static void CompareRccFiles(string rccAPath, string rccBPath, string outputDir, string labelA, string labelB)
        {
            Directory.CreateDirectory(outputDir);
            string reportPath = Path.Combine(outputDir, $"collision_comparison_{labelA}_vs_{labelB}.txt");
            using (var writer = new StreamWriter(reportPath))
            {
                writer.WriteLine($"=== Collision Comparison Report: {labelA} vs {labelB} ===");
                writer.WriteLine($"Generated: {DateTime.Now}");
                writer.WriteLine();
                var gameTypeA = PromptGameType(labelA);
                var gameTypeB = PromptGameType(labelB);
                Collision collA = TryLoadCollision(rccAPath, gameTypeA);
                Collision collB = TryLoadCollision(rccBPath, gameTypeB);
                if (collA == null || collB == null)
                {
                    writer.WriteLine("Failed to load one or both collision files.");
                    return;
                }
                writer.WriteLine($"{labelA}: Vertices={collA.vertexBuffer.Length / 4}, Indices={collA.indBuff.Length}, Faces={collA.indBuff.Length / 3}");
                writer.WriteLine($"{labelB}: Vertices={collB.vertexBuffer.Length / 4}, Indices={collB.indBuff.Length}, Faces={collB.indBuff.Length / 3}");
                // Bounding boxes
                writer.WriteLine();
                writer.WriteLine($"{labelA} bounding box: {GetBoundingBox(collA)}");
                writer.WriteLine($"{labelB} bounding box: {GetBoundingBox(collB)}");
                // Face index stats
                writer.WriteLine();
                writer.WriteLine($"{labelA} face indices (first 5): {GetFaceSample(collA)}");
                writer.WriteLine($"{labelB} face indices (first 5): {GetFaceSample(collB)}");
                // Differences
                writer.WriteLine();
                writer.WriteLine("=== Differences & Summary ===");
                writer.WriteLine($"Vertex count diff: {collA.vertexBuffer.Length / 4} vs {collB.vertexBuffer.Length / 4}");
                writer.WriteLine($"Index count diff: {collA.indBuff.Length} vs {collB.indBuff.Length}");
                writer.WriteLine($"Face count diff: {collA.indBuff.Length / 3} vs {collB.indBuff.Length / 3}");
                writer.WriteLine("=== End of Report ===");
            }
            Console.WriteLine($"✅ Collision comparison exported to {reportPath}");
        }

        /// <summary>
        /// Convert an RCC collision file to a Wavefront OBJ for editing.
        /// Prompts for game type to correctly interpret the data.
        /// </summary>
        public static void ConvertRccToObj(string rccPath, string objPath)
        {
            if (!File.Exists(rccPath))
            {
                Console.WriteLine($"❌ RCC file not found: {rccPath}");
                return;
            }

            var gameType = PromptGameType(Path.GetFileName(rccPath));
            Collision collision = TryLoadCollision(rccPath, gameType);
            if (collision == null)
            {
                Console.WriteLine("❌ Failed to load collision data.");
                return;
            }

            var exporter = new LibReplanetizer.WavefrontExporter();
            exporter.ExportCollision(objPath, collision);
            Console.WriteLine($"✅ RCC converted to OBJ: {objPath}");
        }

        /// <summary>
        /// Convert a Wavefront OBJ into an RCC collision file. The OBJ is parsed
        /// via <see cref="ObjTerrainImporter"/> and collision data is generated
        /// before being serialized with the standalone RCC serializer.
        /// </summary>
        public static void ConvertObjToRcc(string objPath, string rccPath)
        {
            if (!File.Exists(objPath))
            {
                Console.WriteLine($"❌ OBJ file not found: {objPath}");
                return;
            }

            var level = new Level();
            var importer = new ObjTerrainImporter();
            List<TerrainFragment> fragments;
            try
            {
                fragments = importer.ImportObjAsTerrain(objPath, level.textures);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to import OBJ: {ex.Message}");
                return;
            }

            ObjTerrainImporter.GenerateAndAssignCollision(level, fragments);
            var collision = level.collisionEngine as Collision;
            if (collision == null)
            {
                Console.WriteLine("❌ Collision generation failed.");
                return;
            }

            var bytes = DataFunctions.SerializeCollisionToRccStandalone(collision);
            File.WriteAllBytes(rccPath, bytes);
            Console.WriteLine($"✅ OBJ converted to RCC: {rccPath}");

            // Validation: load the written file and compare counts
            var loaded = TryLoadCollision(rccPath, GameType.RaC3);
            if (loaded != null)
            {
                int srcVerts = collision.vertexBuffer.Length / 4;
                int srcInds = collision.indBuff.Length;
                int dstVerts = loaded.vertexBuffer.Length / 4;
                int dstInds = loaded.indBuff.Length;
                Console.WriteLine($"Round-trip vertices: {srcVerts} vs {dstVerts}");
                Console.WriteLine($"Round-trip indices: {srcInds} vs {dstInds}");
                if (srcVerts == dstVerts && srcInds == dstInds)
                    Console.WriteLine("✅ Round-trip counts match");
                else
                    Console.WriteLine("⚠️ Round-trip counts differ");
            }
            else
            {
                Console.WriteLine("❌ Failed to reload generated RCC");
            }
        }

        private static string GetBoundingBox(Collision coll)
        {
            if (coll.vertexBuffer.Length < 4) return "No vertices";
            var verts = new List<Vector3>();
            for (int i = 0; i < coll.vertexBuffer.Length; i += 4)
            {
                verts.Add(new Vector3(
                    coll.vertexBuffer[i + 0],
                    coll.vertexBuffer[i + 1],
                    coll.vertexBuffer[i + 2]
                ));
            }
            var min = verts.Aggregate((a, b) => Vector3.ComponentMin(a, b));
            var max = verts.Aggregate((a, b) => Vector3.ComponentMax(a, b));
            return $"min={min}, max={max}, size={max - min}";
        }

        private static string GetFaceSample(Collision coll)
        {
            var faces = new List<string>();
            for (int i = 0; i < Math.Min(5, coll.indBuff.Length / 3); i++)
            {
                faces.Add($"{coll.indBuff[i * 3 + 0]},{coll.indBuff[i * 3 + 1]},{coll.indBuff[i * 3 + 2]}");
            }
            return string.Join(" | ", faces);
        }
    }
}
