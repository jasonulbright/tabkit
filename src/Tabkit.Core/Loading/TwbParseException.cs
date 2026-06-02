using System;

namespace Tabkit.Core.Loading;

/// <summary>Thrown when a .twb / .twbx file cannot be parsed.</summary>
public sealed class TwbParseException : Exception
{
    public TwbParseException(string message) : base(message) { }
    public TwbParseException(string message, Exception inner) : base(message, inner) { }
}
