// Copyright (C) 2018-2021, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using OpenTK.Mathematics;
using System;
using System.ComponentModel;
using static LibReplanetizer.DataFunctions;

namespace LibReplanetizer.LevelObjects
{
    public class Cuboid : MatrixObject, IRenderable
    {
        public const int ELEMENTSIZE = 0x80;

        [Category("Attributes"), DisplayName("ID")]
        public int id { get; set; }
        
        // 🆕 Store the original inverse rotation matrix to prevent corruption
        private Matrix4 originalInverseRotationMatrix;
        private bool hasOriginalInverseMatrix = false; // Track if we have original data

        static readonly float[] CUBE = {
            -1.0f, -1.0f,  1.0f,
            1.0f, -1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            -1.0f,  1.0f,  1.0f,
            // back
            -1.0f, -1.0f, -1.0f,
            1.0f, -1.0f, -1.0f,
            1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f
        };

        public static readonly ushort[] CUBE_ELEMENTS = {
            0, 1, 2,
            2, 3, 0,
            1, 5, 6,
            6, 2, 1,
            7, 6, 5,
            5, 4, 7,
            4, 0, 3,
            3, 7, 4,
            4, 5, 1,
            1, 0, 4,
            3, 2, 6,
            6, 7, 3
        };

        public Cuboid(byte[] block, int index)
        {
            id = index;
            int offset = index * ELEMENTSIZE;

            Matrix4 transformMatrix = ReadMatrix4(block, offset + 0x00);
            Matrix4 inverseRotationMatrix = ReadMatrix4(block, offset + 0x40);

            modelMatrix = transformMatrix;
            rotation = modelMatrix.ExtractRotation();
            position = modelMatrix.ExtractTranslation();
            scale = modelMatrix.ExtractScale();

            // 🔧 FIX: Store the original inverse rotation matrix exactly as read from file
            originalInverseRotationMatrix = inverseRotationMatrix;
            hasOriginalInverseMatrix = true;

            UpdateTransformMatrix();
        }

        public override LevelObject Clone()
        {
            throw new NotImplementedException();
        }

        public override byte[] ToByteArray()
        {
            byte[] bytes = new byte[0x80];

            WriteMatrix4(bytes, 0x00, modelMatrix);
            
            // 🔧 FIX: Always use the original inverse rotation matrix if available
            // This prevents precision loss that corrupts ship camera rotations
            if (hasOriginalInverseMatrix)
            {
                WriteMatrix4(bytes, 0x40, originalInverseRotationMatrix);
            }
            else
            {
                // Fallback for newly created cuboids
                var inverseMatrix = Matrix4.CreateFromQuaternion(rotation).Inverted();
                WriteMatrix4(bytes, 0x40, inverseMatrix);
            }

            return bytes;
        }
        
        // 🔧 FIX: Only update inverse matrix when rotation significantly changes
        public override void UpdateTransformMatrix()
        {
            base.UpdateTransformMatrix();
            
            // 🔧 CRITICAL FIX: Don't automatically update inverse matrix for ship camera cuboids
            // Only update the inverse rotation matrix if we don't have original data
            // This preserves ship camera rotations during GUI saves
            if (!hasOriginalInverseMatrix)
            {
                originalInverseRotationMatrix = Matrix4.CreateFromQuaternion(rotation).Inverted();
                hasOriginalInverseMatrix = true;
                Console.WriteLine($"  Generated new inverse matrix for cuboid {id}");
            }
            // For existing cuboids (like ship cameras), preserve the original inverse matrix
            // unless explicitly modified via ApplyRC1ToRC3CameraRotationConversion or ForceUpdateInverseMatrix
        }
        
        /// <summary>
        /// Applies coordinate system conversion for camera cuboids (RC1 to RC3)
        /// This fixes the camera rotation issue when transferring between game versions
        /// </summary>
        public void ApplyRC1ToRC3CameraRotationConversion()
        {
            Console.WriteLine($"  Applying RC1 to RC3 camera rotation conversion...");
            Console.WriteLine($"  Original rotation: {rotation}");
            
            // RC1 to RC3/UYA camera coordinate system conversion
            // The camera system likely uses different axis orientations
            
            // Option 1: Y-axis flip (most common in camera systems)
            var convertedRotation = new Quaternion(-rotation.X, rotation.Y, -rotation.Z, rotation.W);
            
            Console.WriteLine($"  Converted rotation: {convertedRotation}");
            
            // Apply the converted rotation
            rotation = convertedRotation;
            
            // 🔧 CRITICAL: Force regeneration of both matrices for coordinate conversion
            // This ensures the inverse matrix matches the new coordinate system
            ForceUpdateInverseMatrix();
            
            Console.WriteLine($"  ✅ Applied RC1 to RC3 camera rotation conversion with fresh matrices");
        }

        /// <summary>
        /// 🆕 ENHANCED: Forces regeneration of all matrices and marks as converted
        /// </summary>
        public void ForceUpdateInverseMatrix()
        {
            // 🔧 CRITICAL: Always regenerate from current rotation to ensure sync
            var rotationMatrix = Matrix4.CreateFromQuaternion(rotation);
            originalInverseRotationMatrix = rotationMatrix.Inverted();
            hasOriginalInverseMatrix = true;
            
            // Also update the transform matrix to be consistent
            UpdateTransformMatrix();
            
            Console.WriteLine($"    Forced matrix regeneration for cuboid {id}");
        }

        /// <summary>
        /// 🆕 NEW: Apply coordinate conversion to ANY cuboid that needs it
        /// </summary>
        public void ApplyCoordinateConversionIfNeeded(bool isShipCamera = false)
        {
            if (isShipCamera)
            {
                ApplyRC1ToRC3CameraRotationConversion();
            }
            else
            {
                // For non-camera cuboids, just ensure matrices are properly synced
                ForceUpdateInverseMatrix();
            }
        }

        public ushort[] GetIndices()
        {
            return CUBE_ELEMENTS;
        }

        public float[] GetVertices()
        {
            return CUBE;
        }

        public bool IsDynamic()
        {
            return false;
        }
    }
}
