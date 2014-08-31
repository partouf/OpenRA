#region Copyright & License Information
/*
 * Copyright 2007-2014 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

namespace OpenRA.Effects {
	public enum MomentCapturePriority { High = 0, Low = 1 };

	public interface IMomentCapture {
		bool ShouldCaptureThisMoment(out WPos focalpoint, out MomentCapturePriority priority);
	}
}
