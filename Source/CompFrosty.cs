using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimFridge
{
	internal class CompFrosty : ThingComp
	{
		public static readonly AccessTools.FieldRef<TickManager, TickList> tickListRareOfTickManager = (
			AccessTools.FieldRefAccess<TickManager, TickList>("tickListRare")
		);

		// Most beer's ideal temperature is around 8 degC
		private const float IDEAL_TEMPERATURE = 8f;

		// Starting temperature
		public float temperature = 21f;

		public CompProperties_Frosty Props => (CompProperties_Frosty) props;

		public override void PostIngested (Pawn ingester)
		{
			base.PostIngested(ingester);

			if (!Settings.enableFrostyBeverages)
			{
				return;
			}

			if (temperature <= IDEAL_TEMPERATURE)
			{
				ingester.needs.mood.thoughts.memories.TryGainMemory(Props.thought, null);
			}
		}

		public override void PostSplitOff (Thing piece)
		{
			if (!Settings.enableFrostyBeverages)
			{
				return;
			}

			ThingWithComps thingWithComps = piece as ThingWithComps;

			if (thingWithComps.GetComp<CompFrosty>() == null)
			{
				CompFrosty compFrosty = new CompFrosty();
				thingWithComps.AllComps.Add(compFrosty);
				compFrosty.props = CompProperties_Frosty.Beer;
				compFrosty.parent = thingWithComps;
				compFrosty.temperature = temperature;

				/* If this thing's ticker-type is rare,
				   it will have already been registered in the rare-tick-list
				   by `Thing#SpawnSetup`; if so we won't register it again. */
				if (thingWithComps.def.tickerType != TickerType.Rare)
				{
					tickListRareOfTickManager(Find.TickManager).RegisterThing(thingWithComps);
				}
			}
		}

		public override void CompTickRare ()
		{
			if (!Settings.enableFrostyBeverages)
			{
				return;
			}

			base.CompTickRare();

			if (!this.parent.Spawned)
			{
				return;
			}

			Map map = this.parent.MapHeld;
			IntVec3 cell = this.parent.PositionHeld;

			float ambientTemperature;

			if (FridgeCache.TryGetFridge(cell, map, out CompRefrigerator fridge))
			{
				ambientTemperature = fridge.currentTemp;
			}
			else
			{
				GenTemperature.TryGetTemperatureForCell(cell, map, out ambientTemperature);
			}

			this.temperature += (ambientTemperature - this.temperature) * 0.05f;
		}

		public override string CompInspectStringExtra ()
		{
			if (!Settings.enableFrostyBeverages)
			{
				return null;
			}

			return this.temperature <= IDEAL_TEMPERATURE ? "RimFridge.FrostyBeverage".Translate() : null;
		}
	}
}

