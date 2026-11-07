using UnityEngine;
using UnityEngine.Rendering;

namespace MoonThemedGame.Computer
{
    // Printed lettering stays attached to the paper and uses the same licensed PT Mono font as the PC.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class NotesCredentials : MonoBehaviour
    {
        public enum NoteContent { Auto, Credentials, RepairGuide }
        public NoteContent content;
        public OperatorTutorialContent tutorial;
        [Tooltip("Printable rectangle in this paper mesh's local units.")]
        public Vector2 guidePrintSize=new Vector2(.00265f,.00363f);
        public Vector3 guidePrintOrigin=new Vector3(.00115242f,-.00042981f,-.00008509f);
        public bool IsRepairGuide=>content==NoteContent.RepairGuide||content==NoteContent.Auto&&name.Replace(" ","").Equals("Notes(1)",System.StringComparison.OrdinalIgnoreCase);
        public string BodyText=>IsRepairGuide?(tutorial?tutorial.Guide:Loc.Russian?OperatorTutorialContent.DefaultRussianGuide:OperatorTutorialContent.DefaultEnglishGuide):Text;
        public const string Login = "Apple";
        public const string Password = "12345";
        public const string Message = "Login: " + Login + "\nPassword: " + Password + "\n\nDon't forget to change\nyour password after\nlogging in.";
        public static string Text => Loc.F("Login: {0}\nPassword: {1}\n\nDon't forget to change\nyour password after\nlogging in.", Login, Password);
        public Font font;
        public Material inkMaterial;
        public TextMesh lettering;
        Material instance;

        void LateUpdate()
        {
            // TextMesh can rebuild its glyph geometry after Refresh/font atlas updates.
            // Fit the actual current geometry as well as the initial text assignment.
            if(IsRepairGuide&&lettering)FitGuide();
        }

        void FitGuide()
        {
            var bounds=lettering.GetComponent<MeshRenderer>().localBounds;
            if(bounds.size.x<=0||bounds.size.y<=0)return;
            float fit=.94f*Mathf.Min(guidePrintSize.x/bounds.size.x,guidePrintSize.y/bounds.size.y);
            var scale=Vector3.one*fit;
            var position=guidePrintOrigin-lettering.transform.localRotation*new Vector3(bounds.min.x*fit,bounds.max.y*fit,0);
            if(lettering.transform.localScale!=scale)lettering.transform.localScale=scale;
            if(lettering.transform.localPosition!=position)lettering.transform.localPosition=position;
        }

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
            if(!tutorial)tutorial=Resources.Load<OperatorTutorialContent>("OperatorTutorialContent");
            lettering.font = font;
            lettering.fontSize = 96;
            lettering.fontStyle = FontStyle.Bold;
            lettering.text = BodyText;
            lettering.color = new Color(0.012f, 0.01f, 0.008f, 1);
            lettering.anchor = TextAnchor.UpperLeft;
            lettering.alignment = TextAlignment.Left;
            lettering.richText = false;
            lettering.lineSpacing = 1.15f;
            font.RequestCharactersInTexture(BodyText, lettering.fontSize, lettering.fontStyle);
            if(IsRepairGuide)FitGuide();
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
