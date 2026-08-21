using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Diagnostics;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Physics;
using RocketFooxball.Runtime.Rendering;
using RocketFooxball.Runtime.Weapons;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MaterialSpecification = RocketFooxball.Editor.MovementLabContract.MaterialSpecification;
using PbrMaterialSpecification = RocketFooxball.Editor.MovementLabContract.PbrMaterialSpecification;
using WorldAnimatorConditionSpecification = RocketFooxball.Editor.MovementLabContract.WorldAnimatorConditionSpecification;
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
            RequireController(MovementLabContract.AnimationsPath + "/WorldCharacter.controller", "world");
            RequireController(MovementLabContract.AnimationsPath + "/FpsKick.controller", "FPS");
        }
        internal static void RequireController(string path, string label)
        {
            var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
            if (controller == null) throw new System.InvalidOperationException("Missing generated " + label + " animator controller: " + path);
        }
    }

    internal static partial class MovementLabAnimatorPipeline
    {
                internal static RuntimeAnimatorController EnsureAnimatorController(string path, string modelPath)
                {
                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                    var idle = FindImportedClip(modelPath, "Idle");
                    var kick = FindImportedClip(modelPath, "Kick");
                    if (idle == null || kick == null)
                    {
                        throw new InvalidOperationException("Missing imported Idle/Kick clips for " + modelPath);
                    }

                    AnimatorState idleState = null;
                    AnimatorState kickState = null;
                    AnimatorStateTransition anyToKick = null;
                    AnimatorStateTransition kickToIdle = null;
                    var rebuild = controller == null;
                    if (!rebuild)
                    {
                        var stateCount = 0;
                        var transitionCount = 0;
                        var subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                        for (var i = 0; i < subAssets.Length; i++)
                        {
                            if (subAssets[i] is AnimatorState) stateCount++;
                            if (subAssets[i] is AnimatorStateTransition) transitionCount++;
                        }

                        var existingStateMachine = controller.layers.Length > 0 ? controller.layers[0].stateMachine : null;
                        var states = existingStateMachine != null ? existingStateMachine.states : Array.Empty<ChildAnimatorState>();
                        for (var i = 0; i < states.Length; i++)
                        {
                            if (states[i].state != null && states[i].state.name == "Idle") idleState = states[i].state;
                            if (states[i].state != null && states[i].state.name == "Kick") kickState = states[i].state;
                        }

                        if (existingStateMachine != null && idleState != null && kickState != null && existingStateMachine.anyStateTransitions.Length == 1)
                        {
                            anyToKick = existingStateMachine.anyStateTransitions[0];
                        }
                        if (kickState != null && kickState.transitions.Length == 1)
                        {
                            kickToIdle = kickState.transitions[0];
                        }

                        rebuild = stateCount != 2 || transitionCount != 2 || states.Length != 2 ||
                            idleState == null || kickState == null || anyToKick == null || kickToIdle == null ||
                            anyToKick.destinationState != kickState || kickToIdle.destinationState != idleState ||
                            idleState.transitions.Length != 0;
                    }

                    if (rebuild)
                    {
                        if (controller != null && !AssetDatabase.DeleteAsset(path))
                        {
                            throw new InvalidOperationException("Failed to rebuild stale Animator controller: " + path);
                        }
                        controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                        var newStateMachine = controller.layers[0].stateMachine;
                        idleState = newStateMachine.AddState("Idle");
                        kickState = newStateMachine.AddState("Kick");
                        anyToKick = newStateMachine.AddAnyStateTransition(kickState);
                        kickToIdle = kickState.AddTransition(idleState);
                    }

                    if (controller.parameters.Length != 1 || controller.parameters[0].name != "Kick" || controller.parameters[0].type != AnimatorControllerParameterType.Trigger)
                    {
                        while (controller.parameters.Length > 0) controller.RemoveParameter(0);
                        controller.AddParameter("Kick", AnimatorControllerParameterType.Trigger);
                    }

                    var stateMachine = controller.layers[0].stateMachine;
                    idleState.motion = idle;
                    kickState.motion = kick;
                    stateMachine.defaultState = idleState;
                    anyToKick.hasExitTime = false;
                    anyToKick.duration = 0.02f;
                    anyToKick.canTransitionToSelf = false;
                    anyToKick.conditions = Array.Empty<AnimatorCondition>();
                    anyToKick.AddCondition(AnimatorConditionMode.If, 0f, "Kick");
                    kickToIdle.hasExitTime = true;
                    kickToIdle.exitTime = 1f;
                    kickToIdle.duration = 0.02f;
                    kickToIdle.conditions = Array.Empty<AnimatorCondition>();
                    EditorUtility.SetDirty(stateMachine);
                    EditorUtility.SetDirty(idleState);
                    EditorUtility.SetDirty(kickState);
                    EditorUtility.SetDirty(anyToKick);
                    EditorUtility.SetDirty(kickToIdle);
                    EditorUtility.SetDirty(controller);
                    return controller;
                }

                internal static RuntimeAnimatorController EnsureWorldAnimatorController(string path, string modelPath)
                {
                    var clipNames = new[] { "Idle", "Run", "Jump", "Fall", "Land", "Kick" };
                    var clips = new AnimationClip[clipNames.Length];
                    for (var i = 0; i < clipNames.Length; i++)
                    {
                        clips[i] = FindImportedClip(modelPath, clipNames[i]);
                        if (clips[i] == null) throw new InvalidOperationException("Missing imported world clip " + clipNames[i] + " for " + modelPath);
                    }

                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                    if (controller == null)
                    {
                        controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                        RebuildWorldAnimatorController(controller, clips);
                    }
                    else if (!IsWorldAnimatorControllerExact(controller, clips, out _))
                    {
                        // Repair in place. Deleting/recreating the main asset changes
                        // its GUID and leaves stale controller subassets behind.
                        RebuildWorldAnimatorController(controller, clips);
                    }
                    return controller;
                }

                internal static void RebuildWorldAnimatorController(AnimatorController controller, AnimationClip[] clips)
                {
                    if (controller == null || clips == null || clips.Length != 6) throw new InvalidOperationException("World animator rebuild inputs are invalid.");

                    while (controller.layers.Length > 1) controller.RemoveLayer(controller.layers.Length - 1);
                    if (controller.layers.Length == 0) controller.AddLayer("Base Layer");

                    var layer = controller.layers[0];
                    layer.name = "Base Layer";
                    var stateMachine = layer.stateMachine;
                    if (stateMachine == null)
                    {
                        controller.RemoveLayer(0);
                        controller.AddLayer("Base Layer");
                        layer = controller.layers[0];
                        stateMachine = layer.stateMachine;
                    }
                    if (stateMachine == null) throw new InvalidOperationException("World animator base state machine is unavailable.");

                    var anyTransitions = stateMachine.anyStateTransitions;
                    for (var i = 0; i < anyTransitions.Length; i++) stateMachine.RemoveAnyStateTransition(anyTransitions[i]);
                    var existingStates = stateMachine.states;
                    for (var i = 0; i < existingStates.Length; i++)
                    {
                        var transitions = existingStates[i].state != null ? existingStates[i].state.transitions : Array.Empty<AnimatorStateTransition>();
                        for (var j = 0; j < transitions.Length; j++) existingStates[i].state.RemoveTransition(transitions[j]);
                        if (existingStates[i].state != null) stateMachine.RemoveState(existingStates[i].state);
                    }
                    var childStateMachines = stateMachine.stateMachines;
                    for (var i = 0; i < childStateMachines.Length; i++) stateMachine.RemoveStateMachine(childStateMachines[i].stateMachine);

                    while (controller.parameters.Length > 0) controller.RemoveParameter(0);
                    controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
                    controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
                    controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
                    controller.AddParameter("Kick", AnimatorControllerParameterType.Trigger);

                    var idle = stateMachine.AddState("Idle");
                    var run = stateMachine.AddState("Run");
                    var jump = stateMachine.AddState("Jump");
                    var fall = stateMachine.AddState("Fall");
                    var land = stateMachine.AddState("Land");
                    var kick = stateMachine.AddState("Kick");
                    stateMachine.defaultState = idle;
                    idle.motion = clips[0];
                    run.motion = clips[1];
                    jump.motion = clips[2];
                    fall.motion = clips[3];
                    land.motion = clips[4];
                    kick.motion = clips[5];

                    ApplyWorldAnimatorTransitions(stateMachine, new Dictionary<string, AnimatorState>(StringComparer.Ordinal)
                    {
                        { "Idle", idle }, { "Run", run }, { "Jump", jump }, { "Fall", fall }, { "Land", land }, { "Kick", kick }
                    });

                    controller.layers[0] = layer;
                    CleanupWorldAnimatorSubassets(controller, stateMachine);
                    EditorUtility.SetDirty(stateMachine);
                    EditorUtility.SetDirty(controller);
                }

                internal static void CleanupWorldAnimatorSubassets(AnimatorController controller, AnimatorStateMachine stateMachine)
                {
                    var keep = new HashSet<UnityEngine.Object> { controller, stateMachine };
                    var states = stateMachine.states;
                    for (var i = 0; i < states.Length; i++)
                    {
                        if (states[i].state == null) continue;
                        keep.Add(states[i].state);
                        var transitions = states[i].state.transitions;
                        for (var j = 0; j < transitions.Length; j++) keep.Add(transitions[j]);
                    }
                    var anyTransitions = stateMachine.anyStateTransitions;
                    for (var i = 0; i < anyTransitions.Length; i++) keep.Add(anyTransitions[i]);

                    var subassets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller));
                    for (var i = 0; i < subassets.Length; i++)
                    {
                        var asset = subassets[i];
                        if (asset == null || keep.Contains(asset)) continue;
                        if (asset is AnimatorState || asset is AnimatorStateTransition || asset is AnimatorStateMachine)
                        {
                            UnityEngine.Object.DestroyImmediate(asset, true);
                        }
                    }
                }

                internal static AnimatorStateTransition AddAnimatorConditionTransition(AnimatorState source, AnimatorState destination, AnimatorConditionMode mode, float threshold, string parameter)
                {
                    var transition = source.AddTransition(destination);
                    transition.hasExitTime = false;
                    transition.exitTime = 0f;
                    transition.duration = 0.02f;
                    transition.offset = 0f;
                    transition.canTransitionToSelf = true;
                    transition.conditions = Array.Empty<AnimatorCondition>();
                    transition.AddCondition(mode, threshold, parameter);
                    return transition;
                }

                internal static void ValidateWorldAnimatorController(Animator animator, string path, string modelPath)
                {
                    var controller = animator.runtimeAnimatorController as AnimatorController;
                    var clipNames = new[] { "Idle", "Run", "Jump", "Fall", "Land", "Kick" };
                    var clips = new AnimationClip[clipNames.Length];
                    for (var i = 0; i < clipNames.Length; i++) clips[i] = FindImportedClip(modelPath, clipNames[i]);
                    if (!IsWorldAnimatorControllerExact(controller, clips, out var reason))
                    {
                        throw new InvalidOperationException("World animator controller contract invalid: " + path + "; " + reason);
                    }
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
                        transition.hasExitTime = specification.HasExitTime;
                        transition.exitTime = specification.ExitTime;
                        transition.duration = specification.Duration;
                        transition.offset = 0f;
                        transition.canTransitionToSelf = specification.CanTransitionToSelf;
                        transition.conditions = Array.Empty<AnimatorCondition>();
                        for (var conditionIndex = 0; conditionIndex < specification.Conditions.Length; conditionIndex++)
                        {
                            var condition = specification.Conditions[conditionIndex];
                            transition.AddCondition(condition.Mode, condition.Threshold, condition.Parameter);
                        }
                    }
                }

                internal static bool IsWorldAnimatorControllerExact(AnimatorController controller, AnimationClip[] clips, out string reason)
                {
                    reason = null;
                    if (controller == null)
                    {
                        reason = "controller missing";
                        return false;
                    }
                    if (clips == null || clips.Length != 6)
                    {
                        reason = "clip set incomplete";
                        return false;
                    }
                    if (controller.parameters.Length != 4)
                    {
                        reason = "parameter count";
                        return false;
                    }
                    var expectedParameters = new[] { "Speed", "Grounded", "VerticalSpeed", "Kick" };
                    var expectedTypes = new[] { AnimatorControllerParameterType.Float, AnimatorControllerParameterType.Bool, AnimatorControllerParameterType.Float, AnimatorControllerParameterType.Trigger };
                    for (var i = 0; i < expectedParameters.Length; i++)
                    {
                        var parameter = controller.parameters[i];
                        if (parameter.name != expectedParameters[i] || parameter.type != expectedTypes[i] || Mathf.Abs(parameter.defaultFloat) > 0.0001f || parameter.defaultInt != 0 || parameter.defaultBool)
                        {
                            reason = "parameter contract: " + expectedParameters[i];
                            return false;
                        }
                    }
                    if (controller.layers.Length != 1 || controller.layers[0].stateMachine == null || controller.layers[0].name != "Base Layer")
                    {
                        reason = "base layer contract";
                        return false;
                    }

                    var stateMachine = controller.layers[0].stateMachine;
                    var expectedStates = new[] { "Idle", "Run", "Jump", "Fall", "Land", "Kick" };
                    if (stateMachine.states.Length != expectedStates.Length || stateMachine.stateMachines.Length != 0 || stateMachine.entryTransitions.Length != 0 || stateMachine.anyStateTransitions.Length != 1 || stateMachine.defaultState == null || stateMachine.defaultState.name != "Idle")
                    {
                        reason = "state machine count/default";
                        return false;
                    }

                    var namedStates = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
                    var boundClips = new HashSet<AnimationClip>();
                    for (var i = 0; i < stateMachine.states.Length; i++)
                    {
                        var state = stateMachine.states[i].state;
                        if (state == null || namedStates.ContainsKey(state.name))
                        {
                            reason = "state identity";
                            return false;
                        }
                        namedStates.Add(state.name, state);
                    }
                    for (var i = 0; i < expectedStates.Length; i++)
                    {
                        if (!namedStates.TryGetValue(expectedStates[i], out var state) || state.motion != clips[i] || state.motion == null || !boundClips.Add(state.motion as AnimationClip))
                        {
                            reason = "state motion: " + expectedStates[i];
                            return false;
                        }
                    }

                    var specifications = GetWorldAnimatorTransitionSpecifications();
                    var transitionCount = stateMachine.anyStateTransitions.Length;
                    for (var i = 0; i < stateMachine.states.Length; i++) transitionCount += stateMachine.states[i].state.transitions.Length;
                    if (transitionCount != specifications.Length)
                    {
                        reason = "transition count";
                        return false;
                    }

                    for (var i = 0; i < specifications.Length; i++)
                    {
                        var specification = specifications[i];
                        var transitions = specification.AnyState ? stateMachine.anyStateTransitions : namedStates[specification.Source].transitions;
                        AnimatorStateTransition found = null;
                        for (var j = 0; j < transitions.Length; j++)
                        {
                            var candidate = transitions[j];
                            if (candidate != null && candidate.destinationState != null && candidate.destinationState.name == specification.Destination && MatchesWorldAnimatorTransition(candidate, specification))
                            {
                                if (found != null)
                                {
                                    reason = "duplicate transition: " + specification.Source + " -> " + specification.Destination;
                                    return false;
                                }
                                found = candidate;
                            }
                        }
                        if (found == null)
                        {
                            reason = "transition semantics: " + specification.Source + " -> " + specification.Destination;
                            return false;
                        }
                    }

                    var subassets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller));
                    var stateCount = 0;
                    var transitionSubassetCount = 0;
                    for (var i = 0; i < subassets.Length; i++)
                    {
                        if (subassets[i] is AnimatorState) stateCount++;
                        if (subassets[i] is AnimatorStateTransition) transitionSubassetCount++;
                    }
                    if (stateCount != 6 || transitionSubassetCount != 19)
                    {
                        reason = "subasset count";
                        return false;
                    }
                    return true;
                }

                internal static bool MatchesWorldAnimatorTransition(AnimatorStateTransition transition, WorldAnimatorTransitionSpecification specification)
                {
                    if (transition.destinationStateMachine != null || transition.isExit || transition.hasExitTime != specification.HasExitTime || Mathf.Abs(transition.exitTime - specification.ExitTime) > 0.0001f || Mathf.Abs(transition.duration - specification.Duration) > 0.0001f || Mathf.Abs(transition.offset) > 0.0001f || transition.canTransitionToSelf != specification.CanTransitionToSelf || !transition.hasFixedDuration || transition.mute || transition.solo || transition.interruptionSource != TransitionInterruptionSource.None || !transition.orderedInterruption)
                    {
                        return false;
                    }
                    var conditions = transition.conditions;
                    if (conditions == null || conditions.Length != specification.Conditions.Length)
                    {
                        return false;
                    }
                    for (var i = 0; i < conditions.Length; i++)
                    {
                        var expected = specification.Conditions[i];
                        if (conditions[i].mode != expected.Mode || conditions[i].parameter != expected.Parameter || Mathf.Abs(conditions[i].threshold - expected.Threshold) > 0.0001f)
                        {
                            return false;
                        }
                    }
                    return true;
                }

    }
}
