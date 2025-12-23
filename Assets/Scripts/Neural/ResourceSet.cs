using UnityEngine;
using Unity.InferenceEngine;

namespace Neural
{
    [CreateAssetMenu(fileName = "Neural",
        menuName = "ScriptableObjects/Neural Resource Set")]
    public sealed class ResourceSet : ScriptableObject
    {
        public ModelAsset model;
        public ComputeShader preprocess;
        public ComputeShader postprocess1;
        public ComputeShader postprocess2;
    }
}