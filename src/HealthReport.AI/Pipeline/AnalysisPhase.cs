namespace HealthReport.AI.Pipeline;

/// <summary>
/// Fases del pipeline de análisis. El orden define la secuencia de ejecución.
/// </summary>
public enum AnalysisPhase
{
    /// <summary>Perfil demográfico: edad, sexo, altura, peso, IMC.</summary>
    Demographics = 1,

    /// <summary>Actividad física: pasos, energía activa, distancia, ejercicio.</summary>
    PhysicalActivity = 2,

    /// <summary>Salud cardiovascular y sueño: FC, HRV, VO₂, SpO₂, sueño.</summary>
    CardiovascularAndSleep = 3,

    /// <summary>Síntesis final: integra los tres análisis anteriores.</summary>
    FinalSynthesis = 4
}
