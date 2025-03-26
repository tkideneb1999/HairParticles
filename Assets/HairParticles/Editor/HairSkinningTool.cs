using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class HairSkinningTool : EditorWindow
{
    public HairObject hairObject;
    public ComputeShader barysCalc;

    [MenuItem("Window/Hair Skinning")]
    static void Init()
    {
        HairSkinningTool window = (HairSkinningTool) EditorWindow.GetWindow(typeof(HairSkinningTool));
    }

    private void OnGUI()
    {
        GUILayout.Label("Hair Asset");
        hairObject = (HairObject) EditorGUILayout.ObjectField(hairObject, typeof(HairObject), true);
        GUILayout.Label("Bary Calculator");
        barysCalc = (ComputeShader) EditorGUILayout.ObjectField(barysCalc, typeof(ComputeShader), true);
        if(GUILayout.Button("Skin Hair"))
            CalculateBarys();
    }

    private void CalculateBarys()
    {
        Debug.Log("Calculating Barys");
        if (barysCalc == null)
        {
            Debug.LogWarning("Compute Shader not set");
            return;
        }
        if (hairObject == null)
        {
            Debug.LogWarning("No Hair Asset set");
            return;
        }

        int kernel = barysCalc.FindKernel("CalcBarys");

        // Input Buffers
        // -- Mesh UVs
        int[] meshIndices = hairObject.mesh.GetIndices(0);

        ComputeBuffer meshUVsBuffer = new ComputeBuffer(hairObject.mesh.uv.Length, sizeof(float) * 2);
        List<Vector2> meshUVs = new List<Vector2>();
        for(int i = 0; i<meshIndices.Length; i++)
        {
            meshUVs.Add(hairObject.mesh.uv[meshIndices[i]]);
        }
        hairObject.mesh.GetUVs(0, meshUVs);
        meshUVsBuffer.SetData(meshUVs.ToArray());
        barysCalc.SetBuffer(kernel, "_MeshUVs", meshUVsBuffer);

        // -- Mesh Indices
        
        ComputeBuffer meshIndicesBuffer = new ComputeBuffer(meshIndices.Length, sizeof(int));
        meshIndicesBuffer.SetData(meshIndices);
        barysCalc.SetBuffer(kernel, "_MeshIndices", meshIndicesBuffer);
        barysCalc.SetInt("_NumIndices", meshIndices.Length);

        // -- Hair Root UVs
        Vector2[] rootUVs = new Vector2[hairObject.rootIndices.Length];
        for (int i = 0; i < hairObject.rootIndices.Length; i++)
        {
            rootUVs[i] = hairObject.particles[hairObject.rootIndices[i]].uv;
        }
        ComputeBuffer rootUVsBuffer = new ComputeBuffer(rootUVs.Length, sizeof(float) * 2);
        rootUVsBuffer.SetData(rootUVs);
        barysCalc.SetBuffer(kernel, "_RootUVs", rootUVsBuffer);

        // Output Buffers
        // -- Face Indices
        ComputeBuffer faceIndicesBuffer = new ComputeBuffer(rootUVs.Length, sizeof(int) * 3);
        barysCalc.SetBuffer(kernel, "_FaceIndices", faceIndicesBuffer);

        // -- Face Barys
        ComputeBuffer faceBarysBuffer = new ComputeBuffer(rootUVs.Length, sizeof(float) * 3);
        barysCalc.SetBuffer(kernel, "_FaceBarys", faceBarysBuffer);
        
        barysCalc.Dispatch(kernel, Mathf.CeilToInt(rootUVs.Length / 32f), 1, 1);
        // -- Get Face Indices Data
        Vector3Int[] faceIndices = new Vector3Int[rootUVs.Length];
        faceIndicesBuffer.GetData(faceIndices);

        // -- Get Face Barys Data
        Vector3[] faceBarys = new Vector3[rootUVs.Length];
        faceBarysBuffer.GetData(faceBarys);

        // Buffer Disposal
        meshUVsBuffer.Dispose();
        meshIndicesBuffer.Dispose();
        rootUVsBuffer.Dispose();

        faceIndicesBuffer.Dispose();
        faceBarysBuffer.Dispose();

        Dictionary<int, Vector3> rootBaryDict = new Dictionary<int, Vector3>();
        Dictionary<int, Vector3Int> rootFaceDict = new Dictionary<int, Vector3Int>();

        for (int i = 0; i < hairObject.rootIndices.Length; i++)
        {
            Debug.Log("Face Index: " + faceIndices[i] + "; Face barys: " + faceBarys[i]);
            //SkinParticle(ref hairObject, faceIndices[i], faceBarys[i], hairObject.rootIndices[i]);
        }

        string indicesString = "";
        for( int i=0; i< meshIndices.Length; i++)
        {
            indicesString += meshIndices[i].ToString() + ", ";
        }
        Debug.Log(indicesString);

        return;

        Vector3[] rootOffsets = new Vector3[hairObject.particles.Length];
        for (int i = 0; i < hairObject.particles.Length; i++)
        {
            int rootIndex = hairObject.particles[i].rootID;

            rootOffsets[i] = hairObject.particles[i].position - hairObject.particles[rootIndex].position;
        }

        for (int i = 0; i < hairObject.particles.Length; i++)
        {
            int rootIndex = hairObject.particles[i].rootID;

            Vector3 rootBarys = rootBaryDict[rootIndex];
            Vector3Int rootFace = rootFaceDict[rootIndex];

            Vector3 pos = rootBarys.x * hairObject.mesh.vertices[rootFace.x]
                + rootBarys.y * hairObject.mesh.vertices[rootFace.y]
                + rootBarys.z * hairObject.mesh.vertices[rootFace.z];
            pos += rootOffsets[i];

            hairObject.particles[i].position = pos;

            if (i == rootIndex)
                continue;
            hairObject.particles[i].boneIndex1  = hairObject.particles[rootIndex].boneIndex1;
            hairObject.particles[i].boneIndex2  = hairObject.particles[rootIndex].boneIndex2;
            hairObject.particles[i].boneIndex3  = hairObject.particles[rootIndex].boneIndex3;
            hairObject.particles[i].boneIndex4  = hairObject.particles[rootIndex].boneIndex4;
            hairObject.particles[i].boneWeights = hairObject.particles[rootIndex].boneWeights;
            hairObject.particles[i].normal      = hairObject.particles[rootIndex].normal;
        }
        hairObject.isSkinned = true;
        EditorUtility.SetDirty(hairObject);
        AssetDatabase.SaveAssetIfDirty(hairObject);
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

        for (int i = 0; i < vertexBoneWeights.Length; i++)
        {
            foreach (KeyValuePair<int, float> boneWeight in vertexBoneWeights[i])
            {
                if (particleBoneWeights.ContainsKey(boneWeight.Key))
                    particleBoneWeights[boneWeight.Key] += boneWeight.Value * barys[i];
                else
                    particleBoneWeights.Add(boneWeight.Key, boneWeight.Value * barys[i]);
            }
        }

        // Sort Weights Based on Highest Weight
        int[] boneIndices = new int[] { 0, 0, 0, 0 };
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
