// Copyright (C) 2018-2021, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using ImGuiNET;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Replanetizer.MemoryHook;
using Replanetizer.Renderer;
using Replanetizer.Tools;
using Replanetizer.Utils;
using SixLabors.ImageSharp;
using static LibReplanetizer.DataFunctions;
using static LibReplanetizer.Utilities;
using Buffer = System.Buffer;
using SysVector2 = System.Numerics.Vector2;
using Texture = LibReplanetizer.Texture;

namespace Replanetizer.Frames
{
    public class LevelFrame : Frame
    {
        private static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();
        protected override string frameName { get; set; } = "Level";

        private FramebufferRenderer? renderer;
        public LevelRenderer? levelRenderer;
        private RendererPayload rendererPayload;
        public Level level { get; set; }
        private bool enableCameraInfo = true;
        public ShaderTable shaderTable;

        private Clipboard clipboard = new Clipboard();

        private int alignmentUbo = GL.GetInteger(GetPName.UniformBufferOffsetAlignment);

        private float movingAvgFrametime = 1.0f;

        public readonly Selection selectedObjects;
        private readonly string[] selectionPositioningOptions = { PivotPositioning.Mean.HUMAN_NAME, PivotPositioning.IndividualOrigins.HUMAN_NAME };
        private readonly string[] selectionSpaceOptions = { TransformSpace.Global.HUMAN_NAME, TransformSpace.Local.HUMAN_NAME };

        private int antialiasing = 1;
        private readonly string[] antialiasingOptions = { "Off", "2x MSAA", "4x MSAA", "8x MSAA", "16x MSAA", "32x MSAA", "64x MSAA", "128x MSAA", "256x MSAA", "512x MSAA" };
        private int maxAntialiasing = 4;

        private Vector2 mousePos;
        private Vector2 windowPos;
        private bool boxSelecting;
        private Vector2 boxSelectStart;
        private Vector3 prevMouseRay;
        private Rectangle contentRegion;
        private int lastMouseX, lastMouseY;
        private bool xLock = false, yLock = false, zLock = false;
        private MouseGrabHandler mouseGrabHandler = new()
        {
            mouseButton = MouseButton.Right
        };
        public readonly Keymap KEYMAP;

        public bool initialized, invalidate;
        private int chunkCount = 0;

        public Camera camera;

        private Toolbox toolbox = new();

        public Dictionary<Texture, GLTexture> textureIds = new Dictionary<Texture, GLTexture>();

        private MemoryHookHandle? hook = null;
        private bool interactiveSession = false;
        private bool hookLiveUpdate = true;
        private bool hookUpdateCamera = false;


        private int width, height;

        private List<Frame> subFrames;

        // Store the latest collision warning message for UI display
        private string? collisionWarningMessage;

        public LevelFrame(Window wnd, string res) : base(wnd)
        {
            level = new Level(res);
            subFrames = new List<Frame>();
            camera = new Camera();

            string? applicationFolder = System.AppContext.BaseDirectory;
            string shaderFolder = Path.Join(applicationFolder, "Shaders");
            shaderTable = new ShaderTable(shaderFolder);

            maxAntialiasing = (int) Math.Log2((double) GL.GetInteger(GetPName.MaxSamples));

            KEYMAP = new Keymap(wnd, Keymap.DEFAULT_KEYMAP);

            selectedObjects = new Selection();
            selectedObjects.CollectionChanged += SelectedObjectsOnCollectionChanged;

            toolbox.ToolChanged += (_, _) => InvalidateView();

            rendererPayload = new RendererPayload(camera, selectedObjects, toolbox);

            // Subscribe to collision warnings from ObjTerrainImporter
            LibReplanetizer.ObjTerrainImporter.CollisionWarning += OnCollisionWarning;

            UpdateWindowSize();
            OnResize();

            LoadLevel(level);
        }

        // Handler for collision warnings
        private void OnCollisionWarning(string message)
        {
            collisionWarningMessage = message;
            LOGGER.Warn("[CollisionWarning] " + message);
        }

        private void DumpCollisionBytes(int length = 64)
        {
            if (level.collBytesEngine == null)
            {
                LOGGER.Warn("[DumpCollisionBytes] collBytesEngine is null");
                Console.WriteLine("[DumpCollisionBytes] collBytesEngine is null");
                return;
            }

            string hex = BitConverter.ToString(level.collBytesEngine, 0,
                Math.Min(length, level.collBytesEngine.Length));
            LOGGER.Info(hex);
            Console.WriteLine(hex);
        }

        private void DumpCollisionBytesFull()
        {
            if (level.collBytesEngine == null)
            {
                LOGGER.Warn("[DumpCollisionBytesFull] collBytesEngine is null");
                Console.WriteLine("[DumpCollisionBytesFull] collBytesEngine is null");
                return;
            }
            string path = CrossFileDialog.SaveFile("CollisionBytesFull.txt", ".txt");
            if (string.IsNullOrEmpty(path)) return;
            using (StreamWriter sw = File.CreateText(path))
            {
                for (int i = 0; i < level.collBytesEngine.Length; i++)
                {
                    sw.WriteLine($"{i:D6}: 0x{level.collBytesEngine[i]:X2}");
                }
            }
            LOGGER.Info($"[DumpCollisionBytesFull] Saved {level.collBytesEngine.Length} bytes to {path}");
            Console.WriteLine($"[DumpCollisionBytesFull] Saved {level.collBytesEngine.Length} bytes to {path}");
        }

        public static bool FrameMustClose(Frame frame)
        {
            return !frame.isOpen;
        }

        private void RenderMenuBar()
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("Level"))
                {
                    if (interactiveSession)
                    {
                        ImGui.BeginDisabled();
                    }
                    if (ImGui.MenuItem("Save as"))
                    {
                        var res = CrossFileDialog.SaveFile();
                        if (res.Length > 0)
                        {
                            // --- PATCH: Ensure collision bytes are updated before saving ---
                            level.UpdateCollisionBytesFromChunks();
                            try
                            {
                                LOGGER.Info("[DIAG] Save As menu item triggered!");
                                Console.WriteLine("[DIAG] Save As menu item triggered!");
                                if (level.collBytesEngine != null && level.collBytesEngine.Length > 0)
                                {
                                    LOGGER.Info($"[DIAG] collBytesEngine length: {level.collBytesEngine.Length}");
                                    Console.WriteLine($"[DIAG] collBytesEngine length: {level.collBytesEngine.Length}");
                                }
                                else
                                {
                                    LOGGER.Warn("[DIAG] collBytesEngine is null or empty before save!");
                                    Console.WriteLine("[DIAG] collBytesEngine is null or empty before save!");
                                }
                            }
                            catch (Exception ex)
                            {
                                LOGGER.Error(ex, "[DIAG] Exception in Save As diagnostics");
                                Console.WriteLine($"[DIAG] Exception: {ex}");
                            }
                            level.Save(res);
                        }
                    }
                    if (interactiveSession)
                    {
                        ImGui.EndDisabled();
                    }

                    if (ImGui.MenuItem("Log collision bytes"))
                    {
                        DumpCollisionBytes();
                    }
                    if (ImGui.MenuItem("Log ALL collision bytes (to .txt)"))
                    {
                        DumpCollisionBytesFull();
                    }

                    if (ImGui.BeginMenu("Export"))
                    {
                        if (ImGui.MenuItem("Collision"))
                        {
                            var res = CrossFileDialog.SaveFile(filter: ".obj|.rcc");
                            if (res.Length > 0)
                            {
                                var extension = Path.GetExtension(res);

                                switch (extension)
                                {
                                    case ".rcc":
                                        FileStream fs = File.Open(res, FileMode.Create);
                                        fs.Write(level.collBytesEngine, 0, level.collBytesEngine.Length);
                                        fs.Close();
                                        break;
                                    default:
                                    case ".obj":
                                        WavefrontExporter exporter = new WavefrontExporter();
                                        exporter.ExportCollision(res, level);
                                        break;
                                }
                            }
                        }
                        if (ImGui.MenuItem("Level as Model"))
                        {
                            subFrames.Add(new LevelExportFrame(this.wnd, this));
                        }
                        if (ImGui.MenuItem("All textures"))
                        {
                            var res = CrossFileDialog.OpenFolder();
                            if (res.Length > 0)
                            {
                                TextureIO.ExportAllTextures(level, res);
                            }
                        }
                        if (ImGui.BeginMenu("Class IDs lists"))
                        {
                            if (ImGui.MenuItem("Mobies"))
                            {
                                var path = CrossFileDialog.SaveFile("mobyIDs.txt", ".txt");
                                ExportListOfProperties<Moby>(path, level.mobs, (m) => { return m.modelID.ToString(); });
                            }
                            if (ImGui.MenuItem("Ties"))
                            {
                                var path = CrossFileDialog.SaveFile("tieIDs.txt", ".txt");
                                ExportListOfProperties<Tie>(path, level.ties, (t) => { return t.modelID.ToString(); });
                            }
                            if (ImGui.MenuItem("Shrubs"))
                            {
                                var path = CrossFileDialog.SaveFile("shrubIDs.txt", ".txt");
                                ExportListOfProperties<Shrub>(path, level.shrubs, (s) => { return s.modelID.ToString(); });
                            }
                            ImGui.EndMenu();
                        }

                        ImGui.EndMenu();
                    }
                    if (ImGui.BeginMenu("Import"))
                    {
                        if (ImGui.MenuItem("Collision (Internal R&C format only)"))
                        {
                            var res = CrossFileDialog.OpenFile(filter: ".rcc");
                            if (res.Length > 0)
                            {
                                FileStream fs = File.Open(res, FileMode.Open);
                                level.collBytesEngine = ReadBlock(fs, 0, (int) fs.Length);
                                fs.Close();
                            }
                            InvalidateView();
                        }
                        // NEW: Resize existing collision to match current terrain fragments
                        if (ImGui.MenuItem("Resize Collision to Terrain"))
                        {
                            enableCameraInfo = true; // Just to see if UI changes
                            LOGGER.Info("[UI] Resize Collision to Terrain selected."); // NLog
                            Console.WriteLine("[UI] Resize Collision to Terrain selected."); // Console
                            ResizeCollisionToCurrentTerrain();
                        }
                        // NEW: Resize existing collision to match current terrain fragments, with byte fixing
                        if (ImGui.MenuItem("Resize Collision to Terrain (with Byte Fixer)", null, false, true))
                        {
                            enableCameraInfo = true;
                            LOGGER.Info("[UI] Resize Collision to Terrain with Byte Fixer selected.");
                            Console.WriteLine("[UI] Resize Collision to Terrain with Byte Fixer selected.");
                            ResizeCollisionToCurrentTerrainWithByteFixer();
                        }
                        ImGui.EndMenu();
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Windows"))
                {
                    if (ImGui.MenuItem("Object properties"))
                    {
                        subFrames.Add(
                            new PropertyFrame(this.wnd, this, listenToCallbacks: true)
                            {
                                selection = selectedObjects
                            }
                        );
                    }
                    if (ImGui.MenuItem("Model viewer"))
                    {
                        if (selectedObjects.newestObject != null && selectedObjects.newestObject is ModelObject obj)
                        {
                            subFrames.Add(new ModelFrame(this.wnd, this, this.shaderTable, obj.model));
                        }
                        else
                        {
                            subFrames.Add(new ModelFrame(this.wnd, this, this.shaderTable));
                        }
                    }
                    if (ImGui.MenuItem("Texture viewer"))
                    {
                        subFrames.Add(new TextureFrame(this.wnd, this));
                    }
                    if (ImGui.MenuItem("Lights"))
                    {
                        subFrames.Add(new LightsFrame(this.wnd, this, level.lights, level.lightConfig));
                    }
                    if (ImGui.MenuItem("Level variables"))
                    {
                        subFrames.Add(
                            new PropertyFrame(this.wnd, this, "Level variables", true)
                            {
                                selectedObject = level.levelVariables
                            }
                        );
                    }
                    if (ImGui.MenuItem("Memory Hook"))
                    {
                        subFrames.Add(new MemoryHookFrame(this.wnd, this));
                    }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Render"))
                {
                    if (ImGui.Checkbox("Moby", ref rendererPayload.visibility.enableMoby)) InvalidateView();
                    if (ImGui.Checkbox("Tie", ref rendererPayload.visibility.enableTie)) InvalidateView();
                    if (ImGui.Checkbox("Shrub", ref rendererPayload.visibility.enableShrub)) InvalidateView();
                    if (ImGui.Checkbox("Spline", ref rendererPayload.visibility.enableSpline)) InvalidateView();
                    if (ImGui.Checkbox("Cuboid", ref rendererPayload.visibility.enableCuboid)) InvalidateView();
                    if (ImGui.Checkbox("Spheres", ref rendererPayload.visibility.enableSpheres)) InvalidateView();
                    if (ImGui.Checkbox("Cylinders", ref rendererPayload.visibility.enableCylinders)) InvalidateView();
                    if (ImGui.Checkbox("Pills", ref rendererPayload.visibility.enablePills)) InvalidateView();
                    if (ImGui.Checkbox("SoundInstances", ref rendererPayload.visibility.enableSoundInstances)) InvalidateView();
                    if (ImGui.Checkbox("Cameras", ref rendererPayload.visibility.enableGameCameras)) InvalidateView();
                    if (ImGui.Checkbox("Pointlights", ref rendererPayload.visibility.enablePointLights)) InvalidateView();
                    if (ImGui.Checkbox("EnvSamples", ref rendererPayload.visibility.enableEnvSamples)) InvalidateView();
                    if (ImGui.Checkbox("EnvTransitions", ref rendererPayload.visibility.enableEnvTransitions)) InvalidateView();
                    if (ImGui.Checkbox("GrindPaths", ref rendererPayload.visibility.enableGrindPaths)) InvalidateView();
                    if (ImGui.Checkbox("Skybox", ref rendererPayload.visibility.enableSkybox)) InvalidateView();
                    if (ImGui.Checkbox("Terrain", ref rendererPayload.visibility.enableTerrain)) InvalidateView();
                    if (ImGui.Checkbox("Collision", ref rendererPayload.visibility.enableCollision)) InvalidateView();
                    ImGui.Separator();
                    if (ImGui.Checkbox("Transparency", ref rendererPayload.visibility.enableTransparency)) InvalidateView();
                    if (ImGui.Checkbox("Distance Culling", ref rendererPayload.visibility.enableDistanceCulling)) InvalidateView();
                    if (ImGui.Checkbox("Frustum Culling", ref rendererPayload.visibility.enableFrustumCulling)) InvalidateView();
                    if (ImGui.Checkbox("Fog", ref rendererPayload.visibility.enableFog)) InvalidateView();
                    if (ImGui.Checkbox("Meshless Models", ref rendererPayload.visibility.enableMeshlessModels)) InvalidateView();
                    ImGui.PushItemWidth(90.0f);
                    if (ImGui.Combo("Antialiasing", ref antialiasing, antialiasingOptions, 1 + maxAntialiasing))
                    {
                        UpdateAaLevel();
                        InvalidateView();
                    }
                    ImGui.PopItemWidth();
                    ImGui.Separator();
                    ImGui.Checkbox("Camera Info", ref enableCameraInfo);

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Tools"))
                {
                    if (ImGui.MenuItem($"Translate        [{KEYMAP.NameOf(Keybinds.ToolTranslate)}]"))
                        toolbox.ChangeTool(ToolType.Translation);
                    if (ImGui.MenuItem($"Rotate           [{KEYMAP.NameOf(Keybinds.ToolRotation)}]"))
                        toolbox.ChangeTool(ToolType.Rotation);
                    if (ImGui.MenuItem($"Scale            [{KEYMAP.NameOf(Keybinds.ToolScaling)}]"))
                        toolbox.ChangeTool(ToolType.Scaling);
                    if (ImGui.MenuItem($"Vertex translate [{KEYMAP.NameOf(Keybinds.ToolVertexTranslator)}]"))
                        toolbox.ChangeTool(ToolType.VertexTranslation);
                    if (ImGui.MenuItem($"No tool          [{KEYMAP.NameOf(Keybinds.ToolNone)}]"))
                        toolbox.ChangeTool(ToolType.None);
                    if (ImGui.MenuItem($"Delete selected  [{KEYMAP.NameOf(Keybinds.DeleteObject)}]"))
                        DeleteObject(selectedObjects);
                    if (ImGui.MenuItem("Deselect all"))
                        selectedObjects.Clear();
                    ImGui.EndMenu();
                }

                // Show Chunks menu even if there is only one chunk
                if (chunkCount > 0 && ImGui.BeginMenu("Chunks"))
                {
                    for (int i = 0; i < chunkCount; i++)
                        if (ImGui.Checkbox($"Chunk {i}", ref rendererPayload.visibility.chunks[i]))
                            InvalidateView();
                    ImGui.EndMenu();
                }

                ImGui.Separator();

                if (interactiveSession)
                {
                    ImGui.Checkbox("Hook Update", ref hookLiveUpdate);
                    ImGui.Checkbox("Hook Camera", ref hookUpdateCamera);
                    ImGui.Checkbox("Animations", ref rendererPayload.visibility.enableAnimations);
                    ImGui.Separator();
                }

                if (selectedObjects != null && selectedObjects.Count != 0)
                {
                    if (ImGui.BeginMenu("Selection"))
                    {
                        int transformSpace = toolbox.transformSpace.KEY;
                        int pivotPositioning = toolbox.pivotPositioning.KEY;

                        ImGui.PushItemWidth(160.0f);
                        if (ImGui.Combo("Transform Space", ref transformSpace, selectionSpaceOptions, selectionSpaceOptions.Count()))
                        {
                            toolbox.transformSpace = TransformSpace.GetByKey(transformSpace) ?? TransformSpace.Global;
                            InvalidateView();
                        }

                        if (ImGui.Combo("Pivot Positioning", ref pivotPositioning, selectionPositioningOptions, selectionPositioningOptions.Count()))
                        {
                            toolbox.pivotPositioning = PivotPositioning.GetByKey(pivotPositioning) ?? PivotPositioning.Mean;
                            InvalidateView();
                        }
                        ImGui.PopItemWidth();

                        ImGui.EndMenu();
                    }
                }

                ImGui.EndMenuBar();
            }
        }

        private void RenderTextOverlay(float deltaTime)
        {
            if (!enableCameraInfo) return;

            const float PAD = 10f;
            const ImGuiWindowFlags WINDOW_FLAGS =
                ImGuiWindowFlags.NoDecoration
                | ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoNav
                | ImGuiWindowFlags.NoMove;

            var viewport = ImGui.GetMainViewport();
            var workPos = viewport.WorkPos;
            var workSize = viewport.WorkSize;
            SysVector2 windowPos = new(
                workPos.X + PAD,
                workPos.Y + workSize.Y - PAD
            );
            SysVector2 windowPosPivot = new(0f, 1f);
            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always, windowPosPivot);

            ImGui.SetNextWindowBgAlpha(0.35f);

            if (ImGui.Begin("Info overlay", WINDOW_FLAGS))
            {
                ImGui.Text("Camera info");
                ImGui.Separator();
                ImGui.Text(
                    "Position: (" +
                    $"x: {camera.position.X:F4}, " +
                    $"y: {camera.position.Y:F4}, " +
                    $"z: {camera.position.Z:F4}" +
                    ")"
                );
                var camRotX = ToDegreesF(camera.rotation.X);
                var camRotZ = ToDegreesF(camera.rotation.Z);
                // Wrap around [0, 360)
                camRotZ = (camRotZ % 360f + 360f) % 360f;
                ImGui.Text(
                    $"Rotation: (yaw: {camRotZ:F4}, pitch: {camRotX:F4})"
                );

                // Show collision warning if present
                if (!string.IsNullOrEmpty(collisionWarningMessage))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, 0xff00ffff); // Cyan for warning
                    ImGui.Text($"Collision Warning: {collisionWarningMessage}");
                    ImGui.PopStyleColor();
                }

                // FIX 1: Add safety checks for deltaTime to prevent crashes
                if (deltaTime > 0.0f && !float.IsInfinity(deltaTime) && !float.IsNaN(deltaTime))
                {
                    // Clamp deltaTime to reasonable bounds (max 1 second frame time)
                    float clampedDeltaTime = MathF.Min(deltaTime, 1.0f);
                    movingAvgFrametime = movingAvgFrametime * 0.95f + clampedDeltaTime * 0.05f;
                    
                    // Ensure movingAvgFrametime stays within reasonable bounds
                    movingAvgFrametime = MathF.Max(movingAvgFrametime, 0.001f); // Min 1000 FPS
                    movingAvgFrametime = MathF.Min(movingAvgFrametime, 1.0f);   // Max 1 FPS
                }
                
                float fps = MathF.Round(1.0f / movingAvgFrametime);
                float frametime = ((float) (MathF.Round(10000.0f * movingAvgFrametime))) / 10.0f;
                
                switch (fps)
                {
                    case < 30:
                        ImGui.PushStyleColor(ImGuiCol.Text, 0xff0000ff);
                        break;
                    case < 50:
                        ImGui.PushStyleColor(ImGuiCol.Text, 0xff00ffff);
                        break;
                    default:
                        ImGui.PushStyleColor(ImGuiCol.Text, 0xffffffff);
                        break;
                }
                ImGui.Text("FPS: " + fps + " (" + frametime + " ms)");
                ImGui.PopStyleColor();
                if (interactiveSession)
                {
                    ImGui.Text("Game Frame Number: " + hook?.GetLevelFrameNumber());
                }

            }
            ImGui.End();
        }

        /// <param name="allowNewGrab">whether a new click will begin grabbing</param>
        /// <returns>whether the cursor is being grabbed</returns>
        private bool CheckForRotationInput(float deltaTime, bool allowNewGrab)
        {
            if (mouseGrabHandler.TryGrabMouse(wnd, allowNewGrab))
            {
                // FIX 2: Only disable mouse when we're actually in the level viewport
                // and only for this specific frame, not globally
                var isMouseInLevelViewport = ImGui.IsWindowHovered() && 
                                           contentRegion.Contains(new Point((int) wnd.MousePosition.X, (int) wnd.MousePosition.Y));
                
                if (isMouseInLevelViewport)
                {
                    ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.NoMouse;
                }
            }
            else
            {
                // FIX 2: Always restore mouse input when not grabbing
                ImGui.GetIO().ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
                return false;
            }

            Vector2 rot = new Vector2(wnd.MouseState.Delta.X * 0.016666f, wnd.MouseState.Delta.Y * 0.016666f);

            rot *= camera.speed;

            camera.Rotate(rot);

            InvalidateView();
            return true;
        }

        private void UpdateWindowSize()
        {
            int prevWidth = width, prevHeight = height;

            System.Numerics.Vector2 avail = ImGui.GetContentRegionAvail();

            width = (int) avail.X;
            height = (int) avail.Y;

            if (width <= 0 || height <= 0) return;

            camera.aspect = (float) width / height;

            if (width != prevWidth || height != prevHeight)
            {
                InvalidateView();
                OnResize();
            }

            System.Numerics.Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
            Vector2 windowZero = new Vector2(cursorScreenPos.X, cursorScreenPos.Y);
            windowPos = windowZero;
            mousePos = wnd.MousePosition - windowZero;
            contentRegion = new Rectangle((int) windowZero.X, (int) windowZero.Y, width, height);
        }

        public override void RenderAsWindow(float deltaTime)
        {
            if (!initialized) CustomGLControl_Load();

            var viewport = ImGui.GetMainViewport();
            var pos = viewport.Pos;
            var size = viewport.Size;

            ImGui.SetNextWindowPos(pos);
            ImGui.SetNextWindowSize(size);
            ImGui.SetNextWindowViewport(viewport.ID);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, 0);

            ImGui.Begin(frameName,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus |
                ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoDocking);

            ImGui.PopStyleVar(2);

            Render(deltaTime);
            ImGui.End();

            RenderSubFrames(deltaTime);
        }

        public override void Render(float deltaTime)
        {
            if (renderer == null) return;

            RenderMenuBar();
            RenderTextOverlay(deltaTime);
            UpdateWindowSize();
            Tick(deltaTime);

            if (invalidate)
            {
                renderer.RenderToTexture(() =>
                {
                    //Setup openGL variables
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    GL.Enable(EnableCap.DepthTest);
                    GL.Viewport(0, 0, width, height);
                    GL.Enable(EnableCap.ScissorTest);
                    GL.Scissor(0, 0, width, height);

                    OnPaint();
                });
                invalidate = false;
            }
            ImGui.Image((IntPtr) renderer.outputTexture, new System.Numerics.Vector2(width, height),
                    System.Numerics.Vector2.UnitY, System.Numerics.Vector2.UnitX);

            if (boxSelecting)
            {
                var draw = ImGui.GetWindowDrawList();
                System.Numerics.Vector2 start = new System.Numerics.Vector2(boxSelectStart.X + windowPos.X, boxSelectStart.Y + windowPos.Y);
                System.Numerics.Vector2 end = new System.Numerics.Vector2(mousePos.X + windowPos.X, mousePos.Y + windowPos.Y);
                uint fill = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.2f, 0.5f, 1f, 0.2f));
                uint line = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.2f, 0.5f, 1f, 1f));
                draw.AddRectFilled(start, end, fill);
                draw.AddRect(start, end, line);
            }
        }

        private void RenderSubFrames(float deltaTime)
        {
            subFrames.RemoveAll(FrameMustClose);
            foreach (Frame levelSubFrame in subFrames)
            {
                levelSubFrame.RenderAsWindow(deltaTime);
            }
        }

        private void CustomGLControl_Load()
        {
            //Setup openGL variables
            GL.Enable(EnableCap.DepthTest);

            Matrix4 dissolvePattern = new Matrix4(1.0f / 17.0f, 9.0f / 17.0f, 3.0f / 17.0f, 11.0f / 17.0f,
                                        13.0f / 17.0f, 5.0f / 17.0f, 15.0f / 17.0f, 7.0f / 17.0f,
                                        4.0f / 17.0f, 12.0f / 17.0f, 2.0f / 17.0f, 10.0f / 17.0f,
                                        16.0f / 17.0f, 8.0f / 17.0f, 14.0f / 17.0f, 6.0f / 17.0f);
            shaderTable.meshShader.UseShader();
            shaderTable.meshShader.SetUniformMatrix4(UniformName.dissolvePattern, ref dissolvePattern);
            shaderTable.animationShader.UseShader();
            shaderTable.animationShader.SetUniformMatrix4(UniformName.dissolvePattern, ref dissolvePattern);

            initialized = true;

            OnResize();
        }

        void LoadLevelTextures()
        {
            textureIds = new Dictionary<Texture, GLTexture>();
            foreach (Texture t in level.textures)
            {
                textureIds.Add(t, new GLTexture(t));
            }

            foreach (List<Texture> list in level.armorTextures)
            {
                foreach (Texture t in list)
                {
                    textureIds.Add(t, new GLTexture(t));
                }
            }

            foreach (Texture t in level.gadgetTextures)
            {
                textureIds.Add(t, new GLTexture(t));
            }

            foreach (Mission mission in level.missions)
            {
                foreach (Texture t in mission.textures)
                {
                    textureIds.Add(t, new GLTexture(t));
                }
            }

            foreach (List<Texture> textures in level.mobyloadTextures)
            {
                foreach (Texture t in textures)
                {
                    textureIds.Add(t, new GLTexture(t));
                }
            }
        }

        private void LoadLevel(Level level)
        {
            this.level = level;

            LoadLevelTextures();

            chunkCount = Math.Max(level.terrainChunks.Count, level.collisionObjects.Count);

            levelRenderer = new LevelRenderer(shaderTable, textureIds);
            levelRenderer.Include(this.level);

            if (level.mobs.Count > 0)
            {
                Moby ratchet = level.mobs[0];
                camera.MoveBehind(ratchet);
            }
            else
            {
                camera.SetPosition(0, 0, 0);
                camera.SetRotation(0, 0);
            }
            selectedObjects.Clear();

            InvalidateView();
            // Do NOT generate collision automatically here.
        }

        /// <summary>
        /// Manually generate collision from the current terrain fragments.
        /// </summary>
        public void ResizeCollisionToCurrentTerrain()
        {
            if (level?.terrainEngine?.fragments == null || level.terrainEngine.fragments.Count == 0)
            {
                LOGGER.Warn("[CollisionResize] No terrain fragments available to generate collision from.");
                Console.WriteLine("[CollisionResize] No terrain fragments available to generate collision from.");
                return;
            }

            LOGGER.Info($"[CollisionResize] Generating unified collision from {level.terrainEngine.fragments.Count} terrain fragments...");
            Console.WriteLine($"[CollisionResize] Generating unified collision from {level.terrainEngine.fragments.Count} terrain fragments...");

            // --- AFTER (Correctly creates one unified chunk) ---
            // Reuse the existing, correct logic from the importer.
            ObjTerrainImporter.GenerateAndAssignCollision(level, level.terrainEngine.fragments);

            // Update chunk count for rendering and saving
            int newChunkCount = level.collisionChunks?.Count ?? 0;
            rendererPayload.visibility.chunks = new bool[newChunkCount];
            if (newChunkCount > 0)
            {
                Array.Fill(rendererPayload.visibility.chunks, true);
            }
            this.chunkCount = newChunkCount;

            // Refresh the renderer to show the new collision
            levelRenderer?.Dispose();
            levelRenderer = new LevelRenderer(shaderTable, textureIds);
            levelRenderer.Include(level);
            InvalidateView();

            LOGGER.Info($"[CollisionResize] Successfully generated collision. Chunks: {newChunkCount}, Bytes: {level.collBytesEngine?.Length ?? 0}");
            Console.WriteLine($"[CollisionResize] Successfully generated collision. Chunks: {newChunkCount}, Bytes: {level.collBytesEngine?.Length ?? 0}");
        }

        /// <summary>
        /// Manually generate collision from the current terrain fragments, with improved byte fixer parsing for range and single offsets.
        /// </summary>
        public void ResizeCollisionToCurrentTerrainWithByteFixer()
        {
            if (level?.terrainEngine?.fragments == null || level.terrainEngine.fragments.Count == 0)
            {
                LOGGER.Warn("[CollisionResize] No terrain fragments available to generate collision from.");
                Console.WriteLine("[CollisionResize] No terrain fragments available to generate collision from.");
                return;
            }

            // Prompt user for a comparison report file
            string reportPath = CrossFileDialog.OpenFile("Select Collision Byte Comparison Report", ".txt");
            Dictionary<int, byte> byteFixes = new Dictionary<int, byte>();
            if (!string.IsNullOrEmpty(reportPath) && File.Exists(reportPath))
            {
                foreach (var line in File.ReadLines(reportPath))
                {
                    // Example: Offsets 0x000000-0x000002: Common byte is 0x00 (Length: 3)
                    // Example: Offset 0x000003:          Common byte is 0x10
                    if (line.StartsWith("Offsets "))
                    {
                        int startIdx = line.IndexOf("0x");
                        int dashIdx = line.IndexOf("-", startIdx);
                        int endIdx = line.IndexOf(":", dashIdx);
                        int byteIdx = line.IndexOf("0x", endIdx);
                        if (startIdx >= 0 && dashIdx > startIdx && endIdx > dashIdx && byteIdx > endIdx)
                        {
                            string startHex = line.Substring(startIdx, dashIdx - startIdx).Trim();
                            string endHex = line.Substring(dashIdx + 1, endIdx - dashIdx - 1).Trim();
                            string byteHex = line.Substring(byteIdx, line.Length - byteIdx).Trim();
                            if (int.TryParse(startHex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out int startOffset) &&
                                int.TryParse(endHex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out int endOffset) &&
                                byte.TryParse(byteHex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out byte value))
                            {
                                for (int i = startOffset; i <= endOffset; i++)
                                    byteFixes[i] = value;
                            }
                        }
                    }
                    else if (line.StartsWith("Offset "))
                    {
                        int offsetIdx = line.IndexOf("0x");
                        int colonIdx = line.IndexOf(":", offsetIdx);
                        int byteIdx = line.IndexOf("0x", colonIdx);
                        if (offsetIdx >= 0 && colonIdx > offsetIdx && byteIdx > colonIdx)
                        {
                            string offsetHex = line.Substring(offsetIdx, colonIdx - offsetIdx).Trim();
                            string byteHex = line.Substring(byteIdx, line.Length - byteIdx).Trim();
                            if (int.TryParse(offsetHex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out int offset) &&
                                byte.TryParse(byteHex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out byte value))
                            {
                                byteFixes[offset] = value;
                            }
                        }
                    }
                }
            }

            LOGGER.Info($"[CollisionResize] Generating unified collision from {level.terrainEngine.fragments.Count} terrain fragments...");
            Console.WriteLine($"[CollisionResize] Generating unified collision from {level.terrainEngine.fragments.Count} terrain fragments...");

            ObjTerrainImporter.GenerateAndAssignCollision(level, level.terrainEngine.fragments);

            // --- PATCH: Apply byte fixes from report ---
            if (level.collBytesEngine != null && byteFixes.Count > 0)
            {
                foreach (var kvp in byteFixes)
                {
                    if (kvp.Key >= 0 && kvp.Key < level.collBytesEngine.Length)
                        level.collBytesEngine[kvp.Key] = kvp.Value;
                }
                LOGGER.Info($"[CollisionResize] Applied {byteFixes.Count} byte fixes from report {reportPath}");
                Console.WriteLine($"[CollisionResize] Applied {byteFixes.Count} byte fixes from report {reportPath}");
            }

            // Update chunk count for rendering and saving
            int newChunkCount = level.collisionChunks?.Count ?? 0;
            rendererPayload.visibility.chunks = new bool[newChunkCount];
            if (newChunkCount > 0)
            {
                Array.Fill(rendererPayload.visibility.chunks, true);
            }
            this.chunkCount = newChunkCount;

            // Refresh the renderer to show the new collision
            levelRenderer?.Dispose();
            levelRenderer = new LevelRenderer(shaderTable, textureIds);
            levelRenderer.Include(level);
            InvalidateView();

            LOGGER.Info($"[CollisionResize] Successfully generated collision. Chunks: {newChunkCount}, Bytes: {level.collBytesEngine?.Length ?? 0}");
            Console.WriteLine($"[CollisionResize] Successfully generated collision. Chunks: {newChunkCount}, Bytes: {level.collBytesEngine?.Length ?? 0}");
        }

        public static bool operator ==(LevelFrame a, LevelFrame b)
        {
            return a?.Equals(b) ?? b is null;
        }

        public static bool operator !=(LevelFrame a, LevelFrame b)
        {
            return !(a == b);
        }

        public override bool Equals(object? obj)
        {
            return obj is LevelFrame frame && frameName == frame.frameName;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(frameName);
        }

        public void SelectedObjectsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!selectedObjects.isOnlySplines && toolbox.type == ToolType.VertexTranslation)
                toolbox.ChangeTool(ToolType.None);
            InvalidateView();
        }

        public void DeleteObject(IEnumerable<LevelObject> levelObjects)
        {
            foreach (var obj in levelObjects)
                DeleteObject(obj);
        }

        public void DeleteObject(LevelObject levelObject)
        {
            /*selectedObjects.Remove(levelObject);
            switch (levelObject)
            {
                case Moby moby:
                    //objectTree.mobyNode.Nodes[level.mobs.IndexOf(moby)].Remove();
                    level.mobs.Remove(moby);
                    if (moby.pvarIndex != -1)
                    {
                        level.pVars.RemoveAt(moby.pvarIndex);
                    }

                    // Reinitializing the buffers is simple but slow
                    if (mobiesBuffers != null) foreach (MeshRenderer buffer in mobiesBuffers) buffer.Dispose();
                    mobiesBuffers = GetRenderableBuffer(level.mobs, RenderedObjectType.Moby);
                    break;
                case Tie tie:
                    //objectTree.tieNode.Nodes[level.ties.IndexOf(tie)].Remove();
                    level.ties.Remove(tie);
                    //level.ties.RemoveRange(1, level.ties.Count - 1);
                    //level.tieModels.RemoveRange(1, level.tieModels.Count - 1);
                    //level.ties.Clear();

                    // Reinitializing the buffers is simple but slow
                    if (tiesBuffers != null) foreach (MeshRenderer buffer in tiesBuffers) buffer.Dispose();
                    tiesBuffers = GetRenderableBuffer(level.ties, RenderedObjectType.Tie);
                    break;
                case Shrub shrub:
                    level.shrubs.Remove(shrub);
                    //level.shrubs.Clear();
                    //level.shrubModels.RemoveAt(level.shrubModels.Count -1);
                    //level.shrubModels.RemoveRange(5, level.shrubModels.Count - 5);

                    // Reinitializing the buffers is simple but slow
                    if (shrubsBuffers != null) foreach (MeshRenderer buffer in shrubsBuffers) buffer.Dispose();
                    shrubsBuffers = GetRenderableBuffer(level.shrubs, RenderedObjectType.Shrub);
                    break;
                case TerrainFragment tFrag:
                    break;
                case Spline spline:
                    level.splines.Remove(spline);
                    break;
                case Cuboid cuboid:
                    level.cuboids.Remove(cuboid);
                    break;
                case SoundInstance soundInstance:
                    level.soundInstances.Remove(soundInstance);
                    break;
            }

            InvalidateView();*/
        }

        private void HandleMouseWheelChanges()
        {
            if (toolbox.tool is not VertexTranslationTool tool) return;
            if (!selectedObjects.TryGetOne(out var obj) || obj is not Spline spline)
                return;

            int delta = (int) wnd.MouseState.ScrollDelta.Y;
            if (delta == 0)
                return;

            if (delta > 0 && tool.currentVertex < spline.GetVertexCount() - 1)
                tool.currentVertex++;
            else if (delta < 0 && tool.currentVertex > 0)
                tool.currentVertex--;

            InvalidateView();
        }

        public void CloneMoby(Moby moby)
        {
            if (!(moby.Clone() is Moby newMoby)) return;

            level.mobs.Add(newMoby);
            selectedObjects.Set(newMoby);
            InvalidateView();
        }

        private void HandleKeyboardShortcuts()
        {
            if (KEYMAP.IsPressed(Keybinds.ToolNone))
                toolbox.ChangeTool(ToolType.None);
            if (KEYMAP.IsPressed(Keybinds.ToolTranslate))
                toolbox.ChangeTool(ToolType.Translation);
            if (KEYMAP.IsPressed(Keybinds.ToolRotation))
                toolbox.ChangeTool(ToolType.Rotation);
            if (KEYMAP.IsPressed(Keybinds.ToolScaling))
                toolbox.ChangeTool(ToolType.Scaling);
            if (KEYMAP.IsPressed(Keybinds.ToolVertexTranslator))
                toolbox.ChangeTool(ToolType.VertexTranslation);
            if (KEYMAP.IsPressed(Keybinds.DeleteObject))
                DeleteObject(selectedObjects);
            if (KEYMAP.IsPressed(Keybinds.Copy))
                clipboard.Copy(selectedObjects);
            if (KEYMAP.IsPressed(Keybinds.Paste))
                clipboard.Apply(level, this);
        }

        private void HandleToolUpdates(Vector3 mouseRay, Vector3 direction)
        {
            if (toolbox.tool is BasicTransformTool basicTool)
            {
                TransformToolData toolData = new TransformToolData(camera, prevMouseRay, mouseRay, direction);
                basicTool.Transform(selectedObjects, toolData);
            }
            else if (toolbox.tool is VertexTranslationTool vertexTranslationTool)
            {
                Vector3 magnitude = mouseRay - prevMouseRay;
                vertexTranslationTool.Transform(selectedObjects, direction, magnitude);
                if (hook is { hookWorking: true } &&
                    selectedObjects.TryGetOne(out var obj) && obj is Spline spline)
                {
                    hook.HandleSplineTranslation(level, spline, vertexTranslationTool.currentVertex);
                }
            }

            selectedObjects.SetDirty();
            InvalidateView();
        }

        public void Tick(float deltaTime)
        {
            rendererPayload.deltaTime = deltaTime;

            if (interactiveSession && hookLiveUpdate && hook != null)
            {
                hook.UpdateMobys(level.mobs, level.mobyModels, this);
                if (hookUpdateCamera)
                {
                    hook.UpdateCamera(camera);
                }
                InvalidateView();
            }

            Point absoluteMousePos = new Point((int) wnd.MousePosition.X, (int) wnd.MousePosition.Y);
            var isWindowHovered = ImGui.IsWindowHovered();
            var isMouseInContentRegion = contentRegion.Contains(absoluteMousePos);
            // Allow rotation if the cursor is directly over the level frame,
            // otherwise defer handling to any foreground frames
            var isRotating = CheckForRotationInput(deltaTime, isWindowHovered);
            // If we're holding right click and rotating, the mouse can
            // leave the bounds of the window (GLFW CursorDisabled mode
            // allows the mouse to move freely). We want to keep rendering
            // in that case
            if (!isRotating && !(isWindowHovered && isMouseInContentRegion))
                return;

            HandleMouseWheelChanges();
            HandleKeyboardShortcuts();
            CheckForMovementInput(deltaTime);

            Vector3 mouseRay = MouseToWorldRay(camera.GetProjectionMatrix(), camera.GetViewMatrix(), new Size(width, height), mousePos);

            bool handled = HandleLeftMouseDown(mouseRay);

            if (!wnd.IsMouseButtonDown(MouseButton.Left) && boxSelecting)
            {
                PerformBoxSelect();
                boxSelecting = false;
            }

            if (!handled)
            {
                xLock = false;
                yLock = false;
                zLock = false;
            }

            lastMouseX = (int) wnd.MousePosition.X;
            lastMouseY = (int) wnd.MousePosition.Y;
            prevMouseRay = mouseRay;
        }

        private bool HandleLeftMouseDown(Vector3 mouseRay)
        {
            if (!wnd.IsMouseButtonDown(MouseButton.Left))
                return false;

            if (renderer == null) return false;

            LevelObject? obj = null;
            Vector3 direction = Vector3.Zero;

            renderer.ExposeFramebuffer(() => { obj = GetObjectAtScreenPosition(mousePos); });

            if (xLock)
                direction = Vector3.UnitX;
            else if (yLock)
                direction = Vector3.UnitY;
            else if (zLock)
                direction = Vector3.UnitZ;

            if (xLock || yLock || zLock)
            {
                HandleToolUpdates(mouseRay, direction);
            }
            else if (obj != null)
            {
                HandleSelect(obj);
            }
            else
            {
                if (!boxSelecting)
                {
                    boxSelecting = true;
                    boxSelectStart = mousePos;
                }
            }

            return true;
        }

        public bool HandleSelect(LevelObject? obj, bool externalCaller = false, bool pointCameraAtObject = false)
        {
            if (wnd.MouseState.WasButtonDown(MouseButton.Left) && !externalCaller)
                return false;

            bool isMultiSelect = KEYMAP.IsDown(Keybinds.MultiSelectModifier);

            if (obj == null)
            {
                if (!isMultiSelect)
                    selectedObjects.Clear();
                return false;
            }

            if (isMultiSelect)
            {
                selectedObjects.Toggle(obj);
            }
            else
            {
                selectedObjects.ToggleOne(obj);
                if (pointCameraAtObject)
                {
                    camera.MoveBehind(obj);
                }
            }

            return true;
        }

        private void PerformBoxSelect()
        {
            Vector2 start = boxSelectStart;
            Vector2 end = mousePos;
            Vector2 min = Vector2.ComponentMin(start, end);
            Vector2 max = Vector2.ComponentMax(start, end);

            bool isMultiSelect = KEYMAP.IsDown(Keybinds.MultiSelectModifier);
            if (!isMultiSelect)
                selectedObjects.Clear();

            Matrix4 view = camera.GetViewMatrix();
            Matrix4 proj = camera.GetProjectionMatrix();

            foreach (var coll in level.collisionObjects)
            {
                Vector2 screen = WorldToScreen(coll.position, view, proj, width, height);
                if (screen.X >= min.X && screen.X <= max.X && screen.Y >= min.Y && screen.Y <= max.Y)
                {
                    selectedObjects.Add(coll);
                }
            }

            InvalidateView();
        }

        private static Vector2 WorldToScreen(Vector3 world, Matrix4 view, Matrix4 proj, int width, int height)
        {
            // FIX: Use Matrix4.TransformRow to transform a Vector4 by a Matrix4
            Vector4 clip = Vector4.TransformRow(new Vector4(world, 1.0f), proj * view);
            if (clip.W == 0) return Vector2.Zero;
            Vector3 ndc = clip.Xyz / clip.W;
            return new Vector2(
                (ndc.X * 0.5f + 0.5f) * width,
                (1 - (ndc.Y * 0.5f + 0.5f)) * height
            );
        }

        private Vector3 GetInputAxes()
        {
            float xAxis = 0, yAxis = 0, zAxis = 0;

            if (KEYMAP.IsDown(Keybinds.MoveRight)) xAxis++;
            if (KEYMAP.IsDown(Keybinds.MoveLeft)) xAxis--;
            if (KEYMAP.IsDown(Keybinds.MoveForward)) yAxis++;
            if (KEYMAP.IsDown(Keybinds.MoveBackward)) yAxis--;
            if (KEYMAP.IsDown(Keybinds.MoveUp)) zAxis++;
            if (KEYMAP.IsDown(Keybinds.MoveDown)) zAxis--;

            var inputAxes = new Vector3(xAxis, yAxis, zAxis);
            inputAxes.NormalizeFast();
            return inputAxes;
        }

        private Vector2 GetInputRotationAxes()
        {
            float xAxis = 0, yAxis = 0;

            if (KEYMAP.IsDown(Keybinds.RotateRight)) xAxis++;
            if (KEYMAP.IsDown(Keybinds.RotateLeft)) xAxis--;
            if (KEYMAP.IsDown(Keybinds.RotateUp)) yAxis--;
            if (KEYMAP.IsDown(Keybinds.RotateDown)) yAxis++;

            var inputAxes = new Vector2(xAxis, yAxis);
            inputAxes.NormalizeFast();
            return inputAxes;
        }

        private void UpdateAaLevel()
        {
            if (antialiasing == 0)
                GL.Disable(EnableCap.Multisample);
            else
                GL.Enable(EnableCap.Multisample);

            FramebufferRenderer.MSAA_LEVEL = 1 << antialiasing;
        }

        protected void OnResize()
        {
            if (!initialized) return;
            GL.Viewport(0, 0, width, height);

            renderer?.Dispose();
            renderer = new FramebufferRenderer(width, height);
            UpdateAaLevel();
        }

        public LevelObject? GetObjectAtScreenPosition(Vector2 pos)
        {
            if (xLock || yLock || zLock) return null;

            int hit = 0;
            GL.ReadBuffer(ReadBufferMode.ColorAttachment1);
            GL.ReadPixels((int) pos.X, height - (int) pos.Y, 1, 1, PixelFormat.RedInteger, PixelType.Int, ref hit);

            if (hit == 0) return null;

            RenderedObjectType hitType = (RenderedObjectType) (hit >> 24);
            int hitId = hit & 0xffffff;

            switch (hitType)
            {
                case RenderedObjectType.Null:
                    return null;
                case RenderedObjectType.Terrain:
                    foreach (Terrain terrain in level.terrainChunks)
                    {
                        foreach (TerrainFragment fragment in terrain.fragments)
                        {
                            if (fragment.globalID == hitId) return fragment;
                        }
                    }
                    foreach (TerrainFragment fragment in level.terrainEngine.fragments)
                    {
                        if (fragment.globalID == hitId) return fragment;
                    }
                    return null;
                case RenderedObjectType.Shrub:
                    return level.shrubs.Find(x => x.globalID == hitId);
                case RenderedObjectType.Tie:
                    return level.ties.Find(x => x.globalID == hitId);
                case RenderedObjectType.Moby:
                    return level.mobs.Find(x => x.globalID == hitId);
                case RenderedObjectType.Spline:
                    return level.splines.Find(x => x.globalID == hitId);
                case RenderedObjectType.Cuboid:
                    return level.cuboids.Find(x => x.globalID == hitId);
                case RenderedObjectType.Sphere:
                    return level.spheres.Find(x => x.globalID == hitId);
                case RenderedObjectType.Cylinder:
                    return level.cylinders.Find(x => x.globalID == hitId);
                case RenderedObjectType.SoundInstance:
                    return level.soundInstances.Find(x => x.globalID == hitId);
                case RenderedObjectType.GameCamera:
                    return level.gameCameras.Find(x => x.globalID == hitId);
                case RenderedObjectType.PointLight:
                    return level.pointLights.Find(x => x.globalID == hitId);
                case RenderedObjectType.EnvSample:
                    return level.envSamples.Find(x => x.globalID == hitId);
                case RenderedObjectType.EnvTransition:
                    return level.envTransitions.Find(x => x.globalID == hitId);
                case RenderedObjectType.GrindPath:
                    return level.grindPaths.Find(x => x.globalID == hitId);
                case RenderedObjectType.Collision:
                    return level.collisionObjects.Find(x => x.globalID == hitId);
                case RenderedObjectType.Tool:
                    switch (hitId)
                    {
                        case 0: xLock = true; break;
                        case 1: yLock = true; break;
                        case 2: zLock = true; break;
                    }
                    InvalidateView();
                    return null;
                case RenderedObjectType.Skybox:
                    return null;
            }

            return null;
        }


        public void InvalidateView()
        {
            invalidate = true;
        }

        public bool StartMemoryHook(ref string message)
        {
            hook = new MemoryHook.MemoryHookHandle(level);

            message = hook.GetLastErrorMessage();

            if (hook.hookWorking)
            {
                interactiveSession = true;
            }

            return hook.hookWorking;
        }

        public bool HasValidHook()
        {
            return interactiveSession;
        }

        /// <summary>
        /// Export all distinct values of a select property found in a list of objects as a text file.
        /// </summary>
        private void ExportListOfProperties<T>(string path, IEnumerable<T> list, Func<T, string> selector)
        {
            if (path.Length <= 0) return;

            using (StreamWriter sw = File.CreateText(path))
            {
                HashSet<string> distinctItems = new HashSet<string>();
                foreach (T item in list)
                {
                    distinctItems.Add(selector(item));
                }

                foreach (string item in distinctItems)
                {
                    sw.WriteLine(item);
                }
            }
        }

        protected void OnPaint()
        {
            rendererPayload.SetWindowSize(width, height);
            levelRenderer?.Render(rendererPayload);
        }

        public void AddSubFrame(Frame frame)
        {
            if (!subFrames.Contains(frame)) subFrames.Add(frame);
        }

        private void CheckForMovementInput(float deltaTime)
        {
            float moveSpeed = KEYMAP.IsDown(Keybinds.MoveFastModifier) ? 40 : 10;
            Vector3 moveDir = GetInputAxes();
            if (moveDir.Length > 0)
            {
                moveDir *= moveSpeed * deltaTime;
                camera.TransformedTranslate(moveDir);

                InvalidateView();
            }

            Vector2 rotateDir = GetInputRotationAxes();
            if (rotateDir.Length > 0)
            {
                rotateDir *= deltaTime;
                camera.Rotate(rotateDir);

                InvalidateView();
            }
        }

        /// <summary>
        /// Remap textureConfig indices after a texture from the main level texture list is deleted.
        /// </summary>
        public void RemapTextureIndices(List<Texture> newTextures)
        {
            // Only remap indices if the modified list is the primary level texture list
            if (!ReferenceEquals(newTextures, level.textures))
                return;

            // Build a map from old Texture to new index
            var textureIndexMap = new Dictionary<Texture, int>();
            for (int i = 0; i < newTextures.Count; i++)
                textureIndexMap[newTextures[i]] = i;

            void RemapModelTextures(Model? model)
            {
                if (model?.textureConfig == null) return;
                foreach (var conf in model.textureConfig)
                {
                    if (conf.id >= 0 && conf.id < level.textures.Count)
                    {
                        var oldTex = level.textures[conf.id];
                        if (textureIndexMap.TryGetValue(oldTex, out int newIdx))
                            conf.id = newIdx;
                        else
                            conf.id = -1;
                    }
                    else
                    {
                        conf.id = -1;
                    }
                }
            }

            // Remap for terrainEngine fragments
            if (level.terrainEngine?.fragments != null)
            {
                foreach (var fragment in level.terrainEngine.fragments)
                    RemapModelTextures(fragment.model);
            }

            // Remap for terrainChunks fragments
            foreach (var terrain in level.terrainChunks)
            {
                foreach (var fragment in terrain.fragments)
                    RemapModelTextures(fragment.model);
            }

            // Remap for other model lists using level textures
            foreach (var model in level.tieModels)
                RemapModelTextures(model);

            foreach (var model in level.mobyModels)
                RemapModelTextures(model);

            if (level.shrubModels != null)
            {
                foreach (var model in level.shrubModels)
                    RemapModelTextures(model);
            }

            RemapModelTextures(level.skybox);
        }
    }

}
