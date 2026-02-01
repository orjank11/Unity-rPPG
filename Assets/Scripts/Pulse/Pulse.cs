using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Filtering;
using Window = MathNet.Numerics.Window;
using MathNet.Numerics.IntegralTransforms;
using UnityEngine;

public class Pulse : MonoBehaviour
{
    [System.Serializable]
    public class PulsePipeline
    {
        public string name;
        public MethodBase method;
        public float currentBpm;
        
        internal OnlineFilter filter;
        internal List<float> bpmHistory = new List<float>();
    }

    [Header("Configuration")]
    [SerializeField] private float lowCutoff = 0.7f;  // ~42 BPM
    [SerializeField] private float highCutoff = 4.0f; // ~240 BPM
    [SerializeField] private float sampleRate = 30f;
    [SerializeField] private float lowerLimitBpm = 40.0f;
    [SerializeField] private float upperLimitBpm = 200.0f;
    [SerializeField] private int windowSize = 512;
    [SerializeField] private float averagePulseSampleSize = 15f;

    [Header("Pipelines")]
    [SerializeField] private List<PulsePipeline> pipelines;

    private const int _nfftSize = 1024;
    private List<double[]> _rgbBuffer;
    private int _bufferIndex = 0;
    private bool _isBufferFull = false;
    
    private readonly float _timeWindowSeconds = 15f;

    protected virtual void Start()
    {
        _rgbBuffer = new List<double[]>(3);
        for (int i = 0; i < 3; i++) _rgbBuffer.Add(new double[windowSize]);

        foreach (var pipe in pipelines)
        {
            if (pipe.method == null) continue;

            if (string.IsNullOrEmpty(pipe.name)) 
                pipe.name = pipe.method.GetType().Name;

            pipe.filter = OnlineFilter.CreateBandpass(
                ImpulseResponse.Infinite,
                sampleRate,
                lowCutoff,
                highCutoff,
                2);
                
            pipe.bpmHistory = new List<float>();
        }

        InvokeRepeating(nameof(ProcessPulseAndEstimateHeartRate), 1f, 0.5f);
    }
    
    void OnEnable() => FaceDetection.OnRoiExtracted += ProcessFrame;
    void OnDisable() => FaceDetection.OnRoiExtracted -= ProcessFrame;

    // --- DATA INGESTION (Unchanged) ---
    public virtual void ProcessFrame(Color32[] pixels)
    {
        long rSum = 0, gSum = 0, bSum = 0;
        foreach (Color32 pixel in pixels)
        {
            rSum += pixel.r;
            gSum += pixel.g;
            bSum += pixel.b;
        }

        int count = pixels.Length;
        _rgbBuffer[0][_bufferIndex] = bSum / (double)count;
        _rgbBuffer[1][_bufferIndex] = gSum / (double)count;
        _rgbBuffer[2][_bufferIndex] = rSum / (double)count;

        _bufferIndex++;
        if (_bufferIndex >= windowSize)
        {
            _bufferIndex = 0;
            _isBufferFull = true;
        }
    }

    protected void ProcessPulseAndEstimateHeartRate()
    {
        if (!_isBufferFull) return;

        double[][] sortedRgb = new double[3][];
        for (int i = 0; i < 3; i++)
        {
            sortedRgb[i] = new double[windowSize];
            Array.Copy(_rgbBuffer[i], _bufferIndex, sortedRgb[i], 0, windowSize - _bufferIndex);
            Array.Copy(_rgbBuffer[i], 0, sortedRgb[i], windowSize - _bufferIndex, _bufferIndex);
        }

        foreach (var pipe in pipelines)
        {
            if (pipe.method == null) continue;

            double[][] rawSignals = pipe.method.ExtractPulseSignal(sortedRgb);
            
            double[][] processedSignals = ProcessSignal(rawSignals, pipe.filter);

            float bpm = EstimateHeartRate(processedSignals, pipe.bpmHistory);

            if (bpm > lowerLimitBpm && bpm < upperLimitBpm)
            {
                pipe.currentBpm = bpm;
                Debug.Log($"Method: <b>{pipe.name}</b> | BPM: {bpm:F1}");
            }
        }
    }

    private double[][] ProcessSignal(double[][] rawSignal, OnlineFilter filter)
    {
        int numChannels = rawSignal.Length;
        double[][] processed = new double[numChannels][];

        for (int i = 0; i < numChannels; i++)
        {
            double mean = rawSignal[i].Average();
            double[] norm = rawSignal[i].Select(x => x - mean).ToArray();
            
            double[] detrended = DetrendSignal(norm);

            double[] filtered = filter.ProcessSamples(detrended);

            double[] window = Window.Hamming(filtered.Length);
            processed[i] = filtered.Zip(window, (s, w) => s * w).ToArray();
        }
        return processed;
    }
    
    private float EstimateHeartRate(double[][] signals, List<float> history)
    {
        float[] bpmEstimates = new float[signals.Length];
        for (int i = 0; i < signals.Length; i++)
        {
            double[] psd = CalculateWelchPSD(signals[i]);
            bpmEstimates[i] = CalculateBPMFromPSD(psd);
        }

        float instantBpm = bpmEstimates.Average();

        history.Add(instantBpm);
        if (history.Count > averagePulseSampleSize) history.RemoveAt(0);

        return history.Average();
    }

    private double[] DetrendSignal(double[] signal)
    {
        int n = signal.Length;

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += signal[i];
            sumXY += i * signal[i];
            sumX2 += i * i;
        }

        double slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        double intercept = (sumY - slope * sumX) / n;

        double[] detrended = new double[n];
        for (int i = 0; i < n; i++)
        {
            double trend = intercept + slope * i;
            detrended[i] = signal[i] - trend;
        }

        return detrended;
    }


    private double[] CalculateWelchPSD(double[] signal)
    {
        int segmentLength = signal.Length;
        int overlap = segmentLength / 4;

        int hopSize = segmentLength - overlap;
        int numSegments = (signal.Length - segmentLength) / hopSize + 1;

        if (numSegments < 1)
            numSegments = 1;

        double[] psd = new double[_nfftSize / 2 + 1];

        double[] window = Window.Hamming(segmentLength);
        double windowPowerCorrection = window.Select(x => x * x).Sum() / segmentLength;

        double[] fftBuffer = new double[_nfftSize + 2];

        for (int i = 0; i < numSegments; i++)
        {
            int start = i * hopSize;

            Array.Clear(fftBuffer, 0, fftBuffer.Length);

            for (int j = 0; j < Math.Min(segmentLength, signal.Length - start); j++)
            {
                fftBuffer[j] = signal[start + j] * window[j];
            }

            Fourier.ForwardReal(fftBuffer, _nfftSize);

            for (int k = 0; k <= _nfftSize / 2; k++)
            {
                double real = fftBuffer[2 * k];
                double imag = (2 * k + 1 < fftBuffer.Length) ? fftBuffer[2 * k + 1] : 0;

                double power = (real * real + imag * imag) / (sampleRate * windowPowerCorrection);
                psd[k] += power / numSegments;
            }
        }

        return psd;
    }

    private float CalculateBPMFromPSD(double[] psd)
    {
        double[] freqs = new double[psd.Length];
        for (int i = 0; i < freqs.Length; i++)
        {
            freqs[i] = i * (sampleRate / _nfftSize);
        }

        int startIdx = 0;
        while (startIdx < freqs.Length && freqs[startIdx] < lowCutoff)
        {
            startIdx++;
        }

        int endIdx = startIdx;
        while (endIdx < freqs.Length && freqs[endIdx] <= highCutoff)
        {
            endIdx++;
        }

        endIdx = Math.Max(startIdx, endIdx - 1);

        if (startIdx >= endIdx)
        {
            return 75f; // Default heart rate if no peak found
        }

        int maxIdx = startIdx;
        double maxPower = psd[startIdx];

        for (int i = startIdx + 1; i <= endIdx; i++)
        {
            if (psd[i] > maxPower)
            {
                maxPower = psd[i];
                maxIdx = i;
            }
        }

        float bpm = (float)(freqs[maxIdx] * 60.0);
        bpm = Math.Max(lowerLimitBpm, Math.Min(upperLimitBpm, bpm));
        return bpm;
    }

}