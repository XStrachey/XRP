Shader "XRP/Unlit"
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

			#pragma vertex UnlitPassVertex
			#pragma fragment UnlitPassFragment
			
			#include "../ShaderLibrary/Unlit.hlsl"
			ENDHLSL
        }
	}
}