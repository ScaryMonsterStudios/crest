// Crest Ocean System

// Copyright 2021 Wave Harmonic Ltd

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Crest
{
    // TODO: there's a bug somewhere, frame 0 is black
    public class FFTBaker
    {
        public static bool Bake(ShapeFFT fftWaves, int resolutionSpace, int resolutionTime, float wavePatchSize)
        {
            Debug.Assert(Mathf.IsPowerOfTwo(resolutionSpace), "Crest: Spatial resolution must be power of 2");
            // Debug.Assert(Mathf.IsPowerOfTwo(resolutionTime), "Crest: Temporal resolution must be power of 2"); // seems unnecessary
            Debug.Assert(Mathf.IsPowerOfTwo((int)wavePatchSize), "Crest: Spatial path size must be power of 2");

            // create the staging texture wavePatchData that is written to then downloaded from the GPU
            var desc = new RenderTextureDescriptor
            {
                width = resolutionSpace,
                height = resolutionSpace,
                volumeDepth = 1,
                autoGenerateMips = false,
                colorFormat = RenderTextureFormat.ARGBFloat,
                enableRandomWrite = true,
                dimension = TextureDimension.Tex2D,
                depthBufferBits = 0,
                msaaSamples = 1,
                sRGB = false,
            };
            var wavePatchData = new RenderTexture(desc);
            var stagingTexture = new Texture2D(desc.width, desc.height, TextureFormat.RFloat, false, true);

            var buf = new CommandBuffer();

            var waveCombineShader = Resources.Load<ComputeShader>("FFT/FFTBake");
            var kernel = waveCombineShader.FindKernel("FFTBake");

            const string folderName = "BakedWave";
            if (Directory.Exists(folderName))
            {
                Directory.Delete(folderName, true);
            }
            
            Directory.CreateDirectory(folderName);
            
            var period = fftWaves._spectrum._period;
            var frameCount = (int)(resolutionTime * period); // not so sure about this
            var frames = new float[frameCount][];

            for (int timeIndex = 0; timeIndex < frameCount; timeIndex++) // this means resolutionTime is actually FPS
            {
                float t = timeIndex / (float)resolutionTime;

                buf.Clear();

                // Generate multi-res FFT into a texture array
                var fftWaveDataTA = FFTCompute.GenerateDisplacements(buf, fftWaves._resolution, fftWaves._windTurbulence, fftWaves.WindDirRadForFFT, fftWaves.WindSpeedForFFT, t, fftWaves._spectrum, true);

                // Compute shader generates the final waves
                buf.SetComputeFloatParam(waveCombineShader, "_WavePatchSize", wavePatchSize);
                buf.SetComputeTextureParam(waveCombineShader, kernel, "_InFFTWaves", fftWaveDataTA);
                buf.SetComputeTextureParam(waveCombineShader, kernel, "_OutHeights", wavePatchData);
                buf.DispatchCompute(waveCombineShader, kernel, resolutionSpace / 8, resolutionSpace / 8, 1);
                Graphics.ExecuteCommandBuffer(buf);

                // Readback data to CPU
                // what was the trick to doing this again? copy the render texture to a normal texture then read it back? urgh
                //var data = wavePatchData.GetPixels();
                RenderTexture.active = wavePatchData;
                stagingTexture.ReadPixels(new Rect(0, 0, wavePatchData.width, wavePatchData.height), 0, 0); // is this correct??

                // data[i].r should have height values. store data somehow/somewhere..
                // var data = stagingTexture.GetPixels();

                frames[timeIndex] = stagingTexture.GetRawTextureData<float>().ToArray();
                var encodedTexture = stagingTexture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
                
                File.WriteAllBytes($"{folderName}/test_{timeIndex}.exr", encodedTexture);
            }
            
            var bakedDataSO = ScriptableObject.CreateInstance<FFTBakedData>();
            bakedDataSO.Initialize(period, resolutionTime, frames, resolutionSpace);
            
            SaveBakedDataToAsset(folderName, bakedDataSO);
            // AssetDatabase.Refresh();

            // Save the data for each slice to disk - in some format?

            // Separately there should be a FFTCollisionProvider and the SimSettingsAnimWaves should allow
            // selecting this provider type, and also have a field for selecting the exported data.

            return true;
        }

        private static void SaveBakedDataToAsset(string folderName, FFTBakedData bakedDataSO)
        {
            var bakedDataDirectory = $"Assets/{folderName}";
            if (!AssetDatabase.IsValidFolder(bakedDataDirectory))
            {
                Debug.Log("Folder not found, creating");
                AssetDatabase.CreateFolder("Assets", folderName);
            }

            AssetDatabase.CreateAsset(bakedDataSO, $"{bakedDataDirectory}/bakedTest.asset");
            // AssetDatabase.SaveAssets();
        }
    }
}