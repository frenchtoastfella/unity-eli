using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class AddEventListenerTool : IEliTool
    {
        public string Name => "add_event_listener";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required.");
            if (string.IsNullOrWhiteSpace(input.event_name))
                return ToolResult.Error("event_name is required.");
            if (string.IsNullOrWhiteSpace(input.target_game_object))
                return ToolResult.Error("target_game_object is required.");
            if (string.IsNullOrWhiteSpace(input.target_component_type))
                return ToolResult.Error("target_component_type is required.");
            if (string.IsNullOrWhiteSpace(input.method_name))
                return ToolResult.Error("method_name is required.");

            // Find source GameObject
            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found.");

            // Find the component containing the event
            Component eventComponent;
            if (!string.IsNullOrWhiteSpace(input.component_type))
            {
                var compType = EliToolHelpers.ResolveType(input.component_type);
                if (compType == null)
                    return ToolResult.Error($"Component type '{input.component_type}' not found.");
                eventComponent = go.GetComponent(compType);
                if (eventComponent == null)
                    return ToolResult.Error($"'{input.game_object}' does not have a '{input.component_type}' component.");
            }
            else
            {
                eventComponent = FindComponentWithEvent(go, input.event_name);
                if (eventComponent == null)
                    return ToolResult.Error($"No component on '{input.game_object}' has an event named '{input.event_name}'. Specify component_type.");
            }

            var so = new SerializedObject(eventComponent);

            // Find the event property (e.g. "onClick" -> "m_OnClick")
            var eventProp = EliToolHelpers.FindProperty(so, input.event_name);
            if (eventProp == null)
                return ToolResult.Error($"Event '{input.event_name}' not found on '{eventComponent.GetType().Name}'. Available: {EliToolHelpers.ListProperties(so)}");

            // Navigate to m_PersistentCalls.m_Calls
            var callsProp = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (callsProp == null || !callsProp.isArray)
                return ToolResult.Error($"'{input.event_name}' does not appear to be a UnityEvent (no m_PersistentCalls.m_Calls found).");

            // Find target GameObject and component
            var targetGo = EliToolHelpers.FindGameObject(input.target_game_object);
            if (targetGo == null)
                return ToolResult.Error($"Target GameObject '{input.target_game_object}' not found.");

            var targetCompType = EliToolHelpers.ResolveType(input.target_component_type);
            if (targetCompType == null)
                return ToolResult.Error($"Target component type '{input.target_component_type}' not found.");

            var targetComp = targetGo.GetComponent(targetCompType);
            if (targetComp == null)
                return ToolResult.Error($"'{input.target_game_object}' does not have a '{input.target_component_type}' component.");

            // Add new persistent call entry
            Undo.RecordObject(eventComponent, $"Unity Eli: Add event listener on {input.game_object}");
            so.Update();

            callsProp.arraySize++;
            var entry = callsProp.GetArrayElementAtIndex(callsProp.arraySize - 1);

            // m_Target — the object to call the method on
            var targetProp = entry.FindPropertyRelative("m_Target");
            targetProp.objectReferenceValue = targetComp;

            // m_TargetAssemblyTypeName
            var targetTypeProp = entry.FindPropertyRelative("m_TargetAssemblyTypeName");
            if (targetTypeProp != null)
                targetTypeProp.stringValue = targetCompType.AssemblyQualifiedName;

            // m_MethodName
            var methodProp = entry.FindPropertyRelative("m_MethodName");
            methodProp.stringValue = input.method_name;

            // m_CallState — 2 = RuntimeOnly (standard for persistent listeners added in editor)
            var callStateProp = entry.FindPropertyRelative("m_CallState");
            callStateProp.enumValueIndex = 2;

            // m_Mode and m_Arguments — depends on whether an argument is provided
            var modeProp = entry.FindPropertyRelative("m_Mode");
            var argsProp = entry.FindPropertyRelative("m_Arguments");

            if (string.IsNullOrWhiteSpace(input.argument))
            {
                modeProp.enumValueIndex = 1; // Void — no argument
            }
            else
            {
                SetArgument(modeProp, argsProp, input.argument);
            }

            so.ApplyModifiedProperties();

            return ToolResult.Success(
                $"Added persistent listener: {eventComponent.GetType().Name}.{input.event_name} -> " +
                $"{input.target_component_type}.{input.method_name}() on '{input.target_game_object}'.");
        }

        /// <summary>
        /// Searches all components on a GameObject for one that has a UnityEvent with the given name.
        /// </summary>
        private static Component FindComponentWithEvent(GameObject go, string eventName)
        {
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                var so = new SerializedObject(comp);
                var prop = EliToolHelpers.FindProperty(so, eventName);
                if (prop == null) continue;

                var calls = prop.FindPropertyRelative("m_PersistentCalls.m_Calls");
                if (calls != null && calls.isArray)
                    return comp;
            }
            return null;
        }

        /// <summary>
        /// Determines the argument type and sets the appropriate mode + argument fields.
        /// UnityEvent modes: 0=EventDefined, 1=Void, 2=Object, 3=Int, 4=Float, 5=String, 6=Bool
        /// </summary>
        private static void SetArgument(SerializedProperty modeProp, SerializedProperty argsProp, string argument)
        {
            // Try bool
            if (string.Equals(argument, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "false", StringComparison.OrdinalIgnoreCase))
            {
                modeProp.enumValueIndex = 6; // Bool
                argsProp.FindPropertyRelative("m_BoolArgument").boolValue =
                    string.Equals(argument, "true", StringComparison.OrdinalIgnoreCase);
                return;
            }

            // Try int
            if (int.TryParse(argument, out var intVal))
            {
                modeProp.enumValueIndex = 3; // Int
                argsProp.FindPropertyRelative("m_IntArgument").intValue = intVal;
                return;
            }

            // Try float (must contain '.' to distinguish from int)
            if (argument.Contains(".") && float.TryParse(argument,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var floatVal))
            {
                modeProp.enumValueIndex = 4; // Float
                argsProp.FindPropertyRelative("m_FloatArgument").floatValue = floatVal;
                return;
            }

            // Default: string
            modeProp.enumValueIndex = 5; // String
            argsProp.FindPropertyRelative("m_StringArgument").stringValue = argument;
        }

        [Serializable]
        private class Input
        {
            public string game_object;
            public string component_type;
            public string event_name;
            public string target_game_object;
            public string target_component_type;
            public string method_name;
            public string argument;
        }
    }
}
