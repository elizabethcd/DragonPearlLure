﻿using System;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
    }
}