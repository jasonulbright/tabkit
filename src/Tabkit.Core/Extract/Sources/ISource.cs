using System.Data;

namespace Tabkit.Core.Extract.Sources;

/// <summary>Pulls a tabular dataset from somewhere.</summary>
public interface ISource
{
    DataTable Read();
}
