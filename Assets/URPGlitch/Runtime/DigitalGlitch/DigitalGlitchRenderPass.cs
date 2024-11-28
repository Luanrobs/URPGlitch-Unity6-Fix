// refered to:
//     https://github.com/keijiro/KinoGlitch.git
//     Assets/Kino/Glitch/DigitalGlitch.cs

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace URPGlitch.Runtime.DigitalGlitch
{
    sealed class DigitalGlitchRenderPass : ScriptableRenderPass, IDisposable
    {
        const string RenderPassName = "DigitalGlitch RenderPass";

        // Material Properties
        static readonly int MainTexID = Shader.PropertyToID("_MainTex");
        static readonly int NoiseTexID = Shader.PropertyToID("_NoiseTex");
        static readonly int TrashTexID = Shader.PropertyToID("_TrashTex");
        static readonly int IntensityID = Shader.PropertyToID("_Intensity");

        readonly ProfilingSampler _profilingSampler;
        readonly System.Random _random;

        readonly Material _glitchMaterial;
        readonly Texture2D _noiseTexture;
        readonly DigitalGlitchVolume _volume;

        RTHandle _mainFrame;
        RTHandle _trashFrame1;
        RTHandle _trashFrame2;

        bool isActive =>
            _glitchMaterial != null &&
            _volume != null &&
            _volume.IsActive;

        public DigitalGlitchRenderPass(Shader shader)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            _profilingSampler = new ProfilingSampler(RenderPassName);
            _random = new System.Random();
            _glitchMaterial = CoreUtils.CreateEngineMaterial(shader);

            _noiseTexture = new Texture2D(64, 32, TextureFormat.ARGB32, false)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };

            var volumeStack = VolumeManager.instance.stack;
            _volume = volumeStack.GetComponent<DigitalGlitchVolume>();

            _mainFrame = RTHandles.Alloc("_MainFrame", name: "_MainFrame");
            _trashFrame1 = RTHandles.Alloc("_TrashFrame1", name: "_TrashFrame1");
            _trashFrame2 = RTHandles.Alloc("_TrashFrame2", name: "_TrashFrame2");
            UpdateNoiseTexture();
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_glitchMaterial);
            CoreUtils.Destroy(_noiseTexture);
        }

        // This method is called by the renderer before executing the render pass.
        // Override this method if you need to to configure render targets and their clear state, and to create temporary render target textures.
        // If a render pass doesn't override this method, this render pass renders to the active Camera's render target.
        // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (!isActive) return;

            var r = (float)_random.NextDouble();
            if (r > Mathf.Lerp(0.9f, 0.5f, _volume.intensity.value))
            {
                UpdateNoiseTexture();
            }
        }

        // Here you can implement the rendering logic.
        // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
        // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var isPostProcessEnabled = renderingData.cameraData.postProcessEnabled;
            var isSceneViewCamera = renderingData.cameraData.isSceneViewCamera;
            if (!isActive || !isPostProcessEnabled || isSceneViewCamera)
            {
                return;
            }

            // TODO: Swap Bufferの検証
            var cmd = CommandBufferPool.Get(RenderPassName);
            cmd.Clear();
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                var source = renderingData.cameraData.renderer.cameraColorTargetHandle;

                var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                cameraTargetDescriptor.depthBufferBits = 0;
                cmd.GetTemporaryRT(Shader.PropertyToID(_mainFrame.name), cameraTargetDescriptor);
                cmd.GetTemporaryRT(Shader.PropertyToID(_trashFrame1.name), cameraTargetDescriptor);
                cmd.GetTemporaryRT(Shader.PropertyToID(_trashFrame2.name), cameraTargetDescriptor);

                var destination = _mainFrame.nameID;
                CoreUtils.SetRenderTarget(cmd, destination);
                cmd.Blit(source, destination);



                var frameCount = Time.frameCount;
                var destinationFrame1 = _trashFrame1.nameID;
                CoreUtils.SetRenderTarget(cmd, destinationFrame1);
                if (frameCount % 13 == 0) cmd.Blit(source, destinationFrame1);
                var destinationFrame2 = _trashFrame2.nameID;
                CoreUtils.SetRenderTarget(cmd, destinationFrame2);
                if (frameCount % 73 == 0) cmd.Blit(source, destinationFrame2);

                var r = (float)_random.NextDouble();
                var blitTrashHandle = r > 0.5f ? _trashFrame1 : _trashFrame2;
                cmd.SetGlobalFloat(IntensityID, _volume.intensity.value);
                cmd.SetGlobalTexture(NoiseTexID, _noiseTexture);
                cmd.SetGlobalTexture(MainTexID, _mainFrame.nameID);
                cmd.SetGlobalTexture(TrashTexID, blitTrashHandle.nameID);

                cmd.Blit(destination, source, _glitchMaterial);

                cmd.ReleaseTemporaryRT(Shader.PropertyToID(_mainFrame.name));
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(_trashFrame1.name));
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(_trashFrame2.name));
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        void UpdateNoiseTexture()
        {
            var color = randomColor;

            for (var y = 0; y < _noiseTexture.height; y++)
            {
                for (var x = 0; x < _noiseTexture.width; x++)
                {
                    var r = (float)_random.NextDouble();
                    if (r > 0.89f)
                    {
                        color = randomColor;
                    }

                    _noiseTexture.SetPixel(x, y, color);
                }
            }

            _noiseTexture.Apply();
        }

        Color randomColor
        {
            get
            {
                var r = (float)_random.NextDouble();
                var g = (float)_random.NextDouble();
                var b = (float)_random.NextDouble();
                var a = (float)_random.NextDouble();
                return new Color(r, g, b, a);
            }
        }
    }
}
