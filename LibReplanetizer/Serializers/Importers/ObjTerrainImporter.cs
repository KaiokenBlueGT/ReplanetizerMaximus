// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using CommunityToolkit.HighPerformance;
using static LibReplanetizer.Models.Model;

namespace LibReplanetizer
{
    /// <summary>
    /// Imports OBJ files and converts them to Replanetizer terrain fragments
    /// </summary>
    public class ObjTerrainImporter
    {
        private static readonly CultureInfo en_US = CultureInfo.CreateSpecificCulture("en-US");
        private static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();

        // Allow consumers (e.g., UI) to display importer warnings.
        public static event Action<string>? CollisionWarning;

        // Helper: Transform OBJ vertex to engine space (swap Y/Z if needed)
        private Vector3 TransformObjToEngineSpace(Vector3 v)
        {
            // Rotate 90 degrees right around Y axis, then swap Y/Z and negate Z for engine
            // Previous (left): return new Vector3(-v.Z, v.Y, v.X);
            // Now (right):
            float rotatedX = v.Z;
            float rotatedY = v.Y;
            float rotatedZ = -v.X;
            return new Vector3(rotatedX, rotatedZ, -rotatedY);
        }

        // Helper: Compute AABB from a list of vertices
        private (Vector3 min, Vector3 max) ComputeAabb(List<Vector3> verts)
        {
            if (verts.Count == 0) return (Vector3.Zero, Vector3.Zero);
            Vector3 min = verts[0], max = verts[0];
            foreach (var v in verts)
            {
                min = Vector3.ComponentMin(min, v);
                max = Vector3.ComponentMax(max, v);
            }
            return (min, max);
        }

        // Helper: Score a fragment by areaXZ and tri count
        private float ScoreFragment(TerrainFragment frag)
        {
            var m = frag.model as TerrainModel;
            if (m == null) return 0;
            var bounds = ComputeAabb(GetFragmentVertices(m));
            float areaXZ = (bounds.max.X - bounds.min.X) * (bounds.max.Z - bounds.min.Z);
            int tris = m.indexBuffer.Length / 3;
            return areaXZ * 0.7f + tris * 0.3f;
        }

        // Helper: Get all vertices from a TerrainModel
        private List<Vector3> GetFragmentVertices(TerrainModel m)
        {
            var verts = new List<Vector3>();
            for (int i = 0; i < m.vertexCount; i++)
            {
                verts.Add(new Vector3(
                    m.vertexBuffer[i * 8 + 0],
                    m.vertexBuffer[i * 8 + 1],
                    m.vertexBuffer[i * 8 + 2]
                ));
            }
            return verts;
        }

        // Fragment ID counter for unique sequential IDs
        private int nextFragId = 0;

        // Maximums for fragment safety
        private const int MAX_TEXTURES_PER_FRAGMENT = 8;
        private const int MAX_VERTS = 2000;
        private const int MAX_TRIS = 3000;
        private const int MIN_TRIS = 8;

        // Fallback texture ID for missing/invalid textures
        private int fallbackTextureId = -1;

        /// <summary>
        /// Imports an OBJ file and converts it to terrain fragments
        /// </summary>
        public List<TerrainFragment> ImportObjAsTerrain(string objFilePath, List<Texture> levelTextures)
        {
            LOGGER.Info($"Starting OBJ terrain import from: {objFilePath}");

            var terrainFragments = new List<TerrainFragment>();
            nextFragId = 0; // Reset for each import

            try
            {
                var objData = ParseObjFile(objFilePath);
                // ---- Gemini: Load textures/materials ----
                var textureDict = LoadAndRegisterTextures(objData.Materials, levelTextures);
                LOGGER.Info($"Loaded {textureDict.Count} materials with textures.");
                // 1. Axis conversion and collect all engine-space vertices
                var engineVerts = objData.Vertices.Select(TransformObjToEngineSpace).ToList();
                // 2. Compute global bounds and scale
                Vector3 min = engineVerts[0], max = engineVerts[0];
                foreach (var v in engineVerts)
                {
                    min = Vector3.ComponentMin(min, v);
                    max = Vector3.ComponentMax(max, v);
                }
                float dx = max.X - min.X, dz = max.Z - min.Z;
                float targetExtent = 1200f; // Increased for larger maps
                float globalScale = MathF.Min(1f, targetExtent / MathF.Max(dx, dz));
                // 3. Apply global scale to all engine-space vertices
                for (int i = 0; i < engineVerts.Count; i++)
                    engineVerts[i] *= globalScale;
                // 4. Bucket faces by (gx, gz) grid
                const float CELL = 48f; // Finer grid for more fragments
                var cellBuckets = new Dictionary<(int gx, int gz), List<(ObjFace face, int objIdx)>>
                ();
                for (int objIdx = 0; objIdx < objData.Objects.Count; objIdx++)
                {
                    var obj = objData.Objects[objIdx];
                    foreach (var face in obj.Faces)
                    {
                        var v = engineVerts[face.Vertices[0].VertexIndex];
                        int gx = (int) MathF.Floor(v.X / CELL);
                        int gz = (int) MathF.Floor(v.Z / CELL);
                        var key = (gx, gz);
                        if (!cellBuckets.ContainsKey(key)) cellBuckets[key] = new List<(ObjFace, int)>();
                        cellBuckets[key].Add((face, objIdx));
                    }
                }
                LOGGER.Info($"Grid partitioning: {cellBuckets.Count} cells created with CELL={CELL}");
                // ---- Gemini: Replace main cell loop with recursive subdivision ----
                foreach (var kvp in cellBuckets)
                {
                    ProcessCell(kvp.Value, objData, engineVerts, textureDict, terrainFragments);
                }
                // --- Fragment count cap ---
                if (terrainFragments.Count > 400)
                {
                    terrainFragments = terrainFragments
                        .OrderByDescending(ScoreFragment)
                        .Take(400)
                        .ToList();
                    LOGGER.Warn($"Fragment count capped to 400. Dropped least useful fragments.");
                }
                LOGGER.Info($"Successfully imported {terrainFragments.Count} terrain fragments");
            }
            catch (Exception ex)
            {
                LOGGER.Error(ex, $"Failed to import OBJ file: {objFilePath}");
                throw;
            }

            // Assign unique sequential fragment IDs
            for (int i = 0; i < terrainFragments.Count; i++)
            {
                terrainFragments[i].off1E = (ushort)i;
            }

            return terrainFragments;
        }

        // ---- Gemini: Recursive cell processing for adaptive fragment generation ----
        private void ProcessCell(List<(ObjFace face, int objIdx)> cellFaces, ObjData objData, List<Vector3> engineVerts, Dictionary<string, int> textureDict, List<TerrainFragment> finalFragments, int depth = 0)
        {
            // --- PROTECTION: Limit recursion depth ---
            const int MAX_CELL_DEPTH = 8;
            if (depth > MAX_CELL_DEPTH)
            {
                LOGGER.Warn($"ProcessCell: Max recursion depth {MAX_CELL_DEPTH} reached, skipping cell with {cellFaces.Count} faces");
                return;
            }
            // --- PROTECTION: Limit total fragments ---
            if (finalFragments.Count > 1000)
            {
                LOGGER.Warn($"ProcessCell: Fragment count exceeded 1000, skipping cell");
                return;
            }
            // --- 1. Build Vertex Data for this Cell ---
            var cellVerts = new List<Vector3>();
            var cellNormals = new List<Vector3>();
            var cellTexCoords = new List<Vector2>();
            var cellColors = new List<Vector3>();
            var vertexLookup = new Dictionary<string, ushort>();

            // Group faces by material to handle multiple textures per fragment
            var facesByMaterial = cellFaces.GroupBy(f => objData.Objects[f.objIdx].Faces.First(face => face == f.face).Material);

            // --- NEW: Split by material set if too many textures ---
            var materialGroups = facesByMaterial.ToList();
            if (materialGroups.Count > MAX_TEXTURES_PER_FRAGMENT)
            {
                // Split into batches of MAX_TEXTURES_PER_FRAGMENT
                for (int i = 0; i < materialGroups.Count; i += MAX_TEXTURES_PER_FRAGMENT)
                {
                    var batch = materialGroups.Skip(i).Take(MAX_TEXTURES_PER_FRAGMENT);
                    ProcessMaterialBatch(batch, objData, engineVerts, textureDict, finalFragments, depth + 1);
                }
                return;
            }

            // Otherwise, process as usual
            ProcessMaterialBatch(materialGroups, objData, engineVerts, textureDict, finalFragments, depth);
        }

        // Helper to process a batch of material groups as a single fragment
        private void ProcessMaterialBatch(IEnumerable<IGrouping<string, (ObjFace face, int objIdx)>> materialGroups, ObjData objData, List<Vector3> engineVerts, Dictionary<string, int> textureDict, List<TerrainFragment> finalFragments, int depth)
        {
            var cellVerts = new List<Vector3>();
            var cellNormals = new List<Vector3>();
            var cellTexCoords = new List<Vector2>();
            var cellColors = new List<Vector3>();
            var vertexLookup = new Dictionary<string, ushort>();
            var allCellIndices = new List<ushort>();
            var textureConfigs = new List<TextureConfig>();

            foreach (var materialGroup in materialGroups)
            {
                var materialName = materialGroup.Key;
                var materialIndices = new List<ushort>();
                ushort nextIndex = (ushort)cellVerts.Count;
                int textureId = fallbackTextureId;
                if (textureDict != null) textureDict.TryGetValue(materialName, out textureId);
                if (textureId < 0 || textureId >= 65536) textureId = fallbackTextureId;
                foreach (var (face, objIdx) in materialGroup)
                {
                    if (face.Vertices.Count != 3) continue;
                    for (int i = 0; i < 3; i++)
                    {
                        var objVertex = face.Vertices[i];
                        string vertexKey = $"{objVertex.VertexIndex}_{objVertex.TextureIndex}_{objVertex.NormalIndex}";
                        if (!vertexLookup.TryGetValue(vertexKey, out ushort index))
                        {
                            index = nextIndex++;
                            vertexLookup[vertexKey] = index;
                            cellVerts.Add(engineVerts[objVertex.VertexIndex]);
                            cellNormals.Add(objData.Normals.Count > objVertex.NormalIndex && objVertex.NormalIndex >= 0 ? objData.Normals[objVertex.NormalIndex] : Vector3.UnitY);
                            cellTexCoords.Add(objData.TextureCoords.Count > objVertex.TextureIndex && objVertex.TextureIndex >= 0 ? objData.TextureCoords[objVertex.TextureIndex] : Vector2.Zero);
                            cellColors.Add(objData.VertexColors.Count > objVertex.VertexIndex && objVertex.VertexIndex >= 0 ? objData.VertexColors[objVertex.VertexIndex] : new Vector3(1, 1, 1));
                        }
                        materialIndices.Add(index);
                    }
                }
                if (materialIndices.Count > 0)
                {
                    // Set wrap modes: Repeat for S, Repeat for T (terrain always repeat)
                    var wrapS = TextureConfig.WrapMode.Repeat;
                    var wrapT = TextureConfig.WrapMode.Repeat;
                    int mode = 0;
                    // Mode calculation based on TextureConfig logic
                    // S: Repeat (bit 0 set), ClampEdge (bit 1 set)
                    // T: Repeat (bit 2 set), ClampEdge (bit 3 set)
                    if (wrapS == TextureConfig.WrapMode.Repeat && wrapT == TextureConfig.WrapMode.Repeat)
                        mode = 0x05000000;
                    else if (wrapS == TextureConfig.WrapMode.ClampEdge && wrapT == TextureConfig.WrapMode.ClampEdge)
                        mode = 0x0F000000;
                    else if (wrapS == TextureConfig.WrapMode.Repeat && wrapT == TextureConfig.WrapMode.ClampEdge)
                        mode = 0x0D000000;
                    else if (wrapS == TextureConfig.WrapMode.ClampEdge && wrapT == TextureConfig.WrapMode.Repeat)
                        mode = 0x07000000;

                    textureConfigs.Add(new TextureConfig
                    {
                        id = textureId,
                        start = allCellIndices.Count,
                        size = materialIndices.Count,
                        mode = mode,
                        wrapModeS = wrapS,
                        wrapModeT = wrapT
                    });
                    allCellIndices.AddRange(materialIndices);
                }
            }

            int triCount = allCellIndices.Count / 3;
            if (triCount == 0) return;

            // Defensive checks
            if (textureConfigs.Count > MAX_TEXTURES_PER_FRAGMENT)
            {
                LOGGER.Warn($"Fragment skipped: too many textures ({textureConfigs.Count})");
                return;
            }
            if (cellVerts.Count > MAX_VERTS || triCount > MAX_TRIS)
            {
                if (depth < 4)
                {
                    LOGGER.Info($"Fragment too large (tris={triCount}, verts={cellVerts.Count}). Subdividing further.");
                    var aabb = ComputeAabb(cellVerts);
                    var center = (aabb.min + aabb.max) * 0.5f;
                    var subCells = new List<List<(ObjFace face, int objIdx)>> { new List<(ObjFace, int)>(), new List<(ObjFace, int)>(), new List<(ObjFace, int)>(), new List<(ObjFace, int)>() };
                    foreach (var materialGroup in materialGroups)
                    {
                        foreach (var (face, objIdx) in materialGroup)
                        {
                            var v0 = engineVerts[face.Vertices[0].VertexIndex];
                            var centroid = (v0 + engineVerts[face.Vertices[1].VertexIndex] + engineVerts[face.Vertices[2].VertexIndex]) / 3f;
                            int subCellIndex = (centroid.X > center.X ? 1 : 0) + (centroid.Z > center.Z ? 2 : 0);
                            subCells[subCellIndex].Add((face, objIdx));
                        }
                    }
                    foreach (var subCellFaces in subCells)
                    {
                        if (subCellFaces.Count > 0)
                        {
                            ProcessCell(subCellFaces, objData, engineVerts, textureDict, finalFragments, depth + 1);
                        }
                    }
                    return;
                }
                LOGGER.Warn($"Fragment skipped: too many verts/tris after max subdivision");
                return;
            }
            if (triCount < MIN_TRIS)
            {
                return;
            }
            // Defensive: skip if index count is not a multiple of 3
            if (allCellIndices.Count < 3 || allCellIndices.Count % 3 != 0)
            {
                LOGGER.Warn($"Skip draw: bad indexCount ({allCellIndices.Count})");
                return;
            }

            var bounds = ComputeAabb(cellVerts);
            var terrainModel = CreateTerrainModelFromData(cellVerts, cellNormals, cellTexCoords, cellColors, allCellIndices, new ObjObject { Name = $"Frag_{finalFragments.Count}" }, textureConfigs, bounds);
            if (terrainModel == null) return;
            terrainModel.primitiveType = PrimitiveType.Triangles; // Force triangle lists

            var terrainFragment = CreateTerrainFragmentFromModel(terrainModel, $"Frag_{finalFragments.Count}");
            terrainFragment.off1E = (ushort)nextFragId++; // Assign unique fragment ID
            finalFragments.Add(terrainFragment);
        }

        private class ObjData
        {
            public List<Vector3> Vertices { get; set; } = new List<Vector3>();
            public List<Vector3> Normals { get; set; } = new List<Vector3>();
            public List<Vector2> TextureCoords { get; set; } = new List<Vector2>();
            public List<Vector3> VertexColors { get; set; } = new List<Vector3>();
            public List<ObjObject> Objects { get; set; } = new List<ObjObject>();
            public List<ObjMaterial> Materials { get; set; } = new List<ObjMaterial>();
        }

        private class ObjObject
        {
            public string Name { get; set; } = "";
            public List<ObjFace> Faces { get; set; } = new List<ObjFace>();
            public string CurrentMaterial { get; set; } = "";
        }

        private class ObjFace
        {
            public List<ObjVertex> Vertices { get; set; } = new List<ObjVertex>();
            public string Material { get; set; } = "";
        }

        private class ObjVertex
        {
            public int VertexIndex { get; set; }
            public int TextureIndex { get; set; }
            public int NormalIndex { get; set; }
        }

        private class ObjMaterial
        {
            public string Name { get; set; } = "";
            public int TextureId { get; set; } = 0;
            public string TexturePath { get; set; } = ""; // NEW: store texture path
        }

        private ObjData ParseObjFile(string objFilePath)
        {
            var objData = new ObjData();
            var currentObject = new ObjObject { Name = "Default" };
            string currentMaterial = "";

            string[] lines = File.ReadAllLines(objFilePath);

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;

                string[] parts = trimmedLine.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                string command = parts[0].ToLower();

                switch (command)
                {
                    case "v": // Vertex
                        ParseVertex(parts, objData);
                        break;

                    case "vn": // Vertex normal
                        ParseNormal(parts, objData);
                        break;

                    case "vt": // Texture coordinate
                        ParseTextureCoord(parts, objData);
                        break;

                    case "f": // Face
                        ParseFace(parts, currentObject, currentMaterial);
                        break;

                    case "o": // Object
                    case "g": // Group (treat as object)
                        if (currentObject.Faces.Count > 0)
                        {
                            objData.Objects.Add(currentObject);
                        }
                        currentObject = new ObjObject { Name = parts.Length > 1 ? parts[1] : "Object" };
                        break;

                    case "usemtl": // Use material
                        currentMaterial = parts.Length > 1 ? parts[1] : "";
                        currentObject.CurrentMaterial = currentMaterial;
                        break;

                    case "mtllib": // Material library
                        // Try to parse MTL file if it exists
                        if (parts.Length > 1)
                        {
                            string mtlPath = Path.Combine(Path.GetDirectoryName(objFilePath) ?? "", parts[1]);
                            if (File.Exists(mtlPath))
                            {
                                ParseMtlFile(mtlPath, objData);
                            }
                        }
                        break;
                }
            }

            // Add the last object if it has faces
            if (currentObject.Faces.Count > 0)
            {
                objData.Objects.Add(currentObject);
            }

            return objData;
        }

        private void ParseVertex(string[] parts, ObjData objData)
        {
            if (parts.Length >= 4)
            {
                float x = float.Parse(parts[1], en_US);
                float y = float.Parse(parts[2], en_US);
                float z = float.Parse(parts[3], en_US);
                objData.Vertices.Add(new Vector3(x, y, z));

                // Check for vertex colors (additional r g b values)
                if (parts.Length >= 7)
                {
                    float r = float.Parse(parts[4], en_US);
                    float g = float.Parse(parts[5], en_US);
                    float b = float.Parse(parts[6], en_US);
                    objData.VertexColors.Add(new Vector3(r, g, b));
                }
                else
                {
                    objData.VertexColors.Add(new Vector3(1.0f, 1.0f, 1.0f)); // Default white
                }
            }
        }

        private void ParseNormal(string[] parts, ObjData objData)
        {
            if (parts.Length >= 4)
            {
                float x = float.Parse(parts[1], en_US);
                float y = float.Parse(parts[2], en_US);
                float z = float.Parse(parts[3], en_US);
                objData.Normals.Add(new Vector3(x, y, z));
            }
        }

        private void ParseTextureCoord(string[] parts, ObjData objData)
        {
            if (parts.Length >= 3)
            {
                float u = float.Parse(parts[1], en_US);
                float v = float.Parse(parts[2], en_US);
                // Flip V for Replanetizer (try both if unsure)
                //objData.TextureCoords.Add(new Vector2(u, 1.0f - v)); //IF THIS FAILS, UNCOMMENT THE SECOND ONE
                objData.TextureCoords.Add(new Vector2(u, v));
            }
        }

        private void ParseFace(string[] parts, ObjObject currentObject, string currentMaterial)
        {
            if (parts.Length >= 4) // At least 3 vertices for a triangle
            {
                var face = new ObjFace { Material = currentMaterial };

                for (int i = 1; i < parts.Length; i++)
                {
                    var vertex = ParseFaceVertex(parts[i]);
                    if (vertex != null)
                    {
                        face.Vertices.Add(vertex);
                    }
                }

                // Triangulate if necessary (quads -> triangles)
                if (face.Vertices.Count == 3)
                {
                    currentObject.Faces.Add(face);
                }
                else if (face.Vertices.Count == 4)
                {
                    // Split quad into two triangles
                    var triangle1 = new ObjFace { Material = currentMaterial };
                    triangle1.Vertices.Add(face.Vertices[0]);
                    triangle1.Vertices.Add(face.Vertices[1]);
                    triangle1.Vertices.Add(face.Vertices[2]);

                    var triangle2 = new ObjFace { Material = currentMaterial };
                    triangle2.Vertices.Add(face.Vertices[0]);
                    triangle2.Vertices.Add(face.Vertices[2]);
                    triangle2.Vertices.Add(face.Vertices[3]);

                    currentObject.Faces.Add(triangle1);
                    currentObject.Faces.Add(triangle2);
                }
                else if (face.Vertices.Count > 4)
                {
                    // Fan triangulation for n-gons
                    for (int i = 1; i < face.Vertices.Count - 1; i++)
                    {
                        var triangle = new ObjFace { Material = currentMaterial };
                        triangle.Vertices.Add(face.Vertices[0]);
                        triangle.Vertices.Add(face.Vertices[i]);
                        triangle.Vertices.Add(face.Vertices[i + 1]);
                        currentObject.Faces.Add(triangle);
                    }
                }
            }
        }

        private ObjVertex? ParseFaceVertex(string vertexString)
        {
            string[] indices = vertexString.Split('/');
            if (indices.Length >= 1 && int.TryParse(indices[0], out int vertexIndex))
            {
                var vertex = new ObjVertex { VertexIndex = vertexIndex - 1 }; // Convert to 0-based

                if (indices.Length >= 2 && !string.IsNullOrEmpty(indices[1]) && int.TryParse(indices[1], out int textureIndex))
                {
                    vertex.TextureIndex = textureIndex - 1;
                }
                else
                {
                    vertex.TextureIndex = -1;
                }

                if (indices.Length >= 3 && !string.IsNullOrEmpty(indices[2]) && int.TryParse(indices[2], out int normalIndex))
                {
                    vertex.NormalIndex = normalIndex - 1;
                }
                else
                {
                    vertex.NormalIndex = -1;
                }

                return vertex;
            }

            return null;
        }

        private void ParseMtlFile(string mtlPath, ObjData objData)
        {
            try
            {
                string[] lines = File.ReadAllLines(mtlPath);
                ObjMaterial? currentMaterial = null;

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    string[] parts = trimmedLine.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;

                    string command = parts[0].ToLower();

                    switch (command)
                    {
                        case "newmtl":
                            if (parts.Length > 1)
                            {
                                currentMaterial = new ObjMaterial { Name = parts[1] };
                                objData.Materials.Add(currentMaterial);
                            }
                            break;

                        case "map_kd":
                            if (currentMaterial != null && parts.Length > 1)
                            {
                                string textureFile = parts[1];
                                currentMaterial.TexturePath = Path.Combine(Path.GetDirectoryName(mtlPath) ?? "", textureFile);
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                LOGGER.Warn(ex, $"Failed to parse MTL file: {mtlPath}");
            }
        }

        private TerrainFragment? CreateTerrainFragmentFromObjData(string name, ObjData objData)
        {
            // Create a single object from all the data
            var combinedObject = new ObjObject { Name = name };
            
            foreach (var obj in objData.Objects)
            {
                combinedObject.Faces.AddRange(obj.Faces);
            }

            // If no objects, create faces from all vertices
            if (objData.Objects.Count == 0 && objData.Vertices.Count >= 3)
            {
                // Simple triangulation of all vertices
                for (int i = 0; i < objData.Vertices.Count - 2; i += 3)
                {
                    var face = new ObjFace();
                    face.Vertices.Add(new ObjVertex { VertexIndex = i, TextureIndex = Math.Min(i, objData.TextureCoords.Count - 1), NormalIndex = Math.Min(i, objData.Normals.Count - 1) });
                    face.Vertices.Add(new ObjVertex { VertexIndex = i + 1, TextureIndex = Math.Min(i + 1, objData.TextureCoords.Count - 1), NormalIndex = Math.Min(i + 1, objData.Normals.Count - 1) });
                    face.Vertices.Add(new ObjVertex { VertexIndex = i + 2, TextureIndex = Math.Min(i + 2, objData.TextureCoords.Count - 1), NormalIndex = Math.Min(i + 2, objData.Normals.Count - 1) });
                    combinedObject.Faces.Add(face);
                }
            }

            return CreateTerrainFragmentFromObject(combinedObject, objData);
        }

        private TerrainFragment? CreateTerrainFragmentFromObject(ObjObject objObject, ObjData objData)
        {
            if (objObject.Faces.Count == 0)
                return null;

            // Build vertex and index lists
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var texCoords = new List<Vector2>();
            var colors = new List<Vector3>();
            var indices = new List<ushort>();
            var vertexLookup = new Dictionary<string, ushort>();

            ushort nextIndex = 0;

            foreach (var face in objObject.Faces)
            {
                if (face.Vertices.Count != 3)
                    continue; // Skip non-triangular faces

                for (int i = 0; i < 3; i++)
                {
                    var objVertex = face.Vertices[i];
                    string vertexKey = $"{objVertex.VertexIndex}_{objVertex.TextureIndex}_{objVertex.NormalIndex}";

                    if (!vertexLookup.TryGetValue(vertexKey, out ushort index))
                    {
                        // Add new vertex
                        index = nextIndex++;
                        vertexLookup[vertexKey] = index;

                        // Position
                        if (objVertex.VertexIndex >= 0 && objVertex.VertexIndex < objData.Vertices.Count)
                        {
                            vertices.Add(objData.Vertices[objVertex.VertexIndex]);
                            // Color
                            if (objVertex.VertexIndex < objData.VertexColors.Count)
                            {
                                colors.Add(objData.VertexColors[objVertex.VertexIndex]);
                            }
                            else
                            {
                                colors.Add(new Vector3(1.0f, 1.0f, 1.0f));
                            }
                        }
                        else
                        {
                            vertices.Add(Vector3.Zero);
                            colors.Add(new Vector3(1.0f, 1.0f, 1.0f));
                        }

                        // Normal
                        if (objVertex.NormalIndex >= 0 && objVertex.NormalIndex < objData.Normals.Count)
                        {
                            normals.Add(objData.Normals[objVertex.NormalIndex]);
                        }
                        else
                        {
                            normals.Add(Vector3.UnitY); // Default up normal
                        }

                        // Texture coordinates
                        if (objVertex.TextureIndex >= 0 && objVertex.TextureIndex < objData.TextureCoords.Count)
                        {
                            texCoords.Add(objData.TextureCoords[objVertex.TextureIndex]);
                        }
                        else
                        {
                            texCoords.Add(Vector2.Zero);
                        }
                    }

                    indices.Add(index);
                }
            }

            // --- Fragment budget and degenerate cull checks ---
            if (vertices.Count < 3)
            {
                LOGGER.Warn($"Skipping fragment '{objObject.Name}': too few verts ({vertices.Count})");
                return null;
            }
            if (indices.Count / 3 > 1500 || vertices.Count > 1000)
            {
                LOGGER.Warn($"Skipping fragment '{objObject.Name}': too many tris ({indices.Count / 3}) or verts ({vertices.Count})");
                return null;
            }

            // --- AABB sanity check (max span, not Y extent) ---
            var bounds = CalculateInputBounds(vertices);
            var size = bounds.max - bounds.min;
            float span = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (span < 0.25f)
            {
                LOGGER.Warn($"Skipping '{objObject.Name}': very small AABB {bounds.min}–{bounds.max}");
                return null;
            }

            if (vertices.Count == 0 || indices.Count == 0)
                return null;

            // Create terrain model
            var terrainModel = CreateTerrainModelFromData(vertices, normals, texCoords, colors, indices, objObject, new List<TextureConfig>(), bounds);
            if (terrainModel == null)
                return null;

            // Create terrain fragment
            var terrainFragment = CreateTerrainFragmentFromModel(terrainModel, objObject.Name);
            terrainFragment.position = CalculateBoundingCenter(terrainModel); // Or use a custom offset if needed
            terrainFragment.UpdateTransformMatrix();

            return terrainFragment;
        }

        // Update CreateTerrainModelFromData to accept textureConfigs
        private TerrainModel CreateTerrainModelFromData(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> texCoords,
            List<Vector3> colors,
            List<ushort> indices,
            ObjObject objObject,
            List<TextureConfig> textureConfigs,
            (Vector3 min, Vector3 max)? boundsOverride = null)
        {
            // 🔧 FIX: Use FormatterServices to create TerrainModel without calling constructor
            var terrainModel = (TerrainModel) System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TerrainModel));

            // Initialize all required fields manually
            terrainModel.lights = new List<int>();

            // 🔧 CRITICAL FIX: Set vertexStride property (not field!) BEFORE setting vertexBuffer to prevent division by zero
            var vertexStrideProperty = typeof(Model).GetProperty("vertexStride");
            vertexStrideProperty?.SetValue(terrainModel, 8); // Terrain models use 8-component vertices (x,y,z,nx,ny,nz,u,v)

            // 🆕 ADD: Auto-scale the imported geometry to reasonable size
            var bounds = boundsOverride ?? CalculateInputBounds(vertices);
            float scale = CalculateAutoScale(bounds);
            float unclampedScale = scale;
            scale = Math.Clamp(scale, 0.5f, 5.0f);
            if (scale != unclampedScale)
            {
                LOGGER.Warn($"Clamped scale for '{objObject.Name}' to {scale}");
            }
            LOGGER.Info($"Input bounds: min={bounds.min}, max={bounds.max}, size={bounds.max - bounds.min}");
            LOGGER.Info($"Applying auto-scale factor: {scale}");

            // Set up vertex buffer (format: x, y, z, nx, ny, nz, u, v per vertex)
            int vertexCount = vertices.Count;
            terrainModel.vertexBuffer = new float[vertexCount * 8];
            terrainModel.weights = new uint[vertexCount];
            terrainModel.ids = new uint[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                int offset = i * 8;

                // 🔧 FIX: Apply scaling to position
                var scaledVertex = vertices[i] * scale;
                terrainModel.vertexBuffer[offset + 0] = scaledVertex.X;
                terrainModel.vertexBuffer[offset + 1] = scaledVertex.Y;
                terrainModel.vertexBuffer[offset + 2] = scaledVertex.Z;

                // Normal (don't scale normals, just normalize them)
                var normal = normals[i].Normalized();
                terrainModel.vertexBuffer[offset + 3] = normal.X;
                terrainModel.vertexBuffer[offset + 4] = normal.Y;
                terrainModel.vertexBuffer[offset + 5] = normal.Z;

                // UV
                terrainModel.vertexBuffer[offset + 6] = texCoords[i].X;
                terrainModel.vertexBuffer[offset + 7] = texCoords[i].Y;

                // Default weights and IDs (not used for terrain)
                terrainModel.weights[i] = 0;
                terrainModel.ids[i] = 0;
            }

            // Set up index buffer
            terrainModel.indexBuffer = indices.ToArray();

            // Defensive: force primitive type to triangles
            terrainModel.primitiveType = PrimitiveType.Triangles;

            // Set up RGBA colors
            terrainModel.rgbas = new byte[vertices.Count * 4];
            for (int i = 0; i < vertices.Count; i++)
            {
                var color = colors[i];
                terrainModel.rgbas[i * 4 + 0] = (byte)(color.X * 255);
                terrainModel.rgbas[i * 4 + 1] = (byte)(color.Y * 255);
                terrainModel.rgbas[i * 4 + 2] = (byte)(color.Z * 255);
                terrainModel.rgbas[i * 4 + 3] = 255;
            }

            // ---- Gemini: Assign textureConfigs directly ----
            terrainModel.textureConfig = textureConfigs;

            // Set model properties using reflection
            var sizeProperty = typeof(Model).GetProperty("size");
            sizeProperty?.SetValue(terrainModel, 1.0f);

            var idProperty = typeof(Model).GetProperty("id");
            idProperty?.SetValue(terrainModel, (short) new Random().Next(1000, 9999));

            // Set the private _faceCount field using reflection
            var faceCountField = typeof(TerrainModel).GetField("_faceCount",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            faceCountField?.SetValue(terrainModel, indices.Count / 3);

            // Initialize lights list for terrain model
            for (int i = 0; i < vertexCount; i++)
            {
                terrainModel.lights.Add(0); // Default lighting value
            }

            LOGGER.Info($"Created TerrainModel: {vertexCount} vertices, {indices.Count / 3} faces, scale={scale}");

            return terrainModel;
        }

        private TerrainFragment CreateTerrainFragmentFromModel(TerrainModel terrainModel, string name)
        {
            // 🔧 FIX: Use FormatterServices for TerrainFragment too - same issue as TerrainModel
            var terrainFragment = (TerrainFragment)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TerrainFragment));

            // Set the model using reflection since it might be protected
            var modelProperty = typeof(ModelObject).GetProperty("model");
            modelProperty?.SetValue(terrainFragment, terrainModel);

            var modelIdProperty = typeof(ModelObject).GetProperty("modelID");
            modelIdProperty?.SetValue(terrainFragment, terrainModel.id);

            var modelMatrixField = typeof(LevelObject).GetField("modelMatrix", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            modelMatrixField?.SetValue(terrainFragment, Matrix4.Identity);

            // Set basic properties
            terrainFragment.cullingCenter = CalculateBoundingCenter(terrainModel);
            terrainFragment.cullingSize = CalculateBoundingRadius(terrainModel, terrainFragment.cullingCenter);

            // Set default values for terrain fragment fields
            terrainFragment.off1C = 0xFFFF;
            terrainFragment.off1E = 0;
            terrainFragment.off20 = 0xFF00;
            terrainFragment.off24 = 0;
            terrainFragment.off28 = 0;
            terrainFragment.off2C = 0;

            LOGGER.Info($"Created terrain fragment '{name}' with {terrainModel.vertexCount} vertices and {terrainModel.faceCount} faces");

            return terrainFragment;
        }

        private Vector3 CalculateBoundingCenter(TerrainModel model)
        {
            if (model.vertexCount == 0)
                return Vector3.Zero;

            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);

            for (int i = 0; i < model.vertexCount; i++)
            {
                Vector3 vertex = new Vector3(
                    model.vertexBuffer[i * 8 + 0],
                    model.vertexBuffer[i * 8 + 1],
                    model.vertexBuffer[i * 8 + 2]
                );

                min = Vector3.ComponentMin(min, vertex);
                max = Vector3.ComponentMax(max, vertex);
            }

            return (min + max) * 0.5f;
        }

        private float CalculateBoundingRadius(TerrainModel model, Vector3 center)
        {
            float maxDistanceSquared = 0;

            for (int i = 0; i < model.vertexCount; i++)
            {
                Vector3 vertex = new Vector3(
                    model.vertexBuffer[i * 8 + 0],
                    model.vertexBuffer[i * 8 + 1],
                    model.vertexBuffer[i * 8 + 2]
                );

                float distanceSquared = (vertex - center).LengthSquared;
                if (distanceSquared > maxDistanceSquared)
                {
                    maxDistanceSquared = distanceSquared;
                }
            }

            return (float)Math.Sqrt(maxDistanceSquared);
        }

        /// <summary>
        /// Calculate the bounding box of input vertices
        /// </summary>
        private (Vector3 min, Vector3 max) CalculateInputBounds(List<Vector3> vertices)
        {
            if (vertices.Count == 0)
                return (Vector3.Zero, Vector3.Zero);

            Vector3 min = vertices[0];
            Vector3 max = vertices[0];

            foreach (var vertex in vertices)
            {
                min = Vector3.ComponentMin(min, vertex);
                max = Vector3.ComponentMax(max, vertex);
            }

            return (min, max);
        }

        /// <summary>
        /// Calculate an appropriate scale factor for imported geometry
        /// </summary>
        private float CalculateAutoScale((Vector3 min, Vector3 max) bounds)
        {
            var size = bounds.max - bounds.min;
            float maxDimension = Math.Max(Math.Max(size.X, size.Y), size.Z);

            // If the model is very large (like exported from Blender in meters), scale it down
            if (maxDimension > 1000.0f)
            {
                return 100.0f / maxDimension; // Scale to ~100 units max
            }
            // If the model is very small (like exported in millimeters), scale it up  
            else if (maxDimension < 1.0f)
            {
                return 100.0f / maxDimension; // Scale to ~100 units max
            }
            // If the model is reasonably sized, don't scale it
            else
            {
                return 1.0f;
            }
        }

        // In your importer class, keep a reference to the global texture list
        private List<string> globalTextureList = new List<string>();

        private void EnsureFallbackTexture(List<Texture> levelTextures)
        {
            if (fallbackTextureId >= 0 && fallbackTextureId < levelTextures.Count && levelTextures[fallbackTextureId] != null)
                return;
            var whitePixel = new byte[] { 255, 255, 255, 255 }; // RGBA
            fallbackTextureId = levelTextures.Count;
            var fallback = new Texture(fallbackTextureId, 1, 1, whitePixel);
            levelTextures.Add(fallback);
        }

        private Dictionary<string, int> LoadAndRegisterTextures(List<ObjMaterial> materials, List<Texture> levelTextures)
        {
            EnsureFallbackTexture(levelTextures);
            var textureDict = new Dictionary<string, int>();
            int maxTextureId = fallbackTextureId; // Keep track of the highest ID we use

            foreach (var mat in materials)
            {
                int textureId = fallbackTextureId;
                if (!string.IsNullOrEmpty(mat.TexturePath) && File.Exists(mat.TexturePath))
                {
                    LOGGER.Debug($"Attempting to load texture: '{mat.TexturePath}' for material '{mat.Name}'");
                    // Check if texture is already loaded
                    var existingTexture = levelTextures.FirstOrDefault(tex => tex != null && tex.renderedImage != null && tex.renderedImage.ToString() == mat.TexturePath);
                    if (existingTexture != null)
                    {
                        textureId = existingTexture.id;
                        LOGGER.Debug($"Texture already loaded at index {textureId}: '{mat.TexturePath}'");
                    }
                    else // Load new texture
                    {
                        textureId = levelTextures.Count;
                        try
                        {
                            using var image = Image.Load<Bgra32>(mat.TexturePath);
                            // PREVENTATIVE FIX: Resize oversized textures to avoid VRAM issues
                            if (image.Width > 256 || image.Height > 256)
                            {
                                LOGGER.Warn($"Texture '{mat.TexturePath}' ({image.Width}x{image.Height}) is larger than 256x256. Resizing...");
                                image.Mutate(x => x.Resize(new ResizeOptions
                                {
                                    Size = new Size(256, 256),
                                    Mode = ResizeMode.Max
                                }));
                                LOGGER.Info($"Resized texture to {image.Width}x{image.Height}");
                            }

                            short mipCount;
                            byte[] data = GenerateMipChain(image, out mipCount);

                            var newTexture = new Texture(textureId, (short)image.Width, (short)image.Height, data);
                            newTexture.mipMapCount = mipCount;
                            newTexture.renderedImage = image; // Store for duplicate checking
                            levelTextures.Add(newTexture);

                            LOGGER.Info($"Texture '{mat.TexturePath}' registered as DXT5 (ID {textureId}, {image.Width}x{image.Height})");
                        }
                        catch (Exception ex)
                        {
                            LOGGER.Error(ex, $"Failed to load or process texture '{mat.TexturePath}'. Using fallback.");
                            textureId = fallbackTextureId;
                        }
                    }

                    mat.TextureId = textureId;
                    textureDict[mat.Name] = textureId;
                    if (textureId > maxTextureId) maxTextureId = textureId;
                }
                else
                {
                    mat.TextureId = fallbackTextureId;
                    textureDict[mat.Name] = fallbackTextureId;
                    LOGGER.Warn($"Texture file not found for material '{mat.Name}': {mat.TexturePath}. Using fallback.");
                }
            }

            // --- CRITICAL FIX: Fill any gaps in the texture list ---
            // The game engine requires a contiguous list of valid textures up to the max ID.
            LOGGER.Info($"Validating texture list up to max ID: {maxTextureId}");
            for (int i = 0; i <= maxTextureId; i++)
            {
                bool needsFallback = false;
                if (i >= levelTextures.Count)
                {
                    // The list is too short, needs padding
                    needsFallback = true; 
                }
                else
                {
                    // The list has an entry, but it might be null or invalid
                    var tex = levelTextures[i];
                    if (tex == null || tex.data == null || tex.data.Length == 0 || tex.width <= 0 || tex.height <= 0)
                    {
                        needsFallback = true;
                    }
                }

                if (needsFallback)
                {
                    if (i < levelTextures.Count && levelTextures[i] != null) {
                         LOGGER.Warn($"Found invalid texture at ID {i}. Replacing with fallback.");
                    } else {
                         LOGGER.Warn($"Found gap in texture list at ID {i}. Filling with fallback.");
                    }

                    // Create a valid 1x1 magenta fallback texture for debugging
                    var fallback = new Texture(i, 1, 1, new byte[] { 255, 0, 255, 255 });
                    fallback.mipMapCount = 1;

                    if (i < levelTextures.Count)
                        levelTextures[i] = fallback; // Replace invalid entry
                    else
                        levelTextures.Add(fallback);   // Add to end of list
                }
            }

            return textureDict;
        }

        private static byte[] GenerateMipChain(Image<Bgra32> baseImage, out short mipCount)
        {
            var encoder = new BcEncoder();
            encoder.OutputOptions.Quality = CompressionQuality.Balanced;
            encoder.OutputOptions.Format = CompressionFormat.Bc3; // DXT5

            var mipmaps = new List<byte[]>();
            int width = baseImage.Width;
            int height = baseImage.Height;
            mipCount = 0;

            using (Image<Bgra32> current = baseImage.Clone())
            {
                while (true)
                {
                    var dxtBytes = encoder.EncodeToRawBytes(ConvertToRgba32Memory2D(current));
                    mipmaps.Add(dxtBytes[0]);
                    mipCount++;
                    LOGGER.Debug($"Mip level {mipCount - 1}: {current.Width}x{current.Height}, DXT5 bytes={dxtBytes.Length}");

                    if (current.Width <= 1 && current.Height <= 1)
                    {
                        break; // Exit after processing the 1x1 mip level
                    }

                    int nextWidth = Math.Max(1, current.Width / 2);
                    int nextHeight = Math.Max(1, current.Height / 2);
                    current.Mutate(x => x.Resize(nextWidth, nextHeight));
                }
            }

            // --- THIS IS THE CRITICAL PART ---
            // Combine the list of byte arrays into a single byte array.
            int totalSize = mipmaps.Sum(m => m.Length);
            byte[] packedData = new byte[totalSize];
            int offset = 0;
            foreach (var mip in mipmaps)
            {
                Buffer.BlockCopy(mip, 0, packedData, offset, mip.Length);
                offset += mip.Length;
            }

            LOGGER.Debug($"Packed DXT5 mip chain: totalBytes={totalSize}, mipLevels={mipCount}");

            // Ensure we are returning the single, combined array.
            return packedData;
        }

        private static ReadOnlyMemory2D<ColorRgba32> ConvertToRgba32Memory2D(Image<Bgra32> image)
        {
            var width = image.Width;
            var height = image.Height;
            var rgbaPixels = new ColorRgba32[width * height];

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var pixel = rowSpan[x];
                        rgbaPixels[y * width + x] = new ColorRgba32(pixel.R, pixel.G, pixel.B, pixel.A);
                    }
                }
            });

            return new ReadOnlyMemory2D<ColorRgba32>(rgbaPixels, height, width);
        }

        public static void LogTerrainFragments(string label, IEnumerable<TerrainFragment> fragments)
        {
            Console.WriteLine($"==== Terrain Fragment Diagnostics: {label} ====");

            int idx = 0;
            foreach (var frag in fragments)
            {
                var m = frag.model as TerrainModel;
                Console.WriteLine($"Fragment #{idx++}:");
                Console.WriteLine($"  modelID: {frag.modelID}");
                Console.WriteLine($"  cullingCenter: {frag.cullingCenter}");
                Console.WriteLine($"  cullingSize: {frag.cullingSize}");
                Console.WriteLine($"  off1C: {frag.off1C}, off1E: {frag.off1E}, off20: {frag.off20}, off24: {frag.off24}, off28: {frag.off28}, off2C: {frag.off2C}");
                if (m != null)
                {
                    Console.WriteLine($"  TerrainModel.id: {m.id}");
                    Console.WriteLine($"  TerrainModel.size: {m.size}");
                    Console.WriteLine($"  TerrainModel.vertexStride: {m.vertexStride}");
                    Console.WriteLine($"  TerrainModel.vertexCount: {m.vertexCount}");
                    Console.WriteLine($"  TerrainModel.faceCount: {m.faceCount}");
                    Console.WriteLine($"  TerrainModel.vertexBuffer.Length: {m.vertexBuffer?.Length ?? 0}");
                    Console.WriteLine($"  TerrainModel.indexBuffer.Length: {m.indexBuffer?.Length ?? 0}");
                    Console.WriteLine($"  TerrainModel.rgbas.Length: {m.rgbas?.Length ?? 0}");
                    Console.WriteLine($"  TerrainModel.textureConfig: {string.Join(", ", m.textureConfig?.Select(tc => $"id={tc.id},mode={tc.mode},size={tc.size}") ?? new List<string>())}");
                    Console.WriteLine($"  TerrainModel.lights.Count: {m.lights?.Count ?? 0}");
                }
                else
                {
                    Console.WriteLine("  (No TerrainModel attached)");
                }
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Generates collision mesh from terrain fragments and assigns to the level
        /// </summary>
        public static void GenerateAndAssignCollision(Level level, List<TerrainFragment> terrainFragments)
        {
            // --- DIAGNOSTIC LOGGING ---
            Console.WriteLine("[CollisionGen] GenerateAndAssignCollision called");
            Console.WriteLine($"[CollisionGen] terrainFragments.Count = {terrainFragments?.Count ?? -1}");

            if (terrainFragments == null || terrainFragments.Count == 0)
            {
                Console.WriteLine("[CollisionGen] No terrain fragments provided, skipping collision generation.");
                return;
            }

            // Erase existing collision
            level.collisionEngine = null;
            if (level.collisionChunks != null)
                level.collisionChunks.Clear();

            // Gather all triangles from terrain fragments
            var vertices = new List<Vector3>();
            var indices = new List<uint>();
            var vertexMap = new Dictionary<Vector3, uint>();
            uint nextIndex = 0;

            foreach (var frag in terrainFragments)
            {
                var model = frag.model as TerrainModel;
                if (model == null) continue;

                // Extract positions
                var fragVerts = new List<Vector3>();
                for (int i = 0; i < model.vertexCount; i++)
                {
                    var v = new Vector3(
                        model.vertexBuffer[i * 8 + 0],
                        model.vertexBuffer[i * 8 + 1],
                        model.vertexBuffer[i * 8 + 2]
                    );
                    fragVerts.Add(v);
                }

                // Map indices and skip degenerate triangles
                for (int i = 0; i < model.indexBuffer.Length; i += 3)
                {
                    uint[] tri = new uint[3];
                    for (int j = 0; j < 3; j++)
                    {
                        var v = fragVerts[model.indexBuffer[i + j]];
                        if (!vertexMap.TryGetValue(v, out uint idx))
                        {
                            idx = nextIndex++;
                            vertexMap[v] = idx;
                            vertices.Add(v);
                        }
                        tri[j] = idx;
                    }

                    if (tri[0] == tri[1] || tri[1] == tri[2] || tri[2] == tri[0])
                        continue; // Skip collapsed triangles

                    indices.Add(tri[0]);
                    indices.Add(tri[1]);
                    indices.Add(tri[2]);
                }
            }

            // --- AFTER (Calculates and embeds a yellow color for walkable ground) ---
            var vertexBuffer = new float[vertices.Count * 4];
            var defaultCollisionType = (byte)31; // CollisionType.Ground = 0x1F
            var colorUnion = new LibReplanetizer.Models.FloatColor
            {
                r = (byte)((defaultCollisionType & 0x03) << 6),
                g = (byte)((defaultCollisionType & 0x0C) << 4),
                b = (byte)(defaultCollisionType & 0xF0),
                a = 255
            };
            for (int i = 0; i < vertices.Count; i++)
            {
                int baseIdx = i * 4;
                vertexBuffer[baseIdx + 0] = vertices[i].X;
                vertexBuffer[baseIdx + 1] = vertices[i].Y;
                vertexBuffer[baseIdx + 2] = vertices[i].Z;
                vertexBuffer[baseIdx + 3] = colorUnion.value; // Embed the packed color
            }
            // Build index buffers
            var indBuff = indices.ToArray();
            var indexBuffer = indices.Select(i => (ushort)i).ToArray();
            var colorBuff = Enumerable.Repeat(BitConverter.ToUInt32(BitConverter.GetBytes(colorUnion.value), 0), vertices.Count).ToArray();

            // Create Collision object with full buffers for robustness
            var collision = (Collision)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Collision));
            collision.vertexBuffer = vertexBuffer;
            collision.indBuff = indBuff;
            collision.indexBuffer = indexBuffer;
            collision.colorBuff = colorBuff;

            // Assign to level
            level.collisionEngine = collision;

            // 🔧 FIX: The level's chunk lists MUST contain the collision data
            // so that both the serializer and editor stay in sync.
            level.collisionChunks = new List<Collision> { collision };
            level.collisionObjects = new List<CollisionObject> { new CollisionObject(collision) };

            // Mark the collision data as needing serialization and update the byte arrays
            level.collisionDirty = true;
            level.UpdateCollisionBytesFromChunks();

            // Diagnostic logging
            Console.WriteLine($"[CollisionGen] Generated collision: {vertices.Count} verts, {indBuff.Length / 3} triangles");
            if (level.collisionEngine != null)
                Console.WriteLine($"[CollisionGen] level.collisionEngine assigned: vertexBuffer={collision.vertexBuffer?.Length}, indBuff={collision.indBuff?.Length}");
            else
                Console.WriteLine("[CollisionGen] level.collisionEngine is null after assignment!");

            // 🆕 ADDED: Log the state of the collision chunks list
            Console.WriteLine($"[CollisionGen] level.collisionChunks count: {level.collisionChunks?.Count ?? 0}");
        }

        /// <summary>
        /// Imports an OBJ file and replaces the level's terrain and collision data
        /// with the geometry from that file.
        /// </summary>
        /// <param name="level">The level whose terrain/collision should be replaced.</param>
        /// <param name="objFilePath">Path to the OBJ file.</param>
        public static void ReplaceCollisionWithObj(Level level, string objFilePath)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (string.IsNullOrWhiteSpace(objFilePath)) throw new ArgumentException("OBJ path is invalid", nameof(objFilePath));

            // Import OBJ as terrain fragments using the level's texture list
            var importer = new ObjTerrainImporter();
            var fragments = importer.ImportObjAsTerrain(objFilePath, level.textures);

            // Replace the terrain fragments on the level (clear chunks if necessary)
            if (level.terrainEngine != null)
            {
                level.terrainEngine.fragments = fragments;
            }
            else
            {
                // If the level lacks a terrain engine, create one with level number 0
                level.terrainEngine = new Terrain(fragments, 0);
            }
            level.terrainChunks = new List<Terrain>();

            // Collision data is no longer automatically regenerated when importing
            // OBJ terrain. Users can explicitly resize or replace collision via
            // separate tools if desired.
        }
    }
}
