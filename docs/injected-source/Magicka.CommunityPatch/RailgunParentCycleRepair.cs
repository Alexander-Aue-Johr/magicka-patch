using System;
using System.Collections.Generic;
using System.Text;
using Magicka.CommunityPatch;

namespace Magicka.GameLogic.Spells
{
	// Readable equivalent of the members and edits injected into Railgun.
	internal partial class Railgun
	{
		private const int CommunityPatchRailTraversalLimit = 256;
		private bool mCommunityPatchLockAllActive;

		private static void CommunityPatchReportParentCycleRecovery(
			string reason,
			int visitedCount,
			int pendingCount,
			int candidateParentCount)
		{
			try
			{
				StringBuilder details = new StringBuilder();
				details.Append("visited_count=").Append(visitedCount);
				details.Append(";pending_count=").Append(pendingCount);
				details.Append(";candidate_parent_count=")
					.Append(candidateParentCount);
				PatchTelemetry.SendRuntimeGuard(
					"magicka_patch_runtime_recovery",
					reason,
					"Railgun.mParents",
					"Magicka.GameLogic.Spells.Railgun",
					details.ToString(),
					string.Empty);
			}
			catch
			{
			}
		}

		private bool CommunityPatchWouldCreateParentCycle(Railgun candidate)
		{
			try
			{
				if (candidate == null)
				{
					CommunityPatchReportParentCycleRecovery(
						"railgun_parent_cycle_check_failed", 0, 0, 0);
					return true;
				}

				Railgun[] pending =
					new Railgun[CommunityPatchRailTraversalLimit];
				Railgun[] visited =
					new Railgun[CommunityPatchRailTraversalLimit];
				pending[0] = this;
				int pendingCount = 1;
				int visitedCount = 0;

				while (pendingCount > 0)
				{
					Railgun current = pending[--pendingCount];
					if (current == null)
						continue;

					bool alreadyVisited = false;
					for (int i = 0; i < visitedCount; i++)
					{
						if (ReferenceEquals(visited[i], current))
						{
							alreadyVisited = true;
							break;
						}
					}
					if (alreadyVisited)
						continue;

					if (ReferenceEquals(current, candidate))
					{
						CommunityPatchReportParentCycleRecovery(
							"railgun_parent_cycle_prevented",
							visitedCount,
							pendingCount,
							candidate.mParents.Count);
						return true;
					}

					if (visitedCount >= CommunityPatchRailTraversalLimit)
					{
						CommunityPatchReportParentCycleRecovery(
							"railgun_parent_cycle_check_limit_reached",
							visitedCount,
							pendingCount,
							candidate.mParents.Count);
						return true;
					}

					visited[visitedCount++] = current;
					for (int i = 0; i < current.mParents.Count; i++)
					{
						Railgun parent = current.mParents[i];
						if (parent == null)
							continue;

						if (pendingCount >= CommunityPatchRailTraversalLimit)
						{
							CommunityPatchReportParentCycleRecovery(
								"railgun_parent_cycle_check_limit_reached",
								visitedCount,
								pendingCount,
								candidate.mParents.Count);
							return true;
						}
						pending[pendingCount++] = parent;
					}
				}
				return false;
			}
			catch
			{
				CommunityPatchReportParentCycleRecovery(
					"railgun_parent_cycle_check_failed", 0, 0, 0);
				return true;
			}
		}

		// Injected immediately after the existing geometric intersection guards
		// and before Railgun.Update changes mLength or selects the candidate:
		//
		// if (CommunityPatchWouldCreateParentCycle(candidate))
		//     continue;

		private void CommunityPatchLockAllEquivalent()
		{
			if (mCommunityPatchLockAllActive)
				return;
			mCommunityPatchLockAllActive = true;
			mLocked = true;
			for (int i = 0; i < mParents.Count; i++)
				mParents[i].LockAll();
			mCommunityPatchLockAllActive = false;
		}
	}
}
