using XiPHiAS.GridTag.Core;

namespace XiPHiAS.GridTag.Vision;

/// <summary>Produces a car-model observation from a preview.</summary>
public interface ICarModelClassifier
{
    /// <summary>Classifies the car model, or returns null when uncertain.</summary>
    CarModelObservation? Classify(IPreview preview, DetectedCar detectedCar);
}

/// <summary>Reads visible driver-name text from a preview.</summary>
public interface IDriverNameReader
{
    /// <summary>Returns zero or more driver-name observations.</summary>
    IReadOnlyList<DriverNameObservation> ReadNames(IPreview preview, DetectedCar detectedCar);
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
    public IEvidence? Create(IPreview preview, DetectedCar detectedCar)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(detectedCar);
        if (modelClassifier is null && driverNameReader is null)
            return null;
        InvocationCount++;
        var sources = new List<IEvidence>();
        if (modelClassifier?.Classify(preview, detectedCar) is { } model)
        {
            ModelObservationCount++;
            sources.Add(new CarModelEvidence(model));
        }
        if (driverNameReader?.ReadNames(preview, detectedCar) is { Count: > 0 } names)
        {
            NameObservationCount += names.Count;
            sources.Add(new DriverNameEvidence(names));
        }
        return sources.Count == 0 ? null : new CompositeEvidence(sources);
    }

    /// <summary>Number of uncertain detections passed to the factory in this sequential run.</summary>
    public int InvocationCount { get; private set; }

    /// <summary>Number of classifier observations produced.</summary>
    public int ModelObservationCount { get; private set; }

    /// <summary>Number of text observations offered for driver-name validation.</summary>
    public int NameObservationCount { get; private set; }
}
