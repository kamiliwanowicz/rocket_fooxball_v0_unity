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

            // Identity is authoritative for self-impact, even when a projectile has no team attribution.
            if (facts.Self)
            {
                return ParticipantRelationship.Self;
            }

            if (!facts.SourceValid)
            {
                return ParticipantRelationship.Unattributed;
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
        /// <summary>Returns only team values that are valid for gameplay attribution.</summary>
        public static ParticipantTeam? GetValidTeam(ParticipantTeam? team)
        {
            return team.HasValue && (team.Value == ParticipantTeam.Blue || team.Value == ParticipantTeam.Red)
                ? team
                : (ParticipantTeam?)null;
        }

        public static bool IsValidTeam(ParticipantTeam? team)
        {
            return GetValidTeam(team).HasValue;
        }

        public static ParticipantRelationship Classify(ParticipantState target, ParticipantState source)
        {
            return Classify(target, source, source != null ? GetValidTeam(source.Team) : (ParticipantTeam?)null);
        }

        /// <summary>
        /// Classifies a target using owner identity and the firing team's immutable snapshot.
        /// A missing/invalid snapshot is intentionally unattributed, except for the owner itself.
        /// </summary>
        public static ParticipantRelationship Classify(
            ParticipantState target,
            ParticipantState source,
            ParticipantTeam? sourceTeamSnapshot)
        {
            return ParticipantRelationshipPolicy.Classify(GetFacts(target, source, sourceTeamSnapshot));
        }

        public static ParticipantRelationshipFacts GetFacts(ParticipantState target, ParticipantState source)
        {
            return GetFacts(target, source, source != null ? GetValidTeam(source.Team) : (ParticipantTeam?)null);
        }

        public static ParticipantRelationshipFacts GetFacts(
            ParticipantState target,
            ParticipantState source,
            ParticipantTeam? sourceTeamSnapshot)
        {
            if (target == null)
            {
                return ParticipantRelationshipFacts.Invalid;
            }

            var self = source != null && target == source;
            var validSnapshot = IsValidTeam(sourceTeamSnapshot);
            var validTargetTeam = IsValidTeam(target.Team);
            var attributed = source != null && validSnapshot && validTargetTeam;
            return new ParticipantRelationshipFacts(
                targetValid: true,
                targetAlive: target.IsAlive,
                targetImmune: target.IsImmune,
                sourceValid: attributed || self,
                self: self,
                sameTeam: attributed && target.Team == sourceTeamSnapshot.Value);
        }
    }
}
