// Copyright (C) 2018-2021, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LibReplanetizer
{
    /// <summary>
    /// Known collision surface types used by the engine. Values may vary between games
    /// but <see cref="CollisionType.Ground"/> is the default walkable surface for UYA.
    /// </summary>
    public enum CollisionType : byte
    {
        /// <summary>Standard walkable ground.</summary>
        Ground = 0x1F,
        /// <summary>Instant-death plane/hazard. Adjust value for your game as needed.</summary>
        DeathPlane = 0x04
    }

    public static class DataFunctions
    {
        [StructLayout(LayoutKind.Explicit)]
        struct FloatUnion
        {
            [FieldOffset(0)]
            public byte byte0;
            [FieldOffset(1)]
            public byte byte1;
            [FieldOffset(2)]
            public byte byte2;
            [FieldOffset(3)]
            public byte byte3;

            [FieldOffset(0)]
            public float value;
        }

        static FloatUnion FLOAT_BYTES;

        public static float ReadFloat(byte[] buf, int offset)
        {
            FLOAT_BYTES.byte0 = buf[offset + 3];
            FLOAT_BYTES.byte1 = buf[offset + 2];
            FLOAT_BYTES.byte2 = buf[offset + 1];
            FLOAT_BYTES.byte3 = buf[offset];
            return FLOAT_BYTES.value;
        }

        public static int ReadInt(byte[] buf, int offset)
        {
            return buf[offset + 0] << 24 | buf[offset + 1] << 16 | buf[offset + 2] << 8 | buf[offset + 3];
        }

        public static short ReadShort(byte[] buf, int offset)
        {
            return (short) (buf[offset + 0] << 8 | buf[offset + 1]);
        }

        public static uint ReadUint(byte[] buf, int offset)
        {
            return (uint) (buf[offset + 0] << 24 | buf[offset + 1] << 16 | buf[offset + 2] << 8 | buf[offset + 3]);
        }

        public static ushort ReadUshort(byte[] buf, int offset)
        {
            return (ushort) (buf[offset + 0] << 8 | buf[offset + 1]);
        }

        public static Matrix4 ReadMatrix4(byte[] buf, int offset)
        {
            return new Matrix4(
                ReadFloat(buf, offset + 0x00),
                ReadFloat(buf, offset + 0x04),
                ReadFloat(buf, offset + 0x08),
                ReadFloat(buf, offset + 0x0C),

                ReadFloat(buf, offset + 0x10),
                ReadFloat(buf, offset + 0x14),
                ReadFloat(buf, offset + 0x18),
                ReadFloat(buf, offset + 0x1C),

                ReadFloat(buf, offset + 0x20),
                ReadFloat(buf, offset + 0x24),
                ReadFloat(buf, offset + 0x28),
                ReadFloat(buf, offset + 0x2C),

                ReadFloat(buf, offset + 0x30),
                ReadFloat(buf, offset + 0x34),
                ReadFloat(buf, offset + 0x38),
                ReadFloat(buf, offset + 0x3C)
                );
        }

        public static Matrix3x4 ReadMatrix3x4(byte[] buf, int offset)
        {
            return new Matrix3x4(
                ReadFloat(buf, offset + 0x00),
                ReadFloat(buf, offset + 0x04),
                ReadFloat(buf, offset + 0x08),
                ReadFloat(buf, offset + 0x0C),

                ReadFloat(buf, offset + 0x10),
                ReadFloat(buf, offset + 0x14),
                ReadFloat(buf, offset + 0x18),
                ReadFloat(buf, offset + 0x1C),

                ReadFloat(buf, offset + 0x20),
                ReadFloat(buf, offset + 0x24),
                ReadFloat(buf, offset + 0x28),
                ReadFloat(buf, offset + 0x2C)
                );
        }

        public static byte[] ReadBlock(FileStream fs, long offset, int length)
        {
            if (length > 0)
            {
                fs.Seek(offset, SeekOrigin.Begin);
                byte[] returnBytes = new byte[length];
                fs.Read(returnBytes, 0, length);
                return returnBytes;
            }
            else
            {
                byte[] returnBytes = new byte[0x10];
                return returnBytes;
            }
        }

        public static byte[] ReadBlockNopad(FileStream fs, long offset, int length)
        {
            if (length > 0)
            {
                fs.Seek(offset, SeekOrigin.Begin);
                byte[] returnBytes = new byte[length];
                fs.Read(returnBytes, 0, length);
                return returnBytes;
            }
            return new byte[0];
        }

        public static byte[] ReadString(FileStream fs, int offset)
        {
            var output = new List<byte>();

            fs.Seek(offset, SeekOrigin.Begin);

            byte[] buffer = new byte[4];
            do
            {
                fs.Read(buffer, 0, 4);
                output.AddRange(buffer);
            }
            while (buffer[3] != '\0');

            output.RemoveAll(item => item == 0);

            return output.ToArray();
        }

        public static void WriteUint(byte[] byteArr, int offset, uint input)
        {
            byte[] byt = BitConverter.GetBytes(input);
            byteArr[offset + 0] = byt[3];
            byteArr[offset + 1] = byt[2];
            byteArr[offset + 2] = byt[1];
            byteArr[offset + 3] = byt[0];
        }

        public static void WriteInt(byte[] byteArr, int offset, int input)
        {
            byte[] byt = BitConverter.GetBytes(input);
            byteArr[offset + 0] = byt[3];
            byteArr[offset + 1] = byt[2];
            byteArr[offset + 2] = byt[1];
            byteArr[offset + 3] = byt[0];
        }

        public static void WriteFloat(byte[] byteArr, int offset, float input)
        {
            byte[] byt = BitConverter.GetBytes(input);
            byteArr[offset + 0] = byt[3];
            byteArr[offset + 1] = byt[2];
            byteArr[offset + 2] = byt[1];
            byteArr[offset + 3] = byt[0];
        }

        public static void WriteShort(byte[] byteArr, int offset, short input)
        {
            byte[] byt = BitConverter.GetBytes(input);
            byteArr[offset + 0] = byt[1];
            byteArr[offset + 1] = byt[0];
        }

        public static void WriteUshort(byte[] byteArr, int offset, ushort input)
        {
            byte[] byt = BitConverter.GetBytes(input);
            byteArr[offset + 0] = byt[1];
            byteArr[offset + 1] = byt[0];
        }

        public static void WriteMatrix4(byte[] byteArray, int offset, Matrix4 input)
        {
            WriteFloat(byteArray, offset + 0x00, input.M11);
            WriteFloat(byteArray, offset + 0x04, input.M12);
            WriteFloat(byteArray, offset + 0x08, input.M13);
            WriteFloat(byteArray, offset + 0x0C, input.M14);

            WriteFloat(byteArray, offset + 0x10, input.M21);
            WriteFloat(byteArray, offset + 0x14, input.M22);
            WriteFloat(byteArray, offset + 0x18, input.M23);
            WriteFloat(byteArray, offset + 0x1C, input.M24);

            WriteFloat(byteArray, offset + 0x20, input.M31);
            WriteFloat(byteArray, offset + 0x24, input.M32);
            WriteFloat(byteArray, offset + 0x28, input.M33);
            WriteFloat(byteArray, offset + 0x2C, input.M34);

            WriteFloat(byteArray, offset + 0x30, input.M41);
            WriteFloat(byteArray, offset + 0x34, input.M42);
            WriteFloat(byteArray, offset + 0x38, input.M43);
            WriteFloat(byteArray, offset + 0x3C, input.M44);
        }

        public static void WriteMatrix3x4(byte[] byteArray, int offset, Matrix3x4 input)
        {
            WriteFloat(byteArray, offset + 0x00, input.M11);
            WriteFloat(byteArray, offset + 0x04, input.M12);
            WriteFloat(byteArray, offset + 0x08, input.M13);
            WriteFloat(byteArray, offset + 0x0C, input.M14);

            WriteFloat(byteArray, offset + 0x10, input.M21);
            WriteFloat(byteArray, offset + 0x14, input.M22);
            WriteFloat(byteArray, offset + 0x18, input.M23);
            WriteFloat(byteArray, offset + 0x1C, input.M24);

            WriteFloat(byteArray, offset + 0x20, input.M31);
            WriteFloat(byteArray, offset + 0x24, input.M32);
            WriteFloat(byteArray, offset + 0x28, input.M33);
            WriteFloat(byteArray, offset + 0x2C, input.M34);
        }

        public static byte[] GetBytes(byte[] array, int ind, int length)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++)
            {
                data[i] = array[ind + i];
            }
            return data;
        }

        public static int GetLength(int length, int alignment = 0)
        {
            while (length % 0x10 != alignment)
            {
                length++;
            }
            return length;
        }

        // vertexbuffers are often aligned to the nearest 0x80 in the file
        public static int DistToFile80(int length, int alignment = 0)
        {
            int added = 0;
            while (length % 0x80 != alignment)
            {
                length++;
                added++;
            }
            return added;
        }

        public static int GetLength20(int length, int alignment = 0)
        {
            while (length % 0x20 != alignment)
            {
                length++;
            }
            return length;
        }

        public static int GetLength100(int length)
        {
            while (length % 0x100 != 0)
            {
                length++;
            }
            return length;
        }

        public static void Pad(List<byte> arr)
        {
            while (arr.Count % 0x10 != 0)
            {
                arr.Add(0);
            }
        }

        /// <summary>
        /// Serializes a Collision object to the expected RCC file format (header + buffers)
        /// </summary>
        public static byte[] SerializeCollisionToRcc(LibReplanetizer.Models.Collision collision)
        {
            // This is a minimal implementation. You may need to adjust for your game version.
            // RCC files start with a header: [offset to collision data][length of collision data]
            // Then the collision data block (vertex/index buffers, etc)
            // We'll write a dummy header and then the buffers.
            using (var ms = new MemoryStream())
            {
                // Write header (8 bytes)
                int headerSize = 8;
                int collisionDataOffset = headerSize; // Data starts right after header
                // Calculate collision data length
                int vertexBytes = collision.vertexBuffer.Length * sizeof(float);
                int indexBytes = collision.indBuff.Length * sizeof(uint);
                int collisionDataLength = vertexBytes + indexBytes;
                // Write offset (relative to start)
                WriteInt(ms, collisionDataOffset);
                WriteInt(ms, collisionDataLength);
                // Write vertex buffer
                var vertexBuf = new byte[vertexBytes];
                Buffer.BlockCopy(collision.vertexBuffer, 0, vertexBuf, 0, vertexBytes);
                ms.Write(vertexBuf, 0, vertexBuf.Length);
                // Write index buffer
                var indexBuf = new byte[indexBytes];
                Buffer.BlockCopy(collision.indBuff, 0, indexBuf, 0, indexBytes);
                ms.Write(indexBuf, 0, indexBuf.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Serializes a <see cref="LibReplanetizer.Models.Collision"/> into a hierarchical
        /// RCC chunk. A callback may be supplied to determine the <see cref="CollisionType"/>
        /// for each face; if omitted all faces are written as <see cref="CollisionType.Ground"/>.
        /// </summary>
        /// <summary>
        /// Serializes a <see cref="LibReplanetizer.Models.Collision"/> into a hierarchical
        /// RCC chunk. A callback may be supplied to determine the <see cref="CollisionType"/>
        /// for each face; if omitted all faces are written as <see cref="CollisionType.Ground"/>.
        /// </summary>
        public static byte[] SerializeCollisionToRccChunked(
            LibReplanetizer.Models.Collision collision,
            Func<int, CollisionType> collisionTypeProvider = null)
        {
            int totalVertexCount = collision.vertexBuffer.Length / 4;
            if (totalVertexCount == 0)
            {
                // Empty collision chunk: header points to 16 bytes of zeroed data
                return new byte[]
                {
                    0,0,0,0x08,  // offset to data
                    0,0,0,0x10,  // length of data (16 bytes)
                    // 16 bytes of zero for shifts/counts/reserved
                    0,0,0,0, 0,0,0,0,
                    0,0,0,0, 0,0,0,0
                };
            }

            float minX = collision.vertexBuffer[0], maxX = collision.vertexBuffer[0];
            float minY = collision.vertexBuffer[1], maxY = collision.vertexBuffer[1];
            float minZ = collision.vertexBuffer[2], maxZ = collision.vertexBuffer[2];
            for (int i = 1; i < totalVertexCount; i++)
            {
                float x = collision.vertexBuffer[i * 4 + 0];
                float y = collision.vertexBuffer[i * 4 + 1];
                float z = collision.vertexBuffer[i * 4 + 2];
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }

            // FIX: Use 'short' for shift values to correctly handle negative coordinates.
            short zShift = (short)MathF.Floor(minZ / 4f);
            short yShift = (short)MathF.Floor(minY / 4f);
            short xShift = (short)MathF.Floor(minX / 4f);

            // Counts remain ushort as they are always positive.
            ushort zCount = (ushort)(MathF.Floor(maxZ / 4f) - zShift + 1);
            ushort yCount = (ushort)(MathF.Floor(maxY / 4f) - yShift + 1);
            ushort xCount = (ushort)(MathF.Floor(maxX / 4f) - xShift + 1);

            // --- PATCH: Subdivide cells that exceed 255 vertices ---
            var cells = new Dictionary<(int z, int y, int x), Cell>();
            int faceTotal = collision.indBuff.Length / 3;
            for (int f = 0; f < faceTotal; f++)
            {
                int g0 = (int)collision.indBuff[f * 3 + 0];
                int g1 = (int)collision.indBuff[f * 3 + 1];
                int g2 = (int)collision.indBuff[f * 3 + 2];
                var cellsForFace = new HashSet<(int z, int y, int x)>();
                int[] verts = { g0, g1, g2 };
                foreach (int gv in verts)
                {
                    float vx = collision.vertexBuffer[gv * 4 + 0];
                    float vy = collision.vertexBuffer[gv * 4 + 1];
                    float vz = collision.vertexBuffer[gv * 4 + 2];
                    int cz = (int)MathF.Floor(vz / 4f) - zShift;
                    int cy = (int)MathF.Floor(vy / 4f) - yShift;
                    int cx = (int)MathF.Floor(vx / 4f) - xShift;
                    cellsForFace.Add((cz, cy, cx));
                }
                foreach (var key in cellsForFace)
                {
                    if (!cells.TryGetValue(key, out var cell))
                    {
                        cell = new Cell();
                        cells[key] = cell;
                    }
                    int l0 = cell.GetOrAddVertex(g0, collision.vertexBuffer);
                    int l1 = cell.GetOrAddVertex(g1, collision.vertexBuffer);
                    int l2 = cell.GetOrAddVertex(g2, collision.vertexBuffer);
                    cell.Indices.Add(l0);
                    cell.Indices.Add(l1);
                    cell.Indices.Add(l2);
                    var cType = collisionTypeProvider != null ?
                        collisionTypeProvider(f) : CollisionType.Ground;
                    cell.FaceTypes.Add(cType);
                }
            }

            // --- PATCH: Subdivide oversized cells ---
            var subdividedCells = new Dictionary<(int z, int y, int x, int sub), Cell>();
            foreach (var kvp in cells)
            {
                var key = kvp.Key;
                var cell = kvp.Value;
                int vertexCount = cell.Vertices.Count / 3;
                if (vertexCount <= 255)
                {
                    subdividedCells[(key.z, key.y, key.x, 0)] = cell;
                }
                else
                {
                    // Split cell into multiple subcells by grouping faces into batches of <=255 vertices
                    int subIndex = 0;
                    var faceGroups = new List<List<int>>();
                    var faceTypesGroups = new List<List<CollisionType>>();
                    int faceCount = cell.Indices.Count / 3;
                    for (int i = 0; i < faceCount; )
                    {
                        var subCell = new Cell();
                        int subVerts = 0;
                        var globalToLocal = new Dictionary<int, int>();
                        int j = i;
                        for (; j < faceCount && subVerts < 255; j++)
                        {
                            int g0 = cell.Indices[j * 3 + 0];
                            int g1 = cell.Indices[j * 3 + 1];
                            int g2 = cell.Indices[j * 3 + 2];
                            int[] gVerts = { g0, g1, g2 };
                            foreach (int gv in gVerts)
                            {
                                if (!globalToLocal.ContainsKey(gv))
                                {
                                    globalToLocal[gv] = subCell.Vertices.Count / 3;
                                    subCell.Vertices.Add(cell.Vertices[gv * 3 + 0]);
                                    subCell.Vertices.Add(cell.Vertices[gv * 3 + 1]);
                                    subCell.Vertices.Add(cell.Vertices[gv * 3 + 2]);
                                    subVerts++;
                                }
                            }
                            subCell.Indices.Add(globalToLocal[g0]);
                            subCell.Indices.Add(globalToLocal[g1]);
                            subCell.Indices.Add(globalToLocal[g2]);
                            subCell.FaceTypes.Add(cell.FaceTypes[j]);
                        }
                        subdividedCells[(key.z, key.y, key.x, subIndex)] = subCell;
                        subIndex++;
                        i = j;
                    }
                }
            }

            using var body = new MemoryStream();

            // Top-level grid header: zShift/zCount followed by z offsets
            WriteUshort(body, (ushort)zShift);
            WriteUshort(body, zCount);

            int zOffsetsStart = (int)body.Position;
            for (int i = 0; i < zCount; i++) WriteInt(body, 0);
            for (int z = 0; z < zCount; z++)
            {
                bool hasZ = false;
                foreach (var key in subdividedCells.Keys) { if (key.z == z) { hasZ = true; break; } }
                if (!hasZ) continue;
                int yBlockStart = (int)body.Position;
                WriteUshort(body, (ushort)yShift);
                WriteUshort(body, yCount);
                int yOffsetsStart = (int)body.Position;
                for (int i = 0; i < yCount; i++) WriteInt(body, 0);
                for (int y = 0; y < yCount; y++)
                {
                    bool hasY = false;
                    foreach (var key in subdividedCells.Keys) { if (key.z == z && key.y == y) { hasY = true; break; } }
                    if (!hasY) continue;
                    int xBlockStart = (int)body.Position;
                    WriteUshort(body, (ushort)xShift);
                    WriteUshort(body, xCount);
                    int xOffsetsStart = (int)body.Position;
                    for (int i = 0; i < xCount; i++) WriteInt(body, 0);
                    for (int x = 0; x < xCount; x++)
                    {
                        // Write all subcells for this (z,y,x)
                        int subCellIdx = 0;
                        while (subdividedCells.TryGetValue((z, y, x, subCellIdx), out var cell))
                        {
                            int vBlockStart = (int)body.Position;
                            int faceCount = cell.Indices.Count / 3;
                            if (faceCount > ushort.MaxValue) throw new ArgumentException("Collision chunk exceeds 65535 faces");
                            int vertexCount = cell.Vertices.Count / 3;
                            byte rCount = 0;
                            WriteUshort(body, (ushort)faceCount);
                            body.WriteByte((byte)vertexCount);
                            body.WriteByte(rCount);
                            float baseZ = 4f * (zShift + z + 0.5f);
                            float baseY = 4f * (yShift + y + 0.5f);
                            float baseX = 4f * (xShift + x + 0.5f);
                            for (int iVert = 0; iVert < vertexCount; iVert++)
                            {
                                float vx = cell.Vertices[iVert * 3 + 0];
                                float vy = cell.Vertices[iVert * 3 + 1];
                                float vz = cell.Vertices[iVert * 3 + 2];
                                WriteFloat(body, (vx - baseX) * 1024f);
                                WriteFloat(body, (vy - baseY) * 1024f);
                                WriteFloat(body, (vz - baseZ) * 1024f);
                            }
                            for (int iFace = 0; iFace < faceCount; iFace++)
                            {
                                body.WriteByte((byte)cell.Indices[iFace * 3 + 0]);
                                body.WriteByte((byte)cell.Indices[iFace * 3 + 1]);
                                body.WriteByte((byte)cell.Indices[iFace * 3 + 2]);
                                body.WriteByte((byte)cell.FaceTypes[iFace]);
                            }
                            int vBlockEnd = (int)body.Position;
                            PatchInt(body, xOffsetsStart + x * 4, vBlockStart);
                            body.Position = vBlockEnd;
                            subCellIdx++;
                        }
                    }
                    int endX = (int)body.Position;
                    PatchInt(body, yOffsetsStart + y * 4, xBlockStart);
                    body.Position = endX;
                }
                int endY = (int)body.Position;
                PatchInt(body, zOffsetsStart + z * 4, yBlockStart);
                body.Position = endY;
            }
            byte[] chunkData = body.ToArray();
            using var ms = new MemoryStream();
            // --- PATCH: Write correct 16-byte header ---
            WriteInt(ms, 0x10); // offset to chunk data
            WriteInt(ms, chunkData.Length); // length of chunk data
            ms.Write(new byte[8], 0, 8); // 8 bytes padding/reserved
            ms.Write(chunkData, 0, chunkData.Length);
            return ms.ToArray();
        }

        /// <summary>
        /// Serialize a <see cref="Collision"/> into a standalone RCC file with a full
        /// 64-byte chunk header. This wraps the chunked serializer and strips the
        /// internal 16-byte header so the result can be written directly to disk.
        /// </summary>
        public static byte[] SerializeCollisionToRccStandalone(
            LibReplanetizer.Models.Collision collision,
            Func<int, CollisionType> collisionTypeProvider = null)
        {
            // Reuse the chunked serializer to build the collision block and ensure
            // cells are subdivided when exceeding 255 vertices.
            var chunked = SerializeCollisionToRccChunked(collision, collisionTypeProvider);

            // Remove the 16-byte chunk header to get the raw collision data block.
            var collisionBlock = new byte[chunked.Length - 0x10];
            Array.Copy(chunked, 0x10, collisionBlock, 0, collisionBlock.Length);

            using var ms = new MemoryStream();
            int headerSize = 0x40;
            for (int i = 0; i < 16; i++)
            {
                int pointer = (i == 4) ? headerSize : 0;
                ms.WriteByte((byte)((pointer >> 24) & 0xFF));
                ms.WriteByte((byte)((pointer >> 16) & 0xFF));
                ms.WriteByte((byte)((pointer >> 8) & 0xFF));
                ms.WriteByte((byte)(pointer & 0xFF));
            }
            ms.Write(collisionBlock, 0, collisionBlock.Length);
            return ms.ToArray();
        }

        class Cell
        {
            public readonly List<float> Vertices = new List<float>();
            public readonly List<int> Indices = new List<int>();
            public readonly List<CollisionType> FaceTypes = new List<CollisionType>();
            private readonly Dictionary<int, int> vertexMap = new Dictionary<int, int>();

            public int GetOrAddVertex(int globalIndex, float[] vertexBuffer)
            {
                if (!vertexMap.TryGetValue(globalIndex, out int local))
                {
                    local = Vertices.Count / 3;
                    vertexMap[globalIndex] = local;
                    Vertices.Add(vertexBuffer[globalIndex * 4 + 0]);
                    Vertices.Add(vertexBuffer[globalIndex * 4 + 1]);
                    Vertices.Add(vertexBuffer[globalIndex * 4 + 2]);
                }
                return local;
            }
        }

        private static void WriteUshort(Stream stream, ushort value)
        {
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }
        private static void WriteFloat(Stream stream, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            stream.WriteByte(bytes[3]);
            stream.WriteByte(bytes[2]);
            stream.WriteByte(bytes[1]);
            stream.WriteByte(bytes[0]);
        }
        private static void WriteInt(Stream stream, int value)
        {
            // Write as big-endian (same as ReadInt)
            stream.WriteByte((byte)((value >> 24) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }
        private static void PatchInt(Stream stream, int pos, int value)
        {
            long cur = stream.Position;
            stream.Position = pos;
            WriteInt(stream, value);
            stream.Position = cur;
        }
    }
}
