using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Gnomes.src.Items
{
	internal class GnomeItem : GrabbableObject
	{
		public AudioClip gnomeTeleport;
		public AudioSource gnomeAudio;
		private PlayerControllerB localPlayer;

		private float randomchance;
		private int isrealgnome;
		private float giggletimer;
		private float randomgiggletime;
		private bool giggletimemade;

		private bool teleporttimertriggered;
		private float teleporttimer;
		private ulong playerID;

		public override void Start()
		{
			base.Start();
			isrealgnome = UnityEngine.Random.Range(0, 1);
			if (isrealgnome == 0)
			{
				this.SetScrapValue(scrapValue / 2);
			}
			
		}

		public override void Update()
		{
			base.Update();
			if (teleporttimertriggered)
			{
				teleporttimer += Time.deltaTime;
			}
			
			giggletimer += Time.deltaTime;

			if (!giggletimemade)
			{
				randomgiggletime = UnityEngine.Random.Range(15, 45);
			}


			if (giggletimer >= randomgiggletime)
			{
				gnomeAudio.PlayOneShot(gnomeTeleport);
				giggletimer = 0;
				giggletimemade = false;
			}
			if (teleporttimer >= 5)
			{
				RollChanceToTeleport();
				teleporttimer = 0;
				teleporttimertriggered = false;
			}
			if (isHeld)
			{
				playerID = playerHeldBy.actualClientId;
				localPlayer = playerHeldBy;
			}
		}

		public override void GrabItem()
		{
			base.GrabItem();

			if (StartOfRound.Instance.allPlayerScripts[playerID].isInsideFactory)
			{
				teleporttimertriggered = true;
			}
		}
		public override void DiscardItem()
		{
			base.DiscardItem();
			if (teleporttimertriggered)
			{
				teleporttimertriggered = false;
			}
		}
		private void RollChanceToTeleport()
		{
			randomchance = UnityEngine.Random.Range(0, 100);
			if (randomchance > 75 && localPlayer != null)
			{
				StartOfRound.Instance.allPlayerScripts[playerID].DropAllHeldItems();
				StartOfRound.Instance.allPlayerScripts[playerID].movementAudio.PlayOneShot(gnomeTeleport);
				
				Vector3 teleportposition = RoundManager.Instance.insideAINodes[UnityEngine.Random.Range(0, RoundManager.Instance.insideAINodes.Length)].transform.position;
				if (teleportposition != null)
				{
					teleportposition = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(teleportposition);
					StartOfRound.Instance.allPlayerScripts[playerID].isInElevator = false;
					StartOfRound.Instance.allPlayerScripts[playerID].isInHangarShipRoom = false;
					StartOfRound.Instance.allPlayerScripts[playerID].isInsideFactory = true;
					StartOfRound.Instance.allPlayerScripts[playerID].averageVelocity = 0f;
					StartOfRound.Instance.allPlayerScripts[StartOfRound.Instance.allPlayerScripts[playerID].playerClientId].TeleportPlayer(teleportposition);
				}
			}
		}
	}
}
