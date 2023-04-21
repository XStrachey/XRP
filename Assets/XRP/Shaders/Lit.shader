Shader "XRP/Lit"
{
	Properties 
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }
	
	SubShader
    {
		Pass
        {
            Tags
            {
                "LightMode" = "ForwardBase"
            }
            HLSLPROGRAM
            // When editing include files, Unity doesn't always respond to a change and fails to refresh the shaders.
            // When that happens, try again by saving the file once more, if necessary with a small change that you can later undo.
            #pragma target 3.0

            #pragma multi_compile_instancing

			#pragma multi_compile _ _CASCADED_SHADOWS_HARD _CASCADED_SHADOWS_SOFT
			#pragma multi_compile _ _SHADOWS_SOFT

			#pragma vertex LitPassVertex
			#pragma fragment LitPassFragment
			
			#include "../ShaderLibrary/Lit.hlsl"
			ENDHLSL
        }

        Pass 
        {
			Tags 
            {
				"LightMode" = "ShadowCaster"
			}
			
			HLSLPROGRAM
			
			#pragma target 3.5
			
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling
			
			#pragma vertex ShadowCasterPassVertex
			#pragma fragment ShadowCasterPassFragment
			
			#include "../ShaderLibrary/ShadowCaster.hlsl"
			
			ENDHLSL
		}
	}
}