using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace URPGlitch.Runtime
{
    // Full screen glitch pass: draws the camera color through the glitch material into a temporary
    // texture and copies the result back to the camera color.
    abstract class GlitchRenderPass : ScriptableRenderPass, IDisposable
    {
        sealed class GlitchPassData
        {
            public TextureHandle Source;
            public TextureHandle SecondaryTexture;
            public int SecondaryTextureID;
            public Material Material;
        }

        sealed class CopyPassData
        {
            public TextureHandle Source;
        }

        static readonly Vector4 IdentityScaleBias = new(1f, 1f, 0f, 0f);

        readonly string _passName;
        readonly ProfilingSampler _profilingSampler;

        protected readonly Material GlitchMaterial;

        protected GlitchRenderPass(Shader shader, string passName)
        {
            _passName = passName;
            _profilingSampler = new ProfilingSampler(passName);
            GlitchMaterial = CoreUtils.CreateEngineMaterial(shader);
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        protected abstract bool IsVolumeActive { get; }

        protected virtual int SecondaryTextureID => 0;

        // Only the last camera of a stack renders the effect, so it is applied once to the composed image.
        public bool ShouldRender(bool isPostProcessEnabled, bool isSceneViewCamera, bool resolvesFinalTarget) =>
            GlitchMaterial != null && IsVolumeActive && isPostProcessEnabled && !isSceneViewCamera && resolvesFinalTarget;

        public virtual void Dispose()
        {
            CoreUtils.Destroy(GlitchMaterial);
#if URP_COMPATIBILITY_MODE
            _compatibilityFrame?.Release();
#endif
        }

        protected abstract void UpdateMaterialProperties();

        protected virtual TextureHandle RecordSecondaryTexture(RenderGraph renderGraph, TextureHandle source,
            RenderTextureDescriptor cameraDescriptor) => TextureHandle.nullHandle;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
            {
                return;
            }

            var cameraData = frameData.Get<UniversalCameraData>();
            var source = resourceData.activeColorTexture;
            var secondaryTexture = RecordSecondaryTexture(renderGraph, source, cameraData.cameraTargetDescriptor);
            UpdateMaterialProperties();

            var destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = $"CameraColor-{_passName}";
            destinationDesc.clearBuffer = false;
            destinationDesc.msaaSamples = MSAASamples.None;
            destinationDesc.bindTextureMS = false;
            var destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<GlitchPassData>(_passName, out var passData, _profilingSampler))
            {
                passData.Source = source;
                passData.SecondaryTexture = secondaryTexture;
                passData.SecondaryTextureID = SecondaryTextureID;
                passData.Material = GlitchMaterial;

                builder.UseTexture(source);
                if (secondaryTexture.IsValid())
                {
                    builder.UseTexture(secondaryTexture);
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (GlitchPassData data, RasterGraphContext context) =>
                {
                    if (data.SecondaryTexture.IsValid())
                    {
                        data.Material.SetTexture(data.SecondaryTextureID, ((RTHandle)data.SecondaryTexture).rt);
                    }

                    Blitter.BlitTexture(context.cmd, (RTHandle)data.Source, IdentityScaleBias, data.Material, 0);
                });
            }

            // Copy back instead of swapping resourceData.cameraColor: with camera stacking the camera color
            // must stay the persistent texture shared by the base and overlay cameras.
            RecordCopyPass(renderGraph, destination, source, $"{_passName} CopyBack");
        }

        protected static void RecordCopyPass(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>(passName, out var passData))
            {
                passData.Source = source;
                builder.UseTexture(source);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CopyPassData data, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, (RTHandle)data.Source, IdentityScaleBias, 0f, true));
            }
        }

        protected static RenderTextureDescriptor ColorOnly(RenderTextureDescriptor descriptor)
        {
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            return descriptor;
        }

#if URP_COMPATIBILITY_MODE // Compatibility Mode is being removed from URP
#pragma warning disable 618, 672 // Type or member is obsolete, Member overrides obsolete member

        RTHandle _compatibilityFrame;

        protected virtual RTHandle PrepareSecondaryTexture(CommandBuffer cmd, RTHandle source,
            RenderTextureDescriptor cameraDescriptor) => null;

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get(_passName);
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                var cameraDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                RenderingUtils.ReAllocateHandleIfNeeded(ref _compatibilityFrame, ColorOnly(cameraDescriptor),
                    FilterMode.Bilinear, TextureWrapMode.Clamp, name: $"_{_passName}Frame");

                var secondaryTexture = PrepareSecondaryTexture(cmd, source, cameraDescriptor);
                if (secondaryTexture != null)
                {
                    GlitchMaterial.SetTexture(SecondaryTextureID, secondaryTexture);
                }

                UpdateMaterialProperties();
                Blitter.BlitCameraTexture(cmd, source, _compatibilityFrame);
                Blitter.BlitCameraTexture(cmd, _compatibilityFrame, source, GlitchMaterial, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

#pragma warning restore 618, 672
#endif
    }
}
