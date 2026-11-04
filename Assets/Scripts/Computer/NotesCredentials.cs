using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Computer
{
    // Printed lettering stays attached to the paper and uses the same licensed PT Mono font as the PC.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class NotesCredentials : MonoBehaviour
    {
        public const string Login = "Apple";
        public const string Password = "12345";
        public const string Message = "Login: " + Login + "\nPassword: " + Password + "\n\nDon't forget to change\nyour password after\nlogging in.";
        public static string Text => Loc.F("Login: {0}\nPassword: {1}\n\nDon't forget to change\nyour password after\nlogging in.", Login, Password);
        public Font font;
        public Material inkMaterial;
        public TextMesh lettering;
        Material instance;

        void OnEnable()
        {
            Loc.Changed -= Refresh;
            Loc.Changed += Refresh;
            Font.textureRebuilt += OnFontRebuilt;
            Refresh();
        }

        public void Refresh()
        {
            if (!font || !inkMaterial || !lettering) return;
            lettering.font = font;
            lettering.fontSize = 96;
            lettering.fontStyle = FontStyle.Bold;
            lettering.text = Text;
            lettering.color = new Color(0.012f, 0.01f, 0.008f, 1);
            lettering.anchor = TextAnchor.UpperLeft;
            lettering.alignment = TextAlignment.Left;
            lettering.richText = false;
            lettering.lineSpacing = 1.15f;
            font.RequestCharactersInTexture(Text, lettering.fontSize, lettering.fontStyle);
            if (!instance) instance = new Material(inkMaterial) { name = "Notes / printed ink", hideFlags = HideFlags.HideAndDontSave };
            var renderer = lettering.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = instance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            UpdateAtlas();
        }

        void OnFontRebuilt(Font rebuilt) { if (rebuilt == font) UpdateAtlas(); }

        void UpdateAtlas()
        {
            if (instance && font && font.material) instance.mainTexture = font.material.mainTexture;
        }

        void OnDisable()
        {
            Loc.Changed -= Refresh;
            Font.textureRebuilt -= OnFontRebuilt;
            if (lettering && inkMaterial) lettering.GetComponent<MeshRenderer>().sharedMaterial = inkMaterial;
            if (instance)
            {
                if (Application.isPlaying) Destroy(instance);
                else DestroyImmediate(instance);
            }
            instance = null;
        }
    }
}
