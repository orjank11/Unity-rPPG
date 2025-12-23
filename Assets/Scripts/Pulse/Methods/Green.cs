using UnityEngine;

public class Green : MethodBase
{
    protected override double[][] ExtractPulseSignal(double[][] rgbSignals)
    {
        return new double[][] { rgbSignals[1] };
    }

}
