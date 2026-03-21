using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class CreateAnimatorControllerTool : IEliTool
    {
        public string Name => "create_animator_controller";
        public bool NeedsAssetRefresh => true;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.path))
                return ToolResult.Error("path is required (e.g. 'Assets/Animations/Player.controller').");

            if (!input.path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                input.path += ".controller";

            // Ensure directory exists
            var dir = Path.GetDirectoryName(input.path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(input.path);
            if (controller == null)
                return ToolResult.Error($"Failed to create AnimatorController at '{input.path}'.");

            var stateMachine = controller.layers[0].stateMachine;
            var addedParams = 0;
            var addedStates = 0;

            // Add parameters
            if (!string.IsNullOrWhiteSpace(input.parameters))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<ParamWrapper>("{\"items\":" + input.parameters + "}");
                    if (wrapper?.items != null)
                    {
                        foreach (var p in wrapper.items)
                        {
                            if (string.IsNullOrWhiteSpace(p.name)) continue;
                            var paramType = ResolveParamType(p.type);
                            controller.AddParameter(p.name, paramType);
                            addedParams++;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Unity Eli] Failed to parse parameters: {e.Message}");
                }
            }

            // Add states
            if (!string.IsNullOrWhiteSpace(input.states))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<StateWrapper>("{\"items\":" + input.states + "}");
                    if (wrapper?.items != null)
                    {
                        foreach (var s in wrapper.items)
                        {
                            if (string.IsNullOrWhiteSpace(s.name)) continue;
                            stateMachine.AddState(s.name);
                            addedStates++;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Unity Eli] Failed to parse states: {e.Message}");
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return ToolResult.Success(
                $"Created AnimatorController at '{input.path}' with {addedParams} parameter(s) and {addedStates} state(s) (plus default Entry/Exit/Any State).");
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

        [Serializable] private class ParamWrapper { public ParamData[] items; }
        [Serializable] private class ParamData { public string name; public string type; }
        [Serializable] private class StateWrapper { public StateData[] items; }
        [Serializable] private class StateData { public string name; }

        [Serializable]
        private class Input
        {
            public string path;
            public string parameters;
            public string states;
        }
    }
}
