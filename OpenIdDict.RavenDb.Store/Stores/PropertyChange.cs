namespace Mcrio.OpenIdDict.RavenDb.Store.Stores;

/// <summary>
/// Class that represents a value change.
/// </summary>
/// <typeparam name="T">Value type.</typeparam>
internal class PropertyChange<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyChange{T}"/> class.
    /// </summary>
    /// <param name="oldPropertyValue">Old value.</param>
    /// <param name="newPropertyValue">New Value.</param>
    internal PropertyChange(T oldPropertyValue, T newPropertyValue)
    {
        OldPropertyValue = oldPropertyValue;
        NewPropertyValue = newPropertyValue;
    }

    /// <summary>
    /// Gets the old property value.
    /// </summary>
    internal T OldPropertyValue { get; }

    /// <summary>
    /// Gets the new property value.
    /// </summary>
    internal T NewPropertyValue { get; }
}