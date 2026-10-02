namespace XiPHiAS.GridTag.Core;

/// <summary>Supplies photos to the shared recognition pipeline, independently of their origin.</summary>
public interface IPhotoSource
{
    /// <summary>Loads a stable, ordered photo batch.</summary>
    Manifest Read();
}

/// <summary>Applies pipeline results through a workflow-specific output adapter.</summary>
public interface IResultSink
{
    /// <summary>Writes results and returns the final outcomes, including individual output failures.</summary>
    ResultFile Write(Manifest manifest, ResultFile results);
}
