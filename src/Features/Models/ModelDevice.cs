namespace Rambl.Features.Models;

/// <summary>The processor a model should use when its runtime supports a choice.</summary>
public enum ModelDevice
{
    /// <summary>Lets the runtime prefer the GPU and fall back to the CPU.</summary>
    Automatic,

    /// <summary>Uses the CPU only.</summary>
    Cpu,

    /// <summary>Uses the GPU only, reporting a failure if it cannot be used.</summary>
    Gpu,
}
