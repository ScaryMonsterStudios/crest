﻿// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

using static Crest.UnderwaterPostProcessUtils;

namespace Crest
{
    /// <summary>
    /// Underwater Post Process. If a camera needs to go underwater it needs to have this script attached. This adds fullscreen passes and should
    /// only be used if necessary. This effect disables itself when camera is not close to the water volume.
    ///
    /// For convenience, all shader material settings are copied from the main ocean shader. This includes underwater
    /// specific features such as enabling the meniscus.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class UnderwaterPostProcess : MonoBehaviour, IUnderwaterPostProcessPerCameraData
    {
        [Header("Settings"), SerializeField, Tooltip("If true, underwater effect copies ocean material params each frame. Setting to false will make it cheaper but risks the underwater appearance looking wrong if the ocean material is changed.")]
        bool _copyOceanMaterialParamsEachFrame = true;

        [SerializeField, Tooltip("Assign this to a material that uses shader `Crest/Underwater/Post Process`, with the same features enabled as the ocean surface material(s).")]
        Material _underwaterPostProcessMaterial;

        [Header("Debug Options"), SerializeField]
        bool _viewOceanMask = false;
        // end public debug options

        private Camera _mainCamera;
        private RenderTexture _textureMask;
        private RenderTexture _depthBuffer;
        private CommandBuffer _commandBuffer;

        private Material _oceanMaskMaterial = null;
        private Material _generalMaskMaterial = null;

        private PropertyWrapperMaterial _underwaterPostProcessMaterialWrapper;

        private List<Renderer> _oceanChunksToRender;
        public List<Renderer> OceanChunksToRender => _oceanChunksToRender;

        private List<Renderer> _generalUnderwaterMasksToRender;
        public List<Renderer> GeneralUnderwaterMasksToRender => _generalUnderwaterMasksToRender;

        private const string SHADER_OCEAN_MASK = "Crest/Underwater/Ocean Mask";
        private const string SHADER_GENERAL_MASK = "Crest/Underwater/General Underwater Mask";

        UnderwaterSphericalHarmonicsData _sphericalHarmonicsData = new UnderwaterSphericalHarmonicsData();

        bool _eventsRegistered = false;
        bool _firstRender = true;


        public void RegisterOceanChunkToRender(Renderer _oceanChunk)
        {
            _oceanChunksToRender.Add(_oceanChunk);
        }

        public void RegisterGeneralUnderwaterMaskToRender(Renderer _renderer)
        {
            _generalUnderwaterMasksToRender.Add(_renderer);
        }

        private bool InitialisedCorrectly()
        {
            _mainCamera = GetComponent<Camera>();
            if (_mainCamera == null)
            {
                Debug.LogError("UnderwaterPostProcess must be attached to a camera", this);
                return false;
            }

            if (_underwaterPostProcessMaterial == null)
            {
                Debug.LogError("UnderwaterPostProcess must have a post processing material assigned", this);
                return false;
            }

            {
                var maskShader = Shader.Find(SHADER_OCEAN_MASK);
                _oceanMaskMaterial = maskShader ? new Material(maskShader) : null;
                if (_oceanMaskMaterial == null)
                {
                    Debug.LogError($"Could not create a material with shader {SHADER_OCEAN_MASK}", this);
                    return false;
                }
            }

            {
                var generalMaskShader = Shader.Find(SHADER_GENERAL_MASK);
                _generalMaskMaterial = generalMaskShader ? new Material(generalMaskShader) : null;
                if (_generalMaskMaterial == null)
                {
                    Debug.LogError($"Could not create a material with shader {SHADER_GENERAL_MASK}", this);
                    return false;
                }
            }

            if (OceanRenderer.Instance && !OceanRenderer.Instance.OceanMaterial.IsKeywordEnabled("_UNDERWATER_ON"))
            {
                Debug.LogError("Underwater must be enabled on the ocean material for UnderwaterPostProcess to work", this);
                return false;
            }

            return CheckMaterial();
        }

        bool CheckMaterial()
        {
            var success = true;

            var keywords = _underwaterPostProcessMaterial.shaderKeywords;
            foreach (var keyword in keywords)
            {
                if (keyword == "_COMPILESHADERWITHDEBUGINFO_ON") continue;

                if (!OceanRenderer.Instance.OceanMaterial.IsKeywordEnabled(keyword))
                {
                    Debug.LogWarning($"Keyword {keyword} was enabled on the underwater material {_underwaterPostProcessMaterial.name} but not on the ocean material {OceanRenderer.Instance.OceanMaterial.name}, underwater appearance may not match ocean surface in standalone builds.", this);

                    success = false;
                }
            }

            return success;
        }

        void Start()
        {
            if (!InitialisedCorrectly())
            {
                enabled = false;
                return;
            }

            // Stop the material from being saved on-edits at runtime
            _underwaterPostProcessMaterial = new Material(_underwaterPostProcessMaterial);
            _underwaterPostProcessMaterialWrapper = new PropertyWrapperMaterial(_underwaterPostProcessMaterial);

            _oceanChunksToRender = new List<Renderer>(OceanBuilder.GetChunkCount);
            _generalUnderwaterMasksToRender = new List<Renderer>();
        }

        private void OnDestroy()
        {
            if (OceanRenderer.Instance && _eventsRegistered)
            {
                OceanRenderer.Instance.ViewerLessThan2mAboveWater -= ViewerLessThan2mAboveWater;
                OceanRenderer.Instance.ViewerMoreThan2mAboveWater -= ViewerMoreThan2mAboveWater;
            }

            _eventsRegistered = false;
        }

        private void ViewerMoreThan2mAboveWater(OceanRenderer ocean)
        {
            // TODO(TRC):Now sort this out
            // enabled = false;
        }

        private void ViewerLessThan2mAboveWater(OceanRenderer ocean)
        {
            enabled = true;
        }

        void OnRenderImage(RenderTexture source, RenderTexture target)
        {
            if (OceanRenderer.Instance == null)
            {
                Graphics.Blit(source, target);
                _eventsRegistered = false;
                return;
            }

            if (!_eventsRegistered)
            {
                OceanRenderer.Instance.ViewerLessThan2mAboveWater += ViewerLessThan2mAboveWater;
                OceanRenderer.Instance.ViewerMoreThan2mAboveWater += ViewerMoreThan2mAboveWater;
                // TODO(TRC):Now sort this out
                // enabled = OceanRenderer.Instance.ViewerHeightAboveWater < 2f;
                _eventsRegistered = true;
            }

            if (_commandBuffer == null)
            {
                _commandBuffer = new CommandBuffer();
                _commandBuffer.name = "Underwater Post Process";
            }

            if (GL.wireframe)
            {
                Graphics.Blit(source, target);
                _oceanChunksToRender.Clear();
                _generalUnderwaterMasksToRender.Clear();
                return;
            }

            InitialiseMaskTextures(source, ref _textureMask, ref _depthBuffer, new Vector2Int(source.width, source.height));
            PopulateUnderwaterMasks(
                _commandBuffer, _mainCamera, this,
                _textureMask.colorBuffer, _depthBuffer.depthBuffer,
                _oceanMaskMaterial, _generalMaskMaterial
            );

            UpdatePostProcessMaterial(
                source,
                _mainCamera,
                _underwaterPostProcessMaterialWrapper,
                _textureMask,
                _depthBuffer,
                _sphericalHarmonicsData,
                _firstRender || _copyOceanMaterialParamsEachFrame,
                _viewOceanMask
            );

            _commandBuffer.Blit(source, target, _underwaterPostProcessMaterial);

            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();

            // Need this to prevent Unity from giving the following warning:
            // - "OnRenderImage() possibly didn't write anything to the destination texture!"
            Graphics.SetRenderTarget(target);

            _firstRender = false;
        }
    }
}