using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UnityEli.Editor.Tools
{
    public class CreateUIElementTool : IEliTool
    {
        public string Name => "create_ui_element";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.name))
            {
                return ToolResult.Error("name is required.");
            }

            if (string.IsNullOrWhiteSpace(input.parent_name))
            {
                return ToolResult.Error("parent_name is required.");
            }

            if (string.IsNullOrWhiteSpace(input.element_type))
            {
                return ToolResult.Error("element_type is required.");
            }

            // Find the parent
            var parent = EliToolHelpers.FindGameObject(input.parent_name);
            if (parent == null)
                return ToolResult.Error($"Parent '{input.parent_name}' not found in the scene.");

            var parentRect = parent.GetComponent<RectTransform>();
            if (parentRect == null)
            {
                return ToolResult.Error($"Parent '{input.parent_name}' does not have a RectTransform. It must be a Canvas or UI element.");
            }

            // Create the element
            var go = new GameObject(input.name);
            go.transform.SetParent(parentRect, false);

            // Set up RectTransform
            var rect = go.AddComponent<RectTransform>();
            SetupRectTransform(rect, inputJson);

            // Add the appropriate component based on element_type
            switch (input.element_type)
            {
                case "Text":
                    SetupText(go, input);
                    break;
                case "Image":
                    SetupImage(go, input, false);
                    break;
                case "Panel":
                    SetupImage(go, input, true);
                    break;
                case "Button":
                    SetupButton(go, rect, input, inputJson);
                    break;
                case "Empty":
                    // Just the RectTransform, no visual component
                    break;
                default:
                    return ToolResult.Error($"Invalid element_type: '{input.element_type}'. Valid values: Text, Image, Panel, Button, Empty.");
            }

            Undo.RegisterCreatedObjectUndo(go, $"Unity Eli: Create UI {input.element_type} '{input.name}'");
            Selection.activeGameObject = go;

            var anchorMinX = JsonHelper.ExtractFloat(inputJson, "anchor_min_x", 0.5f);
            var anchorMinY = JsonHelper.ExtractFloat(inputJson, "anchor_min_y", 0.5f);
            var anchorMaxX = JsonHelper.ExtractFloat(inputJson, "anchor_max_x", 0.5f);
            var anchorMaxY = JsonHelper.ExtractFloat(inputJson, "anchor_max_y", 0.5f);

            return ToolResult.Success(
                $"UI {input.element_type} '{input.name}' created under '{input.parent_name}' " +
                $"with anchors ({anchorMinX},{anchorMinY})-({anchorMaxX},{anchorMaxY}).");
        }

        private void SetupRectTransform(RectTransform rect, string inputJson)
        {
            // Use JsonHelper.ExtractFloat to correctly default to 0.5 when fields are not specified.
            // JsonUtility defaults floats to 0, which is a valid anchor value, so we can't use it
            // to detect "not set".
            var anchorMinX = JsonHelper.ExtractFloat(inputJson, "anchor_min_x", 0.5f);
            var anchorMinY = JsonHelper.ExtractFloat(inputJson, "anchor_min_y", 0.5f);
            var anchorMaxX = JsonHelper.ExtractFloat(inputJson, "anchor_max_x", 0.5f);
            var anchorMaxY = JsonHelper.ExtractFloat(inputJson, "anchor_max_y", 0.5f);

            rect.anchorMin = new Vector2(anchorMinX, anchorMinY);
            rect.anchorMax = new Vector2(anchorMaxX, anchorMaxY);

            var pivotX = JsonHelper.ExtractFloat(inputJson, "pivot_x", 0.5f);
            var pivotY = JsonHelper.ExtractFloat(inputJson, "pivot_y", 0.5f);
            rect.pivot = new Vector2(pivotX, pivotY);

            // Position
            var posX = JsonHelper.ExtractFloat(inputJson, "anchored_position_x", 0f);
            var posY = JsonHelper.ExtractFloat(inputJson, "anchored_position_y", 0f);
            rect.anchoredPosition = new Vector2(posX, posY);

            // Size — default to 200x50
            var sizeX = JsonHelper.ExtractFloat(inputJson, "size_x", 200f);
            var sizeY = JsonHelper.ExtractFloat(inputJson, "size_y", 50f);
            rect.sizeDelta = new Vector2(sizeX, sizeY);
        }

        private void SetupText(GameObject go, Input input)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();

            tmp.text = !string.IsNullOrEmpty(input.text) ? input.text : "Text";
            tmp.fontSize = input.font_size > 0f ? input.font_size : 36f;

            // Text color — default to white
            var r = (input.text_color_r == 0f && input.text_color_g == 0f && input.text_color_b == 0f) ? 1f : input.text_color_r;
            var g = (input.text_color_r == 0f && input.text_color_g == 0f && input.text_color_b == 0f) ? 1f : input.text_color_g;
            var b = (input.text_color_r == 0f && input.text_color_g == 0f && input.text_color_b == 0f) ? 1f : input.text_color_b;
            var a = input.text_color_a != 0f ? input.text_color_a : 1f;
            tmp.color = new Color(r, g, b, a);

            // Alignment
            tmp.alignment = ResolveAlignment(input.text_alignment);

            // Enable overflow handling
            tmp.overflowMode = TextOverflowModes.Overflow;
        }

        private void SetupImage(GameObject go, Input input, bool isPanel)
        {
            var image = go.AddComponent<Image>();

            if (isPanel)
            {
                // Panel defaults to semi-transparent dark background
                var r = input.image_color_r;
                var g = input.image_color_g;
                var b = input.image_color_b;
                var a = input.image_color_a != 0f ? input.image_color_a : 0.4f;
                image.color = new Color(r, g, b, a);
            }
            else
            {
                var r = input.image_color_r != 0f ? input.image_color_r : 1f;
                var g = input.image_color_g != 0f ? input.image_color_g : 1f;
                var b = input.image_color_b != 0f ? input.image_color_b : 1f;
                var a = input.image_color_a != 0f ? input.image_color_a : 1f;
                image.color = new Color(r, g, b, a);
            }
        }

        private void SetupButton(GameObject go, RectTransform rect, Input input, string inputJson)
        {
            // Button = Image + Button component + child Text
            var image = go.AddComponent<Image>();
            var r = input.image_color_r != 0f ? input.image_color_r : 1f;
            var g = input.image_color_g != 0f ? input.image_color_g : 1f;
            var b = input.image_color_b != 0f ? input.image_color_b : 1f;
            var a = input.image_color_a != 0f ? input.image_color_a : 1f;
            image.color = new Color(r, g, b, a);

            go.AddComponent<Button>();

            // Create child Text
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(rect, false);
            var textRect = textGo.AddComponent<RectTransform>();
            // Stretch text to fill button
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = !string.IsNullOrEmpty(input.text) ? input.text : "Button";
            tmp.fontSize = input.font_size > 0f ? input.font_size : 24f;
            tmp.alignment = ResolveAlignment(input.text_alignment);

            // Text color
            var tr = (input.text_color_r == 0f && input.text_color_g == 0f && input.text_color_b == 0f) ? 0.2f : input.text_color_r;
            var tg = (input.text_color_r == 0f && input.text_color_g == 0f && input.text_color_b == 0f) ? 0.2f : input.text_color_g;
            var tb = (input.text_color_r == 0f && input.text_color_g == 0f && input.text_color_b == 0f) ? 0.2f : input.text_color_b;
            var ta = input.text_color_a != 0f ? input.text_color_a : 1f;
            tmp.color = new Color(tr, tg, tb, ta);
        }

        private static TextAlignmentOptions ResolveAlignment(string alignment)
        {
            if (string.IsNullOrEmpty(alignment)) return TextAlignmentOptions.Center;

            switch (alignment)
            {
                case "TopLeft": return TextAlignmentOptions.TopLeft;
                case "Top": return TextAlignmentOptions.Top;
                case "TopRight": return TextAlignmentOptions.TopRight;
                case "Left": return TextAlignmentOptions.Left;
                case "Center": return TextAlignmentOptions.Center;
                case "Right": return TextAlignmentOptions.Right;
                case "BottomLeft": return TextAlignmentOptions.BottomLeft;
                case "Bottom": return TextAlignmentOptions.Bottom;
                case "BottomRight": return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        [Serializable]
        private class Input
        {
            public string name;
            public string parent_name;
            public string element_type;
            public string text;
            public float font_size;
            public float text_color_r;
            public float text_color_g;
            public float text_color_b;
            public float text_color_a;
            public string text_alignment;
            public float image_color_r;
            public float image_color_g;
            public float image_color_b;
            public float image_color_a;
            // Anchor/pivot/size fields kept for JsonUtility but actual values
            // are read via JsonHelper.ExtractFloat for correct defaults.
            public float anchor_min_x;
            public float anchor_min_y;
            public float anchor_max_x;
            public float anchor_max_y;
            public float pivot_x;
            public float pivot_y;
            public float anchored_position_x;
            public float anchored_position_y;
            public float size_x;
            public float size_y;
        }
    }
}
