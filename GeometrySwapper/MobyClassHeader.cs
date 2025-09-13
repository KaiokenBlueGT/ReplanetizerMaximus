using System.Runtime.InteropServices;

namespace LibReplanetizer.LevelObjects
{
    /// <summary>
    /// Header describing code callbacks for a moby class.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Rc1MobyClassHeader
    {
        /// <summary>Size/version field at 0x00.</summary>
        public uint sizeVersion;
        /// <summary>Pointer to the update function at 0x04.</summary>
        public uint pUpdate;
        /// <summary>Callback pointer at 0x08 (render/update helper).</summary>
        public uint func1;
        /// <summary>Callback pointer at 0x0C.</summary>
        public uint func2;
        /// <summary>Callback pointer at 0x10.</summary>
        public uint func3;
        /// <summary>Callback pointer at 0x14.</summary>
        public uint func4;
        /// <summary>Callback pointer at 0x18.</summary>
        public uint func5;
        /// <summary>Callback pointer at 0x1C.</summary>
        public uint func6;
        /// <summary>Callback pointer at 0x20.</summary>
        public uint func7;
        /// <summary>Callback pointer at 0x24.</summary>
        public uint func8;

        public const int SIZE_RC1 = 0x28;
    }

    /// <summary>
    /// Updated header used on GC builds (RC2 and later).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GcMobyClassHeader
    {
        /// <summary>Size/version field at 0x00.</summary>
        public uint sizeVersion;
        /// <summary>Pointer to the update function at 0x04.</summary>
        public uint pUpdate;
        /// <summary>Pointer to an additional routine at 0x08.</summary>
        public uint pExtra; // new field introduced in RC2/RC3
        /// <summary>Callback pointer at 0x0C.</summary>
        public uint func1;
        /// <summary>Callback pointer at 0x10.</summary>
        public uint func2;
        /// <summary>Callback pointer at 0x14.</summary>
        public uint func3;
        /// <summary>Callback pointer at 0x18.</summary>
        public uint func4;
        /// <summary>Callback pointer at 0x1C.</summary>
        public uint func5;
        /// <summary>Callback pointer at 0x20.</summary>
        public uint func6;
        /// <summary>Callback pointer at 0x24.</summary>
        public uint func7;
        /// <summary>Callback pointer at 0x28.</summary>
        public uint func8;
        /// <summary>Callback pointer at 0x2C.</summary>
        public uint func9;

        public const int SIZE_GC = 0x30;
    }

    /// <summary>
    /// Utility methods for working with moby class headers.
    /// </summary>
    public static class MobyClassHeaderConverter
    {
        /// <summary>
        /// Converts an RC1 moby class header to the RC2/RC3 layout.
        /// Missing RC2/3-specific fields are initialised to safe defaults.
        /// </summary>
        /// <remarks>
        /// RC1 defines fewer behaviour callbacks.  Mobys whose behaviour relies on
        /// RC1-specific routines may not function identically after conversion as
        /// these routines have no direct RC2/RC3 counterparts.
        /// </remarks>
        public static GcMobyClassHeader ConvertRc1ClassHeaderToGc(Rc1MobyClassHeader rc1)
        {
            return new GcMobyClassHeader
            {
                sizeVersion = rc1.sizeVersion,
                pUpdate = rc1.pUpdate,
                pExtra = 0, // RC1 has no equivalent field
                func1 = rc1.func1,
                func2 = rc1.func2,
                func3 = rc1.func3,
                func4 = rc1.func4,
                func5 = rc1.func5,
                func6 = rc1.func6,
                func7 = rc1.func7,
                func8 = rc1.func8,
                func9 = 0 // no RC1 counterpart
            };
        }
    }
}
