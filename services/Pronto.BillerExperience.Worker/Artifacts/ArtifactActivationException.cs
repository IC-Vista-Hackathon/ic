namespace Pronto.BillerExperience.Worker.Artifacts;

public sealed class ArtifactActivationException(string message, Exception innerException)
    : Exception(message, innerException);
