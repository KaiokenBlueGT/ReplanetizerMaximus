// Copyright (C) 2018-2021, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using LibReplanetizer.LevelObjects;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using static LibReplanetizer.DataFunctions;

namespace LibReplanetizer.Models
{
    [StructLayout(LayoutKind.Explicit)]
    struct FloatColor
    {
        [FieldOffset(0)]
        public byte r;
        [FieldOffset(1)]
        public byte g;
        [FieldOffset(2)]
        public byte b;
        [FieldOffset(3)]
        public byte a;

        [FieldOffset(0)]
        public float value;
    }

    public class Collision : Model, IRenderable
    {
        public uint[] indBuff = { };
        public uint[] colorBuff = { };

        public Collision()
        {
        }

        public Collision Clone()
        {
            var clone = new Collision();
            clone.indBuff = (uint[]) indBuff.Clone();
            clone.colorBuff = (uint[]) colorBuff.Clone();
            clone.vertexBuffer = (float[]) vertexBuffer.Clone();
            clone.indexBuffer = new ushort[indBuff.Length];
            for (int i = 0; i < indBuff.Length; i++)
            {
                clone.indexBuffer[i] = (ushort) indBuff[i];
            }
            return clone;
        }

        public Collision(FileStream fs, int collisionPointer)
        {
            // Some standalone RCC files store the collision header at
            // offset zero. Only skip parsing when the stream is actually
            // empty so pointer 0 can still be used for real data.
            if (collisionPointer == 0 && fs.Length == 0) return;

            float div = 1024f;

            uint totalVertexCount = 0;

            byte[] headBlock = ReadBlock(fs, collisionPointer, 0x08);
            int collisionStart = collisionPointer + ReadInt(headBlock, 0x00);
            int collisionLength = ReadInt(headBlock, 0x04);
            if (collisionLength == 0)
            {
                collisionLength = (int) fs.Length - collisionStart;
            }
            byte[] collision = ReadBlock(fs, collisionStart, collisionLength);

            var vertexList = new List<float>();
            var indexList = new List<uint>();

            if (collision.Length < 4) return; // Defensive: not enough data
            ushort zShift = ReadUshort(collision, 0);
            ushort zCount = ReadUshort(collision, 2);

            FloatColor fc = new FloatColor { r = 255, g = 0, b = 255, a = 255 };

            for (int z = 0; z < zCount; z++)
            {
                int yOffsetIdx = (z * 4) + 0x04;
                if (yOffsetIdx + 3 >= collision.Length) continue;
                int yOffset = ReadInt(collision, yOffsetIdx);
                if (yOffset <= 0 || yOffset + 3 > collision.Length) continue;

                ushort yShift = ReadUshort(collision, yOffset + 0);
                ushort yCount = ReadUshort(collision, yOffset + 2);

                for (int y = 0; y < yCount; y++)
                {
                    int xOffsetIdx = yOffset + (y * 4) + 0x04;
                    if (xOffsetIdx + 3 >= collision.Length) continue;
                    int xOffset = ReadInt(collision, xOffsetIdx);
                    if (xOffset <= 0 || xOffset + 3 > collision.Length) continue;

                    ushort xShift = ReadUshort(collision, xOffset + 0);
                    ushort xCount = ReadUshort(collision, xOffset + 2);

                    for (int x = 0; x < xCount; x++)
                    {
                        int vOffsetIdx = xOffset + (x * 4) + 4;
                        if (vOffsetIdx + 3 >= collision.Length) continue;
                        int vOffset = ReadInt(collision, vOffsetIdx);
                        if (vOffset <= 0 || vOffset + 3 > collision.Length) continue;
                        ushort faceCount = ReadUshort(collision, vOffset);
                        byte vertexCount = vOffset + 2 < collision.Length ? collision[vOffset + 2] : (byte)0;
                        byte rCount = vOffset + 3 < collision.Length ? collision[vOffset + 3] : (byte)0;

                        if (vertexCount == 0) { continue; }
                        if (vOffset + 4 + (12 * vertexCount) > collision.Length) continue; // Defensive: vertex data out of bounds

                        byte[] collisionType = new byte[vertexCount];

                        // Gather indices and collision types
                        for (int f = 0; f < faceCount; f++)
                        {
                            int fOffset = vOffset + 4 + (12 * vertexCount) + (f * 4);
                            if (fOffset + 3 >= collision.Length) { continue; }

                            byte b0 = collision[fOffset];
                            byte b1 = collision[fOffset + 1];
                            byte b2 = collision[fOffset + 2];
                            byte b3 = collision[fOffset + 3];

                            if (b0 >= vertexCount || b1 >= vertexCount || b2 >= vertexCount) continue; // Defensive: index out of bounds

                            collisionType[b0] = b3;
                            collisionType[b1] = b3;
                            collisionType[b2] = b3;

                            uint f1 = totalVertexCount + b0;
                            uint f2 = totalVertexCount + b1;
                            uint f3 = totalVertexCount + b2;
                            indexList.Add(f1);
                            indexList.Add(f2);
                            indexList.Add(f3);

                            if (f < rCount)
                            {
                                int rOffset = vOffset + 4 + (12 * vertexCount) + (faceCount * 4) + f;
                                if (rOffset < collision.Length)
                                {
                                    byte rv = collision[rOffset];
                                    if (rv >= vertexCount)
                                    {
                                        rv = (byte)(rv % vertexCount);
                                    }

                                    uint f4 = totalVertexCount + rv;
                                    indexList.Add(f1);
                                    indexList.Add(f3);
                                    indexList.Add(f4);
                                    collisionType[rv] = b3;
                                }
                            }
                        }

                        // Parse vertex positions once after gathering indices
                        for (int v = 0; v < vertexCount; v++)
                        {
                            int pOffset = vOffset + (12 * v) + 4;
                            if (pOffset + 8 >= collision.Length) { continue; }

                            vertexList.Add(ReadFloat(collision, pOffset + 0) / div + 4 * (xShift + x + 0.5f));  //Vertex X
                            vertexList.Add(ReadFloat(collision, pOffset + 4) / div + 4 * (yShift + y + 0.5f));  //Vertex Y
                            vertexList.Add(ReadFloat(collision, pOffset + 8) / div + 4 * (zShift + z + 0.5f));  //Vertex Z

                            // Colorize different types of collision without knowing what they are
                            fc.r = (byte)((collisionType[v] & 0x03) << 6);
                            fc.g = (byte)((collisionType[v] & 0x0C) << 4);
                            fc.b = (byte)(collisionType[v] & 0xF0);

                            vertexList.Add(fc.value);
                            totalVertexCount++;
                        }
                    }
                }
            }
            vertexBuffer = vertexList.ToArray();
            indBuff = indexList.ToArray();
        }
    }
}
