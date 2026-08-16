using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Weapons
{
    /// <summary>Relationship between a weapon owner and one participant target.</summary>
    public enum ParticipantRelationship
    {
        Invalid,
        Unattributed,
        Self,
        Friendly,
        Enemy,
        Immune
    }

    /// <summary>Small value-only input for the relationship policy.</summary>
    public readonly struct ParticipantRelationshipFacts
    {
        public ParticipantRelationshipFacts(
            bool targetValid,
            bool targetAlive,
            bool targetImmune,
            bool sourceValid,
            bool self,
            bool sameTeam)
        {
            TargetValid = targetValid;
            TargetAlive = targetAlive;
            TargetImmune = targetImmune;
            SourceValid = sourceValid;
            Self = self;
            SameTeam = sameTeam;
        }

        public bool TargetValid { get; }
        public bool TargetAlive { get; }
        public bool TargetImmune { get; }
        public bool SourceValid { get; }
        public bool Self { get; }
        public bool SameTeam { get; }

        public static ParticipantRelationshipFacts Invalid =>
            new ParticipantRelationshipFacts(false, false, false, false, false, false);
    }

    /// <summary>Pure relationship policy shared by rocket and hitscan weapons.</summary>
    public static class ParticipantRelationshipPolicy
    {
        public static ParticipantRelationship Classify(ParticipantRelationshipFacts facts)
        {
            if (!facts.TargetValid)
            {
                return ParticipantRelationship.Invalid;
            }

            if (!facts.TargetAlive || facts.TargetImmune)
            {
                return ParticipantRelationship.Immune;
            }

            if (!facts.SourceValid)
            {
                return ParticipantRelationship.Unattributed;
            }

            if (facts.Self)
            {
                return ParticipantRelationship.Self;
            }

            return facts.SameTeam ? ParticipantRelationship.Friendly : ParticipantRelationship.Enemy;
        }

        public static bool CanReceiveRocketForce(ParticipantRelationship relationship)
        {
            return relationship == ParticipantRelationship.Unattributed ||
                   relationship == ParticipantRelationship.Self ||
                   relationship == ParticipantRelationship.Enemy;
        }

        public static bool CanReceiveRocketDamage(ParticipantRelationship relationship)
        {
            return relationship == ParticipantRelationship.Enemy;
        }

        public static bool CanReceiveShotgunDamage(ParticipantRelationship relationship)
        {
            return relationship == ParticipantRelationship.Enemy;
        }

        public static float RocketImpulseScale(ParticipantRelationship relationship, float enemyScale)
        {
            if (relationship == ParticipantRelationship.Enemy)
            {
                return enemyScale;
            }

            return CanReceiveRocketForce(relationship) ? 1f : 0f;
        }
    }

    /// <summary>Runtime adapter that turns participant state into pure policy facts.</summary>
    public static class ParticipantRelationshipAdapter
    {
        public static ParticipantRelationship Classify(ParticipantState target, ParticipantState source)
        {
            if (target == null)
            {
                return ParticipantRelationship.Invalid;
            }

            var facts = new ParticipantRelationshipFacts(
                targetValid: true,
                targetAlive: target.IsAlive,
                targetImmune: target.IsImmune,
                sourceValid: source != null,
                self: source != null && target == source,
                sameTeam: source != null && target.Team == source.Team);
            return ParticipantRelationshipPolicy.Classify(facts);
        }

        public static ParticipantRelationshipFacts GetFacts(ParticipantState target, ParticipantState source)
        {
            if (target == null)
            {
                return ParticipantRelationshipFacts.Invalid;
            }

            return new ParticipantRelationshipFacts(
                targetValid: true,
                targetAlive: target.IsAlive,
                targetImmune: target.IsImmune,
                sourceValid: source != null,
                self: source != null && target == source,
                sameTeam: source != null && target.Team == source.Team);
        }
    }
}
