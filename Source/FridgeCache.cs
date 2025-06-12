using Verse;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RimFridge
{
	public class FridgeCache : MapComponent
	{
		private Dictionary<IntVec3, CompRefrigerator> FridgeGrid = new Dictionary<IntVec3, CompRefrigerator>();

		public FridgeCache (Map map) : base(map) { }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool HasFridgeAt (IntVec3 cell)
		{
			return this.FridgeGrid.ContainsKey(cell);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static FridgeCache GetFridgeCache (Map map)
		{
			return map.GetComponent<FridgeCache>();
		}

		public static void AddFridge (CompRefrigerator comp, Map map)
		{
			var c = GetFridgeCache(map);

			if (c != null)
			{
				foreach (IntVec3 cell in GenAdj.OccupiedRect(comp.parent))
				{
					c.FridgeGrid[cell] = comp;
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool TryGetFridge (IntVec3 cell, Map map, out CompRefrigerator comp)
		{
			var c = GetFridgeCache(map);

			if (c != null)
			{
				return c.FridgeGrid.TryGetValue(cell, out comp);
			}

			comp = null;
			return false;
		}

		public static void RemoveFridge (CompRefrigerator comp, Map map)
		{
			var c = GetFridgeCache(map);

			if (c != null)
			{
				foreach (IntVec3 cell in GenAdj.OccupiedRect(comp.parent))
				{
					c.FridgeGrid.Remove(cell);
				}
			}
		}

		public override void ExposeData ()
		{
			base.ExposeData();
		}
	}
}

