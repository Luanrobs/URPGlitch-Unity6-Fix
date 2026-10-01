// refered to:
//     https://github.com/keijiro/KinoGlitch.git
//     Assets/Kino/Glitch/DigitalGlitch.cs

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace URPGlitch.Runtime.DigitalGlitch
{
    sealed class DigitalGlitchRenderPass : GlitchRenderPass
    {
        const string RenderPassName = "DigitalGlitch RenderPass";
        const int TrashFrame1Interval = 13;
        const int TrashFrame2Interval = 73;

        // Material Properties
        static readonly int NoiseTexID = Shader.PropertyToID("_NoiseTex");
        static readonly int TrashTexID = Shader.PropertyToID("_TrashTex");
        static readonly int IntensityID = Shader.PropertyToID("_Intensity");

        readonly System.Random _random;
        readonly Texture2D _noiseTexture;

        // Persistent across frames: they hold old camera frames, refreshed every 13 and 73 frames.
        RTHandle _trashFrame1;
        RTHandle _trashFrame2;

        static DigitalGlitchVolume Volume => VolumeManager.instance.stack.GetComponent<DigitalGlitchVolume>();

        protected override bool IsVolumeActive => Volume != null && Volume.IsActive;

        protected override int SecondaryTextureID => TrashTexID;

        public DigitalGlitchRenderPass(Shader shader) : base(shader, RenderPassName)
        {
            _random = new System.Random();

            _noiseTexture = new Texture2D(64, 32, TextureFormat.ARGB32, false)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };

            UpdateNoiseTexture();
        }

        public override void Dispose()
        {
            base.Dispose();
            CoreUtils.Destroy(_noiseTexture);
            _trashFrame1?.Release();
            _trashFrame2?.Release();
        }

        protected override TextureHandle RecordSecondaryTexture(RenderGraph renderGraph, TextureHandle source,
            RenderTextureDescriptor cameraDescriptor)
        {
            var wereReallocated = ReAllocateTrashFrames(cameraDescriptor);
            var trashFrame1 = renderGraph.ImportTexture(_trashFrame1);
            var trashFrame2 = renderGraph.ImportTexture(_trashFrame2);

            var frameCount = Time.frameCount;
            if (wereReallocated || frameCount % TrashFrame1Interval == 0)
            {
                RecordCopyPass(renderGraph, source, trashFrame1, "DigitalGlitch TrashFrame1");
            }

            if (wereReallocated || frameCount % TrashFrame2Interval == 0)
            {
                RecordCopyPass(renderGraph, source, trashFrame2, "DigitalGlitch TrashFrame2");
            }

            return PickTrashFrame() ? trashFrame1 : trashFrame2;
        }

        protected override void UpdateMaterialProperties()
        {
            var intensity = Volume.intensity.value;
            if ((float)_random.NextDouble() > Mathf.Lerp(0.9f, 0.5f, intensity))
            {
                UpdateNoiseTexture();
            }

            GlitchMaterial.SetFloat(IntensityID, intensity);
            GlitchMaterial.SetTexture(NoiseTexID, _noiseTexture);
        }

        bool ReAllocateTrashFrames(RenderTextureDescriptor cameraDescriptor)
        {
            var descriptor = ColorOnly(cameraDescriptor);
            var wasFrame1Reallocated = RenderingUtils.ReAllocateHandleIfNeeded(ref _trashFrame1, descriptor,
                FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TrashFrame1");
            var wasFrame2Reallocated = RenderingUtils.ReAllocateHandleIfNeeded(ref _trashFrame2, descriptor,
                FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TrashFrame2");
            return wasFrame1Reallocated || wasFrame2Reallocated;
        }

        bool PickTrashFrame() => _random.NextDouble() > 0.5;

#if URP_COMPATIBILITY_MODE // Compatibility Mode is being removed from URP
        protected override RTHandle PrepareSecondaryTexture(CommandBuffer cmd, RTHandle source,
            RenderTextureDescriptor cameraDescriptor)
        {
            var wereReallocated = ReAllocateTrashFrames(cameraDescriptor);
            var frameCount = Time.frameCount;
            if (wereReallocated || frameCount % TrashFrame1Interval == 0)
            {
                Blitter.BlitCameraTexture(cmd, source, _trashFrame1);
            }

            if (wereReallocated || frameCount % TrashFrame2Interval == 0)
            {
                Blitter.BlitCameraTexture(cmd, source, _trashFrame2);
            }

            return PickTrashFrame() ? _trashFrame1 : _trashFrame2;
        }
#endif

        void UpdateNoiseTexture()
        {
            var color = RandomColor();

            for (var y = 0; y < _noiseTexture.height; y++)
            {
                for (var x = 0; x < _noiseTexture.width; x++)
                {
                    var r = (float)_random.NextDouble();
                    if (r > 0.89f)
                    {
                        color = RandomColor();
                    }

                    _noiseTexture.SetPixel(x, y, color);
                }
            }

            _noiseTexture.Apply();
        }

        Color RandomColor()
        {
            var r = (float)_random.NextDouble();
            var g = (float)_random.NextDouble();
            var b = (float)_random.NextDouble();
            var a = (float)_random.NextDouble();
            return new Color(r, g, b, a);
        }
    }
}
