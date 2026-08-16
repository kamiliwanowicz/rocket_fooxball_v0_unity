using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;

namespace RocketFooxball.Runtime.Hud
{
    public enum MatchHudScreen
    {
        Live,
        MatchTable,
        LocalDeath,
        Go,
        Resetting,
        KickoffCountdown,
        OpeningRulesCountdown,
        GoalSummary,
        Final,
        Setup,
        Paused
    }

    /// <summary>Pure presentation priority. Match and participant owners remain mutable-state owners.</summary>
    public static class MatchHudScreenPolicy
    {
        public static MatchHudScreen Resolve(
            MatchController.MatchState state,
            bool localAlive,
            bool matchTableHeld,
            bool goVisible)
        {
            if (state == MatchController.MatchState.Final)
            {
                return MatchHudScreen.Final;
            }
            if (state == MatchController.MatchState.Setup)
            {
                return MatchHudScreen.Setup;
            }
            if (state == MatchController.MatchState.Paused)
            {
                return MatchHudScreen.Paused;
            }
            if (state == MatchController.MatchState.GoalFreeze)
            {
                return MatchHudScreen.GoalSummary;
            }
            if (state == MatchController.MatchState.OpeningCountdown)
            {
                return MatchHudScreen.OpeningRulesCountdown;
            }
            if (state == MatchController.MatchState.KickoffCountdown)
            {
                return MatchHudScreen.KickoffCountdown;
            }
            if (state == MatchController.MatchState.Reset)
            {
                return MatchHudScreen.Resetting;
            }
            if (goVisible)
            {
                return MatchHudScreen.Go;
            }
            if (!localAlive)
            {
                return MatchHudScreen.LocalDeath;
            }
            if (matchTableHeld)
            {
                return MatchHudScreen.MatchTable;
            }
            return MatchHudScreen.Live;
        }
    }

    [DisallowMultipleComponent]
    [MovedFrom("RocketFooxball")]
    public sealed class MatchHud : MonoBehaviour
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const float GoDuration = 0.5f;
        private const int TableRowCount = 6;

        private const string BlueMarker = "[O]";
        private const string RedMarker = @"[/\]";

        [Header("Presentation Owners")]
        [SerializeField] private MatchController match;
        [SerializeField] private ParticipantState localParticipant;
        [SerializeField] private PlayerInputReader input;

        private readonly MatchParticipantStats[] statStorage = new MatchParticipantStats[TableRowCount];
        private bool compositionValid;
        private bool frameValid;
        private bool hasCachedDeath;
        private string cachedDeathKillerName = string.Empty;
        private string cachedDeathWeaponName = string.Empty;
        private bool previousStateInitialized;
        private MatchController.MatchState previousState;
        private float goRemaining;
        private bool finalCursorOverride;
        private CursorLockMode previousCursorLockState;
        private bool previousCursorVisible;
        private bool cursorEdgeInitialized;
        private bool previousCursorCaptured;

        private MatchController.MatchState frameState;
        private float frameMatchTimeRemaining;
        private float framePhaseRemaining;
        private int frameCountdownNumber;
        private int frameBlueGoals;
        private int frameRedGoals;
        private int frameBlueTeamFrags;
        private int frameRedTeamFrags;
        private float frameHealth;
        private float frameMaxHealth;
        private float frameRespawnRemaining;
        private float frameImmunityRemaining;
        private int frameLocalSlotId;
        private bool frameLocalAlive;
        private bool frameLocalImmune;
        private bool frameTableHeld;
        private bool frameGoVisible;
        private bool frameHasGoalSummary;
        private MatchGoalSummary frameGoalSummary;
        private MatchOutcome frameOutcome;
        private MatchDecisionRule frameDecisionRule;
        private BotDifficulty frameSelectedEnemyDifficulty;
        private BotDifficulty frameLockedEnemyDifficulty;
        private bool frameDifficultyLocked;

        private GUIStyle panelStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;
        private GUIStyle headingStyle;
        private GUIStyle titleStyle;
        private GUIStyle bigStyle;
        private GUIStyle tableHeaderStyle;
        private GUIStyle buttonStyle;
        private GUIStyle healthFillStyle;

        private void Awake()
        {
            compositionValid = match != null && localParticipant != null && input != null;
            if (!compositionValid)
            {
                Debug.LogError("MatchHud requires serialized references: match, localParticipant, and input.", this);
                enabled = false;
                return;
            }

            previousState = match.State;
            previousStateInitialized = true;
        }

        private void OnEnable()
        {
            if (!compositionValid || localParticipant == null)
            {
                return;
            }

            localParticipant.Died += OnLocalParticipantDied;
            cursorEdgeInitialized = input != null;
            previousCursorCaptured = input != null && input.CursorCaptured;
        }

        private void OnDisable()
        {
            if (localParticipant != null)
            {
                localParticipant.Died -= OnLocalParticipantDied;
            }

            RestoreFinalCursorOverride();
            cursorEdgeInitialized = false;
            previousCursorCaptured = false;
        }

        private void Update()
        {
            if (!compositionValid)
            {
                return;
            }

            var state = match.State;
            if (!previousStateInitialized)
            {
                previousState = state;
                previousStateInitialized = true;
            }
            else if (state == MatchController.MatchState.Playing &&
                     (previousState == MatchController.MatchState.OpeningCountdown ||
                      previousState == MatchController.MatchState.KickoffCountdown))
            {
                goRemaining = GoDuration;
            }

            var cursorCaptured = input.CursorCaptured;
            if (!cursorEdgeInitialized)
            {
                previousCursorCaptured = cursorCaptured;
                cursorEdgeInitialized = true;
            }
            else
            {
                var cursorReleased = previousCursorCaptured && !cursorCaptured;
                previousCursorCaptured = cursorCaptured;
                if (state == MatchController.MatchState.Playing && cursorReleased)
                {
                    match.TryPauseMatch();
                    state = match.State;
                }
            }

            previousState = state;
            UpdateFinalCursorOverride(
                state == MatchController.MatchState.Setup ||
                state == MatchController.MatchState.Paused ||
                state == MatchController.MatchState.Final);

            if (localParticipant.IsAlive)
            {
                ClearCachedDeath();
            }

            CaptureFrameSnapshot(state);

            if (goRemaining > 0f)
            {
                goRemaining = Mathf.Max(goRemaining - Time.unscaledDeltaTime, 0f);
            }
        }

        private void CaptureFrameSnapshot(MatchController.MatchState state)
        {
            frameState = state;
            frameMatchTimeRemaining = match.MatchTimeRemaining;
            framePhaseRemaining = match.PhaseRemaining;
            frameCountdownNumber = match.CountdownNumber;
            frameBlueGoals = match.BlueGoals;
            frameRedGoals = match.RedGoals;
            frameBlueTeamFrags = match.BlueTeamFrags;
            frameRedTeamFrags = match.RedTeamFrags;
            frameHealth = localParticipant.Health;
            frameMaxHealth = localParticipant.MaxHealth;
            frameRespawnRemaining = localParticipant.RespawnRemaining;
            frameImmunityRemaining = localParticipant.ImmunityRemaining;
            frameLocalSlotId = localParticipant.SlotId;
            frameLocalAlive = localParticipant.IsAlive;
            frameLocalImmune = localParticipant.IsImmune;
            frameTableHeld = input.MatchTableHeld;
            frameGoVisible = goRemaining > 0f;
            frameHasGoalSummary = match.HasLastGoalSummary;
            frameGoalSummary = match.LastGoalSummary;
            frameOutcome = match.Outcome;
            frameDecisionRule = match.DecisionRule;
            frameSelectedEnemyDifficulty = match.SelectedEnemyDifficulty;
            frameLockedEnemyDifficulty = match.LockedEnemyDifficulty;
            frameDifficultyLocked = match.DifficultyLocked;

            var sourceStats = match.ParticipantStats;
            for (var i = 0; i < TableRowCount; i++)
            {
                statStorage[i] = sourceStats != null && i < sourceStats.Count
                    ? sourceStats[i]
                    : default(MatchParticipantStats);
            }

            frameValid = true;
        }

        private void OnLocalParticipantDied(ParticipantDeathEvent death)
        {
            if (death.Victim != localParticipant)
            {
                return;
            }

            cachedDeathKillerName = death.Killer != null && !string.IsNullOrEmpty(death.Killer.DisplayName)
                ? death.Killer.DisplayName
                : death.Cause == ParticipantDeathCause.Self ? "SELF" : "ARENA";
            cachedDeathWeaponName = FormatDeathWeapon(death);
            hasCachedDeath = true;
        }

        private void ClearCachedDeath()
        {
            hasCachedDeath = false;
            cachedDeathKillerName = string.Empty;
            cachedDeathWeaponName = string.Empty;
        }

        private void OnGUI()
        {
            if (!frameValid)
            {
                return;
            }

            EnsureStyles();
            var previousMatrix = GUI.matrix;
            ApplyReferenceCanvas();

            var screen = MatchHudScreenPolicy.Resolve(frameState, frameLocalAlive, frameTableHeld, frameGoVisible);
            switch (screen)
            {
                case MatchHudScreen.Live:
                    DrawLive();
                    break;
                case MatchHudScreen.MatchTable:
                    DrawLive();
                    DrawTable(new Rect(560f, 130f, 1320f, 740f), true);
                    DrawTableHint();
                    break;
                case MatchHudScreen.LocalDeath:
                    DrawDeath();
                    break;
                case MatchHudScreen.Go:
                    DrawLive();
                    DrawGo();
                    break;
                case MatchHudScreen.Resetting:
                    DrawStateBanner("RESETTING...", "Preparing kickoff");
                    break;
                case MatchHudScreen.KickoffCountdown:
                    DrawCountdown("KICKOFF", "Get ready", false);
                    break;
                case MatchHudScreen.OpeningRulesCountdown:
                    DrawCountdown("MATCH START", "MOST GOALS WINS", true);
                    break;
                case MatchHudScreen.Setup:
                    DrawSetup();
                    break;
                case MatchHudScreen.Paused:
                    DrawPaused();
                    break;
                case MatchHudScreen.GoalSummary:
                    DrawGoalSummary();
                    break;
                case MatchHudScreen.Final:
                    DrawFinal();
                    break;
            }

            GUI.matrix = previousMatrix;
        }

        private void DrawLive()
        {
            DrawPanel(new Rect(36f, 30f, 610f, 106f));
            DrawText(new Rect(58f, 45f, 250f, 38f), FormatClock(frameMatchTimeRemaining), titleStyle, Color.white);
            DrawTeamScore(new Rect(300f, 46f, 160f, 34f), ParticipantTeam.Blue, frameBlueGoals);
            DrawTeamScore(new Rect(465f, 46f, 160f, 34f), ParticipantTeam.Red, frameRedGoals);
            DrawHealth();

            if (frameLocalImmune)
            {
                DrawText(
                    new Rect(1500f, 46f, 360f, 45f),
                    "IMMUNE " + FormatCeilSeconds(frameImmunityRemaining),
                    headingStyle,
                    new Color(0.35f, 0.95f, 1f));
            }
        }

        private void DrawHealth()
        {
            DrawPanel(new Rect(36f, 930f, 530f, 112f));
            var health = Mathf.Max(0f, frameHealth);
            var maxHealth = Mathf.Max(frameMaxHealth, 1f);
            var ratio = Mathf.Clamp01(health / maxHealth);
            DrawText(
                new Rect(58f, 944f, 470f, 38f),
                "HEALTH  " + Mathf.CeilToInt(health) + " / " + Mathf.CeilToInt(maxHealth),
                headingStyle,
                Color.white);
            var barRect = new Rect(58f, 992f, 470f, 25f);
            GUI.Box(barRect, GUIContent.none, panelStyle);
            if (ratio > 0f)
            {
                GUI.Box(new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height), GUIContent.none, healthFillStyle);
            }
        }

        private void DrawTeamScore(Rect rect, ParticipantTeam team, int goals)
        {
            var color = TeamColor(team);
            var marker = TeamMarker(team);
            DrawText(rect, marker + " " + (team == ParticipantTeam.Blue ? "BLUE" : "RED") + " " + goals, headingStyle, color);
        }

        private void DrawTableHint()
        {
            DrawText(new Rect(570f, 886f, 900f, 42f), "RELEASE TAB  TO RETURN", smallStyle, new Color(0.75f, 0.8f, 0.86f));
        }

        private void DrawTable(Rect rect, bool drawPanel)
        {
            if (drawPanel)
            {
                DrawPanel(rect);
            }

            var x = rect.x + 30f;
            var y = rect.y + 22f;
            var width = rect.width - 60f;
            DrawText(new Rect(x, y, width, 38f), "PLAYER                         GOALS       FRAGS       DEATHS", tableHeaderStyle, Color.white);
            y += 52f;
            for (var i = 0; i < TableRowCount; i++)
            {
                var stats = statStorage[i];
                var playerName = string.IsNullOrEmpty(stats.DisplayName) ? "—" : stats.DisplayName;
                if (IsLocalStats(stats))
                {
                    playerName = "YOU  " + playerName;
                }

                var rowText = TeamMarker(stats.Team) + " " + playerName.PadRight(31) +
                              stats.Goals.ToString().PadLeft(7) +
                              stats.Frags.ToString().PadLeft(13) +
                              stats.Deaths.ToString().PadLeft(14);
                DrawText(new Rect(x, y, width, 42f), rowText, labelStyle, TeamColor(stats.Team));
                y += 52f;
            }
        }

        private bool IsLocalStats(MatchParticipantStats stats)
        {
            return stats.SlotId == frameLocalSlotId &&
                   (!string.IsNullOrEmpty(stats.DisplayName) || stats.SlotId == 0);
        }

        private void DrawCountdown(string title, string subtitle, bool includeRules)
        {
            DrawPanel(new Rect(390f, 190f, 1140f, includeRules ? 700f : 560f));
            DrawText(new Rect(450f, 250f, 1020f, 72f), title, titleStyle, Color.white);
            DrawText(new Rect(450f, 335f, 1020f, 50f), subtitle, headingStyle, new Color(0.7f, 0.82f, 0.95f));
            DrawText(new Rect(450f, 410f, 1020f, 190f), frameCountdownNumber.ToString(), bigStyle, Color.white);

            if (includeRules)
            {
                DrawText(new Rect(470f, 620f, 980f, 42f), "MOST GOALS WINS", headingStyle, new Color(0.35f, 0.75f, 1f));
                DrawText(new Rect(470f, 670f, 980f, 42f), "GOALS TIED — DECIDED BY TEAM FRAGS", headingStyle, new Color(1f, 0.35f, 0.35f));
                DrawText(new Rect(470f, 720f, 980f, 42f), "EQUAL GOALS AND TEAM FRAGS — DRAW", smallStyle, Color.white);
            }
        }

        private void DrawSetup()
        {
            DrawPanel(new Rect(390f, 170f, 1140f, 740f));
            DrawText(new Rect(450f, 225f, 1020f, 72f), "MATCH SETUP", titleStyle, Color.white);
            DrawText(new Rect(450f, 320f, 1020f, 42f), "ENEMY BOT DIFFICULTY", headingStyle, new Color(0.7f, 0.82f, 0.95f));

            if (GUI.Button(new Rect(500f, 405f, 280f, 78f), "LOW", buttonStyle))
            {
                match.TrySelectEnemyDifficulty(BotDifficulty.Low);
            }
            if (GUI.Button(new Rect(820f, 405f, 280f, 78f), "MEDIUM", buttonStyle))
            {
                match.TrySelectEnemyDifficulty(BotDifficulty.Medium);
            }
            if (GUI.Button(new Rect(1140f, 405f, 280f, 78f), "HIGH", buttonStyle))
            {
                match.TrySelectEnemyDifficulty(BotDifficulty.High);
            }

            DrawText(
                new Rect(450f, 535f, 1020f, 52f),
                "SELECTED  " + DifficultyName(frameSelectedEnemyDifficulty),
                headingStyle,
                TeamColor(ParticipantTeam.Red));

            if (GUI.Button(new Rect(760f, 680f, 400f, 88f), "START", buttonStyle) && match.TryStartConfiguredMatch())
            {
                RestoreFinalCursorOverrideForGameplay();
            }
        }

        private void DrawPaused()
        {
            DrawPanel(new Rect(510f, 250f, 900f, 580f));
            DrawText(new Rect(580f, 325f, 760f, 72f), "PAUSED", titleStyle, Color.white);
            DrawText(new Rect(580f, 445f, 760f, 48f), "ENEMY BOT DIFFICULTY", headingStyle, new Color(0.7f, 0.82f, 0.95f));
            DrawText(
                new Rect(580f, 515f, 760f, 52f),
                "LOCKED  " + DifficultyName(frameDifficultyLocked ? frameLockedEnemyDifficulty : BotDifficulty.Medium),
                headingStyle,
                TeamColor(ParticipantTeam.Red));

            if (GUI.Button(new Rect(760f, 665f, 400f, 88f), "RESUME", buttonStyle) && match.TryResumeMatch())
            {
                RestoreFinalCursorOverrideForGameplay();
            }
        }

        private void DrawStateBanner(string title, string subtitle)
        {
            DrawPanel(new Rect(560f, 340f, 800f, 320f));
            DrawText(new Rect(600f, 415f, 720f, 72f), title, titleStyle, Color.white);
            DrawText(new Rect(600f, 515f, 720f, 48f), subtitle, headingStyle, new Color(0.7f, 0.82f, 0.95f));
        }

        private void DrawGo()
        {
            DrawText(new Rect(620f, 360f, 680f, 170f), "GO!", bigStyle, new Color(0.55f, 1f, 0.55f));
        }

        private void DrawGoalSummary()
        {
            DrawPanel(new Rect(70f, 120f, 610f, 820f));
            var summary = frameGoalSummary;
            var hasSummary = frameHasGoalSummary;
            var team = hasSummary ? summary.ScoringTeam : ParticipantTeam.Blue;
            var teamColor = hasSummary ? TeamColor(team) : Color.white;
            var blueGoals = hasSummary ? summary.BlueGoals : frameBlueGoals;
            var redGoals = hasSummary ? summary.RedGoals : frameRedGoals;
            var blueTeamFrags = hasSummary ? summary.BlueTeamFrags : frameBlueTeamFrags;
            var redTeamFrags = hasSummary ? summary.RedTeamFrags : frameRedTeamFrags;
            DrawText(new Rect(110f, 180f, 520f, 72f), "GOAL!", titleStyle, teamColor);
            DrawText(
                new Rect(110f, 275f, 520f, 50f),
                hasSummary ? TeamMarker(team) + " " + TeamName(team) + " SCORES" : "UNATTRIBUTED",
                headingStyle,
                teamColor);
            DrawText(new Rect(110f, 355f, 520f, 42f), BlueMarker + " BLUE " + blueGoals + "   " + RedMarker + " RED " + redGoals, headingStyle, Color.white);
            DrawText(new Rect(110f, 405f, 520f, 42f), "FRAGS  " + BlueMarker + " BLUE " + blueTeamFrags + "   " + RedMarker + " RED " + redTeamFrags, smallStyle, Color.white);

            var attribution = "UNATTRIBUTED";
            if (hasSummary && summary.IsOwnGoal)
            {
                attribution = "OWN GOAL — " + (string.IsNullOrEmpty(summary.ResponsibleName) ? "UNATTRIBUTED" : summary.ResponsibleName);
            }
            else if (hasSummary && summary.HasScorer && !string.IsNullOrEmpty(summary.ScorerName))
            {
                attribution = "SCORER  " + summary.ScorerName;
            }
            DrawText(new Rect(110f, 490f, 520f, 72f), attribution, headingStyle, Color.white);
            DrawText(new Rect(110f, 580f, 520f, 42f), "NEXT KICKOFF", smallStyle, new Color(0.75f, 0.8f, 0.86f));
            DrawText(new Rect(110f, 625f, 520f, 120f), FormatCeilSeconds(framePhaseRemaining), bigStyle, Color.white);
            DrawTable(new Rect(720f, 120f, 1130f, 820f), true);
        }

        private void DrawDeath()
        {
            DrawPanel(new Rect(70f, 120f, 610f, 820f));
            DrawText(new Rect(110f, 175f, 520f, 72f), "YOU DIED", titleStyle, new Color(1f, 0.35f, 0.35f));
            DrawText(new Rect(110f, 285f, 520f, 42f), "KILLED BY", smallStyle, new Color(0.75f, 0.8f, 0.86f));
            DrawText(new Rect(110f, 330f, 520f, 58f), DeathKillerName(), headingStyle, Color.white);
            DrawText(new Rect(110f, 430f, 520f, 42f), "WEAPON", smallStyle, new Color(0.75f, 0.8f, 0.86f));
            DrawText(new Rect(110f, 475f, 520f, 58f), DeathWeaponName(), headingStyle, Color.white);
            DrawText(new Rect(110f, 585f, 520f, 42f), "RESPAWN IN", smallStyle, new Color(0.75f, 0.8f, 0.86f));
            DrawText(new Rect(110f, 630f, 520f, 100f), FormatCeilSeconds(frameRespawnRemaining), bigStyle, Color.white);
            DrawTable(new Rect(720f, 120f, 1130f, 820f), true);
        }

        private string DeathKillerName()
        {
            return hasCachedDeath && !string.IsNullOrEmpty(cachedDeathKillerName)
                ? cachedDeathKillerName
                : "ARENA";
        }

        private string DeathWeaponName()
        {
            return hasCachedDeath && !string.IsNullOrEmpty(cachedDeathWeaponName)
                ? cachedDeathWeaponName
                : "ARENA";
        }

        private static string FormatDeathWeapon(ParticipantDeathEvent death)
        {
            if (!string.IsNullOrEmpty(death.Weapon))
            {
                return death.Weapon.ToUpperInvariant();
            }

            switch (death.Cause)
            {
                case ParticipantDeathCause.Rocket:
                    return "ROCKET LAUNCHER";
                case ParticipantDeathCause.DashKick:
                    return "DASH KICK";
                case ParticipantDeathCause.Self:
                    return "SELF";
                case ParticipantDeathCause.Arena:
                    return "ARENA";
                default:
                    return death.Cause.ToString().ToUpperInvariant();
            }
        }

        private void DrawFinal()
        {
            DrawPanel(new Rect(60f, 60f, 1800f, 960f));
            DrawText(new Rect(120f, 100f, 1680f, 80f), OutcomeText(), titleStyle, OutcomeColor());
            DrawText(new Rect(120f, 195f, 1680f, 50f), DecisionText(), headingStyle, Color.white);
            DrawText(new Rect(120f, 270f, 1680f, 46f), BlueMarker + " BLUE " + frameBlueGoals + " GOALS   " + frameBlueTeamFrags + " FRAGS", headingStyle, new Color(0.35f, 0.75f, 1f));
            DrawText(new Rect(120f, 320f, 1680f, 46f), RedMarker + " RED " + frameRedGoals + " GOALS   " + frameRedTeamFrags + " FRAGS", headingStyle, new Color(1f, 0.35f, 0.35f));
            DrawTable(new Rect(120f, 395f, 1680f, 465f), true);

            if (GUI.Button(new Rect(1320f, 900f, 250f, 70f), "REMATCH", buttonStyle))
            {
                match.TryStartRematch();
            }
            if (GUI.Button(new Rect(1580f, 900f, 150f, 70f), "EXIT", buttonStyle))
            {
                match.RequestExit();
            }
        }

        private string OutcomeText()
        {
            switch (frameOutcome)
            {
                case MatchOutcome.BlueWin:
                    return "BLUE WINS";
                case MatchOutcome.RedWin:
                    return "RED WINS";
                case MatchOutcome.Draw:
                    return "DRAW";
                default:
                    return "MATCH COMPLETE";
            }
        }

        private string DecisionText()
        {
            switch (frameDecisionRule)
            {
                case MatchDecisionRule.Goals:
                    return "DECIDED BY GOALS";
                case MatchDecisionRule.TeamFrags:
                    return "GOALS TIED — DECIDED BY TEAM FRAGS";
                case MatchDecisionRule.Draw:
                    return "GOALS AND TEAM FRAGS TIED — DRAW";
                default:
                    return string.Empty;
            }
        }

        private Color OutcomeColor()
        {
            switch (frameOutcome)
            {
                case MatchOutcome.BlueWin:
                    return new Color(0.35f, 0.75f, 1f);
                case MatchOutcome.RedWin:
                    return new Color(1f, 0.35f, 0.35f);
                default:
                    return Color.white;
            }
        }

        private void EnsureStyles()
        {
            if (panelStyle != null)
            {
                return;
            }

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft
            };
            panelStyle.normal.background = MakeSolidTexture(new Color(0.025f, 0.04f, 0.08f, 0.9f));
            panelStyle.normal.textColor = Color.white;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleLeft,
                richText = false
            };
            labelStyle.normal.textColor = Color.white;

            smallStyle = new GUIStyle(labelStyle)
            {
                fontSize = 20
            };
            headingStyle = new GUIStyle(labelStyle)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold
            };
            titleStyle = new GUIStyle(labelStyle)
            {
                fontSize = 48,
                fontStyle = FontStyle.Bold
            };
            bigStyle = new GUIStyle(labelStyle)
            {
                fontSize = 112,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            tableHeaderStyle = new GUIStyle(labelStyle)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold
            };
            healthFillStyle = new GUIStyle(GUI.skin.box);
            healthFillStyle.normal.background = MakeSolidTexture(new Color(0.25f, 0.9f, 0.45f, 0.95f));
        }

        private static Texture2D MakeSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void ApplyReferenceCanvas()
        {
            var scale = Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight);
            var offsetX = (Screen.width - ReferenceWidth * scale) * 0.5f;
            var offsetY = (Screen.height - ReferenceHeight * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(
                new Vector3(offsetX, offsetY, 0f),
                Quaternion.identity,
                new Vector3(scale, scale, 1f));
        }

        private void DrawPanel(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, panelStyle);
        }

        private void DrawText(Rect rect, string text, GUIStyle style, Color color)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previousColor;
        }

        private static string FormatClock(float remaining)
        {
            var totalSeconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
            return (totalSeconds / 60).ToString("00") + ":" + (totalSeconds % 60).ToString("00");
        }

        private static string FormatCeilSeconds(float remaining)
        {
            return Mathf.Max(0, Mathf.CeilToInt(remaining)) + "s";
        }

        private static string TeamMarker(ParticipantTeam team)
        {
            return team == ParticipantTeam.Blue ? BlueMarker : RedMarker;
        }

        private static string TeamName(ParticipantTeam team)
        {
            return team == ParticipantTeam.Blue ? "BLUE" : "RED";
        }

        private static string DifficultyName(BotDifficulty difficulty)
        {
            switch (difficulty)
            {
                case BotDifficulty.Low:
                    return "LOW";
                case BotDifficulty.High:
                    return "HIGH";
                default:
                    return "MEDIUM";
            }
        }

        private static Color TeamColor(ParticipantTeam team)
        {
            return team == ParticipantTeam.Blue
                ? new Color(0.35f, 0.75f, 1f)
                : new Color(1f, 0.35f, 0.35f);
        }

        private void UpdateFinalCursorOverride(bool shouldOverride)
        {
            if (shouldOverride)
            {
                if (!finalCursorOverride)
                {
                    previousCursorLockState = Cursor.lockState;
                    previousCursorVisible = Cursor.visible;
                    finalCursorOverride = true;
                }

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            RestoreFinalCursorOverride();
        }

        private void RestoreFinalCursorOverride()
        {
            if (!finalCursorOverride)
            {
                return;
            }

            Cursor.lockState = previousCursorLockState;
            Cursor.visible = previousCursorVisible;
            finalCursorOverride = false;
        }

        private void RestoreFinalCursorOverrideForGameplay()
        {
            RestoreFinalCursorOverride();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
