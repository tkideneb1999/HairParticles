using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
public class Hair : MonoBehaviour
{
    public HairObject hairAsset;
    private HairParticle[] _particles;
    private int[] _indices;
    private bool _initialized = false;
    [HideInInspector] public Bounds _bounds;

    // Skinning
    public SkinnedMeshRenderer skinnedMeshRenderer;
    private ComputeBuffer _skinningBuffer;
    private Matrix4x4[] _boneTransforms;
    private int _boneTransformIndex;
    private int _skinningKernel;

    // Simulation
    private ComputeBuffer _indexSimulationBuffer;
    private ComputeBuffer _simulationVertexFrontBuffer;
    private ComputeBuffer _simulationVertexBackBuffer;
    
    // Rendering
    private ComputeBuffer _vertexRenderBuffer;
    private GraphicsBuffer _indexRenderBuffer;
    public Material hairMaterial;
    public float deepOpacityMapsDistance = 10f;
    private int _hairBufferID = -1;
    private int _renderPass = -1;

    // Shadows
    private int _shadowPass = -1;
    private int _depthPrePass = -1;
    private int _selfShadowPass = -1;
    private int _shadowVPMatrixID = -1;
    private RenderTargetHandle shadowMapHandle;
    private Matrix4x4 _shadowViewMatrix;
    private Matrix4x4 _shadowProjMatrix;
    private Vector3 _lightPosition;
    private Vector4 _shadowProjectionParams;

    public bool initialized
    {
        get { return _initialized; }
    }

    public int numIndices
    {
        get { return _indices.Length; }
    }

    public ComputeBuffer skinningBuffer
    {
        get { return _skinningBuffer; }
    }

    public ComputeBuffer vertexRenderBuffer
    {
        get { return _vertexRenderBuffer; }
    }

    public int hairBufferID
    {
        get
        {
            if(_hairBufferID == -1)
                _hairBufferID = Shader.PropertyToID("_Hair");
            return _hairBufferID;
        }
    }

    public GraphicsBuffer indexRenderBuffer
    {
        get { return _indexRenderBuffer; }
    }

    public Matrix4x4[] boneTransforms
    {
        get { return _boneTransforms; }
    }

    public int numParticles
    {
        get { return _particles.Length; }
    }

    public int renderPass
    {
        get
        {
            if(_renderPass == -1)
                _renderPass = hairMaterial.FindPass("ForwardLit");
            return _renderPass;
        }
    }

    public int shadowPass
    {
        get
        {
            if (_shadowPass == -1)
                _shadowPass = hairMaterial.FindPass("ShadowCaster");
            return _shadowPass;
        }
    }

    public int depthPrepass
    {
        get
        {
            if (_depthPrePass == -1)
                _depthPrePass = hairMaterial.FindPass("ShadowDepthPrepass");
            return _depthPrePass;
        }
    }

    public int selfShadowPass
    {
        get
        {
            if (_selfShadowPass == -1)
                _selfShadowPass = hairMaterial.FindPass("SelfShadowPass");
            return _selfShadowPass;
        }
    }

    public Bounds bounds
    {
        get
        {
            return _bounds;
        }
    }

    public Matrix4x4 shadowViewMatrix
    {
        get
        {
            return _shadowViewMatrix;
        }
    }

    public Matrix4x4 shadowProjMatrix
    {
        get
        {
            return _shadowProjMatrix;
        }
    }

    public Vector4 shadowProjParams
    {
        get
        {
            return _shadowProjectionParams;
        }
    }

    public Vector3 lightPosition
    {
        get
        {
            return _lightPosition;
        }
    }

    public int shadowVPMatrixID
    {
        get
        {
            if(_shadowVPMatrixID == -1)
                _shadowVPMatrixID = Shader.PropertyToID("_HairSelfShadowVPMatrixVP");
            return _shadowVPMatrixID;
        }
    }

    public void InitHair()
    {
        ReleaseBuffers();
        if (hairAsset == null)
            return;

        if (skinnedMeshRenderer == null)
        {
            skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer == null)
                return;
        }

        if (hairMaterial == null)
            return;

        Debug.Log("Skinned Mesh Bounds: " + skinnedMeshRenderer.localBounds);

        _particles = hairAsset.particles;

        _skinningBuffer = new ComputeBuffer(_particles.Length, sizeof(int) * 8 + sizeof(float) * 17);
        _skinningBuffer.SetData(_particles);

        _indices = hairAsset.indices;
        _indexRenderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, _indices.Length, sizeof(int));
        _indexRenderBuffer.SetData(_indices);

        HairRenderParticle[] renderParticles = new HairRenderParticle[_particles.Length];
        for(int i= 0; i < renderParticles.Length; i++)
        {
            renderParticles[i] = new HairRenderParticle(_particles[i]);
        }

        _vertexRenderBuffer = new ComputeBuffer(_particles.Length, sizeof(float) * 13 + sizeof(int) * 3);
        _vertexRenderBuffer.SetData(renderParticles);

        _bounds = hairAsset.bounds;
        
        _hairBufferID = Shader.PropertyToID("_Hair");

        _boneTransforms = new Matrix4x4[skinnedMeshRenderer.bones.Length];
        _boneTransformIndex = Shader.PropertyToID("_BoneMatrices");
        _shadowVPMatrixID = Shader.PropertyToID("_HairSelfShadowVPMatrix");

        _initialized = true;
        Debug.Log("Initialized Hair");
    }

    public void UpdateBoneTransforms()
    {
        for (int i = 0; i < skinnedMeshRenderer.bones.Length; i++)
        {
            _boneTransforms[i] = skinnedMeshRenderer.bones[i].transform.localToWorldMatrix
                * hairAsset.mesh.bindposes[i];
        }
    }


    public void CalcShadowMatrices(Quaternion lightRotation, Matrix4x4 invLightRotation)
    {
        float extend = _bounds.extents.magnitude;
        float nearPlane = 0.01f;
        float farPlane = 2f * extend;

        Vector3 vlightForward = (lightRotation * Vector3.forward).normalized;
        _lightPosition = -_bounds.center - transform.position - vlightForward * extend;
        Matrix4x4 mlightPosition = Matrix4x4.Translate(_lightPosition);

        // Create Shadow Matrices
        _shadowViewMatrix = invLightRotation * mlightPosition;
        _shadowProjMatrix = Matrix4x4.Ortho(-extend, extend, -extend, extend, nearPlane, farPlane);
        _shadowProjMatrix = GL.GetGPUProjectionMatrix(_shadowProjMatrix, true);
        _shadowProjectionParams = new Vector4(nearPlane, farPlane, 1f / farPlane, 0);
    }

    private void ReleaseBuffers()
    {
        if (_simulationVertexBackBuffer != null)
            _simulationVertexBackBuffer.Dispose();
        if (_simulationVertexFrontBuffer != null)
            _simulationVertexFrontBuffer.Dispose();
        if (_vertexRenderBuffer != null)
            _vertexRenderBuffer.Dispose();
        if (_indexRenderBuffer != null)
            _indexRenderBuffer.Dispose();
        if (_indexSimulationBuffer != null)
            _indexSimulationBuffer.Dispose();
        if (_skinningBuffer != null)
            _skinningBuffer.Dispose();
    }

    public void OnEnable()
    {
        InitHair();
        HairSingleton.instance.Add(this);
    }

    public void OnDisable()
    {
        ReleaseBuffers();
        _initialized = false;
        HairSingleton.instance.Remove(this);
    }
}
