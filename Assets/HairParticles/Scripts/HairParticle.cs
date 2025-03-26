using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[System.Serializable]
public struct HairParticle
{
    public int ID;
    public int rootID;
    public int parentID;
    public int curveID;
    public Vector3 position;
    public Vector3 tangent;
    public Vector3 normal;
    public Vector2 uv;
    public float length;
    public float factor;
    public int boneIndex1;
    public int boneIndex2;
    public int boneIndex3;
    public int boneIndex4;
    public Vector4 boneWeights;
}

public struct HairRenderParticle
{
    int ID;
    int parentID;
    int curveID;
    Vector3 position;
    Vector3 tangent;
    Vector3 normal;
    Vector2 uv;
    float length;
    float factor;

    public HairRenderParticle(HairParticle h)
    {
        ID = h.ID;
        parentID = h.parentID;
        curveID = h.curveID;
        position = h.position;
        tangent = h.tangent;
        normal = h.normal;
        uv = h.uv;
        length = h.length;
        factor = h.factor;
    }
}