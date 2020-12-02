﻿using Microsoft.Xna.Framework;
using PurrplingCore.Movement;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Collections.Generic;
using SObject = StardewValley.Object;
using IDrawable = PurrplingCore.Internal.IDrawable;
using Microsoft.Xna.Framework.Graphics;

namespace NpcAdventure.AI.Controller
{
    class FishController : IController, IDrawable
    {
        private readonly PathFinder pathFinder;
        private readonly NpcMovementController joystick;
        private readonly AI_StateMachine ai;
        private readonly NPC fisher;
        private readonly Farmer farmer;
        private readonly Vector2 negativeOne = new Vector2(-1, -1);
        private readonly List<FarmerSprite.AnimationFrame> fishingLeftAnim;
        private readonly List<FarmerSprite.AnimationFrame> fishingRightAnim;
        private readonly Stack<SObject> fishCaught;
        private bool fishingFacingRight;
        private int fishCaughtTimer;
        private int lastCaughtFishIdx;

        public bool IsIdle { get; private set; }
        public bool IsFishing { get; private set; }
        public int Invicibility { get; private set; }

        private enum TileReachability
        {
            Unreachable,
            Walkable,
            Unwalkable,
            Water
        }

        public FishController(AI_StateMachine ai, IModEvents events)
        {
            this.ai = ai;
            this.fisher = ai.npc;
            this.farmer = ai.farmer;
            this.pathFinder = new PathFinder(this.fisher.currentLocation, this.fisher, this.farmer);
            this.joystick = new NpcMovementController(this.fisher, this.pathFinder);
            this.fishCaught = new Stack<SObject>();

            ai.LocationChanged += this.Ai_LocationChanged;
            events.GameLoop.TimeChanged += this.OnTimeChanged;
            this.joystick.EndOfRouteReached += this.ArrivedFishingSpot;

            this.fishingLeftAnim = new List<FarmerSprite.AnimationFrame>
                {
                    new FarmerSprite.AnimationFrame(20, 4000, false, true, null, false),
                    new FarmerSprite.AnimationFrame(21, 4000, false, true, null, false)
                };
            this.fishingRightAnim = new List<FarmerSprite.AnimationFrame>
                {
                    new FarmerSprite.AnimationFrame(20, 4000),
                    new FarmerSprite.AnimationFrame(21, 4000)
                };
        }

        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            if (!this.IsFishing)
                return;

            if (Game1.random.Next(4) == 1)
            {
                SObject fish = this.fisher.currentLocation.getFish(200, 1, this.farmer.FishingLevel / 2, this.farmer, 4.5d, Vector2.Zero);
                if (fish == null || fish.ParentSheetIndex <= 0)
                    fish = new SObject(Game1.random.Next(167, 173), 1, false, -1, 0);
                if (fish.Category == -20 || fish.ParentSheetIndex == 152 || fish.ParentSheetIndex == 153 ||
                    fish.ParentSheetIndex == 157 || fish.ParentSheetIndex == 797 || fish.ParentSheetIndex == 79)
                {
                    fish = new SObject(Game1.random.Next(167, 173), 1, false, -1, 0);
                }
                if (fish.Category != -20 && fish.ParentSheetIndex != 152 && fish.ParentSheetIndex != 153 &&
                    fish.ParentSheetIndex != 157 && fish.ParentSheetIndex != 797 && fish.ParentSheetIndex != 79)
                {
                    int skill = this.farmer.FishingLevel;
                    int quality = 0;

                    if (skill >= 8 && Game1.random.NextDouble() < .05f)
                        quality = 4;
                    else if (skill >= 6 && Game1.random.NextDouble() < 0.2f)
                        quality = 2;
                    else if (skill >= 2 && Game1.random.NextDouble() < 0.55f)
                        quality = 1;

                    fish.Quality = quality;
                    this.fishCaught.Push(fish);
                    this.Invicibility += 2000;
                }

                this.lastCaughtFishIdx = fish.ParentSheetIndex;
                this.fishCaughtTimer = 3000;
            }
        }

        private void ArrivedFishingSpot(object sender, NpcMovementController.EndOfRouteReachedEventArgs e)
        {
            if (this.IsFishing)
                return;

            this.IsFishing = true;
            this.Invicibility = 1000;
            this.fisher.Sprite.SpriteWidth = 32;
            this.fisher.HideShadow = true;
            if (this.fishingFacingRight)
            {
               this.fisher.Sprite.setCurrentAnimation(this.fishingRightAnim);
            }
            else
            {
                this.fisher.drawOffset.Value = new Vector2(-64f, 0);
                this.fisher.Sprite.setCurrentAnimation(this.fishingLeftAnim);
            }
        }

        private void Ai_LocationChanged(object sender, EventArgsLocationChanged e)
        {
            if (this.IsFishing)
                this.IsIdle = true;

            this.joystick.Reset();
            this.CheckFishingHere();
        }

        public void Activate()
        {
            this.IsIdle = false;
            this.CheckFishingHere();
        }

        public bool HasAnyFish()
        {
            return this.fishCaught.Count > 0;
        }

        public void CheckFishingHere()
        {
            var fishSpot = this.GetFishStandingPoint();

            if (fishSpot == this.negativeOne)
            {
                this.IsIdle = true;
                return;
            }

            this.joystick.AcquireTarget(fishSpot);
        }

        public bool GiveFishesTo(Farmer player)
        {
            bool somethingAdded = false;
            while (this.HasAnyFish())
            {
                if (!player.addItemToInventoryBool(this.fishCaught.Peek()))
                    break;

                somethingAdded = true;
                this.fishCaught.Pop();
            }

            if (!somethingAdded)
                this.ai.Monitor.Log("Can't add shared fishes to inventory, it's probably full!");

            return somethingAdded;
        }

        private Vector2 GetFishStandingPoint()
        {
            Vector2 tile = this.negativeOne;
            var maxTilesToWander = 6;
            bool anyWater = false;

            if (this.pathFinder.GameLocation.waterTiles == null)
                return tile;

            TileReachability[,] tileCache = new TileReachability[(maxTilesToWander * 2) + 1, (maxTilesToWander * 2) + 1];
            tileCache[maxTilesToWander, maxTilesToWander] = TileReachability.Walkable;
            Vector2 loc = this.fisher.getTileLocation();
            Vector3 translate = new Vector3(loc.X, loc.Y, 0) - new Vector3(maxTilesToWander, maxTilesToWander, 0);
            Queue<Vector3> tileQueue = new Queue<Vector3>();
            tileQueue.Enqueue(new Vector3(loc.X, loc.Y, 0));

            while (tileQueue.Count != 0)
            {
                Vector3 t = tileQueue.Dequeue();
                Vector3[] neighbors = this.pathFinder.GetDirectWalkableNeighbors(t);
                foreach (Vector3 neighbor in neighbors)
                {
                    Vector3 pos = neighbor - translate;
                    if (pos.X >= 0 && pos.X <= maxTilesToWander * 2 &&
                        pos.Y >= 0 && pos.Y <= maxTilesToWander * 2 &&
                        tileCache[(int)pos.X, (int)pos.Y] == TileReachability.Unreachable)
                    {
                        if (neighbor.Z == 1)
                        {
                            tileCache[(int)pos.X, (int)pos.Y] = TileReachability.Walkable;
                            tileQueue.Enqueue(neighbor);
                        }
                        else
                        {
                            try
                            {
                                if (this.pathFinder.GameLocation.waterTiles[(int)neighbor.X, (int)neighbor.Y])
                                {
                                    tileCache[(int)pos.X, (int)pos.Y] = TileReachability.Water;
                                    anyWater = true;
                                }
                                else
                                {
                                    tileCache[(int)pos.X, (int)pos.Y] = TileReachability.Unwalkable;
                                }
                            } catch (IndexOutOfRangeException)
                            {
                                tileCache[(int)pos.X, (int)pos.Y] = TileReachability.Unreachable;
                            }
                        }
                    }
                }
            }
            tile = this.negativeOne;

            if (!anyWater)
                return this.negativeOne;

            List<Vector3> fishingTiles = new List<Vector3>(maxTilesToWander);
            int xDim = maxTilesToWander * 2;
            for (int y = 0; y < (maxTilesToWander * 2) + 1; y++)
            {
                for (int x = 0; x < (maxTilesToWander * 2) + 1; x++)
                {
                    if (tileCache[x, y] == TileReachability.Water)
                    {
                        if (x > 0 && tileCache[x - 1, y] == TileReachability.Walkable)
                            fishingTiles.Add(new Vector3(x - 1, y, 1));
                        if (x < xDim && tileCache[x + 1, y] == TileReachability.Walkable)
                            fishingTiles.Add(new Vector3(x + 1, y, -1));
                    }
                }
            }

            if (fishingTiles.Count > 0)
            {
                Vector3 t = fishingTiles[Game1.random.Next(fishingTiles.Count)] + translate;
                tile = new Vector2((int)t.X, (int)t.Y);
                this.fishingFacingRight = t.Z > 0;
            }

            return tile;
        }

        public void Deactivate()
        {
            if (this.IsFishing)
            {
                this.fisher.reloadSprite();
                this.fisher.Sprite.SpriteWidth = 16;
                this.fisher.drawOffset.Value = new Vector2(0, 0);
                this.fisher.Sprite.UpdateSourceRect();
                this.fisher.HideShadow = false;
                this.IsFishing = false;
            }

            this.Invicibility = 1000;
            this.joystick.Reset();
        }

        public void SideUpdate(UpdateTickedEventArgs e)
        {
            if (this.Invicibility > 0)
                this.Invicibility -= (int)Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
        }

        public void Update(UpdateTickedEventArgs e)
        {
            if (this.IsIdle)
                return;

            if (!this.ai.IsFarmerNear() || this.IsFishing && this.Invicibility <= 0 && Game1.random.NextDouble() < 0.02f)
            {
                this.IsIdle = true;
                return;
            }

            if (this.fishCaughtTimer > 0)
                this.fishCaughtTimer -= (int)Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;

            this.joystick.Update(e);
        }

        public bool CanFish()
        {
            return this.GetFishStandingPoint() != this.negativeOne;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (this.fishCaughtTimer <= 0)
                return;

            float num1 = (float)(2.0 * Math.Round(Math.Sin(DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 250.0), 2));
            Point tileLocationPoint = this.fisher.getTileLocationPoint();
            Vector2 offset = this.fisher.drawOffset.Value;
            float num2 = (float)((tileLocationPoint.Y + 1) * 64) / 10000f;
            float num3 = num1 - 40f;
            spriteBatch.Draw(Game1.mouseCursors, Game1.GlobalToLocal(Game1.viewport, new Vector2((float)(tileLocationPoint.X * 64 + 64 + offset.X), (float)(tileLocationPoint.Y * 64 - 96 - 36 + offset.Y) + num3)), new Rectangle?(new Rectangle(141, 465, 20, 24)), Color.White * 0.75f, 0.0f, Vector2.Zero, 4f, SpriteEffects.None, num2 + 1E-06f);
            spriteBatch.Draw(Game1.objectSpriteSheet, Game1.GlobalToLocal(Game1.viewport, new Vector2((float)(tileLocationPoint.X * 64 + 64 + 40 + offset.X), (float)(tileLocationPoint.Y * 64 - 64 - 16 - 10 + offset.Y) + num3)), new Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.objectSpriteSheet, this.lastCaughtFishIdx, 16, 16)), Color.White, 0.0f, new Vector2(8f, 8f), 4f, SpriteEffects.None, num2 + 1E-05f);
        }
    }
}