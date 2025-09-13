// Copyright (C) 2018-2025, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.IO;
using System.Collections.Generic;
using ImGuiNET;
using Replanetizer.Utils;
using GeometrySwapper;
using LibReplanetizer;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;
using System.Linq;

namespace Replanetizer.Frames
{
    public class GeometrySwapperFrame : Frame
    {
        protected override string frameName { get; set; } = "R&C1 to R&C3 Geometry Swapper";

        // Settings class for serialization
        private class GeometrySwapperSettings
        {
            public string Rc1SourceLevelDir { get; set; }
            public string Rc3DonorLevelDir { get; set; }
            public string ReferenceUyaPlanetDir { get; set; }
            public string OutputDir { get; set; }
            public string GlobalRc3Dir { get; set; }
            public SwapOptions SelectedOptions { get; set; }
            
            // Advanced options
            public int TieSwapOptions { get; set; }
            public int ShrubSwapOptions { get; set; }
            public int SwingshotSwapOptions { get; set; }
            public int VendorSwapOptions { get; set; }
            public int CrateSwapOptions { get; set; } // Add this line
            public int MobyInstancerOptions { get; set; }
            public int SpecialMobyCopyOptions { get; set; } // Add this new property
            public int VendorModelSource { get; set; }
            public int CuboidImportOptions { get; set; }
            public int SplineImportOptions { get; set; }
            public int AnimationRestoreOptions { get; set; }
            
            // Planet options
            public string PlanetName { get; set; }
            public string CityName { get; set; }
            public int PlanetId { get; set; }
            public bool MarkPlanetAvailable { get; set; }
            public bool PatchAllText { get; set; }
            
            // Other options
            public bool OpenOutputWhenComplete { get; set; }
        }

        // Paths
        private string rc1SourceLevelDir = @"C:\Users\Ryan_\Downloads\temp\Oltanis_RaC1\";
        private string rc3DonorLevelDir = @"C:\Users\Ryan_\Downloads\temp\UYA_DONOR\";
        private string referenceUyaPlanetDir = @"C:\Users\Ryan_\Downloads\temp\Damosel\";
        private string outputDir = @"C:\Users\Ryan_\Downloads\temp\OltanisOnDaxxBase\";
        private string globalRc3Dir = @"D:\Projects\R&C1_to_R&C2_Planet_Format\Up_Your_Arsenal_PSARC\rc3\ps3data\global\";

        // Swap Options
        private SwapOptions selectedOptions = SwapOptions.None;
        private bool isSwapping = false;
        
        // Status tracking
        private string currentOperation = "";
        private StringBuilder consoleOutput = new StringBuilder();
        private float progress = 0.0f;
        private Dictionary<string, bool> operationStatus = new Dictionary<string, bool>();
        private bool swapCompleted = false;
        private bool swapSuccessful = false;
        
        // Console redirection
        private TextWriter originalConsoleOut;
        private StringWriter consoleRedirector;
        
        // Advanced options
        private string planetName = "Oltanis";
        private string cityName = "Gorda City Ruins";
        private int planetId = 27;
        private bool markPlanetAvailable = true;
        private bool patchAllText = false;
        private bool openOutputWhenComplete = true;

        // Advanced swapper options
        // Use FullSwap by default to include texture mapping
        private TieSwapper.TieSwapOptions tieSwapOptions = TieSwapper.TieSwapOptions.FullSwap;
        private ShrubSwapper.ShrubSwapOptions shrubSwapOptions = ShrubSwapper.ShrubSwapOptions.FullSwap;
        private SwingshotOltanisSwapper.SwingshotSwapOptions swingshotSwapOptions = SwingshotOltanisSwapper.SwingshotSwapOptions.Default;
        private VendorOltanisSwapper.VendorSwapOptions vendorSwapOptions = VendorOltanisSwapper.VendorSwapOptions.Default;
        private CratesOltanisSwapper.CrateSwapOptions crateSwapOptions = CratesOltanisSwapper.CrateSwapOptions.PlacementsAndTextures;
        private MobyOltanisInstancer.InstancerOptions mobyInstancerOptions = MobyOltanisInstancer.InstancerOptions.Default;
        private VendorOltanisSwapper.VendorModelSource vendorModelSource = VendorOltanisSwapper.VendorModelSource.RC1;
        private CuboidImporter.CuboidImportOptions cuboidImportOptions = CuboidImporter.CuboidImportOptions.Default;
        private SplineImporter.SplineImportOptions splineImportOptions = SplineImporter.SplineImportOptions.Default;
        private AnimationRestoreOptions animationRestoreOptions = AnimationRestoreOptions.Default;

        // Moby importer settings
        private string mobyImportTargetPath = "";
        private List<string> mobyImporterReferencePaths = new List<string>();
        private bool mobyImporterAllowOverwrite = false;
        private bool mobyImporterPreserveGrindPaths = true;
        private MobyImporter.ImportSelectionMode mobyImporterSelectionMode = MobyImporter.ImportSelectionMode.All;
        private string mobyImporterManualIds = "";

        // Adding new field to track special moby copy options
        [Flags]
        private enum SpecialMobyCopyOptions
        {
            None = 0,
            VendorOrb = 1,
            Vendor = 2,
            SwingshotNodes = 4,
            NanotechCrates = 8,
            AmmoVendors = 16,
            
            // Common combinations
            Default = None,
            All = VendorOrb | Vendor | SwingshotNodes | NanotechCrates | AmmoVendors
        }

        // Add this as a class field
        private SpecialMobyCopyOptions specialMobyCopyOptions = SpecialMobyCopyOptions.Default;

        public GeometrySwapperFrame(Window wnd) : base(wnd)
        {
            // Initialize operation status dictionary
            InitializeOperationStatus();
            
            // Set up console redirection
            originalConsoleOut = Console.Out;
            consoleRedirector = new StringWriter();
        }

        private void InitializeOperationStatus()
        {
            operationStatus["Terrain"] = false;
            operationStatus["Collision"] = false;
            operationStatus["Ties"] = false;
            operationStatus["Shrubs"] = false;
            operationStatus["Skybox"] = false;
            operationStatus["GrindPaths"] = false;
            operationStatus["PointLights"] = false;
            operationStatus["SoundInstances"] = false;
            operationStatus["Mobys"] = false;
            operationStatus["LevelVariables"] = false;
            operationStatus["RatchetPosition"] = false;
            operationStatus["PlanetMap"] = false;
            operationStatus["Vendor"] = false;
            operationStatus["Crates"] = false;
            operationStatus["Swingshots"] = false;
            operationStatus["ShipExitAnimation"] = false;
            operationStatus["Splines"] = false; // 🆕 ADD THIS LINE
            operationStatus["Cuboids"] = false;
            operationStatus["Animations"] = false;
        }

        public override void RenderAsWindow(float deltaTime)
        {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(800, 700), ImGuiCond.FirstUseEver);
            if (ImGui.Begin(frameName, ref isOpen))
            {
                Render(deltaTime);
                ImGui.End();
            }
        }

        public override void Render(float deltaTime)
        {
            if (ImGui.BeginTabBar("GeometrySwapperTabs"))
            {
                if (ImGui.BeginTabItem("Basic Settings"))
                {
                    RenderBasicSettings();
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItem("Advanced Settings"))
                {
                    RenderAdvancedSettings();
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItem("Console Output"))
                {
                    RenderConsoleOutput();
                    ImGui.EndTabItem();
                }
                
                ImGui.EndTabBar();
            }
        }
        
        private void RenderBasicSettings()
        {
            RenderPathInputs();
            ImGui.Separator();
            RenderSwapOptions();
            ImGui.Separator();
            RenderActions();
            
            if (isSwapping)
            {
                RenderProgressBar();
            }
            
            RenderOperationStatus();
        }
        
        private void RenderAdvancedSettings()
        {
            // Planet Registration Options
            if (ImGui.CollapsingHeader("Planet Registration Options", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.InputText("Planet Name", ref planetName, 100);
                ImGui.InputText("City Name", ref cityName, 100);
                ImGui.InputInt("Planet ID", ref planetId);
                ImGui.Checkbox("Mark Planet as Available", ref markPlanetAvailable);
                ImGui.Checkbox("Patch all_text File", ref patchAllText);
                
                ImGui.TextWrapped("These settings are used when the 'Register Planet in Map' option is enabled.");
            }
            
            // Swapper-Specific Options
            if (ImGui.CollapsingHeader("Swapper-Specific Options", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // TIE Swapper Options
                if (ImGui.TreeNode("TIE Swapper Options"))
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Configure TIE (Static Level Geometry) swapping behavior:");
                    ImGui.TextWrapped("TIEs are the main static geometry of levels. These options control how RC1 TIEs are imported.");
                    
                    bool useRC1Placements = tieSwapOptions.HasFlag(TieSwapper.TieSwapOptions.UseRC1Placements);
                    if (ImGui.Checkbox("Use RC1 TIE Placements", ref useRC1Placements))
                    {
                        if (useRC1Placements)
                            tieSwapOptions |= TieSwapper.TieSwapOptions.UseRC1Placements;
                        else
                            tieSwapOptions &= ~TieSwapper.TieSwapOptions.UseRC1Placements;
                    }
                    
                    bool useRC1Models = tieSwapOptions.HasFlag(TieSwapper.TieSwapOptions.UseRC1Models);
                    if (ImGui.Checkbox("Use RC1 TIE Models", ref useRC1Models))
                    {
                        if (useRC1Models)
                            tieSwapOptions |= TieSwapper.TieSwapOptions.UseRC1Models;
                        else
                            tieSwapOptions &= ~TieSwapper.TieSwapOptions.UseRC1Models;
                    }
                    
                    bool mapTextures = tieSwapOptions.HasFlag(TieSwapper.TieSwapOptions.MapTextures);
                    if (ImGui.Checkbox("Map RC1 TIE Textures to RC3 (Recommended)", ref mapTextures))
                    {
                        if (mapTextures)
                            tieSwapOptions |= TieSwapper.TieSwapOptions.MapTextures;
                        else
                            tieSwapOptions &= ~TieSwapper.TieSwapOptions.MapTextures;
                    }
                    ImGui.TextColored(new System.Numerics.Vector4(0.8f, 0.8f, 0.8f, 1.0f), "↳ This fixes texture issues and is highly recommended");
                    
                    // Preset buttons
                    ImGui.Spacing();
                    if (ImGui.Button("Placements Only"))
                        tieSwapOptions = TieSwapper.TieSwapOptions.PlacementsOnly;
                    
                    ImGui.SameLine();
                    if (ImGui.Button("Placements and Models"))
                        tieSwapOptions = TieSwapper.TieSwapOptions.PlacementsAndModels;
                        
                    ImGui.SameLine();
                    if (ImGui.Button("Full Swap"))
                        tieSwapOptions = TieSwapper.TieSwapOptions.FullSwap;
                        
                    ImGui.TreePop();
                }
                
                // Shrub Swapper Options
                if (ImGui.TreeNode("Shrub Swapper Options"))
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Configure shrub swapping behavior:");
                    
                    bool useRC1Placements = shrubSwapOptions.HasFlag(ShrubSwapper.ShrubSwapOptions.UseRC1Placements);
                    if (ImGui.Checkbox("Use RC1 Shrub Placements", ref useRC1Placements))
                    {
                        if (useRC1Placements)
                            shrubSwapOptions |= ShrubSwapper.ShrubSwapOptions.UseRC1Placements;
                        else
                            shrubSwapOptions &= ~ShrubSwapper.ShrubSwapOptions.UseRC1Placements;
                    }
                    
                    bool useRC1Models = shrubSwapOptions.HasFlag(ShrubSwapper.ShrubSwapOptions.UseRC1Models);
                    if (ImGui.Checkbox("Use RC1 Shrub Models", ref useRC1Models))
                    {
                        if (useRC1Models)
                            shrubSwapOptions |= ShrubSwapper.ShrubSwapOptions.UseRC1Models;
                        else
                            shrubSwapOptions &= ~ShrubSwapper.ShrubSwapOptions.UseRC1Models;
                    }
                    
                    bool mapTextures = shrubSwapOptions.HasFlag(ShrubSwapper.ShrubSwapOptions.MapTextures);
                    if (ImGui.Checkbox("Map RC1 Shrub Textures to RC2", ref mapTextures))
                    {
                        if (mapTextures)
                            shrubSwapOptions |= ShrubSwapper.ShrubSwapOptions.MapTextures;
                        else
                            shrubSwapOptions &= ~ShrubSwapper.ShrubSwapOptions.MapTextures;
                    }
                    
                    // Preset buttons
                    ImGui.Spacing();
                    if (ImGui.Button("Placements Only"))
                        shrubSwapOptions = ShrubSwapper.ShrubSwapOptions.PlacementsOnly;
                    
                    ImGui.SameLine();
                    if (ImGui.Button("Placements and Models"))
                        shrubSwapOptions = ShrubSwapper.ShrubSwapOptions.PlacementsAndModels;
                        
                    ImGui.SameLine();
                    if (ImGui.Button("Full Swap"))
                        shrubSwapOptions = ShrubSwapper.ShrubSwapOptions.FullSwap;
                        
                    ImGui.TreePop();
                }
                
                // Swingshot Swapper Options
                if (ImGui.TreeNode("Swingshot Swapper Options"))
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Configure swingshot swapping behavior:");
                    
                    bool repositionExisting = swingshotSwapOptions.HasFlag(SwingshotOltanisSwapper.SwingshotSwapOptions.RepositionExisting);
                    if (ImGui.Checkbox("Reposition Existing Swingshots", ref repositionExisting))
                    {
                        if (repositionExisting)
                            swingshotSwapOptions |= SwingshotOltanisSwapper.SwingshotSwapOptions.RepositionExisting;
                        else
                            swingshotSwapOptions &= ~SwingshotOltanisSwapper.SwingshotSwapOptions.RepositionExisting;
                    }
                    
                    bool createMissing = swingshotSwapOptions.HasFlag(SwingshotOltanisSwapper.SwingshotSwapOptions.CreateMissing);
                    if (ImGui.Checkbox("Create Missing Swingshots", ref createMissing))
                    {
                        if (createMissing)
                            swingshotSwapOptions |= SwingshotOltanisSwapper.SwingshotSwapOptions.CreateMissing;
                        else
                            swingshotSwapOptions &= ~SwingshotOltanisSwapper.SwingshotSwapOptions.CreateMissing;
                    }
                    
                    bool setLightToZero = swingshotSwapOptions.HasFlag(SwingshotOltanisSwapper.SwingshotSwapOptions.SetLightToZero);
                    if (ImGui.Checkbox("Set Light Value to Zero", ref setLightToZero))
                    {
                        if (setLightToZero)
                            swingshotSwapOptions |= SwingshotOltanisSwapper.SwingshotSwapOptions.SetLightToZero;
                        else
                            swingshotSwapOptions &= ~SwingshotOltanisSwapper.SwingshotSwapOptions.SetLightToZero;
                    }
                    
                    // Preset buttons
                    ImGui.Spacing();
                    if (ImGui.Button("Reposition Only"))
                        swingshotSwapOptions = SwingshotOltanisSwapper.SwingshotSwapOptions.RepositionOnly;
                        
                    ImGui.SameLine();
                    if (ImGui.Button("Full Swap"))
                        swingshotSwapOptions = SwingshotOltanisSwapper.SwingshotSwapOptions.FullSwap;
                        
                    ImGui.TreePop();
                }
                
                // Vendor Swapper Options
                if (ImGui.TreeNode("Vendor Swapper Options"))
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Configure vendor swapping behavior:");

                    int vendorSourceIndex = (int)vendorModelSource;
                    string[] vendorSources = { "RC1 Gadgetron", "RC3 Megacorp", "Keep Original" };
                    if (ImGui.Combo("Vendor Model Source", ref vendorSourceIndex, vendorSources, vendorSources.Length))
                        vendorModelSource = (VendorOltanisSwapper.VendorModelSource)vendorSourceIndex;

                    bool useSourcePosition = vendorSwapOptions.HasFlag(VendorOltanisSwapper.VendorSwapOptions.UseSourcePosition);
                    if (ImGui.Checkbox("Use Source Vendor Position", ref useSourcePosition))
                    {
                        if (useSourcePosition)
                            vendorSwapOptions |= VendorOltanisSwapper.VendorSwapOptions.UseSourcePosition;
                        else
                            vendorSwapOptions &= ~VendorOltanisSwapper.VendorSwapOptions.UseSourcePosition;
                    }
                    
                    bool useSourceModel = vendorSwapOptions.HasFlag(VendorOltanisSwapper.VendorSwapOptions.UseSourceModel);
                    if (ImGui.Checkbox("Use Source Vendor Model", ref useSourceModel))
                    {
                        if (useSourceModel)
                            vendorSwapOptions |= VendorOltanisSwapper.VendorSwapOptions.UseSourceModel;
                        else
                            vendorSwapOptions &= ~VendorOltanisSwapper.VendorSwapOptions.UseSourceModel;
                    }
                    
                    // Preset buttons
                    ImGui.Spacing();
                    if (ImGui.Button("Position Only"))
                        vendorSwapOptions = VendorOltanisSwapper.VendorSwapOptions.PositionOnly;
                        
                    ImGui.SameLine();
                    if (ImGui.Button("Full Replacement"))
                        vendorSwapOptions = VendorOltanisSwapper.VendorSwapOptions.FullReplacement;
                        
                    ImGui.TreePop();
                }
                
                // Crate Swapper Options
                if (ImGui.TreeNode("Crate Swapper Options"))
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Configure crate swapping behavior:");

                    bool useRC1Placements = crateSwapOptions.HasFlag(CratesOltanisSwapper.CrateSwapOptions.UseRC1Placements);
                    if (ImGui.Checkbox("Use RC1 Crate Placements", ref useRC1Placements))
                    {
                        if (useRC1Placements)
                            crateSwapOptions |= CratesOltanisSwapper.CrateSwapOptions.UseRC1Placements;
                        else
                            crateSwapOptions &= ~CratesOltanisSwapper.CrateSwapOptions.UseRC1Placements;
                    }

                    bool useRC1Textures = crateSwapOptions.HasFlag(CratesOltanisSwapper.CrateSwapOptions.UseRC1Textures);
                    if (ImGui.Checkbox("Use RC1 Crate Textures", ref useRC1Textures))
                    {
                        if (useRC1Textures)
                            crateSwapOptions |= CratesOltanisSwapper.CrateSwapOptions.UseRC1Textures;
                        else
                            crateSwapOptions &= ~CratesOltanisSwapper.CrateSwapOptions.UseRC1Textures;
                    }

                    // Preset buttons
                    ImGui.Spacing();
                    if (ImGui.Button("Placements Only"))
                        crateSwapOptions = CratesOltanisSwapper.CrateSwapOptions.PlacementsOnly;

                    ImGui.SameLine();
                    if (ImGui.Button("Placements and Textures (Recommended)"))
                        crateSwapOptions = CratesOltanisSwapper.CrateSwapOptions.PlacementsAndTextures;

                    ImGui.TreePop();
                }
                
                // Moby Instancer Options
                if (ImGui.TreeNode("Moby Instancer Options"))
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Configure moby instancing behavior:");
                    
                    bool setLightToZero = mobyInstancerOptions.HasFlag(MobyOltanisInstancer.InstancerOptions.SetLightToZero);
                    if (ImGui.Checkbox("Set Light Value to Zero", ref setLightToZero))
                    {
                        if (setLightToZero)
                            mobyInstancerOptions |= MobyOltanisInstancer.InstancerOptions.SetLightToZero;
                        else
                            mobyInstancerOptions &= ~MobyOltanisInstancer.InstancerOptions.SetLightToZero;
                    }
                    
                    bool useRC2Template = mobyInstancerOptions.HasFlag(MobyOltanisInstancer.InstancerOptions.UseRC2Template);
                    if (ImGui.Checkbox("Use Existing RC2 Moby as Template", ref useRC2Template))
                    {
                        if (useRC2Template)
                            mobyInstancerOptions |= MobyOltanisInstancer.InstancerOptions.UseRC2Template;
                        else
                            mobyInstancerOptions &= ~MobyOltanisInstancer.InstancerOptions.UseRC2Template;
                    }
                    
                    bool copyPvars = mobyInstancerOptions.HasFlag(MobyOltanisInstancer.InstancerOptions.CopyPvars);
                    if (ImGui.Checkbox("Copy PVars from Source Mobys", ref copyPvars))
                    {
                        if (copyPvars)
                            mobyInstancerOptions |= MobyOltanisInstancer.InstancerOptions.CopyPvars;
                        else
                            mobyInstancerOptions &= ~MobyOltanisInstancer.InstancerOptions.CopyPvars;
                    }
                    
                    // Preset buttons
                    ImGui.Spacing();
                    if (ImGui.Button("Default Configuration"))
                        mobyInstancerOptions = MobyOltanisInstancer.InstancerOptions.Default;
                        
                    ImGui.TreePop();
                }

                // Cuboid Importer Options
                if (ImGui.TreeNode("Cuboid Importer Options"))
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Configure cuboid import behavior:");

                    bool importAllCuboids = cuboidImportOptions.HasFlag(CuboidImporter.CuboidImportOptions.ImportAll);
                    if (ImGui.Checkbox("Import All Cuboids", ref importAllCuboids))
                    {
                        if (importAllCuboids)
                            cuboidImportOptions |= CuboidImporter.CuboidImportOptions.ImportAll;
                        else
                            cuboidImportOptions &= ~CuboidImporter.CuboidImportOptions.ImportAll;
                    }

                    bool skipShip = cuboidImportOptions.HasFlag(CuboidImporter.CuboidImportOptions.SkipShipAnimationCuboids);
                    if (ImGui.Checkbox("Skip Ship Animation Cuboids", ref skipShip))
                    {
                        if (skipShip)
                            cuboidImportOptions |= CuboidImporter.CuboidImportOptions.SkipShipAnimationCuboids;
                        else
                            cuboidImportOptions &= ~CuboidImporter.CuboidImportOptions.SkipShipAnimationCuboids;
                    }

                    bool replaceExisting = cuboidImportOptions.HasFlag(CuboidImporter.CuboidImportOptions.ReplaceExisting);
                    if (ImGui.Checkbox("Replace Existing Cuboids", ref replaceExisting))
                    {
                        if (replaceExisting)
                            cuboidImportOptions |= CuboidImporter.CuboidImportOptions.ReplaceExisting;
                        else
                            cuboidImportOptions &= ~CuboidImporter.CuboidImportOptions.ReplaceExisting;
                    }

                    bool preserveIds = cuboidImportOptions.HasFlag(CuboidImporter.CuboidImportOptions.PreserveOriginalIDs);
                    if (ImGui.Checkbox("Preserve Original IDs", ref preserveIds))
                    {
                        if (preserveIds)
                            cuboidImportOptions |= CuboidImporter.CuboidImportOptions.PreserveOriginalIDs;
                        else
                            cuboidImportOptions &= ~CuboidImporter.CuboidImportOptions.PreserveOriginalIDs;
                    }

                    ImGui.Spacing();
                    if (ImGui.Button("Default"))
                        cuboidImportOptions = CuboidImporter.CuboidImportOptions.Default;
                    ImGui.SameLine();
                    if (ImGui.Button("Replace All"))
                        cuboidImportOptions = CuboidImporter.CuboidImportOptions.ReplaceAll;

                    ImGui.TreePop();
                }

                // Spline Importer Options
                if (ImGui.TreeNode("Spline Importer Options"))
                {
                    ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Configure spline import behavior:");

                    bool importAllSplines = splineImportOptions.HasFlag(SplineImporter.SplineImportOptions.ImportAll);
                    if (ImGui.Checkbox("Import All Splines", ref importAllSplines))
                    {
                        if (importAllSplines)
                            splineImportOptions |= SplineImporter.SplineImportOptions.ImportAll;
                        else
                            splineImportOptions &= ~SplineImporter.SplineImportOptions.ImportAll;
                    }

                    bool skipShipPath = splineImportOptions.HasFlag(SplineImporter.SplineImportOptions.SkipShipPathSplines);
                    if (ImGui.Checkbox("Skip Ship Path Splines", ref skipShipPath))
                    {
                        if (skipShipPath)
                            splineImportOptions |= SplineImporter.SplineImportOptions.SkipShipPathSplines;
                        else
                            splineImportOptions &= ~SplineImporter.SplineImportOptions.SkipShipPathSplines;
                    }

                    bool replaceSpline = splineImportOptions.HasFlag(SplineImporter.SplineImportOptions.ReplaceExisting);
                    if (ImGui.Checkbox("Replace Existing Splines", ref replaceSpline))
                    {
                        if (replaceSpline)
                            splineImportOptions |= SplineImporter.SplineImportOptions.ReplaceExisting;
                        else
                            splineImportOptions &= ~SplineImporter.SplineImportOptions.ReplaceExisting;
                    }

                    bool preserveSplineIds = splineImportOptions.HasFlag(SplineImporter.SplineImportOptions.PreserveOriginalIDs);
                    if (ImGui.Checkbox("Preserve Original IDs", ref preserveSplineIds))
                    {
                        if (preserveSplineIds)
                            splineImportOptions |= SplineImporter.SplineImportOptions.PreserveOriginalIDs;
                        else
                            splineImportOptions &= ~SplineImporter.SplineImportOptions.PreserveOriginalIDs;
                    }

                    ImGui.Spacing();
                    if (ImGui.Button("Default##Spline"))
                        splineImportOptions = SplineImporter.SplineImportOptions.Default;
                    ImGui.SameLine();
                    if (ImGui.Button("Replace All##Spline"))
                        splineImportOptions = SplineImporter.SplineImportOptions.ReplaceAll;

                    ImGui.TreePop();
                }
            }
            
            // Special Moby Copy Options
            if (ImGui.CollapsingHeader("Special Moby Copy Options", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Configure which special mobys to copy:");
                
                ImGui.TextWrapped("These settings control which special mobys will be copied when the 'Copy Special Mobys Only' option is enabled.");
                ImGui.TextWrapped("Note: Only the Vendor Orb has been thoroughly tested and is recommended.");
                
                ImGui.Spacing();
                
                bool vendorOrb = specialMobyCopyOptions.HasFlag(SpecialMobyCopyOptions.VendorOrb);
                if (ImGui.Checkbox("Vendor Orb (Recommended)", ref vendorOrb))
                {
                    if (vendorOrb)
                        specialMobyCopyOptions |= SpecialMobyCopyOptions.VendorOrb;
                    else
                        specialMobyCopyOptions &= ~SpecialMobyCopyOptions.VendorOrb;
                }
                
                bool vendor = specialMobyCopyOptions.HasFlag(SpecialMobyCopyOptions.Vendor);
                if (ImGui.Checkbox("Vendor (Untested)", ref vendor))
                {
                    if (vendor)
                        specialMobyCopyOptions |= SpecialMobyCopyOptions.Vendor;
                    else
                        specialMobyCopyOptions &= ~SpecialMobyCopyOptions.Vendor;
                }
                
                bool swingshotNodes = specialMobyCopyOptions.HasFlag(SpecialMobyCopyOptions.SwingshotNodes);
                if (ImGui.Checkbox("Swingshot Nodes (Untested)", ref swingshotNodes))
                {
                    if (swingshotNodes)
                        specialMobyCopyOptions |= SpecialMobyCopyOptions.SwingshotNodes;
                    else
                        specialMobyCopyOptions &= ~SpecialMobyCopyOptions.SwingshotNodes;
                }
                
                bool nanotechCrates = specialMobyCopyOptions.HasFlag(SpecialMobyCopyOptions.NanotechCrates);
                if (ImGui.Checkbox("Nanotech Crates (Untested)", ref nanotechCrates))
                {
                    if (nanotechCrates)
                        specialMobyCopyOptions |= SpecialMobyCopyOptions.NanotechCrates;
                    else
                        specialMobyCopyOptions &= ~SpecialMobyCopyOptions.NanotechCrates;
                }
                
                bool ammoVendors = specialMobyCopyOptions.HasFlag(SpecialMobyCopyOptions.AmmoVendors);
                if (ImGui.Checkbox("Ammo Vendors (Untested)", ref ammoVendors))
                {
                    if (ammoVendors)
                        specialMobyCopyOptions |= SpecialMobyCopyOptions.AmmoVendors;
                    else
                        specialMobyCopyOptions &= ~SpecialMobyCopyOptions.AmmoVendors;
                }
                
                // Preset buttons
                ImGui.Spacing();
                if (ImGui.Button("Vendor Orb Only (Recommended)"))
                    specialMobyCopyOptions = SpecialMobyCopyOptions.VendorOrb;
                
                ImGui.SameLine();
                if (ImGui.Button("All Special Mobys (Untested)"))
                    specialMobyCopyOptions = SpecialMobyCopyOptions.All;

                ImGui.SameLine();
                if (ImGui.Button("None"))
                    specialMobyCopyOptions = SpecialMobyCopyOptions.None;
            }

            if (ImGui.CollapsingHeader("Animation Restoration Options", ImGuiTreeNodeFlags.DefaultOpen))
            {
                bool restorePlayer = animationRestoreOptions.HasFlag(AnimationRestoreOptions.PlayerAnimations);
                if (ImGui.Checkbox("Restore Player Animations", ref restorePlayer))
                {
                    if (restorePlayer)
                        animationRestoreOptions |= AnimationRestoreOptions.PlayerAnimations;
                    else
                        animationRestoreOptions &= ~AnimationRestoreOptions.PlayerAnimations;
                }

                bool restoreRatchet = animationRestoreOptions.HasFlag(AnimationRestoreOptions.RatchetModelAnimations);
                if (ImGui.Checkbox("Restore Ratchet Model Animations", ref restoreRatchet))
                {
                    if (restoreRatchet)
                        animationRestoreOptions |= AnimationRestoreOptions.RatchetModelAnimations;
                    else
                        animationRestoreOptions &= ~AnimationRestoreOptions.RatchetModelAnimations;
                }

                bool preserveExisting = animationRestoreOptions.HasFlag(AnimationRestoreOptions.PreserveTargetAnimations);
                if (ImGui.Checkbox("Preserve Existing Animations", ref preserveExisting))
                {
                    if (preserveExisting)
                        animationRestoreOptions |= AnimationRestoreOptions.PreserveTargetAnimations;
                    else
                        animationRestoreOptions &= ~AnimationRestoreOptions.PreserveTargetAnimations;
                }

                ImGui.Spacing();
                if (ImGui.Button("Default##Anim"))
                    animationRestoreOptions = AnimationRestoreOptions.Default;
            }

            if (ImGui.CollapsingHeader("Moby Importer", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.InputText("Target Level", ref mobyImportTargetPath, 260);
                ImGui.SameLine();
                if (ImGui.Button("Browse##MobyTarget"))
                    mobyImportTargetPath = CrossFileDialog.OpenFile("Select engine.ps3", ".ps3") ?? mobyImportTargetPath;
                // Reference levels
                for (int i = 0; i < mobyImporterReferencePaths.Count; i++)
                {
                    string refPath = mobyImporterReferencePaths[i];
                    ImGui.InputText($"Reference Level {i + 1}", ref refPath, 260);
                    mobyImporterReferencePaths[i] = refPath;
                    ImGui.SameLine();
                    if (ImGui.Button($"Browse##Ref{i}"))
                        mobyImporterReferencePaths[i] = CrossFileDialog.OpenFile("Select engine.ps3", ".ps3") ?? mobyImporterReferencePaths[i];
                    ImGui.SameLine();
                    if (ImGui.Button($"X##Ref{i}"))
                        mobyImporterReferencePaths.RemoveAt(i);
                }
                if (ImGui.Button("Add Reference Level"))
                    mobyImporterReferencePaths.Add("");

                ImGui.Checkbox("Allow Overwrite", ref mobyImporterAllowOverwrite);
                ImGui.Checkbox("Preserve Grind Paths", ref mobyImporterPreserveGrindPaths);

                int modeIndex = (int)mobyImporterSelectionMode;
                string[] modeLabels = { "All", "RC3 Only", "RC2 Only", "RC1 Only", "Common Only", "Uncommon Only", "Manual" };
                ImGui.Combo("Import Mode", ref modeIndex, modeLabels, modeLabels.Length);
                mobyImporterSelectionMode = (MobyImporter.ImportSelectionMode)modeIndex;

                if (mobyImporterSelectionMode == MobyImporter.ImportSelectionMode.Manual)
                {
                    ImGui.InputText("Manual Moby IDs (comma)", ref mobyImporterManualIds, 260);
                }

                if (ImGui.Button("Run Moby Importer"))
                    RunMobyImporter();
            }

            // Post-Process Options
            if (ImGui.CollapsingHeader("Post-Process Options", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Checkbox("Open Output Directory When Complete", ref openOutputWhenComplete);
            }
            
            // Information
            if (ImGui.CollapsingHeader("Information", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextWrapped("This tool transfers elements from a Ratchet & Clank 1 level to a Ratchet & Clank 3 level.");
                ImGui.TextWrapped("Source code originally from RaC1to2GeometrySwap.cs by KaiokenBlueGT.");
                
                ImGui.Spacing();
                ImGui.TextWrapped("Use the advanced options tabs above to configure specific behavior for each swapper component.");
            }
        }
        
        private void RenderConsoleOutput()
        {
            // Top button controls
            if (ImGui.Button("Clear Console"))
            {
                consoleOutput.Clear();
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Copy to Clipboard"))
            {
                ImGui.SetClipboardText(consoleOutput.ToString());
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Save to File..."))
            {
                string logFile = CrossFileDialog.SaveFile("Save Console Log", ".txt");
                if (!string.IsNullOrEmpty(logFile))
                {
                    File.WriteAllText(logFile, consoleOutput.ToString());
                }
            }

            // Main console display area
            ImGui.BeginChild("ConsoleOutputText", new System.Numerics.Vector2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
            ImGui.TextUnformatted(consoleOutput.ToString());
            
            // Auto-scroll to bottom
            if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20)
                ImGui.SetScrollHereY(1.0f);
                
            ImGui.EndChild();
        }

        private void RunMobyImporter()
        {
            Task.Run(() =>
            {
                try
                {
                    Console.SetOut(consoleRedirector);
                    AddToConsoleOutput(">>> Starting Moby Importer <<<");

                    if (!File.Exists(mobyImportTargetPath))
                    {
                        AddToConsoleOutput($"❌ Target level not found: {mobyImportTargetPath}");
                        return;
                    }
                    if (mobyImporterReferencePaths.Count == 0)
                    {
                        AddToConsoleOutput("❌ No reference levels provided.");
                        return;
                    }

                    Level targetLevel = new Level(mobyImportTargetPath);
                    var refs = mobyImporterReferencePaths.Where(p => File.Exists(p)).ToList();
                    if (refs.Count == 0)
                    {
                        AddToConsoleOutput("❌ Reference levels not found.");
                        return;
                    }

                    List<int> manualIds = mobyImporterManualIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => { return int.TryParse(s.Trim(), out int id) ? (int?)id : null; })
                        .Where(id => id.HasValue)
                        .Select(id => id!.Value)
                        .ToList();

                    bool success = MobyImporter.ImportMobysFromMixedReferenceLevels(
                        targetLevel,
                        refs,
                        mobyImporterAllowOverwrite,
                        mobyImporterPreserveGrindPaths,
                        mobyImporterSelectionMode,
                        manualIds);

                    AddToConsoleOutput(success ? "✅ Moby import completed" : "❌ Moby import failed");

                    if (success)
                    {
                        targetLevel.Save(mobyImportTargetPath);
                        AddToConsoleOutput("✅ Target level saved");
                    }
                }
                catch (Exception ex)
                {
                    AddToConsoleOutput($"Error running Moby importer: {ex.Message}");
                }
                finally
                {
                    Console.SetOut(originalConsoleOut);
                }
            });
        }

        private void RenderPathInputs()
        {
            if (ImGui.CollapsingHeader("Paths", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.InputText("RC1 Source Level", ref rc1SourceLevelDir, 260);
                ImGui.SameLine();
                if (ImGui.Button("Browse##RC1Source")) { rc1SourceLevelDir = CrossFileDialog.OpenFolder(rc1SourceLevelDir) ?? rc1SourceLevelDir; }

                ImGui.InputText("RC3 Donor Level", ref rc3DonorLevelDir, 260);
                ImGui.SameLine();
                if (ImGui.Button("Browse##RC2Donor")) { rc3DonorLevelDir = CrossFileDialog.OpenFolder(rc3DonorLevelDir) ?? rc3DonorLevelDir; }

                ImGui.InputText("RC3 Reference Level", ref referenceUyaPlanetDir, 260);
                ImGui.SameLine();
                if (ImGui.Button("Browse##RC2Ref")) { referenceUyaPlanetDir = CrossFileDialog.OpenFolder(referenceUyaPlanetDir) ?? referenceUyaPlanetDir; }

                ImGui.InputText("Global RC3 Directory", ref globalRc3Dir, 260);
                ImGui.SameLine();
                if (ImGui.Button("Browse##Global")) { globalRc3Dir = CrossFileDialog.OpenFolder(globalRc3Dir) ?? globalRc3Dir; }

                ImGui.InputText("Output Directory", ref outputDir, 260);
                ImGui.SameLine();
                if (ImGui.Button("Browse##Output")) { outputDir = CrossFileDialog.OpenFolder(outputDir) ?? outputDir; }
            }
        }

        private void RenderSwapOptions()
        {
            if (ImGui.CollapsingHeader("Swap Options", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Working options
                ImGui.TextColored(new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f), "Stable Options:");
                var workingOptionsMap = new Dictionary<string, SwapOptions>
                {
                    { "Terrain", SwapOptions.Terrain },
                    { "Collision", SwapOptions.Collision },
                    { "Ties", SwapOptions.Ties },
                    { "Shrubs", SwapOptions.Shrubs },
                    { "Skybox", SwapOptions.Skybox },
                    { "Grind Paths", SwapOptions.GrindPaths },
                    { "Point Lights", SwapOptions.PointLights },
                    { "Mobys", SwapOptions.Mobys },
                    { "Swap Level Variables", SwapOptions.SwapLevelVariables },
                    { "Transfer Ratchet Position", SwapOptions.TransferRatchetPosition },
                    { "Swap Vendor with Oltanis", SwapOptions.SwapVendorWithOltanis },
                    { "Swap Crates with Oltanis", SwapOptions.SwapCratesWithOltanis },
                    { "Swap Ship Exit Animation", SwapOptions.SwapShipExitAnim },
                    { "Copy Special Mobys Only", SwapOptions.CopySpecialMobysOnly },
                    { "Import RC1 Cuboids", SwapOptions.ImportCuboids },
                    { "Import RC1 Splines", SwapOptions.ImportSplines },
                    { "Run Ship Camera Diagnostics", SwapOptions.RunShipCameraDiagnostics },
                };

                RenderOptionGroup(workingOptionsMap);
                
                ImGui.Separator();
                
                // Experimental options
                ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Experimental Options:");
                var experimentalOptionsMap = new Dictionary<string, SwapOptions>
                {
                    { "Use Moby Converter", SwapOptions.UseMobyConverter },
                    { "Create Moby Instances", SwapOptions.CreateMobyInstances },
                    { "Run Grind Path Diagnostics", SwapOptions.RunGrindPathDiagnostics },
                    { "Restore Animations", SwapOptions.RestoreAnimations },
                    { "Future to UYA Conversion", SwapOptions.FutureToUyaConversion },
                };
                
                RenderOptionGroup(experimentalOptionsMap);
                
                ImGui.Separator();
                
                // Non-working options
                ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.0f, 0.0f, 1.0f), "Non-Working Options:");
                var nonWorkingOptionsMap = new Dictionary<string, SwapOptions>
                {
                    { "Sound Instances", SwapOptions.SoundInstances },
                    { "Swap Swingshots", SwapOptions.SwapSwingshots },
                    { "Register Planet in Map", SwapOptions.RegisterPlanetInMap },
                    { "RC2 Self Test", SwapOptions.RC2SelfTest },
                };
                
                RenderOptionGroup(nonWorkingOptionsMap);

                ImGui.Separator();
                bool allChecked = selectedOptions.HasFlag(SwapOptions.All);
                if (ImGui.Checkbox("All Stable Options", ref allChecked))
                {
                    if (allChecked)
                    {
                        // Set all working options
                        foreach (var option in workingOptionsMap.Values)
                        {
                            selectedOptions |= option;
                        }
                    }
                    else
                    {
                        // Clear all options
                        selectedOptions = SwapOptions.None;
                    }
                }
            }
        }

        private void RenderOptionGroup(Dictionary<string, SwapOptions> optionsMap)
        {
            float columnWidth = ImGui.GetContentRegionAvail().X / 2.0f - 10.0f;
            int itemCount = 0;

            foreach (var (label, option) in optionsMap)
            {
                bool isChecked = selectedOptions.HasFlag(option);
                
                // Create 2 columns of checkboxes
                if (itemCount % 2 == 0)
                {
                    ImGui.BeginGroup();
                }
                else
                {
                    ImGui.SameLine();
                    ImGui.BeginGroup();
                }
                
                if (ImGui.Checkbox(label, ref isChecked))
                {
                    if (isChecked)
                        selectedOptions |= option;
                    else
                        selectedOptions &= ~option;
                }
                
                ImGui.EndGroup();
                itemCount++;
            }
        }

        private void RenderActions()
        {
            if (isSwapping)
            {
                ImGui.Text("Swapping in progress... Current operation: " + currentOperation);
            }
            else
            {
                if (ImGui.Button("Run Geometry Swap", new System.Numerics.Vector2(150, 30)))
                {
                    StartGeometrySwap();
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Save Settings", new System.Numerics.Vector2(120, 30)))
            {
                SaveSettings();
            }

            ImGui.SameLine();

            if (ImGui.Button("Load Settings", new System.Numerics.Vector2(120, 30)))
            {
                LoadSettings();
            }
            
            // Show open output folder button if swap completed successfully
            if (swapCompleted && swapSuccessful)
            {
                ImGui.SameLine();
                
                if (ImGui.Button("Open Output Folder", new System.Numerics.Vector2(150, 30)))
                {
                    OpenOutputFolder();
                }
            }
        }
        
        private void RenderProgressBar()
        {
            ImGui.Spacing();
            ImGui.Text("Progress:");

            ImGui.ProgressBar(progress, new System.Numerics.Vector2(-1, 0), $"{progress * 100:F0}%");
            ImGui.TextWrapped(currentOperation);
        }
        
        private void RenderOperationStatus()
        {
            if (!swapCompleted) return;
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text(swapSuccessful ? "✅ Swap completed successfully!" : "❌ Swap completed with errors");
            ImGui.Spacing();
            
            ImGui.BeginTable("OperationStatusTable", 3, ImGuiTableFlags.Borders);
            
            ImGui.TableSetupColumn("Operation");
            ImGui.TableSetupColumn("Status");
            ImGui.TableSetupColumn("Details");
            ImGui.TableHeadersRow();
            
            foreach (var operation in operationStatus)
            {
                ImGui.TableNextRow();
                
                // Operation name
                ImGui.TableNextColumn();
                ImGui.Text(operation.Key);
                
                // Status indicator
                ImGui.TableNextColumn();
                ImGui.Text(operation.Value ? "✅" : "⏭️");
                
                // Details (placeholder for now)
                ImGui.TableNextColumn();
                if (operation.Value)
                {
                    ImGui.Text("Successfully completed");
                }
                else
                {
                    ImGui.Text("Skipped or not selected");
                }
            }
            
            ImGui.EndTable();
        }

        private void StartGeometrySwap()
        {
            // Reset status
            isSwapping = true;
            swapCompleted = false;
            swapSuccessful = false;
            progress = 0.0f;
            currentOperation = "Initializing...";
            consoleOutput.Clear();
            InitializeOperationStatus();
            
            // Set up console redirection
            Console.SetOut(consoleRedirector);
            
            // Apply custom options before starting the swap
            ApplyCustomSwapperOptions();
            
            // Run the swap in a background task
            Task.Run(() =>
            {
                try
                {
                    PerformGeometrySwap();
                    swapSuccessful = true;
                }
                catch (Exception ex)
                {
                    AddToConsoleOutput($"Error during geometry swap: {ex.Message}");
                    AddToConsoleOutput(ex.StackTrace);
                    swapSuccessful = false;
                }
                finally
                {
                    // Restore original console output
                    Console.SetOut(originalConsoleOut);
                    isSwapping = false;
                    swapCompleted = true;
                    
                    if (swapSuccessful && openOutputWhenComplete)
                    {
                        OpenOutputFolder();
                    }
                }
            });
        }

        private void ApplyCustomSwapperOptions()
        {
            try
            {
                // Use the public method instead of reflection
                SwapHelper.SetSwapperOptions(
                    tieSwapOptions,
                    shrubSwapOptions,
                    swingshotSwapOptions,
                    vendorSwapOptions,
                    crateSwapOptions,
                    mobyInstancerOptions,
                    (int)specialMobyCopyOptions,
                    vendorModelSource,
                    cuboidImportOptions,
                    splineImportOptions,
                    animationRestoreOptions);
        
                // Log what we're using
                AddToConsoleOutput($"Applied custom swapper options:");
                AddToConsoleOutput($"TIE Options: {tieSwapOptions}");
                AddToConsoleOutput($"Shrub Options: {shrubSwapOptions}");
                AddToConsoleOutput($"Swingshot Options: {swingshotSwapOptions}");
                AddToConsoleOutput($"Vendor Options: {vendorSwapOptions}");
                AddToConsoleOutput($"Crate Options: {crateSwapOptions}");
                AddToConsoleOutput($"Moby Instancer Options: {mobyInstancerOptions}");
                AddToConsoleOutput($"Special Moby Copy Options: {specialMobyCopyOptions}");
                AddToConsoleOutput($"Vendor Model Source: {vendorModelSource}");
                AddToConsoleOutput($"Cuboid Import Options: {cuboidImportOptions}");
                AddToConsoleOutput($"Spline Import Options: {splineImportOptions}");
                AddToConsoleOutput($"Animation Restore Options: {animationRestoreOptions}");
            }
            catch (Exception ex)
            {
                AddToConsoleOutput($"Warning: Could not apply custom swapper options: {ex.Message}");
                AddToConsoleOutput("Default options will be used instead.");
            }
        }
        
        private void PerformGeometrySwap()
        {
            AddToConsoleOutput(">>> Starting R&C1 to R&C3/UYA Geometry Swap <<<");
            AddToConsoleOutput($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            AddToConsoleOutput("Checking input paths...");
            
            // Verify that paths are valid
            if (!Directory.Exists(rc1SourceLevelDir))
            {
                AddToConsoleOutput($"❌ RC1 Source directory not found: {rc1SourceLevelDir}");
                return;
            }
            
            if (!Directory.Exists(rc3DonorLevelDir))
            {
                AddToConsoleOutput($"❌ RC2 Donor directory not found: {rc3DonorLevelDir}");
                return;
            }
            
            if (!Directory.Exists(referenceUyaPlanetDir))
            {
                AddToConsoleOutput($"❌ RC2 Reference directory not found: {referenceUyaPlanetDir}");
                return;
            }
            
            // Create output directory if it doesn't exist
            Directory.CreateDirectory(outputDir);
            
            // Call the SwapHelper
            SwapHelper.PerformSwap(
                rc1SourceLevelDir, 
                rc3DonorLevelDir, 
                referenceUyaPlanetDir, 
                globalRc3Dir, 
                outputDir, 
                selectedOptions,
                UpdateProgress,
                UpdateOperationStatus
            );
            
            // Special handling for planet registration if needed
            if (selectedOptions.HasFlag(SwapOptions.RegisterPlanetInMap))
            {
                UpdateProgress("Registering planet in galactic map...", 0.95f);
                
                try
                {
                    string outputEnginePath = Path.Combine(outputDir, "engine.ps3");
                    var level = new LibReplanetizer.Level(outputEnginePath);
                    
                    bool success = GalacticMapManager.AddPlanetToGalacticMap(
                        level,
                        planetName,
                        cityName,
                        planetId,
                        markPlanetAvailable,
                        patchAllText,
                        globalRc3Dir
                    );
                    
                    if (success)
                    {
                        AddToConsoleOutput($"✅ Planet '{planetName}' registered successfully with ID {planetId}");
                        UpdateOperationStatus("PlanetMap", true);
                        
                        // Save the level again
                        level.Save(outputDir);
                    }
                    else
                    {
                        AddToConsoleOutput($"❌ Failed to register planet '{planetName}' in the galactic map");
                        UpdateOperationStatus("PlanetMap", false);
                    }
                }
                catch (Exception ex)
                {
                    AddToConsoleOutput($"❌ Error during planet registration: {ex.Message}");
                }
            }
            
            UpdateProgress("Swap completed!", 1.0f);
            AddToConsoleOutput(">>> Geometry Swap completed! <<<");
        }
        
        private void UpdateProgress(string operation, float progressValue)
        {
            currentOperation = operation;
            progress = progressValue;
            AddToConsoleOutput(operation);
        }

        private void UpdateOperationStatus(string operation, bool completed)
        {
            // Map the operation name to our dictionary key
            string key = operation switch
            {
                "Terrain" => "Terrain",
                "Collision" => "Collision",
                "Ties" => "Ties",
                "Shrubs" => "Shrubs",
                "Skybox" => "Skybox",
                "GrindPaths" => "GrindPaths",
                "PointLights" => "PointLights",
                "SoundInstances" => "SoundInstances",
                "Mobys" => "Mobys",
                "LevelVariables" => "LevelVariables",
                "RatchetPosition" => "RatchetPosition",
                "PlanetMap" => "PlanetMap",
                "Vendor" => "Vendor",
                "Crates" => "Crates",
                "Swingshots" => "Swingshots",
                "ShipExitAnimation" => "ShipExitAnimation",
                "Splines" => "Splines", // 🆕 ADD THIS LINE
                "Cuboids" => "Cuboids",
                "Animations" => "Animations",
                "SpecialMobys" => "Mobys",  // Map SpecialMobys to the Mobys status
                _ => null
            };

            if (key != null && operationStatus.ContainsKey(key))
            {
                operationStatus[key] = completed;
            }
        }

        private void AddToConsoleOutput(string message)
        {
            // Get the redirected console output
            string consoleText = consoleRedirector.ToString();
            consoleRedirector.GetStringBuilder().Clear();
            
            if (!string.IsNullOrEmpty(consoleText))
            {
                consoleOutput.Append(consoleText);
            }
            
            // Add the new message if provided
            if (!string.IsNullOrEmpty(message))
            {
                consoleOutput.AppendLine(message);
            }
        }
        
        private void OpenOutputFolder()
        {
            if (Directory.Exists(outputDir))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = outputDir,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch (Exception ex)
                {
                    AddToConsoleOutput($"Failed to open output directory: {ex.Message}");
                }
            }
        }

        private void SaveSettings()
        {
            var settings = new GeometrySwapperSettings
            {
                Rc1SourceLevelDir = rc1SourceLevelDir,
                Rc3DonorLevelDir = rc3DonorLevelDir,
                ReferenceUyaPlanetDir = referenceUyaPlanetDir,
                OutputDir = outputDir,
                GlobalRc3Dir = globalRc3Dir,
                SelectedOptions = selectedOptions,
                
                // Advanced options
                TieSwapOptions = (int)tieSwapOptions,
                ShrubSwapOptions = (int)shrubSwapOptions,
                SwingshotSwapOptions = (int)swingshotSwapOptions,
                VendorSwapOptions = (int)vendorSwapOptions,
                CrateSwapOptions = (int)crateSwapOptions, // Add this line
                MobyInstancerOptions = (int)mobyInstancerOptions,
                SpecialMobyCopyOptions = (int)specialMobyCopyOptions, // Add this line
                VendorModelSource = (int)vendorModelSource,
                CuboidImportOptions = (int)cuboidImportOptions,
                SplineImportOptions = (int)splineImportOptions,
                AnimationRestoreOptions = (int)animationRestoreOptions,
            
                // Planet options
                PlanetName = planetName,
                CityName = cityName,
                PlanetId = planetId,
                MarkPlanetAvailable = markPlanetAvailable,
                PatchAllText = patchAllText,
                
                // Other options
                OpenOutputWhenComplete = openOutputWhenComplete
            };

            string settingsFile = CrossFileDialog.SaveFile("Save Swapper Settings", ".json");
            if (!string.IsNullOrEmpty(settingsFile))
            {
                try
                {
                    string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(settingsFile, json);
                    AddToConsoleOutput($"Settings saved to {settingsFile}");
                }
                catch (Exception ex)
                {
                    AddToConsoleOutput($"Error saving settings: {ex.Message}");
                }
            }
        }

        private void LoadSettings()
        {
            string settingsFile = CrossFileDialog.OpenFile("Load Swapper Settings", ".json");
            if (!string.IsNullOrEmpty(settingsFile) && File.Exists(settingsFile))
            {
                try
                {
                    string json = File.ReadAllText(settingsFile);
                    var settings = JsonSerializer.Deserialize<GeometrySwapperSettings>(json);
                    if (settings != null)
                    {
                        // Basic settings
                        rc1SourceLevelDir = settings.Rc1SourceLevelDir;
                        rc3DonorLevelDir = settings.Rc3DonorLevelDir;
                        referenceUyaPlanetDir = settings.ReferenceUyaPlanetDir;
                        outputDir = settings.OutputDir;
                        globalRc3Dir = settings.GlobalRc3Dir;
                        selectedOptions = settings.SelectedOptions;
                        
                        // Advanced options - check for null to maintain backward compatibility
                        tieSwapOptions = (TieSwapper.TieSwapOptions)(settings.TieSwapOptions != 0 ? 
                            settings.TieSwapOptions : (int)TieSwapper.TieSwapOptions.Default);
                            
                        shrubSwapOptions = (ShrubSwapper.ShrubSwapOptions)(settings.ShrubSwapOptions != 0 ? 
                            settings.ShrubSwapOptions : (int)ShrubSwapper.ShrubSwapOptions.Default);
                            
                        swingshotSwapOptions = (SwingshotOltanisSwapper.SwingshotSwapOptions)(settings.SwingshotSwapOptions != 0 ? 
                            settings.SwingshotSwapOptions : (int)SwingshotOltanisSwapper.SwingshotSwapOptions.Default);
                            
                        vendorSwapOptions = (VendorOltanisSwapper.VendorSwapOptions)(settings.VendorSwapOptions != 0 ? 
                            settings.VendorSwapOptions : (int)VendorOltanisSwapper.VendorSwapOptions.Default);
                            
                        crateSwapOptions = (CratesOltanisSwapper.CrateSwapOptions)(settings.CrateSwapOptions != 0 ?
                            settings.CrateSwapOptions : (int)CratesOltanisSwapper.CrateSwapOptions.Default);
                            
                        mobyInstancerOptions = (MobyOltanisInstancer.InstancerOptions)(settings.MobyInstancerOptions != 0 ?
                            settings.MobyInstancerOptions : (int)MobyOltanisInstancer.InstancerOptions.Default);

                        vendorModelSource = (VendorOltanisSwapper.VendorModelSource)(settings.VendorModelSource != 0 ?
                            settings.VendorModelSource : (int)VendorOltanisSwapper.VendorModelSource.RC1);

                        cuboidImportOptions = (CuboidImporter.CuboidImportOptions)(settings.CuboidImportOptions != 0 ?
                            settings.CuboidImportOptions : (int)CuboidImporter.CuboidImportOptions.Default);

                        splineImportOptions = (SplineImporter.SplineImportOptions)(settings.SplineImportOptions != 0 ?
                            settings.SplineImportOptions : (int)SplineImporter.SplineImportOptions.Default);

                        animationRestoreOptions = (AnimationRestoreOptions)(settings.AnimationRestoreOptions != 0 ?
                            settings.AnimationRestoreOptions : (int)AnimationRestoreOptions.Default);
                        
                        // Planet options
                        if (settings.PlanetName != null) planetName = settings.PlanetName;
                        if (settings.CityName != null) cityName = settings.CityName;
                        planetId = settings.PlanetId != 0 ? settings.PlanetId : planetId;
                        markPlanetAvailable = settings.MarkPlanetAvailable;
                        patchAllText = settings.PatchAllText;
                        
                        // Other options
                        openOutputWhenComplete = settings.OpenOutputWhenComplete;
                        
                        // Special Moby Copy Options
                        specialMobyCopyOptions = (SpecialMobyCopyOptions)(settings.SpecialMobyCopyOptions != 0 ? 
                            settings.SpecialMobyCopyOptions : (int)SpecialMobyCopyOptions.Default);
                        
                        AddToConsoleOutput($"Settings loaded from {settingsFile}");
                    }
                }
                catch (Exception ex)
                {
                    AddToConsoleOutput($"Error loading settings: {ex.Message}");
                }
            }
        }
    }
}
