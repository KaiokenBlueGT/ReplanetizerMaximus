// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using OpenTK.Mathematics;
using LibReplanetizer.Models;

namespace LibReplanetizer.LevelObjects
{
    /// <summary>
    /// Wrapper around a collision model that allows it to behave like any other
    /// level object. Transformations are applied directly to the underlying
    /// vertex buffer so that changes persist when the level is saved.
    /// </summary>
    public class CollisionObject : LevelObject, IRenderable
    {
        /// <summary>Underlying collision geometry.</summary>
        public Collision model { get; set; }

        private const int STRIDE = 4; // x, y, z, color

        // Flag indicating that the GPU buffers need updating
        public bool dirty = true;

        public CollisionObject(Collision model)
        {
            this.model = model;
            modelMatrix = Matrix4.Identity;
            // Store the centroid as the initial position for selection purposes
            position = ComputeCentroid();
        }

        private Vector3 ComputeCentroid()
        {
            if (model.vertexBuffer.Length == 0) return Vector3.Zero;
            Vector3 sum = Vector3.Zero;
            int count = model.vertexBuffer.Length / STRIDE;
            for (int i = 0; i < count; i++)
            {
                sum.X += model.vertexBuffer[i * STRIDE + 0];
                sum.Y += model.vertexBuffer[i * STRIDE + 1];
                sum.Z += model.vertexBuffer[i * STRIDE + 2];
            }
            return sum / count;
        }

        /// <summary>
        /// Apply a transformation matrix to the collision geometry. The model
        /// matrix of the object is reset to identity afterwards to avoid double
        /// transformations.
        /// </summary>
        public override void SetFromMatrix(Matrix4 mat)
        {
            int count = model.vertexBuffer.Length / STRIDE;
            for (int i = 0; i < count; i++)
            {
                Vector4 v = new Vector4(
                    model.vertexBuffer[i * STRIDE + 0],
                    model.vertexBuffer[i * STRIDE + 1],
                    model.vertexBuffer[i * STRIDE + 2],
                    1.0f);

                v = mat * v;
                model.vertexBuffer[i * STRIDE + 0] = v.X;
                model.vertexBuffer[i * STRIDE + 1] = v.Y;
                model.vertexBuffer[i * STRIDE + 2] = v.Z;
            }

            // Update selection position to the new centroid
            position = ComputeCentroid();
            rotation = Quaternion.Identity;
            scale = Vector3.One;
            modelMatrix = Matrix4.Identity;
            dirty = true;
        }

        public override void UpdateTransformMatrix()
        {
            // Collision vertices are stored in world space, so the model matrix
            // remains identity. Position is derived from the centroid.
            modelMatrix = Matrix4.Identity;
        }

        public override LevelObject Clone()
        {
            return new CollisionObject(model.Clone());
        }

        public override byte[] ToByteArray()
        {
            // Collision serialization is handled elsewhere; this object does not
            // produce standalone byte data.
            return Array.Empty<byte>();
        }


        /// <summary>
        /// Translate the collision geometry by a vector.
        /// </summary>
        public override void Translate(Vector3 vector)
        {
            int count = model.vertexBuffer.Length / STRIDE;
            for (int i = 0; i < count; i++)
            {
                model.vertexBuffer[i * STRIDE + 0] += vector.X;
                model.vertexBuffer[i * STRIDE + 1] += vector.Y;
                model.vertexBuffer[i * STRIDE + 2] += vector.Z;
            }
            position = ComputeCentroid();
            dirty = true;
        }

        /// <summary>
        /// Scale the collision geometry by a vector (relative to centroid).
        /// </summary>
        public override void Scale(Vector3 scale)
        {
            Vector3 centroid = ComputeCentroid();
            int count = model.vertexBuffer.Length / STRIDE;
            for (int i = 0; i < count; i++)
            {
                float x = model.vertexBuffer[i * STRIDE + 0] - centroid.X;
                float y = model.vertexBuffer[i * STRIDE + 1] - centroid.Y;
                float z = model.vertexBuffer[i * STRIDE + 2] - centroid.Z;
                model.vertexBuffer[i * STRIDE + 0] = centroid.X + x * scale.X;
                model.vertexBuffer[i * STRIDE + 1] = centroid.Y + y * scale.Y;
                model.vertexBuffer[i * STRIDE + 2] = centroid.Z + z * scale.Z;
            }
            position = ComputeCentroid();
            dirty = true;
        }

        /// <summary>
        /// Rotate the collision geometry by a vector (Euler angles, relative to centroid).
        /// </summary>
        public override void Rotate(Vector3 euler)
        {
            Vector3 centroid = ComputeCentroid();
            Matrix3 rotMat = Matrix3.CreateFromQuaternion(Quaternion.FromEulerAngles(euler));
            int count = model.vertexBuffer.Length / STRIDE;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = new Vector3(
                    model.vertexBuffer[i * STRIDE + 0] - centroid.X,
                    model.vertexBuffer[i * STRIDE + 1] - centroid.Y,
                    model.vertexBuffer[i * STRIDE + 2] - centroid.Z);
                v = rotMat * v;
                model.vertexBuffer[i * STRIDE + 0] = centroid.X + v.X;
                model.vertexBuffer[i * STRIDE + 1] = centroid.Y + v.Y;
                model.vertexBuffer[i * STRIDE + 2] = centroid.Z + v.Z;
            }
            position = ComputeCentroid();
            dirty = true;
        }

        // Implement IRenderable by delegating to the underlying model
        public ushort[] GetIndices() => model.GetIndices();
        public float[] GetVertices() => model.GetVertices();
        public bool IsDynamic() => model.IsDynamic();
    }
}

