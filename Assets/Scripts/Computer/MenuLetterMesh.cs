using System.Collections.Generic;
using UnityEngine;

namespace MoonThemedGame.Computer
{
    // Solid bevelled strokes with open counters. Original geometric letterforms.
    public static class MenuLetterMesh
    {
        public static Mesh Create(char character)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            switch(character)
            {
                case 'P': Stem();Bowl();break;
                case 'R': Stem();Bowl();Stroke(new[]{new Vector2(-.01f,.02f),new Vector2(.32f,-.5f)});break;
                case 'A': Stroke(new[]{new Vector2(-.32f,-.5f),new Vector2(0,.5f),new Vector2(.32f,-.5f)});Stroke(new[]{new Vector2(-.2f,-.15f),new Vector2(.2f,-.15f)});break;
                case 'L': Stroke(new[]{new Vector2(-.28f,.5f),new Vector2(-.28f,-.5f),new Vector2(.3f,-.5f)});break;
                case 'E': Stem();Horizontal(.5f,.31f);Horizontal(0,.22f);Horizontal(-.5f,.31f);break;
                case 'H': Stem();Stroke(new[]{new Vector2(.29f,-.5f),new Vector2(.29f,.5f)});Horizontal(0,.29f);break;
                case 'O':
                    var loop=new Vector2[40];for(int i=0;i<loop.Length;i++){float a=i*Mathf.PI*2/loop.Length;loop[i]=new Vector2(Mathf.Cos(a)*.3f,Mathf.Sin(a)*.5f);}Stroke(loop,true);break;
                case 'S': Stroke(Smooth(new[]{new Vector2(.29f,.42f),new Vector2(.12f,.5f),new Vector2(-.15f,.48f),new Vector2(-.29f,.32f),new Vector2(-.23f,.12f),new Vector2(.13f,-.06f),new Vector2(.29f,-.25f),new Vector2(.23f,-.43f),new Vector2(.03f,-.5f),new Vector2(-.17f,-.48f),new Vector2(-.3f,-.4f)}));break;
            }
            var mesh=new Mesh {name="PALE HORSE / extruded "+character};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            var bounds=mesh.bounds;float height=Mathf.Max(.001f,bounds.size.y);
            for(int i=0;i<vertices.Count;i++)vertices[i]=(vertices[i]-bounds.center)/height;
            mesh.SetVertices(vertices);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;

            void Stem()=>Stroke(new[]{new Vector2(-.28f,-.5f),new Vector2(-.28f,.5f)});
            void Horizontal(float y,float end)=>Stroke(new[]{new Vector2(-.28f,y),new Vector2(end,y)});
            void Bowl()=>Stroke(Smooth(new[]{new Vector2(-.28f,.5f),new Vector2(.07f,.5f),new Vector2(.27f,.42f),new Vector2(.31f,.27f),new Vector2(.25f,.1f),new Vector2(.06f,.035f),new Vector2(-.28f,.035f)}));
            void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {int first=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.Add(first);triangles.Add(first+1);triangles.Add(first+2);}
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){Triangle(a,b,c);Triangle(a,c,d);}
            void Stroke(Vector2[] path,bool closed=false)
            {
                const float halfWidth=.067f,halfDepth=.12f,bevel=.023f;
                var section=new[]{new Vector2(-halfWidth+bevel,-halfDepth),new Vector2(halfWidth-bevel,-halfDepth),new Vector2(halfWidth,-halfDepth+bevel),new Vector2(halfWidth,halfDepth-bevel),new Vector2(halfWidth-bevel,halfDepth),new Vector2(-halfWidth+bevel,halfDepth),new Vector2(-halfWidth,halfDepth-bevel),new Vector2(-halfWidth,-halfDepth+bevel)};
                var rings=new Vector3[path.Length,8];
                for(int i=0;i<path.Length;i++)
                {
                    var before=path[i]-path[i==0?(closed?path.Length-1:0):i-1];var after=path[i==path.Length-1?(closed?0:i):i+1]-path[i];
                    if(before.sqrMagnitude<.000001f)before=after;if(after.sqrMagnitude<.000001f)after=before;
                    before.Normalize();after.Normalize();var tangent=(before+after).normalized;
                    var normal=new Vector2(-tangent.y,tangent.x);var segmentNormal=new Vector2(-after.y,after.x);
                    float miter=Mathf.Min(2.2f,1/Mathf.Max(.2f,Vector2.Dot(normal,segmentNormal)));
                    for(int j=0;j<8;j++){var point=path[i]+normal*(section[j].x*miter);rings[i,j]=new Vector3(point.x,point.y,section[j].y);}
                }
                int segments=closed?path.Length:path.Length-1;
                for(int i=0;i<segments;i++)for(int j=0;j<8;j++)Quad(rings[i,j],rings[i,(j+1)%8],rings[(i+1)%path.Length,(j+1)%8],rings[(i+1)%path.Length,j]);
                if(!closed)for(int j=0;j<8;j++)
                {
                    Triangle(new Vector3(path[0].x,path[0].y,0),rings[0,(j+1)%8],rings[0,j]);
                    int end=path.Length-1;Triangle(new Vector3(path[end].x,path[end].y,0),rings[end,j],rings[end,(j+1)%8]);
                }
            }
        }
        static Vector2[] Smooth(Vector2[] points)
        {
            var result=new List<Vector2>();
            for(int i=0;i<points.Length-1;i++)for(int j=0;j<5;j++)
            {
                float t=j/5f;var a=points[Mathf.Max(0,i-1)];var b=points[i];var c=points[i+1];var d=points[Mathf.Min(points.Length-1,i+2)];
                result.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));
            }
            result.Add(points[points.Length-1]);return result.ToArray();
        }
    }
}
