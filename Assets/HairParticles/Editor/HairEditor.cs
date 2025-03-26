using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.TerrainTools;

[CustomEditor(typeof(Hair))]
public class HairEditor : Editor
{
    private Hair _hairParticles;

    private void OnEnable()
    {
        _hairParticles = (Hair)target;
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        GUILayout.Label("Initialized: " + _hairParticles.initialized);
        if(GUILayout.Button("Init Hair"))
        {
            _hairParticles.InitHair();
        }
    }
}
