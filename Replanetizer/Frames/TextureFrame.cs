// Copyright (C) 2018-2021, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.IO;
using ImGuiNET;
using LibReplanetizer;
using Replanetizer.Renderer;
using Replanetizer.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;

namespace Replanetizer.Frames
{
    public class TextureFrame : LevelSubFrame
    {
        private static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();
        
        protected sealed override string frameName { get; set; } = "Textures";
        private Level level => levelFrame.level;
        private static Vector2 IMAGE_SIZE = new(64, 64);
        private static Vector2 ITEM_SIZE = new(64, 84);
        private float itemSizeX;

        public TextureFrame(Window wnd, LevelFrame levelFrame) : base(wnd, levelFrame)
        {
            itemSizeX = IMAGE_SIZE.X + ImGui.GetStyle().ItemSpacing.X;
        }

        // Change RenderTextureList to an instance method
        public void RenderTextureList(List<Texture> textures, float itemSizeX, Dictionary<Texture, GLTexture> textureIds, string prefix = "", int additionalOffset = 0)
        {
            var width = ImGui.GetContentRegionAvail().X - additionalOffset;
            var itemsPerRow = (int) Math.Floor(width / itemSizeX);

            if (itemsPerRow == 0) return;

            int i = 0;
            while (i < textures.Count)
            {
                Texture t = textures[i];

                ImGui.BeginChild("imageChild_" + prefix + i, ITEM_SIZE, ImGuiChildFlags.None);
                
                // 🔧 ADD BETTER TEXTURE VALIDATION
                if (textureIds.ContainsKey(t) && textureIds[t] != null)
                {
                    try
                    {
                        ImGui.Image((IntPtr) textureIds[t].textureID, IMAGE_SIZE);
                    }
                    catch (Exception)
                    {
                        // Fallback: Show a placeholder or error indicator
                        ImGui.Text("⚠️");
                        ImGui.Text("BAD");
                    }
                }
                else
                {
                    // Show placeholder for missing textures
                    ImGui.Text("❌");
                    ImGui.Text("MISS");
                }
                
                string idText = prefix + t.id;
                float idWidth = ImGui.CalcTextSize(idText).X;
                ImGui.SetCursorPosX(ITEM_SIZE.X - idWidth);
                ImGui.Text(idText);
                ImGui.EndChild();

                if (ImGui.BeginPopupContextItem($"context-menu for {i}"))
                {
                    if (ImGui.Button("Export"))
                    {
                        var targetFile = CrossFileDialog.SaveFile(filter: ".bmp;.jpg;.jpeg;.png");
                        if (targetFile.Length > 0)
                        {
                            TextureIO.ExportTexture(t, targetFile, true);
                        }
                    }
                    
                    // 🆕 ADD REPLACE OPTION
                    if (ImGui.Button("Replace"))
                    {
                        var sourceFile = CrossFileDialog.OpenFile(filter: ".bmp;.jpg;.jpeg;.png");
                        if (sourceFile.Length > 0)
                        {
                            try
                            {
                                // Load the image using SixLabors.ImageSharp (same as ObjTerrainImporter)
                                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(sourceFile);
                                
                                Console.WriteLine($"Loaded replacement image: {image.Width}x{image.Height}");
                                
                                // 🔧 FIX: Resize to conform to power-of-2 uniform sizes (256x256, 128x128, 64x64)
                                int targetSize;
                                int maxDimension = Math.Max(image.Width, image.Height);
                                
                                if (maxDimension > 128)
                                {
                                    targetSize = 256;
                                }
                                else if (maxDimension > 64)
                                {
                                    targetSize = 128;
                                }
                                else
                                {
                                    targetSize = 64;
                                }
                                
                                // Always resize to ensure uniform dimensions
                                if (image.Width != targetSize || image.Height != targetSize)
                                {
                                    Console.WriteLine($"Resizing image from {image.Width}x{image.Height} to {targetSize}x{targetSize}...");
                                    image.Mutate(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                                    {
                                        Size = new SixLabors.ImageSharp.Size(targetSize, targetSize),
                                        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Stretch // Force square dimensions
                                    }));
                                    Console.WriteLine($"Resized to: {image.Width}x{image.Height}");
                                }
                                else
                                {
                                    Console.WriteLine($"Image is already the correct size: {targetSize}x{targetSize}");
                                }
                                
                                // Generate DXT5 compressed data with mipmaps (same method as ObjTerrainImporter)
                                short mipCount;
                                byte[] dxtData = GenerateMipChainForTexture(image, out mipCount);
                                
                                // Create new texture with the same ID but new data
                                var newTexture = new Texture(t.id, (short)image.Width, (short)image.Height, dxtData);
                                
                                // 🔧 FIX: Set mipmap count BEFORE copying other properties
                                newTexture.mipMapCount = mipCount;
                                
                                // Copy other properties from original texture (but keep new dimensions and data)
                                newTexture.off06 = t.off06;
                                newTexture.off08 = t.off08;
                                newTexture.off0C = t.off0C;
                                newTexture.off10 = t.off10;
                                newTexture.off14 = t.off14;
                                newTexture.off1C = t.off1C;
                                newTexture.off20 = t.off20;
                                newTexture.vramPointer = t.vramPointer;
                                
                                // Replace in the texture list
                                textures[i] = newTexture;
                                
                                // 🔧 FIX: Update GL texture more carefully
                                // Remove the old GLTexture first
                                if (levelFrame.textureIds.ContainsKey(t))
                                {
                                    levelFrame.textureIds[t].Dispose(); // Dispose old GL texture
                                    levelFrame.textureIds.Remove(t);
                                }
                                
                                // Create new GLTexture and add it to the dictionary
                                try
                                {
                                    var newGLTexture = new GLTexture(newTexture);
                                    levelFrame.textureIds[newTexture] = newGLTexture;
                                    
                                    Console.WriteLine($"✅ Successfully replaced texture {t.id} with {Path.GetFileName(sourceFile)}");
                                    Console.WriteLine($"   New size: {newTexture.width}x{newTexture.height}, Data: {newTexture.data?.Length ?? 0} bytes, Mipmaps: {newTexture.mipMapCount}");
                                }
                                catch (Exception glEx)
                                {
                                    Console.WriteLine($"⚠️ Texture replaced but GL texture creation failed: {glEx.Message}");
                                    // Still show success as the texture data was replaced
                                }
                                
                                ImGui.CloseCurrentPopup();
                                return;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Failed to replace texture: {ex.Message}");
                                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
                            }
                        }
                    }
                    
                    // 🆕 ADD DEBUG INFO
                    if (ImGui.Button("Debug Info"))
                    {
                        LOGGER.Info($"Texture {t.id}: {t.width}x{t.height}, Data: {t.data?.Length ?? 0} bytes");
                        LOGGER.Info($"  Has GL Texture: {textureIds.ContainsKey(t)}");
                        if (textureIds.ContainsKey(t))
                        {
                            LOGGER.Info($"  GL Texture ID: {textureIds[t]?.textureID ?? 0}");
                        }
                    }
                    // 🆕 ADD DELETE OPTION WITH USER CHOICE
                    if (ImGui.Button("Delete"))
                    {
                        // Show deletion method popup
                        ImGui.OpenPopup("Delete Texture Options");
                    }
                    
                    // Handle the deletion method selection popup
                    if (ImGui.BeginPopupModal("Delete Texture Options", ImGuiWindowFlags.AlwaysAutoResize))
                    {
                        ImGui.Text($"How should Texture ID {t.id} be deleted?");
                        ImGui.Separator();
                        
                        ImGui.TextWrapped("Method 1: Compact deletion (shifts IDs)");
                        ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), "✓ Good for: OBJ imported terrains, custom levels");
                        ImGui.TextColored(new Vector4(0.8f, 0.6f, 0.6f, 1.0f), "✗ Bad for: Vanilla levels (breaks texture references)");
                        
                        if (ImGui.Button("Compact Deletion##compact", new Vector2(150, 0)))
                        {
                            // Original method: Remove and shift all subsequent IDs down
                            textures.RemoveAt(i);
                            for (int j = i; j < textures.Count; j++)
                            {
                                textures[j].id = j;
                            }
                            
                            // Remap texture indices after deletion to fix broken references
                            levelFrame.RemapTextureIndices(textures);
                            
                            ImGui.CloseCurrentPopup();
                            ImGui.CloseCurrentPopup(); // Close the context menu too
                            return;
                        }
                        
                        ImGui.Spacing();
                        ImGui.TextWrapped("Method 2: Gap deletion (preserves IDs)");
                        ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1.0f), "✓ Good for: Vanilla levels (preserves texture references)");
                        ImGui.TextColored(new Vector4(0.8f, 0.6f, 0.6f, 1.0f), "✗ Bad for: Creates gaps in texture list");
                        
                        if (ImGui.Button("Gap Deletion##gap", new Vector2(150, 0)))
                        {
                            // New method: Replace with placeholder, don't shift IDs
                            // Create a small placeholder texture (1x1 magenta for debugging)
                            byte[] placeholderData = new byte[] { 255, 0, 255, 255 }; // Magenta RGBA
                            var placeholderTexture = new Texture(t.id, 1, 1, placeholderData);
                            textures[i] = placeholderTexture;
                            
                            // Update the GL texture
                            if (levelFrame.textureIds.ContainsKey(t))
                            {
                                levelFrame.textureIds[t].Dispose();
                                levelFrame.textureIds.Remove(t);
                            }
                            levelFrame.textureIds[placeholderTexture] = new GLTexture(placeholderTexture);
                            
                            LOGGER.Info($"Texture {t.id} replaced with placeholder (no ID shifting)");
                            

                            ImGui.CloseCurrentPopup();
                            ImGui.CloseCurrentPopup(); // Close the context menu too
                            return;
                        }
                        
                        ImGui.Spacing();
                        if (ImGui.Button("Cancel", new Vector2(150, 0)))
                        {
                            ImGui.CloseCurrentPopup();
                        }
                        
                        ImGui.EndPopup();
                    }
                    ImGui.EndPopup();
                }
                else if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    
                    // 🔧 ADD VALIDATION HERE TOO
                    if (textureIds.ContainsKey(t) && textureIds[t] != null)
                    {
                        try
                        {
                            ImGui.Image((IntPtr) textureIds[t].textureID, new System.Numerics.Vector2(t.width, t.height));
                        }
                        catch (Exception)
                        {
                            ImGui.Text("❌ Failed to display texture");
                        }
                    }
                    else
                    {
                        ImGui.Text("❌ Texture not loaded");
                    }
                    
                    string resolutionText = $"{t.width}x{t.height}";
                    float resolutionWidth = ImGui.CalcTextSize(resolutionText).X;
                    ImGui.SetCursorPosX(t.width - resolutionWidth);
                    ImGui.Text(resolutionText);
                    ImGui.EndTooltip();
                }

                i++;

                if ((i % itemsPerRow) != 0)
                {
                    ImGui.SameLine();
                }
            }

            ImGui.NewLine();
        }

        public override void RenderAsWindow(float deltaTime)
        {
            if (ImGui.Begin(frameName, ref isOpen, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                Render(deltaTime);
                ImGui.End();
            }
        }

        public override void Render(float deltaTime)
        {
            if (ImGui.CollapsingHeader("Level textures"))
            {
                RenderTextureList(level.textures, itemSizeX, levelFrame.textureIds);
            }
            if (ImGui.CollapsingHeader("Gadget textures"))
            {
                RenderTextureList(level.gadgetTextures, itemSizeX, levelFrame.textureIds);
            }
            if (ImGui.CollapsingHeader("Armor textures"))
            {
                for (int i = 0; i < level.armorTextures.Count; i++)
                {
                    List<Texture> textureList = level.armorTextures[i];
                    if (ImGui.TreeNode("Armor " + i))
                    {
                        RenderTextureList(textureList, itemSizeX, levelFrame.textureIds);
                        ImGui.TreePop();
                    }
                }
            }
            if (ImGui.CollapsingHeader("Mission textures"))
            {
                foreach (Mission mission in level.missions)
                {
                    if (ImGui.TreeNode("Mission " + mission.missionID))
                    {
                        RenderTextureList(mission.textures, itemSizeX, levelFrame.textureIds);
                        ImGui.TreePop();
                    }
                }
            }
            if (ImGui.CollapsingHeader("Mobyload textures"))
            {
                for (int i = 0; i < level.mobyloadTextures.Count; i++)
                {
                    List<Texture> textureList = level.mobyloadTextures[i];
                    if (textureList.Count > 0)
                    {
                        if (ImGui.TreeNode("Mobyload " + i))
                        {
                            RenderTextureList(textureList, itemSizeX, levelFrame.textureIds);
                            ImGui.TreePop();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Generates a DXT5 compressed mip chain from an image (borrowed from ObjTerrainImporter)
        /// </summary>
        private static byte[] GenerateMipChainForTexture(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Bgra32> baseImage, out short mipCount)
        {
            var encoder = new BcEncoder();
            encoder.OutputOptions.Quality = CompressionQuality.Balanced;
            encoder.OutputOptions.Format = CompressionFormat.Bc3; // DXT5

            var mipmaps = new List<byte[]>();
            mipCount = 0;

            using (var current = baseImage.Clone())
            {
                while (true)
                {
                    var dxtBytes = encoder.EncodeToRawBytes(ConvertToRgba32Memory2D(current));
                    mipmaps.Add(dxtBytes[0]);
                    mipCount++;
                    
                    LOGGER.Debug($"Generated mip level {mipCount - 1}: {current.Width}x{current.Height}");

                    if (current.Width <= 1 && current.Height <= 1)
                    {
                        break; // Exit after processing the 1x1 mip level
                    }

                    int nextWidth = Math.Max(1, current.Width / 2);
                    int nextHeight = Math.Max(1, current.Height / 2);
                    current.Mutate(x => x.Resize(nextWidth, nextHeight));
                }
            }

            // Combine all mipmap levels into a single byte array
            int totalSize = mipmaps.Sum(m => m.Length);
            byte[] packedData = new byte[totalSize];
            int offset = 0;
            foreach (var mip in mipmaps)
            {
                System.Buffer.BlockCopy(mip, 0, packedData, offset, mip.Length);
                offset += mip.Length;
            }

            return packedData;
        }

        /// <summary>
        /// Converts an ImageSharp BGRA32 image to the format needed by BCnEncoder
        /// </summary>
        private static ReadOnlyMemory2D<ColorRgba32> ConvertToRgba32Memory2D(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Bgra32> image)
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
    }
}
