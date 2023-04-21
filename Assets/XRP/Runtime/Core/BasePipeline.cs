using UnityEngine;
using UnityEngine.Rendering;

namespace XRP
{
    public enum ShadowCascades
    {
        Zero = 0,
        Two = 2,
        Four = 4
    }

    /// <summary>
    /// rendering process.
    /// </summary>
    public class BasePipeline : RenderPipeline
    {
        bool enableDynamicBatching;
        bool enableInstanceing;

        const int maxVisibleLights = 16;
	
        static int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
        static int visibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
        static int visibleLightAttenuationsId = Shader.PropertyToID("_VisibleLightAttenuations");
        static int visibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
        static int shadowMapId = Shader.PropertyToID("_ShadowMap");
        static int worldToShadowMatricesId = Shader.PropertyToID("_WorldToShadowMatrices");
        static int shadowBiasId = Shader.PropertyToID("_ShadowBias");
        static int shadowDataId = Shader.PropertyToID("_ShadowData");
        static int shadowMapSizeId = Shader.PropertyToID("_ShadowMapSize");
        static int globalShadowDataId = Shader.PropertyToID("_GlobalShadowData");
        static int cascadedShadowMapId = Shader.PropertyToID("_CascadedShadowMap");
        static int worldToShadowCascadeMatricesId = Shader.PropertyToID("_WorldToShadowCascadeMatrices");
        static int cascadedShadowMapSizeId = Shader.PropertyToID("_CascadedShadowMapSize");
	    static int cascadedShadoStrengthId = Shader.PropertyToID("_CascadedShadowStrength");
        static int cascadeCullingSpheresId = Shader.PropertyToID("_CascadeCullingSpheres");

        const string shadowsSoftKeyword = "_SHADOWS_SOFT";
        const string cascadedShadowsHardKeyword = "_CASCADED_SHADOWS_HARD";
	    const string cascadedShadowsSoftKeyword = "_CASCADED_SHADOWS_SOFT";

        // Why not use a Color array?
        // There is no way to directly pass a color array to the GPU.
        Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
        Vector4[] visibleLightDirections = new Vector4[maxVisibleLights];
        Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
        Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];
        Matrix4x4[] worldToShadowMatrices = new Matrix4x4[maxVisibleLights];
        Vector4[] shadowData = new Vector4[maxVisibleLights];
        Matrix4x4[] worldToShadowCascadeMatrices = new Matrix4x4[5];
        Vector4[] cascadeCullingSpheres = new Vector4[4];
        int shadowCascades;
	    Vector3 shadowCascadeSplit;

        RenderTexture shadowMap, cascadedShadowMap;
        int shadowMapSize;
        float shadowDistance;

        int shadowTileCount;

        public BasePipeline(bool enableDynamicBatching, bool enableInstanceing, ShadowMapSize shadowMapSize, float shadowDistance, int shadowCascades, Vector3 shadowCascadeSplit)
        {
            this.enableDynamicBatching = enableDynamicBatching;
            this.enableInstanceing = enableInstanceing;
            this.shadowMapSize = (int)shadowMapSize;
            this.shadowDistance = shadowDistance;
            this.shadowCascades = shadowCascades;
            this.shadowCascadeSplit = shadowCascadeSplit;

            // However, by default Unity considers the light's intensity to be defined in gamma space, even through we're working in linear space.
            // This is a holdover of Unity's default render pipeline; the new pipelines consider it a linear value. 
            // This behavior is controlled via the boolean GraphicsSettings.lightsUseLinearIntensity property. 
            // It is a project setting, but can only be adjusted via code. 
            GraphicsSettings.lightsUseLinearIntensity = true;
            // When a reverse Z buffer is used we have to push the shadow position Z coordinate to 1 instead. 
            // We can do that by setting the m33 field of the dummy matrix to 1 in the constructor.
            if (SystemInfo.usesReversedZBuffer)
            {
                worldToShadowCascadeMatrices[4].m33 = 1f;
            }
        }

        // The context delays the actual rendering until we submit it.
        // Before that, we configure it and add commands to it for later execution.
        CommandBuffer cmd = new CommandBuffer
        {
            // Give the command buffer a name.
            name = "Render Camera"
        };

        CommandBuffer shadowCmd = new CommandBuffer
        {
            name = "Render Shadow"
        };

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            for (int i = 0; i < cameras.Length; ++i)
            {
                Render(context, cameras[i]);
            }

            // the commands that we issue to the context are buffered. 
            // The actual works happens after we submit it for execution, via the Submit method.
            context.Submit();
        }

        void Render(ScriptableRenderContext context, Camera camera)
        {
            // Rather than rendering every object, we're only going to render those that the camera can see.
            // We do that by starting with all renderers in the scene and then culling those that fall outside of the view frustum of the camera.
            ScriptableCullingParameters cullingParameters;
            if (!camera.TryGetCullingParameters(camera.stereoEnabled, out cullingParameters))
                return;

            // Because it doesn't make sense to render shadow further than the camera can see, use the minimum of the shadow distance and the camera's far plane.
            cullingParameters.shadowDistance = Mathf.Min(shadowDistance, camera.farClipPlane);

#if UNITY_EDITOR
            if (CameraType.SceneView == camera.cameraType)
                // Although the UI works in the game window, it doesn't show up the scene window. 
                // The UI always exists in world space in the scene window, but we have to manually inject it into the scene. 
                // Adding the UI is done by invoking the static ScriptableRenderContext.
                // EmitWorldGeometryForSceneView method, with the current camera as an argument. This must be done before culling.
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif

            CullingResults cull = context.Cull(ref cullingParameters);

            if (cull.visibleLights.Length > 0)
            {
                ConfigureLights(context, cmd, cull);
                // Invoke this method after ConfigureLights, if there is a main light.
                if (mainLightIndex != -1)
                {
                    RenderCascadedShadows(context, shadowCmd, cull);
                }
                else
                {
                    cmd.DisableShaderKeyword(cascadedShadowsHardKeyword);
                    cmd.DisableShaderKeyword(cascadedShadowsSoftKeyword);
                }

                // The shadow map is to be rendered before the regular scene, so invoke RenderShadows in Render before we setup the regular camera, but after culling.
                RenderShadows(context, shadowCmd, cull);
            }
            else
            {
                cmd.DisableShaderKeyword(cascadedShadowsHardKeyword);
                cmd.DisableShaderKeyword(cascadedShadowsSoftKeyword);
                cmd.DisableShaderKeyword(shadowsSoftKeyword);
            }

            // We have to apply the camera's properties to the context, via the SetupCameraProperties method. 
            // That sets up the matrix as well as some other properties.
            // If don't set this function, the unity_MatrixVP matrix is always the same.
            context.SetupCameraProperties(camera);

            // Executing an empty command buffer does nothing. We added it so that we can clear the render target, 
            // to make sure that rendering isn't influenced by what was drawn earlier. 
            cmd.BeginSample("Clear Camera");
            CameraClearFlags clearFlags = camera.clearFlags;
            cmd.ClearRenderTarget(
                    (clearFlags & CameraClearFlags.Depth) != 0,
                    (clearFlags & CameraClearFlags.Color) != 0,
                    camera.backgroundColor
            );
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            cmd.EndSample("Clear Camera");

            var opaqueDrawSettings = new DrawingSettings(
                // The shader pass is identified via a string
                new ShaderTagId("ForwardBase"), 
                // Besides covering parts of the sky, opaque renderers can also end up obscuring each other. 
                // Ideally, only the one closest to the camera is drawn for each fragment of the frame buffer. 
                // So to reduce overdraw as much as possible, we should draw the nearest shapes first.
                new SortingSettings(camera) { criteria = SortingCriteria.CommonOpaque }
            );
            opaqueDrawSettings.enableDynamicBatching = enableDynamicBatching;
            opaqueDrawSettings.enableInstancing = enableInstanceing;

            // The default filter settings include nothing.
            var filterSettings = new FilteringSettings(RenderQueueRange.opaque, -1);

            // We draw the opaque renderers before the skybox to prevent overdraw.
            context.DrawRenderers(cull, ref opaqueDrawSettings, ref filterSettings);

            if (CameraClearFlags.Skybox == camera.clearFlags)
            {
                context.DrawSkybox(camera);
            }

            var transparentDrawSettings = new DrawingSettings(
                // The shader pass is identified via a string
                new ShaderTagId("ForwardBase"), 
                // However, transparent rendering works differently. 
                // It combines the color of what's being drawn with what has been drawn before, so the result appears transparent. 
                // That requires the reverse draw order, from back to front.
                new SortingSettings(camera) { criteria = SortingCriteria.CommonTransparent }
            );
            filterSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(cull, ref transparentDrawSettings, ref filterSettings);

#if UNITY_EDITOR
            DrawDefaultPipeline(context, camera);
#endif

            // We can instruct the context to execute the buffer via its ExecuteCommandBuffer method.
            // This doesn't immediately execute the commands, but copies them to the internal buffer of the context.
            context.ExecuteCommandBuffer(cmd);

            // Command buffers claim resources to store their commands at the native level of the Unity engine. 
            // If we no longer need these resources, it is best to release them immediately. 
            // This can be done by invoking the buffer's Release method, directly after invoking ExecuteCommandBuffer.
            cmd.Clear();

            // Also, make sure to release the render texture when we're done, after we've submitted the context.
            if (shadowMap)
            {
                RenderTexture.ReleaseTemporary(shadowMap);
                shadowMap = null;
            }

            if (cascadedShadowMap)
            {
                RenderTexture.ReleaseTemporary(cascadedShadowMap);
                cascadedShadowMap = null;
            }
        }

#if UNITY_EDITOR
        Material errorMaterial;

        void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
        {
            // At the start of DrawDefaultPipeline, create the error material if it doesn't already exist.
            if (null == errorMaterial)
            {
                Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
                errorMaterial = new Material(errorShader)
                {
                    // Set the material's hide flags to HideFlags.HideAndDontSave 
                    // so it doesn't show up in the project window and doesn't get saved along with all other assets.
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            var drawingSettings = new DrawingSettings(
                new ShaderTagId(), new SortingSettings()
            );
            drawingSettings.SetShaderPassName(0, new ShaderTagId("PrepassBase"));
            drawingSettings.SetShaderPassName(1, new ShaderTagId("Always"));
            drawingSettings.SetShaderPassName(2, new ShaderTagId("Vertex"));
            drawingSettings.SetShaderPassName(3, new ShaderTagId("VertexLMRGBM"));
            drawingSettings.SetShaderPassName(4, new ShaderTagId("VertexLM"));
            drawingSettings.overrideMaterial = errorMaterial;
            drawingSettings.overrideMaterialPassIndex = 0;
        }
#endif

        int mainLightIndex;
        void ConfigureLights(ScriptableRenderContext context, CommandBuffer cmd, CullingResults cull)
        {
            mainLightIndex = -1;
            // We can do that by counting how many shadowed spotlights we encounter in ConfigureLights.
            shadowTileCount = 0;

            for (int i = 0; i < cull.visibleLights.Length; i++)
            {
                if (i == maxVisibleLights)
                {
                    break;
                }
                
                VisibleLight light = cull.visibleLights[i];
                // The VisibleLight.finalColor field holds the light's color. 
                // It is the light's color multiplied by its intensity, and also converted to the correct color space.
                visibleLightColors[i] = light.finalColor;
                Vector4 attenuation = Vector4.zero;
                // To keep the spot fade calculation from affecting the other light types, set the W component of their attenuation vector to 1.
                attenuation.w = 1f;
                Vector4 shadow = Vector4.zero;
                if (light.lightType == LightType.Directional)
                {
                    // We use the direction from the surface toward the light source.
                    // So we have to negate the vector before we assign it to visibleLightDirections.
                    Vector4 v = light.localToWorldMatrix.GetColumn(2);
                    v.x = -v.x;
                    v.y = -v.y;
                    v.z = -v.z;
                    v.w = 0;
                    visibleLightDirections[i] = v;

                    shadow = ConfigureShadows(i, light.light, cull);
                    shadow.z = 1f;

                    if (shadow.x > 0f && mainLightIndex == -1)
                    {
                        mainLightIndex = i;
                        shadowTileCount -= 1;
                    }
                }
                else
                {
                    visibleLightDirections[i] = light.localToWorldMatrix.GetColumn(3);
                    attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.00001f);

                    // In ConfigureLights, when not dealing with a directional light, also check whether the light is a spotlight. 
                    // If so, setup the direction vector, just like for a directional light, but assign it to visibleLightSpotDirections instead.
                    if (light.lightType == LightType.Spot)
                    {
                        Vector4 v = light.localToWorldMatrix.GetColumn(2);
                        v.x = -v.x;
                        v.y = -v.y;
                        v.z = -v.z;
                        visibleLightSpotDirections[i] = v;

                        float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
					    float outerCos = Mathf.Cos(outerRad);
                        float outerTan = Mathf.Tan(outerRad);
					    float innerCos = Mathf.Cos(Mathf.Atan(((46f / 64f) * outerTan)));
                        float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
                        attenuation.z = 1f / angleRange;
                        attenuation.w = -outerCos * attenuation.z;

                        shadow = ConfigureShadows(i, light.light, cull);
                    }
                }
                visibleLightAttenuations[i] = attenuation;
                shadowData[i] = shadow;
            }
            cmd.SetGlobalVectorArray(visibleLightColorsId, visibleLightColors);
            cmd.SetGlobalVectorArray(visibleLightDirectionsOrPositionsId, visibleLightDirections);
            cmd.SetGlobalVectorArray(visibleLightAttenuationsId, visibleLightAttenuations);
            cmd.SetGlobalVectorArray(visibleLightSpotDirectionsId, visibleLightSpotDirections);

            context.ExecuteCommandBuffer(cmd);
		    cmd.Clear();
        }

        Vector4 ConfigureShadows (int lightIndex, Light shadowLight, CullingResults cull)
        {
            Vector4 shadow = Vector4.zero;
            Bounds shadowBounds;
            if (shadowLight.shadows != LightShadows.None && cull.GetShadowCasterBounds(lightIndex, out shadowBounds)) 
            {
                shadowTileCount += 1;
                shadow.x = shadowLight.shadowStrength;
                shadow.y = shadowLight.shadows == LightShadows.Soft ? 1f : 0f;
            }
            return shadow;
        }

        // Setting the render target for shadows.
        RenderTexture SetShadowRenderTarget (CommandBuffer cmd)
        {
            // Supply RenderTexture.GetTemporary with our map's width and height, the amount of bits used for the depth channel, and finally the texture format. 
            // We'll start with a fixed size of 512×512. We'll use 16 bits for the depth channel, so it is high-precision. 
            // As we're creating a shadow map, use the RenderTextureFormat.Shadowmap format.
            RenderTexture texture = RenderTexture.GetTemporary(shadowMapSize, shadowMapSize, 16, RenderTextureFormat.Shadowmap);
            // Make sure that the texture's filter mode is set the bilinear and its wrap mode is set to clamp.
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            // Before we can render shadows, we first have tell the GPU to render to our shadow map. 
            // A convenient way to do this is by invoking CoreUtils.SetRenderTarget with our command buffer and shadow map as arguments.
            CoreUtils.SetRenderTarget(
                cmd, texture,
                // We don't care where it comes from, as we clear it anyway, which we can indicate with RenderBufferLoadAction.DontCare.
                // That makes it possible for tile-based GPUs to be a bit more efficient. 
                // And we need to sample from the texture later, so it needs to be kept in memory, which we indicate with RenderBufferStoreAction.Store. 
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                // We only care about the depth channel, so only that channel needs to be cleared.
                ClearFlag.Depth
            );
            return texture;
        }

        // Determining the offset, setting the viewport, and scissoring can all be put together. 
        // The tile offset can be returned as a 2D vector.
        Vector2 ConfigureShadowTile (CommandBuffer cmd, int tileIndex, int split, float tileSize)
        {
            Vector2 tileOffset;
            tileOffset.x = tileIndex % split;
            tileOffset.y = tileIndex / split;
            var tileViewport = new Rect(
                tileOffset.x * tileSize, tileOffset.y * tileSize, tileSize, tileSize
            );
            cmd.SetViewport(tileViewport);
            cmd.EnableScissorRect(new Rect(
                tileViewport.x + 4f, tileViewport.y + 4f,
                tileSize - 8f, tileSize - 8f
            ));
            return tileOffset;
        }

        // Calculating the world-to-shadow matrix can be put in its own method too. 
        // Define the view and projection matrices as reference parameters so they don't need to be copied. 
        // Likewise, make the world-to-shadow matrix an output parameter.
        void CalculateWorldToShadowMatrix (ref Matrix4x4 viewMatrix, ref Matrix4x4 projectionMatrix,out Matrix4x4 worldToShadowMatrix)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                projectionMatrix.m20 = -projectionMatrix.m20;
                projectionMatrix.m21 = -projectionMatrix.m21;
                projectionMatrix.m22 = -projectionMatrix.m22;
                projectionMatrix.m23 = -projectionMatrix.m23;
            }
            var scaleOffset = Matrix4x4.identity;
            scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
            scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;
            worldToShadowMatrix = scaleOffset * (projectionMatrix * viewMatrix);
        }

        void RenderCascadedShadows (ScriptableRenderContext context, CommandBuffer cmd, CullingResults cull)
        {
            float tileSize = shadowMapSize / 2;
            cascadedShadowMap = SetShadowRenderTarget(cmd);
            cmd.BeginSample("Render Shadows");
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            Light shadowLight = cull.visibleLights[0].light;
            cmd.SetGlobalFloat(shadowBiasId, shadowLight.shadowBias);
            var shadowSettings = new ShadowDrawingSettings(cull, 0);
            var tileMatrix = Matrix4x4.identity;
            tileMatrix.m00 = tileMatrix.m11 = 0.5f;

            for (int i = 0; i < shadowCascades; i++)
            {
                Matrix4x4 viewMatrix, projectionMatrix;
                ShadowSplitData splitData;
                cull.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    0, i, shadowCascades, shadowCascadeSplit, (int)tileSize,
                    shadowLight.shadowNearPlane,
                    out viewMatrix, out projectionMatrix, out splitData
                );

                Vector2 tileOffset = ConfigureShadowTile(cmd, i, 2, tileSize);
                cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                
                shadowSettings.splitData = splitData;
                cascadeCullingSpheres[i] =shadowSettings.splitData.cullingSphere;
                cascadeCullingSpheres[i].w *= splitData.cullingSphere.w;
                context.DrawShadows(ref shadowSettings);
                CalculateWorldToShadowMatrix(
                    ref viewMatrix, ref projectionMatrix,
                    out worldToShadowCascadeMatrices[i]
                );
                tileMatrix.m03 = tileOffset.x * 0.5f;
                tileMatrix.m13 = tileOffset.y * 0.5f;
                worldToShadowCascadeMatrices[i] = tileMatrix * worldToShadowCascadeMatrices[i];
            }

            cmd.DisableScissorRect();
            cmd.SetGlobalTexture(cascadedShadowMapId, cascadedShadowMap);
            cmd.SetGlobalVectorArray(cascadeCullingSpheresId, cascadeCullingSpheres);
            cmd.SetGlobalMatrixArray(worldToShadowCascadeMatricesId, worldToShadowCascadeMatrices);
            // We also need to know the map's size and shadow strength.
            float invShadowMapSize = 1f / shadowMapSize;
            cmd.SetGlobalVector(cascadedShadowMapSizeId, new Vector4(invShadowMapSize, invShadowMapSize, shadowMapSize, shadowMapSize));
            cmd.SetGlobalFloat(cascadedShadoStrengthId, shadowLight.shadowStrength);
            bool hard = shadowLight.shadows == LightShadows.Hard;
            CoreUtils.SetKeyword(cmd, cascadedShadowsHardKeyword, hard);
            CoreUtils.SetKeyword(cmd, cascadedShadowsSoftKeyword, !hard);
            cmd.EndSample("Render Shadows");
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        void RenderShadows(ScriptableRenderContext context, CommandBuffer cmd, CullingResults cull)
        {
            // We'll determine how we have to split the shadow map at the beginning of RenderShadows. 
            int split;
            if (shadowTileCount <= 1)
            {
                split = 1;
            }
            else if (shadowTileCount <= 4)
            {
                split = 2;
            }
            else if (shadowTileCount <= 9)
            {
                split = 3;
            }
            else
            {
                split = 4;
            }
            // We have to constrain rendering to a viewport of that size, so create a Rect struct value at the start of RenderShadows with the appropriate size.
            float tileSize = shadowMapSize / split;
		    float tileScale = 1f / split;
		    Rect tileViewport = new Rect(0f, 0f, tileSize, tileSize);

            // We can make the disappearance of shadows more uniform by clipping them at the configured shadow distance. 
            // To do so, we have to pass the shadow distance to the shader. 
            // We can put it in the second component of the global shadow data vector.
            cmd.SetGlobalVector(globalShadowDataId, new Vector4(tileScale, shadowDistance * shadowDistance));

            shadowMap = SetShadowRenderTarget(cmd);

            cmd.BeginSample("Render Shadows");
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            // To pack all shadow maps in the available space, we must only increment the tile index when we used up a tile. 
            // So use a separate variable to keep track of it instead of relying on the light index. Increment it at the end of each iteration that we didn't skip.
            int tileIndex = 0;
            for (int i = 0; i < cull.visibleLights.Length; ++i)
            {
                if (maxVisibleLights == i)
                    break;

                // Skip the main light.
                if (mainLightIndex == i)
                    continue;

                // Each light that doesn't need a shadow map should be skipped. 
                // We can use the shadow strength that we put in the shadow data to determine this. 
                // If it's zero or less—either because that was the original strength or we left it at zero—directly go to the next iteration of the loop, 
                // by using the continue statement.
                if (shadowData[i].x <= 0f)
                    continue;

                Matrix4x4 viewMatrix, projectionMatrix;
                ShadowSplitData splitData;
                bool validShadows;
                if (shadowData[i].z > 0f) {
                    validShadows = cull.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                            i, 0, 1, Vector3.right, (int)tileSize,
                            cull.visibleLights[i].light.shadowNearPlane,
                            out viewMatrix, out projectionMatrix, out splitData
                        );
                }
                else
                {
                    validShadows = cull.ComputeSpotShadowMatricesAndCullingPrimitives(i, out viewMatrix, out projectionMatrix, out splitData);
                }
                if (!validShadows)
                {
                    // The ComputeSpotShadowMatricesAndCullingPrimitives method returns whether it was able to generate useful matrices. 
                    // It should agree with the result of GetShadowCasterBounds, but to be sure set the strength to zero and skip the light if it fails.
                    shadowData[i].x = 0f;
                    continue;
                }
                // All shadow maps now get rendered to a single tile in the corner of our render texture. 
                // The next step is to change the offset of the viewport for each light.
                Vector2 tileOffset = ConfigureShadowTile(cmd, tileIndex, split, tileSize);
                // float tileOffsetX = tileIndex % split;
                // float tileOffsetY = tileIndex / split;
                tileIndex += 1;
                // tileViewport.x = tileOffsetX * tileSize;
                // tileViewport.y = tileOffsetY * tileSize;
                shadowData[i].z = tileOffset.x * tileScale;
			    shadowData[i].w = tileOffset.y * tileScale;
                // shadowData[i].z = tileOffsetX * tileScale;
			    // shadowData[i].w = tileOffsetY * tileScale;
                // Tell the GPU to use the viewport by invoking SetViewport on the shadow command buffer when we're also setting the view and projection matrices.
                cmd.SetViewport(tileViewport);
                // Finally, if we end up with only a single tile it is not needed to set the viewport and change the scissor state at all. 
                // if (split > 1)
                {
                    // A downside of using an atlas is that sampling at the edge of a tile can lead to an interpolation between data from two tiles, which is incorrect. 
                    // This gets worse when using soft shadows, because the tent filter samples up to four texels away from the original sample position. 
                    // It's better to fade out shadows than mix data from adjacent tiles. 
                    // We can do this by adding an empty border around tiles, by instructing the GPU to limit the writing of data to 
                    // a region that's a bit smaller than the viewport. 
                    // This is known as scissoring, and we can do it by invoking cmd.
                    // EnableScissorRect with a rectangle that is a bit smaller than the viewport. 
                    // We need a border of four texels, so create another rect with four added to the viewport's position and eight subtracted from its size.
                    cmd.EnableScissorRect(new Rect(
                        tileViewport.x + 4f, tileViewport.y + 4f,
                        tileSize - 8f, tileSize - 8f
                    ));
                }
                cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var shadowSettings = new ShadowDrawingSettings(cull, i);
                // We can use the culling sphere to reduce the amount of shapes that have to be rendered into the shadow map by assigning it to the split data of the shadow settings.
                shadowSettings.splitData = splitData;
                context.DrawShadows(ref shadowSettings);

                // However, again there is a difference based on whether the clip-space Z dimension is reversed, which we can check via SystemInfo.usesReversedZBuffer. 
                // If so, we have to negate the Z-component row—the row with index 2—of the projection matrix before multiplying. 
                // We can do that by directly adjusting the m20 through m23 fields of the matrix.
                if (SystemInfo.usesReversedZBuffer)
                {
                    projectionMatrix.m20 = -projectionMatrix.m20;
                    projectionMatrix.m21 = -projectionMatrix.m21;
                    projectionMatrix.m22 = -projectionMatrix.m22;
                    projectionMatrix.m23 = -projectionMatrix.m23;
                }
                // We now have a conversion matrix from world space to shadow clip space. But clip space goes from −1 to 1, 
                // while texture coordinates and depth go from 0 to 1.
                // We can bake that range conversion into our matrix, via an additional multiplication with a matrix that scales and offsets by half a unit in all dimensions. 
                // We could use the Matrix4x4.TRS method to get such a matrix by providing a offset, rotation, and scale.
                // var scaleOffset = Matrix4x4.TRS(Vector3.one * 0.5f, Quaternion.identity, Vector3.one * 0.5f);
                // But as it is a simple matrix, we can also simply start with the identity matrix and set the appropriate fields.
                var scaleOffset = Matrix4x4.identity;
                scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
                scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;
                Matrix4x4 worldToShadowMatrix = scaleOffset * projectionMatrix * viewMatrix;
                CalculateWorldToShadowMatrix(ref viewMatrix, ref projectionMatrix, out worldToShadowMatrices[i]);
                // worldToShadowMatrices[i] = scaleOffset * (projectionMatrix * viewMatrix);

                // Finally, if we end up with only a single tile it is not needed to set the viewport and change the scissor state at all. 
                if (split > 1)
                {
                    // Adjust the world-to-shadow matrices so we end up sampling from the correct tile. 
                    // This is done by multiplying them with a matrix that scales and offsets X and Y appropriately. 
                    // The shader doesn't need to know that we're using an atlas.
                    // var tileMatrix = Matrix4x4.identity;
                    // tileMatrix.m00 = tileMatrix.m11 = tileScale;
                    // tileMatrix.m03 = tileOffsetX * tileScale;
                    // tileMatrix.m13 = tileOffsetY * tileScale;
                    // worldToShadowMatrices[i] = tileMatrix * worldToShadowMatrices[i];
                }
            }
            
            // Finally, if we end up with only a single tile it is not needed to set the viewport and change the scissor state at all. 
            // if (split > 1)
            {
                // We have to disable the scissor rectangle by invoking DisableScissorRect after we're done rendering shadows, otherwise regular rendering will be affected too.
                cmd.DisableScissorRect();
            }
            cmd.SetGlobalTexture(shadowMapId, shadowMap);
            cmd.SetGlobalMatrixArray(worldToShadowMatricesId, worldToShadowMatrices);
            // Shadow acne is caused by texels of the shadow map poking out of surfaces.
            // We'll support the simplest way to mitigate acne, which is by adding a small depth offset when rendering to the shadow map.
            cmd.SetGlobalFloat(shadowBiasId, cull.visibleLights[0].light.shadowBias);
            cmd.SetGlobalVectorArray(shadowDataId, shadowData);
            float invShadowMapSize = 1f / shadowMapSize;
            cmd.SetGlobalVector(shadowMapSizeId, new Vector4(invShadowMapSize, invShadowMapSize, shadowMapSize, shadowMapSize));

            CoreUtils.SetKeyword(cmd, shadowsSoftKeyword, cull.visibleLights[0].light.shadows == LightShadows.Soft);
            
            cmd.EndSample("Render Shadows");
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }
}