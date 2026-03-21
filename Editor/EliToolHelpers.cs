using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor
{
    /// <summary>
    /// Shared utilities used across all IEliTool implementations.
    /// Centralises the FindGameObject / ResolveType / ParseVector / TrySetPropertyValue
    /// logic that was previously duplicated in ~10 tool files.
    /// </summary>
    internal static class EliToolHelpers
    {
        // ── GameObject / Type lookup ─────────────────────────────────────────

        public static GameObject FindGameObject(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) return go;
            // Also find inactive objects
            return Resources.FindObjectsOfTypeAll<GameObject>()
                .FirstOrDefault(o => o.name == name && o.scene.isLoaded);
        }

        public static Type ResolveType(string typeName)
        {
            string[] unityPrefixes = { "UnityEngine.", "UnityEngine.UI.", "TMPro." };
            string[] unityAssemblies =
            {
                "UnityEngine.CoreModule", "UnityEngine.PhysicsModule",
                "UnityEngine.Physics2DModule", "UnityEngine.AudioModule",
                "UnityEngine.AnimationModule", "UnityEngine.UIModule",
                "UnityEngine.UI", "Unity.TextMeshPro"
            };

            foreach (var prefix in unityPrefixes)
            {
                foreach (var asm in unityAssemblies)
                {
                    var t = Type.GetType($"{prefix}{typeName}, {asm}");
                    if (t != null) return t;
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in assembly.GetTypes())
                        if (t.Name == typeName) return t;
                }
                catch { }
            }

            return null;
        }

        // ── Vector parsing ───────────────────────────────────────────────────

        /// <summary>
        /// Parses "1,2,3", "(1, 2, 3)", or "1 2 3" into a float[].
        /// Returns null on failure or component count mismatch.
        /// </summary>
        public static float[] ParseVector(string value, int expectedComponents)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var cleaned = value.Trim().TrimStart('(', '[').TrimEnd(')', ']');
            var parts = cleaned.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != expectedComponents) return null;

            var result = new float[expectedComponents];
            for (int i = 0; i < expectedComponents; i++)
            {
                if (!float.TryParse(parts[i].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out result[i]))
                    return null;
            }
            return result;
        }

        // ── SerializedProperty setter ────────────────────────────────────────

        /// <summary>
        /// Attempts to set a SerializedProperty from a string value.
        /// Returns null on success, or an error message on failure.
        /// Outputs a human-readable description of the value that was set.
        /// </summary>
        public static string TrySetPropertyValue(SerializedProperty property, string value, out string resultDesc)
        {
            resultDesc = value;

            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    if (bool.TryParse(value, out var boolVal))
                    { property.boolValue = boolVal; resultDesc = boolVal.ToString(); return null; }
                    if (value == "0") { property.boolValue = false; resultDesc = "False"; return null; }
                    if (value == "1") { property.boolValue = true; resultDesc = "True"; return null; }
                    return $"Cannot parse '{value}' as boolean. Use 'true' or 'false'.";

                case SerializedPropertyType.Integer:
                    if (int.TryParse(value, out var intVal))
                    { property.intValue = intVal; resultDesc = intVal.ToString(); return null; }
                    return $"Cannot parse '{value}' as integer.";

                case SerializedPropertyType.Float:
                    if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var floatVal))
                    { property.floatValue = floatVal; resultDesc = floatVal.ToString("G"); return null; }
                    return $"Cannot parse '{value}' as float.";

                case SerializedPropertyType.String:
                    property.stringValue = value;
                    resultDesc = $"\"{value}\"";
                    return null;

                case SerializedPropertyType.Enum:
                    for (int i = 0; i < property.enumNames.Length; i++)
                    {
                        if (string.Equals(property.enumNames[i], value, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(property.enumDisplayNames[i], value, StringComparison.OrdinalIgnoreCase))
                        { property.enumValueIndex = i; resultDesc = property.enumDisplayNames[i]; return null; }
                    }
                    if (int.TryParse(value, out var enumIdx) && enumIdx >= 0 && enumIdx < property.enumNames.Length)
                    { property.enumValueIndex = enumIdx; resultDesc = property.enumDisplayNames[enumIdx]; return null; }
                    return $"Cannot parse '{value}' as enum. Valid values: {string.Join(", ", property.enumNames)}.";

                case SerializedPropertyType.Vector2:
                    var v2 = ParseVector(value, 2);
                    if (v2 != null)
                    { property.vector2Value = new Vector2(v2[0], v2[1]); resultDesc = $"({v2[0]}, {v2[1]})"; return null; }
                    return $"Cannot parse '{value}' as Vector2. Use 'x,y'.";

                case SerializedPropertyType.Vector3:
                    var v3 = ParseVector(value, 3);
                    if (v3 != null)
                    { property.vector3Value = new Vector3(v3[0], v3[1], v3[2]); resultDesc = $"({v3[0]}, {v3[1]}, {v3[2]})"; return null; }
                    return $"Cannot parse '{value}' as Vector3. Use 'x,y,z'.";

                case SerializedPropertyType.Vector4:
                    var v4 = ParseVector(value, 4);
                    if (v4 != null)
                    { property.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]); resultDesc = $"({v4[0]}, {v4[1]}, {v4[2]}, {v4[3]})"; return null; }
                    return $"Cannot parse '{value}' as Vector4. Use 'x,y,z,w'.";

                case SerializedPropertyType.Color:
                    var cv = ParseVector(value, 4) ?? ParseVector(value, 3);
                    if (cv != null)
                    {
                        var a = cv.Length > 3 ? cv[3] : 1f;
                        property.colorValue = new Color(cv[0], cv[1], cv[2], a);
                        resultDesc = $"({cv[0]}, {cv[1]}, {cv[2]}, {a})";
                        return null;
                    }
                    return $"Cannot parse '{value}' as Color. Use 'r,g,b' or 'r,g,b,a' (0–1 range).";

                case SerializedPropertyType.Rect:
                    var rv = ParseVector(value, 4);
                    if (rv != null)
                    { property.rectValue = new Rect(rv[0], rv[1], rv[2], rv[3]); resultDesc = $"Rect({rv[0]}, {rv[1]}, {rv[2]}, {rv[3]})"; return null; }
                    return $"Cannot parse '{value}' as Rect. Use 'x,y,width,height'.";

                case SerializedPropertyType.LayerMask:
                    if (int.TryParse(value, out var layerVal))
                    { property.intValue = layerVal; resultDesc = layerVal.ToString(); return null; }
                    return $"Cannot parse '{value}' as LayerMask integer.";

                case SerializedPropertyType.AnimationCurve:
                    return TrySetAnimationCurve(property, value, out resultDesc);

                case SerializedPropertyType.Gradient:
                    return TrySetGradient(property, value, out resultDesc);

                case SerializedPropertyType.ObjectReference:
                    return TrySetObjectReference(property, value, out resultDesc);

                default:
                    return $"Property type '{property.propertyType}' is not supported by this tool.";
            }
        }

        // ── Object reference ─────────────────────────────────────────────────

        private static string TrySetObjectReference(SerializedProperty property, string value, out string resultDesc)
        {
            resultDesc = value;

            // Try exact path first
            var refAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(value);
            if (refAsset == null)
            {
                // Search by name
                var guids = AssetDatabase.FindAssets(System.IO.Path.GetFileNameWithoutExtension(value));
                foreach (var guid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var candidate = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (candidate != null &&
                        (property.objectReferenceValue == null ||
                         candidate.GetType() == property.objectReferenceValue.GetType() ||
                         assetPath.EndsWith(value)))
                    {
                        refAsset = candidate;
                        break;
                    }
                }
            }
            if (refAsset == null)
                return $"Cannot find asset '{value}' for object reference. Use set_component_reference for scene objects.";

            property.objectReferenceValue = refAsset;
            resultDesc = $"{refAsset.name} ({refAsset.GetType().Name})";
            return null;
        }

        // ── AnimationCurve ───────────────────────────────────────────────────

        // Input format: {"keys":[{"time":0,"value":0,"inTangent":0,"outTangent":1},{"time":1,"value":1}]}
        [Serializable] private class AnimCurveWrapper { public AnimKeyData[] keys; }
        [Serializable] private class AnimKeyData { public float time; public float value; public float inTangent; public float outTangent; }

        private static string TrySetAnimationCurve(SerializedProperty property, string value, out string resultDesc)
        {
            resultDesc = value;
            try
            {
                var wrapper = JsonUtility.FromJson<AnimCurveWrapper>(value);
                if (wrapper?.keys == null || wrapper.keys.Length == 0)
                    return "AnimationCurve value must be JSON: {\"keys\":[{\"time\":0,\"value\":0,\"inTangent\":0,\"outTangent\":1},...]}";

                var keyframes = wrapper.keys.Select(k => new Keyframe(k.time, k.value, k.inTangent, k.outTangent)).ToArray();
                property.animationCurveValue = new AnimationCurve(keyframes);
                resultDesc = $"AnimationCurve with {keyframes.Length} key(s)";
                return null;
            }
            catch (Exception e)
            {
                return $"Failed to parse AnimationCurve: {e.Message}. Expected format: {{\"keys\":[{{\"time\":0,\"value\":0}}]}}";
            }
        }

        // ── Gradient ─────────────────────────────────────────────────────────

        // Input format: {"colorKeys":[{"r":1,"g":0,"b":0,"a":1,"time":0}],"alphaKeys":[{"alpha":1,"time":0}]}
        [Serializable] private class GradientWrapper { public GradColorKey[] colorKeys; public GradAlphaKey[] alphaKeys; }
        [Serializable] private class GradColorKey { public float r; public float g; public float b; public float a = 1f; public float time; }
        [Serializable] private class GradAlphaKey { public float alpha = 1f; public float time; }

        private static string TrySetGradient(SerializedProperty property, string value, out string resultDesc)
        {
            resultDesc = value;
            try
            {
                var wrapper = JsonUtility.FromJson<GradientWrapper>(value);
                if (wrapper == null)
                    return "Gradient value must be JSON: {\"colorKeys\":[{\"r\":1,\"g\":0,\"b\":0,\"a\":1,\"time\":0}],\"alphaKeys\":[{\"alpha\":1,\"time\":0}]}";

                var gradient = new Gradient();
                var colorKeys = wrapper.colorKeys != null
                    ? wrapper.colorKeys.Select(k => new GradientColorKey(new Color(k.r, k.g, k.b, k.a), k.time)).ToArray()
                    : new[] { new GradientColorKey(Color.white, 0f) };
                var alphaKeys = wrapper.alphaKeys != null
                    ? wrapper.alphaKeys.Select(k => new GradientAlphaKey(k.alpha, k.time)).ToArray()
                    : new[] { new GradientAlphaKey(1f, 0f) };

                gradient.SetKeys(colorKeys, alphaKeys);
                property.gradientValue = gradient;
                resultDesc = $"Gradient with {colorKeys.Length} color key(s) and {alphaKeys.Length} alpha key(s)";
                return null;
            }
            catch (Exception e)
            {
                return $"Failed to parse Gradient: {e.Message}";
            }
        }

        // ── Property lookup ───────────────────────────────────────────────────

        /// <summary>
        /// Finds a serialized property by name, with m_ prefix fallback and case-insensitive search.
        /// </summary>
        public static SerializedProperty FindProperty(SerializedObject so, string name)
        {
            // 1. Direct lookup
            var prop = so.FindProperty(name);
            if (prop != null) return prop;

            // 2. m_ prefix with uppercase first letter (Unity convention: m_Mass, m_UseGravity)
            var altUpper = "m_" + char.ToUpperInvariant(name[0]) + name.Substring(1);
            prop = so.FindProperty(altUpper);
            if (prop != null) return prop;

            // 3. m_ prefix preserving original case (TMP convention: m_fontSize, m_color)
            var altDirect = "m_" + name;
            prop = so.FindProperty(altDirect);
            if (prop != null) return prop;

            // 4. Case-insensitive scan of all visible properties, also stripping m_ prefix
            var iter = so.GetIterator();
            if (iter.NextVisible(true))
            {
                do
                {
                    if (string.Equals(iter.name, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(iter.displayName, name, StringComparison.OrdinalIgnoreCase))
                        return so.FindProperty(iter.propertyPath);

                    // Strip m_ prefix and compare (e.g. "m_fontSize" matches input "fontSize")
                    var iterName = iter.name;
                    if (iterName.Length > 2 && iterName.StartsWith("m_"))
                    {
                        var stripped = iterName.Substring(2);
                        if (string.Equals(stripped, name, StringComparison.OrdinalIgnoreCase))
                            return so.FindProperty(iter.propertyPath);
                    }
                } while (iter.NextVisible(false));
            }

            return null;
        }

        /// <summary>
        /// Reads a human-readable string representation of a SerializedProperty value.
        /// </summary>
        public static string ReadPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return property.boolValue.ToString();

                case SerializedPropertyType.Integer:
                    return property.intValue.ToString();

                case SerializedPropertyType.Float:
                    return property.floatValue.ToString("G");

                case SerializedPropertyType.String:
                    return $"\"{property.stringValue}\"";

                case SerializedPropertyType.Enum:
                    if (property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length)
                        return property.enumDisplayNames[property.enumValueIndex];
                    return property.enumValueIndex.ToString();

                case SerializedPropertyType.Vector2:
                    var v2 = property.vector2Value;
                    return $"({v2.x}, {v2.y})";

                case SerializedPropertyType.Vector3:
                    var v3 = property.vector3Value;
                    return $"({v3.x}, {v3.y}, {v3.z})";

                case SerializedPropertyType.Vector4:
                    var v4 = property.vector4Value;
                    return $"({v4.x}, {v4.y}, {v4.z}, {v4.w})";

                case SerializedPropertyType.Color:
                    var c = property.colorValue;
                    return $"({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";

                case SerializedPropertyType.Rect:
                    var r = property.rectValue;
                    return $"(x:{r.x}, y:{r.y}, w:{r.width}, h:{r.height})";

                case SerializedPropertyType.ObjectReference:
                    if (property.objectReferenceValue == null)
                        return "null";
                    var refPath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                    if (!string.IsNullOrEmpty(refPath))
                        return $"{property.objectReferenceValue.name} ({property.objectReferenceValue.GetType().Name}) at '{refPath}'";
                    return $"{property.objectReferenceValue.name} ({property.objectReferenceValue.GetType().Name})";

                case SerializedPropertyType.LayerMask:
                    return property.intValue.ToString();

                case SerializedPropertyType.AnimationCurve:
                    var curve = property.animationCurveValue;
                    return $"AnimationCurve ({curve.length} keys)";

                case SerializedPropertyType.ArraySize:
                    return property.intValue.ToString();

                default:
                    return $"({property.propertyType})";
            }
        }

        /// <summary>
        /// Lists all visible serialized properties as "name (type)" strings.
        /// </summary>
        public static string ListProperties(SerializedObject so)
        {
            var names = new List<string>();
            var iter = so.GetIterator();
            if (iter.NextVisible(true))
            {
                do
                {
                    if (iter.name == "m_Script") continue;
                    names.Add($"{iter.name} ({iter.propertyType})");
                } while (iter.NextVisible(false));
            }
            return names.Count > 0 ? string.Join(", ", names) : "(none)";
        }
    }
}
