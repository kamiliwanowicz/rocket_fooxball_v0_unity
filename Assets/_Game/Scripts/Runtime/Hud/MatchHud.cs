using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Weapons;

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

        public static bool ShouldDisplayParticipantRow(bool botsEnabled, bool isLocal)
        {
            return botsEnabled || isLocal;
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
        private const float DamageIndicatorCenterX = 960f;
        private const float DamageIndicatorCenterY = 540f;
        private const float DamageIndicatorRadius = 64f;
        private const float DamageIndicatorThickness = 8f;
        private const float DamageIndicatorArcDegrees = 60f;
        private const int DamageIndicatorSegmentCount = 12;

        public static readonly Rect HealthPanelRect = new Rect(36f, 930f, 530f, 112f);
        public static readonly Rect ShotgunPanelRect = new Rect(1354f, 930f, 530f, 112f);
        public static readonly Rect ShotgunIconRect = new Rect(1376f, 938f, 192f, 96f);
        public static readonly Rect ShotgunTitleRect = new Rect(1588f, 946f, 274f, 30f);
        public static readonly Rect ShotgunShellsRect = new Rect(1588f, 988f, 274f, 26f);

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
        private ShotgunWeapon localShotgun;
        private float hitMarkerRemaining;
        private float damageIndicatorRemaining;
        private float damageIndicatorAngle;
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
        private bool frameHasShotgun;
        private int frameShotgunShells;
        private bool frameHitMarkerVisible;
        private float frameDamageIndicatorRemaining;
        private float frameDamageIndicatorAngle;
        private bool frameHasGoalSummary;
        private MatchGoalSummary frameGoalSummary;
        private MatchOutcome frameOutcome;
        private MatchDecisionRule frameDecisionRule;
        private BotDifficulty frameSelectedEnemyDifficulty;
        private BotDifficulty frameLockedEnemyDifficulty;
        private bool frameDifficultyLocked;
        private bool frameSelectedBotsEnabled;
        private bool frameLockedBotsEnabled;
        private bool frameBotsEnabled;
        private bool frameConfigurationLocked;

        private GUIStyle panelStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;
        private GUIStyle headingStyle;
        private GUIStyle titleStyle;
        private GUIStyle bigStyle;
        private GUIStyle tableHeaderStyle;
        private GUIStyle tableHeaderNumberStyle;
        private GUIStyle tableNumberStyle;
        private GUIStyle buttonStyle;
        private GUIStyle healthFillStyle;
        private GUIStyle scoreStyle;
        private Texture2D whiteTexture;

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
            localShotgun = localParticipant.Shotgun;
        }

        private void OnEnable()
        {
            if (!compositionValid || localParticipant == null)
            {
                return;
            }

            localShotgun = localParticipant.Shotgun;
            localParticipant.Died += OnLocalParticipantDied;
            localParticipant.Damaged += OnLocalParticipantDamaged;
            if (localShotgun != null)
            {
                localShotgun.HitConfirmed += OnShotgunHitConfirmed;
            }
            cursorEdgeInitialized = input != null;
            previousCursorCaptured = input != null && input.CursorCaptured;
        }

        private void OnDisable()
        {
            if (localParticipant != null)
            {
                localParticipant.Died -= OnLocalParticipantDied;
                localParticipant.Damaged -= OnLocalParticipantDamaged;
            }
            if (localShotgun != null)
            {
                localShotgun.HitConfirmed -= OnShotgunHitConfirmed;
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

            if (hitMarkerRemaining > 0f)
            {
                hitMarkerRemaining = Mathf.Max(hitMarkerRemaining - Time.unscaledDeltaTime, 0f);
            }

            if (damageIndicatorRemaining > 0f)
            {
                damageIndicatorRemaining = Mathf.Max(
                    damageIndicatorRemaining - Time.unscaledDeltaTime,
                    0f);
            }

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
            frameHasShotgun = localParticipant.HasShotgun;
            frameShotgunShells = localParticipant.ShotgunShells;
            frameHitMarkerVisible = hitMarkerRemaining > 0f;
            frameDamageIndicatorRemaining = damageIndicatorRemaining;
            frameDamageIndicatorAngle = damageIndicatorAngle;
            frameHasGoalSummary = match.HasLastGoalSummary;
            frameGoalSummary = match.LastGoalSummary;
            frameOutcome = match.Outcome;
            frameDecisionRule = match.DecisionRule;
            frameSelectedEnemyDifficulty = match.SelectedEnemyDifficulty;
            frameLockedEnemyDifficulty = match.LockedEnemyDifficulty;
            frameDifficultyLocked = match.DifficultyLocked;
            frameSelectedBotsEnabled = match.SelectedBotsEnabled;
            frameLockedBotsEnabled = match.LockedBotsEnabled;
            frameBotsEnabled = match.BotsEnabled;
            frameConfigurationLocked = match.ConfigurationLocked;

            var sourceStats = match.ParticipantStats;
            for (var i = 0; i < TableRowCount; i++)
            {
                statStorage[i] = sourceStats != null && i < sourceStats.Count
                    ? sourceStats[i]
                    : default(MatchParticipantStats);
            }

            frameValid = true;
        }

        private void OnShotgunHitConfirmed()
        {
            hitMarkerRemaining = 0.18f;
        }

        private void OnLocalParticipantDamaged(ParticipantDamageEvent damage)
        {
            if (damage.Victim != localParticipant || localParticipant == null)
            {
                return;
            }

            var cameraFeedback = localParticipant.CameraFeedback;
            var camera = cameraFeedback != null ? cameraFeedback.TargetCamera : null;
            var cameraTransform = camera != null ? camera.transform : localParticipant.transform;
            var victimToSource = damage.SourceWorldPosition - damage.Victim.transform.position;
            if (!DamageIndicatorRules.TryResolveAngle(
                    victimToSource,
                    cameraTransform.right,
                    cameraTransform.forward,
                    out var angleDegrees))
            {
                return;
            }

            damageIndicatorAngle = angleDegrees;
            damageIndicatorRemaining = DamageIndicatorRules.VisibleDuration;
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

            if (frameHitMarkerVisible && frameLocalAlive &&
                (screen == MatchHudScreen.Live ||
                 screen == MatchHudScreen.MatchTable ||
                 screen == MatchHudScreen.Go))
            {
                DrawHitMarker();
            }

            if (frameDamageIndicatorRemaining > 0f && frameLocalAlive &&
                (screen == MatchHudScreen.Live ||
                 screen == MatchHudScreen.MatchTable ||
                 screen == MatchHudScreen.Go))
            {
                DrawDamageIndicator();
            }

            GUI.matrix = previousMatrix;
        }

        private void DrawLive()
        {
            DrawText(new Rect(848f, 24f, 96f, 64f), frameBlueGoals.ToString(), scoreStyle, TeamColor(ParticipantTeam.Blue));
            DrawText(new Rect(944f, 24f, 32f, 64f), ":", scoreStyle, Color.white);
            DrawText(new Rect(976f, 24f, 96f, 64f), frameRedGoals.ToString(), scoreStyle, TeamColor(ParticipantTeam.Red));
            DrawHealth();
            DrawShotgunWidget();

            if (frameLocalImmune)
            {
                DrawText(
                    new Rect(1500f, 46f, 360f, 45f),
                    "IMMUNE " + FormatCeilSeconds(frameImmunityRemaining),
                    headingStyle,
                    new Color(0.35f, 0.95f, 1f));
            }
        }

        private void DrawShotgunWidget()
        {
            var shells = Mathf.Max(frameShotgunShells, 0);
            if (!frameHasShotgun && shells == 0)
            {
                return;
            }

            var color = frameHasShotgun && shells > 0
                ? Color.white
                : new Color(0.55f, 0.6f, 0.68f);
            DrawPanel(ShotgunPanelRect);
            DrawShotgunSilhouette(ShotgunIconRect, color);
            DrawText(ShotgunTitleRect, "SHOTGUN", headingStyle, color);
            DrawText(ShotgunShellsRect, "SHELLS  " + shells, smallStyle, color);
        }

        private void DrawShotgunSilhouette(Rect rect, Color color)
        {
            if (whiteTexture == null)
            {
                whiteTexture = MakeSolidTexture(Color.white);
            }

            var previousColor = GUI.color;
            var previousMatrix = GUI.matrix;
            try
            {
                GUI.color = color;
                var stock = new Rect(
                    rect.x + rect.width * 0f,
                    rect.y + rect.height * 0.421875f,
                    rect.width * 0.21875f,
                    rect.height * 0.1875f);
                var body = new Rect(
                    rect.x + rect.width * 0.171875f,
                    rect.y + rect.height * 0.25f,
                    rect.width * 0.3671875f,
                    rect.height * 0.453125f);
                var barrel = new Rect(
                    rect.x + rect.width * 0.5078125f,
                    rect.y + rect.height * 0.328125f,
                    rect.width * 0.4921875f,
                    rect.height * 0.171875f);
                var trigger = new Rect(
                    rect.x + rect.width * 0.3671875f,
                    rect.y + rect.height * 0.65625f,
                    rect.width * 0.0859375f,
                    rect.height * 0.25f);
                GUI.DrawTexture(stock, whiteTexture);
                GUI.DrawTexture(body, whiteTexture);
                GUI.DrawTexture(barrel, whiteTexture);
                GUI.DrawTexture(trigger, whiteTexture);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
            }
        }

        private void DrawHitMarker()
        {
            DrawText(new Rect(840f, 420f, 240f, 240f), "X", bigStyle, Color.white);
        }

        private void DrawDamageIndicator()
        {
            var opacity = DamageIndicatorRules.EvaluateOpacity(frameDamageIndicatorRemaining);
            if (opacity <= 0f)
            {
                return;
            }

            var previousColor = GUI.color;
            var previousMatrix = GUI.matrix;
            var color = new Color(1f, 0.16f, 0.12f, opacity);
            var segmentDegrees = DamageIndicatorArcDegrees / DamageIndicatorSegmentCount;
            var halfArc = DamageIndicatorArcDegrees * 0.5f;
            var center = new Vector2(DamageIndicatorCenterX, DamageIndicatorCenterY);

            try
            {
                GUI.color = color;
                for (var i = 0; i < DamageIndicatorSegmentCount; i++)
                {
                    var startDegrees = frameDamageIndicatorAngle - halfArc + segmentDegrees * i;
                    var endDegrees = startDegrees + segmentDegrees;
                    var startRadians = startDegrees * Mathf.Deg2Rad;
                    var endRadians = endDegrees * Mathf.Deg2Rad;
                    var start = new Vector2(
                        center.x + Mathf.Sin(startRadians) * DamageIndicatorRadius,
                        center.y - Mathf.Cos(startRadians) * DamageIndicatorRadius);
                    var end = new Vector2(
                        center.x + Mathf.Sin(endRadians) * DamageIndicatorRadius,
                        center.y - Mathf.Cos(endRadians) * DamageIndicatorRadius);
                    var midpoint = (start + end) * 0.5f;
                    var segmentLength = Vector2.Distance(start, end) + 1f;
                    var tangentDegrees = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;

                    GUI.matrix = previousMatrix;
                    GUIUtility.RotateAroundPivot(tangentDegrees, midpoint);
                    GUI.DrawTexture(
                        new Rect(
                            midpoint.x - segmentLength * 0.5f,
                            midpoint.y - DamageIndicatorThickness * 0.5f,
                            segmentLength,
                            DamageIndicatorThickness),
                        whiteTexture);
                }
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
            }
        }

        private void DrawHealth()
        {
            DrawPanel(HealthPanelRect);
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
            var playerWidth = width * 0.58f;
            var numberWidth = (width - playerWidth) / 3f;
            DrawText(new Rect(x, y, playerWidth, 38f), "PLAYER", tableHeaderStyle, Color.white);
            DrawText(new Rect(x + playerWidth, y, numberWidth, 38f), "GOALS", tableHeaderNumberStyle, Color.white);
            DrawText(new Rect(x + playerWidth + numberWidth, y, numberWidth, 38f), "FRAGS", tableHeaderNumberStyle, Color.white);
            DrawText(new Rect(x + playerWidth + numberWidth * 2f, y, numberWidth, 38f), "DEATHS", tableHeaderNumberStyle, Color.white);
            y += 52f;
            for (var i = 0; i < TableRowCount; i++)
            {
                var stats = statStorage[i];
                var isLocal = IsLocalStats(stats);
                if (!MatchHudScreenPolicy.ShouldDisplayParticipantRow(frameBotsEnabled, isLocal))
                {
                    continue;
                }

                var playerName = string.IsNullOrEmpty(stats.DisplayName) ? "—" : stats.DisplayName;
                if (isLocal)
                {
                    playerName = "YOU  " + playerName;
                }

                var color = TeamColor(stats.Team);
                DrawText(new Rect(x, y, playerWidth, 42f), TeamMarker(stats.Team) + " " + playerName, labelStyle, color);
                DrawText(new Rect(x + playerWidth, y, numberWidth, 42f), stats.Goals.ToString(), tableNumberStyle, color);
                DrawText(new Rect(x + playerWidth + numberWidth, y, numberWidth, 42f), stats.Frags.ToString(), tableNumberStyle, color);
                DrawText(new Rect(x + playerWidth + numberWidth * 2f, y, numberWidth, 42f), stats.Deaths.ToString(), tableNumberStyle, color);
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
            DrawPanel(new Rect(390f, 130f, 1140f, 820f));
            DrawText(new Rect(450f, 185f, 1020f, 72f), "MATCH SETUP", titleStyle, Color.white);
            DrawText(new Rect(450f, 285f, 1020f, 42f), "BOTS", headingStyle, new Color(0.7f, 0.82f, 0.95f));

            if (GUI.Button(new Rect(500f, 350f, 420f, 78f), "ON", buttonStyle))
            {
                match.TrySelectBotsEnabled(true);
            }
            if (GUI.Button(new Rect(1000f, 350f, 280f, 78f), "OFF", buttonStyle))
            {
                match.TrySelectBotsEnabled(false);
            }

            DrawText(
                new Rect(450f, 445f, 1020f, 42f),
                "BOTS  " + (frameSelectedBotsEnabled ? "ON" : "OFF"),
                headingStyle,
                frameSelectedBotsEnabled ? TeamColor(ParticipantTeam.Red) : new Color(0.65f, 0.68f, 0.72f));

            if (frameSelectedBotsEnabled)
            {
                DrawText(new Rect(450f, 505f, 1020f, 42f), "ENEMY BOT DIFFICULTY", headingStyle, new Color(0.7f, 0.82f, 0.95f));
                if (GUI.Button(new Rect(500f, 560f, 280f, 78f), "LOW", buttonStyle))
                {
                    match.TrySelectEnemyDifficulty(BotDifficulty.Low);
                }
                if (GUI.Button(new Rect(820f, 560f, 280f, 78f), "MEDIUM", buttonStyle))
                {
                    match.TrySelectEnemyDifficulty(BotDifficulty.Medium);
                }
                if (GUI.Button(new Rect(1140f, 560f, 280f, 78f), "HIGH", buttonStyle))
                {
                    match.TrySelectEnemyDifficulty(BotDifficulty.High);
                }

                DrawText(
                    new Rect(450f, 660f, 1020f, 52f),
                    "SELECTED  " + DifficultyName(frameSelectedEnemyDifficulty),
                    headingStyle,
                    TeamColor(ParticipantTeam.Red));
            }

            if (GUI.Button(new Rect(760f, 775f, 400f, 88f), "START", buttonStyle) && match.TryStartConfiguredMatch())
            {
                RestoreFinalCursorOverrideForGameplay();
            }
        }

        private void DrawPaused()
        {
            DrawPanel(new Rect(510f, 250f, 900f, 580f));
            DrawText(new Rect(580f, 325f, 760f, 72f), "PAUSED", titleStyle, Color.white);
            DrawText(
                new Rect(580f, 445f, 760f, 52f),
                "LOCKED  BOTS " + (frameConfigurationLocked && frameLockedBotsEnabled ? "ON" : "OFF"),
                headingStyle,
                frameLockedBotsEnabled ? TeamColor(ParticipantTeam.Red) : new Color(0.65f, 0.68f, 0.72f));

            if (frameConfigurationLocked && frameLockedBotsEnabled)
            {
                DrawText(
                    new Rect(580f, 515f, 760f, 52f),
                    "LOCKED  " + DifficultyName(frameLockedEnemyDifficulty),
                    headingStyle,
                    TeamColor(ParticipantTeam.Red));
            }

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
            DrawText(new Rect(110f, 580f, 520f, 72f), "PRESS ANY BUTTON", headingStyle, Color.white);
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
            DrawText(
                new Rect(120f, 365f, 1680f, 42f),
                "LOCKED  BOTS " + (frameConfigurationLocked && frameLockedBotsEnabled ? "ON" : "OFF") +
                (frameConfigurationLocked && frameLockedBotsEnabled ? "   DIFFICULTY " + DifficultyName(frameLockedEnemyDifficulty) : string.Empty),
                smallStyle,
                frameLockedBotsEnabled ? TeamColor(ParticipantTeam.Red) : new Color(0.65f, 0.68f, 0.72f));
            DrawTable(new Rect(120f, 420f, 1680f, 440f), true);

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
            tableHeaderNumberStyle = new GUIStyle(tableHeaderStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };
            tableNumberStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold
            };
            healthFillStyle = new GUIStyle(GUI.skin.box);
            healthFillStyle.normal.background = MakeSolidTexture(new Color(0.25f, 0.9f, 0.45f, 0.95f));
            scoreStyle = new GUIStyle(labelStyle)
            {
                fontSize = 48,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            whiteTexture = MakeSolidTexture(Color.white);
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
