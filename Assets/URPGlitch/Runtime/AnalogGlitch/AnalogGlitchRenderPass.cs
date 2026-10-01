// refered to:
//     https://github.com/keijiro/KinoGlitch.git
//     Assets/Kino/Glitch/AnalogGlitch.cs

using UnityEngine;
using UnityEngine.Rendering;

namespace URPGlitch.Runtime.AnalogGlitch
{
    sealed class AnalogGlitchRenderPass : GlitchRenderPass
    {
        const string RenderPassName = "AnalogGlitch RenderPass";

        // Material Properties
        static readonly int ScanLineJitterID = Shader.PropertyToID("_ScanLineJitter");
        static readonly int VerticalJumpID = Shader.PropertyToID("_VerticalJump");
        static readonly int HorizontalShakeID = Shader.PropertyToID("_HorizontalShake");
        static readonly int ColorDriftID = Shader.PropertyToID("_ColorDrift");

        float _verticalJumpTime;

        static AnalogGlitchVolume Volume => VolumeManager.instance.stack.GetComponent<AnalogGlitchVolume>();

        protected override bool IsVolumeActive => Volume != null && Volume.IsActive;

        public AnalogGlitchRenderPass(Shader shader) : base(shader, RenderPassName)
        {
        }

        protected override void UpdateMaterialProperties()
        {
            var volume = Volume;
            var scanLineJitter = volume.scanLineJitter.value;
            var verticalJump = volume.verticalJump.value;
            var horizontalShake = volume.horizontalShake.value;
            var colorDrift = volume.colorDrift.value;

            _verticalJumpTime += Time.deltaTime * verticalJump * 11.3f;

            var slThresh = Mathf.Clamp01(1.0f - scanLineJitter * 1.2f);
            var slDisp = 0.002f + Mathf.Pow(scanLineJitter, 3) * 0.05f;
            GlitchMaterial.SetVector(ScanLineJitterID, new Vector2(slDisp, slThresh));

            var vj = new Vector2(verticalJump, _verticalJumpTime);
            GlitchMaterial.SetVector(VerticalJumpID, vj);
            GlitchMaterial.SetFloat(HorizontalShakeID, horizontalShake * 0.2f);

            var cd = new Vector2(colorDrift * 0.04f, Time.time * 606.11f);
            GlitchMaterial.SetVector(ColorDriftID, cd);
        }
    }
}
