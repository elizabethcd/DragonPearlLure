﻿using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Monsters;

namespace DragonPearlLure
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        /// <summary>The Json Assets mod API.</summary>
        private static IJsonAssets JA_API;

        /// <summary>Monitor for logging purposes.</summary>
        private static IMonitor Mon;

        /// <summary>Game1.multiplayer from reflection.</summary>
        private static Multiplayer multiplayer;

        /// <summary>The name of the pearl lure.</summary>
        private static string pearlLureName = "violetlizabet.PearlLure";

        /// <summary>The name of the pearl lure.</summary>
        private static string pearlLureMonsterName = "Pearl Lure Monster";

        /// <summary>The mod unique ID.</summary>
        private static string modID;

        /// <summary>The item ID for the pearl lure.</summary>
        public static int PearlLureID => JA_API.GetObjectId(pearlLureName);

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;

            // Set up some things
            var harmony = new Harmony(this.ModManifest.UniqueID);
            var Game1_multiplayer = this.Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
            Mon = Monitor;
            multiplayer = Game1_multiplayer;
            modID = this.ModManifest.UniqueID;

            // Allow pearl lures to be placed
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.Object), nameof(StardewValley.Object.canBePlacedHere)),
               postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.CanBePlacedHere_Postfix))
            );

            // Cause explosion when placed
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.Object), nameof(StardewValley.Object.placementAction)),
               prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.PlacementAction_Prefix))
            );

            // Allow placeable
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.Object), nameof(StardewValley.Object.isPlaceable)),
               postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.IsPlaceable_Postfix))
            );

            // Spawn flying pearl when exploded
            harmony.Patch(
               original: AccessTools.Method(typeof(TemporaryAnimatedSprite), nameof(TemporaryAnimatedSprite.unload)),
               postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.TASUnload_Postfix))
            );

            // Make flying pearl not chase the farmer
            harmony.Patch(
               original: AccessTools.Method(typeof(Bat), nameof(Bat.behaviorAtGameTick)),
               prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Bat_BehaviorAtGameTick_Prefix))
            );
            harmony.Patch(
               original: AccessTools.Method(typeof(Bat), nameof(Bat.behaviorAtGameTick)),
               postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Bat_BehaviorAtGameTick_Postfix))
            );

            // Make serpent chase the flying pearl
            harmony.Patch(
               original: AccessTools.Method(typeof(Serpent), "updateAnimation"),
               prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Serpent_UpdateAnimation_Prefix))
            );
            harmony.Patch(
               original: AccessTools.Method(typeof(Serpent), "updateAnimation"),
               postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Serpent_UpdateAnimation_Postfix))
            );
        }

        // Grab JA API in order to create pearl lure
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            JA_API = this.Helper.ModRegistry.GetApi<IJsonAssets>("spacechase0.JsonAssets");
            if (JA_API == null)
            {
                this.Monitor.Log("Could not get Json Assets API, mod will not work!", LogLevel.Error);
            }
            else
            {
                JA_API.LoadAssets(Path.Combine(this.Helper.DirectoryPath, "assets", "json-assets"), this.Helper.Translation);
            }
        }

        // Load pearl lure monster asset
        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Characters/Monsters/" + pearlLureMonsterName))
            {
                e.LoadFromModFile<Texture2D>("assets/PearlLureMonster.png", AssetLoadPriority.Medium);
            }
        }

        // Allow our object to be placed
        private static void CanBePlacedHere_Postfix(StardewValley.Object __instance, GameLocation l, Vector2 tile, ref bool __result)
        {
            // Not our item, we don't care
            if (!__instance.Name.Contains(pearlLureName, StringComparison.OrdinalIgnoreCase) || __instance.bigCraftable.Value)
            {
                return;
            }
            else
            {
                // If the tile is suitable, it can be placed
                if ((!l.isTileOccupiedForPlacement(tile, __instance) || l.isTileOccupiedByFarmer(tile) != null))
                {
                    __result = true;
                }
            }
        }

        // Trigger explosion properly when placed
        private static bool PlacementAction_Prefix(StardewValley.Object __instance, GameLocation location, int x, int y, Farmer who, ref bool __result)
        {
            // Not our item, we don't care
            if (!__instance.Name.Contains(pearlLureName, StringComparison.OrdinalIgnoreCase) || __instance.bigCraftable.Value)
            {
                return true;
            }
            else 
            {
                bool success = DoPearlExplosionAnimation(location, x, y, who);
                if (success)
                {
                    __result = true;
                }
                return false;
            }
        }

        // Set placeable to true for our item
        private static void IsPlaceable_Postfix(StardewValley.Object __instance, ref bool __result)
        {
            if (__instance.Name.Contains(pearlLureName, StringComparison.OrdinalIgnoreCase))
            {
                __result = true;
            }
        }

        // Spawn a new pearl lure monster when unloaded
        private static void TASUnload_Postfix(TemporaryAnimatedSprite __instance)
        {
            if (__instance.initialParentTileIndex == PearlLureID)
            {
                Bat newBat = new Bat(__instance.Position, -555);
                newBat.DamageToFarmer = 0;
                newBat.Name = pearlLureMonsterName;
                newBat.reloadSprite();
                __instance.Parent.addCharacter(newBat);
            }
        }

        // Save some important things for the postfix
        private static void Bat_BehaviorAtGameTick_Prefix(Bat __instance, out float[] __state)
        {
            __state = new float[3];
            __state[0] = __instance.xVelocity;
            __state[1] = __instance.yVelocity;
            __state[2] = __instance.rotation;
        }

        // Make the pearl lure monster not chase the farmer
        private static void Bat_BehaviorAtGameTick_Postfix(Bat __instance, float[] __state, float ___maxSpeed, float ___extraVelocity, ref NetInt ___wasHitCounter, ref NetBool ___turningRight, ref float ___targetRotation)
        {
            // Not my monster, leave immediately
            if (!__instance.Name.Equals(pearlLureMonsterName,StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Get the existing target, if there is one
            int targetX = -1;
            int targetY = -1;
            try
            {
                if (__instance.modData.ContainsKey(modID + "X"))
                {
                    targetX = Int32.Parse(__instance.modData[modID + "X"]);
                }
                if (__instance.modData.ContainsKey(modID + "Y"))
                {
                    targetY = Int32.Parse(__instance.modData[modID + "Y"]);
                }
            }
            catch (Exception ex)
            {
                Mon.Log($"Something very weird happened to the modData, causing {ex}", LogLevel.Error);
            }

            // Pick a new target location 1% of the time or if there is no current target location
            if (Game1.random.Next(100) == 0 || targetX < 0 || targetY < 0)
            {
                // Get new target location
                int newTargetX = Game1.random.Next(__instance.currentLocation.Map.DisplaySize.Width);
                int newTargetY = Game1.random.Next(__instance.currentLocation.Map.DisplaySize.Height);
                // Save new target to modData
                __instance.modData[modID + "X"] = newTargetX.ToString();
                __instance.modData[modID + "Y"] = newTargetY.ToString();
                targetX = newTargetX;
                targetY = newTargetY;
            }

            // Reset the bat stats before running the movement calcs
            try
            {
                __instance.xVelocity = __state[0];
                __instance.yVelocity = __state[1];
                __instance.rotation = __state[2];
            }
            catch (Exception ex)
            {
                Mon.Log($"Unable to get lure monster state from prefix due to {ex}",LogLevel.Error);
                return;
            }

            // Get the x and y slope towards the target and normalize
            float xSlope = -(targetX - __instance.GetBoundingBox().Center.X);
            float ySlope = targetY - __instance.GetBoundingBox().Center.Y;
            float t = Math.Max(1f, Math.Abs(xSlope) + Math.Abs(ySlope));
            if (t < (float)((___extraVelocity > 0f) ? 192 : 64))
            {
                __instance.xVelocity = Math.Max(0f - ___maxSpeed, Math.Min(___maxSpeed, __instance.xVelocity * 1.05f));
                __instance.yVelocity = Math.Max(0f - ___maxSpeed, Math.Min(___maxSpeed, __instance.yVelocity * 1.05f));
            }
            xSlope /= t;
            ySlope /= t;

            if ((int)___wasHitCounter.Value <= 0)
            {
                ___targetRotation = (float)Math.Atan2(0f - ySlope, xSlope) - (float)Math.PI / 2f;
                if ((double)(Math.Abs(___targetRotation) - Math.Abs(__instance.rotation)) > Math.PI * 7.0 / 8.0 && Game1.random.NextDouble() < 0.5)
                {
                    ___turningRight.Value = true;
                }
                else if ((double)(Math.Abs(___targetRotation) - Math.Abs(__instance.rotation)) < Math.PI / 8.0)
                {
                    ___turningRight.Value = false;
                }
                if ((bool)___turningRight.Value)
                {
                    __instance.rotation -= (float)Math.Sign(___targetRotation - __instance.rotation) * ((float)Math.PI / 64f);
                }
                else
                {
                    __instance.rotation += (float)Math.Sign(___targetRotation - __instance.rotation) * ((float)Math.PI / 64f);
                }
                __instance.rotation %= (float)Math.PI * 2f;
                ___wasHitCounter.Value = 0;
            }
            float maxAccel = Math.Min(5f, Math.Max(1f, 5f - t / 64f / 2f)) + ___extraVelocity;
            xSlope = (float)Math.Cos((double)__instance.rotation + Math.PI / 2.0);
            ySlope = 0f - (float)Math.Sin((double)__instance.rotation + Math.PI / 2.0);
            __instance.xVelocity += (0f - xSlope) * maxAccel / 6f + (float)Game1.random.Next(-10, 10) / 100f;
            __instance.yVelocity += (0f - ySlope) * maxAccel / 6f + (float)Game1.random.Next(-10, 10) / 100f;
            if (Math.Abs(__instance.xVelocity) > Math.Abs((0f - xSlope) * ___maxSpeed))
            {
                __instance.xVelocity -= (0f - xSlope) * maxAccel / 6f;
            }
            if (Math.Abs(__instance.yVelocity) > Math.Abs((0f - ySlope) * ___maxSpeed))
            {
                __instance.yVelocity -= (0f - ySlope) * maxAccel / 6f;
            }
        }

        // Save some important things for the postfix
        private static void Serpent_UpdateAnimation_Prefix(Serpent __instance, out float[] __state)
        {
            __state = new float[3];
            __state[0] = __instance.xVelocity;
            __state[1] = __instance.yVelocity;
            __state[2] = __instance.rotation;
        }

        // Make the serpents chase the pearl lure monster
        private static void Serpent_UpdateAnimation_Postfix(Serpent __instance, float[] __state, ref int ___wasHitCounter, ref bool ___turningRight, ref float ___targetRotation)
        {
            // Not my monster, leave immediately
            if (!__instance.Name.Contains("Serpent", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // If the serpent is in a weird place, quit and leave
            if (double.IsNaN(__instance.xVelocity) || double.IsNaN(__instance.yVelocity))
            {
                return;
            }
            if (__instance.Position.X <= -640f || __instance.Position.Y <= -640f || __instance.Position.X >= (float)(__instance.currentLocation.Map.Layers[0].LayerWidth * 64 + 640) || __instance.Position.Y >= (float)(__instance.currentLocation.Map.Layers[0].LayerHeight * 64 + 640))
            {
                return;
            }

            // Get the closest pearl lure monster, if there isn't one then quit
            Bat closestLure = getLure(__instance.currentLocation);
            if (closestLure == null)
            {
                return;
            }

            // Reset the serpent stats before running the movement calcs
            try
            {
                __instance.xVelocity = __state[0];
                __instance.yVelocity = __state[1];
                __instance.rotation = __state[2];
            }
            catch (Exception ex)
            {
                Mon.Log($"Unable to get serpent state from prefix due to {ex}", LogLevel.Error);
                return;
            }

            Mon.Log("Redirecting serpent to lure in animation",LogLevel.Warn);
            Mon.Log($"Current x-velocity is {__instance.xVelocity} and y-velocity is {__instance.yVelocity} and rotation is {__instance.rotation}", LogLevel.Warn);

            // Get the x and y slope towards the target and normalize
            float xSlope = -(closestLure.GetBoundingBox().Center.X - __instance.GetBoundingBox().Center.X);
            float ySlope = closestLure.GetBoundingBox().Center.Y - __instance.GetBoundingBox().Center.Y;
            float t = Math.Max(1f, Math.Abs(xSlope) + Math.Abs(ySlope));
            if (t < 64f)
            {
                __instance.xVelocity = Math.Max(-7f, Math.Min(7f, __instance.xVelocity * 1.1f));
                __instance.yVelocity = Math.Max(-7f, Math.Min(7f, __instance.yVelocity * 1.1f));
            }
            xSlope /= t;
            ySlope /= t;
            if (___wasHitCounter <= 0)
            {
                ___targetRotation = (float)Math.Atan2(0f - ySlope, xSlope) - (float)Math.PI / 2f;
                if ((double)(Math.Abs(___targetRotation) - Math.Abs(__instance.rotation)) > Math.PI * 7.0 / 8.0 && Game1.random.NextDouble() < 0.5)
                {
                    ___turningRight = true;
                }
                else if ((double)(Math.Abs(___targetRotation) - Math.Abs(__instance.rotation)) < Math.PI / 8.0)
                {
                    ___turningRight = false;
                }
                if (___turningRight)
                {
                    __instance.rotation -= (float)Math.Sign(___targetRotation - __instance.rotation) * ((float)Math.PI / 64f);
                }
                else
                {
                    __instance.rotation += (float)Math.Sign(___targetRotation - __instance.rotation) * ((float)Math.PI / 64f);
                }
                __instance.rotation %= (float)Math.PI * 2f;
                ___wasHitCounter = 5 + Game1.random.Next(-1, 2);
            }
            float maxAccel = Math.Min(7f, Math.Max(2f, 7f - t / 64f / 2f));
            xSlope = (float)Math.Cos((double)__instance.rotation + Math.PI / 2.0);
            ySlope = 0f - (float)Math.Sin((double)__instance.rotation + Math.PI / 2.0);
            __instance.xVelocity += (0f - xSlope) * maxAccel / 6f + (float)Game1.random.Next(-10, 10) / 100f;
            __instance.yVelocity += (0f - ySlope) * maxAccel / 6f + (float)Game1.random.Next(-10, 10) / 100f;
            if (Math.Abs(__instance.xVelocity) > Math.Abs((0f - xSlope) * 7f))
            {
                __instance.xVelocity -= (0f - xSlope) * maxAccel / 6f;
            }
            if (Math.Abs(__instance.yVelocity) > Math.Abs((0f - ySlope) * 7f))
            {
                __instance.yVelocity -= (0f - ySlope) * maxAccel / 6f;
            }

            Mon.Log("Finished redirecting in animation", LogLevel.Warn);
            Mon.Log($"Current x-velocity is {__instance.xVelocity} and y-velocity is {__instance.yVelocity}", LogLevel.Warn);
        }

        // Generate explosion animation when placed
        private static bool DoPearlExplosionAnimation(GameLocation location, int x, int y, Farmer who)
        {
            Vector2 placementTile = new Vector2(x / 64, y / 64);
            foreach (TemporaryAnimatedSprite temporarySprite2 in location.temporarySprites)
            {
                if (temporarySprite2.position.Equals(placementTile * 64f))
                {
                    return false;
                }
            }
            int idNum = Game1.random.Next();
            location.playSound("thudStep");
            TemporaryAnimatedSprite pearlTAS = new TemporaryAnimatedSprite(PearlLureID, 100f, 1, 24, placementTile * 64f, flicker: true, flipped: false, location, who)
            {
                bombRadius = 3,
                bombDamage = 1,
                shakeIntensity = 0.5f,
                shakeIntensityChange = 0.002f,
                extraInfoForEndBehavior = idNum,
                endFunction = location.removeTemporarySpritesWithID
            };
            multiplayer.broadcastSprites(location, pearlTAS);
            location.netAudio.StartPlaying("fuse");
            return true;
        }

        private static Bat getLure(GameLocation location)
        {
            foreach (Character charact in location.characters)
            {
                if (charact is Bat batChar && batChar.Name.Equals(pearlLureMonsterName, StringComparison.OrdinalIgnoreCase))
                {
                    Mon.Log("Found a lure monster", LogLevel.Warn);
                    return batChar;
                }
            }
            return null;
        }

        //private static Bat findClosestPearlMonster(Bat[] monsterList, int xLoc, int yLoc)
        //{
        //    if (monsterList.Length == 0)
        //    {
        //        return null;
        //    }

        //    List<Tuple<Bat, double>> batDists = new List<Tuple<Bat, double>>();
        //    foreach (Bat pearlMonst in monsterList)
        //    {
        //        double distance = Math.Sqrt((pearlMonst.GetBoundingBox().Center.X - xLoc) ^ 2 + (pearlMonst.GetBoundingBox().Center.Y - yLoc) ^ 2);
        //        batDists.Add(new Tuple<Bat,double> (pearlMonst, distance));
        //    }

        //    batDists.Sort((x, y) => y.Item1.CompareTo(x.Item1));

        //    return batDists[0].Item1;

        //}
    }
}