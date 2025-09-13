using System;

namespace GeometrySwapper
{
    public static class FutureToUyaConverter
    {
        [Flags]
        public enum FutureSwapOptions
        {
            None = 0,
            UFrags = 1 << 0,
            Mobys = 1 << 1,
            Ties = 1 << 2,
            Textures = 1 << 3,
            Collision = 1 << 4,
            PositionMappings = 1 << 5,
            All = UFrags | Mobys | Ties | Textures | Collision | PositionMappings
        }

        public static void ConvertFutureLevelToUya(
            string futureLevelPath,
            string uyaDonorPath,
            string outputPath,
            FutureSwapOptions options)
        {
            // Method intentionally left blank.
        }
    }
}
