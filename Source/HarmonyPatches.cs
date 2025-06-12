using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimFridge
{
	[HarmonyPatch(
		typeof(Reachability),
		nameof(Reachability.CanReach),
		new[] {typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms)})
	]
	public static class EnsureThatItemsInAFridgeCanBeReachedByPawns
	{
		/* At first glance this patch looks as though it's for path-finding.
		   But it's really for allowing items in fridges to be considered reachable
		   in the menu that pops up when an item in a fridge is right-clicked. */

		public static readonly AccessTools.FieldRef<Reachability, Map> mapOfReachability = (
			AccessTools.FieldRefAccess<Reachability, Map>("map")
		);

		[HarmonyPrefix]
		public static void SetPathEndModeSuchThatFridgeCanBeReached (
			Reachability __instance,
			LocalTargetInfo dest,
			ref PathEndMode peMode
		)
		{
			if (
				   peMode != PathEndMode.Touch
				&& dest.Thing?.def.category == ThingCategory.Item
				&& FridgeCache.GetFridgeCache(mapOfReachability(__instance))?.HasFridgeAt(dest.Cell) == true
			)
			{
				peMode = PathEndMode.Touch;
			}
		}
	}

	[HarmonyBefore(new string[] {"io.github.dametri.thermodynamicscore"})]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Thing), "AmbientTemperature", MethodType.Getter)]
	static class Patch_Thing_AmbientTemperature
	{
		static bool Prefix (Thing __instance, ref float __result)
		{
			Pawn p = __instance as Pawn;

			if (
				   (p == null || p.Dead)
				&& __instance.Map != null
				&& FridgeCache.TryGetFridge(__instance.Position, __instance.Map, out CompRefrigerator fridge)
				&& fridge != null
			)
			{
				__result = fridge.currentTemp;
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(TradeShip), "ColonyThingsWillingToBuy")]
	static class Patch_PassingShip_TryOpenComms
	{
		// Before an orbital trade
		static void Postfix (ref IEnumerable<Thing> __result, Pawn playerNegotiator)
		{
			if (!Settings.ActAsBeacon)
				return;

			List<Thing> things = null;

			if (playerNegotiator != null && playerNegotiator.Map != null)
			{
				foreach (Thing thing in playerNegotiator.Map.listerBuildings.allBuildingsColonist)
				{
					if (thing is RimFridge_Building storage)//IsRimFridge(thing?.def))
					{
						//var storage = thing as Building_Storage;
						foreach (IntVec3 cell in storage.AllSlotCells())
						{
							foreach (Thing refrigeratedItem in playerNegotiator.Map.thingGrid.ThingsAt(cell))
							{
								if (storage.settings.AllowedToAccept(refrigeratedItem))
								{
									if (things == null)
									{
										if (__result?.Count() == 0)
											things = new List<Thing>();
										else
											things = new List<Thing>(__result);
									}

									things.Add(refrigeratedItem);
									break;
								}
							}
						}
					}
				}
			}

			if (things != null)
				__result = things;
		}
	}

	[HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.TryFindBestFoodSourceFor))]
	static class EnsureThatPrisonersGetFoodFromFridgesInPrisons
	{
		static void Postfix (ref bool __result, Pawn getter, Pawn eater, ref Thing foodSource, ref ThingDef foodDef)
		{
			if (
				   __result == false
				&& getter.Map != null
				&& getter == eater
				&& getter.RaceProps.ToolUser
				&& getter.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
			{
				Room prison = getter.Position.GetRoomOrAdjacent(getter.Map);

				if (prison != null && prison.IsPrisonCell)
				{
					foreach (Thing t in prison.ContainedAndAdjacentThings)
					{
						if (
							   t.Map != null
							&& t is Building_Storage storage
							&& !t.IsForbidden(getter)
						)
						{
							foreach (IntVec3 cell in storage.AllSlotCells())
							{
								foreach (Thing possibleFood in t.Map.thingGrid.ThingsAt(cell))
								{
									if (
										   getter.RaceProps.CanEverEat(possibleFood)
										&& !possibleFood.IsForbidden(getter)
										&& storage.Map.reservationManager.CanReserve(getter, new LocalTargetInfo(possibleFood))
									)
									{
										__result = true;
										foodSource = possibleFood;
										foodDef = possibleFood.def;
										return;
									}
								}
							}
						}
					}
				}
			}
		}
	}

	public static class DisplayStackedItemsNicelyInFridges
	{
		[HarmonyPatch(typeof(GenThing), nameof(GenThing.TrueCenter), new[] {typeof(Thing)})]
		public static class MungeTrueCenterOfItemsInFridges
		{
			[HarmonyPostfix]
			public static Vector3 MungeTrueCenter (Vector3 originalValue, Thing t)
			{
				if (t.def.category == ThingCategory.Item && t.Spawned)
				{
					var things = t.Map.thingGrid.ThingsListAtFast(t.Position);

					if (things.Count > 2)
					{
						var thingID = t.thingIDNumber;
						int depthInStack = 0;
						bool haveFridgeInCell = false;

						foreach (var eachThing in things)
						{
							haveFridgeInCell = haveFridgeInCell || eachThing is RimFridge_Building;
							depthInStack += (
								eachThing.thingIDNumber < thingID
								&& eachThing.def.category == ThingCategory.Item
							) ? 1 : 0;
						}

						if (haveFridgeInCell)
						{
							IntVec3 p = t.Position;
							Vector3 v = p.ToVector3Shifted();
							float altitude = t.def.Altitude;

							return new Vector3(
								v.x,
								altitude + (float) depthInStack * (3f / 74f) / 10f,
								v.z + (float) depthInStack / 16f - 0.05f
							);
						}
					}
				}

				return originalValue;
			}
		}

		[HarmonyPatch(typeof(GenMapUI), nameof(GenMapUI.LabelDrawPosFor), new[] {typeof(Thing), typeof(float)})]
		public static class MakeTheStackCountLabelsReadable
		{
			[HarmonyPostfix]
			public static Vector2 OffsetTheLabels (Vector2 originalValue, Thing thing)
			{
				if (thing.def.category == ThingCategory.Item && thing.Spawned)
				{
					var things = thing.Map.thingGrid.ThingsListAtFast(thing.Position);

					if (things.Count > 2)
					{
						var thingID = thing.thingIDNumber;
						int depthInStack = -1;
						bool haveFridgeInCell = false;

						foreach (var eachThing in things)
						{
							haveFridgeInCell = haveFridgeInCell || eachThing is RimFridge_Building;
							depthInStack += (
								eachThing.thingIDNumber < thingID
								&& eachThing.def.category == ThingCategory.Item
							) ? 1 : 0;
						}

						if (haveFridgeInCell)
						{
							originalValue.x += (float) depthInStack * 17.0f;
						}
					}
				}

				return originalValue;
			}
		}
	}

	public static class HacksForCompatibility
	{
		[HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.ErrorCheckPatches))]
		public static class ForceTheApplicationOfSomePatches
		{
			[HarmonyPostfix]
			public static void RifleThroughThePatchesThatAreAboutToBeApplied ()
			{
				/* If an exception is thrown by ErrorCheckPatches, the game stops loading and all that's
					left is a black screen. To avoid being the mod that breaks a user's game, we catch
					ANY exception. */
				try
				{
					var rimFridge = LoadedModManager.GetMod<SettingsController>();
					var rimFridgeMetadata = rimFridge.Content.ModMetaData;
					var rimFridgeIndex = Compatibility.FindIndexOfModInActiveModLoadOrder(rimFridge);

					var patchesToNotForceApplicationOf = new HashSet<string>(
						Settings.forcedApplicationOfPatches.Where(a => !a.shouldForceApplication).Select(a => a.patch)
					);

					var applicationOfPatches = new List<Settings.ApplicationOfPatch>();

					var modsField = typeof(PatchOperationFindMod).GetField("mods", BindingFlags.Instance | BindingFlags.NonPublic);
					var applicableModNamesOf = (PatchOperationFindMod p) => (List<string>) modsField.GetValue(p);

					foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading.Skip(rimFridgeIndex + 1))
					{
						var patches = mod.Patches.OfType<PatchOperationFindMod>();

						if (patches.Any(p => applicableModNamesOf (p).Any(name => name == rimFridgeMetadata.Name)))
						{
							/* This mod has at-least one patch that targets us, so we're not going to force the
								application of any patches, as that would likely cause problems if the mod's already
								accounted for us in a potentially-separate patch. */
							continue;
						}

						foreach (var patch in patches)
						{
							try
							{
								var applicableModNames = applicableModNamesOf(patch);

								if (applicableModNames.Any(Compatibility.IsRecognisedRimFridgeModName))
								{
									var patchID = Compatibility.IdentifierForPatch(patch, of: mod);

									var forcingApplication = !patchesToNotForceApplicationOf.Contains(patchID);

									if (forcingApplication)
									{
										Logger.Message($"Forcing the application of patch {patchID} of {mod.ModMetaData.Name}.");

										applicableModNames.Add(rimFridgeMetadata.Name);
									}

									applicationOfPatches.Add(
										new()
										{
											patch = patchID,
											shouldForceApplication = forcingApplication
										}
									);
								}
							}
							catch (Exception e)
							{
								Logger.Error(e.ToString());
							}
						}
					}

					applicationOfPatches.TrimExcess();
					Settings.forcedApplicationOfPatches = applicationOfPatches;
				}
				catch (Exception e)
				{
					Logger.Error(e.ToString());
				}
			}
		}
	}
}

