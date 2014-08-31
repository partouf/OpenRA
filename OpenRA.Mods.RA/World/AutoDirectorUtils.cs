using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenRA.Mods.RA.Buildings;
using OpenRA.Traits;
using OpenRA.Mods.RA.Effects;
using OpenRA.Effects;

namespace OpenRA.Mods.RA
{
	public enum ActivityType { Unknown, Buildings, Mixed, Troups, Battle, KodakMoment }

	public class ActivityHotspot : IComparable<ActivityHotspot> {
		public ActivityType Type;
		public int Owner = 0;

		protected int maxDistanceX;
		protected int maxDistanceY;

		protected List<int2> lstPoints;
		public int2 CenterPosInt2;

		protected int healthDeficiencyTotal = 0;
		protected int customHeat = 0;

		public ActivityHotspot(int maxDistanceX, int maxDistanceY)
		{
			lstPoints = new List<int2>();
			CenterPosInt2 = new int2();

			this.maxDistanceX = maxDistanceX;
			this.maxDistanceY = maxDistanceY;

			Type = ActivityType.Unknown;
			Owner = 0;
		}

		public bool HasPoints()
		{
			return lstPoints.Count > 0;
		}

		// include total health deficiency to heat, the more damaged "stuff", the more important
		public int Heat()
		{
			// the more buildings however that don't get repaired (lategame), the more we should probably lower the importance of health deficiency
			if ((Type == ActivityType.Buildings) || (Type == ActivityType.Mixed))
			{
				return
					lstPoints.Count + (healthDeficiencyTotal / lstPoints.Count);
			}
			else if (Type == ActivityType.KodakMoment)
			{
				return customHeat;
			}
			else
			{
				return
					lstPoints.Count + healthDeficiencyTotal;
			}
		}

		public void SetCustomHeat(int heat)
		{
			this.customHeat = heat;
		}

		public int2 DistanceTo(int2 anotherPos)
		{
			if (!this.HasPoints())
			{
				return new int2(0, 0);
			}

			var diff = CenterPosInt2 - anotherPos;

			return new int2(Math.Abs(diff.X), Math.Abs(diff.Y));
		}

		public int2 DistanceTo(CPos anotherPos)
		{
			return DistanceTo(anotherPos.ToInt2());
		}

		public int2 DistanceTo(ActivityHotspot anotherPos)
		{
			return DistanceTo(anotherPos.CenterPosInt2);
		}

		public bool IsCloseBy(int2 anotherPos)
		{
			var distance = this.DistanceTo(anotherPos);

			// do different IsCloseBy for vertical distance, as the viewport isn't a square
			return
				(distance.X <= maxDistanceX) &&
				(distance.Y <= maxDistanceY);
		}

		public bool IsCloseBy(CPos anotherPos)
		{
			return IsCloseBy(anotherPos.ToInt2());
		}

		public bool IsCloseBy(ActivityHotspot anotherPos)
		{
			return IsCloseBy(anotherPos.CenterPosInt2);
		}

		// we only need to be able to set the viewport to include all points, doesn't have to be fancy mathematically
		protected void CalculateCenter()
		{
			if (lstPoints.Count > 1)
			{
				int2 min = new int2(0, 0);
				int2 max = new int2(0, 0);

				bool first = true;
				foreach (var p in lstPoints)
				{
					if (first)
					{
						min = p;
						max = p;
						first = false;
					}
					else
					{
						min = int2.Min(min, p);
						max = int2.Max(max, p);
					}
				}

				CenterPosInt2.X = min.X + ((max.X - min.X) / 2);
				CenterPosInt2.Y = min.Y + ((max.Y - min.Y) / 2);
			}
			else if (lstPoints.Count == 1)
			{
				CenterPosInt2.X = lstPoints[0].X;
				CenterPosInt2.Y = lstPoints[0].Y;
			}
			else
			{
				CenterPosInt2.X = 0;
				CenterPosInt2.Y = 0;
			}
		}

		public void Add(Actor anotherPos)
		{
			if (Owner == 0)
			{
				Owner = anotherPos.Owner.ClientIndex;
			}
			else if (Owner != anotherPos.Owner.ClientIndex)
			{
				Owner = -1; // (mixed owners)
			}

			var health = anotherPos.TraitOrDefault<Health>();
			if (health != null)
			{
				healthDeficiencyTotal += health.MaxHP - health.HP;
			}

			lstPoints.Add(anotherPos.Location.ToInt2());

			CalculateCenter();
		}

		public int CompareTo(ActivityHotspot other)
		{
			if (other.Owner == Owner)
			{
				// same, no special priority
			}
			else if (other.Owner == -1)
			{
				// -1 (mixed owners) is more important
				return -1;
			}
			else if (this.Owner == -1)
			{
				return 1;
			}

			return other.Heat() - this.Heat();
		}
	}

	static class AutoDirectorUtils
	{
		public static bool FightsGoingOn(this World world)
		{
			bool b = false;

			foreach (var actor in world.Actors)
			{
				if (actor.IsInWorld && !actor.IsDead() && actor.HasTrait<AttackBase>())
				{
					if (actor.Trait<AttackBase>().IsAttacking)
					{
						b = true;
						break;
					}
				}
			}

			return b;
		}

		public static void ListSupportPowers(this World world, List<ActivityHotspot> lstHotspots, bool highOnly)
		{
			foreach (var effect in world.Effects)
			{
				if (effect is IMomentCapture)
				{
					IMomentCapture cap = (IMomentCapture)effect;
					WPos pos;
					MomentCapturePriority prio;
					if (cap.ShouldCaptureThisMoment(out pos, out prio))
					{
						ActivityHotspot spot = new ActivityHotspot(0, 0);
						spot.Owner = -1;
						spot.Type = ActivityType.KodakMoment;
						spot.CenterPosInt2 = pos.ToCPos().ToInt2();
						spot.SetCustomHeat(99 - (int)prio);

						if (prio == MomentCapturePriority.High)
						{
							lstHotspots.Insert(0, spot);
							if (highOnly)
							{
								return;
							}
						}
						else if (!highOnly)
						{
							lstHotspots.Add(spot);
						}
					}
				}
			}
		}

		// todo: some brand of helicopter's attack isn't being handled by this code?
		public static List<ActivityHotspot> FindHotspots(this World world, int maxDistanceX = 6, int maxDistanceY = 5)
		{
			var lstHotspots = new List<ActivityHotspot>();

			ListSupportPowers(world, lstHotspots, true);
			if (lstHotspots.Count > 0)
			{
				return lstHotspots;
			}

			lstHotspots.Clear();
			ListSupportPowers(world, lstHotspots, false);

			bool attackingGoingOn = false;

			// units and combat
			foreach (var actor in world.Actors)
			{
				if ((actor.Owner.PlayerActor != null) && actor.IsInWorld && !actor.IsDead() && actor.HasTrait<AttackBase>())
				{
					if (actor.Trait<AttackBase>().IsFiringOrReloading())
					{
						attackingGoingOn = true;

						bool found = false;
						foreach (var possiblehotspot in lstHotspots)
						{
							if ((possiblehotspot.Type != ActivityType.KodakMoment) && possiblehotspot.IsCloseBy(actor.Location))
							{
								if (possiblehotspot.Type != ActivityType.Battle)
								{
									// make a higher ranked hotspot, so the attacking units will be focussed instead of the normal units
									var newspot = new ActivityHotspot(maxDistanceX, maxDistanceY);
									newspot.Type = ActivityType.Battle;
									newspot.Add(actor);
									lstHotspots.Add(newspot);
								}
								else
								{
									possiblehotspot.Add(actor);
								}
								found = true;
								break;
							}
						}

						if (!found)
						{
							var newspot = new ActivityHotspot(maxDistanceX, maxDistanceY);
							newspot.Type = ActivityType.Battle;
							newspot.Add(actor);
							lstHotspots.Add(newspot);
						}
					}
					else
					{
						bool found = false;
						foreach (var possiblehotspot in lstHotspots)
						{
							if ((possiblehotspot.Type != ActivityType.KodakMoment) && possiblehotspot.IsCloseBy(actor.Location))
							{
								// don't change activity type, if it's a battle we need to know
								possiblehotspot.Add(actor);
								found = true;
								break;
							}
						}

						if (!found)
						{
							var newspot = new ActivityHotspot(maxDistanceX, maxDistanceY);
							newspot.Type = ActivityType.Troups;
							newspot.Add(actor);
							lstHotspots.Add(newspot);
						}
					}
				}
			}

			if (!attackingGoingOn)
			{
				// find building hotspots if nothing else is going on
				foreach (var actor in world.Actors)
				{
					if ((actor.Owner.ClientIndex != 0) && actor.IsInWorld && actor.HasTrait<Building>())
					{
						bool found = false;
						foreach (var possiblehotspot in lstHotspots)
						{
							if ((possiblehotspot.Type != ActivityType.KodakMoment) && possiblehotspot.IsCloseBy(actor.Location))
							{
								if (actor.HasTrait<AttackBase>() && actor.Trait<AttackBase>().IsFiringOrReloading())
								{
									// upgrade to battle if it's an attacking turret

									if (possiblehotspot.Type != ActivityType.Battle)
									{
										// make a higher ranked hotspot, so the attacking turrets will be focussed instead of the normal buildings
										var newspot = new ActivityHotspot(maxDistanceX, maxDistanceY);
										newspot.Type = ActivityType.Battle;
										newspot.Add(actor);
										lstHotspots.Add(newspot);
									}
									else
									{
										possiblehotspot.Add(actor);
									}
								}
								else if (possiblehotspot.Type == ActivityType.Troups)
								{
									possiblehotspot.Type = ActivityType.Mixed;
									possiblehotspot.Add(actor);
								}
								else
								{
									possiblehotspot.Add(actor);
								}

								found = true;
								break;
							}
						}

						if (!found)
						{
							var newspot = new ActivityHotspot(maxDistanceX, maxDistanceY);
							if (actor.HasTrait<AttackBase>() && actor.Trait<AttackBase>().IsFiringOrReloading())
							{
								newspot.Type = ActivityType.Battle;
							}
							else
							{
								newspot.Type = ActivityType.Buildings;
							}

							newspot.Add(actor);
							lstHotspots.Add(newspot);
						}
					}
				}
			}

			lstHotspots.Sort();

			return lstHotspots;
		}
	}
}
