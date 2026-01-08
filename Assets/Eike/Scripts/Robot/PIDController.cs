using UnityEngine;

namespace Eike.Scripts
{
    public class PIDController
{
    // Controller gains
    private readonly float Kp;
    private readonly float Ki;
    private readonly float Kd;

    // Internal state variables
    private float integralTerm;
    private float lastError;
    private float outputLimit;

    /// <summary>
    /// Initializes a new instance of the PIDController class.
    /// </summary>
    /// <param name="proportionalGain">Proportional gain (Kp).</param>
    /// <param name="integralGain">Integral gain (Ki).</param>
    /// <param name="derivativeGain">Derivative gain (Kd).</param>
    /// <param name="maxOutput">The maximum absolute value the output can have.</param>
    public PIDController(float proportionalGain, float integralGain, float derivativeGain, float maxOutput)
    {
        this.Kp = proportionalGain;
        this.Ki = integralGain;
        this.Kd = derivativeGain;
        this.outputLimit = Mathf.Abs(maxOutput); // Ensure limit is positive
        
        // Reset state upon creation
        Reset();
    }

    /// <summary>
    /// Calculates the control output based on the current process error.
    /// </summary>
    /// <param name="setpoint">The target value.</param>
    /// <param name="processVariable">The current measured value (e.g., current angle).</param>
    /// <param name="deltaTime">The time elapsed since the last calculation.</param>
    /// <returns>The calculated control output (e.g., motor force/torque).</returns>
    public float CalculateOutput(float setpoint, float processVariable, float deltaTime)
    {
        // 1. Calculate the Error
        // float error = setpoint - processVariable;
        float error = Mathf.DeltaAngle(processVariable, setpoint);

        // 2. Proportional Term (P)
        // Drives the system based on the current error.
        float proportionalTerm = Kp * error;

        // 3. Integral Term (I)
        // Accumulates past errors to eliminate steady-state error.
        this.integralTerm += error * deltaTime;
        
        // Anti-windup: Clamp the integral term to prevent excessive accumulation
        // that causes overshoot when the output is saturated.
        float integralTermLimit = outputLimit / Ki;
        this.integralTerm = Mathf.Clamp(this.integralTerm, -integralTermLimit, integralTermLimit);
        
        float integralOutput = Ki * this.integralTerm;

        // 4. Derivative Term (D)
        // Predicts future error and dampens oscillation.
        // It uses the change in error over time.
        float derivativeTerm = 0;
        if (deltaTime > 0)
        {
            derivativeTerm = Kd * ((error - this.lastError) / deltaTime);
        }

        // 5. Calculate Total Output
        float output = proportionalTerm + integralOutput + derivativeTerm;

        // 6. Clamp Output (Saturation)
        // Ensures the output does not exceed physical limits (e.g., maximum motor torque).
        output = Mathf.Clamp(output, -outputLimit, outputLimit);

        // 7. Update for next iteration
        this.lastError = error;

        return output;
    }

    /// <summary>
    /// Resets the internal state of the controller (Integral term and last error).
    /// </summary>
    public void Reset()
    {
        this.integralTerm = 0;
        this.lastError = 0;
    }
}
}