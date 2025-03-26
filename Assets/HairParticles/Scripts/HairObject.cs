using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class HairObject : ScriptableObject
{
    public Mesh mesh;
    public HairParticle[] particles;
    [HideInInspector] public int[] indices;
    public Bounds bounds;
    public int strandAmount;
    public int[] rootIndices;
    public bool isSkinned;

    public int pointAmount
    {
        get { return particles.Length; }
    }
}
