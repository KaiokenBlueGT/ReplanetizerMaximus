// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using LibReplanetizer;
using System;
using System.Linq;

namespace GeometrySwapper
{
    /// <summary>
    /// Provides validation helpers for levels after swapping operations.
    /// </summary>
    public static class Validator
    {
        /// <summary>
        /// Ensures pVar indices assigned to mobys are contiguous starting from 0.
        /// Throws InvalidOperationException if a gap is detected.
        /// </summary>
        /// <param name="level">Level to validate.</param>
        public static void AssertContiguousPVars(Level level)
        {
            if (level?.mobs == null)
                return;

            var ids = level.mobs
                           .Select(m => m.pvarIndex)
                           .Where(i => i >= 0)
                           .OrderBy(i => i)
                           .ToList();
            for (int i = 0; i < ids.Count; ++i)
            {
                if (ids[i] != i)
                    throw new InvalidOperationException($"pVar hole at {i} (found {ids[i]})");
            }
        }
    }
}
