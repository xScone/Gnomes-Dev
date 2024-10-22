using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Net;
using GameNetcodeStuff;
using LethalLib.Modules;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using static UnityEngine.UIElements.UIR.Implementation.UIRStylePainter;

namespace AnomalousDucks
{

	// You may be wondering, how does the Example Enemy know it is from class MistEyesAI?
	// Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
	// Asset bundles cannot contain scripts, so our script lives here. It is important to get the
	// reference right, or else it will not find this file. See the guide for more information.

	class FlashbangGnomeAI : EnemyAI
	{
		// We set these in our Asset Bundle, so we can disable warning CS0649:
		// Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
		public Transform turnCompass;
		public Transform attackArea;
#pragma warning restore 0649
		float timeSinceHittingLocalPlayer;
		float timeSinceNewRandPos;
		Vector3 lastKnownPlayerPos;
		bool gotLastPos = false;
		public float stareDistance;
		public Transform cornerDetection;
		LineRenderer line;
		private float currentChaseSpeed = 6f;
		System.Random enemyRandom;
		bool isDeadAnimationDone;
		public AudioClip gnomeTeleport;

		private float chaseTime;
		private bool startChase;
		private bool chaseStarted;

		private float teleportTimer;
		public AudioClip flashbangSound;
		private Vector3 newTeleport;


		private PlayerControllerB previousTarget;

		enum State
		{
			SearchingForPlayer,
			PlayerFound
		}

		[Conditional("DEBUG")]
		void LogIfDebugBuild(string text)
		{
			Plugin.Logger.LogInfo(text);
		}

		public override void Start()
		{
			base.Start();
			LogIfDebugBuild("Blue Gnome Spawned");
			stareDistance = 40f;
			#if DEBUG
			line = gameObject.AddComponent<LineRenderer>();
			line.widthMultiplier = 0.2f; // reduce width of the line
			#endif
			timeSinceHittingLocalPlayer = 0;
			creatureAnimator.SetTrigger("startWalk");
			timeSinceNewRandPos = 0;
			enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
			isDeadAnimationDone = false;

			StartSearch(transform.position);
			currentBehaviourStateIndex = (int)State.SearchingForPlayer;



		}
		// everything working except for the gnome going back into search mode after losing the player.
		public override void Update()
		{
			base.Update();

			if (!chaseStarted)
			{
				teleportTimer += Time.deltaTime;
				if (teleportTimer > 45 && GetClosestPlayer() != null)
				{
					if (RoundManager.Instance.insideAINodes.Length != 0)
					{
						bool gnomeTeleported = false;
						//base.GetComponentInChildren<AudioSource>().PlayOneShot(gnomeTeleport);
						if (!gnomeTeleported)
						{
							gnomeTeleported = true;
							timeSinceHittingLocalPlayer = 0f;
							newTeleport = GenerateTeleportLocation(Vector3.zero, false);
							this.serverPosition = newTeleport;
							this.transform.position = newTeleport;
							this.agent.Warp(newTeleport);
							this.SyncPositionToClients();
							StartSearch(transform.position);
							if (IsOwner && OwnerClientId != 0) 
							{
								ChangeEnemyOwnerServerRpc(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
							}
							teleportTimer = 0f;
							SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
						}
					}

				}
			}
			//StartCoroutine(DrawPath(line, agent));
			timeSinceHittingLocalPlayer += Time.deltaTime;
			timeSinceNewRandPos += Time.deltaTime;
			var state = currentBehaviourStateIndex;
			var flag = false;
			if (chaseTime <= 3)
			{
				agent.speed = 6f;
			}

			if (IsOwner)
			{
				for (int i = 0; i < GameNetworkManager.Instance.maxAllowedPlayers; i++)
				{
					if (PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]) && StartOfRound.Instance.allPlayerScripts[i].HasLineOfSightToPosition(base.transform.position + Vector3.up * 1.6f, 68f)
						&& Vector3.Distance(StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position, eye.position) > 0.3f)
					{
						flag = true;
						chaseTime = 0;
					}
					if (startChase != flag)
					{
						startChase = flag;
					}
				}
			}
			if (chaseStarted)
			{
				if (base.IsOwner)
				{
					chaseTime += Time.deltaTime;
					agent.speed = 8f;
				}
			}
			if (startChase)
			{
				if (!chaseStarted)
				{
					chaseStarted = true;
					if (GameNetworkManager.Instance.localPlayerController.HasLineOfSightToPosition(base.transform.position, 70f, 25))
					{
						chaseTime = 0;
					}
				}
			}
			else
			{
				if (chaseTime >= 6)
				{
					chaseStarted = false;
					if (base.IsOwner)
					{
						StartSearch(transform.position);
						agent.speed = 2f;
					}
				}
				else
				{
					chaseTime += Time.deltaTime;
				}
			}


		}
		public static IEnumerator DrawPath(LineRenderer line, NavMeshAgent agent)
		{
			if (!agent.enabled) yield break;
			yield return new WaitForEndOfFrame();
			line.SetPosition(0, agent.transform.position); //set the line's origin

			line.positionCount = agent.path.corners.Length; //set the array of positions to the amount of corners
			for (var i = 1; i < agent.path.corners.Length; i++)
			{
				line.SetPosition(i, agent.path.corners[i]); //go through each corner and set that to the line renderer's position
			}
		}
		public override void DoAIInterval()
		{

			base.DoAIInterval();

			if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
			{
				return;
			};
			switch (currentBehaviourStateIndex)
			{
				case (int)State.SearchingForPlayer:
					if (!base.IsServer)
					{
						ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
						break;
					}
					for (int i = 0; i < GameNetworkManager.Instance.maxAllowedPlayers; i++)
					{
						if (PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i])
							&& !Physics.Linecast(base.transform.position + Vector3.up * 0.5f, StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position, 
							StartOfRound.Instance.collidersAndRoomMaskAndDefault) && Vector3.Distance(base.transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position) < 30f)
						{
							SwitchToBehaviourServerRpc((int)State.PlayerFound);
							return;
						}
					}
					movingTowardsTargetPlayer = false;
					StartSearch(transform.position);
					break;

				case (int)State.PlayerFound:
					agent.speed = 0;
					if (currentSearch != null)
					{
						StopSearch(currentSearch);
					}
					if (TargetClosestPlayer())
					{
						if (previousTarget != targetPlayer)
						{
							previousTarget = targetPlayer;
							//PreviousTargetSetServerRpc();
							ChangeEnemyOwnerServerRpc(targetPlayer.actualClientId);
						}
						movingTowardsTargetPlayer = true;
					}
					else
					{
						agent.speed = 2f;
						SwitchToBehaviourServerRpc((int)State.SearchingForPlayer);
						ChangeEnemyOwnerServerRpc(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
					}
					break;

				default:
					LogIfDebugBuild("This Behavior State doesn't exist!");
					break;
			}
		}
		bool FoundClosestPlayerInRange(float range, float senseRange)
		{
			TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
			if (targetPlayer == null)
			{
				// Couldn't see a player, so we check if a player is in sensing distance instead
				TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
				range = senseRange;
			}
			return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
		}

		bool TargetClosestPlayerInAnyCase()
		{
			mostOptimalDistance = 2000f;
			targetPlayer = null;
			for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
			{
				tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
				if (tempDist < mostOptimalDistance)
				{
					mostOptimalDistance = tempDist;
					targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
				}
			}
			if (targetPlayer == null) return false;
			return true;
		}

		public override void OnCollideWithPlayer(Collider other)
		{
			PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
			if (playerControllerB != null)
			{
				base.GetComponentInChildren<AudioSource>().PlayOneShot(gnomeTeleport);
				StunExplosion(base.transform.position, affectAudio: true, 1f, 7.5f, 1f);
				if (RoundManager.Instance.insideAINodes.Length != 0)
				{
					bool gnomeTeleported = false;
					if (!gnomeTeleported)
					{
						GetClosestPlayer().GetComponentInChildren<AudioSource>().PlayOneShot(flashbangSound);
						GetClosestPlayer().GetComponentInChildren<AudioSource>().PlayOneShot(gnomeTeleport);
						gnomeTeleported = true;
						timeSinceHittingLocalPlayer = 0f;
						newTeleport = GenerateTeleportLocation(Vector3.zero, false);
						this.serverPosition = newTeleport;
						this.transform.position = newTeleport;
						this.agent.Warp(newTeleport);
						this.SyncPositionToClients();
						StartSearch(transform.position);
						if (IsOwner && OwnerClientId != 0)
						{
							ChangeEnemyOwnerServerRpc(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
						}
						SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
					}
				}
			}

		}
		private Vector3 GenerateTeleportLocation(Vector3 teleportposition, bool isPlayer)
		{
			Vector3 gnometeleportposition = RoundManager.Instance.insideAINodes[UnityEngine.Random.Range(0, RoundManager.Instance.insideAINodes.Length)].transform.position;
			Plugin.Logger.LogInfo("Generating Teleport Location!");
			if (!isPlayer && GetClosestPlayer() != null)
			{
				while (Vector3.Distance(gnometeleportposition, GetClosestPlayer().transform.position) <= 40f)
				{
					gnometeleportposition = RoundManager.Instance.insideAINodes[UnityEngine.Random.Range(0, RoundManager.Instance.insideAINodes.Length)].transform.position;
					if (Vector3.Distance(gnometeleportposition, GetClosestPlayer().transform.position) >= 40f)
					{
						gnometeleportposition = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(gnometeleportposition);
						teleportposition = gnometeleportposition;
						return teleportposition;
					}
				}
			}
			else
			{
				gnometeleportposition = RoundManager.Instance.insideAINodes[UnityEngine.Random.Range(0, RoundManager.Instance.insideAINodes.Length)].transform.position;
				gnometeleportposition = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(gnometeleportposition);
				teleportposition = gnometeleportposition;
				return teleportposition;
			}
			
			return teleportposition;
		}

		private void StunExplosion(Vector3 explosionPosition, bool affectAudio, float flashSeverityMultiplier, float enemyStunTime, float flashSeverityDistanceRolloff = 1f, bool isHeldItem = false, float addToFlashSeverity = 0f)
		{
			PlayerControllerB playerControllerB = GameNetworkManager.Instance.localPlayerController;
			if (GameNetworkManager.Instance.localPlayerController.isPlayerDead && GameNetworkManager.Instance.localPlayerController.spectatedPlayerScript != null)
			{
				playerControllerB = GameNetworkManager.Instance.localPlayerController.spectatedPlayerScript;
			}
			float num = Vector3.Distance(playerControllerB.transform.position, explosionPosition);
			float num2 = 7f / (num * flashSeverityDistanceRolloff);
			if (Physics.Linecast(explosionPosition + Vector3.up * 0.5f, playerControllerB.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
			{
				num2 /= 13f;
			}
			else if (num < 2f)
			{
				num2 = 1f;
			}
			else if (!playerControllerB.HasLineOfSightToPosition(explosionPosition, 60f, 15, 2f))
			{
				num2 = Mathf.Clamp(num2 / 3f, 0f, 1f);
			}
			num2 = Mathf.Clamp(num2 * flashSeverityMultiplier, 0f, 1f);
			if (num2 > 0.6f)
			{
				num2 += addToFlashSeverity;
			}
			HUDManager.Instance.flashFilter = num2;
			if (affectAudio)
			{
				SoundManager.Instance.earsRingingTimer = num2;
			}
			if (enemyStunTime <= 0f)
			{
				return;
			}
			Collider[] array = Physics.OverlapSphere(explosionPosition, 12f, 524288);
			if (array.Length == 0)
			{
				return;
			}
			for (int i = 0; i < array.Length; i++)
			{
				EnemyAICollisionDetect component = array[i].GetComponent<EnemyAICollisionDetect>();
				if (component == null)
				{
					continue;
				}
				Vector3 b = component.mainScript.transform.position + Vector3.up * 0.5f;
				if (component.GetComponentInParent<FlashbangGnomeAI>().DWHasLineOfSightToPosition(explosionPosition + Vector3.up * 0.5f, 120f, 23, 7f) || (!Physics.Linecast(explosionPosition + Vector3.up * 0.5f, component.mainScript.transform.position + Vector3.up * 0.5f, 256) && Vector3.Distance(explosionPosition, b) < 11f))
				{
					component.mainScript.SetEnemyStunned(setToStunned: true, enemyStunTime);
				}
			}
		}
		public bool DWHasLineOfSightToPosition(Vector3 pos, float width = 45f, int range = 30, float proximityAwareness = 7.5f) {
            if (eye == null) {
                _ = transform;
            } else {
                _ = eye;
            }

            if (Vector3.Distance(eye.position, pos) < (float)range && !Physics.Linecast(eye.position, pos, StartOfRound.Instance.collidersAndRoomMaskAndDefault)) {
                Vector3 to = pos - eye.position;
                if (Vector3.Angle(eye.forward, to) < width || Vector3.Distance(transform.position, pos) < proximityAwareness) {
                    return true;
                }
            }
            return false;
        }
	}
}