using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace Neural {

    struct Config
    {
        #region Compile-time constants

        // These values must be matched with the ones defined in Common.hlsl.
        public const int MaxDetection = 512;

        #endregion

        #region Variables from tensor shapes

        public int InputWidth { get; private set; }
        public int InputHeight { get; private set; }
        public int OutputCount { get; private set; }

        #endregion

        #region Data size calculation properties

        public int InputFootprint => InputWidth * InputWidth * 3;

        #endregion

        #region Tensor shape utilities

        public TensorShape InputShape
            => new TensorShape(1, 3, InputHeight, InputWidth);

        #endregion

        #region Constructor

       public Config(ResourceSet resources, Model model)
        {
            // Get input shape
            var inShape = model.inputs[0].shape.ToIntArray();
            
            Debug.Log($"Input shape length: {inShape.Length}, values: [{string.Join(", ", inShape)}]");
            
            var shapesField = typeof(Model).GetField("shapes", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var shapes = (Dictionary<int, DynamicTensorShape>)shapesField.GetValue(model);
            var outShape = shapes[model.outputs[0].index].ToIntArray();
            
            Debug.Log($"Output shape length: {outShape.Length}, values: [{string.Join(", ", outShape)}]");
            
            if (inShape.Length == 4)
            {
                if (inShape[1] == 3 || inShape[1] == 1) 
                {
                    InputHeight = inShape[2];
                    InputWidth = inShape[3];
                }
                else
                {
                    InputHeight = inShape[1];
                    InputWidth = inShape[2];
                }
            }
            else
            {
                Debug.LogError($"Unexpected input shape length: {inShape.Length}");
                InputHeight = 480;
                InputWidth = 640;
            }
            
            if (outShape.Length == 3)
            {
                OutputCount = outShape[1]; 
            }
            else if (outShape.Length == 4)
            {
                OutputCount = outShape[1] * outShape[2];
            }
            else if (outShape.Length == 2)
            {
                OutputCount = outShape[1];
            }
            else
            {
                Debug.LogError($"Unexpected output shape length: {outShape.Length}");
                OutputCount = 17640;
            }
            
            Debug.Log($"Parsed config - InputWidth: {InputWidth}, InputHeight: {InputHeight}, OutputCount: {OutputCount}");
        }


        #endregion
    }

} // namespace UltraFace