using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Filtering;
using Window = MathNet.Numerics.Window;

using MathNet.Numerics.IntegralTransforms;
using UnityEngine;

public class Pulse : MonoBehaviour
{
    [SerializeField] private float lowCutoff = 1.2f; 
    [SerializeField] private float highCutoff = 4.0f; 
    [SerializeField] private float sampleRate = 30f;
    [SerializeField] private float lowerLimitBpm = 40.0f;
    [SerializeField] private float upperLimitBpm = 200.0f;
    [SerializeField] private int windowSize = 512;
    [SerializeField] private float baseHighPulse = 100f;
    [SerializeField] private float currentPulse = 0f;
    
    [SerializeField] private MethodBase[] pulseMethods;
    
    private const int _nfftSize = 1024;

    private List<double[]> _rgbBuffer;
    private int _bufferIndex = 0;
    private bool _isBufferFull = false;

    private float averagePulseSampleSize = 30f;
    private List<float> avgPulse = new List<float>();
    private OnlineFilter bandpassFilter;
    
    private readonly List<(float bpm, DateTime timestamp)> _heartRateHistory = new List<(float bpm, DateTime timestamp)>();
    private readonly float _timeWindowSeconds = 15f;

    protected virtual void Start()
    {
        InitializeFilter();
        _rgbBuffer = new List<double[]>(new double[3][]); // RGB channels
        for (int i = 0; i < 3; i++)
            _rgbBuffer[i] = new double[windowSize];

        InvokeRepeating(nameof(ProcessPulseAndEstimateHeartRate), 0, 1);
    }

    private void InitializeFilter()
    {
        bandpassFilter = OnlineFilter.CreateBandpass(
            ImpulseResponse.Infinite,
            sampleRate,
            lowCutoff,
            highCutoff,
            3);
    }

    public virtual void ProcessFrame(Texture2D texture)
    {
        Color32[] pixels = texture.GetPixels32();
        
        long rSum = 0, gSum = 0, bSum = 0;
        foreach (Color32 pixel in pixels)
        {
            rSum += pixel.r;
            gSum += pixel.g;
            bSum += pixel.b;
        }

        int pixelCount = pixels.Length;
        _rgbBuffer[0][_bufferIndex] = bSum / (double)pixelCount; // B
        _rgbBuffer[1][_bufferIndex] = gSum / (double)pixelCount; // G
        _rgbBuffer[2][_bufferIndex] = rSum / (double)pixelCount; // R

        _bufferIndex++;
        if (_bufferIndex >= windowSize)
        {
            _bufferIndex = 0;
            _isBufferFull = true;
        }
    }


    protected void ProcessPulseAndEstimateHeartRate()
    {

        if (_isBufferFull)
        {
            double[][] rgbSignals = new double[3][];
            for (int i = 0; i < 3; i++)
                rgbSignals[i] = _rgbBuffer[i];

            ExtractPulseSignal(rgbSignals);
        }
    }

    private void ExtractPulseSignal(double[][] rgbSignals)
    {
        /*
           float bpm = EstimateHeartRate(signal);
           UpdateHeartRate(bpm);
         */
    }

    private double[][] ProcessSignal(double[][] rawSignal)
    {

        int numChannels = rawSignal.Length;
        double[][] processedSignals = new double[numChannels][];

        for (int i = 0; i < numChannels; i++)
        {
            double[] signal = rawSignal[i];

            double mean = signal.Average();
            double[] normalized = signal.Select(x => x - mean).ToArray();

            double stdDev = Math.Sqrt(normalized.Select(x => x * x).Average());
            if (stdDev > 0)
            {
                normalized = normalized.Select(x => x / stdDev).ToArray();
            }

            double[] detrended = DetrendSignal(normalized);

            double[] filtered = bandpassFilter.ProcessSamples(detrended);

            double[] window = Window.Hamming(filtered.Length);
            double[] windowed = filtered.Zip(window, (s, w) => s * w).ToArray();

            processedSignals[i] = windowed;

        }

        return processedSignals;
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

    private float EstimateHeartRate(double[][] signals)
    {
        int numChannels = signals.Length;
        float[] bpmEstimates = new float[numChannels];

        for (int i = 0; i < numChannels; i++)
        {
            double[] psd = CalculateWelchPSD(signals[i]);
            bpmEstimates[i] = CalculateBPMFromPSD(psd);
        }

        float currentPulse = bpmEstimates.Average();

        avgPulse.Add(currentPulse);

        if (avgPulse.Count > averagePulseSampleSize)
        {
            avgPulse.RemoveAt(0);
        }

        float averagePulseValue = avgPulse.Average();

        return averagePulseValue;
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

    void UpdateHeartRate(float bpm)
    {
        DateTime currentTime = DateTime.Now;
        currentPulse = bpm;
        _heartRateHistory.Add((bpm, currentTime));
        _heartRateHistory.RemoveAll(item => (currentTime - item.timestamp).TotalSeconds > _timeWindowSeconds);
      
    }
}
