using UnityEngine;
using Unity.InferenceEngine;

namespace Neural
{
    [CreateAssetMenu(fileName = "UltraFace",
        menuName = "ScriptableObjects/UltraFace Resource Set")]
    public sealed class ResourceSet : ScriptableObject
    {
        public ModelAsset model;
        public ComputeShader preprocess;
        public ComputeShader postprocess1;
        public ComputeShader postprocess2;
    }
}