using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds the fighter Animator Controller graph from code so the state layout, parameters and
/// transitions stay reproducible and reviewable instead of living only inside a binary asset.
/// Attack states are derived from the project's <see cref="AttackData"/> assets, which keeps the
/// graph and the frame data in sync. Attack, movement and reaction states have no entry
/// transitions: gameplay enters them with a direct cross-fade (see RobotState), so the graph only
/// owns the locomotion blend, the crouch toggle and the return paths once a one-shot clip ends. Re-running the menu item rebuilds the base layer in place
/// while preserving the asset GUID, so scene and prefab references are never broken.
/// </summary>
public static class AnimatorAutoSetup
{
    public const string ControllerPath = "Assets/_Project/Art/Animations/Robot_Animator.controller";

    private const string ClipSearchRoot = "Assets/_Project/Art";
    private const string PackClipSearchRoot = FighterModelImportSetup.PackAnimationsRoot;
    private const string AttackDataSearchRoot = "Assets/_Project/Attacks";
    private const string CrouchStateName = "Crouch";
    private const string JumpStateName = "Jump";

    private const float LocomotionTransitionDuration = 0.1f;
    private const float ReturnTransitionDuration = 0.1f;
    private const float ReturnExitTime = 0.9f;
    private const float JumpMinimumExitTime = 0.25f;
    private const float RowSpacing = 70f;

    private static readonly Vector3 LocomotionPosition = new Vector3(300f, 200f, 0f);
    private static readonly Vector3 CrouchPosition = new Vector3(300f, 320f, 0f);
    private static readonly Vector3 MovementColumnOrigin = new Vector3(700f, -100f, 0f);
    private static readonly Vector3 AttackColumnOrigin = new Vector3(700f, 150f, 0f);
    private static readonly Vector3 ReactionColumnOrigin = new Vector3(700f, 800f, 0f);

    private enum SpeedSource
    {
        None,
        Attack,
        Timed
    }

    private enum ExitRule
    {
        ReturnByExitTime,
        ReturnWhenGrounded,
        Terminal
    }

    /// <summary>
    /// A one-shot state entered by cross-fade from gameplay code. The name doubles as the state
    /// name; clip candidates are matched in order against the indexed animation assets when no
    /// explicit clip is provided.
    /// </summary>
    private readonly struct TriggerStateDefinition
    {
        public readonly string Trigger;
        public readonly AnimationClip ExplicitClip;
        public readonly string[] ClipCandidates;
        public readonly SpeedSource Speed;
        public readonly ExitRule Exit;

        public TriggerStateDefinition(string trigger, AnimationClip explicitClip, string[] clipCandidates, SpeedSource speed, ExitRule exit)
        {
            Trigger = trigger;
            ExplicitClip = explicitClip;
            ClipCandidates = clipCandidates;
            Speed = speed;
            Exit = exit;
        }
    }

    private static readonly string[] IdleClipCandidates = { "Fight_Idle", "Fighting Idle", "Bouncing Fight Idle", "Idle" };
    private static readonly string[] WalkForwardClipCandidates = { "Walk_Forward", "Walking", "WalkForward", "Walk" };
    private static readonly string[] WalkBackwardClipCandidates = { "Walk_Backward", "Walking Backward", "Walking Backwards", "WalkBackward" };
    private static readonly string[] CrouchClipCandidates = { "Crouch_Idle", "Crouching Idle", "Crouch" };
    private static readonly string[] JumpClipCandidates = { "Jump", "Jumping Up", "Jumping" };
    private static readonly string[] DashForwardClipCandidates = { "Dash_Forward", "Step Forward", "Dodge Forward" };
    private static readonly string[] DashBackwardClipCandidates = { "Dash_Backward", "Step Backward", "Dodge Backward" };
    private static readonly string[] HitstunClipCandidates = { "Hit_Head", "Head Hit", "Hit_Body", "Hit Reaction" };
    private static readonly string[] BlockstunClipCandidates = { "Block_Hit", "Blocking", "Block" };
    private static readonly string[] DeathClipCandidates = { "Knockout", "Knocked Out", "Death", "Dying" };

    /// <summary>
    /// Fallback clip names per attack trigger, used only when the AttackData asset has no clip assigned.
    /// Names follow the martial-arts library under Art/Animations/Combat, with generic Mixamo catalogue
    /// names as a second chance. The resolved clip is written back into the asset so it becomes the
    /// explicit source of truth.
    /// </summary>
    private static readonly Dictionary<string, string[]> AttackClipCandidates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        { "LeftPunch", new[] { "Jab", "Punching" } },
        { "RightPunch", new[] { "Cross_Punch", "Cross Punch" } },
        { "LeftKick", new[] { "Front_Kick", "Kicking" } },
        { "RightKick", new[] { "Roundhouse_Kick", "Roundhouse Kick", "Mma Kick" } },
        { "CrouchPunch", new[] { "Body_Hook", "Hook" } },
        { "CrouchSweep", new[] { "Leg_Sweep", "Leg Sweep" } },
        { "Launcher", new[] { "Launcher_Hook", "Hook Punch", "Uppercut" } },
        { "Hadouken", new[] { "Hadouken", "Fireball" } },
        { "Shoryuken", new[] { "Uppercut", "Shoryuken" } },
        { "Overhead", new[] { "Cross_Punch", "Cross Punch" } },
        { "AntiAir", new[] { "Uppercut", "Shoryuken" } },
        { "AirLeftPunch", new[] { "Jab", "Punching" } },
        { "AirRightPunch", new[] { "Cross_Punch", "Cross Punch" } },
        { "AirLeftKick", new[] { "Front_Kick", "Kicking" } },
        { "AirRightKick", new[] { "Roundhouse_Kick", "Roundhouse Kick", "Mma Kick" } }
    };

    [MenuItem("Fighting Game/Setup Animator Controller", priority = 20)]
    public static void SetupAnimatorController()
    {
        AnimatorController controller = LoadOrCreateController();
        Dictionary<string, AnimationClip> clipLibrary = BuildClipLibrary();

        List<TriggerStateDefinition> attackStates = BuildAttackStateDefinitions(clipLibrary);
        List<TriggerStateDefinition> movementStates = BuildMovementStateDefinitions();
        List<TriggerStateDefinition> reactionStates = BuildReactionStateDefinitions();

        EnsureParameters(controller);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        ClearStateMachine(stateMachine);

        AnimatorState locomotion = CreateLocomotionState(controller, stateMachine, clipLibrary);
        AnimatorState crouch = CreateCrouchState(stateMachine, locomotion, clipLibrary);
        stateMachine.defaultState = locomotion;

        CreateTriggerStates(stateMachine, locomotion, crouch, movementStates, MovementColumnOrigin, clipLibrary);
        CreateTriggerStates(stateMachine, locomotion, crouch, attackStates, AttackColumnOrigin, clipLibrary);
        CreateTriggerStates(stateMachine, locomotion, crouch, reactionStates, ReactionColumnOrigin, clipLibrary);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AnimatorAutoSetup] Rebuilt '{ControllerPath}' with {stateMachine.states.Length} states; {clipLibrary.Count} clips indexed under '{ClipSearchRoot}' and '{PackClipSearchRoot}'.");
    }

    /// <summary>
    /// Returns the clip assigned to a named state of the generated controller, or null when the
    /// state does not exist or has no motion. Used by scene tooling to wire timed clips into fighters.
    /// </summary>
    public static AnimationClip GetStateClip(AnimatorController controller, string stateName)
    {
        if (controller == null || controller.layers.Length == 0) return null;

        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            if (child.state.name == stateName) return child.state.motion as AnimationClip;
        }

        return null;
    }

    private static AnimatorController LoadOrCreateController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller != null) return controller;

        string directory = Path.GetDirectoryName(ControllerPath);
        if (!AssetDatabase.IsValidFolder(directory))
        {
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
        }

        return AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
    }

    private static List<TriggerStateDefinition> BuildAttackStateDefinitions(Dictionary<string, AnimationClip> clipLibrary)
    {
        var definitions = new List<TriggerStateDefinition>();
        var seenTriggers = new HashSet<string>(StringComparer.Ordinal);
        string[] guids = AssetDatabase.FindAssets("t:AttackData", new[] { AttackDataSearchRoot });

        foreach (string guid in guids)
        {
            AttackData data = AssetDatabase.LoadAssetAtPath<AttackData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null || string.IsNullOrEmpty(data.animationTrigger)) continue;

            if (data.animationClip == null && AttackClipCandidates.TryGetValue(data.animationTrigger, out string[] candidates))
            {
                data.animationClip = ResolveClip(clipLibrary, candidates, null);
                EditorUtility.SetDirty(data);
            }

            if (!seenTriggers.Add(data.animationTrigger)) continue;

            definitions.Add(new TriggerStateDefinition(
                data.animationTrigger, data.animationClip, Array.Empty<string>(),
                SpeedSource.Attack, ExitRule.ReturnByExitTime));
        }

        definitions.Sort((a, b) => string.CompareOrdinal(a.Trigger, b.Trigger));
        return definitions;
    }

    private static List<TriggerStateDefinition> BuildMovementStateDefinitions()
    {
        return new List<TriggerStateDefinition>
        {
            new TriggerStateDefinition(FighterAnimatorParameters.JumpTrigger, null, JumpClipCandidates, SpeedSource.None, ExitRule.ReturnWhenGrounded),
            new TriggerStateDefinition(FighterAnimatorParameters.DashForwardTrigger, null, DashForwardClipCandidates, SpeedSource.Timed, ExitRule.ReturnByExitTime),
            new TriggerStateDefinition(FighterAnimatorParameters.DashBackwardTrigger, null, DashBackwardClipCandidates, SpeedSource.Timed, ExitRule.ReturnByExitTime)
        };
    }

    private static List<TriggerStateDefinition> BuildReactionStateDefinitions()
    {
        return new List<TriggerStateDefinition>
        {
            new TriggerStateDefinition(FighterAnimatorParameters.HitstunTrigger, null, HitstunClipCandidates, SpeedSource.Timed, ExitRule.ReturnByExitTime),
            new TriggerStateDefinition(FighterAnimatorParameters.BlockstunTrigger, null, BlockstunClipCandidates, SpeedSource.Timed, ExitRule.ReturnByExitTime),
            new TriggerStateDefinition(FighterAnimatorParameters.DeathTrigger, null, DeathClipCandidates, SpeedSource.None, ExitRule.Terminal)
        };
    }

    private static readonly string[] ParameterNames =
    {
        FighterAnimatorParameters.InputXName,
        FighterAnimatorParameters.InputYName,
        FighterAnimatorParameters.AttackSpeedName,
        FighterAnimatorParameters.TimedSpeedName,
        FighterAnimatorParameters.IsGroundedName,
        FighterAnimatorParameters.IsCrouchingName
    };

    private static void EnsureParameters(AnimatorController controller)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = parameters.Length - 1; i >= 0; i--)
        {
            if (Array.IndexOf(ParameterNames, parameters[i].name) < 0) controller.RemoveParameter(i);
        }

        EnsureParameter(controller, FighterAnimatorParameters.InputXName, AnimatorControllerParameterType.Float, 0f);
        EnsureParameter(controller, FighterAnimatorParameters.InputYName, AnimatorControllerParameterType.Float, 0f);
        EnsureParameter(controller, FighterAnimatorParameters.AttackSpeedName, AnimatorControllerParameterType.Float, 1f);
        EnsureParameter(controller, FighterAnimatorParameters.TimedSpeedName, AnimatorControllerParameterType.Float, 1f);
        EnsureParameter(controller, FighterAnimatorParameters.IsGroundedName, AnimatorControllerParameterType.Bool, 0f);
        EnsureParameter(controller, FighterAnimatorParameters.IsCrouchingName, AnimatorControllerParameterType.Bool, 0f);
    }

    private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type, float defaultFloat)
    {
        AnimatorControllerParameter[] parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name != name) continue;
            if (parameters[i].type == type && Mathf.Approximately(parameters[i].defaultFloat, defaultFloat)) return;

            controller.RemoveParameter(i);
            break;
        }

        controller.AddParameter(new AnimatorControllerParameter
        {
            name = name,
            type = type,
            defaultFloat = defaultFloat
        });
    }

    private static void ClearStateMachine(AnimatorStateMachine stateMachine)
    {
        ChildAnimatorStateMachine[] subMachines = stateMachine.stateMachines;
        for (int i = subMachines.Length - 1; i >= 0; i--)
            stateMachine.RemoveStateMachine(subMachines[i].stateMachine);

        ChildAnimatorState[] states = stateMachine.states;
        for (int i = states.Length - 1; i >= 0; i--)
            stateMachine.RemoveState(states[i].state);

        AnimatorStateTransition[] anyStateTransitions = stateMachine.anyStateTransitions;
        for (int i = anyStateTransitions.Length - 1; i >= 0; i--)
            stateMachine.RemoveAnyStateTransition(anyStateTransitions[i]);
    }

    /// <summary>
    /// 1D locomotion blend on InputY: -1 walks away from the opponent, 0 holds the guard idle and
    /// +1 advances. A single axis keeps the step cycles blending continuously with no clip pops.
    /// </summary>
    private static AnimatorState CreateLocomotionState(AnimatorController controller, AnimatorStateMachine stateMachine, Dictionary<string, AnimationClip> clipLibrary)
    {
        BlendTree tree = new BlendTree
        {
            name = FighterAnimatorParameters.LocomotionStateName,
            blendType = BlendTreeType.Simple1D,
            blendParameter = FighterAnimatorParameters.InputYName,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);

        AnimationClip idle = ResolveClip(clipLibrary, IdleClipCandidates, null);
        tree.AddChild(ResolveClip(clipLibrary, WalkBackwardClipCandidates, idle), -1f);
        tree.AddChild(idle, 0f);
        tree.AddChild(ResolveClip(clipLibrary, WalkForwardClipCandidates, idle), 1f);

        AnimatorState state = stateMachine.AddState(FighterAnimatorParameters.LocomotionStateName, LocomotionPosition);
        state.motion = tree;
        state.writeDefaultValues = false;
        return state;
    }

    private static AnimatorState CreateCrouchState(AnimatorStateMachine stateMachine, AnimatorState locomotion, Dictionary<string, AnimationClip> clipLibrary)
    {
        AnimatorState crouch = stateMachine.AddState(CrouchStateName, CrouchPosition);
        crouch.motion = ResolveClip(clipLibrary, CrouchClipCandidates, locomotion.motion is BlendTree tree && tree.children.Length > 0 ? tree.children[0].motion as AnimationClip : null);
        crouch.writeDefaultValues = false;

        AnimatorStateTransition enterCrouch = locomotion.AddTransition(crouch);
        enterCrouch.hasExitTime = false;
        enterCrouch.hasFixedDuration = true;
        enterCrouch.duration = LocomotionTransitionDuration;
        enterCrouch.AddCondition(AnimatorConditionMode.If, 0f, FighterAnimatorParameters.IsCrouchingName);

        AnimatorStateTransition standUp = crouch.AddTransition(locomotion);
        standUp.hasExitTime = false;
        standUp.hasFixedDuration = true;
        standUp.duration = LocomotionTransitionDuration;
        standUp.AddCondition(AnimatorConditionMode.IfNot, 0f, FighterAnimatorParameters.IsCrouchingName);

        return crouch;
    }

    private static void CreateTriggerStates(
        AnimatorStateMachine stateMachine,
        AnimatorState locomotion,
        AnimatorState crouch,
        List<TriggerStateDefinition> definitions,
        Vector3 columnOrigin,
        Dictionary<string, AnimationClip> clipLibrary)
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            TriggerStateDefinition definition = definitions[i];
            Vector3 position = columnOrigin + Vector3.up * (RowSpacing * i);

            AnimatorState state = stateMachine.AddState(definition.Trigger, position);
            state.motion = definition.ExplicitClip != null ? definition.ExplicitClip : ResolveClip(clipLibrary, definition.ClipCandidates, null);
            state.writeDefaultValues = false;
            ApplySpeedSource(state, definition.Speed);

            switch (definition.Exit)
            {
                case ExitRule.ReturnByExitTime:
                    AddReturnTransition(state, crouch, AnimatorConditionMode.If, ReturnExitTime);
                    AddReturnTransition(state, locomotion, AnimatorConditionMode.IfNot, ReturnExitTime);
                    break;

                case ExitRule.ReturnWhenGrounded:
                    AnimatorStateTransition landing = AddReturnTransition(state, locomotion, AnimatorConditionMode.IfNot, JumpMinimumExitTime);
                    landing.AddCondition(AnimatorConditionMode.If, 0f, FighterAnimatorParameters.IsGroundedName);
                    break;
            }
        }
    }

    private static AnimatorStateTransition AddReturnTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode crouchCondition, float exitTime)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.hasFixedDuration = true;
        transition.duration = ReturnTransitionDuration;
        transition.interruptionSource = TransitionInterruptionSource.Source;
        transition.orderedInterruption = false;
        transition.AddCondition(crouchCondition, 0f, FighterAnimatorParameters.IsCrouchingName);
        return transition;
    }

    private static void ApplySpeedSource(AnimatorState state, SpeedSource source)
    {
        switch (source)
        {
            case SpeedSource.Attack:
                state.speedParameterActive = true;
                state.speedParameter = FighterAnimatorParameters.AttackSpeedName;
                break;
            case SpeedSource.Timed:
                state.speedParameterActive = true;
                state.speedParameter = FighterAnimatorParameters.TimedSpeedName;
                break;
            default:
                state.speedParameterActive = false;
                break;
        }
    }

    /// <summary>
    /// Indexes every AnimationClip under the art root, including clips embedded in FBX files.
    /// Standalone .anim assets take priority over embedded ones so a hand-tuned clip always wins.
    /// </summary>
    private static Dictionary<string, AnimationClip> BuildClipLibrary()
    {
        var library = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipSearchRoot, PackClipSearchRoot });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            bool isStandalone = path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase);

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is not AnimationClip clip) continue;
                if (clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;

                if (isStandalone || !library.ContainsKey(clip.name))
                    library[clip.name] = clip;
            }
        }

        return library;
    }

    private static AnimationClip ResolveClip(Dictionary<string, AnimationClip> library, string[] candidates, AnimationClip fallback, bool warnIfMissing = true)
    {
        foreach (string candidate in candidates)
        {
            if (library.TryGetValue(candidate, out AnimationClip exact)) return exact;
        }

        foreach (string candidate in candidates)
        {
            foreach (KeyValuePair<string, AnimationClip> entry in library)
            {
                if (entry.Key.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0) return entry.Value;
            }
        }

        if (warnIfMissing)
        {
            string outcome = fallback != null ? $"falling back to '{fallback.name}'" : "state left without motion";
            Debug.LogWarning($"[AnimatorAutoSetup] No clip found for candidates [{string.Join(", ", candidates)}]; {outcome}.");
        }

        return fallback;
    }
}
