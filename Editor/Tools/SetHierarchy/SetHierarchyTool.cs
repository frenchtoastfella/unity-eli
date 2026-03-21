using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEli.Editor.Tools
{
    public class SetHierarchyTool : IEliTool
    {
        public string Name => "set_hierarchy";
        public bool NeedsAssetRefresh => false;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.hierarchy))
                return ToolResult.Error("'hierarchy' is required.");

            var mode = (input.mode ?? "world").ToLowerInvariant();
            if (mode != "world" && mode != "ui")
                return ToolResult.Error("'mode' must be 'world' or 'ui'.");

            // Parse hierarchy text into tree
            List<HierarchyNode> nodes;
            try
            {
                nodes = ParseHierarchy(input.hierarchy);
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Failed to parse hierarchy: {e.Message}");
            }

            if (nodes.Count == 0)
                return ToolResult.Error("No valid hierarchy entries found. Each line must start with '- Name'.");

            // Resolve root parent
            Transform parent = null;
            if (!string.IsNullOrWhiteSpace(input.root))
            {
                var rootGo = EliToolHelpers.FindGameObject(input.root);
                if (rootGo == null)
                    return ToolResult.Error($"Root GameObject '{input.root}' not found.");
                parent = rootGo.transform;

                if (mode == "ui" && rootGo.GetComponent<RectTransform>() == null)
                    return ToolResult.Error($"Root '{input.root}' has no RectTransform. UI mode requires a RectTransform parent (e.g. a Canvas).");
            }

            // Apply hierarchy (pass 1: create/update objects and add components)
            int created = 0, updated = 0, removed = 0, componentsAdded = 0, componentsUpdated = 0;
            var errors = new List<string>();
            var deferredRefs = new List<DeferredReference>();

            try
            {
                ApplyNodes(nodes, parent, mode, input.clean,
                    ref created, ref updated, ref removed, ref componentsAdded, ref componentsUpdated,
                    errors, deferredRefs);
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Failed to apply hierarchy: {e.Message}");
            }

            // Pass 2: resolve deferred scene object references
            int refsSet = 0;
            foreach (var dref in deferredRefs)
            {
                try
                {
                    var err = ResolveDeferredReference(dref);
                    if (err != null)
                        errors.Add(err);
                    else
                        refsSet++;
                }
                catch (Exception e)
                {
                    errors.Add($"Failed to set reference '{dref.FieldName}' on '{dref.ComponentTypeName}': {e.Message}");
                }
            }

            // Build result summary
            var sb = new StringBuilder();
            sb.Append($"Hierarchy applied ({mode} mode):");
            var stats = new List<string>();
            if (created > 0) stats.Add($"{created} created");
            if (updated > 0) stats.Add($"{updated} updated");
            if (removed > 0) stats.Add($"{removed} removed");
            if (componentsAdded > 0) stats.Add($"{componentsAdded} components added");
            if (componentsUpdated > 0) stats.Add($"{componentsUpdated} components configured");
            if (refsSet > 0) stats.Add($"{refsSet} references set");
            sb.Append(stats.Count > 0 ? " " + string.Join(", ", stats) + "." : " no changes needed.");

            if (errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var err in errors)
                    sb.AppendLine($"  - {err}");
            }

            return ToolResult.Success(sb.ToString());
        }

        // ── Parsing ──────────────────────────────────────────────────────────

        private static List<HierarchyNode> ParseHierarchy(string text)
        {
            var roots = new List<HierarchyNode>();
            // Stack of (depth, node) for building the tree
            var stack = new List<KeyValuePair<int, HierarchyNode>>();

            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;

                // Count leading spaces
                int spaces = 0;
                while (spaces < rawLine.Length && rawLine[spaces] == ' ') spaces++;
                int depth = spaces / 2;

                var trimmed = rawLine.Substring(spaces);

                // Component line: "+ ComponentType prop=value prop=value"
                if (trimmed.StartsWith("+ "))
                {
                    var compContent = trimmed.Substring(2);
                    var compDef = ParseComponentDef(compContent);

                    // Attach to the most recent node in the stack
                    if (stack.Count > 0)
                    {
                        stack[stack.Count - 1].Value.Components.Add(compDef);
                    }
                    continue;
                }

                // GameObject line: "- Name key:value key:value"
                if (!trimmed.StartsWith("- "))
                    continue; // skip lines that aren't hierarchy entries

                var content = trimmed.Substring(2); // after "- "
                var node = ParseNode(content);

                // Find parent: pop stack entries at >= this depth
                while (stack.Count > 0 && stack[stack.Count - 1].Key >= depth)
                    stack.RemoveAt(stack.Count - 1);

                if (stack.Count == 0)
                {
                    roots.Add(node);
                }
                else
                {
                    stack[stack.Count - 1].Value.Children.Add(node);
                }

                stack.Add(new KeyValuePair<int, HierarchyNode>(depth, node));
            }

            return roots;
        }

        private static HierarchyNode ParseNode(string content)
        {
            var node = new HierarchyNode
            {
                Properties = new Dictionary<string, string>(),
                Children = new List<HierarchyNode>(),
                Components = new List<ComponentDef>()
            };

            // Find where properties start: first " key:" pattern.
            int propsStart = -1;
            for (int i = 1; i < content.Length; i++)
            {
                if (content[i] == ':')
                {
                    int keyStart = i - 1;
                    while (keyStart > 0 && content[keyStart] != ' ') keyStart--;
                    if (content[keyStart] == ' ')
                    {
                        var key = content.Substring(keyStart + 1, i - keyStart - 1);
                        if (key.Length > 0 && !key.Contains(" "))
                        {
                            propsStart = keyStart;
                            break;
                        }
                    }
                }
            }

            if (propsStart >= 0)
            {
                node.Name = content.Substring(0, propsStart).Trim();
                var propsStr = content.Substring(propsStart).Trim();

                // Parse "key:value" pairs separated by spaces
                foreach (var token in propsStr.Split(' '))
                {
                    var colonIdx = token.IndexOf(':');
                    if (colonIdx > 0 && colonIdx < token.Length - 1)
                    {
                        var key = token.Substring(0, colonIdx).ToLowerInvariant();
                        var val = token.Substring(colonIdx + 1);
                        node.Properties[key] = val;
                    }
                }
            }
            else
            {
                node.Name = content.Trim();
            }

            return node;
        }

        private static ComponentDef ParseComponentDef(string content)
        {
            var def = new ComponentDef
            {
                Properties = new Dictionary<string, string>()
            };

            // Split into tokens; first token is the component type name
            // Properties use "prop=value" format (= instead of : to avoid confusion with type names)
            int firstSpace = content.IndexOf(' ');
            if (firstSpace < 0)
            {
                def.TypeName = content.Trim();
                return def;
            }

            def.TypeName = content.Substring(0, firstSpace).Trim();
            var propsStr = content.Substring(firstSpace + 1).Trim();

            // Parse "key=value" pairs separated by spaces
            // Support quoted values: key="value with spaces"
            int pos = 0;
            while (pos < propsStr.Length)
            {
                // Skip whitespace
                while (pos < propsStr.Length && propsStr[pos] == ' ') pos++;
                if (pos >= propsStr.Length) break;

                // Find '='
                int eqIdx = propsStr.IndexOf('=', pos);
                if (eqIdx < 0) break;

                var key = propsStr.Substring(pos, eqIdx - pos);
                pos = eqIdx + 1;

                if (pos >= propsStr.Length)
                {
                    def.Properties[key] = "";
                    break;
                }

                string val;
                if (propsStr[pos] == '"')
                {
                    // Quoted value — find closing quote
                    pos++; // skip opening quote
                    int closeQuote = propsStr.IndexOf('"', pos);
                    if (closeQuote < 0)
                    {
                        val = propsStr.Substring(pos);
                        pos = propsStr.Length;
                    }
                    else
                    {
                        val = propsStr.Substring(pos, closeQuote - pos);
                        pos = closeQuote + 1;
                    }
                }
                else
                {
                    // Unquoted — read until next space
                    int nextSpace = propsStr.IndexOf(' ', pos);
                    if (nextSpace < 0)
                    {
                        val = propsStr.Substring(pos);
                        pos = propsStr.Length;
                    }
                    else
                    {
                        val = propsStr.Substring(pos, nextSpace - pos);
                        pos = nextSpace;
                    }
                }

                def.Properties[key] = val;
            }

            return def;
        }

        // ── Apply ────────────────────────────────────────────────────────────

        private static void ApplyNodes(List<HierarchyNode> nodes, Transform parent, string mode, bool clean,
            ref int created, ref int updated, ref int removed, ref int componentsAdded, ref int componentsUpdated,
            List<string> errors, List<DeferredReference> deferredRefs)
        {
            var matched = new HashSet<Transform>();

            foreach (var node in nodes)
            {
                if (string.IsNullOrEmpty(node.Name))
                {
                    errors.Add("Skipped entry with empty name.");
                    continue;
                }

                var child = FindDirectChild(parent, node.Name);
                bool isNew = child == null;

                if (isNew)
                {
                    GameObject go;
                    if (mode == "ui")
                    {
                        go = new GameObject(node.Name, typeof(RectTransform));
                        if (parent != null)
                            go.transform.SetParent(parent, false);
                    }
                    else
                    {
                        go = new GameObject(node.Name);
                        if (parent != null)
                            go.transform.SetParent(parent, false);
                    }
                    Undo.RegisterCreatedObjectUndo(go, $"Unity Eli: Create {node.Name}");
                    child = go.transform;
                    created++;
                }
                else
                {
                    Undo.RecordObject(child, $"Unity Eli: Update {node.Name}");
                    Undo.RecordObject(child.gameObject, $"Unity Eli: Update {node.Name}");
                    updated++;
                }

                matched.Add(child);
                ApplyProperties(child, node.Properties, mode, errors);

                // Apply components
                foreach (var compDef in node.Components)
                {
                    ApplyComponent(child.gameObject, compDef, ref componentsAdded, ref componentsUpdated, errors, deferredRefs);
                }

                // Recurse into children
                if (node.Children.Count > 0)
                    ApplyNodes(node.Children, child, mode, clean,
                        ref created, ref updated, ref removed, ref componentsAdded, ref componentsUpdated,
                        errors, deferredRefs);
            }

            // Clean: destroy children not mentioned in the hierarchy
            if (clean && parent != null)
            {
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    var c = parent.GetChild(i);
                    if (!matched.Contains(c))
                    {
                        Undo.DestroyObjectImmediate(c.gameObject);
                        removed++;
                    }
                }
            }
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            if (parent == null)
            {
                // Scene root level
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    if (root.name == name) return root.transform;
                return null;
            }
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name) return c;
            }
            return null;
        }

        // ── Component application ───────────────────────────────────────────

        private static void ApplyComponent(GameObject go, ComponentDef compDef, ref int componentsAdded,
            ref int componentsUpdated, List<string> errors, List<DeferredReference> deferredRefs)
        {
            if (string.IsNullOrWhiteSpace(compDef.TypeName))
            {
                errors.Add($"Skipped component with empty type on '{go.name}'.");
                return;
            }

            var compType = EliToolHelpers.ResolveType(compDef.TypeName);
            if (compType == null)
            {
                errors.Add($"Component type '{compDef.TypeName}' not found on '{go.name}'.");
                return;
            }

            if (!typeof(Component).IsAssignableFrom(compType))
            {
                errors.Add($"'{compDef.TypeName}' is not a Component type on '{go.name}'.");
                return;
            }

            // Get or add the component
            var component = go.GetComponent(compType);
            if (component == null)
            {
                component = Undo.AddComponent(go, compType);
                if (component == null)
                {
                    errors.Add($"Failed to add '{compDef.TypeName}' to '{go.name}'.");
                    return;
                }
                componentsAdded++;
            }

            // Set properties if any
            if (compDef.Properties.Count == 0)
                return;

            var so = new SerializedObject(component);
            bool anySet = false;

            foreach (var kvp in compDef.Properties)
            {
                var propName = kvp.Key;
                var propValue = kvp.Value;

                // Check for scene object reference (@GameObjectName or @GameObjectName.ComponentType)
                if (propValue.StartsWith("@"))
                {
                    // Defer reference resolution to pass 2 (after all objects are created)
                    deferredRefs.Add(new DeferredReference
                    {
                        TargetComponent = component,
                        ComponentTypeName = compDef.TypeName,
                        FieldName = propName,
                        ReferenceValue = propValue.Substring(1), // strip @
                        GameObjectName = go.name
                    });
                    continue;
                }

                var prop = EliToolHelpers.FindProperty(so, propName);
                if (prop == null)
                {
                    errors.Add($"Property '{propName}' not found on '{compDef.TypeName}' on '{go.name}'. Available: {EliToolHelpers.ListProperties(so)}");
                    continue;
                }

                string resultDesc;
                var err = EliToolHelpers.TrySetPropertyValue(prop, propValue, out resultDesc);
                if (err != null)
                {
                    errors.Add($"Failed to set '{propName}' on '{compDef.TypeName}' on '{go.name}': {err}");
                }
                else
                {
                    anySet = true;
                }
            }

            if (anySet)
            {
                so.ApplyModifiedProperties();
                componentsUpdated++;
            }
        }

        // ── Deferred reference resolution (pass 2) ──────────────────────────

        private static string ResolveDeferredReference(DeferredReference dref)
        {
            if (dref.TargetComponent == null)
                return $"Target component was destroyed before reference '{dref.FieldName}' could be set.";

            var so = new SerializedObject(dref.TargetComponent);
            var prop = EliToolHelpers.FindProperty(so, dref.FieldName);
            if (prop == null)
                return $"Property '{dref.FieldName}' not found on '{dref.ComponentTypeName}' on '{dref.GameObjectName}'.";

            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                return $"Property '{dref.FieldName}' on '{dref.ComponentTypeName}' is not an object reference (type: {prop.propertyType}).";

            // Parse reference: "GameObjectName" or "GameObjectName.ComponentType"
            string goName;
            string sourceCompType = null;
            var dotIdx = dref.ReferenceValue.IndexOf('.');
            if (dotIdx > 0)
            {
                goName = dref.ReferenceValue.Substring(0, dotIdx);
                sourceCompType = dref.ReferenceValue.Substring(dotIdx + 1);
            }
            else
            {
                goName = dref.ReferenceValue;
            }

            var sourceGo = EliToolHelpers.FindGameObject(goName);
            if (sourceGo == null)
                return $"Referenced GameObject '@{dref.ReferenceValue}' not found for '{dref.FieldName}' on '{dref.ComponentTypeName}' on '{dref.GameObjectName}'.";

            UnityEngine.Object valueToAssign;

            if (!string.IsNullOrEmpty(sourceCompType))
            {
                // Explicit component type specified
                var resolvedType = EliToolHelpers.ResolveType(sourceCompType);
                if (resolvedType == null)
                    return $"Component type '{sourceCompType}' not found for reference '@{dref.ReferenceValue}'.";

                if (typeof(Component).IsAssignableFrom(resolvedType))
                {
                    var comp = sourceGo.GetComponent(resolvedType);
                    if (comp == null)
                        return $"'{goName}' does not have a '{sourceCompType}' component for reference '@{dref.ReferenceValue}'.";
                    valueToAssign = comp;
                }
                else
                {
                    return $"'{sourceCompType}' is not a Component type for reference '@{dref.ReferenceValue}'.";
                }
            }
            else
            {
                // Auto-detect based on field type
                var fieldTypeName = GetFieldTypeName(prop);
                valueToAssign = ResolveSourceValue(sourceGo, fieldTypeName);

                if (valueToAssign == null)
                    return $"Could not find a matching reference on '@{goName}' for field '{dref.FieldName}' " +
                           $"(expected type: {fieldTypeName}). Try @GameObjectName.ComponentType syntax.";
            }

            so.Update();
            prop.objectReferenceValue = valueToAssign;
            so.ApplyModifiedProperties();
            return null; // success
        }

        private static UnityEngine.Object ResolveSourceValue(GameObject sourceGo, string fieldTypeName)
        {
            if (string.IsNullOrEmpty(fieldTypeName))
                return null;

            if (fieldTypeName == "GameObject" || fieldTypeName == "UnityEngine.GameObject")
                return sourceGo;

            if (fieldTypeName == "Transform" || fieldTypeName == "UnityEngine.Transform")
                return sourceGo.transform;

            var resolvedType = EliToolHelpers.ResolveType(fieldTypeName);
            if (resolvedType != null && typeof(Component).IsAssignableFrom(resolvedType))
            {
                var comp = sourceGo.GetComponent(resolvedType);
                if (comp != null)
                    return comp;
            }

            // Fallback: search all components
            foreach (var comp in sourceGo.GetComponents<Component>())
            {
                if (comp == null) continue;
                var compType = comp.GetType();
                if (compType.Name == fieldTypeName || compType.FullName == fieldTypeName)
                    return comp;
            }

            return null;
        }

        private static string GetFieldTypeName(SerializedProperty property)
        {
            var typeStr = property.type;
            if (typeStr.StartsWith("PPtr<$") && typeStr.EndsWith(">"))
                return typeStr.Substring(6, typeStr.Length - 7);
            if (typeStr.StartsWith("PPtr<") && typeStr.EndsWith(">"))
                return typeStr.Substring(5, typeStr.Length - 6);
            return typeStr;
        }

        // ── Property application ─────────────────────────────────────────────

        private static void ApplyProperties(Transform t, Dictionary<string, string> props, string mode, List<string> errors)
        {
            var go = t.gameObject;

            // Common: active, tag, layer
            if (props.TryGetValue("active", out var active))
                go.SetActive(active != "false" && active != "0");

            if (props.TryGetValue("tag", out var tag))
            {
                try { go.tag = tag; }
                catch { errors.Add($"Invalid tag '{tag}' on '{go.name}'."); }
            }

            if (props.TryGetValue("layer", out var layer))
            {
                int idx = LayerMask.NameToLayer(layer);
                if (idx >= 0) go.layer = idx;
                else errors.Add($"Unknown layer '{layer}' on '{go.name}'.");
            }

            if (mode == "ui")
                ApplyUIProperties(t, props, errors);
            else
                ApplyWorldProperties(t, props, errors);
        }

        private static void ApplyWorldProperties(Transform t, Dictionary<string, string> props, List<string> errors)
        {
            if (props.TryGetValue("pos", out var pos))
            {
                var v = ParseVec3(pos);
                if (v.HasValue) t.localPosition = v.Value;
                else errors.Add($"Invalid pos '{pos}' on '{t.name}' (expected x,y,z).");
            }

            if (props.TryGetValue("rot", out var rot))
            {
                var v = ParseVec3(rot);
                if (v.HasValue) t.localEulerAngles = v.Value;
                else errors.Add($"Invalid rot '{rot}' on '{t.name}' (expected x,y,z).");
            }

            if (props.TryGetValue("scale", out var scale))
            {
                var v = ParseVec3(scale);
                if (v.HasValue) t.localScale = v.Value;
                else errors.Add($"Invalid scale '{scale}' on '{t.name}' (expected x,y,z).");
            }
        }

        private static void ApplyUIProperties(Transform t, Dictionary<string, string> props, List<string> errors)
        {
            var rect = t.GetComponent<RectTransform>();
            if (rect == null)
            {
                errors.Add($"'{t.name}' has no RectTransform — cannot apply UI properties.");
                return;
            }

            Undo.RecordObject(rect, $"Unity Eli: Update RectTransform {t.name}");

            if (props.TryGetValue("anchor", out var anchor))
            {
                var v = ParseVec4(anchor);
                if (v.HasValue)
                {
                    rect.anchorMin = new Vector2(v.Value.x, v.Value.y);
                    rect.anchorMax = new Vector2(v.Value.z, v.Value.w);
                }
                else errors.Add($"Invalid anchor '{anchor}' on '{t.name}' (expected minX,minY,maxX,maxY).");
            }

            if (props.TryGetValue("pivot", out var pivot))
            {
                var v = ParseVec2(pivot);
                if (v.HasValue) rect.pivot = v.Value;
                else errors.Add($"Invalid pivot '{pivot}' on '{t.name}' (expected x,y).");
            }

            if (props.TryGetValue("pos", out var pos))
            {
                var v = ParseVec2(pos);
                if (v.HasValue) rect.anchoredPosition = v.Value;
                else errors.Add($"Invalid pos '{pos}' on '{t.name}' (expected x,y).");
            }

            if (props.TryGetValue("size", out var size))
            {
                var v = ParseVec2(size);
                if (v.HasValue) rect.sizeDelta = v.Value;
                else errors.Add($"Invalid size '{size}' on '{t.name}' (expected w,h).");
            }

            if (props.TryGetValue("rot", out var rot))
            {
                if (TryParseFloat(rot, out float z))
                    rect.localEulerAngles = new Vector3(0, 0, z);
                else errors.Add($"Invalid rot '{rot}' on '{t.name}' (expected z degrees).");
            }

            if (props.TryGetValue("scale", out var scale))
            {
                var v = ParseVec3(scale);
                if (v.HasValue) rect.localScale = v.Value;
                else errors.Add($"Invalid scale '{scale}' on '{t.name}' (expected x,y,z).");
            }
        }

        // ── Vector parsing ───────────────────────────────────────────────────

        private static bool TryParseFloat(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, Inv, out v);
        }

        private static Vector2? ParseVec2(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(',');
            if (p.Length >= 2
                && float.TryParse(p[0], NumberStyles.Float, Inv, out float x)
                && float.TryParse(p[1], NumberStyles.Float, Inv, out float y))
                return new Vector2(x, y);
            return null;
        }

        private static Vector3? ParseVec3(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(',');
            if (p.Length >= 3
                && float.TryParse(p[0], NumberStyles.Float, Inv, out float x)
                && float.TryParse(p[1], NumberStyles.Float, Inv, out float y)
                && float.TryParse(p[2], NumberStyles.Float, Inv, out float z))
                return new Vector3(x, y, z);
            return null;
        }

        private static Vector4? ParseVec4(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(',');
            if (p.Length >= 4
                && float.TryParse(p[0], NumberStyles.Float, Inv, out float x)
                && float.TryParse(p[1], NumberStyles.Float, Inv, out float y)
                && float.TryParse(p[2], NumberStyles.Float, Inv, out float z)
                && float.TryParse(p[3], NumberStyles.Float, Inv, out float w))
                return new Vector4(x, y, z, w);
            return null;
        }

        // ── Data types ───────────────────────────────────────────────────────

        private class HierarchyNode
        {
            public string Name;
            public Dictionary<string, string> Properties;
            public List<HierarchyNode> Children;
            public List<ComponentDef> Components;
        }

        private class ComponentDef
        {
            public string TypeName;
            public Dictionary<string, string> Properties;
        }

        private class DeferredReference
        {
            public Component TargetComponent;
            public string ComponentTypeName;
            public string FieldName;
            public string ReferenceValue; // "GameObjectName" or "GameObjectName.ComponentType"
            public string GameObjectName;
        }

        [Serializable]
        private class Input
        {
            public string hierarchy;
            public string root;
            public string mode;
            public bool clean;
        }
    }
}
