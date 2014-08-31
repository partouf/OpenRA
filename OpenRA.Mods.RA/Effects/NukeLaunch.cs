#region Copyright & License Information
/*
 * Copyright 2007-2014 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Effects;
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Traits;
using System;

namespace OpenRA.Mods.RA.Effects
{
	public class NukeLaunch : IEffect, IMomentCapture
	{
		readonly Player firedBy;
		readonly Animation anim;
		readonly string weapon;

		readonly WPos ascendSource;
		readonly WPos ascendTarget;
		readonly WPos descendSource;
		readonly WPos descendTarget;
		readonly int delay;
		readonly int turn;

		WPos pos;
		int ticks;
		WPos targetPos;
		WPos launchPos;
		bool goingUp = true;

		int MagicCaptureImpactFrom = 427 * 2;

		public NukeLaunch(Player firedBy, string weapon, WPos launchPos, WPos targetPos, WRange velocity, int delay, bool skipAscent)
		{
			this.firedBy = firedBy;
			this.weapon = weapon;
			this.delay = delay;
			this.turn = delay / 2;

			this.launchPos = launchPos;
			this.targetPos = targetPos;

			var offset = new WVec(WRange.Zero, WRange.Zero, velocity * turn);
			ascendSource = launchPos;
			ascendTarget = launchPos + offset;
			descendSource = targetPos + offset;
			descendTarget = targetPos;

			anim = new Animation(firedBy.World, weapon);
			anim.PlayRepeating("up");

			pos = launchPos;
			var weaponRules = firedBy.World.Map.Rules.Weapons[weapon.ToLowerInvariant()];
			if (weaponRules.Report != null && weaponRules.Report.Any())
				Sound.Play(weaponRules.Report.Random(firedBy.World.SharedRandom), pos);

			if (skipAscent)
				ticks = turn;
		}


		public void Tick(World world)
		{
			anim.Tick();

			if (ticks == turn)
				anim.PlayRepeating("down");

			if (ticks <= turn)
				pos = WPos.LerpQuadratic(ascendSource, ascendTarget, WAngle.Zero, ticks, turn);
			else
				pos = WPos.LerpQuadratic(descendSource, descendTarget, WAngle.Zero, ticks - turn, delay - turn);

			if (ticks == delay)
				Explode(world);

			ticks++;
		}

		void Explode(World world)
		{
			world.AddFrameEndTask(w => w.Remove(this));
			var weapon = world.Map.Rules.Weapons[this.weapon.ToLowerInvariant()];
			weapon.Impact(Target.FromPos(pos), firedBy.PlayerActor, Enumerable.Empty<int>());
			world.WorldActor.Trait<ScreenShaker>().AddEffect(20, pos, 5);

			foreach (var a in world.ActorsWithTrait<NukePaletteEffect>())
				a.Trait.Enable();
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			return anim.Render(pos, wr.Palette("effect"));
		}

		public float FractionComplete { get { return ticks * 1f / delay; } }

		public bool ShouldCaptureThisMoment(out WPos focalpoint, out MomentCapturePriority priority)
		{
			if (!goingUp && (pos.Z <= MagicCaptureImpactFrom))
			{
				focalpoint = targetPos;
				priority = MomentCapturePriority.High;

				return true;
			}
			else if (goingUp && (Math.Abs(launchPos.Z - pos.Z) <= MagicCaptureImpactFrom))
			{
				focalpoint = launchPos;
				priority = MomentCapturePriority.Low;

				return true;
			}

			focalpoint = targetPos;
			priority = MomentCapturePriority.Low;

			return false;
		}
	}
}
