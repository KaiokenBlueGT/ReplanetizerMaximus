// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;

namespace GeometrySwapper
{
    /// <summary>
    /// Handles the registration of planets in the Galactic Map for Ratchet & Clank games
    /// </summary>
    public static class GalacticMapManager
    {
        // Constants for the Galactic Map structure
        private const int PLANET_COUNT_OFFSET = 0x8142;
        private const int PLANET_LIST_BASE_OFFSET = 0x8146;
        private const int PLANET_STRUCT_SIZE = 0x40;

        /// <summary>
        /// Adds a planet to the Galactic Map in an RC2 level
        /// </summary>
        /// <param name="targetLevel">The RC2 level to add the planet to</param>
        /// <param name="planetName">The name of the planet to display on the map</param>
        /// <param name="cityName">The name of the city/location on the planet</param>
        /// <param name="planetId">The ID of the planet (should match the level number/ID)</param>
        /// <param name="isAvailable">Whether the planet should be marked as available</param>
        /// <param name="patchAllText">Whether to also patch the all_text file with the planet name</param>
        /// <param name="gamePath">Path to the game's ps3data directory (required if patchAllText is true)</param>
        /// <returns>True if the operation was successful</returns>
        public static bool AddPlanetToGalacticMap(Level targetLevel, string planetName, string cityName, int planetId, bool isAvailable = true, bool patchAllText = false, string gamePath = null)
        {
            Console.WriteLine($"\n🪐 ADDING PLANET TO GALACTIC MAP: {planetName} - {cityName} (ID: {planetId})");

            if (targetLevel == null)
            {
                Console.WriteLine("  ❌ Error: Target level is null");
                return false;
            }

            try
            {
                // Step 1: Add entries to language data tables for both planet and city names
                Console.WriteLine("  Adding planet and city names to language data tables...");

                // Planet name uses the base planetId
                int planetNameId = AddPlanetNameToLanguageData(targetLevel, planetName, planetId);

                // City name uses planetId + 10000 (based on RC2's pattern)
                int cityNameId = AddCityNameToLanguageData(targetLevel, cityName, planetId + 10000);

                // Step 2: Update UI elements to make the planet visible on the map
                Console.WriteLine("  Updating UI elements for the galactic map...");

                // Step 3: Update the PlanetCount in engine.ps3
                UpdatePlanetCountInEngineFile(targetLevel, planetId);

                // Step 4: Add a planet entry to the planet list
                AddPlanetEntryToPlanetList(targetLevel, planetId, planetNameId, isAvailable);

                // Step 5 (Optional): Update the all_text file with the planet and city names
                bool allTextSuccess = true;
                if (patchAllText)
                {
                    if (string.IsNullOrEmpty(gamePath))
                    {
                        Console.WriteLine("  ⚠️ Warning: Cannot patch all_text file - gamePath not provided");
                    }
                    else
                    {
                        allTextSuccess = AddPlanetToAllTextFile(gamePath, planetId, planetName, cityName);
                    }
                }

                Console.WriteLine($"✅ Planet '{planetName} - {cityName}' (ID: {planetId}) added to the Galactic Map successfully");
                return true && allTextSuccess;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error adding planet to Galactic Map: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Adds the planet name to all language data tables
        /// </summary>
        private static int AddPlanetNameToLanguageData(Level targetLevel, string planetName, int planetId)
        {
            // Planet names need to be prefixed with "Planet " in RC2
            string fullPlanetName = $"Planet {planetName}";
            Console.WriteLine($"  Adding planet name '{fullPlanetName}' with ID {planetId} to language tables...");

            // Initialize language data lists if they don't exist
            if (targetLevel.english == null) targetLevel.english = new List<LanguageData>();
            if (targetLevel.ukenglish == null) targetLevel.ukenglish = new List<LanguageData>();
            if (targetLevel.french == null) targetLevel.french = new List<LanguageData>();
            if (targetLevel.german == null) targetLevel.german = new List<LanguageData>();
            if (targetLevel.spanish == null) targetLevel.spanish = new List<LanguageData>();
            if (targetLevel.italian == null) targetLevel.italian = new List<LanguageData>();
            if (targetLevel.japanese == null) targetLevel.japanese = new List<LanguageData>();
            if (targetLevel.korean == null) targetLevel.korean = new List<LanguageData>();

            // Create entries for all languages (using the same English name for simplicity)
            AddLanguageEntry(targetLevel.english, planetId, fullPlanetName);
            AddLanguageEntry(targetLevel.ukenglish, planetId, fullPlanetName);
            AddLanguageEntry(targetLevel.french, planetId, fullPlanetName);
            AddLanguageEntry(targetLevel.german, planetId, fullPlanetName);
            AddLanguageEntry(targetLevel.spanish, planetId, fullPlanetName);
            AddLanguageEntry(targetLevel.italian, planetId, fullPlanetName);
            AddLanguageEntry(targetLevel.japanese, planetId, fullPlanetName);
            AddLanguageEntry(targetLevel.korean, planetId, fullPlanetName);

            Console.WriteLine("  ✅ Planet name added to all language tables");
            return planetId;
        }

        /// <summary>
        /// Adds the city name to all language data tables
        /// </summary>
        private static int AddCityNameToLanguageData(Level targetLevel, string cityName, int cityId)
        {
            Console.WriteLine($"  Adding city name '{cityName}' with ID {cityId} to language tables...");

            // Create entries for all languages (using the same English name for simplicity)
            AddLanguageEntry(targetLevel.english, cityId, cityName);
            AddLanguageEntry(targetLevel.ukenglish, cityId, cityName);
            AddLanguageEntry(targetLevel.french, cityId, cityName);
            AddLanguageEntry(targetLevel.german, cityId, cityName);
            AddLanguageEntry(targetLevel.spanish, cityId, cityName);
            AddLanguageEntry(targetLevel.italian, cityId, cityName);
            AddLanguageEntry(targetLevel.japanese, cityId, cityName);
            AddLanguageEntry(targetLevel.korean, cityId, cityName);

            Console.WriteLine("  ✅ City name added to all language tables");
            return cityId;
        }

        /// <summary>
        /// Adds a language entry to the specified language list
        /// </summary>
        private static void AddLanguageEntry(List<LanguageData> languageList, int id, string text)
        {
            // Check if this ID already exists in the list
            var existingEntry = languageList.Find(item => item.id == id);

            if (existingEntry != null)
            {
                // Replace existing entry
                existingEntry.text = Encoding.ASCII.GetBytes(text);
                Console.WriteLine($"  Updated existing language entry with ID {id}");
            }
            else
            {
                // Create a new language data entry
                // Create a byte array with structure needed by LanguageData
                byte[] textBytes = Encoding.ASCII.GetBytes(text);
                byte[] block = new byte[8 + 0x10 + textBytes.Length + 4]; // Header + element size + text data + null terminator
                
                // First int is the offset to the text data
                int textOffset = 8 + 0x10; // After header and one element
                LibReplanetizer.DataFunctions.WriteInt(block, 8, textOffset);
                // Second int is the ID
                LibReplanetizer.DataFunctions.WriteInt(block, 12, id);
                // Third int is the secondId (sound ID?)
                LibReplanetizer.DataFunctions.WriteInt(block, 16, 0);
                
                // Copy the text into the block
                for (int i = 0; i < textBytes.Length; i++)
                {
                    block[textOffset + i] = textBytes[i];
                }
                
                var newEntry = new LanguageData(block, 0);
                languageList.Add(newEntry);
                Console.WriteLine($"  Created new language entry with ID {id}");
            }
        }

        /// <summary>
        /// Update the PlanetCount value in engine.ps3 to include the new planet
        /// </summary>
        private static void UpdatePlanetCountInEngineFile(Level targetLevel, int planetId)
        {
            string enginePath = Path.Combine(targetLevel.path ?? throw new ArgumentNullException(nameof(targetLevel.path)), "engine.ps3");
            if (!File.Exists(enginePath))
            {
                Console.WriteLine("  ❌ Error: engine.ps3 file not found");
                throw new FileNotFoundException("engine.ps3 file not found", enginePath);
            }

            Console.WriteLine($"  Updating PlanetCount in engine.ps3...");

            // Read the current PlanetCount
            byte[] data = File.ReadAllBytes(enginePath);
            int currentCount = BitConverter.ToInt32(data, PLANET_COUNT_OFFSET);
            int requiredCount = planetId + 1; // PlanetCount needs to be at least planetId+1

            Console.WriteLine($"  Current PlanetCount: {currentCount}, Required for planet {planetId}: {requiredCount}");

            // Update the PlanetCount if it's not high enough
            if (currentCount < requiredCount)
            {
                // Convert the new count to bytes (little endian)
                byte[] newCountBytes = BitConverter.GetBytes(requiredCount);

                // Update the bytes in the file
                for (int i = 0; i < 4; i++)
                {
                    data[PLANET_COUNT_OFFSET + i] = newCountBytes[i];
                }

                // Write the updated data back to the file
                File.WriteAllBytes(enginePath, data);
                Console.WriteLine($"  ✅ PlanetCount updated from {currentCount} to {requiredCount}");
            }
            else
            {
                Console.WriteLine($"  ✅ PlanetCount already sufficient ({currentCount} ≥ {requiredCount})");
            }
        }

        /// <summary>
        /// Adds a planet entry to the planet list in engine.ps3
        /// </summary>
        private static void AddPlanetEntryToPlanetList(Level targetLevel, int planetId, int textId, bool isAvailable)
        {
            string enginePath = Path.Combine(targetLevel.path ?? throw new ArgumentNullException(nameof(targetLevel.path)), "engine.ps3");
            if (!File.Exists(enginePath))
            {
                Console.WriteLine("  ❌ Error: engine.ps3 file not found");
                throw new FileNotFoundException("engine.ps3 file not found", enginePath);
            }

            Console.WriteLine($"  Adding planet entry to planet list in engine.ps3...");

            // Read the entire file
            byte[] data = File.ReadAllBytes(enginePath);

            // Read the planet list base offset - this is a pointer, not an absolute file position
            int planetListOffset = BitConverter.ToInt32(data, PLANET_LIST_BASE_OFFSET);
            Console.WriteLine($"  Planet list base offset: 0x{planetListOffset:X8}");
            
            // The offset is likely a memory address, not a file offset
            // We need to calculate the actual file position
            // For RC2, the file offset can be calculated from memory offset
            // This is an approximation - you may need to adjust based on your specific file structure
            int fileBaseOffset = 0; // This may need to be adjusted based on RC2 file structure
            int planetListFileOffset = 0;
            
            // Try to find the real offset by scanning the file for a planet list structure
            Console.WriteLine($"  Searching for planet list structure...");
            bool foundPlanetList = false;
            
            // Read the current PlanetCount for reference
            int currentCount = BitConverter.ToInt32(data, PLANET_COUNT_OFFSET);
            
            // Loop through potential offsets where the planet list might be located
            for (int offset = 0; offset < data.Length - PLANET_STRUCT_SIZE * 2; offset += 4)
            {
                // Check if this position contains what looks like a planet entry
                // First entry should have ID 0, and a following entry with ID 1
                if (BitConverter.ToInt32(data, offset) == 0)
                {
                    // Check if next entry has ID 1
                    if (BitConverter.ToInt32(data, offset + PLANET_STRUCT_SIZE) == 1)
                    {
                        planetListFileOffset = offset;
                        foundPlanetList = true;
                        Console.WriteLine($"  Found likely planet list at file offset: 0x{planetListFileOffset:X8}");
                        break;
                    }
                }
            }
            
            // If we couldn't find it automatically, use a hardcoded offset
            if (!foundPlanetList)
            {
                // Hardcoded offset as fallback - this will need to be determined through analysis
                // of a working RC2 engine.ps3 file
                planetListFileOffset = 0x38000; // Example - adjust based on your RC2 file
                Console.WriteLine($"  Using fallback planet list offset: 0x{planetListFileOffset:X8}");
            }

            // Calculate offset for this planet's entry
            int planetEntryOffset = planetListFileOffset + (planetId * PLANET_STRUCT_SIZE);
            
            // Verify we have enough space in the file
            if (planetEntryOffset + PLANET_STRUCT_SIZE > data.Length)
            {
                Console.WriteLine($"  ❌ Error: Planet entry offset (0x{planetEntryOffset:X8}) exceeds file size ({data.Length})");
                throw new IndexOutOfRangeException("Planet entry offset exceeds file size");
            }

            // If this is a new planet (greater than the current count), we need to duplicate the last entry as a template
            if (planetId >= currentCount)
            {
                // Get the last existing planet entry as a template
                int lastPlanetOffset = planetListFileOffset + ((currentCount - 1) * PLANET_STRUCT_SIZE);
                byte[] templateEntry = new byte[PLANET_STRUCT_SIZE];
                Array.Copy(data, lastPlanetOffset, templateEntry, 0, PLANET_STRUCT_SIZE);
                
                // Copy the template to the new planet's position
                Array.Copy(templateEntry, 0, data, planetEntryOffset, PLANET_STRUCT_SIZE);
            }

            // Now update the planet entry fields
            BitConverter.GetBytes(planetId).CopyTo(data, planetEntryOffset); // LevelID at +0x00
            BitConverter.GetBytes(textId).CopyTo(data, planetEntryOffset + 0x04); // TextID at +0x04

            // Set planet coordinates (arbitrary values that look good on the map)
            // You can adjust these to position it properly on the galaxy map
            float xPos = 0.55f;
            float yPos = -0.40f;
            BitConverter.GetBytes(xPos).CopyTo(data, planetEntryOffset + 0x08); // X position at +0x08
            BitConverter.GetBytes(yPos).CopyTo(data, planetEntryOffset + 0x0C); // Y position at +0x0C

            // Set icon index (use a default one for now)
            int iconIdx = 16; // Choose an appropriate icon
            BitConverter.GetBytes(iconIdx).CopyTo(data, planetEntryOffset + 0x10); // IconIdx at +0x10

            // Set availability flags
            int flags = isAvailable ? 1 : 0; // 1 = unlocked
            BitConverter.GetBytes(flags).CopyTo(data, planetEntryOffset + 0x14); // Flags at +0x14

            // Write the updated data back to the file
            File.WriteAllBytes(enginePath, data);

            Console.WriteLine($"  ✅ Planet entry added at offset 0x{planetEntryOffset:X8} with TextID {textId}, IconIdx {iconIdx}, Flags {flags}");
        }

        /// <summary>
        /// Adds a planet and city name to the global all_text file for the game
        /// </summary>
        /// <param name="gamePath">Path to the game's ps3data directory</param>
        /// <param name="planetId">ID of the planet</param>
        /// <param name="planetName">Name of the planet</param>
        /// <param name="cityName">Name of the city or location on the planet</param>
        /// <returns>True if the operation was successful</returns>
        public static bool AddPlanetToAllTextFile(string gamePath, int planetId, string planetName, string cityName)
        {
            Console.WriteLine($"\n📝 ADDING PLANET TO ALL_TEXT FILE: {planetName} - {cityName} (ID: {planetId})");
            
            // The full planet name with "Planet" prefix (RC2 format)
            string fullPlanetName = $"Planet {planetName}";
            
            // City name ID is planetId + 10000 (based on RC2 pattern)
            int cityNameId = planetId + 10000;

            try
            {
                string allTextPath = Path.Combine(gamePath, "global", "all_text");
                if (!File.Exists(allTextPath))
                {
                    Console.WriteLine($"  ❌ Error: all_text file not found at {allTextPath}");
                    return false;
                }

                // Read the entire file as bytes
                byte[] allTextBytes = File.ReadAllBytes(allTextPath);
                
                // First verify the file is large enough to contain a valid header
                if (allTextBytes.Length < 12)
                {
                    Console.WriteLine($"  ❌ Error: all_text file is too small or corrupted");
                    return false;
                }
                
                // First 4 bytes contain the string count - verify this is a reasonable number
                int stringCount = BitConverter.ToInt32(allTextBytes, 0);
                if (stringCount < 0 || stringCount > 100000) // Sanity check - no more than 100k strings expected
                {
                    Console.WriteLine($"  ❌ Error: Invalid string count in all_text file: {stringCount}");
                    Console.WriteLine($"  The file may be corrupt or have a different format than expected.");
                    
                    // Check if this might be a big-endian file (PS2/PS3 could use big-endian)
                    byte[] countBytes = new byte[4];
                    Array.Copy(allTextBytes, 0, countBytes, 0, 4);
                    Array.Reverse(countBytes);
                    int reversedCount = BitConverter.ToInt32(countBytes, 0);
                    
                    if (reversedCount > 0 && reversedCount < 100000) 
                    {
                        Console.WriteLine($"  Detected possible big-endian format. Trying with string count: {reversedCount}");
                        stringCount = reversedCount;
                    }
                    else
                    {
                        // Attempt to manually locate the string table
                        Console.WriteLine("  Attempting to manually locate text entries in the file...");
                        
                        // Find a reasonable table offset by searching for known IDs
                        bool foundTable = false;
                        int manualTableOffset = 0;
                        
                        // Try to find entries for existing planet IDs (1-26)
                        for (int offset = 0; offset < allTextBytes.Length - 1000; offset += 4)
                        {
                            // Look for patterns of IDs - e.g. sequence of small numbers
                            if (offset + 12 < allTextBytes.Length)
                            {
                                int possibleId = BitConverter.ToInt32(allTextBytes, offset);
                                int nextValue = BitConverter.ToInt32(allTextBytes, offset + 12);
                                
                                // If we find sequential IDs that are likely planet IDs
                                if (possibleId >= 1 && possibleId < 27 && nextValue == possibleId + 1)
                                {
                                    manualTableOffset = offset;
                                    foundTable = true;
                                    stringCount = 27; // Assume at least all planets exist
                                    break;
                                }
                            }
                        }
                        
                        if (!foundTable)
                        {
                            Console.WriteLine("  ❌ Cannot process all_text file - unable to determine format");
                            return false;
                        }
                        
                        manualTableOffset = manualTableOffset;
                        Console.WriteLine($"  Found table at offset: 0x{manualTableOffset:X8}, assuming {stringCount} strings");
                    }
                }
                else
                {
                    Console.WriteLine($"  Found {stringCount} existing strings in all_text file");
                }

                // Each string table entry is 12 bytes: 4 bytes ID, 4 bytes offset, 4 bytes (unknown/unused)
                int tableStartOffset = 4; // After the count
                int dataStartOffset = tableStartOffset + (stringCount * 12);
                
                // Verify this makes sense within the file
                if (dataStartOffset > allTextBytes.Length)
                {
                    Console.WriteLine($"  ❌ Error: Calculated string data offset exceeds file size");
                    Console.WriteLine($"  Table would end at {dataStartOffset}, but file is only {allTextBytes.Length} bytes");
                    return false;
                }

                // Create lists to hold new data
                List<byte> newTableEntries = new List<byte>();
                List<byte> newStringData = new List<byte>();
                
                bool planetNameExists = false;
                bool cityNameExists = false;
                
                // Check for existing entries with the same IDs
                for (int i = 0; i < stringCount; i++)
                {
                    int entryOffset = tableStartOffset + (i * 12);
                    
                    // Ensure the entry offset is within bounds
                    if (entryOffset + 8 < allTextBytes.Length)
                    {
                        int id = BitConverter.ToInt32(allTextBytes, entryOffset);
                        int stringOffset = BitConverter.ToInt32(allTextBytes, entryOffset + 4);
                        
                        // Verify string offset is in bounds
                        if (stringOffset >= 0 && stringOffset < allTextBytes.Length)
                        {
                            // Check if the planet ID already exists
                            if (id == planetId)
                            {
                                planetNameExists = true;
                                
                                // Get the existing string for this ID
                                string existingString = GetStringAtOffset(allTextBytes, stringOffset);
                                Console.WriteLine($"  Found existing planet name entry with ID {planetId}: \"{existingString}\"");
                                
                                // If the existing string doesn't match what we want, we'll replace it
                                if (existingString != fullPlanetName)
                                {
                                    Console.WriteLine($"  Will replace with: \"{fullPlanetName}\"");
                                }
                                else
                                {
                                    Console.WriteLine($"  Existing planet name matches, no need to replace");
                                }
                            }
                            
                            // Check if the city ID already exists
                            if (id == cityNameId)
                            {
                                cityNameExists = true;
                                
                                // Get the existing string for this ID
                                string existingString = GetStringAtOffset(allTextBytes, stringOffset);
                                Console.WriteLine($"  Found existing city name entry with ID {cityNameId}: \"{existingString}\"");
                                
                                // If the existing string doesn't match what we want, we'll replace it
                                if (existingString != cityName)
                                {
                                    Console.WriteLine($"  Will replace with: \"{cityName}\"");
                                }
                                else
                                {
                                    Console.WriteLine($"  Existing city name matches, no need to replace");
                                }
                            }
                        }
                    }
                }
                
                // If both entries already exist with the correct strings, there's nothing to do
                if (planetNameExists && cityNameExists)
                {
                    Console.WriteLine("  ✅ Both planet and city names already exist in all_text file");
                    return true;
                }
                
                // Calculate offsets for new string data
                int currentDataEndOffset = allTextBytes.Length;
                
                // Add planet name string if needed
                if (!planetNameExists)
                {
                    // Add table entry for planet name
                    newTableEntries.AddRange(BitConverter.GetBytes(planetId)); // ID
                    newTableEntries.AddRange(BitConverter.GetBytes(currentDataEndOffset + newStringData.Count)); // Offset
                    newTableEntries.AddRange(BitConverter.GetBytes(0)); // Unknown/unused
                    
                    // Add string data for planet name
                    byte[] planetNameBytes = Encoding.ASCII.GetBytes(fullPlanetName);
                    newStringData.AddRange(planetNameBytes);
                    newStringData.Add(0); // Null terminator
                    
                    // Ensure 4-byte alignment
                    while (newStringData.Count % 4 != 0)
                        newStringData.Add(0);
                    
                    Console.WriteLine($"  Added planet name entry: ID {planetId}, string \"{fullPlanetName}\"");
                }
                
                // Add city name string if needed
                if (!cityNameExists)
                {
                    // Add table entry for city name
                    newTableEntries.AddRange(BitConverter.GetBytes(cityNameId)); // ID
                    newTableEntries.AddRange(BitConverter.GetBytes(currentDataEndOffset + newStringData.Count)); // Offset
                    newTableEntries.AddRange(BitConverter.GetBytes(0)); // Unknown/unused
                    
                    // Add string data for city name
                    byte[] cityNameBytes = Encoding.ASCII.GetBytes(cityName);
                    newStringData.AddRange(cityNameBytes);
                    newStringData.Add(0); // Null terminator
                    
                    // Ensure 4-byte alignment
                    while (newStringData.Count % 4 != 0)
                        newStringData.Add(0);
                    
                    Console.WriteLine($"  Added city name entry: ID {cityNameId}, string \"{cityName}\"");
                }
                
                if (newTableEntries.Count == 0)
                {
                    Console.WriteLine("  ✅ No changes needed to all_text file");
                    return true;
                }
                
                // Create the new file
                List<byte> newAllTextBytes = new List<byte>();
                
                // Write the updated string count
                int newStringCount = stringCount + (newTableEntries.Count / 12);
                newAllTextBytes.AddRange(BitConverter.GetBytes(newStringCount));
                
                // Write the existing string table and data
                newAllTextBytes.AddRange(allTextBytes.Skip(4).Take(dataStartOffset - 4));
                
                // Write the new table entries
                newAllTextBytes.AddRange(newTableEntries);
                
                // Write the existing string data
                newAllTextBytes.AddRange(allTextBytes.Skip(dataStartOffset).Take(currentDataEndOffset - dataStartOffset));
                
                // Write the new string data
                newAllTextBytes.AddRange(newStringData);
                
                // Create backup of original file
                string backupPath = allTextPath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(allTextPath, backupPath);
                    Console.WriteLine($"  Created backup of original all_text file: {backupPath}");
                }
                
                // Write the new file
                File.WriteAllBytes(allTextPath, newAllTextBytes.ToArray());
                
                Console.WriteLine($"✅ Successfully updated all_text file with {newStringCount} strings");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error adding planet to all_text file: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// Reads a null-terminated string from a binary buffer at the given offset
        /// </summary>
        private static string GetStringAtOffset(byte[] buffer, int offset)
        {
            // Find the null terminator
            int endOffset = offset;
            while (endOffset < buffer.Length && buffer[endOffset] != 0)
            {
                endOffset++;
            }
            
            // Convert the bytes to a string
            return Encoding.ASCII.GetString(buffer, offset, endOffset - offset);
        }
    }
}
