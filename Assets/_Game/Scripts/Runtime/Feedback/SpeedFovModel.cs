using UnityEngine;

namespace RocketFooxball.Runtime.Feedback
{
    /// <summary>Pure speed-to-FOV calculation.</summary>
    public static class SpeedFovModel
    {
        public static float Evaluate(float baseFov, float maxFov, float baseSpeed, float hardCap, float horizontalSpeed)
        {
            var speedT = Mathf.InverseLerp(baseSpeed, hardCap, horizontalSpeed);
            return Mathf.Lerp(baseFov, Mathf.Max(baseFov, maxFov), speedT);
        }
    }
}
