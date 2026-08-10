using System;

namespace RocketFooxball.Editor
{
    [Serializable]
    internal sealed class MovementLabGeneratedState
    {
        public int schemaVersion;
        public string gitSha;
        public string manifestStatus;
        public string sourceSignature;
        public string generatedOutputFingerprint;
        public string unityVersion;
        public string lightingInputDigest;
        public string bakedProfile;
        public string[] staleStages;
        public string[] staleReasons;
        public string[] fingerprintPaths;
        public string[] fingerprintHashes;
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
        public string repositoryInputDigest;
        public string dependencyDigest;
        public string outputDigest;
        public string[] predecessorDigests;
        public string[] staleReasons;
        public string priorInputDigest;
        public string priorOutputDigest;
        public string profile;
        public MovementLabPathDigest[] outputs;
    }

    [Serializable]
    internal sealed class MovementLabPathDigest
    {
        public string path;
        public string digest;
        public bool missing;
    }

    // Typed contract for the lighting-owned manifest. T5's profile writer can
    // extend this shape without reverting to interpolated JSON.
    [Serializable]
    internal sealed class MovementLabLightingManifestState
    {
        public int schemaVersion;
        public string profileId;
        public string profileTag;
        public string sourceSha;
        public string unityVersion;
        public string lightingInputDigest;
        public string[] outputPaths;
        public string[] outputHashes;
    }
}
