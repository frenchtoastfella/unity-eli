using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class ConfigureAnimatorControllerTool : IEliTool
    {
        public string Name => "configure_animator_controller";
        public bool NeedsAssetRefresh => true;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.path))
                return ToolResult.Error("path is required.");
            if (string.IsNullOrWhiteSpace(input.action))
                return ToolResult.Error("action is required: add_state, remove_state, add_parameter, remove_parameter, add_transition.");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(input.path);
            if (controller == null)
                return ToolResult.Error($"AnimatorController not found at '{input.path}'.");

            string result;
            switch (input.action.ToLowerInvariant())
            {
                case "add_state":     result = AddState(controller, input); break;
                case "remove_state":  result = RemoveState(controller, input); break;
                case "add_parameter": result = AddParameter(controller, input); break;
                case "remove_parameter": result = RemoveParameter(controller, input); break;
                case "add_transition": result = AddTransition(controller, input); break;
                default:
                    return ToolResult.Error(
                        $"Unknown action '{input.action}'. Valid: add_state, remove_state, add_parameter, remove_parameter, add_transition.");
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return result;
        }

        private static string AddState(AnimatorController controller, Input input)
        {
            if (string.IsNullOrWhiteSpace(input.state_name))
                return ToolResult.Error("state_name is required for add_state.");

            var sm = controller.layers[0].stateMachine;
            if (sm.states.Any(s => s.state.name == input.state_name))
                return ToolResult.Success($"State '{input.state_name}' already exists.");

            sm.AddState(input.state_name);
            return ToolResult.Success($"Added state '{input.state_name}' to '{controller.name}'.");
        }

        private static string RemoveState(AnimatorController controller, Input input)
        {
            if (string.IsNullOrWhiteSpace(input.state_name))
                return ToolResult.Error("state_name is required for remove_state.");

            var sm = controller.layers[0].stateMachine;
            var stateMatch = sm.states.FirstOrDefault(s => s.state.name == input.state_name);
            if (stateMatch.state == null)
                return ToolResult.Error($"State '{input.state_name}' not found.");

            sm.RemoveState(stateMatch.state);
            return ToolResult.Success($"Removed state '{input.state_name}' from '{controller.name}'.");
        }

        private static string AddParameter(AnimatorController controller, Input input)
        {
            if (string.IsNullOrWhiteSpace(input.parameter_name))
                return ToolResult.Error("parameter_name is required for add_parameter.");

            if (controller.parameters.Any(p => p.name == input.parameter_name))
                return ToolResult.Success($"Parameter '{input.parameter_name}' already exists.");

            var paramType = ResolveParamType(input.parameter_type);
            controller.AddParameter(input.parameter_name, paramType);
            return ToolResult.Success($"Added {paramType} parameter '{input.parameter_name}' to '{controller.name}'.");
        }

        private static string RemoveParameter(AnimatorController controller, Input input)
        {
            if (string.IsNullOrWhiteSpace(input.parameter_name))
                return ToolResult.Error("parameter_name is required for remove_parameter.");

            var idx = -1;
            for (int i = 0; i < controller.parameters.Length; i++)
            {
                if (controller.parameters[i].name == input.parameter_name)
                { idx = i; break; }
            }
            if (idx < 0)
                return ToolResult.Error($"Parameter '{input.parameter_name}' not found.");

            controller.RemoveParameter(idx);
            return ToolResult.Success($"Removed parameter '{input.parameter_name}' from '{controller.name}'.");
        }

        private static string AddTransition(AnimatorController controller, Input input)
        {
            if (string.IsNullOrWhiteSpace(input.state_name))
                return ToolResult.Error("state_name (source state) is required for add_transition.");
            if (string.IsNullOrWhiteSpace(input.target_state))
                return ToolResult.Error("target_state is required for add_transition.");

            var sm = controller.layers[0].stateMachine;
            var srcMatch = sm.states.FirstOrDefault(s => s.state.name == input.state_name);
            if (srcMatch.state == null)
                return ToolResult.Error($"Source state '{input.state_name}' not found.");

            var dstMatch = sm.states.FirstOrDefault(s => s.state.name == input.target_state);
            if (dstMatch.state == null)
                return ToolResult.Error($"Target state '{input.target_state}' not found.");

            var transition = srcMatch.state.AddTransition(dstMatch.state);
            transition.hasExitTime = input.has_exit_time;

            // Add condition if specified
            if (!string.IsNullOrWhiteSpace(input.condition_parameter))
            {
                if (!Enum.TryParse<AnimatorConditionMode>(input.condition_mode, true, out var condMode))
                    condMode = AnimatorConditionMode.If;
                transition.AddCondition(condMode, input.condition_threshold, input.condition_parameter);
                return ToolResult.Success(
                    $"Added transition '{input.state_name}' → '{input.target_state}' with condition {input.condition_parameter} {condMode} {input.condition_threshold}.");
            }

            return ToolResult.Success(
                $"Added transition '{input.state_name}' → '{input.target_state}' (no condition).");
        }

        private static AnimatorControllerParameterType ResolveParamType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return AnimatorControllerParameterType.Float;
            switch (type.ToLowerInvariant())
            {
                case "int":     return AnimatorControllerParameterType.Int;
                case "bool":    return AnimatorControllerParameterType.Bool;
                case "trigger": return AnimatorControllerParameterType.Trigger;
                default:        return AnimatorControllerParameterType.Float;
            }
        }

        [Serializable]
        private class Input
        {
            public string path;
            public string action;
            public string state_name;
            public string target_state;
            public string parameter_name;
            public string parameter_type;
            public string condition_parameter;
            public string condition_mode;
            public float condition_threshold;
            public bool has_exit_time;
        }
    }
}
