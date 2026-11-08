using System.Collections.Generic;
using System.Text.RegularExpressions;
using MoonThemedGame.Scavenger;
using UnityEngine;

namespace MoonThemedGame.Interactions
{
    // Crisp terminal hints are drawn after scene post-processing, without CRT distortion or grain.
    public static class InteractionHud
    {
        static readonly Dictionary<int,GUIStyle> styles=new Dictionary<int,GUIStyle>();
        static readonly Regex keys=new Regex(@"\[[^\]\r\n]+\]");
        static readonly Color background=new Color(0.015f,0.023f,0.022f,0.86f);
        static readonly Color accent=new Color(0.66f,0.82f,0.73f,0.9f);
        static Font font;
        static FirstPersonController player;
        public static bool Suppressed
        {
            get
            {
                if(DeveloperConsole.IsOpen||OperatorRadioBriefing.IsPlaying)return true;
                if(!player)player=Object.FindFirstObjectByType<FirstPersonController>();
                return player&&player.Noclip;
            }
        }

        static GUIStyle Style(int size)
        {
            if(styles.TryGetValue(size,out var style))return style;
            if(!font){var catalog=Resources.Load<ShopCatalog>("ShopCatalog");if(catalog)font=catalog.hudFont;}
            style=new GUIStyle(GUI.skin.label){font=font,fontSize=size,alignment=TextAnchor.UpperLeft,wordWrap=true,richText=true,padding=new RectOffset()};
            style.normal.textColor=new Color(0.88f,0.92f,0.88f);
            styles[size]=style;return style;
        }

        public static Rect Prompt(float centerX,float top,string text,int size=17,float opacity=1)
        {
            if(Suppressed)return default;
            if(string.IsNullOrWhiteSpace(text) || opacity<=0)return default;
            text=GameControls.FormatHint(text);
            float scale=Mathf.Clamp(Screen.height/900f,1,1.3f);
            int mainSize=Mathf.RoundToInt(size*scale),smallSize=Mathf.Max(14,mainSize-2);
            float padding=Mathf.Round(13*scale),gap=Mathf.Round(5*scale);
            var lines=text.Split('\n');
            for(int i=0;i<lines.Length;i++)
                lines[i]=keys.Replace(lines[i],match=>"<color=#B6D7C2>"+match.Value+"</color>");
            float width=0;
            for(int i=0;i<lines.Length;i++)width=Mathf.Max(width,Style(i==0?mainSize:smallSize).CalcSize(new GUIContent(lines[i])).x);
            width=Mathf.Clamp(Mathf.Ceil(width)+padding*2+2,100,Mathf.Min(650*scale,Screen.width-32));
            float inner=width-padding*2,height=padding*2;
            var heights=new float[lines.Length];
            for(int i=0;i<lines.Length;i++)
            {heights[i]=Mathf.Ceil(Style(i==0?mainSize:smallSize).CalcHeight(new GUIContent(lines[i]),inner));height+=heights[i]+(i>0?gap:0);}
            var rect=new Rect(Mathf.Round(Mathf.Clamp(centerX-width/2,16,Screen.width-width-16)),
                Mathf.Round(Mathf.Clamp(top,16,Mathf.Max(16,Screen.height-height-16))),Mathf.Ceil(width),Mathf.Ceil(height));
            var previous=GUI.color;
            GUI.color=new Color(background.r,background.g,background.b,background.a*opacity);GUI.DrawTexture(rect,Texture2D.whiteTexture);
            GUI.color=new Color(accent.r,accent.g,accent.b,accent.a*opacity);
            GUI.DrawTexture(new Rect(rect.x,rect.y+8,2,rect.height-16),Texture2D.whiteTexture);
            GUI.color=new Color(accent.r,accent.g,accent.b,0.3f*opacity);
            GUI.DrawTexture(new Rect(rect.x+2,rect.y,20,1),Texture2D.whiteTexture);
            float y=rect.y+padding;
            for(int i=0;i<lines.Length;i++)
            {
                var row=new Rect(rect.x+padding,y,inner,heights[i]);
                GUI.color=new Color(1,1,1,(i==0?1:0.8f)*opacity);GUI.Label(row,lines[i],Style(i==0?mainSize:smallSize));
                y+=heights[i]+gap;
            }
            GUI.color=previous;return rect;
        }

        public static void Footer(string text)=>Prompt(Screen.width/2f,Screen.height-76,text,15);
        public static void Corner(string text)=>Prompt(16+Mathf.Min(240,Screen.width*0.2f),Screen.height-76,text,14);
    }
}
