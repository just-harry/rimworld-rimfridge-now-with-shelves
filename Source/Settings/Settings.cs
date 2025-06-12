using RimWorld;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimFridge
{
	public class SettingsController : Mod
	{
		public SettingsController (ModContentPack content) : base(content)
		{
			base.GetSettings<Settings>();

			var harmony = new Harmony("com.rimfridge.rimworld.mod");

			var patch = (Type type) =>
			{
				try
				{
					harmony.CreateClassProcessor(type).Patch();
				}
				catch (Exception e)
				{
					Logger.Error(e.ToString());
				}
			};

			patch(typeof(EnsureThatItemsInAFridgeCanBeReachedByPawns));
			patch(typeof(Patch_Thing_AmbientTemperature));
			patch(typeof(Patch_PassingShip_TryOpenComms));
			patch(typeof(EnsureThatPrisonersGetFoodFromFridgesInPrisons));
			patch(typeof(DisplayStackedItemsNicelyInFridges.MungeTrueCenterOfItemsInFridges));
			patch(typeof(DisplayStackedItemsNicelyInFridges.MakeTheStackCountLabelsReadable));
			patch(typeof(HacksForCompatibility.ForceTheApplicationOfSomePatches));
		}

		public override string SettingsCategory ()
		{
			return "RimFridge";
		}

		internal class GUIState
		{
			internal Vector2 forcedApplicationOfPatchesScrollPosition;
			internal List<bool> initialStateOfShouldForceApplicationOfPatches;
		}

		internal GUIState guiState = null;

		public override void DoSettingsWindowContents (Rect rect)
		{
			if (guiState == null)
			{
				guiState = new();
				guiState.initialStateOfShouldForceApplicationOfPatches = (
					Settings.forcedApplicationOfPatches.Select(a => a.shouldForceApplication).ToList()
				);
			}

			GUI.BeginGroup(new Rect(0, 60, 800, 600));
			Text.Font = GameFont.Small;
			Widgets.Label(new Rect(0, 40, 300, 20), "RimFridge.ModifyBasePowerRequirement".Translate() + ":");
			Settings.PowerFactor.AsString = Widgets.TextField(new Rect(320, 40, 100, 20), Settings.PowerFactor.AsString);

			if (Widgets.ButtonText(new Rect(320, 65, 100, 20), "RimFridge.Apply".Translate()))
			{
				if (Settings.PowerFactor.ValidateInput())
				{
					GetSettings<Settings>().Write();
					Messages.Message("RimFridge.NewPowerFactorApplied".Translate(), MessageTypeDefOf.PositiveEvent);

					if (Current.Game != null)
					{
						RimFridgeSettingsUtil.ApplyFactor(Settings.PowerFactor.AsFloat);
					}
				}
			}

			Widgets.Label(new Rect(20, 100, 400, 30), "RimFridge.PowerFactorExplanation".Translate());
			Widgets.CheckboxLabeled(new Rect(0, 140, 200, 30), "RimFridge.ActAsTradeBeacon".Translate(), ref Settings.ActAsBeacon);
			Widgets.CheckboxLabeled(new Rect(0, 180, 200, 30), "RimFridge.EnableFrostyBeverages".Translate(), ref Settings.enableFrostyBeverages);

			if (ShouldShowCompatibilitySettings)
			{
				Widgets.DrawMenuSection(new Rect(0, 210, 800, 370));

				Widgets.Label(new Rect(10, 210, 790, 30), "RimFridge.Compatibility".Translate());

				Widgets.Label(new Rect(10, 240, 790, 30), "RimFridge.ForceApplicationOfThesePatches".Translate());
				Widgets.BeginScrollView(new Rect(0, 280, 800, 300), ref guiState.forcedApplicationOfPatchesScrollPosition, new Rect(0, 250, 800 - GenUI.ScrollBarWidth, 30 * Settings.forcedApplicationOfPatches.Count));

				for (int index = 0; index < Settings.forcedApplicationOfPatches.Count; ++index)
				{
					var patch = Settings.forcedApplicationOfPatches[index];

					Widgets.CheckboxLabeled(
						new Rect(20, 280 + index * 30, 780 - GenUI.ScrollBarWidth, 28),
						patch.patch,
						ref patch.shouldForceApplication,
						placeCheckboxNearText: true
					);
				}

				Widgets.EndScrollView();
			}

			GUI.EndGroup();
		}

		public override void WriteSettings ()
		{
			base.WriteSettings();

			if (ShouldShowCompatibilitySettings)
			{
				if (
					!guiState.initialStateOfShouldForceApplicationOfPatches.SequenceEqual(
						Settings.forcedApplicationOfPatches.Select(a => a.shouldForceApplication)
					)
				)
				{
					ModsConfig.RestartFromChangedMods();
				}
			}

			guiState = null;
		}

		protected static bool ShouldShowCompatibilitySettings
		{
			get => Current.Game == null;
		}
	}

	internal class Settings : ModSettings
	{
		public static readonly FloatInput PowerFactor = new FloatInput("RimFridge.BasePowerFactor");
		public static bool ActAsBeacon = false;
		public static bool enableFrostyBeverages = true;
		/* Making this a List causes access to be O(n), but we want to maintain
			the order the patches were loaded in. */
		public static List<ApplicationOfPatch> forcedApplicationOfPatches = new();

		internal class ApplicationOfPatch : IExposable
		{
			public string patch;
			public bool shouldForceApplication;

			public void ExposeData ()
			{
				Scribe_Values.Look(ref this.patch, "patch");
				Scribe_Values.Look(ref this.shouldForceApplication, "shouldForceApplication", true);
			}
		}

		public override void ExposeData ()
		{
			base.ExposeData();
			Scribe_Values.Look(ref(PowerFactor.AsString), "RimFridge.PowerFactor", "1.00", false);
			Scribe_Values.Look(ref ActAsBeacon, "RimFridge.ActAsBeacon", false, false);
			Scribe_Values.Look(ref enableFrostyBeverages, "RimFridge.EnableFrostyBeverages", true, false);
			Scribe_Collections.Look(ref forcedApplicationOfPatches, "RimFridge.ForcedApplicationOfPatches", LookMode.Deep);

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				forcedApplicationOfPatches ??= new();
			}
		}
	}
}

