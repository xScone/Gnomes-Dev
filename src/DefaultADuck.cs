using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using GameNetcodeStuff;
using LethalLib.Modules;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using static UnityEngine.UIElements.UIR.Implementation.UIRStylePainter;


namespace AnomalousDucks.src
{
	class DefaultADuck : EnemyAI
	{
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
		System.Random enemyRandom;
		bool isDeadAnimationDone;
		public AudioClip duckyTeleport;
		private System.Random random;
		private int realSeed;
		private PlayerControllerB chosenPlayer;

		private bool timerStart;
		private float timer;
		private float timerEnd;
		private Vector3 teleportlocation;

		private bool wasOwnerLastFrame;
		private bool ableToRotate;

		private PlayerControllerB previousTarget;

		[Conditional("DEBUG")]
		void LogIfDebugBuild(string text)
		{
			Plugin.Logger.LogInfo(text);
		}

		enum State
		{
			TeleportingToPlayer,
			PlayerFound,
		}

		public override void Start()
		{
			base.Start();
			SyncSeedServerRpc();
			agent.speed = 0f;
			LogIfDebugBuild("Duck Spawned");
			realSeed = StartOfRound.Instance.randomMapSeed;
			random = new System.Random(realSeed);
			timeSinceHittingLocalPlayer = 0;
			creatureAnimator.SetTrigger("startWalk");
			timeSinceNewRandPos = 0;
			enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
			isDeadAnimationDone = false;

			StartSearch(transform.position);
			currentBehaviourStateIndex = (int)State.TeleportingToPlayer;
		}

		public override void Update()
		{
			base.Update();
			if (timerStart)
			{
				timer += Time.deltaTime;
			}
			
			if (timer > timerEnd && IsHost)
			{
				TeleportDuckyServerRpc();
			}
			if (base.IsOwner && chosenPlayer != null)
			{
				if (!wasOwnerLastFrame)
				{
					wasOwnerLastFrame = true;
				}
				bool flag = false;
				for (int i = 0; i < GameNetworkManager.Instance.maxAllowedPlayers; i++)
				{
					if (PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]) && StartOfRound.Instance.allPlayerScripts[i].HasLineOfSightToPosition(base.transform.position)
						&& Vector3.Distance(StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position, eye.position) > 0.3f)
					{
						flag = true;
					}
				}
				if (flag != ableToRotate)
				{
					ableToRotate = flag;
				}
				if (!ableToRotate)
				{
					if (ableToRotate)
					{
						ableToRotate = false;
					}
					if (base.IsOwner)
					{
						this.transform.LookAt(this.GetClosestPlayer().transform.position);
					}

				}
			}
		}

		[ServerRpc]
		private void TeleportDuckyServerRpc()
		{
			if (IsHost)
			{
				SyncSeedServerRpc();
				var randomplayer = (int)random.Next(0, StartOfRound.Instance.allPlayerScripts.Length);
				chosenPlayer = StartOfRound.Instance.allPlayerScripts[randomplayer];
				for (int i = 0; i < RoundManager.Instance.insideAINodes.Length; i++)
				{
					if (Vector3.Distance(RoundManager.Instance.insideAINodes[i].transform.position, chosenPlayer.transform.position) < 10 && chosenPlayer.isInsideFactory)
					{
						teleportlocation = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(chosenPlayer.transform.position);
					}
				}
				TeleportDuckyClientRpc(randomplayer, teleportlocation);
			}
		}
		[ClientRpc]
		private void TeleportDuckyClientRpc(int randomPlayer, Vector3 teleportLoc)
		{
			chosenPlayer = StartOfRound.Instance.allPlayerScripts[randomPlayer];
			Plugin.Logger.LogInfo("Chosen Player:" + chosenPlayer);
			if (chosenPlayer == null) { return; }
			Plugin.Logger.LogInfo(teleportLoc);
			if (chosenPlayer.isInsideFactory && teleportLoc != null)
			{
				base.GetComponentInChildren<AudioSource>().PlayOneShot(duckyTeleport);

				this.serverPosition = teleportLoc;
				this.transform.position = teleportLoc;
				this.agent.Warp(teleportLoc);
				this.SyncPositionToClients();
				Plugin.Logger.LogInfo("Ducky Teleported! Target: " + chosenPlayer);

				TimerServerRpc();
				SwitchToBehaviourServerRpc((int)State.PlayerFound);
			}
			else
			{
				Plugin.Logger.LogInfo($"Could not teleport to player: {chosenPlayer}, are they outside?");
				TimerServerRpc();
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
				case (int)State.TeleportingToPlayer:
					StartSearch(transform.position);
					ChangeEnemyOwnerServerRpc(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
					if (!timerStart)
					{
						TimerServerRpc();
					}
					break;

				case (int)State.PlayerFound:
					timerStart = false;
					StopSearch(currentSearch);
					if (Vector3.Distance(chosenPlayer.transform.position, this.transform.position) > 25 && !timerStart)
					{
						SwitchToBehaviourServerRpc((int)State.TeleportingToPlayer);
					}
					break;

				default:
					LogIfDebugBuild("This Behavior State doesn't exist!");
					break;
			}
		}

		[ServerRpc]
		private void TimerServerRpc()
		{
			SyncSeedServerRpc();
			if (IsHost)
			{
				timerEnd = (float)random.Next(30, 190);
				TimerClientRpc(timerEnd);
			}
		}
		[ClientRpc]
		private void TimerClientRpc(float randomNumber)
		{
			if (!timerStart)
			{
				this.timerEnd = randomNumber;
				timerStart = true;
				timer = 0;
				Plugin.Logger.LogInfo("Timer End: " + timerEnd);
			}
			else
			{
				timerStart = false;
				timer = 0;
			}
		}
		[ServerRpc]
		private void SyncSeedServerRpc()
		{
			if (IsHost) 
			{
				realSeed += 1;
				SyncSeedClientRpc(realSeed);
			}
		}
		[ClientRpc]
		private void SyncSeedClientRpc(int currentseed)
		{
			realSeed = currentseed;
			random = new System.Random(realSeed);
		}

	}
	
}
