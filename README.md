# Remote Photoplethysmography (rPPG) in Unity

Real-time webcam-based pulse detection implementation for Unity. This project extracts the Blood Volume Pulse (BVP) signal from webcam using the **Unity Inference Engine** for face tracking and various signal processing algorithms for heart rate estimation.

## 🚀 Features

* **Face Detection:** Uses **BlazeFace (ONNX)** Unity Inference Engine for face tracking and Region of Interest (ROI) selection.
* **Real-time Processing:** RGB signal extraction.
* **Multiple Algorithms:** 
    * **GREEN:** Using the green color channel.
    * **CHROM:** Chrominance-based method.
    * **POS:** Plane-Orthogonal-to-Skin method.

## 📚 References

### Models & Samples
* **BlazeFace Model:** [Hugging Face - Unity Sentis BlazeFace](https://huggingface.co/unity/inference-engine-blaze-face)
* **Sentis Face Detection Sample:** [Unity-Technologies/sentis-samples](https://github.com/Unity-Technologies/sentis-samples/tree/main/BlazeDetectionSample/Face)

### Scientific Papers
* **Green (2008):** Verkruysse, W., et al. ["Remote plethysmographic imaging using ambient light."](https://doi.org/10.1364/OE.16.021434) *Optics Express.*
* **CHROM (2013):** De Haan, G., & Jeanne, V. ["Robust Pulse Rate from Chrominance-Based rPPG."](https://doi.org/10.1109/TBME.2013.2267162) *IEEE Transactions on Biomedical Engineering.*
* **POS (2017):** Wang, W., et al. ["Algorithmic Principles of Remote PPG."](https://doi.org/10.1109/TBME.2016.2609282) *IEEE Transactions on Biomedical Engineering.*


## 📝 License
MIT
