using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Unity.Burst.Intrinsics.X86.Avx;

[System.Serializable]
public class HairRendererSettings
{
    public int shadowMapResolution = 1024;
    public int maxShadowMaps = 4;

    private void CalcScissorRects()
    {

    }
}

public class HairRenderer : ScriptableRendererFeature
{
    class HairSimulationPass : ScriptableRenderPass
    {
        // Skinning
        private ComputeShader _hairSkinning;
        private int _skinningKernel;
        private int _boneTransformID;
        private int _inParticlesID;
        private int _outParticlesID;

        //Simulation
        private ComputeShader _hairSimulation;

        public HairSimulationPass(HairRendererSettings settings, ComputeShader hairSkinning, ComputeShader hairSimulation)
        {
            //Hair Skinning
            _hairSkinning = hairSkinning;
            _skinningKernel = hairSkinning.FindKernel("HairSkinning");
            _inParticlesID = Shader.PropertyToID("_InParticles");
            _outParticlesID = Shader.PropertyToID("_OutParticles");
            _boneTransformID = Shader.PropertyToID("_BoneMatrices");

            _hairSimulation = hairSimulation;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            cmd.BeginSample("Hair");
            cmd.BeginSample("Hair Simulation");
            Hair[] hairObjects = GameObject.FindObjectsOfType<Hair>();
            foreach (Hair h in hairObjects)
            {
                if (!h.initialized)
                    continue;
                if (h.skinnedMeshRenderer.isVisible)
                {
                    // Skinning
                    h.UpdateBoneTransforms();
                    cmd.SetComputeBufferParam(_hairSkinning, _skinningKernel, _inParticlesID, h.skinningBuffer);
                    cmd.SetComputeBufferParam(_hairSkinning, _skinningKernel, _outParticlesID, h.vertexRenderBuffer);
                    cmd.SetComputeMatrixArrayParam(_hairSkinning, _boneTransformID, h.boneTransforms);
                    cmd.DispatchCompute(
                        _hairSkinning,
                        _skinningKernel,
                        Mathf.CeilToInt(h.numParticles / 32f), 1, 1);
                }
            }
            cmd.EndSample("Hair Simulation");
            context.ExecuteCommandBuffer(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {

        }
    }

    class HairDepthPrepass : ScriptableRenderPass
    {
        private int _shadowMapResolution;
        private int _maxShadowMaps;

        private HairSingleton _hairsInstance;

        private RenderTextureDescriptor _depthMapDescriptor;
        private RenderTargetHandle _depthMapHandle;


        public HairDepthPrepass(HairRendererSettings settings, RenderTargetHandle depthMapHandle)
        {
            _shadowMapResolution = settings.shadowMapResolution;
            _maxShadowMaps = settings.maxShadowMaps;
            _hairsInstance = HairSingleton.instance;
            //TODO: Calculate ScissorRects

            _depthMapDescriptor = new RenderTextureDescriptor(_shadowMapResolution, _shadowMapResolution, RenderTextureFormat.RFloat);
            _depthMapDescriptor.depthBufferBits = 16;
            _depthMapDescriptor.enableRandomWrite = true;

            _depthMapHandle = depthMapHandle;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureTarget(_depthMapHandle.Identifier());

            ConfigureClear(ClearFlag.All, new Color(0, 0, 0, 0));
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            _hairsInstance.CalcNearest(renderingData.cameraData.camera, _maxShadowMaps);
            cmd.GetTemporaryRT(
                _depthMapHandle.id,
                _depthMapDescriptor,
                FilterMode.Bilinear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            cmd.BeginSample("Hair Depth Prepass");

            Light[] lights = Light.GetLights(LightType.Directional, 0);
            Quaternion inverseRotation = Quaternion.Inverse(lights[0].transform.rotation);
            Matrix4x4 lightRotation = Matrix4x4.Rotate(inverseRotation);

            int[] nearestIndices = _hairsInstance.nearestIndices;
            for (int i = 0; i < _maxShadowMaps; i++)
            {
                if (nearestIndices[i] == -1)
                    continue;
                Hair h = _hairsInstance.hairObjects[i];
                


                h.CalcShadowMatrices(lights[0].transform.rotation, lightRotation);

                cmd.SetGlobalBuffer(h.hairBufferID, h.vertexRenderBuffer);
                cmd.SetViewProjectionMatrices(h.shadowViewMatrix, h.shadowProjMatrix);

                cmd.DrawProcedural(
                    h.indexRenderBuffer,
                    Matrix4x4.identity,
                    h.hairMaterial,
                    h.depthPrepass,
                    MeshTopology.Lines,
                    h.numIndices);
            }

            cmd.SetGlobalTexture(_depthMapHandle.id, _depthMapHandle.Identifier(), RenderTextureSubElement.Depth);
            cmd.EndSample("Hair Depth Prepass");
            context.ExecuteCommandBuffer(cmd);
        }
    }

    class HairShadowPass : ScriptableRenderPass
    {
        private int _shadowMapResolution;
        private int _maxShadowMaps;

        private HairSingleton _hairsInstance;

        private RenderTargetHandle _depthMapHandle;

        private RenderTextureDescriptor _opacityMapDescriptor;
        private RenderTargetHandle _opacityMapHandle;

        private Rect[] scissorRects;
        private int _shadowProjectionParamsID;
        private int _invLightDirID;
        private int _shadowLayerDistID;
        private int _shadowResolutionID;

        public HairShadowPass(HairRendererSettings settings, RenderTargetHandle depthMapHandle, RenderTargetHandle shadowMapHandle)
        {
            _shadowMapResolution = settings.shadowMapResolution;
            _maxShadowMaps = settings.maxShadowMaps;
            _hairsInstance = HairSingleton.instance;
            //TODO: Calculate ScissorRects

            _depthMapHandle = depthMapHandle;

            _opacityMapDescriptor = new RenderTextureDescriptor(_shadowMapResolution, _shadowMapResolution);
            _opacityMapDescriptor.depthBufferBits = 0;
            _opacityMapDescriptor.enableRandomWrite = true;

            _opacityMapHandle = shadowMapHandle;

            _shadowProjectionParamsID = Shader.PropertyToID("_HairShadowProjectionParams");
            _invLightDirID = Shader.PropertyToID("_HairInvLightDirection");
            _shadowLayerDistID = Shader.PropertyToID("_HairOpacityLayerDistance");
            _shadowResolutionID = Shader.PropertyToID("_HairShadowResolution");
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureTarget(_opacityMapHandle.Identifier());  
            ConfigureClear(ClearFlag.Color, new Color(0,0,0,0));
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            cmd.GetTemporaryRT(
                _opacityMapHandle.id, 
                _opacityMapDescriptor, 
                FilterMode.Bilinear);
            cmd.SetGlobalFloat(_shadowResolutionID, _shadowMapResolution);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            cmd.BeginSample("Hair Shadows");

            //cmd.Blit(_depthMapHandle.Identifier(), _opacityMapHandle.Identifier());

            Light[] lights = Light.GetLights(LightType.Directional, 0);
            Vector3 vlightForward = (lights[0].transform.rotation * Vector3.forward).normalized;

            int[] nearestIndices = _hairsInstance.nearestIndices;
            for(int i = 0; i< _maxShadowMaps; i++)
            {
                if (nearestIndices[i] == -1)
                    continue;
                Hair h = _hairsInstance.hairObjects[i];
                cmd.SetGlobalBuffer(h.hairBufferID, h.vertexRenderBuffer);
                

                cmd.SetGlobalVector(_shadowProjectionParamsID, h.shadowProjParams);
                cmd.SetGlobalVector(_invLightDirID, vlightForward);
                
                cmd.SetViewProjectionMatrices(h.shadowViewMatrix, h.shadowProjMatrix);

                // TODO: Set ScissorRect
                

                // Calculate Layer Distance
                // Calc BBox Points nearest and furthest Distance
                //Vector3 bboxMax = h.bounds.max;
                //Vector3 bboxMin = h.bounds.min;
                //Vector3[] bboxPoints = new Vector3[]
                //{
                //    bboxMax,
                //    new Vector3(bboxMin.x, bboxMax.y, bboxMin.z),
                //    new Vector3(bboxMax.x, bboxMax.y, bboxMin.z),
                //    new Vector3(bboxMin.x, bboxMax.y, bboxMax.z),
                //    bboxMin,
                //    new Vector3(bboxMax.x, bboxMin.y, bboxMax.z),
                //    new Vector3(bboxMin.x, bboxMin.y, bboxMax.z),
                //    new Vector3(bboxMax.x, bboxMin.y, bboxMin.z),
                //};
                //
                //float min = h.shadowProjParams.y;
                //float max = h.shadowProjParams.x;
                //foreach(Vector3 point in bboxPoints)
                //{
                //    float dist = Vector3.Dot(point - h.lightPosition, vlightForward);
                //    min = Mathf.Min(min, dist);
                //    max = Mathf.Max(max, dist);
                //}
                // LayerDistance = (furthest - nearest) / (1 - numLayers)
                //Debug.Log("Min: " + min + "; Max: " + max + "; Dist: " + (max - min));
                //float layerDistance = (max - min) / 3f;
                // Set Layer Distance
                //cmd.SetGlobalFloat(_shadowLayerDistID, layerDistance);
                

                // Call SelfShadowPass
                cmd.DrawProcedural(
                    h.indexRenderBuffer,
                    Matrix4x4.identity,
                    h.hairMaterial,
                    h.selfShadowPass,
                    MeshTopology.Lines,
                    h.numIndices);
            }
            cmd.SetViewProjectionMatrices(renderingData.cameraData.camera.worldToCameraMatrix, renderingData.cameraData.camera.projectionMatrix);
            cmd.EndSample("Hair Shadows");
            context.ExecuteCommandBuffer(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            
        }
    }

    class HairRenderPass : ScriptableRenderPass
    {
        private RenderTargetHandle _shadowMapHandle;
        private RenderTargetHandle _depthMapHandle;

        public HairRenderPass(HairRendererSettings settings, RenderTargetHandle depthMapHandle, RenderTargetHandle shadowMapHandle)
        {
            _shadowMapHandle = shadowMapHandle;
            _depthMapHandle = depthMapHandle;
        }

        // This method is called before executing the render pass.
        // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
        // When empty this render pass will render to the active camera render target.
        // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
        // The render pipeline will ensure target setup and clearing happens in a performant manner.
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            
            
        }

        // Here you can implement the rendering logic.
        // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
        // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            cmd.BeginSample("Hair Rendering");
            Hair[] hairObjects = GameObject.FindObjectsOfType<Hair>();
            foreach(Hair h in hairObjects)
            {
                if (!h.initialized)
                    continue;
                if(h.skinnedMeshRenderer.isVisible)
                {
                    cmd.SetGlobalBuffer(h.hairBufferID, h.vertexRenderBuffer);
                    cmd.SetGlobalMatrix(h.shadowVPMatrixID, (h.shadowProjMatrix * h.shadowViewMatrix));
                    // Rendering
                    cmd.DrawProcedural(
                        h.indexRenderBuffer, 
                        Matrix4x4.identity, 
                        h.hairMaterial, 
                        h.renderPass, 
                        MeshTopology.Lines, 
                        h.numIndices);
                }
            }
            cmd.EndSample("Hair Rendering");
            cmd.EndSample("Hair");
            context.ExecuteCommandBuffer(cmd);
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(_shadowMapHandle.id);
            cmd.ReleaseTemporaryRT(_depthMapHandle.id);
        }
    }

    public HairRendererSettings settings = new HairRendererSettings();
    private HairSimulationPass m_hairSimulationPass;
    private HairDepthPrepass m_hairDepthPrepass;
    private HairShadowPass m_hairShadowPass;
    private HairRenderPass m_hairRenderPass;
    private RenderTargetHandle _depthMap;
    private RenderTargetHandle _shadowMap;

    /// <inheritdoc/>
    public override void Create()
    {
        ComputeShader hairSkinning = Resources.Load<ComputeShader>("HairSkinning");
        ComputeShader hairSimulation = Resources.Load<ComputeShader>("HairSimulation");
        _depthMap = new RenderTargetHandle();
        _depthMap.Init("_HairShadowDepthMap");
        _shadowMap = new RenderTargetHandle();
        _shadowMap.Init("_HairShadowMap");

        m_hairSimulationPass = new HairSimulationPass(settings, hairSkinning, hairSimulation);
        m_hairDepthPrepass = new HairDepthPrepass(settings, _depthMap);
        m_hairShadowPass = new HairShadowPass(settings, _depthMap, _shadowMap);
        m_hairRenderPass = new HairRenderPass(settings, _depthMap, _shadowMap);


        // Configures where the render pass should be injected.
        m_hairSimulationPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        m_hairDepthPrepass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        m_hairShadowPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        m_hairRenderPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_hairSimulationPass);
        renderer.EnqueuePass(m_hairDepthPrepass);
        renderer.EnqueuePass(m_hairShadowPass);
        renderer.EnqueuePass(m_hairRenderPass);
    }
}


