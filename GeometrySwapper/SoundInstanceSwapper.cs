using System;
using System.Collections.Generic;
using System.Linq;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using OpenTK.Mathematics;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles the swapping of sound instances between levels, specifically from RC1 to RC2
    /// </summary>
    public class SoundInstanceSwapper
    {
        /// <summary>
        /// Copies sound instances from an RC1 level to an RC2 level
        /// </summary>
        /// <param name="targetLevel">The RC2 level where the sound instances will be added</param>
        /// <param name="rc1SourceLevel">The RC1 level containing the source sound instances</param>
        /// <returns>True if the operation was successful</returns>
        public static bool SwapSoundInstances(Level targetLevel, Level rc1SourceLevel)
        {
            Console.WriteLine($"?? Swapping sound instances from RC1 level to RC2 level...");

            if (targetLevel == null)
            {
                Console.WriteLine("  ? Error: Target level is null");
                return false;
            }

            if (rc1SourceLevel == null)
            {
                Console.WriteLine("  ? Error: RC1 source level is null");
                return false;
            }

            try
            {
                if (rc1SourceLevel.soundInstances == null || rc1SourceLevel.soundInstances.Count == 0)
                {
                    Console.WriteLine("  ?? No sound instances found in RC1 source level");
                    return false;
                }

                // Initialize the target level's sound instances list if it's null
                if (targetLevel.soundInstances == null)
                {
                    targetLevel.soundInstances = new List<SoundInstance>();
                }
                
                // Keep track of how many existing instances we had
                int existingInstanceCount = targetLevel.soundInstances.Count;
                
                // Clone each sound instance from RC1 to RC2
                foreach (var rc1SoundInstance in rc1SourceLevel.soundInstances)
                {
                    // Create a new sound instance using the same byte array data
                    var clonedInstance = CloneSoundInstance(rc1SoundInstance);
                    
                    // Add to target level
                    targetLevel.soundInstances.Add(clonedInstance);
                }

                Console.WriteLine($"  ? Added {rc1SourceLevel.soundInstances.Count} sound instances from RC1 level");
                Console.WriteLine($"  Total sound instances in target level: {targetLevel.soundInstances.Count}");
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ? Error during sound instance swap: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates a deep clone of a SoundInstance
        /// </summary>
        private static SoundInstance CloneSoundInstance(SoundInstance source)
        {
            // Create a byte array from the original instance
            byte[] sourceBytes = source.ToByteArray();
            
            // Create a new instance from the byte data
            SoundInstance newInstance = new SoundInstance(sourceBytes, 0);
            
            return newInstance;
        }
    }
}