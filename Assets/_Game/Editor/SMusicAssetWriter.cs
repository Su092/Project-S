using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
namespace S.EditorTools
{
    /// <summary>Preserves the asset object and GUID while replacing native mesh buffers explicitly.</summary>
    public static class SMusicAssetWriter
    {
        public static T Save<T>(T source,string path) where T:UnityEngine.Object
        {
            var target=AssetDatabase.LoadAssetAtPath<T>(path);
            if(!target){AssetDatabase.CreateAsset(source,path);return source;}
            Copy(source,target);UnityEngine.Object.DestroyImmediate(source);return target;
        }
        public static void Copy(UnityEngine.Object source,UnityEngine.Object target)
        {
            if(source==target)return;
            if(source is Mesh mesh&&target is Mesh destination)CopyMesh(mesh,destination);
            else EditorUtility.CopySerialized(source,target);
            EditorUtility.SetDirty(target);
        }
        public static void CopyMesh(Mesh source,Mesh target)
        {
            if(source==target)return;
            if(!source.isReadable||!target.isReadable)throw new InvalidOperationException("Mesh replacement requires readable source/target.");
            // Never CopySerialized a Mesh: this can retain the old native vertex/index buffers.
            target.Clear(false);target.ClearBlendShapes();target.indexFormat=source.indexFormat;
            target.name=source.name;target.vertices=source.vertices;
            if(source.HasVertexAttribute(VertexAttribute.Normal))target.normals=source.normals;
            if(source.HasVertexAttribute(VertexAttribute.Tangent))target.tangents=source.tangents;
            if(source.HasVertexAttribute(VertexAttribute.Color))target.colors=source.colors;
            for(int channel=0;channel<8;channel++)
            {
                var attribute=(VertexAttribute)((int)VertexAttribute.TexCoord0+channel);
                if(!source.HasVertexAttribute(attribute))continue;
                int dimension=source.GetVertexAttributeDimension(attribute);
                if(dimension==2){var uv=new List<Vector2>();source.GetUVs(channel,uv);target.SetUVs(channel,uv);}
                else if(dimension==3){var uv=new List<Vector3>();source.GetUVs(channel,uv);target.SetUVs(channel,uv);}
                else if(dimension==4){var uv=new List<Vector4>();source.GetUVs(channel,uv);target.SetUVs(channel,uv);}
                else throw new InvalidOperationException("Unsupported UV dimension "+dimension+" in "+source.name);
            }
            target.bindposes=source.bindposes;
            var counts=source.GetBonesPerVertex();var weights=source.GetAllBoneWeights();
            if(counts.Length>0&&weights.Length>0)target.SetBoneWeights(counts,weights); // Views owned by source Mesh.
            target.subMeshCount=source.subMeshCount;
            for(int sub=0;sub<source.subMeshCount;sub++)
                target.SetIndices(source.GetIndices(sub,false),source.GetTopology(sub),sub,false,(int)source.GetBaseVertex(sub));
            if(source.blendShapeCount>0)
            {
                var vertices=new Vector3[source.vertexCount];var normals=new Vector3[source.vertexCount];var tangents=new Vector3[source.vertexCount];
                for(int shape=0;shape<source.blendShapeCount;shape++)
                for(int frame=0;frame<source.GetBlendShapeFrameCount(shape);frame++)
                {
                    source.GetBlendShapeFrameVertices(shape,frame,vertices,normals,tangents);
                    target.AddBlendShapeFrame(source.GetBlendShapeName(shape),source.GetBlendShapeFrameWeight(shape,frame),vertices,normals,tangents);
                }
            }
            target.bounds=source.bounds;
            AssertGeometry(source,target);
        }
        public static void AssertGeometry(Mesh expected,Mesh actual)
        {
            void Require(bool condition,string name){if(!condition)throw new InvalidOperationException("Mesh copy mismatch "+expected.name+": "+name);}
            Require(expected.vertexCount==actual.vertexCount,"vertex count");
            Require(expected.vertices.SequenceEqual(actual.vertices),"positions");
            Require(expected.normals.SequenceEqual(actual.normals),"normals");
            Require(expected.tangents.SequenceEqual(actual.tangents),"tangents");
            Require(expected.colors.SequenceEqual(actual.colors),"colors");
            for(int channel=0;channel<8;channel++)
            {
                var a=new List<Vector4>();var b=new List<Vector4>();expected.GetUVs(channel,a);actual.GetUVs(channel,b);
                Require(a.SequenceEqual(b),"UV"+channel);
            }
            Require(expected.indexFormat==actual.indexFormat&&expected.subMeshCount==actual.subMeshCount,"index format/submeshes");
            for(int sub=0;sub<expected.subMeshCount;sub++)
                Require(expected.GetTopology(sub)==actual.GetTopology(sub)&&expected.GetBaseVertex(sub)==actual.GetBaseVertex(sub)&&expected.GetIndices(sub,false).SequenceEqual(actual.GetIndices(sub,false)),"indices "+sub);
            Require(expected.bindposes.SequenceEqual(actual.bindposes),"bind poses");
            var ec=expected.GetBonesPerVertex();var ac=actual.GetBonesPerVertex();var ew=expected.GetAllBoneWeights();var aw=actual.GetAllBoneWeights();
            Require(ec.Length==ac.Length&&ew.Length==aw.Length,"weight counts");
            for(int i=0;i<ec.Length;i++)Require(ec[i]==ac[i],"bone count "+i);
            for(int i=0;i<ew.Length;i++)Require(ew[i].boneIndex==aw[i].boneIndex&&Mathf.Abs(ew[i].weight-aw[i].weight)<.0001f,"weight "+i);
            Require(expected.blendShapeCount==actual.blendShapeCount,"blend shapes");
            Require(expected.bounds==actual.bounds,"bounds");
        }
    }
}
