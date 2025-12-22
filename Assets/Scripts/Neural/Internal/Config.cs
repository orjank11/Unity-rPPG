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
            => new TensorShape(1, InputHeight, InputWidth, 3);

        #endregion

        #region Constructor

        public Config(ResourceSet resources, Model model)
        {
            var inShape = model.inputs[0].shape.ToIntArray();
                // Access internal shapes dictionary
            var shapesField = typeof(Model).GetField("shapes", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var shapes = (Dictionary<int, DynamicTensorShape>)shapesField.GetValue(model);
            var outShape = shapes[model.outputs[0].index].ToIntArray();
    
            InputWidth = inShape[6];
            InputHeight = inShape[5];
            OutputCount = outShape[6];
        }

        #endregion
    }

} // namespace UltraFace