using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using OpenRA.Graphics;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA.Widgets
{
	public class AutoDirectorWidget : Widget
	{
		public readonly float MaxPanningTime;
		public readonly int MaxPannableDistance;

		public readonly int TimeToWaitMax;
		public readonly int TimeToWaitForBattleView;
		public readonly int TimeToWaitForTroupsView;

		public readonly int HotspotSizeX;
		public readonly int HotspotSizeY;

		public Func<bool> IsEnabled = () => false;

		private readonly World world;
		private readonly WorldRenderer worldRenderer;

		protected CheckboxWidget chkAutoDirector;

		protected DateTime lastViewJump = DateTime.Now;
		protected int2 lastCenter = new int2(0, 0);
		protected List<ActivityHotspot> currentHotspotList = null;
		protected ActivityHotspot lastHighlight;

		protected bool panningCamera = false;
		protected double panningTimer = 0;
		protected WPos panningTo;
		protected DateTime lastPanningTick = DateTime.Now;

		protected List<int> ownerRotation = new List<int>();

		// debugging vars
		public bool DebugMode = true;
		protected string DebugFont = ChromeMetrics.Get<string>("TextFont");

		[ObjectCreator.UseCtor]
		public AutoDirectorWidget(World world, WorldRenderer worldRenderer)
		{
			this.world = world;
			this.worldRenderer = worldRenderer;

			this.lastHighlight = null;

			Log.AddChannel("autodirector", "autodirector.txt");
		}

		public void ClickAutodirectorCheckbox()
		{
			var chk = !chkAutoDirector.IsChecked();
			chkAutoDirector.IsChecked = () => chk;
			this.IsEnabled = () => chk;
		}

		public void Init(Widget parentWidget)
		{
			chkAutoDirector = parentWidget.Get<CheckboxWidget>("AUTODIRECTOR_CHECKBOX");
			chkAutoDirector.OnClick = ClickAutodirectorCheckbox;

			var chk = chkAutoDirector.IsChecked();
			chkAutoDirector.IsChecked = () => chk;
			this.IsEnabled = () => chk;
		}

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
		}

		public override void Draw()
		{
			if (currentHotspotList != null)
			{
				int maxheat = (from spot in currentHotspotList
							  select spot.Heat()).Max();

				foreach (var spot in currentHotspotList)
				{
					int heat = spot.Heat();

					var pos = new WPos(1024 * spot.CenterPosInt2.X + 512, 1024 * spot.CenterPosInt2.Y + 512, 0);
					double fr = (float)heat / (float)maxheat * 1024f;
					var range = new WRange(Math.Max((int)fr, 10));

					this.worldRenderer.DrawRangeCircleWithContrast(pos, range, Color.Red, Color.Black);

					SpriteFont font = Game.Renderer.Fonts[DebugFont];
					var text = spot.Type.ToString();

					var screenPos = this.worldRenderer.Viewport.Zoom * (this.worldRenderer.ScreenPosition(pos) - this.worldRenderer.Viewport.TopLeft.ToFloat2()) - 0.5f * font.Measure(text).ToFloat2();
					var screenPxPos = new float2((float)Math.Round(screenPos.X), (float)Math.Round(screenPos.Y));

					font.DrawTextWithContrast(text, screenPxPos, Color.Red, Color.Black, 1);
				}
			}
		}

		/*

		How should it work?

		- Battle moments should always be highlighted, but if multiple battles are going on, there should be something intelligent.....

		- Troups moving out should be highlighted.

		- Bases should be highlighted, but how to determine which one?

		*/

		// determine battle moment to highlight
		protected ActivityHotspot GetImportantBattle()
		{
			foreach (var spot in currentHotspotList)
			{
				if ((spot.Type == ActivityType.KodakMoment) && (spot.Heat() >= 99))
				{
					// nukes, emps, etc
					return spot;
				}
			}

			foreach (var spot in currentHotspotList)
			{
				if (spot.Type == ActivityType.Battle)
				{
					// first battle is the one with the most Heat(), so there's probably lots of interesting stuff going on?
					return spot;
				}
				else if ((spot.Type == ActivityType.KodakMoment) && (spot.Heat() < 99))
				{
					// launching nukes, if no battle
					return spot;
				}
			}

			return null;
		}

		protected ActivityHotspot GetImportantTroupMovementOrBuildingPlacements()
		{
			// todo: troup movement can be a little boring to watch if there's only 1 player moving out, or
			//   if player isn't moving his units at all, what to do then?
			foreach (var spot in currentHotspotList)
			{
				if (spot.Type == ActivityType.Troups)
				{
					// if multiple players are approaching eachother, they will have owner -1 and be sorted higher on the list
					//  but if it's not -1, alternate between factions
					if (ownerRotation.Contains(spot.Owner))
					{
						// try to go to a different owner, so skip this one
					}
					else if (spot.Owner == -1)
					{
						return spot;
					}
					else
					{
						return spot;
					}
				}
			}

			// try to look for buildings from owners who don't have troups on the field
			foreach (var spot in currentHotspotList)
			{
				if ((spot.Type == ActivityType.Buildings) || (spot.Type == ActivityType.Mixed))
				{
					// todo: how to determine if this is important enough or if this base is getting too much attention, etc...
					//   when we get to the buildingspots, we have a lot more freedom than in other situations
					if (ownerRotation.Contains(spot.Owner))
					{
						// try to go to a different owner, so skip this one
					}
					else
					{
						return spot;
					}
				}
			}

			// options depleted, start over again
			ownerRotation.Clear();

			foreach (var spot in currentHotspotList)
			{
				if ((spot.Type == ActivityType.Troups))
				{
					return spot;
				}
			}

			foreach (var spot in currentHotspotList)
			{
				if ((spot.Type == ActivityType.Mixed))
				{
					return spot;
				}
			}

			foreach (var spot in currentHotspotList)
			{
				if ((spot.Type == ActivityType.Buildings))
				{
					return spot;
				}
			}

			return null;
		}

		protected void TickPanner()
		{
			if (panningCamera)
			{
				var now = DateTime.Now;
				var timediff = now - lastPanningTick;
				lastPanningTick = now;

				panningTimer += timediff.TotalSeconds;
				if (panningTimer < MaxPanningTime)
				{
					var viewdiff = panningTo - this.worldRenderer.Viewport.CenterPosition;

					// we should be at this % of the ride
					// double p = PanningTimer / MaxPanningTime;

					// we have this amount of time left
					double tl = MaxPanningTime - panningTimer;

					// this tick was % of the time left
					double p = timediff.TotalSeconds / tl;

					double xd = viewdiff.X * p;
					double yd = viewdiff.Y * p;

					int x = this.worldRenderer.Viewport.CenterPosition.X + (int)Math.Round(xd);
					int y = this.worldRenderer.Viewport.CenterPosition.Y + (int)Math.Round(yd);

					if (viewdiff.X > 0)
					{
						x = Math.Min(x, panningTo.X);
					}
					else if (viewdiff.X < 0)
					{
						x = Math.Max(x, panningTo.X);
					}

					if (viewdiff.Y > 0)
					{
						y = Math.Min(y, panningTo.Y);
					}
					else if (viewdiff.Y < 0)
					{
						y = Math.Max(y, panningTo.Y);
					}

					var newpos = new WPos(x, y, 0);

					if (panningTo == newpos)
					{
						panningCamera = false;
					}

					this.worldRenderer.Viewport.Center(newpos);
				}
				else
				{
					this.worldRenderer.Viewport.Center(panningTo);

					panningCamera = false;
				}
			}
		}

		public void PanCamera(WPos to)
		{
			var diff = to - this.worldRenderer.Viewport.CenterPosition;

			// todo: don't try to go over screen border too much? ... 
			if (diff.Length > MaxPannableDistance)
			{
				this.worldRenderer.Viewport.Center(to);

				panningCamera = false;
			}
			else
			{
				panningTimer = 0;
				panningTo = to;
				panningCamera = true;

				lastPanningTick = DateTime.Now;
			}
		}

		public override void Tick()
		{
			if (IsEnabled())
			{
				TickPanner();

				currentHotspotList = AutoDirectorUtils.FindHotspots(world, HotspotSizeX, HotspotSizeY);

				/*
				var i = 1;
				foreach (var spot in CurrentHotspotList) {
					Log.Write("autodirector", "spot {0}: {1} degrees of {2} at {3},{4} (Owner {5})", i, spot.Heat(), (int)(spot.Type), spot.CenterPosInt2.X, spot.CenterPosInt2.Y, spot.Owner);
					i++;
				}
				*/

				ActivityHotspot spotToHighlight = null;

				// 1. Battle moments should always be highlighted, but if multiple battles are going on, there should be something intelligent.....
				if (spotToHighlight == null)
				{
					spotToHighlight = GetImportantBattle();
				}

				// 2. Troups moving out should be highlighted.
				// and 3. Bases should be highlighted
				if (spotToHighlight == null)
				{
					spotToHighlight = GetImportantTroupMovementOrBuildingPlacements();
				}

				// If we found something interesting, go put it inside the viewport
				if (spotToHighlight != null)
				{
					if ((spotToHighlight.Type != ActivityType.KodakMoment) && (spotToHighlight.IsCloseBy(this.worldRenderer.Viewport.CenterPosition.ToCPos())))
					{
						// stay on current spot for a bit longer, especially if there was a battle going on a bit ago,
						if (spotToHighlight.Type == ActivityType.Battle)
						{
							lastViewJump = DateTime.Now;
						}

						return;
					}
					else
					{
						// if it's not close by, make sure we don't just jump directly, but only after enough time has passed for "the smoke to have cleared up"
						var diff = DateTime.Now - lastViewJump;
						double waitTime = TimeToWaitMax;

						// don't have to wait as long if its battle or troup movement
						if (spotToHighlight.Type == ActivityType.Battle)
						{
							waitTime = TimeToWaitForBattleView;
						}
						else if (spotToHighlight.Type == ActivityType.Troups)
						{
							waitTime = TimeToWaitForTroupsView;
						}
						else if (spotToHighlight.Type == ActivityType.KodakMoment)
						{
							waitTime = 0;
						}

						if (diff.TotalSeconds > waitTime)
						{
							// ok
						}
						else
						{
							return;
						}
					}

					if (!ownerRotation.Contains(spotToHighlight.Owner))
					{
						ownerRotation.Add(spotToHighlight.Owner);
					}

					lastHighlight = spotToHighlight;
					lastCenter = spotToHighlight.CenterPosInt2;

					var newpos = new WPos(1024 * lastCenter.X + 512, 1024 * lastCenter.Y + 512, 0);
					PanCamera(newpos);

					lastViewJump = DateTime.Now;
				}
			}
		}
	}
}
