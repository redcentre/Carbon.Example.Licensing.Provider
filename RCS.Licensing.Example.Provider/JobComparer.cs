using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RCS.Licensing.Example.Provider.EFCore;

namespace RCS.Licensing.Example.Provider;

public sealed class JobComparer : IEqualityComparer<Job>
{
	public bool Equals(Job? x, Job? y) => x?.Id == y?.Id;

	public int GetHashCode([DisallowNull] Job obj) => obj.Id.GetHashCode();
}