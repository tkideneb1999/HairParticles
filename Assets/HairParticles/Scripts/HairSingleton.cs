using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HairSingleton
{
    private static HairSingleton _instance;

    public static HairSingleton instance
    {
        get
        {
            if ( _instance == null )
                _instance = new HairSingleton();
            return _instance;
        }
    }

    private List<Hair> _hairObjects;
    private int[] _nearestIndices;

    public List<Hair> hairObjects
    {
        get { return _hairObjects; }
    }
    public HairSingleton()
    {
        _hairObjects = new List<Hair>();
    }

    public int[] nearestIndices
    {
        get { return _nearestIndices; }
    }

    public void Add(Hair hair)
    {
        if (!hairObjects.Contains(hair))
            hairObjects.Add(hair);

    }

    public void Remove(Hair hair)
    {
        hairObjects.Remove(hair);
    }

    public int[] CalcNearest(Camera camera, int maxSort)
    {
        Dictionary<int, float> indexDistDict = new Dictionary<int, float>();
        for(int i = 0; i<_hairObjects.Count; i++)
        {
            Hair h = _hairObjects[i];
            if (!h.skinnedMeshRenderer.isVisible)
                continue;
            if (!h.initialized)
                continue;
            Vector3 ch = h.transform.position - camera.transform.position;
            if (Vector3.Dot(camera.transform.forward, ch.normalized) <= 0f)
                continue;
            indexDistDict.Add(i, ch.magnitude);
        }
        _nearestIndices = new int[maxSort];
        for(int i = 0; i< maxSort; i++)
        {
            if (indexDistDict.Count == 0)
            {
                _nearestIndices[i] = -1;
                continue;
            }
            float dist = camera.farClipPlane;
            int index = -1;
            foreach(KeyValuePair<int, float> pair in indexDistDict)
            {
                if(pair.Value < dist)
                {
                    index = pair.Key;
                    dist = pair.Value;
                }
            }
            _nearestIndices[i] = index;
            indexDistDict.Remove(i);
        }
        return _nearestIndices;
    }
}
