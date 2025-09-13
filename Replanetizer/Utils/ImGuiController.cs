// Copyright (C) 2018-2021, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Replanetizer.Renderer;

namespace Replanetizer.Utils
{
    /// <summary>
    /// A modified version of Veldrid.ImGui's ImGuiRenderer.
    /// Manages input for ImGui and handles rendering ImGui's DrawLists with Veldrid.
    /// </summary>
    public class ImGuiController : IDisposable
    {
        private static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();

        private bool frameBegun;

        private int vertexArray;
        private int vertexBuffer;
        private int vertexBufferSize;
        private int indexBuffer;
        private int indexBufferSize;

        private GLTexture? fontGlTexture;
        private Shader? shader;

        private int windowWidth;
        private int windowHeight;

        private System.Numerics.Vector2 scaleFactor = System.Numerics.Vector2.One;

        /// <summary>
        /// Constructs a new ImGuiController.
        /// </summary>
        public ImGuiController(int width, int height)
        {
            windowWidth = width;
            windowHeight = height;

            IntPtr context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);
            var io = ImGui.GetIO();
            io.Fonts.AddFontDefault();

            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

            CreateDeviceResources();

            SetPerFrameImGuiData(1f / 60f);

            ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;

            ImGui.NewFrame();
            frameBegun = true;
        }

        public void WindowResized(int width, int height)
        {
            windowWidth = width;
            windowHeight = height;
        }

        public void DestroyDeviceObjects()
        {
            Dispose();
        }

        public void CreateDeviceResources()
        {
            GLUtil.CreateVertexArray("ImGui", out vertexArray);

            vertexBufferSize = 10000;
            indexBufferSize = 2000;

            GLUtil.CreateVertexBuffer("ImGui", out vertexBuffer);
            GLUtil.CreateElementBuffer("ImGui", out indexBuffer);
            GL.NamedBufferData(vertexBuffer, vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.NamedBufferData(indexBuffer, indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

            RecreateFontDeviceTexture();

            string vertexSource = @"#version 330 core

uniform mat4 worldToView;

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_texCoord;
layout(location = 2) in vec4 in_color;

out vec4 color;
out vec2 texCoord;

void main()
{
    gl_Position = worldToView * vec4(in_position, 0, 1);
    color = in_color;
    texCoord = in_texCoord;
}";
            string fragmentSource = @"#version 330 core

uniform sampler2D fontTexture;

in vec4 color;
in vec2 texCoord;

out vec4 outputColor;

void main()
{
    outputColor = color * texture(fontTexture, texCoord);
}";
            shader = new Shader("ImGui", vertexSource, fragmentSource);

            GL.VertexArrayVertexBuffer(vertexArray, 0, vertexBuffer, IntPtr.Zero, Unsafe.SizeOf<ImDrawVert>());
            GL.VertexArrayElementBuffer(vertexArray, indexBuffer);

            GL.EnableVertexArrayAttrib(vertexArray, 0);
            GL.VertexArrayAttribBinding(vertexArray, 0, 0);
            GL.VertexArrayAttribFormat(vertexArray, 0, 2, VertexAttribType.Float, false, 0);

            GL.EnableVertexArrayAttrib(vertexArray, 1);
            GL.VertexArrayAttribBinding(vertexArray, 1, 0);
            GL.VertexArrayAttribFormat(vertexArray, 1, 2, VertexAttribType.Float, false, 8);

            GL.EnableVertexArrayAttrib(vertexArray, 2);
            GL.VertexArrayAttribBinding(vertexArray, 2, 0);
            GL.VertexArrayAttribFormat(vertexArray, 2, 4, VertexAttribType.UnsignedByte, true, 16);

            GLUtil.CheckGlError("End of ImGui setup");
        }

        /// <summary>
        /// Recreates the device texture used to render text.
        /// </summary>
        public void RecreateFontDeviceTexture()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

            fontGlTexture = new GLTexture("ImGui Text Atlas", width, height, pixels);
            fontGlTexture.SetMagFilter(TextureMagFilter.Linear);
            fontGlTexture.SetMinFilter(TextureMinFilter.Linear);

            io.Fonts.SetTexID((IntPtr) fontGlTexture.textureID);
            io.Fonts.ClearTexData();
        }

        /// <summary>
        /// Renders the ImGui draw list data.
        /// This method requires a <see cref="GraphicsDevice"/> because it may create new DeviceBuffers if the size of vertex
        /// or index data has increased beyond the capacity of the existing buffers.
        /// A <see cref="CommandList"/> is needed to submit drawing and resource update commands.
        /// </summary>
        public void Render()
        {
            if (frameBegun)
            {
                frameBegun = false;

                GL.VertexArrayVertexBuffer(vertexArray, 0, vertexBuffer, IntPtr.Zero, Unsafe.SizeOf<ImDrawVert>());
                GL.VertexArrayElementBuffer(vertexArray, indexBuffer);

                GL.EnableVertexArrayAttrib(vertexArray, 0);
                GL.VertexArrayAttribBinding(vertexArray, 0, 0);
                GL.VertexArrayAttribFormat(vertexArray, 0, 2, VertexAttribType.Float, false, 0);

                GL.EnableVertexArrayAttrib(vertexArray, 1);
                GL.VertexArrayAttribBinding(vertexArray, 1, 0);
                GL.VertexArrayAttribFormat(vertexArray, 1, 2, VertexAttribType.Float, false, 8);

                GL.EnableVertexArrayAttrib(vertexArray, 2);
                GL.VertexArrayAttribBinding(vertexArray, 2, 0);
                GL.VertexArrayAttribFormat(vertexArray, 2, 4, VertexAttribType.UnsignedByte, true, 16);

                ImGui.Render();
                RenderImDrawData(ImGui.GetDrawData());
            }
        }

        /// <summary>
        /// Updates ImGui input and IO configuration state.
        /// </summary>
        public void Update(GameWindow wnd, float deltaSeconds)
        {
            if (frameBegun)
            {
                ImGui.Render();
            }

            // FIX 2: Add safety check for deltaTime to prevent NaN/Infinity propagation
            if (deltaSeconds <= 0.0f || float.IsInfinity(deltaSeconds) || float.IsNaN(deltaSeconds))
            {
                deltaSeconds = 1.0f / 60.0f; // Default to 60 FPS timing
            }
            
            // Clamp to reasonable bounds (1000 FPS to 1 FPS)
            deltaSeconds = MathF.Max(0.001f, MathF.Min(1.0f, deltaSeconds));

            SetPerFrameImGuiData(deltaSeconds);
            
            // FIX 2: Ensure mouse input is always processed unless explicitly disabled
            // This prevents the "sticky" input state issue
            ImGuiIOPtr io = ImGui.GetIO();
            bool wasMouseDisabled = (io.ConfigFlags & ImGuiConfigFlags.NoMouse) != 0;
            
            UpdateImGuiInput(wnd);
            
            // If mouse was disabled but no camera is currently grabbing, re-enable it
            if (wasMouseDisabled && !IsAnyCameraCurrentlyGrabbing())
            {
                io.ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
            }

            frameBegun = true;
            ImGui.NewFrame();
        }

        // Helper method to check if any camera is currently grabbing input
        // This would need to be implemented based on your MouseGrabHandler design
        private bool IsAnyCameraCurrentlyGrabbing()
        {
            // This is a placeholder - you'd need to track this globally
            // or implement a proper input state manager
            return false;
        }

        /// <summary>
        /// Sets per-frame data based on the associated window.
        /// This is called by Update(float).
        /// </summary>
        private void SetPerFrameImGuiData(float deltaSeconds)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new System.Numerics.Vector2(
                windowWidth / scaleFactor.X,
                windowHeight / scaleFactor.Y);
            io.DisplayFramebufferScale = scaleFactor;
            io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
        }

        readonly List<char> PRESSED_CHARS = new List<char>();

        private void UpdateImGuiInput(GameWindow wnd)
        {
            ImGuiIOPtr io = ImGui.GetIO();

            MouseState mouseState = wnd.MouseState;
            KeyboardState keyboardState = wnd.KeyboardState;

            io.MouseDown[0] = mouseState[MouseButton.Left];
            io.MouseDown[1] = mouseState[MouseButton.Right];
            io.MouseDown[2] = mouseState[MouseButton.Middle];

            var screenPoint = new Vector2i((int) mouseState.X, (int) mouseState.Y);
            var point = screenPoint;//wnd.PointToClient(screenPoint);
            io.MousePos = new System.Numerics.Vector2(point.X, point.Y);

            foreach (Keys key in Enum.GetValues(typeof(Keys)))
            {
                if (key == Keys.Unknown || !keyboardState.IsKeyDown(key))
                {
                    continue;
                }
                io.AddKeyEvent(ConvertKeyToImGuiKey(key), true);
            }

            foreach (var c in PRESSED_CHARS)
            {
                io.AddInputCharacter(c);
            }
            PRESSED_CHARS.Clear();

            io.KeyCtrl = keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl);
            io.KeyAlt = keyboardState.IsKeyDown(Keys.LeftAlt) || keyboardState.IsKeyDown(Keys.RightAlt);
            io.KeyShift = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);
            io.KeySuper = keyboardState.IsKeyDown(Keys.LeftSuper) || keyboardState.IsKeyDown(Keys.RightSuper);
        }

        internal void PressChar(char keyChar)
        {
            PRESSED_CHARS.Add(keyChar);
        }

        internal void MouseScroll(Vector2 offset)
        {
            ImGuiIOPtr io = ImGui.GetIO();

            io.MouseWheel = offset.Y;
            io.MouseWheelH = offset.X;
        }

        private static ImGuiKey ConvertKeyToImGuiKey(Keys key)
        {
            switch (key)
            {
                // Navigation keys
                case Keys.Tab:
                    return ImGuiKey.Tab;
                case Keys.Left:
                    return ImGuiKey.LeftArrow;
                case Keys.Right:
                    return ImGuiKey.RightArrow;
                case Keys.Up:
                    return ImGuiKey.UpArrow;
                case Keys.Down:
                    return ImGuiKey.DownArrow;
                case Keys.PageUp:
                    return ImGuiKey.PageUp;
                case Keys.PageDown:
                    return ImGuiKey.PageDown;
                case Keys.Home:
                    return ImGuiKey.Home;
                case Keys.End:
                    return ImGuiKey.End;
                
                // Editing keys
                case Keys.Delete:
                    return ImGuiKey.Delete;
                case Keys.Backspace:
                    return ImGuiKey.Backspace;
                case Keys.Enter:
                    return ImGuiKey.Enter;
                case Keys.Escape:
                    return ImGuiKey.Escape;
                case Keys.Insert:
                    return ImGuiKey.Insert;
                
                // Number keys (top row)
                case Keys.D0:
                    return ImGuiKey._0;
                case Keys.D1:
                    return ImGuiKey._1;
                case Keys.D2:
                    return ImGuiKey._2;
                case Keys.D3:
                    return ImGuiKey._3;
                case Keys.D4:
                    return ImGuiKey._4;
                case Keys.D5:
                    return ImGuiKey._5;
                case Keys.D6:
                    return ImGuiKey._6;
                case Keys.D7:
                    return ImGuiKey._7;
                case Keys.D8:
                    return ImGuiKey._8;
                case Keys.D9:
                    return ImGuiKey._9;
                
                // Number pad keys (using correct OpenTK naming)
                case Keys.KeyPad0:
                    return ImGuiKey.Keypad0;
                case Keys.KeyPad1:
                    return ImGuiKey.Keypad1;
                case Keys.KeyPad2:
                    return ImGuiKey.Keypad2;
                case Keys.KeyPad3:
                    return ImGuiKey.Keypad3;
                case Keys.KeyPad4:
                    return ImGuiKey.Keypad4;
                case Keys.KeyPad5:
                    return ImGuiKey.Keypad5;
                case Keys.KeyPad6:
                    return ImGuiKey.Keypad6;
                case Keys.KeyPad7:
                    return ImGuiKey.Keypad7;
                case Keys.KeyPad8:
                    return ImGuiKey.Keypad8;
                case Keys.KeyPad9:
                    return ImGuiKey.Keypad9;
                case Keys.KeyPadDecimal:
                    return ImGuiKey.KeypadDecimal;
                case Keys.KeyPadDivide:
                    return ImGuiKey.KeypadDivide;
                case Keys.KeyPadMultiply:
                    return ImGuiKey.KeypadMultiply;
                case Keys.KeyPadSubtract:
                    return ImGuiKey.KeypadSubtract;
                case Keys.KeyPadAdd:
                    return ImGuiKey.KeypadAdd;
                case Keys.KeyPadEnter:
                    return ImGuiKey.KeypadEnter;
                
                // Letter keys
                case Keys.A:
                    return ImGuiKey.A;
                case Keys.B:
                    return ImGuiKey.B;
                case Keys.C:
                    return ImGuiKey.C;
                case Keys.D:
                    return ImGuiKey.D;
                case Keys.E:
                    return ImGuiKey.E;
                case Keys.F:
                    return ImGuiKey.F;
                case Keys.G:
                    return ImGuiKey.G;
                case Keys.H:
                    return ImGuiKey.H;
                case Keys.I:
                    return ImGuiKey.I;
                case Keys.J:
                    return ImGuiKey.J;
                case Keys.K:
                    return ImGuiKey.K;
                case Keys.L:
                    return ImGuiKey.L;
                case Keys.M:
                    return ImGuiKey.M;
                case Keys.N:
                    return ImGuiKey.N;
                case Keys.O:
                    return ImGuiKey.O;
                case Keys.P:
                    return ImGuiKey.P;
                case Keys.Q:
                    return ImGuiKey.Q;
                case Keys.R:
                    return ImGuiKey.R;
                case Keys.S:
                    return ImGuiKey.S;
                case Keys.T:
                    return ImGuiKey.T;
                case Keys.U:
                    return ImGuiKey.U;
                case Keys.V:
                    return ImGuiKey.V;
                case Keys.W:
                    return ImGuiKey.W;
                case Keys.X:
                    return ImGuiKey.X;
                case Keys.Y:
                    return ImGuiKey.Y;
                case Keys.Z:
                    return ImGuiKey.Z;
                
                // Function keys
                case Keys.F1:
                    return ImGuiKey.F1;
                case Keys.F2:
                    return ImGuiKey.F2;
                case Keys.F3:
                    return ImGuiKey.F3;
                case Keys.F4:
                    return ImGuiKey.F4;
                case Keys.F5:
                    return ImGuiKey.F5;
                case Keys.F6:
                    return ImGuiKey.F6;
                case Keys.F7:
                    return ImGuiKey.F7;
                case Keys.F8:
                    return ImGuiKey.F8;
                case Keys.F9:
                    return ImGuiKey.F9;
                case Keys.F10:
                    return ImGuiKey.F10;
                case Keys.F11:
                    return ImGuiKey.F11;
                case Keys.F12:
                    return ImGuiKey.F12;
                
                // Modifier keys
                case Keys.LeftShift:
                    return ImGuiKey.LeftShift;
                case Keys.RightShift:
                    return ImGuiKey.RightShift;
                case Keys.LeftControl:
                    return ImGuiKey.LeftCtrl;
                case Keys.RightControl:
                    return ImGuiKey.RightCtrl;
                case Keys.LeftAlt:
                    return ImGuiKey.LeftAlt;
                case Keys.RightAlt:
                    return ImGuiKey.RightAlt;
                case Keys.LeftSuper:
                    return ImGuiKey.LeftSuper;
                case Keys.RightSuper:
                    return ImGuiKey.RightSuper;
                
                // Symbol keys
                case Keys.Space:
                    return ImGuiKey.Space;
                case Keys.Apostrophe:
                    return ImGuiKey.Apostrophe;
                case Keys.Comma:
                    return ImGuiKey.Comma;
                case Keys.Minus:
                    return ImGuiKey.Minus;
                case Keys.Period:
                    return ImGuiKey.Period;
                case Keys.Slash:
                    return ImGuiKey.Slash;
                case Keys.Semicolon:
                    return ImGuiKey.Semicolon;
                case Keys.Equal:
                    return ImGuiKey.Equal;
                case Keys.LeftBracket:
                    return ImGuiKey.LeftBracket;
                case Keys.Backslash:
                    return ImGuiKey.Backslash;
                case Keys.RightBracket:
                    return ImGuiKey.RightBracket;
                case Keys.GraveAccent:
                    return ImGuiKey.GraveAccent;
                
                // System keys
                case Keys.CapsLock:
                    return ImGuiKey.CapsLock;
                case Keys.ScrollLock:
                    return ImGuiKey.ScrollLock;
                case Keys.NumLock:
                    return ImGuiKey.NumLock;
                case Keys.PrintScreen:
                    return ImGuiKey.PrintScreen;
                case Keys.Pause:
                    return ImGuiKey.Pause;
                case Keys.Menu:
                    return ImGuiKey.Menu;
            }

            // For any unmapped keys, cast directly to ImGuiKey and hope for the best
            // This provides a fallback for keys not explicitly mapped above
            return (ImGuiKey) key;
        }

        private void RenderImDrawData(ImDrawDataPtr drawData)
        {
            if (shader == null) return;

            if (drawData.CmdListsCount == 0)
            {
                return;
            }

            for (int i = 0; i < drawData.CmdListsCount; i++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[i];

                int vertexSize = cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>();
                if (vertexSize > vertexBufferSize)
                {
                    int newSize = (int) Math.Max(vertexBufferSize * 1.5f, vertexSize);
                    GL.NamedBufferData(vertexBuffer, newSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
                    vertexBufferSize = newSize;

                    LOGGER.Info("Resized dear imgui vertex buffer to new size {0}", vertexBufferSize);
                }

                int indexSize = cmdList.IdxBuffer.Size * sizeof(ushort);
                if (indexSize > indexBufferSize)
                {
                    int newSize = (int) Math.Max(indexBufferSize * 1.5f, indexSize);
                    GL.NamedBufferData(indexBuffer, newSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
                    indexBufferSize = newSize;

                    LOGGER.Info("Resized dear imgui index buffer to new size {0}", indexBufferSize);
                }
            }

            // Setup orthographic projection matrix into our constant buffer
            ImGuiIOPtr io = ImGui.GetIO();
            Matrix4 mvp = Matrix4.CreateOrthographicOffCenter(
                0.0f,
                io.DisplaySize.X,
                io.DisplaySize.Y,
                0.0f,
                -1.0f,
                1.0f);

            shader.UseShader();
            shader.SetUniformMatrix4(UniformName.worldToView, ref mvp);
            shader.SetUniform1(UniformName.fontTexture, 0);
            GLUtil.CheckGlError("Projection");

            GL.BindVertexArray(vertexArray);
            GLUtil.CheckGlError("VAO");

            drawData.ScaleClipRects(io.DisplayFramebufferScale);

            GL.Enable(EnableCap.Blend);
            GL.Enable(EnableCap.ScissorTest);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);

            // Render command lists
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[n];

                GL.NamedBufferSubData(vertexBuffer, IntPtr.Zero, cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>(), cmdList.VtxBuffer.Data);
                GLUtil.CheckGlError($"Data Vert {n}");

                GL.NamedBufferSubData(indexBuffer, IntPtr.Zero, cmdList.IdxBuffer.Size * sizeof(ushort), cmdList.IdxBuffer.Data);
                GLUtil.CheckGlError($"Data Idx {n}");

                int vtxOffset = 0;
                int idxOffset = 0;

                for (int cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
                {
                    ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmdI];
                    if (pcmd.UserCallback != IntPtr.Zero)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        GL.ActiveTexture(TextureUnit.Texture0);
                        GL.BindTexture(TextureTarget.Texture2D, (int) pcmd.TextureId);
                        GLUtil.CheckGlError("Texture");

                        // We do _windowHeight - (int)clip.W instead of (int)clip.Y because gl has flipped Y when it comes to these coordinates
                        var clip = pcmd.ClipRect;
                        GL.Scissor((int) clip.X, windowHeight - (int) clip.W, (int) (clip.Z - clip.X), (int) (clip.W - clip.Y));
                        GLUtil.CheckGlError("Scissor");

                        if ((io.BackendFlags & ImGuiBackendFlags.RendererHasVtxOffset) != 0)
                        {
                            GL.DrawElementsBaseVertex(PrimitiveType.Triangles, (int) pcmd.ElemCount, DrawElementsType.UnsignedShort, (IntPtr) (idxOffset * sizeof(ushort)), vtxOffset);
                        }
                        else
                        {
                            GL.DrawElements(BeginMode.Triangles, (int) pcmd.ElemCount, DrawElementsType.UnsignedShort, (int) pcmd.IdxOffset * sizeof(ushort));
                        }
                        GLUtil.CheckGlError("Draw");
                    }

                    idxOffset += (int) pcmd.ElemCount;
                }
                vtxOffset += cmdList.VtxBuffer.Size;
            }

            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.ScissorTest);
        }

        /// <summary>
        /// Frees all graphics resources used by the renderer.
        /// </summary>
        public void Dispose()
        {
            if (fontGlTexture != null)
                fontGlTexture.Dispose();

            if (shader != null)
                shader.Dispose();
        }
    }
}
