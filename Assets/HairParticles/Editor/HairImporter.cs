using System.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEditor.AssetImporters;
using UnityEngine;
using System.Linq;
using UnityEditor;

public enum AxisMapping
{
    X = 0,
    Y = 1,
    Z = 2,
}

[ScriptedImporter(version: 1, ext: "hso")]
public class HairImporter : ScriptedImporter
{
    public Mesh mesh;
    [Header("Mesh Axis Mapping")]
    public AxisMapping meshXmapping = AxisMapping.X;
    public bool meshInvertX = false;
    public AxisMapping meshYmapping = AxisMapping.Y;
    public bool meshInvertY = false;
    public AxisMapping meshZmapping = AxisMapping.Z;
    public bool meshInvertZ = true;
    [Header("Bounding Box Axis Mapping")]
    public AxisMapping bboxXmapping = AxisMapping.X;
    public bool bboxInvertX = false;
    public AxisMapping bboxYmapping = AxisMapping.Y;
    public bool bboxInvertY = false;
    public AxisMapping bboxZmapping = AxisMapping.Z;
    public bool bboxInvertZ = true;

    [Header("Compute Shader Bary Calculation")]
    public ComputeShader calcBaryCompute;
    public bool useComputeShader;

    public override void OnImportAsset(AssetImportContext ctx)
    {
        string[] lines = File.ReadAllLines(ctx.assetPath);
        string meshName = null;
        List<int> indices = new List<int>();
        List<HairParticle>  hairParticles = new List<HairParticle>((lines.Length - 2) / 7);
        Vector3 min = new Vector3(20,20,20);
        Vector3 max = new Vector3(-20,-20,-20);
        HashSet<int> rootIDs = new HashSet<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            string[] data = lines[i].Split(' ');
            if (data[0] == "point")
            {
                // Point ID
                HairParticle p = new HairParticle();
                p.ID = int.Parse(data[1]);

                // Root ID
                data = lines[++i].Split(' ');
                p.rootID = int.Parse(data[1]);
                rootIDs.Add(p.rootID);

                // Strand ID
                data = lines[++i].Split(' ');
                p.curveID = int.Parse(data[1]);
                

                // Parent ID
                data = lines[++i].Split(' ');
                p.parentID = int.Parse(data[1]);

                // Position
                data = lines[++i].Split(' ');
                string[] posData = data[1].Split(',');
                float[] inVector = new float[]
                {
                    float.Parse(posData[0], CultureInfo.InvariantCulture.NumberFormat),
                    float.Parse(posData[1], CultureInfo.InvariantCulture.NumberFormat),
                    float.Parse(posData[2], CultureInfo.InvariantCulture.NumberFormat)
                };
                p.position = new Vector3(
                    inVector[(int)meshXmapping] * (meshInvertX ? 1 : -1), 
                    inVector[(int)meshYmapping] * (meshInvertY ? 1 : -1), 
                    inVector[(int)meshZmapping] * (meshInvertZ ? 1 : -1)
                    );
                float[] bboxVector = new float[]
                {
                    inVector[(int)bboxXmapping] * (bboxInvertX ? 1 : -1),
                    inVector[(int)bboxYmapping] * (bboxInvertY ? 1 : -1),
                    inVector[(int)bboxZmapping] * (bboxInvertZ ? 1 : -1)
                };
                for(int pi = 0; pi < 3; pi++)
                {
                    if (bboxVector[pi] < min[pi])
                        min[pi] = bboxVector[pi];
                    if (bboxVector[pi] > max[pi])
                        max[pi] = bboxVector[pi];
                }
                // Tangent
                data = lines[++i].Split(' ');
                string[] tanData = data[1].Split(',');
                p.tangent = new Vector3(
                    float.Parse(tanData[(int)meshXmapping], CultureInfo.InvariantCulture.NumberFormat) * (meshInvertX ? 1 : -1),
                    float.Parse(tanData[(int)meshYmapping], CultureInfo.InvariantCulture.NumberFormat) * (meshInvertY ? 1 : -1),
                    float.Parse(tanData[(int)meshZmapping], CultureInfo.InvariantCulture.NumberFormat) * (meshInvertZ ? 1 : -1)
                    );

                // UV
                data = lines[++i].Split(' ');
                string[] uvData = data[1].Split(',');
                p.uv = new Vector2(
                    float.Parse(uvData[0], CultureInfo.InvariantCulture.NumberFormat),
                    float.Parse(uvData[1], CultureInfo.InvariantCulture.NumberFormat)
                    );

                // Length
                data = lines[++i].Split(' ');
                p.length = float.Parse(data[1], CultureInfo.InvariantCulture.NumberFormat);

                // Factor
                data = lines[++i].Split(' ');
                p.factor = float.Parse(data[1], CultureInfo.InvariantCulture.NumberFormat);

                // Face Indices
                ++i;

                // Face Barycentric Coordinates
                ++i;

                hairParticles.Add(p);
            }
            else if (data[0] == "mesh")
            {
                meshName = data[1];
            }
            else if (data[0] == "indices")
            {
                data = data[1].Split(',');
                indices = new List<int>(data.Length);
                for (int j = 0; j < data.Length; j++)
                {
                    indices.Add(int.Parse(data[j]));
                }
            }
        }

        HairObject hairAsset = ScriptableObject.CreateInstance<HairObject>();
        hairAsset.particles = hairParticles.ToArray();
        hairAsset.indices = indices.ToArray();
        Debug.Log("Bounds: Min: " + min + " Max: " + max);
        hairAsset.bounds = new Bounds((min + max) * .5f, max - min);
        Debug.Log(hairAsset.bounds);
        hairAsset.strandAmount = rootIDs.Count;
        hairAsset.rootIndices = rootIDs.ToArray<int>();
        if (mesh != null)
        {
            hairAsset.mesh = mesh;
            if (!useComputeShader)
                SkinHair(ref hairAsset);
        }

        ctx.AddObjectToAsset("hairParticles", hairAsset);
        ctx.SetMainObject(hairAsset);
        Debug.Log("Finished Hair Import");
    }

    private void SkinHair(ref HairObject hairAsset)
    {
        // Get Bone Indices Per Vertex
        // If switch between two choose one closest (highest bary)
        // Interpolate Weights base on Barys

        Dictionary<int, Vector3> rootBaryDict = new Dictionary<int, Vector3>();
        Dictionary<int, Vector3Int> rootFaceDict = new Dictionary<int, Vector3Int>();
        List<int> triangles = new List<int>(hairAsset.mesh.GetTriangles(0));

        for (int i = 0; i < hairAsset.rootIndices.Length; i++)
        {
            int rootIndex = hairAsset.rootIndices[i]; // r
            Vector3 rootUV = new Vector3(
                hairAsset.particles[rootIndex].uv.x, 
                hairAsset.particles[rootIndex].uv.y, 
                0);
            Vector3Int faceIndices = new Vector3Int(-1, -1, -1);
            Vector3 barys = new Vector3(-1, -1, -1);
            int triStartIndex = -1;
            
            for (int j = 0; j < triangles.Count; j += 3)
            {
                // Barys based on UVs

                Vector3[] triUVs =
                {
                    new Vector3(hairAsset.mesh.uv[triangles[j]].x, hairAsset.mesh.uv[triangles[j]].y, 0),
                    new Vector3(hairAsset.mesh.uv[triangles[j + 1]].x, hairAsset.mesh.uv[triangles[j + 1]].y, 0),
                    new Vector3(hairAsset.mesh.uv[triangles[j + 2]].x, hairAsset.mesh.uv[triangles[j + 2]].y, 0),
                };

                Vector3 ab = triUVs[1] - triUVs[0];
                Vector3 ac = triUVs[2] - triUVs[0];

                float area2 = Vector3.Cross(ab, ac).magnitude;

                Vector3 ra = triUVs[0] - rootUV;
                Vector3 rb = triUVs[1] - rootUV;
                Vector3 rc = triUVs[2] - rootUV;

                float alpha = Vector3.Cross(rb, rc).magnitude / area2;
                if (alpha < 0.0f || alpha > 1.0f)
                    continue;

                float beta = Vector3.Cross(rc, ra).magnitude / area2;
                if (beta < 0.0f || beta > 1.0f)
                    continue;

                float gamma = 1 - alpha - beta;
                if (gamma < 0.0f || gamma > 1.0f)
                    continue;

                faceIndices = new Vector3Int(triangles[j], triangles[j + 1], triangles[j + 2]);
                barys = new Vector3(alpha, beta, gamma);
                triStartIndex = j;
                rootBaryDict.Add(rootIndex, barys);
                rootFaceDict.Add(rootIndex, faceIndices);
                break;
            }
            if(triStartIndex == -1)
            {
                Debug.LogWarning("No barycentric Coordinates found for Root Index " + rootIndex);
                continue;
            }
            SkinParticle(ref hairAsset, faceIndices, barys, rootIndex);
        }

        Vector3[] rootOffsets = new Vector3[hairAsset.particles.Length];
        for(int i = 0; i< hairAsset.particles.Length; i++)
        {
            int rootIndex = hairAsset.particles[i].rootID;

            rootOffsets[i] = hairAsset.particles[i].position - hairAsset.particles[rootIndex].position;
        }

        for (int i = 0; i < hairAsset.particles.Length; i++)
        {
            int rootIndex = hairAsset.particles[i].rootID;

            Vector3 rootBarys = rootBaryDict[rootIndex];
            Vector3Int rootFace = rootFaceDict[rootIndex];

            Vector3 pos = rootBarys.x * hairAsset.mesh.vertices[rootFace.x] 
                + rootBarys.y * hairAsset.mesh.vertices[rootFace.y] 
                + rootBarys.z * hairAsset.mesh.vertices[rootFace.z];
            pos += rootOffsets[i];

            hairAsset.particles[i].position = pos;

            if (i == rootIndex)
                continue;
            hairAsset.particles[i].boneIndex1  = hairAsset.particles[rootIndex].boneIndex1;
            hairAsset.particles[i].boneIndex2  = hairAsset.particles[rootIndex].boneIndex2;
            hairAsset.particles[i].boneIndex3  = hairAsset.particles[rootIndex].boneIndex3;
            hairAsset.particles[i].boneIndex4  = hairAsset.particles[rootIndex].boneIndex4;
            hairAsset.particles[i].boneWeights = hairAsset.particles[rootIndex].boneWeights;
            hairAsset.particles[i].normal      = hairAsset.particles[rootIndex].normal;
        }

        hairAsset.isSkinned = true;
    }

    void SkinParticle(ref HairObject hairAsset, Vector3Int faceIndices, Vector3 barys, int particleIndex)
    {
        // Get Bone Indices Per Vertex
        Dictionary<int, float>[] vertexBoneWeights = new Dictionary<int, float>[3];
        for (int i = 0; i < 3; i++)
        {
            ref BoneWeight boneWeight = ref hairAsset.mesh.boneWeights[faceIndices[i]];
            vertexBoneWeights[i] = new Dictionary<int, float>();
            if (!(boneWeight.weight0 <= 0f))
                vertexBoneWeights[i].Add(boneWeight.boneIndex0, boneWeight.weight0);
            if (!(boneWeight.weight1 <= 0f))
                vertexBoneWeights[i].Add(boneWeight.boneIndex1, boneWeight.weight1);
            if (!(boneWeight.weight2 <= 0f))
                vertexBoneWeights[i].Add(boneWeight.boneIndex2, boneWeight.weight2);
            if (!(boneWeight.weight3 <= 0f))
                vertexBoneWeights[i].Add(boneWeight.boneIndex3, boneWeight.weight3);
        }

        Dictionary<int, float> particleBoneWeights = new Dictionary<int, float>();

        for(int i = 0; i<vertexBoneWeights.Length; i++)
        {
            foreach(KeyValuePair<int, float> boneWeight in vertexBoneWeights[i])
            {
                if (particleBoneWeights.ContainsKey(boneWeight.Key))
                    particleBoneWeights[boneWeight.Key] += boneWeight.Value * barys[i];
                else
                    particleBoneWeights.Add(boneWeight.Key, boneWeight.Value * barys[i]);
            }
        }

        // Sort Weights Based on Highest Weight
        int[] boneIndices = new int[] {0, 0, 0, 0};
        Vector4 sortedWeights = new Vector4(0, 0, 0, 0);
        for (int i = 0; i < 4; i++)
        {
            int index = -1;
            float maxValue = -1f;
            foreach (KeyValuePair<int, float> boneWeight in particleBoneWeights)
            {
                if (boneWeight.Value > maxValue)
                {
                    maxValue = boneWeight.Value;
                    index = boneWeight.Key;
                }
            }
            if (index == -1)
                continue;
            boneIndices[i] = index;
            sortedWeights[i] = maxValue;
            particleBoneWeights.Remove(index);
        }
        sortedWeights /= (sortedWeights.x + sortedWeights.y + sortedWeights.z + sortedWeights.w);
        hairAsset.particles[particleIndex].boneWeights = sortedWeights;
        hairAsset.particles[particleIndex].boneIndex1 = boneIndices[0];
        hairAsset.particles[particleIndex].boneIndex2 = boneIndices[1];
        hairAsset.particles[particleIndex].boneIndex3 = boneIndices[2];
        hairAsset.particles[particleIndex].boneIndex4 = boneIndices[3];

        // Generate Normal from mesh Tangent
        Vector4 tangent =
            hairAsset.mesh.tangents[faceIndices[0]] * barys[0]
            + hairAsset.mesh.tangents[faceIndices[1]] * barys[1]
            + hairAsset.mesh.tangents[faceIndices[2]] * barys[2];
        hairAsset.particles[particleIndex].normal = new Vector3(tangent.x, tangent.y, tangent.z);
    }
}
