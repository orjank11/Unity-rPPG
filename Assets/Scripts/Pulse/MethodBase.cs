using System;
using System.Linq;
using UnityEngine;

public abstract class MethodBase : MonoBehaviour
{
    protected abstract double[][] ExtractPulseSignal(double[][] rgbSignals);
    
    protected double StandardDeviation(double[] values)
    {
        double mean = values.Average();
        double sumOfSquaresOfDifferences = values.Sum(val => (val - mean) * (val - mean));
        return Math.Sqrt(sumOfSquaresOfDifferences / values.Length);
    }
}
