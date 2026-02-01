using UnityEngine;

public class Chrom : MethodBase
{
    public override double[][] ExtractPulseSignal(double[][] rgbSignals)
    {
        int signalLength = rgbSignals[0].Length;
    
        double[] Xcomp = new double[signalLength];
        double[] Ycomp = new double[signalLength];
    
        for (int i = 0; i < signalLength; i++)
        {
            Xcomp[i] = 3 * rgbSignals[2][i] - 2 * rgbSignals[1][i];
        
            Ycomp[i] = (1.5 * rgbSignals[2][i]) + rgbSignals[1][i] - (1.5 * rgbSignals[0][i]);
        }
    
        double sX = StandardDeviation(Xcomp);
        double sY = StandardDeviation(Ycomp);
        double alpha = sX / sY;
    
        double[] bvp = new double[signalLength];
        for (int i = 0; i < signalLength; i++)
        {
            bvp[i] = Xcomp[i] - alpha * Ycomp[i];
        }
    
        return new double[][] { bvp };
    }
}
