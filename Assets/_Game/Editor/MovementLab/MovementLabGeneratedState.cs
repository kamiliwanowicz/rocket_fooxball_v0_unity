using System;

namespace RocketFooxball.Editor
{
    [Serializable]
    internal sealed class MovementLabGeneratedState
    {
        public int schemaVersion;
        public string sourceSignature;
        public string generatedOutputFingerprint;
        public string unityVersion;
        public string[] fingerprintPaths;
        public MovementLabStageRecord[] stages;

        internal MovementLabStageRecord Find(string stageName)
        {
            if (stages == null) return null;
            for (var i = 0; i < stages.Length; i++)
            {
                if (stages[i] != null && string.Equals(stages[i].stage, stageName, StringComparison.Ordinal)) return stages[i];
            }
            return null;
        }
    }

    [Serializable]
    internal sealed class MovementLabStageRecord
    {
        public string stage;
        public int schemaVersion;
        public string contractVersion;
        public string unityVersion;
        public string inputDigest;
        public string outputDigest;
        public string[] predecessorDigests;
        public MovementLabPathDigest[] outputs;
    }

    [Serializable]
    internal sealed class MovementLabPathDigest
    {
        public string path;
        public string digest;
        public bool missing;
    }
}
