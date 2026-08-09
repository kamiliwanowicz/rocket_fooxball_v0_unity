using UnityEngine;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Movement;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Fixed-capacity, closest-surface target selection for one blast.</summary>
    public sealed class ExplosionTargetCollector
    {
        private readonly PlayerMotor[] playerTargets;
        private readonly Collider[] playerColliders;
        private readonly float[] playerDistances;
        private readonly BallMotor[] ballTargets;
        private readonly Collider[] ballColliders;
        private readonly float[] ballDistances;

        public ExplosionTargetCollector(int playerCapacity, int ballCapacity)
        {
            playerTargets = new PlayerMotor[playerCapacity];
            playerColliders = new Collider[playerCapacity];
            playerDistances = new float[playerCapacity];
            ballTargets = new BallMotor[ballCapacity];
            ballColliders = new Collider[ballCapacity];
            ballDistances = new float[ballCapacity];
        }

        private int playerCount;
        private int ballCount;

        public int PlayerCount => playerCount;
        public int BallCount => ballCount;

        public void Clear()
        {
            playerCount = 0;
            ballCount = 0;
        }

        public void AddPlayer(PlayerMotor target, Collider collider, Vector3 origin, bool directImpact = false)
        {
            Add(target, collider, origin, directImpact, playerTargets, playerColliders, playerDistances, ref playerCount);
        }

        public void AddBall(BallMotor target, Collider collider, Vector3 origin, bool directImpact = false)
        {
            Add(target, collider, origin, directImpact, ballTargets, ballColliders, ballDistances, ref ballCount);
        }

        public PlayerMotor GetPlayer(int index) => playerTargets[index];
        public BallMotor GetBall(int index) => ballTargets[index];
        public Collider GetPlayerCollider(int index) => playerColliders[index];
        public Collider GetBallCollider(int index) => ballColliders[index];
        public float GetPlayerDistance(int index) => playerDistances[index];
        public float GetBallDistance(int index) => ballDistances[index];

        private static void Add<T>(T target, Collider collider, Vector3 origin, bool directImpact, T[] targets, Collider[] colliders, float[] distances, ref int count)
            where T : Component
        {
            if (target == null || collider == null)
            {
                return;
            }

            var distance = directImpact ? 0f : Vector3.Distance(origin, collider.ClosestPoint(origin));
            for (var i = 0; i < count; i++)
            {
                if (targets[i] != target)
                {
                    continue;
                }

                if (distance < distances[i])
                {
                    distances[i] = distance;
                    colliders[i] = collider;
                }
                return;
            }

            if (count >= targets.Length)
            {
                return;
            }

            targets[count] = target;
            colliders[count] = collider;
            distances[count] = distance;
            count++;
        }
    }
}
