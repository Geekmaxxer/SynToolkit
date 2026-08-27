#nullable enable

namespace SynToolkit.ViewModels;

/// <summary>Search text supplied when another page links to a specific installer card.</summary>
public sealed record InstallerNavigationRequest(string SearchTerm);
