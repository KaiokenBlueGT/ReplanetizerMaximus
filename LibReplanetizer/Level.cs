// Copyright (C) 2018-2021, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using LibReplanetizer.Headers;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using LibReplanetizer.Models.Animations;
using LibReplanetizer.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
using LibReplanetizer.Serializers;

namespace LibReplanetizer
{
    public class Level
    {
        private static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();

        public bool valid;

        public string? path;

        public GameType game;

        // Add these two properties
        public int SplinePointer { get; set; }
        public int SplineCount { get; set; }
        public int CameraPointer { get; set; }
        public int CameraCount { get; set; }

        //Models
        public List<Model> mobyModels = new List<Model>();
        public List<Model> tieModels = new List<Model>();
        public List<Model> shrubModels;
        public List<Model> gadgetModels;
        public List<Model> armorModels;
        public Model collisionEngine;
        public List<Collision> collisionChunks;
        // Editable wrappers around collision geometry
        public List<CollisionObject> collisionObjects = new List<CollisionObject>();
        public List<Texture> textures;
        public List<List<Texture>> armorTextures;
        public List<Texture> gadgetTextures;
        public SkyboxModel skybox;

        public byte[] renderDefBytes;
        public byte[] collBytesEngine;
        public List<byte[]> collBytesChunks;
        // Indicates that collision geometry has been modified and needs reserialization
        public bool collisionDirty;
        public byte[] billboardBytes;
        public byte[] soundConfigBytes;

        public List<Animation> playerAnimations;
        public List<UiElement> uiElements;


        //Level objects
        public List<Moby> mobs;
        public List<Tie> ties;
        public List<Shrub> shrubs;
        public List<Light> lights;
        public List<Spline> splines;
        public Terrain terrainEngine;
        public List<Terrain> terrainChunks;
        public List<int> textureConfigMenus;
        public List<Mission> missions;
        public List<List<MobyModel>> mobyloadModels;
        public List<List<Texture>> mobyloadTextures;

        public LevelVariables levelVariables;
        public OcclusionData? occlusionData;

        public List<LanguageData> english;
        public List<LanguageData> ukenglish;
        public List<LanguageData> french;
        public List<LanguageData> german;
        public List<LanguageData> spanish;
        public List<LanguageData> italian;
        public List<LanguageData> japanese;
        public List<LanguageData> korean;

        // Table of moby class headers used by RC2/RC3 mobys.
        public List<GcMobyClassHeader> mobyClassHeaders = new List<GcMobyClassHeader>();

        public byte[] unk3;
        public byte[] unk4;
        public byte[] unk5;
        public byte[] unk6;
        public byte[] unk7;
        public byte[] unk8;
        public byte[] unk9;
        public byte[] unk14;
        public byte[] unk17;

        public LightConfig lightConfig;

        public List<KeyValuePair<int, int>> type50s;
        public List<KeyValuePair<int, int>> type5Cs;

        public byte[] tieData;
        public byte[] shrubData;

        public byte[] tieGroupData;
        public byte[] shrubGroupData;

        public byte[] areasData;

        public List<DirectionalLight> directionalLights;
        public List<PointLight> pointLights;
        public List<EnvSample> envSamples;
        public List<EnvTransition> envTransitions;
        public List<SoundInstance> soundInstances;
        public List<GrindPath> grindPaths;
        public List<GlobalPvarBlock> type4Cs;

        public List<byte[]> pVars;
        public List<Cuboid> cuboids;
        public List<Sphere> spheres;
        public List<Cylinder> cylinders;
        public List<Pill> pills;
        public List<GameCamera> gameCameras;

        public List<int> mobyIds;
        public List<int> tieIds;
        public List<int> shrubIds;

        ~Level()
        {
            LOGGER.Trace("Level destroyed");
        }

        //Engine file constructor
        public Level(string enginePath)
        {

            path = Path.GetDirectoryName(enginePath);

            // Engine elements
            using (EngineParser engineParser = new EngineParser(enginePath))
            {
                game = engineParser.GetGameType();

                //REMOVE THESE ASAP!!!!!111
                renderDefBytes = engineParser.GetRenderDefBytes();
                collBytesEngine = engineParser.GetCollisionBytes();
                billboardBytes = engineParser.GetBillboardBytes();
                soundConfigBytes = engineParser.GetSoundConfigBytes();

                LOGGER.Debug("Parsing skybox...");
                skybox = engineParser.GetSkyboxModel();
                LOGGER.Debug("Success");

                LOGGER.Debug("Parsing moby models...");
                mobyModels = engineParser.GetMobyModels();
                LOGGER.Debug("Added {0} moby models", mobyModels.Count);

                LOGGER.Debug("Parsing tie models...");
                tieModels = engineParser.GetTieModels();
                LOGGER.Debug("Added {0} tie models", tieModels.Count);

                LOGGER.Debug("Parsing shrub models...");
                shrubModels = engineParser.GetShrubModels();
                LOGGER.Debug("Added {0} shrub models", shrubModels.Count);

                LOGGER.Debug("Parsing weapons...");
                gadgetModels = engineParser.GetGadgets();
                LOGGER.Debug("Added {0} weapons", gadgetModels.Count);

                LOGGER.Debug("Parsing textures...");
                textures = engineParser.GetTextures();
                LOGGER.Debug("Added {0} textures", textures.Count);

                LOGGER.Debug("Parsing ties...");
                ties = engineParser.GetTies(tieModels);
                LOGGER.Debug("Added {0} ties", ties.Count);

                LOGGER.Debug("Parsing Shrubs...");
                shrubs = engineParser.GetShrubs(shrubModels);
                LOGGER.Debug("Added {0} shrubs", shrubs.Count);

                LOGGER.Debug("Parsing Lights...");
                lights = engineParser.GetLights();
                LOGGER.Debug("Added {0} lights", lights.Count);

                LOGGER.Debug("Parsing terrain elements...");
                terrainEngine = engineParser.GetTerrainModel();
                LOGGER.Debug("Added {0} terrain elements", terrainEngine.fragments.Count);

                LOGGER.Debug("Parsing player animations...");
                playerAnimations = (mobyModels.Count > 0) ? engineParser.GetPlayerAnimations((MobyModel) mobyModels[0]) : new List<Animation>();
                LOGGER.Debug("Added {0} player animations", playerAnimations.Count);

                uiElements = engineParser.GetUiElements();
                LOGGER.Debug("Added {0} ui elements", uiElements.Count);

                lightConfig = engineParser.GetLightConfig();
                textureConfigMenus = engineParser.GetTextureConfigMenu();

                collisionEngine = engineParser.GetCollisionModel();

                unk3 = engineParser.GetUnk3Bytes();
                unk4 = engineParser.GetUnk4Bytes();
                unk5 = engineParser.GetUnk5Bytes();
                unk8 = engineParser.GetUnk8Bytes();
                unk9 = engineParser.GetUnk9Bytes();
            }


            // Gameplay elements
            using (GameplayParser gameplayParser = new GameplayParser(game, path + @"/gameplay_ntsc"))
            {
                LOGGER.Debug("Parsing Level variables...");
                levelVariables = gameplayParser.GetLevelVariables();

                LOGGER.Debug("Parsing pvars...");
                pVars = gameplayParser.GetPvars();

                LOGGER.Debug("Parsing mobs...");
                mobs = gameplayParser.GetMobies(mobyModels, pVars);
                LOGGER.Debug("Added {0} mobs", mobs.Count);

                LOGGER.Debug("Parsing splines...");
                splines = gameplayParser.GetSplines();
                LOGGER.Debug("Added {0} splines", splines.Count);

                LOGGER.Debug("Parsing languages...");
                english = gameplayParser.GetEnglish();
                ukenglish = gameplayParser.GetUkEnglish();
                french = gameplayParser.GetFrench();
                german = gameplayParser.GetGerman();
                spanish = gameplayParser.GetSpanish();
                italian = gameplayParser.GetItalian();
                japanese = gameplayParser.GetJapanese();
                korean = gameplayParser.GetKorean();

                LOGGER.Debug("Parsing other gameplay assets...");
                unk6 = gameplayParser.GetUnk6();
                unk7 = gameplayParser.GetUnk7();
                unk14 = gameplayParser.GetUnk14();
                unk17 = gameplayParser.GetUnk17();

                tieData = gameplayParser.GetTieData(ties.Count);
                shrubData = gameplayParser.GetShrubData(shrubs.Count);

                tieGroupData = gameplayParser.GetTieGroups();
                shrubGroupData = gameplayParser.GetShrubGroups();

                areasData = gameplayParser.GetAreasData();

                directionalLights = gameplayParser.GetDirectionalLights();
                pointLights = gameplayParser.GetPointLights();
                envSamples = gameplayParser.GetEnvSamples();
                envTransitions = gameplayParser.GetEnvTransitions();
                soundInstances = gameplayParser.GetSoundInstances();
                grindPaths = gameplayParser.GetGrindPaths();

                type4Cs = gameplayParser.GetType4Cs();
                type50s = gameplayParser.GetType50s();
                type5Cs = gameplayParser.GetType5Cs();

                cuboids = gameplayParser.GetCuboids();
                spheres = gameplayParser.GetSpheres();
                cylinders = gameplayParser.GetCylinders();
                pills = gameplayParser.GetPills();

                gameCameras = gameplayParser.GetGameCameras();

                mobyIds = gameplayParser.GetMobyIds();
                tieIds = gameplayParser.GetTieIds();
                shrubIds = gameplayParser.GetShrubIds();
                occlusionData = gameplayParser.GetOcclusionData();
            }

            terrainChunks = new List<Terrain>();
            collisionChunks = new List<Collision>();
            collisionObjects = new List<CollisionObject>();
            collBytesChunks = new List<byte[]>();

            int chunkCount = levelVariables?.chunkCount ?? 0;
            for (int i = 0; i < chunkCount; i++)
            {
                string chunkPath = Path.Join(path, $"chunk{i}.ps3");
                if (!File.Exists(chunkPath))
                {
                    LOGGER.Warn("Missing chunk file {0}", chunkPath);
                    continue;
                }

                using (ChunkParser chunkParser = new ChunkParser(chunkPath, game))
                {
                    // Load both terrain and collision so the level state stays consistent.
                    terrainChunks.Add(chunkParser.GetTerrainModels());
                    collisionChunks.Add(chunkParser.GetCollisionModel());
                    collBytesChunks.Add(chunkParser.GetCollBytes());
                }
            }

            // Ensure LevelVariables.chunkCount matches the actual number of chunks loaded.
            if (levelVariables != null)
            {
                levelVariables.chunkCount = collisionChunks.Count;
            }

            // After loading, ensure the main 'collisionEngine' is consistent with the chunks for the editor's display.
            if (collisionChunks.Count > 0)
            {
                var masterVerts = new List<float>();
                var masterIndices = new List<uint>();
                uint masterVertexOffset = 0;
                foreach(var chunk in collisionChunks)
                {
                    if (chunk == null || chunk.vertexBuffer == null || chunk.indBuff == null) continue;
                    masterVerts.AddRange(chunk.vertexBuffer);
                    foreach(var index in chunk.indBuff)
                    {
                        masterIndices.Add(masterVertexOffset + index);
                    }
                    masterVertexOffset += (uint)(chunk.vertexBuffer.Length / 4);
                }
                // --- START OF FIX ---
                // If the chunks produced no vertices, but the main engine collision has vertices,
                // it means the chunk parsing failed. In this case, trust the main engine collision.
                if (masterVerts.Count == 0 && collisionEngine != null && collisionEngine.vertexBuffer != null && collisionEngine.vertexBuffer.Length > 0)
                {
                    // The chunks are invalid/empty, but the main collision is good.
                    // Clear the bad chunk data and use the main collision data instead.
                    collisionChunks.Clear();
                    collisionChunks.Add((Collision)collisionEngine);
                    LOGGER.Warn("Loaded collision chunks were empty/invalid. Falling back to master collision engine data.");
                    Console.WriteLine("Loaded collision chunks were empty/invalid. Falling back to master collision engine data.");
                }
                else
                {
                    // Rebuild the main engine object from the chunks to ensure a consistent state.
                    collisionEngine = new Collision
                    {
                        vertexBuffer = masterVerts.ToArray(),
                        indBuff = masterIndices.ToArray(),
                        indexBuffer = masterIndices.ConvertAll(i => (ushort)i).ToArray()
                    };
                }
                // --- END OF FIX ---
            }

            // Create editable wrappers for collision geometry so it can be manipulated
            if (collisionChunks.Count > 0)
            {
                foreach (var chunk in collisionChunks)
                    collisionObjects.Add(new CollisionObject(chunk));
            }
            else if (collisionEngine != null)
            {
                collisionObjects.Add(new CollisionObject((Collision)collisionEngine));
            }

            List<string> armorPaths = ArmorHeader.FindArmorFiles(game, enginePath);
            armorModels = new List<Model>();
            armorTextures = new List<List<Texture>>();

            foreach (string armor in armorPaths)
            {
                LOGGER.Debug("Looking for armor data in {0}", armor);
                List<Texture> tex;
                MobyModel? model;
                MobyModel? ratchet = (MobyModel?) mobyModels?[0];
                using (ArmorParser parser = new ArmorParser(game, armor))
                {
                    tex = parser.GetTextures();
                    model = parser.GetArmor();

                    if (model != null && ratchet != null)
                    {
                        /*
                         * Armor models do not contain animations, instead they are stored in the ratchet model which itself does not contain a mesh.
                         * For export purposes we assign these animations here.
                         */
                        model.animations = ratchet.animations;
                        model.boneCount = ratchet.boneCount;
                        model.boneDatas = ratchet.boneDatas;
                        model.boneMatrices = ratchet.boneMatrices;
                        model.skeleton = ratchet.skeleton;
                    }
                }

                string vram = armor.Replace(".ps3", ".vram");

                using (VramParser parser = new VramParser(vram))
                {
                    parser.GetTextures(tex);
                }

                if (model != null)
                    armorModels.Add(model);

                armorTextures.Add(tex);
            }

            string gadgetPath = GadgetHeader.FindGadgetFile(game, enginePath);
            gadgetTextures = new List<Texture>();

            if (gadgetPath != "")
            {
                LOGGER.Debug("Looking for gadget data in {0}", gadgetPath);
                using (GadgetParser parser = new GadgetParser(game, gadgetPath))
                {
                    gadgetModels.AddRange(parser.GetModels());
                    gadgetTextures.AddRange(parser.GetTextures());
                }

                using (VramParser parser = new VramParser(gadgetPath.Replace(".ps3", ".vram")))
                {
                    parser.GetTextures(gadgetTextures);
                }
            }

            List<string> missionPaths = MissionHeader.FindMissionFiles(game, enginePath);
            missions = new List<Mission>();

            for (int i = 0; i < missionPaths.Count; i++)
            {
                string missionPath = missionPaths[i];
                string vramPath = missionPath.Replace(".ps3", ".vram");

                if (!File.Exists(vramPath))
                {
                    LOGGER.Warn("Could not find .vram file for {0}", missionPath);
                    continue;
                }

                LOGGER.Debug("Looking for mission data in {0}", missionPath);

                Mission mission = new Mission(i);

                using (MissionParser parser = new MissionParser(game, missionPath))
                {
                    mission.models = parser.GetModels();
                    mission.textures = parser.GetTextures();
                }

                using (VramParser parser = new VramParser(vramPath))
                {
                    parser.GetTextures(mission.textures);
                }

                missions.Add(mission);
            }

            mobyloadModels = new List<List<MobyModel>>();
            mobyloadTextures = new List<List<Texture>>();

            for (int mobyloadFileID = 0; mobyloadFileID < 32; mobyloadFileID++)
            {
                string? mobyloadFilePath = MobyloadHeader.FindMobyloadFile(game, enginePath, mobyloadFileID);
                if (mobyloadFilePath != null)
                {
                    using (MobyloadParser parser = new MobyloadParser(game, mobyloadFilePath))
                    {
                        mobyloadModels.Add(parser.GetMobyModels());
                        mobyloadTextures.Add(parser.GetTextures());
                    }
                }
                else
                {
                    mobyloadModels.Add(new List<MobyModel>());
                    mobyloadTextures.Add(new List<Texture>());
                }
            }

            using (VramParser vramParser = new VramParser(path + @"/vram.ps3"))
            {
                vramParser.GetTextures(textures);
            }

            LOGGER.Info("Level parsing done");
            valid = true;
        }

        // Minimal constructor for stub Level (for OBJ to RCC conversion)
        public Level() {
            valid = false;
            path = null;
            game = GameType.RaC1; // Default, not used
            mobyModels = new List<Model>();
            tieModels = new List<Model>();
            shrubModels = new List<Model>();
            gadgetModels = new List<Model>();
            armorModels = new List<Model>();
            collisionChunks = new List<Collision>();
            collisionObjects = new List<CollisionObject>();
            textures = new List<Texture>();
            armorTextures = new List<List<Texture>>();
            gadgetTextures = new List<Texture>();
            terrainChunks = new List<Terrain>();
            collBytesChunks = new List<byte[]>();
            missions = new List<Mission>();
            mobyloadModels = new List<List<MobyModel>>();
            mobyloadTextures = new List<List<Texture>>();
        }

        // Copies data like gadget models from gadget files etc into engine data.
        public void EmplaceCommonData()
        {
            int gadgetTextureOffset = textures.Count;

            textures.AddRange(gadgetTextures);

            foreach (Model model in gadgetModels)
            {
                if (game != GameType.RaC1)
                {
                    foreach (TextureConfig conf in model.textureConfig)
                    {
                        conf.id += gadgetTextureOffset;
                    }
                }

                mobyModels.RemoveAll(x => x.id == model.id);
            }

            mobyModels.AddRange(gadgetModels);

            if (armorModels.Count > 0)
            {
                // Replace the empty ratchet model with the first armor model.
                // This can be changed once we know where the game stores which armor model to use.

                int armorTextureOffset = textures.Count;
                textures.AddRange(armorTextures[0]);

                Model defaultRatchetModel = armorModels[0];

                foreach (TextureConfig conf in defaultRatchetModel.textureConfig)
                {
                    conf.id += armorTextureOffset;
                }

                mobyModels.RemoveAll(x => x.id == 0);
                mobyModels.Add(defaultRatchetModel);
                mobs.ForEach(x =>
                {
                    if (x.modelID == 0)
                    {
                        x.model = defaultRatchetModel;
                    }
                });
            }

            mobyModels.ForEach(x =>
            {
                if (x.id == 0 && x is MobyModel mobyModel)
                {
                    mobyModel.animations = playerAnimations;
                }
            });
        }

        /// <summary>
        /// Ensures that the collision data is serialized from the current collisionChunks (if present)
        /// </summary>
        public void UpdateCollisionBytesFromChunks()
        {
            // If nothing has modified the collision since the last save, keep the existing bytes.
            if (!collisionDirty && collBytesEngine != null && collBytesEngine.Length > 0)
            {
                return;
            }

            // If collisionChunks are missing but collisionObjects exist (e.g. after editing),
            // rebuild the chunk list from the editable collision objects so that any
            // transformations are preserved when saving the level.
            if ((collisionChunks == null || collisionChunks.Count == 0) &&
                collisionObjects != null && collisionObjects.Count > 0)
            {
                collisionChunks = new List<Collision>();
                foreach (var obj in collisionObjects)
                {
                    if (obj?.model != null)
                        collisionChunks.Add(obj.model);
                }
            }

            if (collisionChunks == null || collisionChunks.Count == 0)
            {
                // If there's still no collision, create a default empty header.
                collBytesEngine = new byte[] { 0,0,0,0x10, 0,0,0,0, 0,0,0,0, 0,0,0,0 };
                collBytesChunks = new List<byte[]>(); // Ensure this is cleared too
                collisionDirty = false;
                return;
            }

            // --- Serialize each chunk to collBytesChunks ---
            collBytesChunks = new List<byte[]>();
            foreach (var chunk in collisionChunks)
            {
                if (chunk == null || chunk.vertexBuffer == null || chunk.indBuff == null) {
                    collBytesChunks.Add(new byte[0]);
                    continue;
                }
                // --- AFTER (Explicitly provide the collision type) ---
                collBytesChunks.Add(DataFunctions.SerializeCollisionToRccChunked(chunk,
                    (faceIndex) => (CollisionType)31)); // CollisionType.Ground
            }

            // --- Combine all chunks into a single master collision object before serializing. ---
            var combinedVerts = new List<float>();
            var combinedInds = new List<uint>();
            uint vertexOffset = 0;

            foreach (var chunk in collisionChunks)
            {
                if (chunk == null || chunk.vertexBuffer == null || chunk.indBuff == null) continue;

                combinedVerts.AddRange(chunk.vertexBuffer);
                foreach (var index in chunk.indBuff)
                {
                    combinedInds.Add(vertexOffset + index);
                }
                vertexOffset += (uint)(chunk.vertexBuffer.Length / 4); // Each vertex is 4 floats (XYZW)
            }

            if (combinedVerts.Count == 0)
            {
                collBytesEngine = new byte[] { 0,0,0,0x10, 0,0,0,0, 0,0,0,0, 0,0,0,0 };
                collisionDirty = false;
                return;
            }

            var masterCollision = new Models.Collision
            {
                vertexBuffer = combinedVerts.ToArray(),
                indBuff = combinedInds.ToArray()
            };

            // Now, serialize the single master object.
            collBytesEngine = DataFunctions.SerializeCollisionToRccChunked(masterCollision,
                (faceIndex) => (CollisionType)31); // CollisionType.Ground
            collisionDirty = false;
        }

        public void Save(string outputFile)
        {
            string? directory;
            if (File.Exists(outputFile) && File.GetAttributes(outputFile).HasFlag(FileAttributes.Directory))
            {
                directory = outputFile;
            }
            else
            {
                directory = Path.GetDirectoryName(outputFile);
            }

            if (directory == null) return;

            // --- CRITICAL PATCH: Update collision bytes before saving ---
            UpdateCollisionBytesFromChunks();

            // --- SAFETY PATCH: Only save chunk files when terrain chunks are present ---
            int chunkCount = terrainChunks?.Count ?? 0;
            if (levelVariables != null)
                levelVariables.chunkCount = chunkCount;

            if (chunkCount > 0)
            {
                if (collisionChunks == null) collisionChunks = new List<Collision>();
                if (collisionObjects == null) collisionObjects = new List<CollisionObject>();
                if (collBytesChunks == null) collBytesChunks = new List<byte[]>();
                for (int i = collisionChunks.Count; i < chunkCount; i++)
                    collisionChunks.Add(new Collision());
                for (int i = collisionObjects.Count; i < chunkCount; i++)
                    collisionObjects.Add(new CollisionObject(new Collision()));
                for (int i = collBytesChunks.Count; i < chunkCount; i++)
                    collBytesChunks.Add(new byte[0]);
            }

            EngineSerializer engineSerializer = new EngineSerializer();
            engineSerializer.Save(this, directory);
            GameplaySerializer gameplaySerializer = new GameplaySerializer();
            gameplaySerializer.Save(this, directory);

            if (chunkCount > 0)
            {
                for (int i = 0; i < chunkCount; i++)
                {
                    ChunkSerializer chunkSerializer = new ChunkSerializer();
                    chunkSerializer.Save(this, directory, i);
                }
            }
        }
    }
}
