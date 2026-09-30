using GridTag.Core;

namespace GridTag.Vision;

/// <summary>Produces a car-model observation from a preview.</summary>
public interface ICarModelClassifier
{
    /// <summary>Classifies the car model, or returns null when uncertain.</summary>
    CarModelObservation? Classify(object preview);
}

/// <summary>Reads visible driver-name text from a preview.</summary>
public interface IDriverNameReader
{
    /// <summary>Returns zero or more driver-name observations.</summary>
    IReadOnlyList<DriverNameObservation> ReadNames(object preview);
}

/// <summary>Builds Core evidence from optional Vision observations.</summary>
public sealed class VisionEvidenceFactory
{
    private readonly ICarModelClassifier? modelClassifier;
    private readonly IDriverNameReader? driverNameReader;

    /// <summary>Creates a factory from optional model and driver readers.</summary>
    public VisionEvidenceFactory(ICarModelClassifier? modelClassifier = null, IDriverNameReader? driverNameReader = null)
    {
        this.modelClassifier = modelClassifier;
        this.driverNameReader = driverNameReader;
    }

    /// <summary>Creates combined evidence; returns null when no source has an observation.</summary>
    public IEvidence? Create(object preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var sources = new List<IEvidence>();
        if (modelClassifier?.Classify(preview) is { } model)
            sources.Add(new CarModelEvidence(model));
        if (driverNameReader?.ReadNames(preview) is { Count: > 0 } names)
            sources.Add(new DriverNameEvidence(names));
        return sources.Count == 0 ? null : new CompositeEvidence(sources);
    }
}
