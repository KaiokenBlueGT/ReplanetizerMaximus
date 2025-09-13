// Copyright (C) 2018-2021, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using LibReplanetizer.Headers;
using System;
using System.Collections.Generic;
using static LibReplanetizer.DataFunctions;

namespace LibReplanetizer.LevelObjects
{
    public class OcclusionData : ISerializable
    {
        public List<KeyValuePair<int, int>> mobyData;
        public List<KeyValuePair<int, int>> tieData;
        public List<KeyValuePair<int, int>> shrubData;

        public OcclusionData(byte[] occlusionBlock, OcclusionDataHeader head)
        {
            mobyData = new List<KeyValuePair<int, int>>();
            tieData = new List<KeyValuePair<int, int>>();
            shrubData = new List<KeyValuePair<int, int>>();

            int offset = 0;
            ReadPairs(occlusionBlock, offset, head.mobyCount, mobyData);
            offset += head.mobyCount * 0x08;

            ReadPairs(occlusionBlock, offset, head.tieCount, tieData);
            offset += head.tieCount * 0x08;

            ReadPairs(occlusionBlock, offset, head.shrubCount, shrubData);
        }

        public byte[] ToByteArray()
        {
            byte[] bytes = new byte[0x10 + mobyData.Count * 0x08 + tieData.Count * 0x08 + shrubData.Count * 0x08];
            WriteInt(bytes, 0x00, mobyData.Count);
            WriteInt(bytes, 0x04, tieData.Count);
            WriteInt(bytes, 0x08, shrubData.Count);

            int offset = 0x10;
            offset += WritePairs(bytes, offset, mobyData);
            offset += WritePairs(bytes, offset, tieData);
            WritePairs(bytes, offset, shrubData);

            return bytes;
        }

        private static void ReadPairs(byte[] block, int start, int count, List<KeyValuePair<int, int>> dest)
        {
            for (int i = 0; i < count; i++)
            {
                dest.Add(new KeyValuePair<int, int>(
                    ReadInt(block, start + i * 0x08 + 0x00),
                    ReadInt(block, start + i * 0x08 + 0x04)));
            }
        }

        private static int WritePairs(byte[] dest, int start, List<KeyValuePair<int, int>> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                WriteInt(dest, start + i * 0x08 + 0x00, source[i].Key);
                WriteInt(dest, start + i * 0x08 + 0x04, source[i].Value);
            }
            return source.Count * 0x08;
        }
    }
}
