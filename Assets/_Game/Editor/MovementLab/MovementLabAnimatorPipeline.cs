using System;
using System.Collections.Generic;
using RocketFooxball.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

using WorldAnimatorTransitionSpecification = RocketFooxball.Editor.MovementLabContract.WorldAnimatorTransitionSpecification;
using static RocketFooxball.Editor.MovementLabContractCatalog;
using static RocketFooxball.Editor.MovementLabImportPipeline;

namespace RocketFooxball.Editor
{
    internal static partial class MovementLabAnimatorPipeline
    {
        internal static void Validate()
        {
            MovementLabImportPipeline.ValidateModelImporterContracts();

            var worldPath = MovementLabContract.AnimationsPath + "/WorldCharacter.controller";
            RequireController(worldPath, "world");
            var worldController = AssetDatabase.LoadAssetAtPath<AnimatorController>(worldPath);
            var worldClips = LoadImportedClips(CharacterModelPath, new[]
            {
                MovementLabContract.IdleStateName,
                MovementLabContract.RunStateName,
                MovementLabContract.JumpStateName,
                MovementLabContract.FallStateName,
                MovementLabContract.LandStateName,
                MovementLabContract.KickStateName
            });
            if (!IsWorldAnimatorControllerExact(worldController, worldClips, out var worldReason))
                throw new InvalidOperationException("World animator controller contract invalid: " + worldPath + "; " + worldReason);

            var fpsPath = MovementLabContract.AnimationsPath + "/FpsKick.controller";
            RequireController(fpsPath, "FPS");
            var fpsController = AssetDatabase.LoadAssetAtPath<AnimatorController>(fpsPath);
            var fpsClips = LoadImportedClips(FpsKickModelPath, new[]
            {
                MovementLabContract.IdleStateName,
                MovementLabContract.KickStateName
            });
            if (!IsFpsAnimatorControllerExact(fpsController, fpsClips, out var fpsReason))
                throw new InvalidOperationException("FPS animator controller contract invalid: " + fpsPath + "; " + fpsReason);
        }

        internal static void RequireController(string path, string label)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) throw new InvalidOperationException("Missing generated " + label + " animator controller: " + path);
        }
    }

    internal static partial class MovementLabAnimatorPipeline
    {
        internal static RuntimeAnimatorController EnsureAnimatorController(string path, string modelPath)
        {
            var clips = LoadImportedClips(modelPath, new[]
            {
                MovementLabContract.IdleStateName,
                MovementLabContract.KickStateName
            });
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                RebuildAnimatorController(controller, clips);
            }
            else if (!IsFpsAnimatorControllerExact(controller, clips, out _))
            {
                // Repair the existing main asset. Deleting/recreating it would
                // change its GUID and leave prefab references pointing at stale data.
                RebuildAnimatorController(controller, clips);
            }
            return controller;
        }

        internal static void RebuildAnimatorController(AnimatorController controller, AnimationClip[] clips)
        {
            if (controller == null || clips == null || clips.Length != 2 || clips[0] == null || clips[1] == null)
                throw new InvalidOperationException("FPS animator rebuild inputs are invalid.");

            while (controller.layers.Length > 1) controller.RemoveLayer(controller.layers.Length - 1);
            if (controller.layers.Length == 0) controller.AddLayer(MovementLabContract.AnimatorBaseLayerName);

            var layer = controller.layers[0];
            if (layer.stateMachine == null) throw new InvalidOperationException("FPS animator base state machine is unavailable.");
            ConfigureLayer(ref layer, MovementLabContract.AnimatorBaseLayerName);

            var stateMachine = layer.stateMachine;
            ClearStateMachine(stateMachine);
            stateMachine.name = MovementLabContract.AnimatorBaseLayerName;
            ConfigureParameters(controller, new[] { MovementLabContract.KickTriggerParameterName },
                new[] { AnimatorControllerParameterType.Trigger });

            var idle = stateMachine.AddState(MovementLabContract.IdleStateName);
            var kick = stateMachine.AddState(MovementLabContract.KickStateName);
            ConfigureState(idle, clips[0]);
            ConfigureState(kick, clips[1]);
            stateMachine.defaultState = idle;

            var anyToKick = stateMachine.AddAnyStateTransition(kick);
            ConfigureTransition(anyToKick, false, 0f, 0f, false,
                new AnimatorConditionSpecification(AnimatorConditionMode.If, 0f, MovementLabContract.KickTriggerParameterName));
            var kickToIdle = kick.AddTransition(idle);
            ConfigureTransition(kickToIdle, true, 1f, 0.02f, false);

            var layers = controller.layers;
            layers[0] = layer;
            controller.layers = layers;
            CleanupAnimatorSubassets(controller, new[] { stateMachine });
            MarkAnimatorObjectsDirty(controller, new[] { stateMachine }, new[] { idle, kick }, new[] { anyToKick, kickToIdle });
        }

        internal static void RebuildFpsAnimatorController(AnimatorController controller, AnimationClip[] clips)
        {
            RebuildAnimatorController(controller, clips);
        }

        internal static RuntimeAnimatorController EnsureWorldAnimatorController(string path, string modelPath)
        {
            var clips = LoadImportedClips(modelPath, new[]
            {
                MovementLabContract.IdleStateName,
                MovementLabContract.RunStateName,
                MovementLabContract.JumpStateName,
                MovementLabContract.FallStateName,
                MovementLabContract.LandStateName,
                MovementLabContract.KickStateName
            });
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                RebuildWorldAnimatorController(controller, clips);
            }
            else if (!IsWorldAnimatorControllerExact(controller, clips, out _))
            {
                // Repair in place. The controller's main-asset GUID remains
                // stable while stale states/transitions are removed below.
                RebuildWorldAnimatorController(controller, clips);
            }
            return controller;
        }

        internal static void RebuildWorldAnimatorController(AnimatorController controller, AnimationClip[] clips)
        {
            if (controller == null || clips == null || clips.Length != 6)
                throw new InvalidOperationException("World animator rebuild inputs are invalid.");
            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) throw new InvalidOperationException("World animator rebuild clip is missing at index " + i + ".");
            }

            while (controller.layers.Length > 2) controller.RemoveLayer(controller.layers.Length - 1);
            if (controller.layers.Length == 0) controller.AddLayer(MovementLabContract.AnimatorBaseLayerName);
            if (controller.layers.Length == 1) controller.AddLayer(MovementLabContract.AnimatorKickLayerName);

            var layers = controller.layers;
            var baseLayer = layers[0];
            var kickLayer = layers[1];
            if (baseLayer.stateMachine == null || kickLayer.stateMachine == null)
                throw new InvalidOperationException("World animator layer state machine is unavailable.");

            ConfigureLayer(ref baseLayer, MovementLabContract.AnimatorBaseLayerName);
            ConfigureLayer(ref kickLayer, MovementLabContract.AnimatorKickLayerName);
            var baseStateMachine = baseLayer.stateMachine;
            var kickStateMachine = kickLayer.stateMachine;
            ClearStateMachine(baseStateMachine);
            ClearStateMachine(kickStateMachine);
            baseStateMachine.name = MovementLabContract.AnimatorBaseLayerName;
            kickStateMachine.name = MovementLabContract.AnimatorKickLayerName;

            ConfigureParameters(controller,
                new[] { "Speed", "Grounded", "VerticalSpeed", MovementLabContract.KickTriggerParameterName },
                new[]
                {
                    AnimatorControllerParameterType.Float,
                    AnimatorControllerParameterType.Bool,
                    AnimatorControllerParameterType.Float,
                    AnimatorControllerParameterType.Trigger
                });

            var baseStates = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            var baseMotions = new[] { clips[0], clips[1], clips[2], clips[3], clips[4] };
            for (var i = 0; i < MovementLabContract.WorldBaseStateNames.Length; i++)
            {
                var state = baseStateMachine.AddState(MovementLabContract.WorldBaseStateNames[i]);
                ConfigureState(state, baseMotions[i]);
                baseStates.Add(state.name, state);
            }
            baseStateMachine.defaultState = baseStates[MovementLabContract.IdleStateName];
            ApplyWorldAnimatorTransitions(baseStateMachine, baseStates);

            var empty = kickStateMachine.AddState(MovementLabContract.EmptyStateName);
            var kick = kickStateMachine.AddState(MovementLabContract.KickStateName);
            ConfigureState(empty, null);
            ConfigureState(kick, clips[5]);
            kickStateMachine.defaultState = empty;
            var anyToKick = kickStateMachine.AddAnyStateTransition(kick);
            ConfigureTransition(anyToKick, false, 0f, 0f, false,
                new AnimatorConditionSpecification(AnimatorConditionMode.If, 0f, MovementLabContract.KickTriggerParameterName));
            var kickToEmpty = kick.AddTransition(empty);
            ConfigureTransition(kickToEmpty, true, 1f, 0.02f, false);

            layers[0] = baseLayer;
            layers[1] = kickLayer;
            controller.layers = layers;
            CleanupAnimatorSubassets(controller, new[] { baseStateMachine, kickStateMachine });

            var allStates = new List<AnimatorState>(baseStates.Values) { empty, kick };
            var allTransitions = new List<AnimatorStateTransition> { anyToKick, kickToEmpty };
            foreach (var state in baseStates.Values) allTransitions.AddRange(state.transitions);
            MarkAnimatorObjectsDirty(controller, new[] { baseStateMachine, kickStateMachine }, allStates.ToArray(), allTransitions.ToArray());
        }

        internal static void CleanupWorldAnimatorSubassets(AnimatorController controller, AnimatorStateMachine stateMachine)
        {
            CleanupAnimatorSubassets(controller, new[] { stateMachine });
        }

        internal static void CleanupWorldAnimatorSubassets(AnimatorController controller, params AnimatorStateMachine[] stateMachines)
        {
            CleanupAnimatorSubassets(controller, stateMachines);
        }

        private static void CleanupAnimatorSubassets(AnimatorController controller, AnimatorStateMachine[] stateMachines)
        {
            var keep = new HashSet<UnityEngine.Object> { controller };
            if (stateMachines != null)
            {
                for (var machineIndex = 0; machineIndex < stateMachines.Length; machineIndex++)
                {
                    var stateMachine = stateMachines[machineIndex];
                    if (stateMachine == null) continue;
                    keep.Add(stateMachine);
                    var states = stateMachine.states;
                    for (var stateIndex = 0; stateIndex < states.Length; stateIndex++)
                    {
                        var state = states[stateIndex].state;
                        if (state == null) continue;
                        keep.Add(state);
                        var transitions = state.transitions;
                        for (var transitionIndex = 0; transitionIndex < transitions.Length; transitionIndex++)
                        {
                            if (transitions[transitionIndex] != null) keep.Add(transitions[transitionIndex]);
                        }
                    }
                    var anyTransitions = stateMachine.anyStateTransitions;
                    for (var transitionIndex = 0; transitionIndex < anyTransitions.Length; transitionIndex++)
                    {
                        if (anyTransitions[transitionIndex] != null) keep.Add(anyTransitions[transitionIndex]);
                    }
                }
            }

            var subassets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller));
            for (var i = 0; i < subassets.Length; i++)
            {
                var asset = subassets[i];
                if (asset == null || keep.Contains(asset)) continue;
                if (asset is AnimatorState || asset is AnimatorTransitionBase || asset is AnimatorStateMachine || asset is StateMachineBehaviour)
                    UnityEngine.Object.DestroyImmediate(asset, true);
            }
        }

        internal static AnimatorStateTransition AddAnimatorConditionTransition(AnimatorState source, AnimatorState destination, AnimatorConditionMode mode, float threshold, string parameter)
        {
            var transition = source.AddTransition(destination);
            ConfigureTransition(transition, false, 0f, 0.02f, true,
                new AnimatorConditionSpecification(mode, threshold, parameter));
            return transition;
        }

        internal static void ValidateWorldAnimatorController(Animator animator, string path, string modelPath)
        {
            if (animator == null) throw new InvalidOperationException("World Animator is missing: " + path);
            var avatars = FindImportedAvatars(modelPath);
            if (avatars.Length != 1 || avatars[0] == null || animator.avatar != avatars[0] ||
                !string.Equals(AssetDatabase.GetAssetPath(avatars[0]), modelPath, StringComparison.Ordinal))
                throw new InvalidOperationException("World Animator Avatar provenance invalid: " + modelPath);

            var clips = LoadImportedClips(modelPath, new[]
            {
                MovementLabContract.IdleStateName,
                MovementLabContract.RunStateName,
                MovementLabContract.JumpStateName,
                MovementLabContract.FallStateName,
                MovementLabContract.LandStateName,
                MovementLabContract.KickStateName
            });
            if (!IsWorldAnimatorControllerExact(animator.runtimeAnimatorController as AnimatorController, clips, out var reason))
                throw new InvalidOperationException("World animator controller contract invalid: " + path + "; " + reason);
        }

        internal static void ValidateFpsAnimatorController(Animator animator, string path, string modelPath)
        {
            if (animator == null) throw new InvalidOperationException("FPS Animator is missing: " + path);
            var avatars = FindImportedAvatars(modelPath);
            if (avatars.Length != 1 || avatars[0] == null || animator.avatar != avatars[0] ||
                !string.Equals(AssetDatabase.GetAssetPath(avatars[0]), modelPath, StringComparison.Ordinal))
                throw new InvalidOperationException("FPS Animator Avatar provenance invalid: " + modelPath);

            var clips = LoadImportedClips(modelPath, new[]
            {
                MovementLabContract.IdleStateName,
                MovementLabContract.KickStateName
            });
            if (!IsFpsAnimatorControllerExact(animator.runtimeAnimatorController as AnimatorController, clips, out var reason))
                throw new InvalidOperationException("FPS animator controller contract invalid: " + path + "; " + reason);
        }

        internal static WorldAnimatorTransitionSpecification[] GetWorldAnimatorTransitionSpecifications()
        {
            return (WorldAnimatorTransitionSpecification[])MovementLabContract.WorldAnimatorTransitions.Clone();
        }

        internal static void ApplyWorldAnimatorTransitions(AnimatorStateMachine stateMachine, Dictionary<string, AnimatorState> states)
        {
            var specifications = MovementLabContract.WorldAnimatorTransitions;
            for (var i = 0; i < specifications.Length; i++)
            {
                var specification = specifications[i];
                var destination = states[specification.Destination];
                var transition = specification.AnyState
                    ? stateMachine.AddAnyStateTransition(destination)
                    : states[specification.Source].AddTransition(destination);
                var conditions = new AnimatorConditionSpecification[specification.Conditions.Length];
                for (var conditionIndex = 0; conditionIndex < conditions.Length; conditionIndex++)
                {
                    var condition = specification.Conditions[conditionIndex];
                    conditions[conditionIndex] = new AnimatorConditionSpecification(condition.Mode, condition.Threshold, condition.Parameter);
                }
                ConfigureTransition(transition, specification.HasExitTime, specification.ExitTime, specification.Duration,
                    specification.CanTransitionToSelf, conditions);
            }
        }

        internal static bool IsWorldAnimatorControllerExact(AnimatorController controller, AnimationClip[] clips, out string reason)
        {
            reason = null;
            if (controller == null) return Fail("controller missing", out reason);
            if (clips == null || clips.Length != 6) return Fail("clip set incomplete", out reason);
            if (!ValidateParameters(controller, new[] { "Speed", "Grounded", "VerticalSpeed", MovementLabContract.KickTriggerParameterName },
                    new[] { AnimatorControllerParameterType.Float, AnimatorControllerParameterType.Bool, AnimatorControllerParameterType.Float, AnimatorControllerParameterType.Trigger }, out reason)) return false;
            if (controller.layers.Length != 2) return Fail("layer count", out reason);

            var baseLayer = controller.layers[0];
            var kickLayer = controller.layers[1];
            if (!IsLayerExact(baseLayer, MovementLabContract.AnimatorBaseLayerName, out reason) ||
                !IsLayerExact(kickLayer, MovementLabContract.AnimatorKickLayerName, out reason)) return false;
            if (baseLayer.stateMachine == null || kickLayer.stateMachine == null) return Fail("layer state machine missing", out reason);

            var baseMachine = baseLayer.stateMachine;
            var kickMachine = kickLayer.stateMachine;
            if (!IsFlatStateMachine(baseMachine, out reason) || !IsFlatStateMachine(kickMachine, out reason)) return false;
            if (baseMachine.anyStateTransitions.Length != 0 || baseMachine.states.Length != MovementLabContract.WorldBaseStateNames.Length ||
                baseMachine.defaultState == null || baseMachine.defaultState.name != MovementLabContract.IdleStateName)
                return Fail("base state machine contract", out reason);
            if (kickMachine.anyStateTransitions.Length != 1 || kickMachine.states.Length != 2 ||
                kickMachine.defaultState == null || kickMachine.defaultState.name != MovementLabContract.EmptyStateName)
                return Fail("kick state machine contract", out reason);

            var baseStates = GetNamedStates(baseMachine, out reason);
            if (baseStates == null) return false;
            var kickStates = GetNamedStates(kickMachine, out reason);
            if (kickStates == null) return false;
            if (!HasExpectedStates(baseStates, MovementLabContract.WorldBaseStateNames, out reason) ||
                !HasExpectedStates(kickStates, MovementLabContract.WorldKickStateNames, out reason)) return false;

            var expectedBaseMotions = new[] { clips[0], clips[1], clips[2], clips[3], clips[4] };
            if (!ValidateStateMotions(baseStates, MovementLabContract.WorldBaseStateNames, expectedBaseMotions, out reason) ||
                !ValidateStateMotions(kickStates, MovementLabContract.WorldKickStateNames, new AnimationClip[] { null, clips[5] }, out reason)) return false;
            if (!ValidateAllStateSettings(baseStates.Values, out reason) || !ValidateAllStateSettings(kickStates.Values, out reason)) return false;
            if (!DistinctClips(clips, out reason)) return false;
            if (!ValidateWorldKickCurveBindings(clips[5], out reason)) return false;

            var specifications = GetWorldAnimatorTransitionSpecifications();
            if (specifications.Length != 14) return Fail("base transition specification count", out reason);
            var baseTransitionCount = 0;
            for (var i = 0; i < baseMachine.states.Length; i++) baseTransitionCount += baseMachine.states[i].state.transitions.Length;
            if (baseTransitionCount != specifications.Length) return Fail("base transition count", out reason);
            for (var i = 0; i < specifications.Length; i++)
            {
                var specification = specifications[i];
                if (!baseStates.TryGetValue(specification.Source, out var source)) return Fail("base transition source", out reason);
                var found = FindMatchingTransition(source.transitions, specification.Destination,
                    transition => MatchesWorldAnimatorTransition(transition, specification));
                if (found == null) return Fail("base transition semantics: " + specification.Source + " -> " + specification.Destination, out reason);
            }

            var anyToKick = kickMachine.anyStateTransitions[0];
            if (!MatchesTriggerTransition(anyToKick, kickStates[MovementLabContract.KickStateName], out reason)) return false;
            var kickTransitions = kickStates[MovementLabContract.KickStateName].transitions;
            if (kickTransitions.Length != 1 || !MatchesReturnTransition(kickTransitions[0], kickStates[MovementLabContract.EmptyStateName], out reason)) return false;

            var allStates = new List<AnimatorState>(baseStates.Values);
            allStates.AddRange(kickStates.Values);
            var allTransitions = new List<AnimatorStateTransition> { anyToKick };
            for (var i = 0; i < allStates.Count; i++) allTransitions.AddRange(allStates[i].transitions);
            return ValidateAnimatorSubassets(controller, new[] { baseMachine, kickMachine }, allStates, allTransitions, out reason);
        }

        internal static bool IsFpsAnimatorControllerExact(AnimatorController controller, AnimationClip[] clips, out string reason)
        {
            reason = null;
            if (controller == null) return Fail("controller missing", out reason);
            if (clips == null || clips.Length != 2) return Fail("clip set incomplete", out reason);
            if (!ValidateParameters(controller, new[] { MovementLabContract.KickTriggerParameterName },
                    new[] { AnimatorControllerParameterType.Trigger }, out reason)) return false;
            if (controller.layers.Length != 1) return Fail("layer count", out reason);
            var layer = controller.layers[0];
            if (!IsLayerExact(layer, MovementLabContract.AnimatorBaseLayerName, out reason) || layer.stateMachine == null) return false;
            var stateMachine = layer.stateMachine;
            if (!IsFlatStateMachine(stateMachine, out reason) || stateMachine.states.Length != 2 ||
                stateMachine.anyStateTransitions.Length != 1 || stateMachine.defaultState == null ||
                stateMachine.defaultState.name != MovementLabContract.IdleStateName) return Fail("state machine contract", out reason);

            var states = GetNamedStates(stateMachine, out reason);
            if (states == null || !HasExpectedStates(states, new[] { MovementLabContract.IdleStateName, MovementLabContract.KickStateName }, out reason)) return false;
            if (!ValidateStateMotions(states, new[] { MovementLabContract.IdleStateName, MovementLabContract.KickStateName }, clips, out reason) ||
                !ValidateAllStateSettings(states.Values, out reason) || !DistinctClips(clips, out reason)) return false;

            if (!MatchesTriggerTransition(stateMachine.anyStateTransitions[0], states[MovementLabContract.KickStateName], out reason)) return false;
            var kickTransitions = states[MovementLabContract.KickStateName].transitions;
            if (kickTransitions.Length != 1 || !MatchesReturnTransition(kickTransitions[0], states[MovementLabContract.IdleStateName], out reason)) return false;
            if (states[MovementLabContract.IdleStateName].transitions.Length != 0) return Fail("idle transition contract", out reason);

            var allStates = new List<AnimatorState>(states.Values);
            var allTransitions = new List<AnimatorStateTransition> { stateMachine.anyStateTransitions[0] };
            allTransitions.AddRange(kickTransitions);
            return ValidateAnimatorSubassets(controller, new[] { stateMachine }, allStates, allTransitions, out reason);
        }

        internal static bool MatchesWorldAnimatorTransition(AnimatorStateTransition transition, WorldAnimatorTransitionSpecification specification)
        {
            if (transition == null || transition.destinationStateMachine != null || transition.isExit ||
                transition.hasExitTime != specification.HasExitTime || Mathf.Abs(transition.exitTime - specification.ExitTime) > 0.0001f ||
                Mathf.Abs(transition.duration - specification.Duration) > 0.0001f || Mathf.Abs(transition.offset) > 0.0001f ||
                transition.canTransitionToSelf != specification.CanTransitionToSelf || !transition.hasFixedDuration ||
                transition.mute || transition.solo || transition.interruptionSource != TransitionInterruptionSource.None ||
                !transition.orderedInterruption) return false;
            var conditions = transition.conditions;
            if (conditions == null || conditions.Length != specification.Conditions.Length) return false;
            for (var i = 0; i < conditions.Length; i++)
            {
                var expected = specification.Conditions[i];
                if (conditions[i].mode != expected.Mode || conditions[i].parameter != expected.Parameter ||
                    Mathf.Abs(conditions[i].threshold - expected.Threshold) > 0.0001f) return false;
            }
            return true;
        }

        private readonly struct AnimatorConditionSpecification
        {
            internal readonly AnimatorConditionMode Mode;
            internal readonly float Threshold;
            internal readonly string Parameter;

            internal AnimatorConditionSpecification(AnimatorConditionMode mode, float threshold, string parameter)
            {
                Mode = mode;
                Threshold = threshold;
                Parameter = parameter;
            }
        }

        private static AnimationClip[] LoadImportedClips(string modelPath, string[] names)
        {
            var clips = new AnimationClip[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                clips[i] = FindImportedClip(modelPath, names[i]);
                if (clips[i] == null) throw new InvalidOperationException("Missing imported clip " + names[i] + " for " + modelPath);
            }
            return clips;
        }

        private static void ConfigureLayer(ref AnimatorControllerLayer layer, string name)
        {
            layer.name = name;
            layer.defaultWeight = 1f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.avatarMask = null;
            layer.iKPass = false;
            layer.syncedLayerIndex = -1;
            layer.syncedLayerAffectsTiming = false;
        }

        private static void ConfigureParameters(AnimatorController controller, string[] names, AnimatorControllerParameterType[] types)
        {
            while (controller.parameters.Length > 0) controller.RemoveParameter(0);
            for (var i = 0; i < names.Length; i++) controller.AddParameter(names[i], types[i]);
        }

        private static void ConfigureState(AnimatorState state, Motion motion)
        {
            state.motion = motion;
            state.speed = 1f;
            state.cycleOffset = 0f;
            state.mirror = false;
            state.speedParameterActive = false;
            state.mirrorParameterActive = false;
            state.cycleOffsetParameterActive = false;
            state.timeParameterActive = false;
            state.iKOnFeet = false;
            state.writeDefaultValues = true;
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, bool hasExitTime, float exitTime,
            float duration, bool canTransitionToSelf, params AnimatorConditionSpecification[] conditions)
        {
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.duration = duration;
            transition.hasFixedDuration = true;
            transition.offset = 0f;
            transition.canTransitionToSelf = canTransitionToSelf;
            transition.conditions = Array.Empty<AnimatorCondition>();
            transition.mute = false;
            transition.solo = false;
            transition.interruptionSource = TransitionInterruptionSource.None;
            transition.orderedInterruption = true;
            if (conditions == null) return;
            for (var i = 0; i < conditions.Length; i++)
            {
                var condition = conditions[i];
                transition.AddCondition(condition.Mode, condition.Threshold, condition.Parameter);
            }
        }

        private static void ClearStateMachine(AnimatorStateMachine stateMachine)
        {
            var anyTransitions = stateMachine.anyStateTransitions;
            for (var i = 0; i < anyTransitions.Length; i++) stateMachine.RemoveAnyStateTransition(anyTransitions[i]);
            var entryTransitions = stateMachine.entryTransitions;
            for (var i = 0; i < entryTransitions.Length; i++) stateMachine.RemoveEntryTransition(entryTransitions[i]);
            var states = stateMachine.states;
            for (var i = 0; i < states.Length; i++)
            {
                var state = states[i].state;
                if (state == null) continue;
                var transitions = state.transitions;
                for (var j = 0; j < transitions.Length; j++) state.RemoveTransition(transitions[j]);
                stateMachine.RemoveState(state);
            }
            var childStateMachines = stateMachine.stateMachines;
            for (var i = 0; i < childStateMachines.Length; i++) stateMachine.RemoveStateMachine(childStateMachines[i].stateMachine);
            stateMachine.behaviours = Array.Empty<StateMachineBehaviour>();
        }

        private static void MarkAnimatorObjectsDirty(AnimatorController controller, AnimatorStateMachine[] machines,
            AnimatorState[] states, AnimatorStateTransition[] transitions)
        {
            EditorUtility.SetDirty(controller);
            if (machines != null) for (var i = 0; i < machines.Length; i++) if (machines[i] != null) EditorUtility.SetDirty(machines[i]);
            if (states != null) for (var i = 0; i < states.Length; i++) if (states[i] != null) EditorUtility.SetDirty(states[i]);
            if (transitions != null) for (var i = 0; i < transitions.Length; i++) if (transitions[i] != null) EditorUtility.SetDirty(transitions[i]);
        }

        private static bool ValidateParameters(AnimatorController controller, string[] names, AnimatorControllerParameterType[] types, out string reason)
        {
            reason = null;
            var parameters = controller.parameters;
            if (parameters.Length != names.Length) return Fail("parameter count", out reason);
            for (var i = 0; i < names.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.name != names[i] || parameter.type != types[i] ||
                    Mathf.Abs(parameter.defaultFloat) > 0.0001f || parameter.defaultInt != 0 || parameter.defaultBool)
                    return Fail("parameter contract: " + names[i], out reason);
            }
            return true;
        }

        private static bool IsLayerExact(AnimatorControllerLayer layer, string name, out string reason)
        {
            if (layer.name != name || Mathf.Abs(layer.defaultWeight - 1f) > 0.0001f ||
                layer.blendingMode != AnimatorLayerBlendingMode.Override || layer.avatarMask != null || layer.iKPass ||
                layer.syncedLayerIndex != -1 ||
                layer.syncedLayerAffectsTiming) return Fail("layer contract: " + name, out reason);
            reason = null;
            return true;
        }

        private static bool IsFlatStateMachine(AnimatorStateMachine stateMachine, out string reason)
        {
            if (stateMachine == null || stateMachine.stateMachines.Length != 0 || stateMachine.entryTransitions.Length != 0 ||
                stateMachine.behaviours.Length != 0)
                return Fail("state machine must be flat", out reason);
            reason = null;
            return true;
        }

        private static Dictionary<string, AnimatorState> GetNamedStates(AnimatorStateMachine stateMachine, out string reason)
        {
            var named = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            for (var i = 0; i < stateMachine.states.Length; i++)
            {
                var state = stateMachine.states[i].state;
                if (state == null || named.ContainsKey(state.name))
                {
                    Fail("state identity", out reason);
                    return null;
                }
                named.Add(state.name, state);
            }
            reason = null;
            return named;
        }

        private static bool HasExpectedStates(Dictionary<string, AnimatorState> states, string[] expected, out string reason)
        {
            if (states.Count != expected.Length) return Fail("state count", out reason);
            for (var i = 0; i < expected.Length; i++) if (!states.ContainsKey(expected[i])) return Fail("state missing: " + expected[i], out reason);
            reason = null;
            return true;
        }

        private static bool ValidateStateMotions(Dictionary<string, AnimatorState> states, string[] names, AnimationClip[] clips, out string reason)
        {
            if (clips.Length != names.Length) return Fail("state motion input count", out reason);
            for (var i = 0; i < names.Length; i++)
            {
                var state = states[names[i]];
                if (state.motion != clips[i]) return Fail("state motion: " + names[i], out reason);
            }
            reason = null;
            return true;
        }

        private static bool ValidateAllStateSettings(IEnumerable<AnimatorState> states, out string reason)
        {
            foreach (var state in states)
            {
                if (state == null || Mathf.Abs(state.speed - 1f) > 0.0001f || Mathf.Abs(state.cycleOffset) > 0.0001f ||
                    state.mirror || state.speedParameterActive || state.mirrorParameterActive || state.cycleOffsetParameterActive ||
                    state.timeParameterActive || state.iKOnFeet || !state.writeDefaultValues)
                    return Fail("state settings", out reason);
            }
            reason = null;
            return true;
        }

        private static bool DistinctClips(AnimationClip[] clips, out string reason)
        {
            var seen = new HashSet<AnimationClip>();
            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null || !seen.Add(clips[i])) return Fail("clips must be distinct", out reason);
            }
            reason = null;
            return true;
        }

        private static AnimatorStateTransition FindMatchingTransition(AnimatorStateTransition[] transitions, string destination,
            Func<AnimatorStateTransition, bool> predicate)
        {
            AnimatorStateTransition found = null;
            for (var i = 0; i < transitions.Length; i++)
            {
                var candidate = transitions[i];
                if (candidate == null || candidate.destinationState == null || candidate.destinationState.name != destination || !predicate(candidate)) continue;
                if (found != null) return null;
                found = candidate;
            }
            return found;
        }

        private static bool MatchesTriggerTransition(AnimatorStateTransition transition, AnimatorState destination, out string reason)
        {
            if (transition == null || transition.destinationState != destination || transition.destinationStateMachine != null || transition.isExit ||
                transition.hasExitTime || Mathf.Abs(transition.exitTime) > 0.0001f || Mathf.Abs(transition.duration) > 0.0001f ||
                Mathf.Abs(transition.offset) > 0.0001f || transition.canTransitionToSelf || !transition.hasFixedDuration ||
                transition.mute || transition.solo || transition.interruptionSource != TransitionInterruptionSource.None || !transition.orderedInterruption)
                return Fail("AnyState -> Kick transition", out reason);
            var conditions = transition.conditions;
            if (conditions == null || conditions.Length != 1 || conditions[0].mode != AnimatorConditionMode.If ||
                conditions[0].parameter != MovementLabContract.KickTriggerParameterName || Mathf.Abs(conditions[0].threshold) > 0.0001f)
                return Fail("Kick trigger transition condition", out reason);
            reason = null;
            return true;
        }

        private static bool MatchesReturnTransition(AnimatorStateTransition transition, AnimatorState destination, out string reason)
        {
            if (transition == null || transition.destinationState != destination || transition.destinationStateMachine != null || transition.isExit ||
                !transition.hasExitTime || Mathf.Abs(transition.exitTime - 1f) > 0.0001f || Mathf.Abs(transition.duration - 0.02f) > 0.0001f ||
                Mathf.Abs(transition.offset) > 0.0001f || transition.canTransitionToSelf || !transition.hasFixedDuration ||
                transition.mute || transition.solo || transition.interruptionSource != TransitionInterruptionSource.None || !transition.orderedInterruption)
                return Fail("Kick return transition", out reason);
            if (transition.conditions == null || transition.conditions.Length != 0) return Fail("Kick return transition conditions", out reason);
            reason = null;
            return true;
        }

        private static bool ValidateAnimatorSubassets(AnimatorController controller, AnimatorStateMachine[] machines,
            List<AnimatorState> expectedStates, List<AnimatorStateTransition> expectedTransitions, out string reason)
        {
            var subassets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller));
            var actualStates = new HashSet<AnimatorState>();
            var actualTransitions = new HashSet<AnimatorStateTransition>();
            var actualMachines = new HashSet<AnimatorStateMachine>();
            var unexpectedTransitions = 0;
            var unexpectedBehaviours = 0;
            for (var i = 0; i < subassets.Length; i++)
            {
                if (subassets[i] is AnimatorState state) actualStates.Add(state);
                if (subassets[i] is AnimatorStateTransition transition) actualTransitions.Add(transition);
                if (subassets[i] is AnimatorTransitionBase && !(subassets[i] is AnimatorStateTransition)) unexpectedTransitions++;
                if (subassets[i] is AnimatorStateMachine stateMachine) actualMachines.Add(stateMachine);
                if (subassets[i] is StateMachineBehaviour) unexpectedBehaviours++;
            }
            if (actualStates.Count != expectedStates.Count || actualTransitions.Count != expectedTransitions.Count ||
                actualMachines.Count != machines.Length || unexpectedTransitions != 0 || unexpectedBehaviours != 0)
                return Fail("unique expected controller subassets", out reason);
            for (var i = 0; i < expectedStates.Count; i++) if (!actualStates.Contains(expectedStates[i])) return Fail("missing state subasset", out reason);
            for (var i = 0; i < expectedTransitions.Count; i++) if (!actualTransitions.Contains(expectedTransitions[i])) return Fail("missing transition subasset", out reason);
            for (var i = 0; i < machines.Length; i++) if (!actualMachines.Contains(machines[i])) return Fail("missing state machine subasset", out reason);
            reason = null;
            return true;
        }

        private static bool ValidateWorldKickCurveBindings(AnimationClip clip, out string reason)
        {
            if (clip == null) return Fail("world Kick clip missing", out reason);
            var allowed = new HashSet<string>(MovementLabContract.WorldKickLegBoneNames, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            for (var i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                if (binding.type != typeof(Transform)) return Fail("world Kick contains a non-Transform curve", out reason);
                var path = binding.path ?? string.Empty;
                var separator = path.LastIndexOf('/');
                var boneName = separator >= 0 ? path.Substring(separator + 1) : path;
                if (!allowed.Contains(boneName)) return Fail("world Kick curve targets forbidden bone/object: " + path, out reason);
                seen.Add(boneName);
            }
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (objectBindings.Length != 0) return Fail("world Kick contains object-reference curves", out reason);
            if (seen.Count != allowed.Count) return Fail("world Kick leg curve set incomplete", out reason);
            reason = null;
            return true;
        }

        private static bool Fail(string message, out string reason)
        {
            reason = message;
            return false;
        }
    }
}
