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
using static UnityEngine.UIElements.UIR.Implementation.UIRStylePainter;

namespace Gnomes {

    // You may be wondering, how does the Example Enemy know it is from class MistEyesAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    class GnomeAI : EnemyAI
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
		float chaseTimer;
		LineRenderer line;
		System.Random enemyRandom;
        bool isDeadAnimationDone;
		public AudioClip gnomeTeleport;
		private float randomnumber;

		enum State {
            SearchingForPlayer,
			PlayerFound,
		}

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start() {
            base.Start();
			LogIfDebugBuild("Example Enemy Spawned");
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

        public override void Update() {
            base.Update();
			//StartCoroutine(DrawPath(line, agent));
            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceNewRandPos += Time.deltaTime;
            var state = currentBehaviourStateIndex;
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
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
		public override void DoAIInterval() {
            
            base.DoAIInterval();
			
			if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };
			switch(currentBehaviourStateIndex) {
				case (int)State.SearchingForPlayer:
					agent.speed = 1f;
					if (FoundClosestPlayerInRange(50f, 50f))
					{
						if (currentSearch != null)
						{
							StopSearch(currentSearch);
						}
						SwitchToBehaviourClientRpc((int)State.PlayerFound);
					}
				break;

				case (int)State.PlayerFound:
					agent.speed = 0f;
					if (FoundClosestPlayerInRange(40f, 40f))
					{
						for (int i = 0; i < GameNetworkManager.Instance.connectedPlayers; i++)
						{
							if (PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]) && !StartOfRound.Instance.allPlayerScripts[i].HasLineOfSightToPosition(cornerDetection.position + Vector3.up * 2f, 100f) && Vector3.Distance(StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position, eye.position) > 0.3f && PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]) && !StartOfRound.Instance.allPlayerScripts[i].HasLineOfSightToPosition(transform.position + Vector3.up * 2f, 100f) && Vector3.Distance(StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position, eye.position) > 0.3f)
							{
								agent.speed = 5f;
								turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
								transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
								SetDestinationToPosition(targetPlayer.transform.position);
							}
							else
							{
								agent.velocity = Vector3.zero;
								agent.speed = 0f;
							}
						}
					}
					break;

				default:
					LogIfDebugBuild("This Behavior State doesn't exist!");
				break;
			}
		}
        bool FoundClosestPlayerInRange(float range, float senseRange) {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if(targetPlayer == null){
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }
        
        bool TargetClosestPlayerInAnyCase() {
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
            if(targetPlayer == null) return false;
            return true;
        }

        public override void OnCollideWithPlayer(Collider other) {
            if (timeSinceHittingLocalPlayer < 1f) {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
			if (playerControllerB != null)
			{
				if (RoundManager.Instance.insideAINodes.Length != 0)
				{
					playerControllerB.DropAllHeldItems();
					Vector3 teleportposition = RoundManager.Instance.insideAINodes[UnityEngine.Random.Range(0, RoundManager.Instance.insideAINodes.Length)].transform.position;
					Vector3 gnometeleportposition = RoundManager.Instance.insideAINodes[UnityEngine.Random.Range(0, RoundManager.Instance.insideAINodes.Length)].transform.position;
					teleportposition = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(teleportposition);
					gnometeleportposition = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(gnometeleportposition);
					timeSinceHittingLocalPlayer = 0f;
					playerControllerB.movementAudio.PlayOneShot(gnomeTeleport);
					playerControllerB.averageVelocity = 0f;
					StartOfRound.Instance.allPlayerScripts[playerControllerB.playerClientId].TeleportPlayer(teleportposition);

					this.serverPosition = gnometeleportposition;
					this.transform.position = gnometeleportposition;
					this.agent.Warp(gnometeleportposition);
					this.SyncPositionToClients();
				}
			}
		}
	}
}