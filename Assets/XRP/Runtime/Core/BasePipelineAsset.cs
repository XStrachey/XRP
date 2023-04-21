using UnityEngine;
using UnityEngine.Rendering;

namespace XRP
{
    /// <summary>
    /// The main purpose of the pipeline asset is to give Unity a way to get a hold of a pipeline object instance that is responsible for rendering. 
    /// The asset itself is just a handle and a place to store pipeline settings.
    /// </summary>
    [CreateAssetMenu(menuName = "Rendering/XRP/Base Pipeline")]
    public class BasePipelineAsset : RenderPipelineAsset
    {
        [SerializeField]
        bool enableDynamicBatching;
        [SerializeField]
        bool enableInstanceing;
        [SerializeField]
        ShadowMapSize shadowMapSize = ShadowMapSize._1024;
        [SerializeField]
        float shadowDistance = 100.0f;
        [SerializeField]
	    ShadowCascades shadowCascades = ShadowCascades.Four;
        [SerializeField]
        float twoCascadesSplit = 0.25f;
        [SerializeField]
        Vector3 fourCascadesSplit = new Vector3(0.067f, 0.2f, 0.467f);

        protected override RenderPipeline CreatePipeline()
        {
            Vector3 shadowCascadeSplit = shadowCascades == ShadowCascades.Four ? fourCascadesSplit : new Vector3(twoCascadesSplit, 0, 0);
            return new BasePipeline(enableDynamicBatching, enableInstanceing, shadowMapSize, shadowDistance, (int)shadowCascades, shadowCascadeSplit);
        }
    }
}
