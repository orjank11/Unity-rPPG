using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using UnityEngine;

public class Pos : MethodBase
{
    private readonly float SampleRate = 30f; // TODO:  Fix so it's not a hard coded value
    
        // POS algorithm projection matrix as defined in the original paper
    private readonly double[,] _projection = new double[,] { 
        { 0, 1, -1 }, 
        { -2, 1, 1 } 
    };

    protected override double[][] ExtractPulseSignal(double[][] rgbSignals)
    {
        int windowLength = (int)(1.6 * SampleRate);
        int totalSamples = rgbSignals[0].Length;
        
        double[] result = new double[totalSamples];
        
        Matrix<double> X = Matrix<double>.Build.DenseOfRowArrays(rgbSignals);
        
        for (int n = 0; n < totalSamples; n++)
        {
            int m = n - windowLength + 1;
            
            if (m >= 0)
            {
                Matrix<double> window = X.SubMatrix(0, 3, m, n - m + 1);
                
                Matrix<double> normalizationMatrix = GetNormalizationMatrix(window);
                Matrix<double> normalizedWindow = normalizationMatrix.Multiply(window);
                
                Matrix<double> projectionMatrix = Matrix<double>.Build.DenseOfArray(_projection);
                Matrix<double> s = projectionMatrix.Multiply(normalizedWindow);
                
                double[] s0 = s.Row(0).ToArray();
                double[] s1 = s.Row(1).ToArray();
                
                double stdS0 = StandardDeviation(s0);
                double stdS1 = StandardDeviation(s1);
                
                double[] hn = new double[s0.Length];
                for (int i = 0; i < hn.Length; i++)
                {
                    hn[i] = s0[i] + (stdS0 / stdS1) * s1[i];
                }
                
                double mean = hn.Average();
                for (int i = 0; i < hn.Length; i++)
                {
                    hn[i] -= mean;
                }
                
                for (int i = 0; i < hn.Length; i++)
                {
                    result[m + i] += hn[i];
                }
            }
        }
        
        return new double[][] { result };
    }
    
    
    private Matrix<double> GetNormalizationMatrix(Matrix<double> x)
    {
        Vector<double> means = Vector<double>.Build.Dense(x.RowCount);
        
        for (int i = 0; i < x.RowCount; i++)
        {
            means[i] = x.Row(i).Average();
        }
        
        Matrix<double> normMatrix = Matrix<double>.Build.Sparse(x.RowCount, x.RowCount);
        
        for (int i = 0; i < x.RowCount; i++)
        {
            if (means[i] != 0)
            {
                normMatrix[i, i] = 1.0 / means[i];
            }
        }
        
        return normMatrix;
    }
}
