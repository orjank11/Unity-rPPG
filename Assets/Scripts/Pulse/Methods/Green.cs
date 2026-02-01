using UnityEngine;

public class Green : MethodBase
{
    public override double[][] ExtractPulseSignal(double[][] rgbSignals)
    {
        return new double[][] { rgbSignals[1] };
    }

}
